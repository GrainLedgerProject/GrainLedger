-- Forward replacements: retired setup is unavailable for new work, history remains intact.
CREATE OR REPLACE FUNCTION ledger.assigned(a uuid,f uuid) RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger,identity AS $$
    SELECT EXISTS(SELECT 1 FROM ledger.assignments x JOIN ledger.facilities facility ON facility.id=x.facility_id
        JOIN identity.users u ON u."Id"=x.user_id
        WHERE x.user_id=a AND x.facility_id=f AND NOT x.is_deleted AND NOT facility.is_deleted
          AND u."IsActive" AND u."Role" IN ('MillOperator','MillManager'))
$$;

CREATE FUNCTION ledger.require_active_location(location_id uuid) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ledger.locations l JOIN ledger.facilities f ON f.id=l.facility_id
        WHERE l.id=location_id AND NOT l.is_deleted AND NOT f.is_deleted) THEN
        RAISE EXCEPTION 'Active location and facility required' USING ERRCODE='23514';
    END IF;
END $$;
REVOKE ALL ON FUNCTION ledger.require_active_location(uuid) FROM PUBLIC;

CREATE FUNCTION ledger.require_active_operation(operation_id uuid) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
DECLARE op ledger.operations; item jsonb;
BEGIN
    SELECT * INTO STRICT op FROM ledger.operations WHERE id=operation_id;
    IF op.is_deleted OR NOT EXISTS(SELECT 1 FROM ledger.facilities WHERE id=op.facility_id AND NOT is_deleted)
        OR NOT EXISTS(SELECT 1 FROM ledger.equipment WHERE id=op.equipment_id AND facility_id=op.facility_id AND NOT is_deleted) THEN
        RAISE EXCEPTION 'Active operation, facility and equipment required' USING ERRCODE='23514';
    END IF;
    FOR item IN SELECT value FROM jsonb_array_elements(op.content->'inputs') UNION ALL
                SELECT value FROM jsonb_array_elements(op.content->'outputs') LOOP
        PERFORM ledger.require_active_location((item->>'locationId')::uuid);
    END LOOP;
    IF EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'outputs') x
        JOIN ledger.catalog_commodities map ON map.ledger_commodity_id=(x->>'commodityId')::uuid
        JOIN catalog.commodities c ON c.id=map.catalog_commodity_id WHERE c.is_archived) THEN
        RAISE EXCEPTION 'Active output commodities required' USING ERRCODE='23514';
    END IF;
END $$;
REVOKE ALL ON FUNCTION ledger.require_active_operation(uuid) FROM PUBLIC;

CREATE OR REPLACE FUNCTION ledger.can_read_operation(a uuid,op uuid) RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger,identity AS $$
    SELECT EXISTS (
        SELECT 1 FROM ledger.operations x WHERE x.id=op AND NOT x.is_deleted
          AND EXISTS(SELECT 1 FROM ledger.facilities WHERE id=x.facility_id AND NOT is_deleted) AND (
            ledger.role(a) IN ('CustomerManager','ExportStaff','PurchasingStaff')
            AND ledger.own(a,x.owner_id)
            OR ledger.role(a) IN ('MillManager','MillOperator')
            AND ledger.assigned(a,x.facility_id)
        )
    )
$$;

CREATE OR REPLACE FUNCTION ledger.require_evidence(a uuid,e uuid,o uuid,f uuid) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ledger.evidence WHERE id=e AND facility_id=f AND owner_id=o)
       OR NOT EXISTS(SELECT 1 FROM ledger.facilities WHERE id=f AND NOT is_deleted) THEN
        RAISE EXCEPTION 'Evidence missing or outside owner/facility scope' USING ERRCODE='42501';
    END IF;
END $$;

CREATE OR REPLACE FUNCTION ledger.credit_location(l uuid,o uuid) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
DECLARE
    destination ledger.locations;
BEGIN
    PERFORM ledger.require_active_location(l);
    SELECT * INTO STRICT destination FROM ledger.locations WHERE id=l FOR UPDATE;
    IF destination.kind='COMPARTMENT' THEN
        IF destination.owner_id IS DISTINCT FROM o
           AND (destination.owner_id IS NOT NULL OR NOT destination.cleared) THEN
            RAISE EXCEPTION 'Compartment requires empty-stock recorded clearance before owner switch'
                USING ERRCODE='23514';
        END IF;
        UPDATE ledger.locations SET owner_id=o,cleared=false,version=gen_random_uuid() WHERE id=l;
    END IF;
