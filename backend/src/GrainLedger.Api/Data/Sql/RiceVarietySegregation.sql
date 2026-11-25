ALTER TABLE grainledger.capture_plot_seasons
    ADD COLUMN rice_variety_id uuid NULL,
    ADD CONSTRAINT capture_fk_plot_seasons_rice_variety FOREIGN KEY (rice_variety_id)
        REFERENCES grainledger.catalog_rice_varieties(id) ON DELETE RESTRICT;
CREATE INDEX capture_ix_plot_seasons_rice_variety_id ON grainledger.capture_plot_seasons (rice_variety_id);


ALTER TABLE grainledger.ledger_lots
    ADD COLUMN variety_status text NOT NULL DEFAULT 'UNKNOWN',
    ADD COLUMN rice_variety_id uuid REFERENCES grainledger.catalog_rice_varieties(id) ON DELETE RESTRICT,
    ADD COLUMN rice_variety_name text,
    ADD CONSTRAINT ledger_lots_variety_check CHECK (
        (variety_status='SINGLE' AND rice_variety_id IS NOT NULL AND NULLIF(btrim(rice_variety_name),'') IS NOT NULL)
        OR (variety_status IN ('UNKNOWN','MIXED') AND rice_variety_id IS NULL AND rice_variety_name IS NULL));

CREATE TABLE grainledger.ledger_byproduct_variety_policy_revisions (
    company_id uuid NOT NULL REFERENCES grainledger.identity_organizations("Id"),
    stage text NOT NULL CHECK (stage IN ('BRAN','HUSK')),
    revision uuid NOT NULL UNIQUE,
    change_no bigint NOT NULL,
    allow_cross_variety boolean,
    actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
    reason text NOT NULL,
    recorded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY(company_id,stage,change_no)
);
CREATE TRIGGER ledger_byproduct_variety_policy_immutable BEFORE UPDATE OR DELETE
    ON grainledger.ledger_byproduct_variety_policy_revisions FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE FUNCTION grainledger.ledger_effective_byproduct_variety_policy(owner uuid, policy_stage text) RETURNS jsonb
LANGUAGE sql VOLATILE SET search_path TO 'pg_catalog','grainledger','pg_temp' AS $$
    SELECT jsonb_build_object('companyId',owner,'stage',policy_stage,
        'allowCrossVariety',COALESCE(p.allow_cross_variety,true),
        'source',CASE WHEN p.allow_cross_variety IS NULL THEN 'SYSTEM_DEFAULT' ELSE 'COMPANY_OVERRIDE' END,
        'revision',p.revision)
    FROM (SELECT 1) seed LEFT JOIN LATERAL (
        SELECT r.allow_cross_variety,r.revision FROM grainledger.ledger_byproduct_variety_policy_revisions r
        WHERE r.company_id=owner AND r.stage=policy_stage ORDER BY r.change_no DESC LIMIT 1
    ) p ON true
$$;

CREATE FUNCTION grainledger.ledger_resolve_lot_variety(owner uuid, stage text, input_ids uuid[]) RETURNS jsonb
LANGUAGE plpgsql STABLE SET search_path TO 'pg_catalog','grainledger','pg_temp' AS $$
DECLARE n integer; found_count integer; unknown_count integer; mixed_count integer; variety_count integer;
    first_lot grainledger.ledger_lots; policy jsonb;
BEGIN
    SELECT count(*) INTO n FROM (SELECT DISTINCT unnest(input_ids) id) ids;
    IF n=0 OR input_ids IS NULL THEN RAISE EXCEPTION 'Input lots required' USING ERRCODE='23514'; END IF;
    SELECT count(*),count(*) FILTER (WHERE l.variety_status='UNKNOWN'),
           count(*) FILTER (WHERE l.variety_status='MIXED'),count(DISTINCT l.rice_variety_id)
    INTO found_count,unknown_count,mixed_count,variety_count
    FROM grainledger.ledger_lots l WHERE l.id=ANY(input_ids) AND l.owner_id=owner;
    IF found_count<>n THEN RAISE EXCEPTION 'Input lot missing or outside owner' USING ERRCODE='23514'; END IF;
    SELECT * INTO first_lot FROM grainledger.ledger_lots l WHERE l.id=ANY(input_ids) ORDER BY l.id LIMIT 1;
    IF n=1 THEN
        RETURN jsonb_build_object('varietyStatus',first_lot.variety_status,'riceVarietyId',first_lot.rice_variety_id,
            'riceVariety',first_lot.rice_variety_name,'varietyMixPolicy',NULL);
    END IF;
    IF unknown_count>0 THEN RAISE EXCEPTION 'Rice variety is unknown' USING ERRCODE='23514'; END IF;
    IF mixed_count=0 AND variety_count=1 THEN
        RETURN jsonb_build_object('varietyStatus','SINGLE','riceVarietyId',first_lot.rice_variety_id,
            'riceVariety',first_lot.rice_variety_name,'varietyMixPolicy',NULL);
    END IF;
    IF stage NOT IN ('BRAN','HUSK') OR stage IS NULL THEN
        RAISE EXCEPTION 'Different rice varieties cannot be mixed' USING ERRCODE='23514';
    END IF;
    policy:=grainledger.ledger_effective_byproduct_variety_policy(owner,stage);
    IF NOT (policy->>'allowCrossVariety')::boolean THEN
        RAISE EXCEPTION 'Company policy forbids cross-variety mixing for %',stage USING ERRCODE='23514';
    END IF;
    RETURN jsonb_build_object('varietyStatus','MIXED','riceVarietyId',NULL,'riceVariety',NULL,'varietyMixPolicy',policy);
END $$;
REVOKE ALL ON FUNCTION grainledger.ledger_effective_byproduct_variety_policy(uuid,text) FROM PUBLIC;
REVOKE ALL ON FUNCTION grainledger.ledger_resolve_lot_variety(uuid,text,uuid[]) FROM PUBLIC;

