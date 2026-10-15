CREATE FUNCTION ledger.commit_operation(a uuid,op uuid,corr uuid DEFAULT NULL) RETURNS jsonb
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
BEGIN
    PERFORM ledger.lock_writes();
    SELECT * INTO STRICT operation FROM ledger.operations WHERE id=op FOR UPDATE;
    PERFORM ledger.require_staff(a,operation.facility_id,ARRAY['MillOperator']);
    IF operation.status='COMMITTED' AND corr IS NULL THEN
        RETURN ledger.operation_json(op);
    END IF;
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
        'varianceKg',variance::text,'allowedVarianceKg',allowed::text,'content',operation.content
    ),operation.occurred_at);
    FOR allocation IN SELECT value FROM jsonb_array_elements(operation.content->'inputs') LOOP
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
        INSERT INTO ledger.contributions(lot_id,source_lot_id,quantity_kg)
        WITH input_weights AS (
            SELECT (i->>'lotId')::uuid input_id,sum(ledger.kg(i->>'quantityKg')*1000000) mass
            FROM jsonb_array_elements(operation.content->'inputs') i GROUP BY (i->>'lotId')::uuid
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
        SELECT (output->>'id')::uuid,source_lot_id,
               sum(base+CASE WHEN n<=remaining THEN 1 ELSE 0 END)/1000000
        FROM source_ranked GROUP BY source_lot_id;
        IF (SELECT sum(quantity_kg) FROM ledger.contributions WHERE lot_id=(output->>'id')::uuid)
           IS DISTINCT FROM quantity THEN
            RAISE EXCEPTION 'Lineage does not conserve output mass' USING ERRCODE='23514';
        END IF;
    END LOOP;
    UPDATE ledger.operations
    SET status='COMMITTED',variance_kg=variance,allowed_variance_kg=allowed,correction_id=corr
    WHERE id=op;
    RETURN ledger.operation_json(op);
END $$;

CREATE FUNCTION ledger.operation_json(op uuid) RETURNS jsonb
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger AS $$
    SELECT operation.content || jsonb_build_object(
        'id',operation.id,'ownerId',operation.owner_id,'facilityId',operation.facility_id,
        'kind',operation.kind,'occurredAt',operation.occurred_at,'equipmentId',operation.equipment_id,
        'status',operation.status,'version',operation.version,
        'confirmedVersion',operation.confirmed_version,'confirmedBy',operation.confirmed_by,
        'varianceKg',CASE WHEN operation.status='DRAFT'
            THEN (inputs.mass-outputs.mass-ledger.kg(operation.content->>'declaredLossKg'))::text
            ELSE operation.variance_kg::text END,
        'allowedVarianceKg',CASE WHEN operation.status='DRAFT'
            THEN (floor(inputs.mass*policy.tolerance_percent*10000)/1000000)::text
            ELSE operation.allowed_variance_kg::text END,
        'correctionId',operation.correction_id
    )
    FROM ledger.operations operation
    CROSS JOIN LATERAL (
        SELECT sum(ledger.kg(value->>'quantityKg')) mass
        FROM jsonb_array_elements(operation.content->'inputs')
    ) inputs
    CROSS JOIN LATERAL (
        SELECT sum(ledger.kg(value->>'quantityKg')) mass
        FROM jsonb_array_elements(operation.content->'outputs')
    ) outputs
    LEFT JOIN LATERAL (
        SELECT tolerance_percent FROM ledger.policies
        WHERE facility_id=operation.facility_id AND kind=operation.kind
          AND effective_from<=operation.occurred_at
        ORDER BY effective_from DESC LIMIT 1
    ) policy ON true
    WHERE operation.id=op
$$;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA ledger FROM PUBLIC;