END $$;

CREATE OR REPLACE FUNCTION ledger.post(e uuid,l uuid,loc uuid,q numeric,rev uuid DEFAULT NULL) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
BEGIN
    PERFORM ledger.require_active_location(loc);
    INSERT INTO ledger.postings(event_id,lot_id,location_id,quantity_kg,reverses_posting_id)
    VALUES(e,l,loc,q,rev);
    UPDATE ledger.positions SET quantity_kg=quantity_kg+q WHERE lot_id=l AND location_id=loc;
    IF NOT FOUND THEN INSERT INTO ledger.positions VALUES(l,loc,q); END IF;
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
BEGIN
    PERFORM ledger.lock_writes();
    PERFORM ledger.require_active_operation(op);
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

CREATE OR REPLACE FUNCTION ledger.review_replacement(
    a uuid,corr uuid,replacement uuid,reviewed uuid,physical_positions jsonb
) RETURNS void LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
DECLARE
    original ledger.operations;
    replacement_draft ledger.operations;
    output jsonb;
    output_facility uuid;
BEGIN
    SELECT op.* INTO STRICT original
    FROM ledger.corrections c JOIN ledger.operations op ON op.id=c.operation_id
    WHERE c.id=corr;
    PERFORM ledger.require_active_operation(replacement);
    SELECT * INTO STRICT replacement_draft FROM ledger.operations
    WHERE id=replacement FOR UPDATE;
    PERFORM ledger.require_staff(a,original.facility_id,ARRAY['MillManager']);

    -- Physical evidence reviews exact replacement content, never an inferred latest version.
    IF replacement_draft.status<>'DRAFT'
       OR replacement_draft.owner_id<>original.owner_id
       OR replacement_draft.facility_id<>original.facility_id
       OR replacement_draft.version IS DISTINCT FROM reviewed THEN
        RAISE EXCEPTION 'Replacement review stale or outside original owner/facility'
            USING ERRCODE='23514';
    END IF;
    FOR output IN SELECT value FROM jsonb_array_elements(replacement_draft.content->'outputs') LOOP
        IF NOT EXISTS (
            SELECT 1 FROM jsonb_array_elements(physical_positions) pos
            WHERE pos->>'locationId'=output->>'locationId'
        ) THEN
            RAISE EXCEPTION 'Replacement must retain reconciled current physical locations'
                USING ERRCODE='23514';
        END IF;
        SELECT facility_id INTO STRICT output_facility FROM ledger.locations
        WHERE id=(output->>'locationId')::uuid;
        PERFORM ledger.require_staff(a,output_facility,ARRAY['MillManager']);
    END LOOP;
    UPDATE ledger.corrections
    SET replacement_operation_id=replacement_draft.id,
        replacement_version=replacement_draft.version,
        replacement_content=replacement_draft.content
    WHERE id=corr;
END $$;

CREATE OR REPLACE FUNCTION ledger.accept_arrival(r uuid) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
DECLARE h capture.procurements; s ledger.receiving; ms ledger.arrival_measurement_sets; snap jsonb; commodity uuid; c uuid; loc uuid; row record; event_id uuid; review ledger.arrival_reviews; code text;
BEGIN
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
        code:=upper('LL-'||replace(row.lot_id::text,'-',''));
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
    END LOOP;
END $$;