CREATE FUNCTION grainledger.ledger_byproduct_variety_policy(a uuid, action text, payload jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path TO 'pg_catalog','grainledger','pg_temp' AS $$
DECLARE company uuid; policy_stage text; current_revision uuid; expected_revision uuid; next_no bigint;
    reason_text text; new_revision uuid;
BEGIN
    IF action='write' THEN PERFORM grainledger.ledger_lock_writes(); END IF;
    SELECT u."OrganizationId" INTO company
    FROM grainledger.identity_users u JOIN grainledger.identity_organizations o ON o."Id"=u."OrganizationId"
    WHERE u."Id"=a AND u."IsActive" AND u."Role"='CustomerManager' AND o."Kind"='Customer';
    IF NOT FOUND THEN RAISE EXCEPTION 'Byproduct policy authority denied' USING ERRCODE='42501'; END IF;
    IF action='read' THEN
        IF payload IS DISTINCT FROM '{}'::jsonb THEN RAISE EXCEPTION 'Invalid policy request' USING ERRCODE='22023'; END IF;
        RETURN jsonb_build_object('companyId',company,'items',jsonb_build_array(
            grainledger.ledger_effective_byproduct_variety_policy(company,'BRAN') - 'companyId',
            grainledger.ledger_effective_byproduct_variety_policy(company,'HUSK') - 'companyId'));
    END IF;
    IF action IS DISTINCT FROM 'write' OR jsonb_typeof(payload) IS DISTINCT FROM 'object'
        OR NOT (payload ?& ARRAY['stage','allowCrossVariety','expectedRevision','reason'])
        OR (payload - ARRAY['stage','allowCrossVariety','expectedRevision','reason']) <> '{}'::jsonb
        OR jsonb_typeof(payload->'stage') IS DISTINCT FROM 'string'
        OR payload->>'stage' NOT IN ('BRAN','HUSK')
        OR jsonb_typeof(payload->'allowCrossVariety') NOT IN ('boolean','null')
        OR jsonb_typeof(payload->'expectedRevision') NOT IN ('string','null')
        OR jsonb_typeof(payload->'reason') IS DISTINCT FROM 'string'
    THEN RAISE EXCEPTION 'Invalid policy request' USING ERRCODE='22023'; END IF;
    policy_stage:=payload->>'stage';
    reason_text:=btrim(payload->>'reason');
    IF length(reason_text) NOT BETWEEN 1 AND 500 THEN RAISE EXCEPTION 'Policy reason must be 1–500 characters' USING ERRCODE='22023'; END IF;
    IF jsonb_typeof(payload->'expectedRevision')='string' THEN
        expected_revision:=(payload->>'expectedRevision')::uuid;
        IF expected_revision='00000000-0000-0000-0000-000000000000'::uuid THEN RAISE EXCEPTION 'Invalid policy revision' USING ERRCODE='22023'; END IF;
    END IF;
    SELECT revision,change_no+1 INTO current_revision,next_no FROM grainledger.ledger_byproduct_variety_policy_revisions
        WHERE company_id=company AND stage=policy_stage ORDER BY change_no DESC LIMIT 1;
    IF current_revision IS DISTINCT FROM expected_revision THEN
        RAISE EXCEPTION 'Byproduct variety policy changed; reload before saving' USING ERRCODE='23514';
    END IF;
    new_revision:=gen_random_uuid();
    INSERT INTO grainledger.ledger_byproduct_variety_policy_revisions(company_id,stage,revision,change_no,allow_cross_variety,actor_id,reason)
        VALUES(company,policy_stage,new_revision,coalesce(next_no,1),(payload->>'allowCrossVariety')::boolean,a,reason_text);
    RETURN grainledger.ledger_effective_byproduct_variety_policy(company,policy_stage) - 'companyId';
END $$;
REVOKE ALL ON FUNCTION grainledger.ledger_byproduct_variety_policy(uuid,text,jsonb) FROM PUBLIC;
CREATE OR REPLACE FUNCTION grainledger.ledger_accept_arrival(r uuid) RETURNS void
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE h grainledger.capture_procurements; s grainledger.ledger_receiving; ms grainledger.ledger_arrival_measurement_sets; snap jsonb; commodity uuid; c uuid; loc uuid; row record; event_id uuid; review grainledger.ledger_arrival_reviews; code text; item jsonb;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    SELECT * INTO STRICT h FROM grainledger.capture_procurements WHERE id=r;
    SELECT * INTO STRICT s FROM grainledger.ledger_receiving WHERE procurement_id=r AND accepted_measurement_set_id IS NOT NULL;
    SELECT * INTO STRICT ms FROM grainledger.ledger_arrival_measurement_sets WHERE id=s.accepted_measurement_set_id;
    SELECT * INTO review FROM grainledger.ledger_arrival_reviews WHERE id=s.accepted_review_id;
    SELECT payload->'result' INTO snap FROM grainledger.capture_business_events WHERE id=h.event_id;
    c:=(snap->>'CommodityId')::uuid; loc:=(snap->>'ReceivingLocationId')::uuid;
    PERFORM grainledger.ledger_require_active_location(loc);
    IF EXISTS(SELECT 1 FROM grainledger.ledger_lots WHERE receipt_id=r) THEN RAISE EXCEPTION 'Arrival already posted' USING ERRCODE='23514'; END IF;
    IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_locations WHERE id=loc AND facility_id=h.collection_site_id AND kind IN ('STORE','SILO','COMPARTMENT') AND (owner_id=h.company_id OR owner_id IS NULL AND (kind<>'COMPARTMENT' OR cleared))) THEN RAISE EXCEPTION 'Receiving location no longer eligible' USING ERRCODE='23514'; END IF;
    SELECT ledger_commodity_id INTO commodity FROM grainledger.ledger_catalog_commodities WHERE catalog_commodity_id=c;
    IF NOT FOUND THEN PERFORM grainledger.ledger_provision_catalog_commodity(c); SELECT ledger_commodity_id INTO STRICT commodity FROM grainledger.ledger_catalog_commodities WHERE catalog_commodity_id=c; END IF;
    PERFORM grainledger.ledger_credit_location(loc,h.company_id);
    FOR row IN SELECT i.*,sl.id source_id,sl.lot_id,ps.plot_id,ps.season_id,ps.rice_variety,m.actual_kg,m.measured_value actual_value,m.measured_unit actual_unit
        FROM grainledger.capture_procurement_items i JOIN grainledger.capture_source_lots sl ON sl.procurement_item_id=i.id JOIN grainledger.capture_plot_seasons ps ON ps.id=i.plot_season_id
        JOIN grainledger.ledger_arrival_measurements m ON m.procurement_item_id=i.id AND m.measurement_set_id=ms.id WHERE i.procurement_id=r ORDER BY i.line_no
    LOOP
        SELECT x INTO STRICT item FROM jsonb_array_elements(snap->'Items') x WHERE (x->>'Id')::uuid=row.id;
        code:=grainledger.ledger_source_lot_code(r,row.lot_id);
        INSERT INTO grainledger.ledger_lots(id,code,owner_id,commodity_id,stage,product_identity,receipt_id,receipt_line_id,code_origin_id,variety_status,rice_variety_id,rice_variety_name)
        VALUES(row.lot_id,code,h.company_id,commodity,'RAW_PADDY','RAW_PADDY',r,row.id,grainledger.ledger_register_code(code,row.lot_id,NULL,NULL),
            CASE WHEN item->>'RiceVarietyId' IS NULL THEN 'UNKNOWN' ELSE 'SINGLE' END,
            (item->>'RiceVarietyId')::uuid,CASE WHEN item->>'RiceVarietyId' IS NOT NULL THEN item->>'RiceVariety' END);
        event_id:=gen_random_uuid();
        INSERT INTO grainledger.ledger_events(id,kind,actor_id,lot_id,details,occurred_at) VALUES(event_id,'RECEIPT',ms.operator_id,row.lot_id,
            jsonb_build_object('receiptId',r,'receiptLineId',row.id,'captureEventId',h.event_id,'sourceLotId',row.source_id,'catalogCommodityId',c,
                'farmerId',h.farmer_id,'plotSeasonId',row.plot_season_id,'plotId',row.plot_id,'seasonId',row.season_id,'riceVariety',item->>'RiceVariety','riceVarietyId',(item->>'RiceVarietyId')::uuid,
                'originalQuantity',row.measured_value::text,'originalUnit',row.measured_unit,'sourceQuantityKg',row.net_mass_kg::text,
                'arrivalQuantity',row.actual_value::text,'arrivalUnit',row.actual_unit,'quantityKg',row.actual_kg::text,'moisturePercent',row.moisture_percent::text,
                'measurementSetId',ms.id,'managerReviewId',review.id,'managerId',review.manager_id,'evidenceId',review.evidence_id,'receivingStatus',s.status),ms.captured_at);
        PERFORM grainledger.ledger_post(event_id,row.lot_id,loc,row.actual_kg);
        INSERT INTO grainledger.ledger_contributions VALUES(row.lot_id,row.lot_id,row.actual_kg);
        PERFORM grainledger.ledger_pool_ingress(COALESCE(review.manager_id,ms.operator_id),COALESCE(review.operation_id,ms.operation_id),
            'ARRIVAL',loc,row.lot_id,row.actual_kg);
    END LOOP;
END $$;

