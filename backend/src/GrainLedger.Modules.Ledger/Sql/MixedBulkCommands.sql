-- Preserve generated-code draft handling and every unchanged historical command.
ALTER FUNCTION ledger.command(uuid,text,jsonb) RENAME TO command_pre_mixed;
REVOKE ALL ON FUNCTION ledger.command_pre_mixed(uuid,text,jsonb) FROM PUBLIC;
DO $$
DECLARE granted record;
BEGIN
    FOR granted IN SELECT DISTINCT role.rolname FROM pg_proc p
        CROSS JOIN LATERAL aclexplode(p.proacl) acl JOIN pg_roles role ON role.oid=acl.grantee
        WHERE p.oid='ledger.command_pre_mixed(uuid,text,jsonb)'::regprocedure
          AND acl.privilege_type='EXECUTE' AND acl.grantee<>p.proowner
    LOOP
        EXECUTE format('REVOKE EXECUTE ON FUNCTION ledger.command_pre_mixed(uuid,text,jsonb) FROM %I',granted.rolname);
    END LOOP;
END $$;

-- Final command responses are immutable too: retry never repeats a split/merge.
CREATE TABLE ledger.mixed_bulk_command_results (
    action text NOT NULL,
    command_id uuid NOT NULL,
    actor_id uuid NOT NULL REFERENCES identity.users("Id"),
    request jsonb NOT NULL,
    result jsonb NOT NULL,
    PRIMARY KEY(action,command_id)
);
CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON ledger.mixed_bulk_command_results
    FOR EACH ROW EXECUTE FUNCTION ledger.immutable();
REVOKE ALL ON ledger.mixed_bulk_command_results FROM PUBLIC;

CREATE OR REPLACE FUNCTION ledger.accept_arrival(r uuid) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
DECLARE h capture.procurements; s ledger.receiving; ms ledger.arrival_measurement_sets; snap jsonb; commodity uuid; c uuid; loc uuid; row record; event_id uuid; review ledger.arrival_reviews; code text;
BEGIN
    PERFORM ledger.lock_writes();
    SELECT * INTO STRICT h FROM capture.procurements WHERE id=r;
    SELECT * INTO STRICT s FROM ledger.receiving WHERE procurement_id=r AND accepted_measurement_set_id IS NOT NULL;
    SELECT * INTO STRICT ms FROM ledger.arrival_measurement_sets WHERE id=s.accepted_measurement_set_id;
    SELECT * INTO review FROM ledger.arrival_reviews WHERE id=s.accepted_review_id;
    SELECT payload->'result' INTO snap FROM capture.business_events WHERE id=h.event_id;
    c:=(snap->>'CommodityId')::uuid; loc:=(snap->>'ReceivingLocationId')::uuid;
    PERFORM ledger.require_active_location(loc);
    IF EXISTS(SELECT 1 FROM ledger.lots WHERE receipt_id=r) THEN RAISE EXCEPTION 'Arrival already posted' USING ERRCODE='23514'; END IF;
    IF NOT EXISTS(SELECT 1 FROM ledger.locations WHERE id=loc AND facility_id=h.collection_site_id AND kind IN ('STORE','SILO','COMPARTMENT') AND (owner_id=h.company_id OR owner_id IS NULL AND (kind<>'COMPARTMENT' OR cleared))) THEN RAISE EXCEPTION 'Receiving location no longer eligible' USING ERRCODE='23514'; END IF;
    SELECT ledger_commodity_id INTO commodity FROM ledger.catalog_commodities WHERE catalog_commodity_id=c;
    IF NOT FOUND THEN PERFORM ledger.provision_catalog_commodity(c); SELECT ledger_commodity_id INTO STRICT commodity FROM ledger.catalog_commodities WHERE catalog_commodity_id=c; END IF;
    PERFORM ledger.credit_location(loc,h.company_id);
    FOR row IN SELECT i.*,sl.id source_id,sl.lot_id,ps.plot_id,ps.season_id,ps.rice_variety,m.actual_kg,m.measured_value actual_value,m.measured_unit actual_unit
        FROM capture.procurement_items i JOIN capture.source_lots sl ON sl.procurement_item_id=i.id JOIN capture.plot_seasons ps ON ps.id=i.plot_season_id
        JOIN ledger.arrival_measurements m ON m.procurement_item_id=i.id AND m.measurement_set_id=ms.id WHERE i.procurement_id=r ORDER BY i.line_no
    LOOP
        code:=ledger.source_lot_code(r,row.lot_id);
        INSERT INTO ledger.lots(id,code,owner_id,commodity_id,stage,product_identity,receipt_id,receipt_line_id,code_origin_id)
        VALUES(row.lot_id,code,h.company_id,commodity,'RAW_PADDY','RAW_PADDY',r,row.id,ledger.register_code(code,row.lot_id,NULL,NULL));
        event_id:=gen_random_uuid();
        INSERT INTO ledger.events(id,kind,actor_id,lot_id,details,occurred_at) VALUES(event_id,'RECEIPT',ms.operator_id,row.lot_id,
            jsonb_build_object('receiptId',r,'receiptLineId',row.id,'captureEventId',h.event_id,'sourceLotId',row.source_id,'catalogCommodityId',c,
                'farmerId',h.farmer_id,'plotSeasonId',row.plot_season_id,'plotId',row.plot_id,'seasonId',row.season_id,'riceVariety',row.rice_variety,
                'originalQuantity',row.measured_value::text,'originalUnit',row.measured_unit,'sourceQuantityKg',row.net_mass_kg::text,
                'arrivalQuantity',row.actual_value::text,'arrivalUnit',row.actual_unit,'quantityKg',row.actual_kg::text,'moisturePercent',row.moisture_percent::text,
                'measurementSetId',ms.id,'managerReviewId',review.id,'managerId',review.manager_id,'evidenceId',review.evidence_id,'receivingStatus',s.status),ms.captured_at);
        PERFORM ledger.post(event_id,row.lot_id,loc,row.actual_kg);
        INSERT INTO ledger.contributions VALUES(row.lot_id,row.lot_id,row.actual_kg);
        PERFORM ledger.pool_ingress(COALESCE(review.manager_id,ms.operator_id),COALESCE(review.operation_id,ms.operation_id),
            'ARRIVAL',loc,row.lot_id,row.actual_kg);
    END LOOP;
