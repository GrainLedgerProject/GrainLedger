-- Forward-only: NULL marker means historical stock, never relabel or rewrite it.
ALTER TABLE capture.procurements ADD COLUMN receiving_workflow text CHECK(receiving_workflow IS NULL OR receiving_workflow='PHYSICAL_ARRIVAL_V1');
ALTER TABLE capture.procurements ALTER COLUMN receiving_workflow SET DEFAULT 'PHYSICAL_ARRIVAL_V1';
ALTER TABLE capture.source_lots DROP CONSTRAINT source_lots_ledger_lot;
ALTER FUNCTION ledger.procurement_package_complete(uuid) RENAME TO legacy_procurement_package_complete;
DROP FUNCTION ledger.procurement_receipt(uuid,uuid,uuid,uuid,uuid,uuid,uuid,jsonb);

CREATE TABLE ledger.receiving (
    procurement_id uuid PRIMARY KEY REFERENCES capture.procurements(id),
    status text NOT NULL CHECK(status IN ('PENDING_ARRIVAL','AWAITING_MANAGER_REVIEW','REMEASUREMENT_REQUESTED','RECEIVED_MATCHED','RECEIVED_SHORTAGE_APPROVED','BLOCKED_UNRESOLVED')),
    version integer NOT NULL DEFAULT 0 CHECK(version>=0),
    latest_measurement_set_id uuid,
    accepted_measurement_set_id uuid,
    accepted_review_id uuid,
    CHECK ((status IN ('RECEIVED_MATCHED','RECEIVED_SHORTAGE_APPROVED'))=(accepted_measurement_set_id IS NOT NULL)),
    CHECK ((status='RECEIVED_SHORTAGE_APPROVED')=(accepted_review_id IS NOT NULL))
);
CREATE TABLE ledger.arrival_measurement_sets (
    id uuid PRIMARY KEY,
    procurement_id uuid NOT NULL REFERENCES ledger.receiving(procurement_id),
    operation_id uuid NOT NULL UNIQUE,
    operator_id uuid NOT NULL REFERENCES identity.users("Id"),
    captured_at timestamptz NOT NULL,
    recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    observations text NOT NULL CHECK(length(observations)<=4000),
    expected_version integer NOT NULL,
    UNIQUE(procurement_id,id)
);
CREATE TABLE ledger.arrival_measurements (
    measurement_set_id uuid NOT NULL REFERENCES ledger.arrival_measurement_sets(id),
    procurement_item_id uuid NOT NULL REFERENCES capture.procurement_items(id),
    measured_value numeric(24,12) NOT NULL CHECK(measured_value>0),
    measured_unit text NOT NULL CHECK(measured_unit IN ('KG','G','T')),
    actual_kg numeric(20,6) NOT NULL CHECK(actual_kg>0),
    PRIMARY KEY(measurement_set_id,procurement_item_id),
    CHECK(actual_kg=CASE measured_unit WHEN 'KG' THEN measured_value WHEN 'G' THEN measured_value/1000 WHEN 'T' THEN measured_value*1000 END)
);
CREATE TABLE ledger.arrival_reviews (
    id uuid PRIMARY KEY,
    procurement_id uuid NOT NULL REFERENCES ledger.receiving(procurement_id),
    measurement_set_id uuid NOT NULL,
    operation_id uuid NOT NULL UNIQUE,
    expected_version integer NOT NULL,
    action text NOT NULL CHECK(action IN ('APPROVE_SHORTAGE','REQUEST_REMEASUREMENT','KEEP_BLOCKED')),
    reason text NOT NULL CHECK(length(trim(reason)) BETWEEN 1 AND 4000),
    evidence_id uuid NOT NULL REFERENCES ledger.evidence(id),
    manager_id uuid NOT NULL REFERENCES identity.users("Id"),
    recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE(procurement_id,id),
    FOREIGN KEY(procurement_id,measurement_set_id) REFERENCES ledger.arrival_measurement_sets(procurement_id,id)
);
ALTER TABLE ledger.receiving ADD FOREIGN KEY(procurement_id,latest_measurement_set_id) REFERENCES ledger.arrival_measurement_sets(procurement_id,id) DEFERRABLE INITIALLY DEFERRED;
ALTER TABLE ledger.receiving ADD FOREIGN KEY(procurement_id,accepted_measurement_set_id) REFERENCES ledger.arrival_measurement_sets(procurement_id,id) DEFERRABLE INITIALLY DEFERRED;
ALTER TABLE ledger.receiving ADD FOREIGN KEY(procurement_id,accepted_review_id) REFERENCES ledger.arrival_reviews(procurement_id,id) DEFERRABLE INITIALLY DEFERRED;
CREATE TABLE ledger.arrival_operations (
    operation_id uuid PRIMARY KEY,
    procurement_id uuid NOT NULL REFERENCES ledger.receiving(procurement_id),
    actor_id uuid NOT NULL REFERENCES identity.users("Id"),
    kind text NOT NULL,
    payload jsonb NOT NULL,
    result jsonb NOT NULL,
    recorded_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX receiving_facility_queue ON capture.procurements(collection_site_id,received_at DESC,id) WHERE receiving_workflow IS NOT NULL;
DO $$ DECLARE t text; BEGIN
    FOREACH t IN ARRAY ARRAY['arrival_measurement_sets','arrival_measurements','arrival_reviews','arrival_operations'] LOOP
        EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON ledger.%I FOR EACH ROW EXECUTE FUNCTION ledger.immutable()',t);
    END LOOP;
END $$;

CREATE FUNCTION ledger.procurement_package_complete(r uuid) RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE h capture.procurements; ev capture.business_events; snapshot jsonb; state ledger.receiving; row record; item jsonb; loc uuid; commodity uuid; n integer; actual numeric;
BEGIN
    SELECT * INTO h FROM capture.procurements WHERE id=r;
    IF NOT FOUND THEN RETURN false; END IF;
    IF h.receiving_workflow IS NULL THEN RETURN ledger.legacy_procurement_package_complete(r); END IF;
    SELECT * INTO ev FROM capture.business_events WHERE id=h.event_id;
    SELECT * INTO state FROM ledger.receiving WHERE procurement_id=r;
    IF ev.id IS NULL OR state.procurement_id IS NULL OR h.status<>'COMMITTED' OR ev.event_type<>'PROCUREMENT_COMMITTED'
       OR ev.company_id IS DISTINCT FROM h.company_id OR ev.actor_id IS DISTINCT FROM h.received_by OR ev.occurred_at<>h.received_at THEN RETURN false; END IF;
    snapshot:=ev.payload->'result'; loc:=(snapshot->>'ReceivingLocationId')::uuid; commodity:=(snapshot->>'CommodityId')::uuid;
    SELECT count(*) INTO n FROM capture.procurement_items WHERE procurement_id=r;
    IF snapshot IS NULL OR n NOT BETWEEN 1 AND 100 OR jsonb_typeof(snapshot->'Items') IS DISTINCT FROM 'array'
       OR jsonb_array_length(snapshot->'Items')<>n OR (SELECT count(DISTINCT (x->>'Id')::uuid) FROM jsonb_array_elements(snapshot->'Items') x)<>n
       OR (snapshot->>'Id')::uuid IS DISTINCT FROM h.id OR snapshot->>'Code' IS DISTINCT FROM h.code
       OR (snapshot->>'FarmerId')::uuid IS DISTINCT FROM h.farmer_id OR (snapshot->>'CollectionSiteId')::uuid IS DISTINCT FROM h.collection_site_id
       OR (snapshot->>'ReceivedBy')::uuid IS DISTINCT FROM h.received_by OR (snapshot->>'EventId')::uuid IS DISTINCT FROM ev.id
       OR (snapshot->>'ReceivedAt')::timestamptz IS DISTINCT FROM h.received_at OR (snapshot->>'RecordedAt')::timestamptz IS DISTINCT FROM ev.recorded_at
       OR snapshot->>'Status' IS DISTINCT FROM 'PENDING_ARRIVAL' OR (snapshot->>'ReceivingVersion')::integer IS DISTINCT FROM 0
       OR (snapshot->>'TotalNetMassKg')::numeric IS DISTINCT FROM (SELECT sum(net_mass_kg) FROM capture.procurement_items WHERE procurement_id=r)
       OR NOT EXISTS(SELECT 1 FROM catalog.commodities WHERE id=commodity AND code='PADDY')
       OR NOT EXISTS(SELECT 1 FROM ledger.locations WHERE id=loc AND facility_id=h.collection_site_id)
       OR NOT EXISTS(SELECT 1 FROM capture.offline_operations o WHERE o.applied_event_id=ev.id AND o.company_id=h.company_id
           AND o.submitted_by=h.received_by AND o.status='APPLIED' AND o.client_operation_id=ev.correlation_id AND o.result_payload=snapshot
           AND o.captured_at=(snapshot->>'CapturedAt')::timestamptz AND o.payload->>'operationType'='PROCUREMENT_COMMITTED') THEN RETURN false; END IF;
    IF (SELECT count(*) FROM ledger.lots WHERE receipt_id=r)<>(CASE WHEN state.accepted_measurement_set_id IS NULL THEN 0 ELSE n END) THEN RETURN false; END IF;
    IF state.latest_measurement_set_id IS NOT NULL AND (SELECT count(*) FROM ledger.arrival_measurements WHERE measurement_set_id=state.latest_measurement_set_id)<>n THEN RETURN false; END IF;
    IF state.accepted_measurement_set_id IS NOT NULL AND state.accepted_measurement_set_id IS DISTINCT FROM state.latest_measurement_set_id THEN RETURN false; END IF;
    IF (state.status='PENDING_ARRIVAL') IS DISTINCT FROM (state.version=0 AND state.latest_measurement_set_id IS NULL)
       OR EXISTS(SELECT 1 FROM ledger.arrival_measurement_sets ms WHERE ms.procurement_id=r AND
           (SELECT count(*) FROM ledger.arrival_measurements WHERE measurement_set_id=ms.id)<>n)
       OR EXISTS(SELECT 1 FROM ledger.arrival_measurements m JOIN ledger.arrival_measurement_sets ms ON ms.id=m.measurement_set_id
           JOIN capture.procurement_items i ON i.id=m.procurement_item_id WHERE ms.procurement_id=r AND i.procurement_id<>r)
       OR EXISTS(SELECT 1 FROM ledger.arrival_reviews v JOIN ledger.evidence e ON e.id=v.evidence_id
           WHERE v.procurement_id=r AND (e.owner_id<>h.company_id OR e.facility_id<>h.collection_site_id)) THEN RETURN false; END IF;
    FOR row IN SELECT i.*,sl.id source_id,sl.lot_id,ps.plot_id,ps.season_id,ps.rice_variety,p.farmer_id
        FROM capture.procurement_items i LEFT JOIN capture.source_lots sl ON sl.procurement_item_id=i.id AND sl.company_id=i.company_id
        JOIN capture.plot_seasons ps ON ps.id=i.plot_season_id AND ps.company_id=i.company_id
        JOIN capture.plots p ON p.id=ps.plot_id AND p.company_id=ps.company_id
        JOIN capture.crop_seasons s ON s.id=ps.season_id AND s.company_id=ps.company_id WHERE i.procurement_id=r
    LOOP
        SELECT x INTO item FROM jsonb_array_elements(snapshot->'Items') x WHERE (x->>'Id')::uuid=row.id;
        IF row.company_id<>h.company_id OR row.farmer_id<>h.farmer_id OR row.source_id IS NULL OR row.line_no NOT BETWEEN 1 AND n
           OR (item->>'LineNo')::integer IS DISTINCT FROM row.line_no OR (item->>'PlotSeasonId')::uuid IS DISTINCT FROM row.plot_season_id
           OR (item->>'PlotId')::uuid IS DISTINCT FROM row.plot_id OR (item->>'SeasonId')::uuid IS DISTINCT FROM row.season_id
           OR item->>'RiceVariety' IS DISTINCT FROM row.rice_variety OR (item->>'SourceLotId')::uuid IS DISTINCT FROM row.source_id
           OR (item->>'LotId')::uuid IS DISTINCT FROM row.lot_id OR item->>'LotCode' IS DISTINCT FROM upper('LL-'||replace(row.lot_id::text,'-',''))
           OR item->>'Stage' IS DISTINCT FROM 'RAW_PADDY' OR (item->>'MeasuredValue')::numeric IS DISTINCT FROM row.measured_value
           OR item->>'MeasuredUnit' IS DISTINCT FROM row.measured_unit OR (item->>'NetMassKg')::numeric IS DISTINCT FROM row.net_mass_kg
           OR (item->>'MoisturePercent')::numeric IS DISTINCT FROM row.moisture_percent OR (item->'InitialStock'->>'LocationId')::uuid IS DISTINCT FROM loc
           OR (item->'InitialStock'->>'ReceivedKg')::numeric IS DISTINCT FROM 0
           OR row.net_mass_kg IS DISTINCT FROM (CASE row.measured_unit WHEN 'KG' THEN row.measured_value WHEN 'G' THEN row.measured_value/1000 WHEN 'T' THEN row.measured_value*1000 END)
           OR NOT EXISTS(SELECT 1 FROM catalog.measurement_units WHERE code=row.measured_unit AND quantity_kind='MASS') THEN RETURN false; END IF;
        IF state.accepted_measurement_set_id IS NULL THEN
            IF EXISTS(SELECT 1 FROM ledger.lots WHERE id=row.lot_id) THEN RETURN false; END IF;
        ELSE
            SELECT actual_kg INTO actual FROM ledger.arrival_measurements WHERE measurement_set_id=state.accepted_measurement_set_id AND procurement_item_id=row.id;
            IF actual IS NULL OR actual<=0 OR (state.status='RECEIVED_MATCHED' AND actual<>row.net_mass_kg)
               OR (state.status='RECEIVED_SHORTAGE_APPROVED' AND actual>row.net_mass_kg)
               OR NOT EXISTS(SELECT 1 FROM ledger.lots l JOIN ledger.catalog_commodities m ON m.ledger_commodity_id=l.commodity_id
                    WHERE l.id=row.lot_id AND l.owner_id=h.company_id AND l.receipt_id=r AND l.receipt_line_id=row.id
                    AND l.code=item->>'LotCode' AND l.stage='RAW_PADDY' AND l.product_identity='RAW_PADDY' AND m.catalog_commodity_id=commodity)
               OR (SELECT count(*) FROM ledger.events WHERE lot_id=row.lot_id AND kind='RECEIPT')<>1
               OR NOT EXISTS(SELECT 1 FROM ledger.events e JOIN ledger.postings p ON p.event_id=e.id JOIN ledger.arrival_measurement_sets ms ON ms.id=state.accepted_measurement_set_id
                    JOIN ledger.arrival_measurements am ON am.measurement_set_id=ms.id AND am.procurement_item_id=row.id
                    LEFT JOIN ledger.arrival_reviews rv ON rv.id=state.accepted_review_id
                    WHERE e.lot_id=row.lot_id AND e.kind='RECEIPT' AND e.actor_id=ms.operator_id AND e.occurred_at=ms.captured_at
                    AND e.details->>'measurementSetId'=state.accepted_measurement_set_id::text AND e.details->>'receiptId'=r::text
                    AND e.details->>'receiptLineId'=row.id::text AND e.details->>'sourceLotId'=row.source_id::text
                    AND (e.details->>'quantityKg')::numeric=actual AND (e.details->>'sourceQuantityKg')::numeric=row.net_mass_kg
                    AND e.details->>'captureEventId'=ev.id::text AND e.details->>'catalogCommodityId'=commodity::text
                    AND e.details->>'farmerId'=h.farmer_id::text AND e.details->>'plotSeasonId'=row.plot_season_id::text
                    AND e.details->>'plotId'=row.plot_id::text AND e.details->>'seasonId'=row.season_id::text
                    AND e.details->>'riceVariety'=row.rice_variety AND (e.details->>'originalQuantity')::numeric=row.measured_value
                    AND e.details->>'originalUnit'=row.measured_unit AND (e.details->>'moisturePercent')::numeric=row.moisture_percent
                    AND (e.details->>'arrivalQuantity')::numeric=am.measured_value AND e.details->>'arrivalUnit'=am.measured_unit
                    AND (e.details->>'managerReviewId')::uuid IS NOT DISTINCT FROM state.accepted_review_id
                    AND (e.details->>'managerId')::uuid IS NOT DISTINCT FROM rv.manager_id
                    AND (e.details->>'evidenceId')::uuid IS NOT DISTINCT FROM rv.evidence_id
                    AND e.details->>'receivingStatus'=state.status
                    AND p.lot_id=row.lot_id AND p.location_id=loc AND p.quantity_kg=actual AND p.reverses_posting_id IS NULL
                    AND (SELECT count(*) FROM ledger.postings WHERE event_id=e.id)=1)
               OR NOT EXISTS(SELECT 1 FROM ledger.contributions WHERE lot_id=row.lot_id AND source_lot_id=row.lot_id AND quantity_kg=actual)
               OR (SELECT count(*) FROM ledger.contributions WHERE lot_id=row.lot_id)<>1 THEN RETURN false; END IF;
        END IF;
    END LOOP;
    IF state.status='RECEIVED_SHORTAGE_APPROVED' AND (NOT EXISTS(SELECT 1 FROM ledger.arrival_measurements m JOIN capture.procurement_items i ON i.id=m.procurement_item_id WHERE m.measurement_set_id=state.accepted_measurement_set_id AND m.actual_kg<i.net_mass_kg)
       OR NOT EXISTS(SELECT 1 FROM ledger.arrival_reviews v JOIN ledger.evidence e ON e.id=v.evidence_id WHERE v.id=state.accepted_review_id AND v.action='APPROVE_SHORTAGE' AND v.measurement_set_id=state.accepted_measurement_set_id AND e.owner_id=h.company_id AND e.facility_id=h.collection_site_id)) THEN RETURN false; END IF;
    RETURN true;
END $$;

CREATE FUNCTION ledger.register_pending_procurement(a uuid,r uuid) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE h capture.procurements; snapshot jsonb;
BEGIN
    PERFORM ledger.lock_writes();
    SELECT * INTO STRICT h FROM capture.procurements WHERE id=r AND receiving_workflow='PHYSICAL_ARRIVAL_V1';
    IF ledger.role(a) IS DISTINCT FROM 'PurchasingStaff' OR NOT ledger.own(a,h.company_id) OR h.received_by<>a THEN RAISE EXCEPTION 'Procurement company authority denied' USING ERRCODE='42501'; END IF;
    IF NOT EXISTS(SELECT 1 FROM identity.organizations WHERE "Id"=h.company_id AND "Kind"='Customer') THEN RAISE EXCEPTION 'Customer procurement owner required' USING ERRCODE='42501'; END IF;
    SELECT payload->'result' INTO snapshot FROM capture.business_events WHERE id=h.event_id;
    IF NOT EXISTS(SELECT 1 FROM ledger.locations WHERE id=(snapshot->>'ReceivingLocationId')::uuid AND facility_id=h.collection_site_id
        AND kind IN ('STORE','SILO','COMPARTMENT') AND (owner_id=h.company_id OR owner_id IS NULL AND (kind<>'COMPARTMENT' OR cleared))) THEN RAISE EXCEPTION 'Receiving location no longer eligible' USING ERRCODE='23514'; END IF;
    INSERT INTO ledger.receiving(procurement_id,status) VALUES(r,'PENDING_ARRIVAL');
    IF ledger.procurement_package_complete(r) IS DISTINCT FROM true THEN RAISE EXCEPTION 'Pending source package incomplete' USING ERRCODE='23514'; END IF;
END $$;

CREATE FUNCTION ledger.receiving_detail(a uuid,r uuid) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE h capture.procurements; s ledger.receiving; snap jsonb; result jsonb;
BEGIN
    SELECT * INTO STRICT h FROM capture.procurements WHERE id=r AND receiving_workflow IS NOT NULL;
    PERFORM ledger.require_staff(a,h.collection_site_id,ARRAY['MillOperator','MillManager']);
    SELECT * INTO STRICT s FROM ledger.receiving WHERE procurement_id=r;
    SELECT payload->'result' INTO snap FROM capture.business_events WHERE id=h.event_id;
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

CREATE FUNCTION ledger.receiving_tasks(a uuid,f uuid,p integer,n integer) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE result jsonb;
BEGIN
    PERFORM ledger.require_staff(a,f,ARRAY['MillOperator','MillManager']);
    IF p IS NULL OR n IS NULL OR p<1 OR n NOT BETWEEN 1 AND 100 OR (p::bigint-1)*n>2147483647 THEN RAISE EXCEPTION 'Invalid pagination' USING ERRCODE='22023'; END IF;
    SELECT jsonb_build_object('items',COALESCE(jsonb_agg(ledger.receiving_detail(a,q.id) ORDER BY q.received_at DESC,q.id),'[]'::jsonb),'page',p,'pageSize',n,
        'hasMore',EXISTS(SELECT 1 FROM capture.procurements WHERE collection_site_id=f AND receiving_workflow IS NOT NULL ORDER BY received_at DESC,id OFFSET p*n LIMIT 1)) INTO result
    FROM (SELECT id,received_at FROM capture.procurements WHERE collection_site_id=f AND receiving_workflow IS NOT NULL ORDER BY received_at DESC,id OFFSET (p-1)*n LIMIT n) q;
    RETURN result;
END $$;

CREATE FUNCTION ledger.accept_arrival(r uuid) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
DECLARE h capture.procurements; s ledger.receiving; ms ledger.arrival_measurement_sets; snap jsonb; commodity uuid; c uuid; loc uuid; row record; event_id uuid; review ledger.arrival_reviews; code text;
BEGIN
    SELECT * INTO STRICT h FROM capture.procurements WHERE id=r;
    SELECT * INTO STRICT s FROM ledger.receiving WHERE procurement_id=r AND accepted_measurement_set_id IS NOT NULL;
    SELECT * INTO STRICT ms FROM ledger.arrival_measurement_sets WHERE id=s.accepted_measurement_set_id;
    SELECT * INTO review FROM ledger.arrival_reviews WHERE id=s.accepted_review_id;
    SELECT payload->'result' INTO snap FROM capture.business_events WHERE id=h.event_id;
    c:=(snap->>'CommodityId')::uuid; loc:=(snap->>'ReceivingLocationId')::uuid;
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

CREATE FUNCTION ledger.arrival_command(a uuid,r uuid,k text,j jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE h capture.procurements; s ledger.receiving; prior ledger.arrival_operations; op uuid; set_id uuid; review_id uuid; x jsonb; v numeric; kg numeric; unit text; action text; result jsonb; normalized jsonb;
BEGIN
    PERFORM ledger.lock_writes();
    SELECT * INTO STRICT h FROM capture.procurements WHERE id=r AND receiving_workflow='PHYSICAL_ARRIVAL_V1';
    PERFORM ledger.require_staff(a,h.collection_site_id,CASE k WHEN 'measurements' THEN ARRAY['MillOperator'] WHEN 'reviews' THEN ARRAY['MillManager'] ELSE ARRAY[]::text[] END);
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
            IF NOT EXISTS(SELECT 1 FROM catalog.measurement_units WHERE code=unit AND quantity_kind='MASS') THEN RAISE EXCEPTION 'Arrival mass unit not configured' USING ERRCODE='23514'; END IF;
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

CREATE FUNCTION ledger.arrival_package_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE r uuid;
BEGIN
    IF TG_TABLE_NAME='arrival_measurements' THEN SELECT procurement_id INTO r FROM ledger.arrival_measurement_sets WHERE id=NEW.measurement_set_id;
    ELSE r:=NEW.procurement_id; END IF;
    IF ledger.procurement_package_complete(r) IS DISTINCT FROM true THEN RAISE EXCEPTION 'Receiving source/stock package incomplete' USING ERRCODE='23514'; END IF;
    RETURN NEW;
END $$;
DO $$ DECLARE t text; BEGIN
    FOREACH t IN ARRAY ARRAY['receiving','arrival_measurement_sets','arrival_measurements','arrival_reviews','arrival_operations'] LOOP
        EXECUTE format('CREATE CONSTRAINT TRIGGER arrival_package AFTER INSERT OR UPDATE ON ledger.%I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ledger.arrival_package_guard()',t);
    END LOOP;
END $$;
CREATE FUNCTION ledger.procurement_stock(o uuid,l uuid) RETURNS TABLE(location_id uuid,on_hand_kg numeric,available_kg numeric)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
    SELECT p.location_id,p.quantity_kg,CASE WHEN ledger.blocked(p.lot_id) THEN 0 ELSE p.quantity_kg END
    FROM capture.source_lots s LEFT JOIN ledger.lots x ON x.id=s.lot_id AND x.owner_id=s.company_id
    LEFT JOIN ledger.positions p ON p.lot_id=x.id WHERE s.lot_id=l AND s.company_id=o
$$;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA ledger FROM PUBLIC;