CREATE OR REPLACE FUNCTION grainledger.ledger_procurement_package_complete(r uuid) RETURNS boolean
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE h grainledger.capture_procurements; ev grainledger.capture_business_events; snapshot jsonb; state grainledger.ledger_receiving; row record; item jsonb; loc uuid; commodity uuid; n integer; actual numeric; applied grainledger.ledger_arrival_measurement_sets;
BEGIN
    SELECT * INTO h FROM grainledger.capture_procurements WHERE id=r;
    IF NOT FOUND THEN RETURN false; END IF;
    IF h.receiving_workflow IS NULL THEN RETURN grainledger.ledger_legacy_procurement_package_complete(r); END IF;
    SELECT * INTO ev FROM grainledger.capture_business_events WHERE id=h.event_id;
    SELECT * INTO state FROM grainledger.ledger_receiving WHERE procurement_id=r;
    IF ev.id IS NULL OR state.procurement_id IS NULL OR h.status<>'COMMITTED' OR ev.event_type<>'PROCUREMENT_COMMITTED'
       OR ev.company_id IS DISTINCT FROM h.company_id OR ev.actor_id IS DISTINCT FROM h.received_by OR ev.occurred_at<>h.received_at THEN RETURN false; END IF;
    snapshot:=ev.payload->'result'; loc:=(snapshot->>'ReceivingLocationId')::uuid; commodity:=(snapshot->>'CommodityId')::uuid;
    SELECT count(*) INTO n FROM grainledger.capture_procurement_items WHERE procurement_id=r;
    IF snapshot IS NULL OR n NOT BETWEEN 1 AND 100 OR jsonb_typeof(snapshot->'Items') IS DISTINCT FROM 'array'
       OR jsonb_array_length(snapshot->'Items')<>n OR (SELECT count(DISTINCT (x->>'Id')::uuid) FROM jsonb_array_elements(snapshot->'Items') x)<>n
       OR (snapshot->>'Id')::uuid IS DISTINCT FROM h.id OR snapshot->>'Code' IS DISTINCT FROM h.code
       OR (snapshot->>'FarmerId')::uuid IS DISTINCT FROM h.farmer_id OR (snapshot->>'CollectionSiteId')::uuid IS DISTINCT FROM h.collection_site_id
       OR (snapshot->>'ReceivedBy')::uuid IS DISTINCT FROM h.received_by OR (snapshot->>'EventId')::uuid IS DISTINCT FROM ev.id
       OR (snapshot->>'ReceivedAt')::timestamptz IS DISTINCT FROM h.received_at OR (snapshot->>'RecordedAt')::timestamptz IS DISTINCT FROM ev.recorded_at
       OR snapshot->>'Status' IS DISTINCT FROM 'PENDING_ARRIVAL' OR (snapshot->>'ReceivingVersion')::integer IS DISTINCT FROM 0
       OR (snapshot->>'TotalNetMassKg')::numeric IS DISTINCT FROM (SELECT sum(net_mass_kg) FROM grainledger.capture_procurement_items WHERE procurement_id=r)
       OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_commodities WHERE id=commodity AND code='PADDY')
       OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_locations WHERE id=loc AND facility_id=h.collection_site_id)
       OR NOT EXISTS(SELECT 1 FROM grainledger.capture_offline_operations o WHERE o.applied_event_id=ev.id AND o.company_id=h.company_id
           AND o.submitted_by=h.received_by AND o.status='APPLIED' AND o.client_operation_id=ev.correlation_id AND o.result_payload=snapshot
           AND o.captured_at=(snapshot->>'CapturedAt')::timestamptz AND o.payload->>'operationType'='PROCUREMENT_COMMITTED') THEN RETURN false; END IF;
    IF (SELECT count(*) FROM grainledger.ledger_lots WHERE receipt_id=r)<>(CASE WHEN state.accepted_measurement_set_id IS NULL THEN 0 ELSE n END) THEN RETURN false; END IF;
    IF state.latest_measurement_set_id IS NOT NULL AND (SELECT count(*) FROM grainledger.ledger_arrival_measurements WHERE measurement_set_id=state.latest_measurement_set_id)<>n THEN RETURN false; END IF;
    IF state.accepted_measurement_set_id IS NOT NULL AND state.accepted_measurement_set_id IS DISTINCT FROM state.latest_measurement_set_id THEN RETURN false; END IF;
    IF (state.status='PENDING_ARRIVAL') IS DISTINCT FROM (state.version=0 AND state.latest_measurement_set_id IS NULL)
       OR EXISTS(SELECT 1 FROM grainledger.ledger_arrival_measurement_sets ms WHERE ms.procurement_id=r AND
           ((SELECT count(*) FROM grainledger.ledger_arrival_measurements WHERE measurement_set_id=ms.id)<>n
            OR (ms.threshold_revision IS NOT NULL AND (ms.threshold_company_id IS DISTINCT FROM h.company_id
                OR grainledger.ledger_receiving_threshold_snapshot_valid(ms.threshold_company_id,ms.threshold_percentage,ms.threshold_source,ms.threshold_revision) IS DISTINCT FROM true))))
       OR EXISTS(SELECT 1 FROM grainledger.ledger_arrival_measurements m JOIN grainledger.ledger_arrival_measurement_sets ms ON ms.id=m.measurement_set_id
           JOIN grainledger.capture_procurement_items i ON i.id=m.procurement_item_id WHERE ms.procurement_id=r AND i.procurement_id<>r)
       OR EXISTS(SELECT 1 FROM grainledger.ledger_arrival_reviews v JOIN grainledger.ledger_evidence e ON e.id=v.evidence_id
           WHERE v.procurement_id=r AND (e.owner_id<>h.company_id OR e.facility_id<>h.collection_site_id)) THEN RETURN false; END IF;
    SELECT * INTO applied FROM grainledger.ledger_arrival_measurement_sets WHERE id=state.accepted_measurement_set_id;
    IF state.status='RECEIVED_WITHIN_TOLERANCE' AND
       (applied.threshold_revision IS NULL OR applied.threshold_company_id IS DISTINCT FROM h.company_id
        OR grainledger.ledger_receiving_threshold_snapshot_valid(applied.threshold_company_id,applied.threshold_percentage,applied.threshold_source,applied.threshold_revision) IS DISTINCT FROM true
        OR state.accepted_review_id IS NOT NULL
        OR EXISTS(SELECT 1 FROM grainledger.ledger_arrival_reviews WHERE measurement_set_id=state.accepted_measurement_set_id AND action='APPROVE_SHORTAGE')
        OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_arrival_measurements m JOIN grainledger.capture_procurement_items i ON i.id=m.procurement_item_id
            WHERE m.measurement_set_id=state.accepted_measurement_set_id AND m.actual_kg<i.net_mass_kg)) THEN RETURN false; END IF;
    FOR row IN SELECT i.*,sl.id source_id,sl.lot_id,ps.plot_id,ps.season_id,ps.rice_variety,ps.rice_variety_id,p.farmer_id
        FROM grainledger.capture_procurement_items i LEFT JOIN grainledger.capture_source_lots sl ON sl.procurement_item_id=i.id AND sl.company_id=i.company_id
        JOIN grainledger.capture_plot_seasons ps ON ps.id=i.plot_season_id AND ps.company_id=i.company_id
        JOIN grainledger.capture_plots p ON p.id=ps.plot_id AND p.company_id=ps.company_id
        JOIN grainledger.capture_crop_seasons s ON s.id=ps.season_id AND s.company_id=ps.company_id WHERE i.procurement_id=r
    LOOP
        SELECT x INTO item FROM jsonb_array_elements(snapshot->'Items') x WHERE (x->>'Id')::uuid=row.id;
        IF row.company_id<>h.company_id OR row.farmer_id<>h.farmer_id OR row.source_id IS NULL OR row.line_no NOT BETWEEN 1 AND n
           OR (item->>'LineNo')::integer IS DISTINCT FROM row.line_no OR (item->>'PlotSeasonId')::uuid IS DISTINCT FROM row.plot_season_id
           OR (item->>'PlotId')::uuid IS DISTINCT FROM row.plot_id OR (item->>'SeasonId')::uuid IS DISTINCT FROM row.season_id
           OR (CASE WHEN item->>'RiceVarietyId' IS NULL THEN item->>'RiceVariety' IS DISTINCT FROM row.rice_variety
                ELSE (item->>'RiceVarietyId')::uuid IS DISTINCT FROM row.rice_variety_id OR NULLIF(btrim(item->>'RiceVariety'),'') IS NULL END) OR (item->>'SourceLotId')::uuid IS DISTINCT FROM row.source_id
           OR (item->>'LotId')::uuid IS DISTINCT FROM row.lot_id OR item->>'LotCode' IS DISTINCT FROM grainledger.ledger_source_lot_code(r,row.lot_id)
           OR item->>'Stage' IS DISTINCT FROM 'RAW_PADDY' OR (item->>'MeasuredValue')::numeric IS DISTINCT FROM row.measured_value
           OR item->>'MeasuredUnit' IS DISTINCT FROM row.measured_unit OR (item->>'NetMassKg')::numeric IS DISTINCT FROM row.net_mass_kg
           OR (item->>'MoisturePercent')::numeric IS DISTINCT FROM row.moisture_percent OR (item->'InitialStock'->>'LocationId')::uuid IS DISTINCT FROM loc
           OR (item->'InitialStock'->>'ReceivedKg')::numeric IS DISTINCT FROM 0
           OR row.net_mass_kg IS DISTINCT FROM (CASE row.measured_unit WHEN 'KG' THEN row.measured_value WHEN 'G' THEN row.measured_value/1000 WHEN 'T' THEN row.measured_value*1000 END)
           OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_measurement_units WHERE code=row.measured_unit AND quantity_kind='MASS') THEN RETURN false; END IF;
        IF state.accepted_measurement_set_id IS NULL THEN
            IF EXISTS(SELECT 1 FROM grainledger.ledger_lots WHERE id=row.lot_id) THEN RETURN false; END IF;
        ELSE
            SELECT actual_kg INTO actual FROM grainledger.ledger_arrival_measurements WHERE measurement_set_id=state.accepted_measurement_set_id AND procurement_item_id=row.id;
            IF actual IS NULL OR actual<=0 OR (state.status='RECEIVED_MATCHED' AND actual<>row.net_mass_kg)
               OR (state.status='RECEIVED_SHORTAGE_APPROVED' AND actual>row.net_mass_kg)
               OR (state.status='RECEIVED_WITHIN_TOLERANCE' AND (actual>row.net_mass_kg OR (row.net_mass_kg-actual)*100>row.net_mass_kg*applied.threshold_percentage))
               OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_lots l JOIN grainledger.ledger_catalog_commodities m ON m.ledger_commodity_id=l.commodity_id
                    WHERE l.id=row.lot_id AND l.owner_id=h.company_id AND l.receipt_id=r AND l.receipt_line_id=row.id
                    AND (item->>'RiceVarietyId' IS NULL AND l.variety_status='UNKNOWN' OR item->>'RiceVarietyId' IS NOT NULL AND l.variety_status='SINGLE' AND l.rice_variety_id=(item->>'RiceVarietyId')::uuid AND l.rice_variety_name=item->>'RiceVariety')
                    AND l.code=item->>'LotCode' AND l.stage='RAW_PADDY' AND l.product_identity='RAW_PADDY' AND m.catalog_commodity_id=commodity)
               OR (SELECT count(*) FROM grainledger.ledger_events WHERE lot_id=row.lot_id AND kind='RECEIPT')<>1
               OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_events e JOIN grainledger.ledger_postings p ON p.event_id=e.id JOIN grainledger.ledger_arrival_measurement_sets ms ON ms.id=state.accepted_measurement_set_id
                    JOIN grainledger.ledger_arrival_measurements am ON am.measurement_set_id=ms.id AND am.procurement_item_id=row.id
                    LEFT JOIN grainledger.ledger_arrival_reviews rv ON rv.id=state.accepted_review_id
                    WHERE e.lot_id=row.lot_id AND e.kind='RECEIPT' AND e.actor_id=ms.operator_id AND e.occurred_at=ms.captured_at
                    AND e.details->>'measurementSetId'=state.accepted_measurement_set_id::text AND e.details->>'receiptId'=r::text
                    AND e.details->>'receiptLineId'=row.id::text AND e.details->>'sourceLotId'=row.source_id::text
                    AND (e.details->>'quantityKg')::numeric=actual AND (e.details->>'sourceQuantityKg')::numeric=row.net_mass_kg
                    AND e.details->>'captureEventId'=ev.id::text AND e.details->>'catalogCommodityId'=commodity::text
                    AND e.details->>'farmerId'=h.farmer_id::text AND e.details->>'plotSeasonId'=row.plot_season_id::text
                    AND e.details->>'plotId'=row.plot_id::text AND e.details->>'seasonId'=row.season_id::text
                    AND e.details->>'riceVariety'=item->>'RiceVariety' AND (e.details->>'riceVarietyId')::uuid IS NOT DISTINCT FROM (item->>'RiceVarietyId')::uuid AND (e.details->>'originalQuantity')::numeric=row.measured_value
                    AND e.details->>'originalUnit'=row.measured_unit AND (e.details->>'moisturePercent')::numeric=row.moisture_percent
                    AND (e.details->>'arrivalQuantity')::numeric=am.measured_value AND e.details->>'arrivalUnit'=am.measured_unit
                    AND (e.details->>'managerReviewId')::uuid IS NOT DISTINCT FROM state.accepted_review_id
                    AND (e.details->>'managerId')::uuid IS NOT DISTINCT FROM rv.manager_id
                    AND (e.details->>'evidenceId')::uuid IS NOT DISTINCT FROM rv.evidence_id
                    AND e.details->>'receivingStatus'=state.status
                    AND p.lot_id=row.lot_id AND p.location_id=loc AND p.quantity_kg=actual AND p.reverses_posting_id IS NULL
                    AND (SELECT count(*) FROM grainledger.ledger_postings WHERE event_id=e.id)=1)
               OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_contributions WHERE lot_id=row.lot_id AND source_lot_id=row.lot_id AND quantity_kg=actual)
               OR (SELECT count(*) FROM grainledger.ledger_contributions WHERE lot_id=row.lot_id)<>1 THEN RETURN false; END IF;
        END IF;
    END LOOP;
    IF state.status='RECEIVED_SHORTAGE_APPROVED' AND (NOT EXISTS(SELECT 1 FROM grainledger.ledger_arrival_measurements m JOIN grainledger.capture_procurement_items i ON i.id=m.procurement_item_id WHERE m.measurement_set_id=state.accepted_measurement_set_id AND m.actual_kg<i.net_mass_kg)
       OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_arrival_reviews v JOIN grainledger.ledger_evidence e ON e.id=v.evidence_id WHERE v.id=state.accepted_review_id AND v.action='APPROVE_SHORTAGE' AND v.measurement_set_id=state.accepted_measurement_set_id AND e.owner_id=h.company_id AND e.facility_id=h.collection_site_id)) THEN RETURN false; END IF;
    RETURN true;
