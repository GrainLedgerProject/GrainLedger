using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261103000200_LedgerLotCodeCutover")]
public partial class LedgerLotCodeCutover : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $migration$
        BEGIN
            IF to_regprocedure('grainledger.ledger_command_legacy(uuid,text,jsonb)') IS NOT NULL THEN
                ALTER FUNCTION grainledger.ledger_command_legacy(uuid,text,jsonb) RENAME TO ledger_command_core;
            END IF;
        END $migration$;

        CREATE OR REPLACE FUNCTION grainledger.ledger_command_pre_mixed(a uuid, action text, p jsonb) RETURNS jsonb
            LANGUAGE plpgsql SECURITY DEFINER
            SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
            AS $$
        DECLARE prior grainledger.ledger_operations; saved grainledger.ledger_operations; result jsonb; item jsonb;
            outputs jsonb:='[]'::jsonb; assigned_code text; reservation grainledger.ledger_lot_code_reservations;
            prior_outputs jsonb; requested_outputs jsonb; original_lot grainledger.ledger_lots;
        BEGIN
            IF action<>'draft' THEN RETURN grainledger.ledger_command_core(a,action,p); END IF;
            PERFORM grainledger.ledger_lock_writes();
            SELECT * INTO prior FROM grainledger.ledger_operations WHERE id=(p->>'id')::uuid;
            IF FOUND AND prior.lot_code_scheme='LEGACY' THEN
                RAISE EXCEPTION 'Legacy drafts cannot be edited; create a new draft with reserved lot codes' USING ERRCODE='23514';
            END IF;
            IF FOUND AND prior.lot_code_scheme='GENERATED' AND NOT p ? 'version' THEN
                SELECT jsonb_agg(value-'code' ORDER BY ordinal) INTO prior_outputs
                FROM jsonb_array_elements(prior.content->'outputs') WITH ORDINALITY AS x(value,ordinal);
                SELECT jsonb_agg(value-'code' ORDER BY ordinal) INTO requested_outputs
                FROM jsonb_array_elements(p->'outputs') WITH ORDINALITY AS x(value,ordinal);
                IF prior.status='DRAFT' AND prior.created_by=a
                   AND jsonb_set(prior.content,'{outputs}',prior_outputs)
                       =jsonb_set(p,'{outputs}',requested_outputs) THEN
                    PERFORM grainledger.ledger_require_staff(a,prior.facility_id,ARRAY['MillOperator']);
                    RETURN grainledger.ledger_operation_json(prior.id)||jsonb_build_object('createdBy',a,'canEdit',true,
                        'canDelete',NOT grainledger.ledger_record_in_use('operations',prior.id));
                END IF;
            END IF;
            -- Core command performs persisted role, owner, input, facility and draft-version checks.
            result:=grainledger.ledger_command_core(a,action,p);
            SELECT * INTO STRICT saved FROM grainledger.ledger_operations WHERE id=(p->>'id')::uuid FOR UPDATE;
            IF EXISTS(SELECT 1 FROM jsonb_array_elements(saved.content->'outputs') x
                GROUP BY (x->>'id')::uuid HAVING count(*)>1) THEN
                RAISE EXCEPTION 'Duplicate output lot identifier' USING ERRCODE='23514';
            END IF;
            IF EXISTS(SELECT 1 FROM jsonb_array_elements(saved.content->'outputs') x
                WHERE x ? 'originalLotId' AND x->>'originalLotId' IS NOT NULL
                GROUP BY (x->>'originalLotId')::uuid HAVING count(*)>1) THEN
                RAISE EXCEPTION 'Original output cannot be replaced twice in one draft' USING ERRCODE='23514';
            END IF;
            FOR item IN SELECT value FROM jsonb_array_elements(saved.content->'outputs') LOOP
                IF nullif(item->>'originalLotId','') IS NOT NULL THEN
                    SELECT * INTO STRICT original_lot FROM grainledger.ledger_lots WHERE id=(item->>'originalLotId')::uuid;
                    IF original_lot.owner_id<>saved.owner_id OR original_lot.stage IS DISTINCT FROM item->>'stage'
                       OR original_lot.product_identity IS DISTINCT FROM item->>'productIdentity'
                       OR original_lot.operation_id IS NULL OR NOT EXISTS(
                           SELECT 1 FROM grainledger.ledger_operations old
                           JOIN grainledger.ledger_corrections c ON c.operation_id=old.id
                           WHERE old.id=original_lot.operation_id AND old.owner_id=saved.owner_id
                             AND old.facility_id=saved.facility_id AND old.kind=saved.kind
                             AND c.status IN ('REQUESTED','RECONCILED')
                       ) THEN
                        RAISE EXCEPTION 'Original output mapping is outside a matching correction' USING ERRCODE='23514';
                    END IF;
                    IF nullif(item->>'code','') IS NOT NULL AND item->>'code'<>original_lot.code THEN
                        RAISE EXCEPTION 'Replacement code must match mapped original output' USING ERRCODE='23514';
                    END IF;
                    outputs:=outputs||jsonb_build_array(item||jsonb_build_object('code',original_lot.code));
                    CONTINUE;
                END IF;
                SELECT * INTO reservation FROM grainledger.ledger_lot_code_reservations WHERE lot_id=(item->>'id')::uuid;
                IF FOUND AND item->>'code' IS NOT NULL AND item->>'code'<>reservation.code THEN
                    RAISE EXCEPTION 'Output code does not match the reserved code' USING ERRCODE='23514';
                END IF;
                IF NOT FOUND AND nullif(item->>'code','') IS NOT NULL THEN
                    RAISE EXCEPTION 'New output code is assigned by the server' USING ERRCODE='23514';
                END IF;
                assigned_code:=grainledger.ledger_reserve_lot_code_internal((item->>'id')::uuid,saved.owner_id,
                    'OPERATION',saved.id,item->>'stage',item->>'productIdentity');
                outputs:=outputs||jsonb_build_array(item||jsonb_build_object('code',assigned_code));
            END LOOP;
            UPDATE grainledger.ledger_operations SET content=jsonb_set(content,'{outputs}',outputs),lot_code_scheme='GENERATED'
            WHERE id=saved.id;
            RETURN result||jsonb_build_object('outputs',outputs);
        END $$;

        CREATE OR REPLACE FUNCTION grainledger.ledger_commit_operation(a uuid, op uuid, corr uuid DEFAULT NULL::uuid) RETURNS jsonb
            LANGUAGE plpgsql SECURITY DEFINER
            SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
            AS $_$
        DECLARE
            operation grainledger.ledger_operations;
            allocation jsonb;
            output jsonb;
            input_lot grainledger.ledger_lots;
            quantity numeric;
            total_input numeric:=0;
            total_output numeric:=0;
            declared_loss numeric;
            variance numeric;
            allowed numeric;
            tolerance numeric;
            first_stage text;
            first_commodity uuid;
            first_product text;
            products text[];
            event_id uuid:=gen_random_uuid();
            policy_id uuid;
            output_facility uuid;
            original_operation uuid;
            resolved_inputs jsonb:='[]'::jsonb;
            actual_id uuid;
            expected_revision bigint;
            selected_pool grainledger.ledger_mixed_bulk_pools;
            actual_allocation jsonb;
            allocated_shares jsonb;
            pool_outputs jsonb:='[]'::jsonb;
            prior_result grainledger.ledger_mixed_bulk_command_results;
        BEGIN
            PERFORM grainledger.ledger_lock_writes();
            PERFORM grainledger.ledger_require_active_operation(op);
            SELECT * INTO STRICT operation FROM grainledger.ledger_operations WHERE id=op FOR UPDATE;
            PERFORM grainledger.ledger_require_staff(a,operation.facility_id,ARRAY['MillOperator']);
            SELECT * INTO prior_result FROM grainledger.ledger_mixed_bulk_command_results cache
            WHERE cache.action='processing' AND cache.command_id=op;
            IF FOUND THEN
                IF prior_result.actor_id IS DISTINCT FROM a
                   OR prior_result.request IS DISTINCT FROM jsonb_build_object('content',operation.content,'correctionId',corr) THEN
                    RAISE EXCEPTION 'Processing retry conflicts with original actor or payload' USING ERRCODE='23514';
                END IF;
                RETURN prior_result.result;
            END IF;
            IF operation.status='COMMITTED' AND corr IS NULL THEN RETURN grainledger.ledger_operation_json(op); END IF;
            IF operation.status<>'DRAFT' THEN
                RAISE EXCEPTION 'Only draft operations can commit' USING ERRCODE='23514';
            END IF;
            IF operation.lot_code_scheme<>'GENERATED' THEN
                RAISE EXCEPTION 'Legacy drafts cannot be committed; create a new draft with reserved lot codes' USING ERRCODE='23514';
            END IF;
            IF corr IS NOT NULL THEN
                SELECT operation_id INTO original_operation FROM grainledger.ledger_corrections
                WHERE id=corr AND status='RECONCILED' AND replacement_operation_id=op;
                IF original_operation IS NULL THEN
                    RAISE EXCEPTION 'Replacement lacks reconciled correction' USING ERRCODE='23514';
                END IF;
            END IF;
            IF operation.occurred_at>now() THEN
                RAISE EXCEPTION 'Occurrence time cannot be in future' USING ERRCODE='23514';
            END IF;
            IF NOT EXISTS (
                SELECT 1 FROM grainledger.ledger_equipment
                WHERE id=operation.equipment_id AND facility_id=operation.facility_id
            ) THEN
                RAISE EXCEPTION 'Equipment outside facility' USING ERRCODE='23514';
            END IF;

            -- Missing, null and empty arrays never become a loss-only operation.
            IF jsonb_typeof(operation.content->'inputs') IS DISTINCT FROM 'array'
               OR jsonb_typeof(operation.content->'outputs') IS DISTINCT FROM 'array' THEN
                RAISE EXCEPTION 'Input/output arrays required' USING ERRCODE='23514';
            END IF;
            IF jsonb_array_length(operation.content->'inputs')=0
               OR jsonb_array_length(operation.content->'outputs')=0 THEN
                RAISE EXCEPTION 'Positive inputs and actual outputs required; loss-only operations unsupported'
                    USING ERRCODE='23514';
            END IF;
            IF EXISTS (
                SELECT 1 FROM jsonb_array_elements(operation.content->'inputs') i
                GROUP BY (i->>'lotId')::uuid,(i->>'locationId')::uuid HAVING count(*)>1
            ) THEN
                RAISE EXCEPTION 'Duplicate input allocation' USING ERRCODE='23514';
            END IF;
            IF EXISTS (
                SELECT 1 FROM jsonb_array_elements(operation.content->'outputs') o
                GROUP BY o->>'code' HAVING count(*)>1
            ) THEN
                RAISE EXCEPTION 'Duplicate output lot code' USING ERRCODE='23514';
            END IF;

            -- One owner, authorized source facility and current eligibility under the posting lock.
            FOR allocation IN
                SELECT value FROM jsonb_array_elements(operation.content->'inputs')
                ORDER BY (value->>'lotId')::uuid,(value->>'locationId')::uuid
            LOOP
                SELECT * INTO STRICT input_lot FROM grainledger.ledger_lots
                WHERE id=(allocation->>'lotId')::uuid FOR UPDATE;
                quantity:=grainledger.ledger_kg(allocation->>'quantityKg');
                IF quantity<=0 THEN
                    RAISE EXCEPTION 'Input must be positive' USING ERRCODE='23514';
                END IF;
                IF input_lot.owner_id<>operation.owner_id OR NOT EXISTS (
                    SELECT 1 FROM grainledger.ledger_locations
                    WHERE id=(allocation->>'locationId')::uuid AND facility_id=operation.facility_id
                ) THEN
                    RAISE EXCEPTION 'Cross-owner or facility input denied' USING ERRCODE='42501';
                END IF;
                expected_revision:=NULL;
                IF (SELECT l.mixed_bulk FROM grainledger.ledger_locations l WHERE l.id=(allocation->>'locationId')::uuid) THEN
                    IF jsonb_typeof(allocation->'poolRevision') IS DISTINCT FROM 'number'
                       OR allocation->>'poolRevision' !~ '^[0-9]+$' THEN
                        RAISE EXCEPTION 'Current pool revision required for mixed-bulk input' USING ERRCODE='23514';
                    END IF;
                    SELECT * INTO STRICT selected_pool FROM grainledger.ledger_mixed_bulk_pools pool
                    WHERE pool.location_id=(allocation->>'locationId')::uuid FOR UPDATE;
                    expected_revision:=(allocation->>'poolRevision')::bigint;
                    IF selected_pool.lot_id IS DISTINCT FROM input_lot.id OR selected_pool.revision<>expected_revision THEN
                        RAISE EXCEPTION 'Mixed-bulk input generation changed; reload and resave draft' USING ERRCODE='40001';
                    END IF;
                END IF;
                IF grainledger.ledger_blocked(input_lot.id,corr) THEN
                    RAISE EXCEPTION 'Input has HOLD, correction or receiving restriction' USING ERRCODE='23514';
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM grainledger.ledger_positions WHERE lot_id=input_lot.id
                      AND location_id=(allocation->>'locationId')::uuid AND quantity_kg>=quantity
                ) THEN
                    RAISE EXCEPTION 'Insufficient stock' USING ERRCODE='23514';
                END IF;
                IF operation.kind='DRYING' AND input_lot.stage<>'RAW_PADDY'
                   OR operation.kind='MILLING' AND input_lot.stage<>'DRIED_PADDY'
                   OR operation.kind IN ('BLENDING','BAGGING')
                      AND input_lot.stage NOT IN ('HEAD_RICE','BROKEN_RICE','BLENDED_RICE') THEN
                    RAISE EXCEPTION 'Undefined stage transition' USING ERRCODE='23514';
                END IF;
                IF first_stage IS NULL THEN
                    first_stage:=input_lot.stage;
                    first_commodity:=input_lot.commodity_id;
                    first_product:=input_lot.product_identity;
                END IF;
                IF operation.kind='MERGE' AND (
                    input_lot.stage<>first_stage OR input_lot.commodity_id<>first_commodity
                    OR input_lot.product_identity<>first_product
                ) THEN
                    RAISE EXCEPTION 'Merge requires same stage, commodity and underlying product identity'
                        USING ERRCODE='23514';
                END IF;
                IF operation.kind IN ('DRYING','MILLING','BAGGING')
                   AND input_lot.commodity_id<>first_commodity THEN
                    RAISE EXCEPTION 'Commodity change requires approved blending' USING ERRCODE='23514';
                END IF;
                IF operation.kind='BAGGING' AND input_lot.product_identity<>first_product THEN
                    RAISE EXCEPTION 'Bagging cannot mix product identities' USING ERRCODE='23514';
                END IF;
                products:=array_append(products,input_lot.commodity_id::text||':'||input_lot.product_identity);
                total_input:=total_input+quantity;
                actual_id:=grainledger.ledger_pool_withdraw(a,op,'PROCESSING',input_lot.id,
                    (allocation->>'locationId')::uuid,quantity,expected_revision);
                resolved_inputs:=resolved_inputs||jsonb_build_array(allocation||jsonb_build_object(
                    'logicalLotId',input_lot.id,'actualLotId',actual_id,'lotId',actual_id));
            END LOOP;

            FOR output IN SELECT value FROM jsonb_array_elements(operation.content->'outputs') LOOP
                IF nullif(output->>'originalLotId','') IS NULL THEN
                    IF NOT EXISTS (
                        SELECT 1 FROM grainledger.ledger_lot_code_reservations r
                        WHERE r.lot_id=(output->>'id')::uuid AND r.code=output->>'code'
                          AND r.owner_id=operation.owner_id AND r.context_kind='OPERATION' AND r.context_id=op
                          AND r.stage=output->>'stage' AND r.product_identity=output->>'productIdentity'
                    ) THEN
                        RAISE EXCEPTION 'Output lot is missing its matching reserved code' USING ERRCODE='23514';
                    END IF;
                ELSIF corr IS NULL OR NOT EXISTS (
                    SELECT 1 FROM grainledger.ledger_lots l
                    WHERE l.id=(output->>'originalLotId')::uuid AND l.operation_id=original_operation
                      AND l.owner_id=operation.owner_id AND l.code=output->>'code'
                      AND l.stage=output->>'stage' AND l.product_identity=output->>'productIdentity'
                ) THEN
                    RAISE EXCEPTION 'Replacement output does not match original lot' USING ERRCODE='23514';
                END IF;
                quantity:=grainledger.ledger_kg(output->>'quantityKg');
                IF quantity<=0 THEN
                    RAISE EXCEPTION 'Output must be positive' USING ERRCODE='23514';
                END IF;
                SELECT facility_id INTO STRICT output_facility FROM grainledger.ledger_locations
                WHERE id=(output->>'locationId')::uuid;
                IF output_facility<>operation.facility_id AND NOT (
                    corr IS NOT NULL AND EXISTS (
                        SELECT 1 FROM grainledger.ledger_corrections c,
                            jsonb_array_elements(c.reconciliation) physical_position
                        WHERE c.id=corr AND (physical_position->>'locationId')::uuid=(output->>'locationId')::uuid
                    )
                ) THEN
                    RAISE EXCEPTION 'Output outside processing facility or reconciled physical locations'
                        USING ERRCODE='42501';
                END IF;
                PERFORM grainledger.ledger_require_staff(a,output_facility,ARRAY['MillOperator']);
                IF output->>'stage' IS NULL
                   OR operation.kind='DRYING' AND output->>'stage'<>'DRIED_PADDY'
                   OR operation.kind='MILLING' AND output->>'stage' NOT IN ('HEAD_RICE','BROKEN_RICE','BRAN','HUSK')
                   OR operation.kind='MERGE' AND output->>'stage'<>first_stage
                   OR operation.kind='BLENDING' AND output->>'stage'<>'BLENDED_RICE'
                   OR operation.kind='BAGGING' AND output->>'stage'<>'BAGGED_RICE' THEN
                    RAISE EXCEPTION 'Invalid output stage' USING ERRCODE='23514';
                END IF;
                IF operation.kind<>'BLENDING' AND (output->>'commodityId')::uuid IS DISTINCT FROM first_commodity THEN
                    RAISE EXCEPTION 'Output commodity mismatch' USING ERRCODE='23514';
                END IF;
                IF operation.kind IN ('MILLING','DRYING')
                   AND output->>'productIdentity' IS DISTINCT FROM output->>'stage' THEN
                    RAISE EXCEPTION 'Output product identity must match produced fraction' USING ERRCODE='23514';
                END IF;
                IF operation.kind IN ('BAGGING','MERGE')
                   AND output->>'productIdentity' IS DISTINCT FROM first_product THEN
                    RAISE EXCEPTION 'Underlying product identity must be retained' USING ERRCODE='23514';
                END IF;
                IF operation.kind='BLENDING' AND (
                    output->>'productIdentity' IS DISTINCT FROM 'BLENDED_RICE' OR NOT EXISTS (
                        SELECT 1 FROM grainledger.ledger_compatibilities c
                        WHERE c.facility_id=operation.facility_id
                          AND c.output_commodity_id=(output->>'commodityId')::uuid
                          AND c.effective_from<=operation.occurred_at
                          AND c.input_products @> products AND c.input_products <@ products
                    )
                ) THEN
                    RAISE EXCEPTION 'No approved blend compatibility' USING ERRCODE='23514';
                END IF;
                total_output:=total_output+quantity;
            END LOOP;

            -- Threshold floors sub-micro-kg tails; six-decimal variance comparison remains exact.
            declared_loss:=grainledger.ledger_kg(operation.content->>'declaredLossKg');
            IF declared_loss<0 THEN
                RAISE EXCEPTION 'Declared loss cannot be negative' USING ERRCODE='23514';
            END IF;
            SELECT p.id,p.tolerance_percent INTO policy_id,tolerance FROM grainledger.ledger_policies p
            WHERE p.facility_id=operation.facility_id AND p.kind=operation.kind
              AND p.effective_from<=operation.occurred_at
            ORDER BY p.effective_from DESC LIMIT 1;
            IF policy_id IS NULL THEN
                RAISE EXCEPTION 'No applicable confirmed balance policy' USING ERRCODE='23514';
            END IF;
            variance:=total_input-total_output-declared_loss;
            allowed:=floor(total_input*tolerance*10000)/1000000;
            IF abs(variance)>allowed THEN
                RAISE EXCEPTION 'Mass variance exceeds confirmed tolerance' USING ERRCODE='23514';
            END IF;
            IF variance<>0 AND (
                operation.confirmed_version IS DISTINCT FROM operation.version
                OR grainledger.ledger_role(operation.confirmed_by) IS DISTINCT FROM 'MillManager'
                OR NOT grainledger.ledger_assigned(operation.confirmed_by,operation.facility_id)
            ) THEN
                RAISE EXCEPTION 'Nonzero variance requires current manager confirmation' USING ERRCODE='23514';
            END IF;
            INSERT INTO grainledger.ledger_events(id,kind,actor_id,operation_id,details,occurred_at)
            VALUES(event_id,operation.kind,a,op,jsonb_build_object(
                'policyId',policy_id,'declaredLossKg',declared_loss::text,
                'varianceKg',variance::text,'allowedVarianceKg',allowed::text,'content',operation.content,
                'resolvedInputs',resolved_inputs
            ),operation.occurred_at);
            FOR allocation IN SELECT value FROM jsonb_array_elements(resolved_inputs) LOOP
                PERFORM grainledger.ledger_post(event_id,(allocation->>'lotId')::uuid,
                                    (allocation->>'locationId')::uuid,-grainledger.ledger_kg(allocation->>'quantityKg'));
            END LOOP;
            FOR output IN SELECT value FROM jsonb_array_elements(operation.content->'outputs') LOOP
                PERFORM grainledger.ledger_credit_location((output->>'locationId')::uuid,operation.owner_id);
                INSERT INTO grainledger.ledger_lots(id,code,owner_id,commodity_id,stage,product_identity,operation_id,code_origin_id)
                VALUES((output->>'id')::uuid,output->>'code',operation.owner_id,
                       (output->>'commodityId')::uuid,output->>'stage',output->>'productIdentity',op,
                       grainledger.ledger_register_code(output->>'code',(output->>'id')::uuid,NULL,original_operation));
                quantity:=grainledger.ledger_kg(output->>'quantityKg');
                PERFORM grainledger.ledger_post(event_id,(output->>'id')::uuid,(output->>'locationId')::uuid,quantity);

                -- Exact integer micro-kg: output -> input (ties input ID), input -> source (ties source ID).
                WITH input_weights AS (
                    SELECT (i->>'lotId')::uuid input_id,sum(grainledger.ledger_kg(i->>'quantityKg')*1000000) mass
                    FROM jsonb_array_elements(resolved_inputs) i GROUP BY (i->>'lotId')::uuid
                ), input_bases AS (
                    SELECT input_id,div(quantity*1000000*mass,total_input*1000000) base,
                           mod(quantity*1000000*mass,total_input*1000000) remainder FROM input_weights
                ), input_ranked AS (
                    SELECT *,row_number() OVER(ORDER BY remainder DESC,input_id) n,
                           quantity*1000000-sum(base) OVER() remaining FROM input_bases
                ), input_shares AS (
                    SELECT input_id,base+CASE WHEN n<=remaining THEN 1 ELSE 0 END units FROM input_ranked
                ), source_weights AS (
                    SELECT s.input_id,s.units,c.source_lot_id,c.quantity_kg*1000000 mass,
                           sum(c.quantity_kg*1000000) OVER(PARTITION BY s.input_id) total
                    FROM input_shares s JOIN grainledger.ledger_contributions c ON c.lot_id=s.input_id
                ), source_bases AS (
                    SELECT input_id,units,source_lot_id,div(units*mass,total) base,
                           mod(units*mass,total) remainder FROM source_weights
                ), source_ranked AS (
                    SELECT *,row_number() OVER(PARTITION BY input_id ORDER BY remainder DESC,source_lot_id) n,
                           units-sum(base) OVER(PARTITION BY input_id) remaining FROM source_bases
                )
                SELECT COALESCE(jsonb_agg(jsonb_build_object('inputLotId',input_id,'sourceLotId',source_lot_id,
                    'quantityKg',(base+CASE WHEN n<=remaining THEN 1 ELSE 0 END)/1000000)), '[]'::jsonb)
                INTO allocated_shares FROM source_ranked;
                -- Ordinary contribution history retains zero-rounded roots; custody ancestry is positive only.
                INSERT INTO grainledger.ledger_contributions(lot_id,source_lot_id,quantity_kg)
                SELECT (output->>'id')::uuid,(share->>'sourceLotId')::uuid,sum((share->>'quantityKg')::numeric)
                FROM jsonb_array_elements(allocated_shares) share GROUP BY (share->>'sourceLotId')::uuid;
                -- Persist actual allocations before custody exists too; later ingress must retain intermediate ancestry.
                INSERT INTO grainledger.ledger_custody_processing_allocations(event_id,operation_id,input_lot_id,output_lot_id,source_lot_id,quantity_kg)
                SELECT event_id,op,(share->>'inputLotId')::uuid,(output->>'id')::uuid,
                    (share->>'sourceLotId')::uuid,(share->>'quantityKg')::numeric
                FROM jsonb_array_elements(allocated_shares) share WHERE (share->>'quantityKg')::numeric>0;
                IF (SELECT sum(quantity_kg) FROM grainledger.ledger_contributions WHERE lot_id=(output->>'id')::uuid)
                   IS DISTINCT FROM quantity THEN
                    RAISE EXCEPTION 'Lineage does not conserve output mass' USING ERRCODE='23514';
                END IF;
                actual_id:=grainledger.ledger_pool_ingress(a,op,'PROCESSING_OUTPUT',(output->>'locationId')::uuid,
                    (output->>'id')::uuid,quantity);
            END LOOP;
            SELECT COALESCE(jsonb_agg(jsonb_build_object('outputLotId',o.value->>'id',
                'locationId',o.value->>'locationId','currentPoolLotId',pool.lot_id) ORDER BY o.ordinal),'[]'::jsonb)
            INTO pool_outputs FROM jsonb_array_elements(operation.content->'outputs') WITH ORDINALITY o(value,ordinal)
            JOIN grainledger.ledger_locations loc ON loc.id=(o.value->>'locationId')::uuid AND loc.mixed_bulk
            JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.location_id=loc.id;
            UPDATE grainledger.ledger_operations
            SET status='COMMITTED',variance_kg=variance,allowed_variance_kg=allowed,correction_id=corr
            WHERE id=op;
            actual_allocation:=grainledger.ledger_operation_json(op)||jsonb_build_object('resolvedInputs',resolved_inputs,'poolOutputs',pool_outputs);
            INSERT INTO grainledger.ledger_mixed_bulk_command_results(action,command_id,actor_id,request,result)
            VALUES('processing',op,a,jsonb_build_object('content',operation.content,'correctionId',corr),actual_allocation);
            RETURN actual_allocation;
        END $_$;

        CREATE OR REPLACE FUNCTION grainledger.ledger_source_lot_code(r uuid, v_lot uuid) RETURNS text
            LANGUAGE plpgsql STABLE
            SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
            AS $$
        DECLARE reserved text;
        BEGIN
            SELECT code INTO reserved FROM grainledger.ledger_lot_code_reservations
            WHERE lot_id=v_lot AND context_kind='SOURCE' AND context_id=r
              AND stage='RAW_PADDY' AND product_identity='RAW_PADDY';
            IF reserved IS NOT NULL THEN RETURN reserved; END IF;
            RAISE EXCEPTION 'Source lot is missing its reserved code' USING ERRCODE='23514';
        END $$;
        CREATE OR REPLACE FUNCTION grainledger.ledger_legacy_procurement_package_complete(r uuid) RETURNS boolean
            LANGUAGE plpgsql SECURITY DEFINER
            SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
            AS $$
        DECLARE
            h grainledger.capture_procurements;
            ev grainledger.capture_business_events;
            snapshot jsonb;
            row record;
            item_snapshot jsonb;
            commodity uuid;
            location uuid;
            count_items integer;
        BEGIN
            SELECT * INTO h FROM grainledger.capture_procurements WHERE id=r AND status='COMMITTED';
            IF NOT FOUND OR h.event_id IS NULL THEN RETURN false; END IF;
            SELECT * INTO ev FROM grainledger.capture_business_events WHERE id=h.event_id;
            IF NOT FOUND OR ev.company_id IS DISTINCT FROM h.company_id
               OR ev.actor_id IS DISTINCT FROM h.received_by OR ev.event_type<>'PROCUREMENT_COMMITTED'
               OR ev.occurred_at<>h.received_at THEN RETURN false; END IF;
            snapshot:=ev.payload->'result';
            commodity:=(snapshot->>'CommodityId')::uuid;
            location:=(snapshot->>'ReceivingLocationId')::uuid;
            IF snapshot IS NULL OR (snapshot->>'Id')::uuid IS DISTINCT FROM h.id
               OR snapshot->>'Code' IS DISTINCT FROM h.code
               OR (snapshot->>'FarmerId')::uuid IS DISTINCT FROM h.farmer_id
               OR (snapshot->>'CollectionSiteId')::uuid IS DISTINCT FROM h.collection_site_id
               OR (snapshot->>'ReceivedBy')::uuid IS DISTINCT FROM h.received_by
               OR (snapshot->>'EventId')::uuid IS DISTINCT FROM ev.id
               OR (snapshot->>'ReceivedAt')::timestamptz IS DISTINCT FROM h.received_at
               OR (snapshot->>'RecordedAt')::timestamptz IS DISTINCT FROM ev.recorded_at
               OR snapshot->>'Status' IS DISTINCT FROM 'COMMITTED'
               OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_commodities WHERE id=commodity AND code='PADDY')
               OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_locations WHERE id=location AND facility_id=h.collection_site_id)
               OR NOT EXISTS(SELECT 1 FROM grainledger.capture_offline_operations o WHERE o.applied_event_id=ev.id
                   AND o.company_id=h.company_id AND o.submitted_by=h.received_by AND o.status='APPLIED'
                   AND o.client_operation_id=ev.correlation_id AND o.result_payload=snapshot
                   AND o.captured_at=(snapshot->>'CapturedAt')::timestamptz
                   AND o.payload->>'operationType'='PROCUREMENT_COMMITTED') THEN RETURN false; END IF;
            SELECT count(*) INTO count_items FROM grainledger.capture_procurement_items WHERE procurement_id=r;
            IF count_items NOT BETWEEN 1 AND 100 OR jsonb_typeof(snapshot->'Items') IS DISTINCT FROM 'array'
               OR jsonb_array_length(snapshot->'Items')<>count_items
               OR (SELECT count(*) FROM grainledger.ledger_lots WHERE receipt_id=r)<>count_items
               OR (snapshot->>'TotalNetMassKg')::numeric IS DISTINCT FROM
                  (SELECT sum(net_mass_kg) FROM grainledger.capture_procurement_items WHERE procurement_id=r)
               OR (SELECT count(DISTINCT (x->>'Id')::uuid) FROM jsonb_array_elements(snapshot->'Items') x)<>count_items
               THEN RETURN false; END IF;
            FOR row IN
                SELECT i.*,sl.id source_id,sl.lot_id,ps.plot_id,ps.season_id,ps.rice_variety,p.farmer_id
                FROM grainledger.capture_procurement_items i
                LEFT JOIN grainledger.capture_source_lots sl ON sl.procurement_item_id=i.id AND sl.company_id=i.company_id
                JOIN grainledger.capture_plot_seasons ps ON ps.id=i.plot_season_id AND ps.company_id=i.company_id
                JOIN grainledger.capture_plots p ON p.id=ps.plot_id AND p.company_id=ps.company_id
                JOIN grainledger.capture_crop_seasons s ON s.id=ps.season_id AND s.company_id=ps.company_id
                WHERE i.procurement_id=r
            LOOP
                SELECT x INTO item_snapshot FROM jsonb_array_elements(snapshot->'Items') x WHERE (x->>'Id')::uuid=row.id;
                IF row.company_id<>h.company_id OR row.farmer_id<>h.farmer_id OR row.source_id IS NULL
                   OR row.line_no NOT BETWEEN 1 AND count_items
                   OR (item_snapshot->>'LineNo')::integer IS DISTINCT FROM row.line_no
                   OR (item_snapshot->>'PlotSeasonId')::uuid IS DISTINCT FROM row.plot_season_id
                   OR (item_snapshot->>'PlotId')::uuid IS DISTINCT FROM row.plot_id
                   OR (item_snapshot->>'SeasonId')::uuid IS DISTINCT FROM row.season_id
                   OR item_snapshot->>'RiceVariety' IS DISTINCT FROM row.rice_variety
                   OR (item_snapshot->>'SourceLotId')::uuid IS DISTINCT FROM row.source_id
                   OR (item_snapshot->>'LotId')::uuid IS DISTINCT FROM row.lot_id
                   OR item_snapshot->>'Stage' IS DISTINCT FROM 'RAW_PADDY'
                   OR (item_snapshot->>'MeasuredValue')::numeric IS DISTINCT FROM row.measured_value
                   OR item_snapshot->>'MeasuredUnit' IS DISTINCT FROM row.measured_unit
                   OR (item_snapshot->>'NetMassKg')::numeric IS DISTINCT FROM row.net_mass_kg
                   OR (item_snapshot->>'MoisturePercent')::numeric IS DISTINCT FROM row.moisture_percent
                   OR (item_snapshot->'InitialStock'->>'LocationId')::uuid IS DISTINCT FROM location
                   OR (item_snapshot->'InitialStock'->>'ReceivedKg')::numeric IS DISTINCT FROM row.net_mass_kg
                   OR row.net_mass_kg IS DISTINCT FROM (CASE row.measured_unit
                       WHEN 'KG' THEN row.measured_value WHEN 'G' THEN row.measured_value/1000
                       WHEN 'T' THEN row.measured_value*1000 END)
                   OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_measurement_units WHERE code=row.measured_unit AND quantity_kind='MASS')
                   OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_lots l JOIN grainledger.ledger_catalog_commodities m ON m.ledger_commodity_id=l.commodity_id
                       WHERE l.id=row.lot_id AND l.owner_id=h.company_id AND l.receipt_id=r AND l.receipt_line_id=row.id
                       AND l.code=item_snapshot->>'LotCode' AND l.stage='RAW_PADDY' AND l.product_identity='RAW_PADDY'
                       AND m.catalog_commodity_id=commodity)
                   OR (SELECT count(*) FROM grainledger.ledger_events e WHERE e.lot_id=row.lot_id AND e.kind='RECEIPT')<>1
                   OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_events e JOIN grainledger.ledger_postings pp ON pp.event_id=e.id
                       WHERE e.lot_id=row.lot_id AND e.kind='RECEIPT' AND e.actor_id=h.received_by AND e.occurred_at=h.received_at
                       AND e.details->>'receiptId'=r::text AND e.details->>'receiptLineId'=row.id::text
                       AND e.details->>'captureEventId'=ev.id::text AND e.details->>'sourceLotId'=row.source_id::text
                       AND e.details->>'catalogCommodityId'=commodity::text
                       AND e.details->>'farmerId'=h.farmer_id::text AND e.details->>'plotSeasonId'=row.plot_season_id::text
                       AND e.details->>'plotId'=row.plot_id::text AND e.details->>'seasonId'=row.season_id::text
                       AND e.details->>'riceVariety'=row.rice_variety
                       AND (e.details->>'originalQuantity')::numeric=row.measured_value AND e.details->>'originalUnit'=row.measured_unit
                       AND (e.details->>'quantityKg')::numeric=row.net_mass_kg
                       AND (e.details->>'moisturePercent')::numeric=row.moisture_percent
                       AND pp.lot_id=row.lot_id AND pp.location_id=location AND pp.quantity_kg=row.net_mass_kg
                       AND pp.reverses_posting_id IS NULL
                       AND (SELECT count(*) FROM grainledger.ledger_postings WHERE event_id=e.id)=1)
                   OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_contributions WHERE lot_id=row.lot_id AND source_lot_id=row.lot_id AND quantity_kg=row.net_mass_kg)
                   OR (SELECT count(*) FROM grainledger.ledger_contributions WHERE lot_id=row.lot_id)<>1 THEN RETURN false; END IF;
            END LOOP;
            RETURN true;
        END $$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Lot-code cutover cannot be rolled back safely.");
}