CREATE OR REPLACE FUNCTION ledger.arrival_command(a uuid,r uuid,k text,j jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE h capture.procurements; s ledger.receiving; prior ledger.arrival_operations; op uuid; set_id uuid; review_id uuid; x jsonb; v numeric; kg numeric; unit text; action text; result jsonb; normalized jsonb;
BEGIN
    PERFORM ledger.lock_writes();
    SELECT * INTO STRICT h FROM capture.procurements WHERE id=r AND receiving_workflow='PHYSICAL_ARRIVAL_V1';
    PERFORM ledger.require_staff(a,h.collection_site_id,CASE k WHEN 'measurements' THEN ARRAY['MillOperator'] WHEN 'reviews' THEN ARRAY['MillManager'] ELSE ARRAY[]::text[] END);
    PERFORM ledger.require_active_location((SELECT (payload->'result'->>'ReceivingLocationId')::uuid FROM capture.business_events WHERE id=h.event_id));
    op:=(j->>'operationId')::uuid;
    IF op IS NULL OR op='00000000-0000-0000-0000-000000000000' THEN RAISE EXCEPTION 'Operation ID required' USING ERRCODE='22023'; END IF;
    normalized:=j;
    IF k='measurements' AND jsonb_typeof(j->'items')='array' THEN normalized:=jsonb_set(j,'{items}',(SELECT jsonb_agg(entry.value ORDER BY entry.value->>'procurementItemId') FROM jsonb_array_elements(j->'items') entry)); END IF;
    SELECT * INTO prior FROM ledger.arrival_operations WHERE operation_id=op;
    IF FOUND THEN
        IF prior.actor_id<>a THEN RAISE EXCEPTION 'Operation actor mismatch' USING ERRCODE='42501'; END IF;
        IF prior.procurement_id<>r OR prior.kind<>k OR prior.payload<>normalized THEN RAISE EXCEPTION 'Operation ID used with different content' USING ERRCODE='23514'; END IF;
        RETURN prior.result;
    END IF;
    SELECT * INTO STRICT s FROM ledger.receiving WHERE procurement_id=r FOR UPDATE;
    IF (j->>'expectedReceivingVersion')::integer IS DISTINCT FROM s.version THEN RAISE EXCEPTION 'Receiving version stale' USING ERRCODE='23514'; END IF;
    IF k='measurements' THEN
        IF s.status NOT IN ('PENDING_ARRIVAL','REMEASUREMENT_REQUESTED') THEN RAISE EXCEPTION 'Arrival measurement not allowed in current state' USING ERRCODE='23514'; END IF;
        IF jsonb_typeof(j->'items') IS DISTINCT FROM 'array' OR jsonb_array_length(j->'items') NOT BETWEEN 1 AND 100
           OR jsonb_array_length(j->'items')<>(SELECT count(*) FROM capture.procurement_items WHERE procurement_id=r)
           OR (SELECT count(DISTINCT (entry.value->>'procurementItemId')::uuid) FROM jsonb_array_elements(j->'items') entry)<>jsonb_array_length(j->'items')
           OR j->>'capturedAt' IS NULL OR NOT isfinite((j->>'capturedAt')::timestamptz)
           OR j->>'observations' IS NULL OR length(j->>'observations')>4000 THEN RAISE EXCEPTION 'Complete arrival set, finite timestamp and observations required' USING ERRCODE='22023'; END IF;
        set_id:=gen_random_uuid();
        INSERT INTO ledger.arrival_measurement_sets(id,procurement_id,operation_id,operator_id,captured_at,observations,expected_version)
        VALUES(set_id,r,op,a,(j->>'capturedAt')::timestamptz,j->>'observations',s.version);
        FOR x IN SELECT value FROM jsonb_array_elements(j->'items') LOOP
            IF NOT EXISTS(SELECT 1 FROM capture.procurement_items WHERE id=(x->>'procurementItemId')::uuid AND procurement_id=r)
               OR jsonb_typeof(x->'measuredValue') IS DISTINCT FROM 'string' OR x->>'measuredValue' !~ '^[0-9]{1,12}(\.[0-9]{1,12})?$' THEN RAISE EXCEPTION 'Invalid arrival line or decimal precision' USING ERRCODE='22023'; END IF;
            v:=(x->>'measuredValue')::numeric; unit:=x->>'measuredUnit';
            kg:=CASE unit WHEN 'KG' THEN v WHEN 'G' THEN v/1000 WHEN 'T' THEN v*1000 END;
            IF kg IS NULL OR kg<=0 OR kg>=100000000000000 OR trunc(kg,6)<>kg THEN RAISE EXCEPTION 'Arrival mass must be positive exact numeric(20,6) kg; zero/full loss unsupported' USING ERRCODE='22023'; END IF;
            IF NOT EXISTS(SELECT 1 FROM catalog.measurement_units WHERE code=unit AND quantity_kind='MASS' AND NOT is_deleted) THEN RAISE EXCEPTION 'Arrival mass unit not configured' USING ERRCODE='23514'; END IF;
            INSERT INTO ledger.arrival_measurements VALUES(set_id,(x->>'procurementItemId')::uuid,v,unit,kg);
        END LOOP;
        IF EXISTS(SELECT 1 FROM ledger.arrival_measurements m JOIN capture.procurement_items i ON i.id=m.procurement_item_id WHERE m.measurement_set_id=set_id AND m.actual_kg<>i.net_mass_kg) THEN
            UPDATE ledger.receiving SET status='AWAITING_MANAGER_REVIEW',version=version+1,latest_measurement_set_id=set_id WHERE procurement_id=r;
        ELSE
            UPDATE ledger.receiving SET status='RECEIVED_MATCHED',version=version+1,latest_measurement_set_id=set_id,accepted_measurement_set_id=set_id WHERE procurement_id=r;
            PERFORM ledger.accept_arrival(r);
        END IF;
    ELSE
        IF s.status NOT IN ('AWAITING_MANAGER_REVIEW','BLOCKED_UNRESOLVED') OR (j->>'measurementSetId')::uuid IS DISTINCT FROM s.latest_measurement_set_id THEN RAISE EXCEPTION 'Review state or measurement set stale' USING ERRCODE='23514'; END IF;
        PERFORM ledger.require_evidence(a,(j->>'evidenceId')::uuid,h.company_id,h.collection_site_id);
        IF j->>'reason' IS NULL OR length(trim(j->>'reason')) NOT BETWEEN 1 AND 4000 THEN RAISE EXCEPTION 'Review reason required' USING ERRCODE='22023'; END IF;
        action:=j->>'action';
        IF action IS NULL OR action NOT IN ('APPROVE_SHORTAGE','REQUEST_REMEASUREMENT','KEEP_BLOCKED') THEN RAISE EXCEPTION 'Unsupported receiving review action' USING ERRCODE='22023'; END IF;
        IF action='APPROVE_SHORTAGE' AND (EXISTS(SELECT 1 FROM ledger.arrival_measurements m JOIN capture.procurement_items i ON i.id=m.procurement_item_id WHERE m.measurement_set_id=s.latest_measurement_set_id AND m.actual_kg>i.net_mass_kg)
            OR NOT EXISTS(SELECT 1 FROM ledger.arrival_measurements m JOIN capture.procurement_items i ON i.id=m.procurement_item_id WHERE m.measurement_set_id=s.latest_measurement_set_id AND m.actual_kg<i.net_mass_kg)) THEN RAISE EXCEPTION 'Only measured shortage without any surplus can be approved' USING ERRCODE='23514'; END IF;
        review_id:=gen_random_uuid();
        INSERT INTO ledger.arrival_reviews(id,procurement_id,measurement_set_id,operation_id,expected_version,action,reason,evidence_id,manager_id)
        VALUES(review_id,r,s.latest_measurement_set_id,op,s.version,action,j->>'reason',(j->>'evidenceId')::uuid,a);
        UPDATE ledger.receiving SET status=CASE action WHEN 'APPROVE_SHORTAGE' THEN 'RECEIVED_SHORTAGE_APPROVED' WHEN 'REQUEST_REMEASUREMENT' THEN 'REMEASUREMENT_REQUESTED' ELSE 'BLOCKED_UNRESOLVED' END,
            version=version+1,accepted_measurement_set_id=CASE WHEN action='APPROVE_SHORTAGE' THEN latest_measurement_set_id END,
            accepted_review_id=CASE WHEN action='APPROVE_SHORTAGE' THEN review_id END WHERE procurement_id=r;
        IF action='APPROVE_SHORTAGE' THEN PERFORM ledger.accept_arrival(r); END IF;
    END IF;
    IF ledger.procurement_package_complete(r) IS DISTINCT FROM true THEN RAISE EXCEPTION 'Receiving package incomplete' USING ERRCODE='23514'; END IF;
    result:=ledger.receiving_detail(a,r);
    INSERT INTO ledger.arrival_operations(operation_id,procurement_id,actor_id,kind,payload,result) VALUES(op,r,a,k,normalized,result);
    RETURN result;
END $$;

CREATE OR REPLACE FUNCTION ledger.receiving_detail(a uuid,r uuid) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE h capture.procurements; s ledger.receiving; snap jsonb; result jsonb;
BEGIN
    SELECT * INTO STRICT h FROM capture.procurements WHERE id=r AND receiving_workflow IS NOT NULL;
    PERFORM ledger.require_staff(a,h.collection_site_id,ARRAY['MillOperator','MillManager']);
    SELECT * INTO STRICT s FROM ledger.receiving WHERE procurement_id=r;
    SELECT payload->'result' INTO snap FROM capture.business_events WHERE id=h.event_id;
    PERFORM ledger.require_active_location((snap->>'ReceivingLocationId')::uuid);
    SELECT jsonb_build_object('id',h.id,'code',h.code,'ownerCompanyId',h.company_id,'ownerCompanyName',o."Name",
        'facilityId',h.collection_site_id,'facilityName',f.name,'receivingLocationId',l.id,'receivingLocationName',l.name,
        'status',s.status,'receivingVersion',s.version,'receivedAt',h.received_at,'expectedTotalKg',snap->>'TotalNetMassKg',
        'actualTotalKg',(SELECT sum(actual_kg)::text FROM ledger.arrival_measurements WHERE measurement_set_id=s.latest_measurement_set_id),
        'usableStockKg',CASE WHEN s.accepted_measurement_set_id IS NULL THEN '0' ELSE COALESCE((SELECT sum(p.quantity_kg)::text FROM ledger.positions p JOIN capture.source_lots sl ON sl.lot_id=p.lot_id JOIN capture.procurement_items i ON i.id=sl.procurement_item_id WHERE i.procurement_id=r AND NOT ledger.blocked(p.lot_id)),'0') END,
        'latestMeasurementSetId',s.latest_measurement_set_id,
        'items',(SELECT jsonb_agg(jsonb_build_object('id',i.id,'lineNo',i.line_no,'sourceLotId',sl.id,'lotId',sl.lot_id,'lotCode',upper('LL-'||replace(sl.lot_id::text,'-','')),
            'plotSeasonId',i.plot_season_id,'riceVariety',ps.rice_variety,'expectedValue',i.measured_value::text,'expectedUnit',i.measured_unit,'expectedKg',i.net_mass_kg::text,
            'actualValue',m.measured_value::text,'actualUnit',m.measured_unit,'actualKg',m.actual_kg::text) ORDER BY i.line_no)
            FROM capture.procurement_items i JOIN capture.source_lots sl ON sl.procurement_item_id=i.id JOIN capture.plot_seasons ps ON ps.id=i.plot_season_id
            LEFT JOIN ledger.arrival_measurements m ON m.procurement_item_id=i.id AND m.measurement_set_id=s.latest_measurement_set_id WHERE i.procurement_id=r),
        'measurementSets',COALESCE((SELECT jsonb_agg(jsonb_build_object('id',ms.id,'operationId',ms.operation_id,'operatorId',ms.operator_id,'capturedAt',ms.captured_at,'recordedAt',ms.recorded_at,'observations',ms.observations,
            'items',(SELECT jsonb_agg(jsonb_build_object('procurementItemId',m.procurement_item_id,'measuredValue',m.measured_value::text,'measuredUnit',m.measured_unit,'actualKg',m.actual_kg::text) ORDER BY i.line_no) FROM ledger.arrival_measurements m JOIN capture.procurement_items i ON i.id=m.procurement_item_id WHERE m.measurement_set_id=ms.id)) ORDER BY ms.recorded_at,ms.id) FROM ledger.arrival_measurement_sets ms WHERE ms.procurement_id=r),'[]'::jsonb),
        'reviews',COALESCE((SELECT jsonb_agg(jsonb_build_object('id',v.id,'operationId',v.operation_id,'measurementSetId',v.measurement_set_id,'action',v.action,'reason',v.reason,'evidenceId',v.evidence_id,'managerId',v.manager_id,'recordedAt',v.recorded_at) ORDER BY v.recorded_at,v.id) FROM ledger.arrival_reviews v WHERE v.procurement_id=r),'[]'::jsonb)) INTO result
    FROM identity.organizations o JOIN ledger.facilities f ON f.id=h.collection_site_id JOIN ledger.locations l ON l.id=(snap->>'ReceivingLocationId')::uuid WHERE o."Id"=h.company_id;
    RETURN result;
END $$;

REVOKE ALL ON FUNCTION ledger.require_evidence(uuid,uuid,uuid,uuid),ledger.credit_location(uuid,uuid),
    ledger.post(uuid,uuid,uuid,numeric,uuid),ledger.commit_operation(uuid,uuid,uuid),
    ledger.review_replacement(uuid,uuid,uuid,uuid,jsonb),ledger.accept_arrival(uuid) FROM PUBLIC;