END $$;

CREATE OR REPLACE FUNCTION grainledger.ledger_receiving_detail(a uuid, r uuid) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE h grainledger.capture_procurements; s grainledger.ledger_receiving; snap jsonb; result jsonb; effective jsonb;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    SELECT * INTO STRICT h FROM grainledger.capture_procurements WHERE id=r AND receiving_workflow IS NOT NULL;
    PERFORM grainledger.ledger_require_staff(a,h.collection_site_id,ARRAY['MillOperator','MillManager']);
    SELECT * INTO STRICT s FROM grainledger.ledger_receiving WHERE procurement_id=r;
    SELECT payload->'result' INTO snap FROM grainledger.capture_business_events WHERE id=h.event_id;
    PERFORM grainledger.ledger_require_active_location((snap->>'ReceivingLocationId')::uuid);
    effective:=grainledger.ledger_receiving_threshold_effective(h.company_id);
    SELECT jsonb_build_object('id',h.id,'code',h.code,'ownerCompanyId',h.company_id,'ownerCompanyName',o."Name",
        'facilityId',h.collection_site_id,'facilityName',f.name,'receivingLocationId',l.id,'receivingLocationName',l.name,
        'status',s.status,'receivingVersion',s.version,'receivedAt',h.received_at,'expectedTotalKg',snap->>'TotalNetMassKg',
        'effectiveThreshold',effective,
        'actualTotalKg',(SELECT sum(actual_kg)::text FROM grainledger.ledger_arrival_measurements WHERE measurement_set_id=s.latest_measurement_set_id),
        'usableStockKg',CASE WHEN s.accepted_measurement_set_id IS NULL THEN '0' ELSE COALESCE((
            SELECT sum(stock.available_kg)::text
            FROM grainledger.ledger_procurement_stock_bulk(h.company_id,(
                SELECT array_agg(sl.lot_id) FROM grainledger.capture_source_lots sl
                JOIN grainledger.capture_procurement_items i ON i.id=sl.procurement_item_id
                WHERE i.procurement_id=r AND sl.company_id=h.company_id
            )) stock
        ),'0') END,
        'latestMeasurementSetId',s.latest_measurement_set_id,
        'items',(SELECT jsonb_agg(jsonb_build_object('id',i.id,'lineNo',i.line_no,'sourceLotId',sl.id,'lotId',sl.lot_id,'lotCode',grainledger.ledger_source_lot_code(r,sl.lot_id),
            'plotSeasonId',i.plot_season_id,'riceVariety',item.value->>'RiceVariety','riceVarietyId',(item.value->>'RiceVarietyId')::uuid,'expectedValue',i.measured_value::text,'expectedUnit',i.measured_unit,'expectedKg',i.net_mass_kg::text,
            'actualValue',m.measured_value::text,'actualUnit',m.measured_unit,'actualKg',m.actual_kg::text) ORDER BY i.line_no)
            FROM grainledger.capture_procurement_items i JOIN grainledger.capture_source_lots sl ON sl.procurement_item_id=i.id JOIN LATERAL (SELECT x value FROM jsonb_array_elements(snap->'Items') x WHERE (x->>'Id')::uuid=i.id) item ON true
            LEFT JOIN grainledger.ledger_arrival_measurements m ON m.procurement_item_id=i.id AND m.measurement_set_id=s.latest_measurement_set_id WHERE i.procurement_id=r),
        'measurementSets',COALESCE((SELECT jsonb_agg(jsonb_build_object('id',ms.id,'operationId',ms.operation_id,'operatorId',ms.operator_id,'capturedAt',ms.captured_at,'recordedAt',ms.recorded_at,'observations',ms.observations,
            'threshold',CASE WHEN ms.threshold_revision IS NULL THEN NULL ELSE jsonb_build_object('companyId',ms.threshold_company_id,'percentage',ms.threshold_percentage::text,'source',ms.threshold_source,'revision',ms.threshold_revision) END,
            'items',(SELECT jsonb_agg(jsonb_build_object('procurementItemId',m.procurement_item_id,'measuredValue',m.measured_value::text,'measuredUnit',m.measured_unit,'actualKg',m.actual_kg::text) ORDER BY i.line_no) FROM grainledger.ledger_arrival_measurements m JOIN grainledger.capture_procurement_items i ON i.id=m.procurement_item_id WHERE m.measurement_set_id=ms.id)) ORDER BY ms.recorded_at,ms.id) FROM grainledger.ledger_arrival_measurement_sets ms WHERE ms.procurement_id=r),'[]'::jsonb),
        'reviews',COALESCE((SELECT jsonb_agg(jsonb_build_object('id',v.id,'operationId',v.operation_id,'measurementSetId',v.measurement_set_id,'action',v.action,'reason',v.reason,'evidenceId',v.evidence_id,'managerId',v.manager_id,'recordedAt',v.recorded_at) ORDER BY v.recorded_at,v.id) FROM grainledger.ledger_arrival_reviews v WHERE v.procurement_id=r),'[]'::jsonb)) INTO result
    FROM grainledger.identity_organizations o JOIN grainledger.ledger_facilities f ON f.id=h.collection_site_id JOIN grainledger.ledger_locations l ON l.id=(snap->>'ReceivingLocationId')::uuid WHERE o."Id"=h.company_id;
    RETURN result;