END $$;

CREATE OR REPLACE FUNCTION ledger.commit_operation(a uuid,op uuid,corr uuid DEFAULT NULL) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE
    operation ledger.operations;
    allocation jsonb;
    output jsonb;
    input_lot ledger.lots;
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
    selected_pool ledger.mixed_bulk_pools;
    actual_allocation jsonb;
    allocated_shares jsonb;
    pool_outputs jsonb:='[]'::jsonb;
    prior_result ledger.mixed_bulk_command_results;
BEGIN
    PERFORM ledger.lock_writes();
    PERFORM ledger.require_active_operation(op);
    SELECT * INTO STRICT operation FROM ledger.operations WHERE id=op FOR UPDATE;
    PERFORM ledger.require_staff(a,operation.facility_id,ARRAY['MillOperator']);
    SELECT * INTO prior_result FROM ledger.mixed_bulk_command_results cache
    WHERE cache.action='processing' AND cache.command_id=op;
    IF FOUND THEN
        IF prior_result.actor_id IS DISTINCT FROM a
           OR prior_result.request IS DISTINCT FROM jsonb_build_object('content',operation.content,'correctionId',corr) THEN
            RAISE EXCEPTION 'Processing retry conflicts with original actor or payload' USING ERRCODE='23514';
        END IF;
        RETURN prior_result.result;
    END IF;
    IF operation.status='COMMITTED' AND corr IS NULL THEN RETURN ledger.operation_json(op); END IF;
    IF operation.status<>'DRAFT' THEN
        RAISE EXCEPTION 'Only draft operations can commit' USING ERRCODE='23514';
    END IF;
    IF corr IS NOT NULL THEN
        SELECT operation_id INTO original_operation FROM ledger.corrections
        WHERE id=corr AND status='RECONCILED' AND replacement_operation_id=op;
        IF original_operation IS NULL THEN
            RAISE EXCEPTION 'Replacement lacks reconciled correction' USING ERRCODE='23514';
        END IF;
    END IF;
    IF operation.occurred_at>now() THEN
        RAISE EXCEPTION 'Occurrence time cannot be in future' USING ERRCODE='23514';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM ledger.equipment
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
        SELECT * INTO STRICT input_lot FROM ledger.lots
        WHERE id=(allocation->>'lotId')::uuid FOR UPDATE;
        quantity:=ledger.kg(allocation->>'quantityKg');
        IF quantity<=0 THEN
            RAISE EXCEPTION 'Input must be positive' USING ERRCODE='23514';
        END IF;
        IF input_lot.owner_id<>operation.owner_id OR NOT EXISTS (
            SELECT 1 FROM ledger.locations
            WHERE id=(allocation->>'locationId')::uuid AND facility_id=operation.facility_id
        ) THEN
            RAISE EXCEPTION 'Cross-owner or facility input denied' USING ERRCODE='42501';
        END IF;
        expected_revision:=NULL;
        IF (SELECT l.mixed_bulk FROM ledger.locations l WHERE l.id=(allocation->>'locationId')::uuid) THEN
            IF jsonb_typeof(allocation->'poolRevision') IS DISTINCT FROM 'number'
               OR allocation->>'poolRevision' !~ '^[0-9]+$' THEN
                RAISE EXCEPTION 'Current pool revision required for mixed-bulk input' USING ERRCODE='23514';
            END IF;
            SELECT * INTO STRICT selected_pool FROM ledger.mixed_bulk_pools pool
            WHERE pool.location_id=(allocation->>'locationId')::uuid FOR UPDATE;
            expected_revision:=(allocation->>'poolRevision')::bigint;
            IF selected_pool.lot_id IS DISTINCT FROM input_lot.id OR selected_pool.revision<>expected_revision THEN
                RAISE EXCEPTION 'Mixed-bulk input generation changed; reload and resave draft' USING ERRCODE='40001';
            END IF;
        END IF;
        IF ledger.blocked(input_lot.id,corr) THEN
            RAISE EXCEPTION 'Input has HOLD, correction or receiving restriction' USING ERRCODE='23514';
        END IF;
        IF NOT EXISTS (
            SELECT 1 FROM ledger.positions WHERE lot_id=input_lot.id
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
        actual_id:=ledger.pool_withdraw(a,op,'PROCESSING',input_lot.id,
            (allocation->>'locationId')::uuid,quantity,expected_revision);
        resolved_inputs:=resolved_inputs||jsonb_build_array(allocation||jsonb_build_object(
            'logicalLotId',input_lot.id,'actualLotId',actual_id,'lotId',actual_id));
    END LOOP;

    FOR output IN SELECT value FROM jsonb_array_elements(operation.content->'outputs') LOOP
        quantity:=ledger.kg(output->>'quantityKg');
        IF quantity<=0 THEN
            RAISE EXCEPTION 'Output must be positive' USING ERRCODE='23514';
        END IF;
        SELECT facility_id INTO STRICT output_facility FROM ledger.locations
        WHERE id=(output->>'locationId')::uuid;
        IF output_facility<>operation.facility_id AND NOT (
            corr IS NOT NULL AND EXISTS (
                SELECT 1 FROM ledger.corrections c,
                    jsonb_array_elements(c.reconciliation) physical_position
                WHERE c.id=corr AND (physical_position->>'locationId')::uuid=(output->>'locationId')::uuid
            )
        ) THEN
            RAISE EXCEPTION 'Output outside processing facility or reconciled physical locations'
                USING ERRCODE='42501';
        END IF;
        PERFORM ledger.require_staff(a,output_facility,ARRAY['MillOperator']);
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
                SELECT 1 FROM ledger.compatibilities c
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
    declared_loss:=ledger.kg(operation.content->>'declaredLossKg');
    IF declared_loss<0 THEN
        RAISE EXCEPTION 'Declared loss cannot be negative' USING ERRCODE='23514';
    END IF;
    SELECT p.id,p.tolerance_percent INTO policy_id,tolerance FROM ledger.policies p
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
        OR ledger.role(operation.confirmed_by) IS DISTINCT FROM 'MillManager'
        OR NOT ledger.assigned(operation.confirmed_by,operation.facility_id)
    ) THEN
        RAISE EXCEPTION 'Nonzero variance requires current manager confirmation' USING ERRCODE='23514';
    END IF;
    INSERT INTO ledger.events(id,kind,actor_id,operation_id,details,occurred_at)
    VALUES(event_id,operation.kind,a,op,jsonb_build_object(
        'policyId',policy_id,'declaredLossKg',declared_loss::text,
        'varianceKg',variance::text,'allowedVarianceKg',allowed::text,'content',operation.content,
        'resolvedInputs',resolved_inputs
    ),operation.occurred_at);
    FOR allocation IN SELECT value FROM jsonb_array_elements(resolved_inputs) LOOP
        PERFORM ledger.post(event_id,(allocation->>'lotId')::uuid,
                            (allocation->>'locationId')::uuid,-ledger.kg(allocation->>'quantityKg'));
    END LOOP;
    FOR output IN SELECT value FROM jsonb_array_elements(operation.content->'outputs') LOOP
        PERFORM ledger.credit_location((output->>'locationId')::uuid,operation.owner_id);
        INSERT INTO ledger.lots(id,code,owner_id,commodity_id,stage,product_identity,operation_id,code_origin_id)
        VALUES((output->>'id')::uuid,output->>'code',operation.owner_id,
               (output->>'commodityId')::uuid,output->>'stage',output->>'productIdentity',op,
               ledger.register_code(output->>'code',(output->>'id')::uuid,NULL,original_operation));
        quantity:=ledger.kg(output->>'quantityKg');
        PERFORM ledger.post(event_id,(output->>'id')::uuid,(output->>'locationId')::uuid,quantity);

        -- Exact integer micro-kg: output -> input (ties input ID), input -> source (ties source ID).
        WITH input_weights AS (
            SELECT (i->>'lotId')::uuid input_id,sum(ledger.kg(i->>'quantityKg')*1000000) mass
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
            FROM input_shares s JOIN ledger.contributions c ON c.lot_id=s.input_id
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
        INSERT INTO ledger.contributions(lot_id,source_lot_id,quantity_kg)
        SELECT (output->>'id')::uuid,(share->>'sourceLotId')::uuid,sum((share->>'quantityKg')::numeric)
        FROM jsonb_array_elements(allocated_shares) share GROUP BY (share->>'sourceLotId')::uuid;
        -- Persist actual allocations before custody exists too; later ingress must retain intermediate ancestry.
        INSERT INTO ledger.custody_processing_allocations(event_id,operation_id,input_lot_id,output_lot_id,source_lot_id,quantity_kg)
        SELECT event_id,op,(share->>'inputLotId')::uuid,(output->>'id')::uuid,
            (share->>'sourceLotId')::uuid,(share->>'quantityKg')::numeric
        FROM jsonb_array_elements(allocated_shares) share WHERE (share->>'quantityKg')::numeric>0;
        IF (SELECT sum(quantity_kg) FROM ledger.contributions WHERE lot_id=(output->>'id')::uuid)
           IS DISTINCT FROM quantity THEN
            RAISE EXCEPTION 'Lineage does not conserve output mass' USING ERRCODE='23514';
        END IF;
        actual_id:=ledger.pool_ingress(a,op,'PROCESSING_OUTPUT',(output->>'locationId')::uuid,
            (output->>'id')::uuid,quantity);
    END LOOP;
    SELECT COALESCE(jsonb_agg(jsonb_build_object('outputLotId',o.value->>'id',
        'locationId',o.value->>'locationId','currentPoolLotId',pool.lot_id) ORDER BY o.ordinal),'[]'::jsonb)
    INTO pool_outputs FROM jsonb_array_elements(operation.content->'outputs') WITH ORDINALITY o(value,ordinal)
    JOIN ledger.locations loc ON loc.id=(o.value->>'locationId')::uuid AND loc.mixed_bulk
    JOIN ledger.mixed_bulk_pools pool ON pool.location_id=loc.id;
    UPDATE ledger.operations
    SET status='COMMITTED',variance_kg=variance,allowed_variance_kg=allowed,correction_id=corr
    WHERE id=op;
    actual_allocation:=ledger.operation_json(op)||jsonb_build_object('resolvedInputs',resolved_inputs,'poolOutputs',pool_outputs);
    INSERT INTO ledger.mixed_bulk_command_results(action,command_id,actor_id,request,result)
    VALUES('processing',op,a,jsonb_build_object('content',operation.content,'correctionId',corr),actual_allocation);
    RETURN actual_allocation;
END $$;

CREATE OR REPLACE FUNCTION ledger.command(a uuid,action text,p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
<<mixed_command>>
DECLARE
    result jsonb; item jsonb; saved ledger.operations; location ledger.locations; source ledger.lots;
    source_actual uuid; destination uuid; source_location uuid; facility uuid; current_pool uuid;
    command_id uuid; quantity numeric; expected_revision bigint; selected_pool ledger.mixed_bulk_pools;
    prior ledger.events; cache ledger.mixed_bulk_command_results; cache_action text;
    hold ledger.holds; affected_facility uuid; source_mixed boolean; destination_mixed boolean;
    requested_mode boolean;
BEGIN
    PERFORM ledger.lock_writes();
    IF ledger.role(a) IS NULL THEN RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501'; END IF;
    IF p ?| ARRAY['createdBy','created_by'] THEN
        RAISE EXCEPTION 'Creator derives from authenticated actor' USING ERRCODE='22023';
    END IF;
    command_id:=NULLIF(p->>'id','')::uuid;
    cache_action:=CASE WHEN action IN ('draft','locations_update') THEN action||':'||COALESCE(p->>'version','create')
        WHEN action IN ('locations','mixed-bulk-policies','transfer','commit','hold') THEN action ELSE NULL END;
    IF cache_action IS NOT NULL THEN
        SELECT * INTO cache FROM ledger.mixed_bulk_command_results c
        WHERE c.action=cache_action AND c.command_id=mixed_command.command_id;
        IF FOUND THEN
            IF cache.actor_id IS DISTINCT FROM a OR cache.request IS DISTINCT FROM p THEN
                RAISE EXCEPTION 'Command retry conflicts with original actor or payload' USING ERRCODE='23514';
            END IF;
            IF action IN ('draft','commit') THEN
                SELECT * INTO STRICT saved FROM ledger.operations WHERE id=command_id;
                PERFORM ledger.require_staff(a,saved.facility_id,ARRAY['MillOperator']);
            ELSIF action='transfer' THEN
                SELECT facility_id INTO STRICT facility FROM ledger.locations WHERE id=(p->>'fromLocationId')::uuid;
                PERFORM ledger.require_staff(a,facility,CASE WHEN p->>'holdId' IS NULL THEN ARRAY['MillOperator'] ELSE ARRAY['MillManager'] END);
                SELECT facility_id INTO STRICT facility FROM ledger.locations WHERE id=(p->>'toLocationId')::uuid;
                PERFORM ledger.require_staff(a,facility,CASE WHEN p->>'holdId' IS NULL THEN ARRAY['MillOperator'] ELSE ARRAY['MillManager'] END);
            ELSIF action='hold' THEN
                SELECT * INTO STRICT source FROM ledger.lots WHERE id=(p->>'lotId')::uuid;
                IF ledger.role(a)<>'CustomerManager' OR NOT ledger.own(a,source.owner_id) THEN
                    RAISE EXCEPTION 'HOLD authority denied' USING ERRCODE='42501';
                END IF;
            ELSIF ledger.role(a)<>'Administrator' THEN
                RAISE EXCEPTION 'Setup authority denied' USING ERRCODE='42501';
            END IF;
            IF action IN ('locations','locations_update') THEN
                SELECT * INTO STRICT location FROM ledger.locations WHERE id=command_id FOR UPDATE;
                IF location.is_deleted THEN
                    RAISE EXCEPTION 'Removed setup record cannot be changed or restored' USING ERRCODE='23514';
                END IF;
                IF location.version IS DISTINCT FROM (cache.result->>'version')::uuid THEN
                    RAISE EXCEPTION 'Setup record version stale' USING ERRCODE='23514';
                END IF;
            END IF;
            RETURN cache.result;
        END IF;
    END IF;

    IF action='mixed-bulk-policies' THEN
        IF ledger.role(a)<>'Administrator' THEN RAISE EXCEPTION 'Setup authority denied' USING ERRCODE='42501'; END IF;
        IF p->'identityScopeConfirmed' IS DISTINCT FROM 'true'::jsonb THEN
            RAISE EXCEPTION 'Identity scope confirmation required' USING ERRCODE='23514';
        END IF;
        SELECT * INTO STRICT location FROM ledger.locations WHERE id=(p->>'locationId')::uuid FOR UPDATE;
        PERFORM ledger.require_active_location(location.id);
        IF NOT location.mixed_bulk OR location.kind NOT IN ('SILO','STORE','COMPARTMENT') THEN
            RAISE EXCEPTION 'Policy requires explicitly enabled eligible storage location' USING ERRCODE='23514';
        END IF;
        IF NOT EXISTS(SELECT 1 FROM identity.organizations WHERE "Id"=(p->>'ownerId')::uuid AND "Kind"='Customer')
           OR NOT EXISTS(SELECT 1 FROM ledger.commodities WHERE id=(p->>'commodityId')::uuid)
           OR NULLIF(trim(p->>'productIdentity'),'') IS NULL OR p->>'effectiveFrom' IS NULL THEN
            RAISE EXCEPTION 'Explicit customer, commodity, stage, product and effective date required' USING ERRCODE='23514';
        END IF;
        PERFORM ledger.require_evidence(a,(p->>'evidenceId')::uuid,(p->>'ownerId')::uuid,location.facility_id);
        INSERT INTO ledger.mixed_bulk_policies(id,location_id,owner_id,commodity_id,stage,product_identity,
            location_kind,effective_from,evidence_id,approved_by)
        VALUES(command_id,location.id,(p->>'ownerId')::uuid,(p->>'commodityId')::uuid,p->>'stage',p->>'productIdentity',
            location.kind,(p->>'effectiveFrom')::timestamptz,(p->>'evidenceId')::uuid,a);
        result:=p||jsonb_build_object('approvedBy',a,'locationKind',location.kind);
    ELSIF action IN ('locations','locations_update') THEN
        IF p ? 'mixedBulk' AND jsonb_typeof(p->'mixedBulk') IS DISTINCT FROM 'boolean' THEN
            RAISE EXCEPTION 'mixedBulk must be boolean' USING ERRCODE='22023';
        END IF;
        IF action='locations_update' THEN
            SELECT * INTO STRICT location FROM ledger.locations WHERE id=command_id FOR UPDATE;
            requested_mode:=COALESCE((p->>'mixedBulk')::boolean,location.mixed_bulk);
            IF requested_mode IS DISTINCT FROM location.mixed_bulk THEN
                IF EXISTS(SELECT 1 FROM ledger.positions WHERE location_id=command_id AND quantity_kg>0)
                   OR EXISTS(SELECT 1 FROM ledger.mixed_bulk_pools WHERE location_id=command_id AND lot_id IS NOT NULL)
                   OR EXISTS(SELECT 1 FROM ledger.operations op WHERE NOT op.is_deleted AND op.status='DRAFT' AND (
                       EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'inputs') x WHERE (x->>'locationId')::uuid=command_id)
                       OR EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'outputs') x WHERE (x->>'locationId')::uuid=command_id)))
                   OR EXISTS(SELECT 1 FROM ledger.corrections c WHERE c.status IN ('REQUESTED','RECONCILED') AND (
                       EXISTS(SELECT 1 FROM jsonb_array_elements(COALESCE(c.reconciliation,'[]'::jsonb)) x WHERE (x->>'locationId')::uuid=command_id)
                       OR EXISTS(SELECT 1 FROM ledger.operations op WHERE op.id=c.operation_id AND (
                           EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'inputs') x WHERE (x->>'locationId')::uuid=command_id)
                           OR EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'outputs') x WHERE (x->>'locationId')::uuid=command_id))))) THEN
                    RAISE EXCEPTION 'Mixed-bulk mode change requires empty storage without unresolved dependent work' USING ERRCODE='23514';
                END IF;
                IF location.mixed_bulk AND NOT location.cleared AND EXISTS(
                    SELECT 1 FROM ledger.custody_events ce WHERE ce.location_id=mixed_command.command_id) THEN
                    RAISE EXCEPTION 'Mixed-bulk mode change requires recorded clearance after drain' USING ERRCODE='23514';
                END IF;
            END IF;
        ELSE requested_mode:=COALESCE((p->>'mixedBulk')::boolean,false);
        END IF;
        result:=ledger.command_pre_mixed(a,action,p);
        UPDATE ledger.locations SET mixed_bulk=requested_mode WHERE id=command_id;
        result:=result-'mixed_bulk'||jsonb_build_object('mixedBulk',requested_mode);
    ELSIF action='draft' THEN
        result:=ledger.command_pre_mixed(a,action,p);
        SELECT * INTO STRICT saved FROM ledger.operations WHERE id=command_id FOR UPDATE;
        FOR item IN SELECT value FROM jsonb_array_elements(saved.content->'inputs') LOOP
            SELECT * INTO STRICT location FROM ledger.locations WHERE id=(item->>'locationId')::uuid FOR UPDATE;
            IF location.mixed_bulk THEN
                IF jsonb_typeof(item->'poolRevision') IS DISTINCT FROM 'number' OR item->>'poolRevision' !~ '^[0-9]+$' THEN
                    RAISE EXCEPTION 'Current pool revision required for mixed-bulk input' USING ERRCODE='23514';
                END IF;
                SELECT * INTO STRICT selected_pool FROM ledger.mixed_bulk_pools pool WHERE pool.location_id=location.id FOR UPDATE;
                IF selected_pool.lot_id IS DISTINCT FROM (item->>'lotId')::uuid OR selected_pool.revision<>(item->>'poolRevision')::bigint THEN
                    RAISE EXCEPTION 'Mixed-bulk input generation changed; reload and resave draft' USING ERRCODE='40001';
                END IF;
                SELECT * INTO STRICT source FROM ledger.lots WHERE id=selected_pool.lot_id;
                IF ledger.mixed_bulk_eligible(location.id,saved.owner_id,source.commodity_id,source.stage,
                    source.product_identity,clock_timestamp()) IS NULL THEN
                    RAISE EXCEPTION 'No active evidence-backed mixed-bulk identity policy' USING ERRCODE='23514';
                END IF;
            END IF;
        END LOOP;
    ELSIF action='transfer' THEN
        SELECT * INTO STRICT source FROM ledger.lots WHERE id=(p->>'lotId')::uuid FOR UPDATE;
        source_location:=(p->>'fromLocationId')::uuid; destination:=(p->>'toLocationId')::uuid;
        PERFORM ledger.require_active_location(source_location);
        PERFORM ledger.require_active_location(destination);
        SELECT * INTO STRICT location FROM ledger.locations WHERE id=source_location FOR UPDATE;
        SELECT loc.facility_id,loc.mixed_bulk INTO STRICT facility,destination_mixed FROM ledger.locations loc WHERE loc.id=destination FOR UPDATE;
        source_mixed:=location.mixed_bulk;
        PERFORM ledger.require_staff(a,location.facility_id,CASE WHEN p->>'holdId' IS NULL THEN ARRAY['MillOperator'] ELSE ARRAY['MillManager'] END);
        PERFORM ledger.require_staff(a,facility,CASE WHEN p->>'holdId' IS NULL THEN ARRAY['MillOperator'] ELSE ARRAY['MillManager'] END);
        SELECT * INTO prior FROM ledger.events WHERE id=command_id;
        IF FOUND THEN
            IF prior.kind<>'TRANSFER' OR prior.actor_id IS DISTINCT FROM a OR prior.details IS DISTINCT FROM p THEN
                RAISE EXCEPTION 'Transfer retry payload conflicts' USING ERRCODE='23514';
            END IF;
            RETURN jsonb_build_object('id',command_id);
        END IF;
        quantity:=ledger.kg(p->>'quantityKg');
        IF quantity<=0 OR source_location=destination THEN
            RAISE EXCEPTION 'Positive transfer between different locations required' USING ERRCODE='23514';
        END IF;
        expected_revision:=NULL;
        IF source_mixed THEN
            IF jsonb_typeof(p->'poolRevision') IS DISTINCT FROM 'number' OR p->>'poolRevision' !~ '^[0-9]+$' THEN
                RAISE EXCEPTION 'Current pool revision required for mixed-bulk input' USING ERRCODE='23514';
            END IF;
            expected_revision:=(p->>'poolRevision')::bigint;
            SELECT * INTO STRICT selected_pool FROM ledger.mixed_bulk_pools pool WHERE pool.location_id=source_location FOR UPDATE;
            IF selected_pool.lot_id IS DISTINCT FROM source.id OR selected_pool.revision<>expected_revision THEN
                RAISE EXCEPTION 'Mixed-bulk input generation changed; reload current stock' USING ERRCODE='40001';
            END IF;
        END IF;
        IF NOT EXISTS(SELECT 1 FROM ledger.positions WHERE lot_id=source.id AND location_id=source_location AND quantity_kg>=quantity) THEN
            RAISE EXCEPTION 'Insufficient stock' USING ERRCODE='23514';
        END IF;
        IF p->>'holdId' IS NULL THEN
            IF ledger.blocked(source.id) THEN RAISE EXCEPTION 'Lot restricted from transfer' USING ERRCODE='23514'; END IF;
        ELSE
            IF NOT EXISTS(SELECT 1 FROM ledger.effective_hold_ids(source.id) h WHERE h=(p->>'holdId')::uuid)
               OR NOT EXISTS(SELECT 1 FROM ledger.locations WHERE id=destination AND kind='ISOLATION') THEN
                RAISE EXCEPTION 'HOLD transfer requires active effective HOLD and isolation location' USING ERRCODE='23514';
            END IF;
            IF ledger.mixed_bulk_independent_blocked(source.id) THEN
                RAISE EXCEPTION 'Independent correction/receiving block prevents isolation movement' USING ERRCODE='23514';
            END IF;
        END IF;
        source_actual:=ledger.pool_withdraw(a,command_id,CASE WHEN destination_mixed THEN 'TRANSFER_TO_MIXED' ELSE 'TRANSFER' END,
            source.id,source_location,quantity,expected_revision);
        PERFORM ledger.credit_location(destination,source.owner_id);
        -- Keep ordinary TRANSFER details exactly equal to authenticated logical request.
        INSERT INTO ledger.events(id,kind,actor_id,lot_id,details,occurred_at)
        VALUES(command_id,'TRANSFER',a,source_actual,p,(p->>'occurredAt')::timestamptz);
        PERFORM ledger.post(command_id,source_actual,source_location,-quantity);
        PERFORM ledger.post(command_id,source_actual,destination,quantity);
        current_pool:=ledger.pool_ingress(a,command_id,'TRANSFER',destination,source_actual,quantity);
        result:=jsonb_build_object('id',command_id);
        IF source_actual<>source.id OR source_mixed OR destination_mixed THEN
            result:=result||jsonb_build_object('actualLotId',source_actual);
            IF destination_mixed THEN result:=result||jsonb_build_object('currentPoolLotId',current_pool); END IF;
            IF source_mixed THEN result:=result||jsonb_build_object('sourceCurrentPoolLotId',
                (SELECT pool.lot_id FROM ledger.mixed_bulk_pools pool WHERE pool.location_id=source_location)); END IF;
        END IF;
    ELSIF action='hold' THEN
        result:=ledger.command_pre_mixed(a,action,p);
        -- Notify every affected facility, including stock derived from intermediate pool lots.
        INSERT INTO ledger.notifications(user_id,hold_id,lot_id,message)
        SELECT DISTINCT u."Id",command_id,pos.lot_id,p->>'reason'
        FROM ledger.positions pos JOIN ledger.locations loc ON loc.id=pos.location_id
        JOIN identity.users u ON u."IsActive" AND u."Role" IN ('MillManager','MillOperator')
        WHERE pos.quantity_kg>0 AND ledger.assigned(u."Id",loc.facility_id)
          AND EXISTS(SELECT 1 FROM ledger.effective_hold_ids(pos.lot_id) h WHERE h=command_id)
          AND NOT EXISTS(SELECT 1 FROM ledger.notifications n WHERE n.user_id=u."Id" AND n.hold_id=command_id AND n.lot_id=pos.lot_id);
    ELSIF action='hold_response' THEN
        SELECT * INTO STRICT hold FROM ledger.holds WHERE id=command_id FOR UPDATE;
        SELECT * INTO STRICT source FROM ledger.lots WHERE id=hold.lot_id;
        SELECT ev.facility_id INTO facility FROM ledger.evidence ev
        WHERE ev.id=(p->>'evidenceId')::uuid AND ev.owner_id=source.owner_id AND EXISTS(
            SELECT 1 FROM ledger.positions pos JOIN ledger.locations loc ON loc.id=pos.location_id
            WHERE loc.facility_id=ev.facility_id AND (pos.lot_id=source.id OR EXISTS(
                SELECT 1 FROM ledger.effective_hold_ids(pos.lot_id) h WHERE h=command_id))
              AND (pos.quantity_kg>0 OR NOT EXISTS(
                  SELECT 1 FROM ledger.positions active WHERE active.quantity_kg>0 AND EXISTS(
                      SELECT 1 FROM ledger.effective_hold_ids(active.lot_id) h WHERE h=command_id))));
        IF facility IS NULL THEN RAISE EXCEPTION 'HOLD response requires evidence scoped to affected current or historical facility' USING ERRCODE='42501'; END IF;
        PERFORM ledger.require_staff(a,facility,ARRAY['MillManager']);
        PERFORM ledger.require_evidence(a,(p->>'evidenceId')::uuid,source.owner_id,facility);
        FOR affected_facility IN SELECT DISTINCT loc.facility_id FROM ledger.positions pos
            JOIN ledger.locations loc ON loc.id=pos.location_id WHERE pos.quantity_kg>0
              AND EXISTS(SELECT 1 FROM ledger.effective_hold_ids(pos.lot_id) h WHERE h=command_id)
        LOOP PERFORM ledger.require_staff(a,affected_facility,ARRAY['MillManager']); END LOOP;
        SELECT * INTO prior FROM ledger.events ev WHERE ev.kind='HOLD_RESPONSE'
            AND ev.lot_id=source.id AND ev.details=p ORDER BY ev.occurred_at,ev.id LIMIT 1;
        IF FOUND THEN
            IF prior.actor_id IS DISTINCT FROM a THEN
                RAISE EXCEPTION 'HOLD response retry conflicts with original actor' USING ERRCODE='23514';
            END IF;
            RETURN jsonb_build_object('id',command_id);
        END IF;
        IF p->>'status' IS NULL OR p->>'status' NOT IN ('ACKNOWLEDGED','ISOLATED','RESPONDED') THEN
            RAISE EXCEPTION 'HOLD release unsupported' USING ERRCODE='23514';
        END IF;
        IF p->>'status'='ISOLATED' AND EXISTS(SELECT 1 FROM ledger.positions pos
            JOIN ledger.locations loc ON loc.id=pos.location_id WHERE pos.quantity_kg>0 AND loc.kind<>'ISOLATION'
              AND EXISTS(SELECT 1 FROM ledger.effective_hold_ids(pos.lot_id) h WHERE h=command_id)) THEN
            RAISE EXCEPTION 'Affected HOLD stock remains outside isolation' USING ERRCODE='23514';
        END IF;
        UPDATE ledger.holds SET status=p->>'status',response=p->>'response',evidence_id=(p->>'evidenceId')::uuid WHERE id=command_id;
        INSERT INTO ledger.events(kind,actor_id,lot_id,details) VALUES('HOLD_RESPONSE',a,source.id,p);
        RETURN jsonb_build_object('id',command_id);
    ELSE
        IF action IN ('correction_request','correction_reconcile','correction_cancel','correction_commit') THEN
            PERFORM ledger.require_mixed_correction_safe(action,p||jsonb_build_object('_correctionId',command_id));
        END IF;
        result:=ledger.command_pre_mixed(a,action,p);
    END IF;
    IF cache_action IS NOT NULL THEN
        INSERT INTO ledger.mixed_bulk_command_results(action,command_id,actor_id,request,result)
        VALUES(cache_action,command_id,a,p,result);
    END IF;
    RETURN result;
END $$;
REVOKE ALL ON FUNCTION ledger.accept_arrival(uuid),ledger.commit_operation(uuid,uuid,uuid),ledger.command(uuid,text,jsonb) FROM PUBLIC;