END $$;

CREATE OR REPLACE FUNCTION grainledger.ledger_register_pending_procurement(a uuid, r uuid) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE h grainledger.capture_procurements; snapshot jsonb;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    SELECT * INTO STRICT h FROM grainledger.capture_procurements WHERE id=r AND receiving_workflow='PHYSICAL_ARRIVAL_V1';
    IF grainledger.ledger_role(a) IS DISTINCT FROM 'PurchasingStaff' OR NOT grainledger.ledger_own(a,h.company_id) OR h.received_by<>a THEN RAISE EXCEPTION 'Procurement company authority denied' USING ERRCODE='42501'; END IF;
    IF NOT EXISTS(SELECT 1 FROM grainledger.identity_organizations WHERE "Id"=h.company_id AND "Kind"='Customer') THEN RAISE EXCEPTION 'Customer procurement owner required' USING ERRCODE='42501'; END IF;
    SELECT payload->'result' INTO snapshot FROM grainledger.capture_business_events WHERE id=h.event_id;
    IF NOT EXISTS(SELECT 1 FROM grainledger.catalog_commodities WHERE id=(snapshot->>'CommodityId')::uuid AND code='PADDY' AND NOT is_archived) THEN
        RAISE EXCEPTION 'Active PADDY commodity required for new procurement' USING ERRCODE='23514';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_locations WHERE id=(snapshot->>'ReceivingLocationId')::uuid AND facility_id=h.collection_site_id AND NOT is_deleted
        AND kind IN ('STORE','SILO','COMPARTMENT') AND (owner_id=h.company_id OR owner_id IS NULL AND (kind<>'COMPARTMENT' OR cleared)))
        OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_facilities WHERE id=h.collection_site_id AND NOT is_deleted) THEN
        RAISE EXCEPTION 'Receiving location no longer eligible' USING ERRCODE='23514';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM grainledger.capture_farmers WHERE id=h.farmer_id AND company_id=h.company_id AND NOT is_deleted)
        OR EXISTS(SELECT 1 FROM grainledger.capture_procurement_items i WHERE i.procurement_id=r AND NOT EXISTS(
            SELECT 1 FROM grainledger.capture_plot_seasons ps JOIN grainledger.capture_plots p ON p.id=ps.plot_id AND p.company_id=ps.company_id
            JOIN grainledger.capture_crop_seasons season ON season.id=ps.season_id AND season.company_id=ps.company_id
            WHERE ps.id=i.plot_season_id AND ps.company_id=h.company_id AND p.farmer_id=h.farmer_id
                AND NOT ps.is_deleted AND NOT p.is_deleted AND NOT season.is_deleted
        )) THEN
        RAISE EXCEPTION 'Active farmer, plot and season references required for new procurement' USING ERRCODE='23514';
    END IF;
    IF EXISTS(SELECT 1 FROM grainledger.capture_procurement_items i WHERE i.procurement_id=r AND NOT EXISTS(
        SELECT 1 FROM grainledger.catalog_measurement_units u WHERE u.code=i.measured_unit AND u.quantity_kind='MASS' AND NOT u.is_deleted
    )) THEN
        RAISE EXCEPTION 'Active mass units required for new procurement' USING ERRCODE='23514';
    END IF;
    IF EXISTS(SELECT 1 FROM grainledger.capture_procurement_items i
        JOIN grainledger.capture_plot_seasons ps ON ps.id=i.plot_season_id
        LEFT JOIN LATERAL (SELECT x FROM jsonb_array_elements(snapshot->'Items') x WHERE (x->>'Id')::uuid=i.id) item ON true
        WHERE i.procurement_id=r AND (ps.rice_variety_id IS NULL OR (item.x->>'RiceVarietyId')::uuid IS DISTINCT FROM ps.rice_variety_id
            OR NULLIF(btrim(item.x->>'RiceVariety'),'') IS NULL)) THEN
        RAISE EXCEPTION 'Procurement rice variety snapshot invalid' USING ERRCODE='23514';
    END IF;
    IF (SELECT count(*) FROM grainledger.capture_procurement_items WHERE procurement_id=r)>1 THEN
        IF EXISTS(SELECT 1 FROM grainledger.ledger_locations
            WHERE id=(snapshot->>'ReceivingLocationId')::uuid AND kind IN ('SILO','COMPARTMENT') AND NOT mixed_bulk) THEN
            RAISE EXCEPTION 'Choose separated storage or register one line for this bulk location' USING ERRCODE='23514';
        END IF;
        IF EXISTS(SELECT 1 FROM grainledger.ledger_locations
            WHERE id=(snapshot->>'ReceivingLocationId')::uuid AND mixed_bulk)
            AND (SELECT count(DISTINCT x->>'RiceVarietyId') FROM jsonb_array_elements(snapshot->'Items') x)>1 THEN
            RAISE EXCEPTION 'Different rice varieties cannot be mixed' USING ERRCODE='23514';
        END IF;
    END IF;
    INSERT INTO grainledger.ledger_receiving(procurement_id,status) VALUES(r,'PENDING_ARRIVAL');
    IF grainledger.ledger_procurement_package_complete(r) IS DISTINCT FROM true THEN RAISE EXCEPTION 'Pending source package incomplete' USING ERRCODE='23514'; END IF;
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
    variety jsonb;
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

    SELECT grainledger.ledger_resolve_lot_variety(operation.owner_id,
        CASE WHEN operation.kind='MERGE' AND first_stage IN ('BRAN','HUSK') AND first_product=first_stage THEN first_stage END,
        array_agg(DISTINCT (value->>'actualLotId')::uuid)) INTO variety
    FROM jsonb_array_elements(resolved_inputs);
    IF variety->>'varietyStatus'='UNKNOWN' AND operation.kind NOT IN ('DRYING','MILLING','BAGGING') THEN
        RAISE EXCEPTION 'Rice variety is unknown' USING ERRCODE='23514';
    END IF;
    IF variety->>'varietyStatus'='MIXED' AND NOT (operation.kind='MERGE' AND first_stage IN ('BRAN','HUSK') AND first_product=first_stage) THEN
        RAISE EXCEPTION 'Different rice varieties cannot be mixed' USING ERRCODE='23514';
    END IF;

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
    ) || CASE WHEN variety->'varietyMixPolicy' <> 'null'::jsonb THEN jsonb_build_object('varietyMixPolicy',variety->'varietyMixPolicy') ELSE '{}'::jsonb END,operation.occurred_at);
    FOR allocation IN SELECT value FROM jsonb_array_elements(resolved_inputs) LOOP
        PERFORM grainledger.ledger_post(event_id,(allocation->>'lotId')::uuid,
                            (allocation->>'locationId')::uuid,-grainledger.ledger_kg(allocation->>'quantityKg'));
    END LOOP;
    FOR output IN SELECT value FROM jsonb_array_elements(operation.content->'outputs') LOOP
        PERFORM grainledger.ledger_credit_location((output->>'locationId')::uuid,operation.owner_id);
        INSERT INTO grainledger.ledger_lots(id,code,owner_id,commodity_id,stage,product_identity,operation_id,code_origin_id,variety_status,rice_variety_id,rice_variety_name)
        VALUES((output->>'id')::uuid,output->>'code',operation.owner_id,
               (output->>'commodityId')::uuid,output->>'stage',output->>'productIdentity',op,
               grainledger.ledger_register_code(output->>'code',(output->>'id')::uuid,NULL,original_operation),
               variety->>'varietyStatus',(variety->>'riceVarietyId')::uuid,variety->>'riceVariety');
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

CREATE OR REPLACE FUNCTION grainledger.ledger_reserve_approved_blend(a uuid, proposal uuid) RETURNS integer
LANGUAGE plpgsql SECURITY DEFINER SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
AS $$
DECLARE approved record; allocation record; available numeric; allocation_count integer := 0; variety jsonb;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    SELECT p.company_id, (v.request_json->>'FacilityId')::uuid AS facility_id,
           p.snapshot_json->'selection'->'allocations' AS allocations
    INTO approved
    FROM grainledger.exports_blend_plan_proposals p
    JOIN grainledger.exports_blend_plan_previews v ON v.id = p.preview_id AND v.company_id = p.company_id
    JOIN grainledger.exports_active_blend_approvals active ON active.proposal_id = p.id AND active.company_id = p.company_id
    JOIN grainledger.exports_blend_plan_decisions d ON d.proposal_id = p.id AND d.version = 1
    WHERE p.id = proposal AND d.action = 'APPROVED' AND d.actor_id = a;
    IF NOT FOUND OR grainledger.ledger_role(a) IS DISTINCT FROM 'CustomerManager'
       OR NOT COALESCE(grainledger.ledger_own(a,approved.company_id),false) THEN
        RAISE EXCEPTION 'Blend reservation access denied' USING ERRCODE='42501';
    END IF;
    IF approved.facility_id IS NULL OR jsonb_typeof(approved.allocations) IS DISTINCT FROM 'array' THEN
        RAISE EXCEPTION 'Missing approved stock allocations' USING ERRCODE='23514';
    END IF;
    FOR allocation IN
        SELECT x."lotId" AS lot_id, x."locationId" AS location_id, x."quantityKg" AS quantity_kg
        FROM jsonb_to_recordset(approved.allocations) AS x("lotId" uuid, "locationId" uuid, "quantityKg" numeric)
        ORDER BY 1, 2
    LOOP
        IF allocation.quantity_kg IS NULL OR allocation.quantity_kg <= 0
            OR allocation.quantity_kg > 99999999999999.999999
            OR trunc(allocation.quantity_kg,6) <> allocation.quantity_kg THEN
            RAISE EXCEPTION 'Invalid approved stock quantity' USING ERRCODE='23514';
        END IF;
        SELECT pos.quantity_kg - grainledger.ledger_blend_reserved_kg(pos.lot_id,pos.location_id)
        INTO available
        FROM grainledger.ledger_positions pos
        JOIN grainledger.ledger_lots lot ON lot.id = pos.lot_id
        JOIN grainledger.ledger_locations loc ON loc.id = pos.location_id
        LEFT JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.location_id = loc.id
        WHERE pos.lot_id = allocation.lot_id AND pos.location_id = allocation.location_id
            AND lot.owner_id = approved.company_id AND loc.facility_id = approved.facility_id AND NOT loc.is_deleted
            AND (loc.owner_id IS NULL OR loc.owner_id = approved.company_id)
            AND (NOT loc.mixed_bulk OR pool.lot_id = pos.lot_id)
            AND NOT grainledger.ledger_container_lot_blocked(pos.lot_id)
        FOR UPDATE OF pos;
        IF NOT FOUND OR available < allocation.quantity_kg THEN
            RAISE EXCEPTION 'Approved stock position is no longer available' USING ERRCODE='23514';
        END IF;
        INSERT INTO grainledger.ledger_blend_reservations(proposal_id,lot_id,location_id,quantity_kg)
        VALUES (proposal, allocation.lot_id, allocation.location_id, allocation.quantity_kg);
        allocation_count := allocation_count + 1;
    END LOOP;
    IF allocation_count = 0 THEN
        RAISE EXCEPTION 'Missing approved stock allocations' USING ERRCODE='23514';
    END IF;
    SELECT grainledger.ledger_resolve_lot_variety(approved.company_id,NULL,array_agg(DISTINCT lot_id)) INTO variety
    FROM grainledger.ledger_blend_reservations WHERE proposal_id=proposal;
    IF variety->>'varietyStatus'='UNKNOWN' THEN
        RAISE EXCEPTION 'Rice variety is unknown' USING ERRCODE='23514';
    ELSIF variety->>'varietyStatus'<>'SINGLE' THEN
        RAISE EXCEPTION 'Different rice varieties cannot be mixed' USING ERRCODE='23514';
    END IF;
    RETURN allocation_count;
END;
$$;

CREATE OR REPLACE FUNCTION grainledger.ledger_lot_json(a uuid, l uuid, detail boolean DEFAULT false) RETURNS jsonb
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT jsonb_build_object(
        'id',x.id,'code',x.code,'ownerId',x.owner_id,'commodityId',x.commodity_id,
        'stage',x.stage,'productIdentity',x.product_identity,
        'varietyStatus',x.variety_status,'riceVarietyId',x.rice_variety_id,'riceVariety',x.rice_variety_name,
        'positions',coalesce((
            SELECT jsonb_agg(jsonb_build_object(
                'locationId',p.location_id,'facilityId',lc.facility_id,
                'quantityKg',p.quantity_kg::text,
                'availableKg',CASE WHEN grainledger.ledger_blocked(l) THEN '0.000000' ELSE GREATEST(0,p.quantity_kg-grainledger.ledger_blend_reserved_kg(p.lot_id,p.location_id))::numeric(20,6)::text END
            ) || CASE WHEN lc.mixed_bulk AND pool.lot_id=l THEN jsonb_build_object('poolRevision',pool.revision) ELSE '{}'::jsonb END)
            FROM grainledger.ledger_positions p JOIN grainledger.ledger_locations lc ON lc.id=p.location_id
            LEFT JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.location_id=lc.id
            WHERE p.lot_id=l AND (grainledger.ledger_own(a,x.owner_id) OR grainledger.ledger_assigned(a,lc.facility_id))
        ),'[]'::jsonb),
        'restrictions',to_jsonb(array_remove(ARRAY[
            CASE WHEN EXISTS(SELECT 1 FROM grainledger.ledger_effective_hold_ids(l)) THEN 'HOLD' END,
            CASE WHEN EXISTS (
                SELECT 1 FROM grainledger.ledger_corrections
                WHERE operation_id=x.operation_id AND status IN ('REQUESTED','RECONCILED')
            ) OR EXISTS (
                SELECT 1 FROM grainledger.ledger_receipt_correction_blocks WHERE receipt_id=x.receipt_id
            ) THEN 'CORRECTION' END,
            CASE WHEN grainledger.ledger_receiving_restricted(l) THEN 'RECEIVING_DISCREPANCY' END,
            CASE WHEN EXISTS (
                SELECT 1 FROM grainledger.ledger_receipt_reversals WHERE receipt_id=x.receipt_id
            ) OR EXISTS (
                SELECT 1 FROM grainledger.ledger_operations WHERE id=x.operation_id AND status='CORRECTED'
            ) THEN 'SUPERSEDED' END
        ],NULL)),
        'mixedBulk',EXISTS(SELECT 1 FROM grainledger.ledger_custody_edges ce WHERE ce.input_lot_id=l OR ce.output_lot_id=l)
            OR EXISTS(SELECT 1 FROM grainledger.ledger_positions p JOIN grainledger.ledger_locations lc ON lc.id=p.location_id WHERE p.lot_id=l AND lc.mixed_bulk),
        'poolLocationId',coalesce(
            (SELECT pool.location_id FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.lot_id=l),
            (SELECT ce.location_id FROM grainledger.ledger_custody_events ce WHERE ce.input_lot_id=l OR ce.output_lot_id=l ORDER BY ce.created_at DESC LIMIT 1)),
        'poolRevision',(SELECT pool.revision FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.lot_id=l),
        'isCurrentPool',EXISTS(SELECT 1 FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.lot_id=l),
        'custodyDerived',grainledger.ledger_mixed_bulk_custody_derived(l),
        'sourceComposition',CASE WHEN detail THEN coalesce((
            SELECT jsonb_agg(jsonb_build_object('sourceLotId',shares.source_lot_id,'sourceLotCode',source.code,'quantityKg',shares.quantity_kg::text) ORDER BY shares.source_lot_id)
            FROM (
                SELECT ca.source_lot_id,sum(ca.quantity_kg) quantity_kg FROM grainledger.ledger_custody_allocations ca
                WHERE ca.output_lot_id=l GROUP BY ca.source_lot_id
                UNION ALL
                SELECT cpa.source_lot_id,sum(cpa.quantity_kg) FROM grainledger.ledger_custody_processing_allocations cpa
                WHERE cpa.output_lot_id=l GROUP BY cpa.source_lot_id
                UNION ALL
                SELECT c.source_lot_id,sum(c.quantity_kg) FROM grainledger.ledger_contributions c
                WHERE c.lot_id=l
                  AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_custody_allocations ca WHERE ca.output_lot_id=l)
                  AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_custody_processing_allocations cpa WHERE cpa.output_lot_id=l)
                GROUP BY c.source_lot_id
            ) shares JOIN grainledger.ledger_lots source ON source.id=shares.source_lot_id
        ),'[]'::jsonb) ELSE '[]'::jsonb END
    ) || CASE WHEN detail AND grainledger.ledger_role(a)<>'MillOperator' THEN
        jsonb_build_object(
            'contributions',coalesce((
                SELECT jsonb_agg(jsonb_build_object('sourceLotId',source_lot_id,'quantityKg',quantity_kg::text))
                FROM grainledger.ledger_contributions WHERE lot_id=l
            ),'[]'::jsonb),
            'custodyLinks',coalesce((
                SELECT jsonb_agg(jsonb_build_object('eventId',links.event_id,'inputLotId',links.input_lot_id,'outputLotId',links.output_lot_id,'quantityKg',links.quantity_kg::text,'locationId',links.location_id,'poolRevision',links.revision) ORDER BY links.created_at,links.event_id)
                FROM (
                    SELECT ce.event_id,ce.input_lot_id,ce.output_lot_id,ce.quantity_kg,ce.location_id,ce.revision,ce.created_at
                    FROM grainledger.ledger_custody_events ce WHERE ce.input_lot_id=l OR ce.output_lot_id=l
                    UNION ALL
                    SELECT ce.event_id,cpa.input_lot_id,cpa.output_lot_id,cpa.quantity_kg,ce.location_id,ce.revision,ce.created_at
                    FROM grainledger.ledger_custody_processing_allocations cpa JOIN grainledger.ledger_custody_events ce ON ce.event_id=cpa.event_id
                    WHERE cpa.input_lot_id=l OR cpa.output_lot_id=l
                ) links
                WHERE grainledger.ledger_can_read_lot(a,coalesce(links.input_lot_id,links.output_lot_id))
            ),'[]'::jsonb)
        )
    ELSE '{}'::jsonb END
    FROM grainledger.ledger_lots x
    WHERE x.id=l AND grainledger.ledger_can_read_lot(a,l)
$$;

CREATE TABLE grainledger.ledger_bulk_location_last_lots (
    location_id uuid PRIMARY KEY REFERENCES grainledger.ledger_locations(id),
    lot_id uuid REFERENCES grainledger.ledger_lots(id)
);
INSERT INTO grainledger.ledger_bulk_location_last_lots(location_id)
SELECT DISTINCT l.id FROM grainledger.ledger_locations l
JOIN grainledger.ledger_postings p ON p.location_id=l.id AND p.quantity_kg>0
WHERE l.kind IN ('SILO','COMPARTMENT') OR l.mixed_bulk;
REVOKE ALL ON grainledger.ledger_bulk_location_last_lots FROM PUBLIC;

CREATE FUNCTION grainledger.ledger_bulk_refill_guard(loc uuid, incoming uuid) RETURNS void
LANGUAGE plpgsql SET search_path TO 'pg_catalog','grainledger','pg_temp' AS $$
DECLARE previous uuid;
BEGIN
    SELECT lot_id INTO previous FROM grainledger.ledger_bulk_location_last_lots WHERE location_id=loc;
    IF FOUND AND previous IS DISTINCT FROM incoming AND NOT EXISTS (
        SELECT 1 FROM grainledger.ledger_lots old_lot JOIN grainledger.ledger_lots new_lot ON new_lot.id=incoming
        WHERE old_lot.id=previous AND old_lot.owner_id=new_lot.owner_id
          AND old_lot.commodity_id=new_lot.commodity_id AND old_lot.stage=new_lot.stage
          AND old_lot.product_identity=new_lot.product_identity
          AND old_lot.variety_status='SINGLE' AND new_lot.variety_status='SINGLE'
          AND old_lot.rice_variety_id=new_lot.rice_variety_id
    ) THEN
        RAISE EXCEPTION 'Bulk location requires recorded clearance before changing contents' USING ERRCODE='23514';
    END IF;
END $$;
REVOKE ALL ON FUNCTION grainledger.ledger_bulk_refill_guard(uuid,uuid) FROM PUBLIC;

CREATE OR REPLACE FUNCTION grainledger.ledger_post(e uuid, l uuid, loc uuid, q numeric, rev uuid DEFAULT NULL::uuid) RETURNS void
LANGUAGE plpgsql SET search_path TO 'pg_catalog','grainledger','pg_temp' AS $$
DECLARE destination grainledger.ledger_locations; withdrawal boolean;
BEGIN
    PERFORM grainledger.ledger_require_active_location(loc);
    IF q>0 THEN
        SELECT * INTO STRICT destination FROM grainledger.ledger_locations WHERE id=loc FOR UPDATE;
        withdrawal:=EXISTS(SELECT 1 FROM grainledger.ledger_events WHERE id=e AND kind='POOL_WITHDRAW');
        IF destination.kind IN ('SILO','COMPARTMENT') AND NOT destination.mixed_bulk AND NOT withdrawal THEN
            IF EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE location_id=loc AND quantity_kg>0 AND lot_id<>l) THEN
                RAISE EXCEPTION 'Bulk location requires a single separate lot or an approved pool' USING ERRCODE='23514';
            END IF;
            IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE location_id=loc AND quantity_kg>0) THEN
                PERFORM grainledger.ledger_bulk_refill_guard(loc,l);
            END IF;
            INSERT INTO grainledger.ledger_bulk_location_last_lots(location_id,lot_id) VALUES(loc,l)
            ON CONFLICT(location_id) DO UPDATE SET lot_id=excluded.lot_id;
        END IF;
    END IF;
    INSERT INTO grainledger.ledger_postings(event_id,lot_id,location_id,quantity_kg,reverses_posting_id)
    VALUES(e,l,loc,q,rev);
    UPDATE grainledger.ledger_positions SET quantity_kg=quantity_kg+q WHERE lot_id=l AND location_id=loc;
    IF NOT FOUND THEN INSERT INTO grainledger.ledger_positions VALUES(l,loc,q); END IF;
END $$;

CREATE FUNCTION grainledger.ledger_separate_bulk_deferred_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path TO 'pg_catalog','grainledger','pg_temp' AS $$
BEGIN
    IF (TG_OP='INSERT' AND NEW.quantity_kg>0 OR TG_OP='UPDATE' AND NEW.quantity_kg>OLD.quantity_kg)
       AND EXISTS(SELECT 1 FROM grainledger.ledger_locations WHERE id=NEW.location_id
                  AND kind IN ('SILO','COMPARTMENT') AND NOT mixed_bulk)
       AND (SELECT count(*) FROM grainledger.ledger_positions WHERE location_id=NEW.location_id AND quantity_kg>0)>1 THEN
        RAISE EXCEPTION 'Bulk location requires a single separate lot or an approved pool' USING ERRCODE='23514';
    END IF;
    RETURN NULL;
END $$;
REVOKE ALL ON FUNCTION grainledger.ledger_separate_bulk_deferred_guard() FROM PUBLIC;
CREATE CONSTRAINT TRIGGER ledger_separate_bulk_deferred_guard
AFTER INSERT OR UPDATE ON grainledger.ledger_positions DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_separate_bulk_deferred_guard();

CREATE OR REPLACE FUNCTION grainledger.ledger_mixed_bulk_location_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path TO 'pg_catalog','grainledger','pg_temp' AS $$
BEGIN
    IF TG_OP='UPDATE' AND (OLD.kind IN ('SILO','COMPARTMENT') OR OLD.mixed_bulk
        OR NEW.kind IN ('SILO','COMPARTMENT') OR NEW.mixed_bulk)
        AND (NEW.kind IS DISTINCT FROM OLD.kind OR NEW.mixed_bulk IS DISTINCT FROM OLD.mixed_bulk)
        AND (EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE location_id=OLD.id AND quantity_kg>0)
             OR EXISTS(SELECT 1 FROM grainledger.ledger_bulk_location_last_lots WHERE location_id=OLD.id)) THEN
        RAISE EXCEPTION 'Bulk location requires recorded clearance before changing contents' USING ERRCODE='23514';
    END IF;
    IF TG_OP='UPDATE' AND OLD.mixed_bulk AND NEW.cleared
       AND ((NEW.owner_id IS NULL AND (OLD.cleared IS DISTINCT FROM NEW.cleared OR OLD.owner_id IS DISTINCT FROM NEW.owner_id))
            OR NOT NEW.mixed_bulk)
       AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE location_id=OLD.id AND quantity_kg>0)
       AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=OLD.id AND lot_id IS NOT NULL) THEN
        UPDATE grainledger.ledger_mixed_bulk_pools SET owner_id=NULL,commodity_id=NULL,stage=NULL,product_identity=NULL
        WHERE location_id=OLD.id;
    END IF;
    IF TG_OP='UPDATE' AND (NEW.mixed_bulk IS DISTINCT FROM OLD.mixed_bulk
       OR (OLD.mixed_bulk OR NEW.mixed_bulk) AND (NEW.kind IS DISTINCT FROM OLD.kind OR NEW.owner_id IS DISTINCT FROM OLD.owner_id)) THEN
        IF EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE location_id=OLD.id AND quantity_kg>0)
           OR EXISTS(SELECT 1 FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=OLD.id AND lot_id IS NOT NULL) THEN
            RAISE EXCEPTION 'Occupied mixed-bulk location requires drain and recorded clearance before identity or mode change' USING ERRCODE='23514';
        END IF;
        IF OLD.mixed_bulk AND (NEW.kind IS DISTINCT FROM OLD.kind OR NEW.owner_id IS DISTINCT FROM OLD.owner_id
            OR NEW.mixed_bulk IS DISTINCT FROM OLD.mixed_bulk) THEN
            IF NOT NEW.cleared AND NOT (OLD.cleared AND OLD.owner_id IS NULL AND NEW.owner_id IS NOT NULL
                AND NEW.kind=OLD.kind AND NEW.mixed_bulk=OLD.mixed_bulk) THEN
                RAISE EXCEPTION 'Mixed-bulk identity or mode change requires recorded clearance' USING ERRCODE='23514';
            END IF;
            IF NEW.owner_id IS NOT NULL THEN NEW.cleared:=false; END IF;
        END IF;
    END IF;
    RETURN NEW;
END $$;

-- Patch active custody and command definitions in place; fail migration if their expected bodies changed.
DO $migration$
DECLARE definition text; old_text text; new_text text;
BEGIN
    SELECT pg_get_functiondef('grainledger.ledger_command_core(uuid,text,jsonb)'::regprocedure) INTO definition;
    old_text:='UPDATE grainledger.ledger_locations SET cleared=true,owner_id=NULL,version=gen_random_uuid() WHERE id=location.id;';
    new_text:='DELETE FROM grainledger.ledger_bulk_location_last_lots WHERE location_id=location.id;' || E'\n                ' || old_text;
    IF position(old_text IN definition)=0 THEN RAISE EXCEPTION 'Unexpected ledger_command_core clearance definition'; END IF;
    definition:=replace(definition,old_text,new_text);
    old_text:='AND ee.kind IN (''DRYING'',''MILLING'',''MERGE'',''BLENDING'',''BAGGING'') ORDER BY pp.id';
    new_text:='AND ee.kind IN (''DRYING'',''MILLING'',''MERGE'',''BLENDING'',''BAGGING'') ORDER BY pp.quantity_kg DESC, pp.id';
    IF position(old_text IN definition)=0 THEN RAISE EXCEPTION 'Unexpected ledger_command_core correction definition'; END IF;
    EXECUTE replace(definition,old_text,new_text);

    SELECT pg_get_functiondef('grainledger.ledger_pool_ingress(uuid,uuid,text,uuid,uuid,numeric)'::regprocedure) INTO definition;
    old_text:='req jsonb; result_id uuid;';
    IF position(old_text IN definition)=0 THEN RAISE EXCEPTION 'Unexpected ledger_pool_ingress declarations'; END IF;
    definition:=replace(definition,old_text,'req jsonb; result_id uuid; variety jsonb;');
    old_text:='    PERFORM grainledger.ledger_credit_location(loc,incoming.owner_id);';
    new_text:=$patch$
    IF old_pool.lot_id IS NULL THEN
        PERFORM grainledger.ledger_bulk_refill_guard(loc,incoming_lot);
    END IF;
    variety:=grainledger.ledger_resolve_lot_variety(incoming.owner_id,
        CASE WHEN incoming.stage=incoming.product_identity AND incoming.stage IN ('BRAN','HUSK') THEN incoming.stage END,
        CASE WHEN old_pool.lot_id IS NULL THEN ARRAY[incoming_lot] ELSE ARRAY[incoming_lot,old_pool.lot_id] END);
    IF variety->>'varietyStatus'='UNKNOWN' THEN
        RAISE EXCEPTION 'Rice variety is unknown' USING ERRCODE='23514';
    ELSIF variety->>'varietyStatus'='MIXED'
        AND NOT (incoming.stage=incoming.product_identity AND incoming.stage IN ('BRAN','HUSK')) THEN
        RAISE EXCEPTION 'Different rice varieties cannot be mixed' USING ERRCODE='23514';
    END IF;
    PERFORM grainledger.ledger_credit_location(loc,incoming.owner_id);$patch$;
    IF position(old_text IN definition)=0 THEN RAISE EXCEPTION 'Unexpected ledger_pool_ingress credit'; END IF;
    definition:=replace(definition,old_text,new_text);
    old_text:=$patch$INSERT INTO grainledger.ledger_lots(id,code,owner_id,commodity_id,stage,product_identity,code_origin_id)
    VALUES(output_id,output_code,incoming.owner_id,incoming.commodity_id,incoming.stage,incoming.product_identity,output_origin);$patch$;
    new_text:=$patch$INSERT INTO grainledger.ledger_lots(id,code,owner_id,commodity_id,stage,product_identity,code_origin_id,variety_status,rice_variety_id,rice_variety_name)
    VALUES(output_id,output_code,incoming.owner_id,incoming.commodity_id,incoming.stage,incoming.product_identity,output_origin,
        variety->>'varietyStatus',(variety->>'riceVarietyId')::uuid,variety->>'riceVariety');$patch$;
    IF position(old_text IN definition)=0 THEN RAISE EXCEPTION 'Unexpected ledger_pool_ingress output'; END IF;
    definition:=replace(definition,old_text,new_text);
    old_text:=$patch$'incomingLotId',incoming_lot,'quantityKg',quantity::text,'priorPoolLotId',old_pool.lot_id));$patch$;
    new_text:=$patch$'incomingLotId',incoming_lot,'quantityKg',quantity::text,'priorPoolLotId',old_pool.lot_id)
            || CASE WHEN variety->'varietyMixPolicy'<>'null'::jsonb
                THEN jsonb_build_object('varietyMixPolicy',variety->'varietyMixPolicy') ELSE '{}'::jsonb END);$patch$;
    IF position(old_text IN definition)=0 THEN RAISE EXCEPTION 'Unexpected ledger_pool_ingress event'; END IF;
    definition:=replace(definition,old_text,new_text);
    old_text:='    UPDATE grainledger.ledger_locations SET cleared=false WHERE id=loc AND cleared;';
    new_text:=$patch$    INSERT INTO grainledger.ledger_bulk_location_last_lots(location_id,lot_id) VALUES(loc,output_id)
    ON CONFLICT(location_id) DO UPDATE SET lot_id=excluded.lot_id;
    UPDATE grainledger.ledger_locations SET cleared=false WHERE id=loc AND cleared;$patch$;
    IF position(old_text IN definition)=0 THEN RAISE EXCEPTION 'Unexpected ledger_pool_ingress location update'; END IF;
    EXECUTE replace(definition,old_text,new_text);

    SELECT pg_get_functiondef('grainledger.ledger_pool_withdraw(uuid,uuid,text,uuid,uuid,numeric,bigint)'::regprocedure) INTO definition;
    old_text:=$patch$INSERT INTO grainledger.ledger_lots(id,code,owner_id,commodity_id,stage,product_identity,code_origin_id)
    VALUES(aliquot_id,aliquot_code,source.owner_id,source.commodity_id,source.stage,source.product_identity,aliquot_origin);$patch$;
    new_text:=$patch$INSERT INTO grainledger.ledger_lots(id,code,owner_id,commodity_id,stage,product_identity,code_origin_id,variety_status,rice_variety_id,rice_variety_name)
    VALUES(aliquot_id,aliquot_code,source.owner_id,source.commodity_id,source.stage,source.product_identity,aliquot_origin,source.variety_status,source.rice_variety_id,source.rice_variety_name);$patch$;
    IF position(old_text IN definition)=0 THEN RAISE EXCEPTION 'Unexpected ledger_pool_withdraw aliquot'; END IF;
    definition:=replace(definition,old_text,new_text);
    old_text:=$patch$INSERT INTO grainledger.ledger_lots(id,code,owner_id,commodity_id,stage,product_identity,code_origin_id)
        VALUES(remainder_id,remainder_code,source.owner_id,source.commodity_id,source.stage,source.product_identity,remainder_origin);$patch$;
    new_text:=$patch$INSERT INTO grainledger.ledger_lots(id,code,owner_id,commodity_id,stage,product_identity,code_origin_id,variety_status,rice_variety_id,rice_variety_name)
        VALUES(remainder_id,remainder_code,source.owner_id,source.commodity_id,source.stage,source.product_identity,remainder_origin,source.variety_status,source.rice_variety_id,source.rice_variety_name);$patch$;
    IF position(old_text IN definition)=0 THEN RAISE EXCEPTION 'Unexpected ledger_pool_withdraw remainder'; END IF;
    definition:=replace(definition,old_text,new_text);
    old_text:='    RETURN actual_id;';
    new_text:=$patch$    IF output_quantity>0 AND (destination.mixed_bulk OR destination.kind IN ('SILO','COMPARTMENT')) THEN
        INSERT INTO grainledger.ledger_bulk_location_last_lots(location_id,lot_id) VALUES(loc,remainder_id)
        ON CONFLICT(location_id) DO UPDATE SET lot_id=excluded.lot_id;
    END IF;
    RETURN actual_id;$patch$;
    IF position(old_text IN definition)=0 THEN RAISE EXCEPTION 'Unexpected ledger_pool_withdraw return'; END IF;
    EXECUTE replace(definition,old_text,new_text);
END $migration$;
