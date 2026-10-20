-- Forward-only: old observations have no applied transport-loss policy.
CREATE TABLE ledger.receiving_threshold_history (
    revision uuid PRIMARY KEY,
    company_id uuid REFERENCES identity.organizations("Id"),
    scope text NOT NULL CHECK(scope IN ('SYSTEM_DEFAULT','COMPANY_OVERRIDE')),
    action text NOT NULL CHECK(action IN ('INITIALIZE','CREATE','UPDATE','DELETE')),
    old_percentage numeric(8,6) CHECK(old_percentage>=0 AND old_percentage<100),
    new_percentage numeric(8,6) CHECK(new_percentage>=0 AND new_percentage<100),
    actor_id uuid REFERENCES identity.users("Id"),
    recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    reason text NOT NULL CHECK(length(trim(reason)) BETWEEN 1 AND 4000),
    migration_id text,
    CHECK((scope='SYSTEM_DEFAULT')=(company_id IS NULL)),
    CHECK((action='INITIALIZE' AND scope='SYSTEM_DEFAULT' AND actor_id IS NULL AND migration_id IS NOT NULL
           AND old_percentage IS NULL AND new_percentage IS NOT NULL)
       OR (action<>'INITIALIZE' AND actor_id IS NOT NULL AND migration_id IS NULL
           AND ((action='CREATE' AND scope='COMPANY_OVERRIDE' AND old_percentage IS NULL AND new_percentage IS NOT NULL)
             OR (action='UPDATE' AND old_percentage IS NOT NULL AND new_percentage IS NOT NULL)
             OR (action='DELETE' AND scope='COMPANY_OVERRIDE' AND old_percentage IS NOT NULL AND new_percentage IS NULL))))
);
CREATE INDEX receiving_threshold_history_page ON ledger.receiving_threshold_history(recorded_at DESC,revision DESC);
CREATE INDEX receiving_threshold_history_company ON ledger.receiving_threshold_history(company_id,recorded_at DESC,revision DESC);
CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON ledger.receiving_threshold_history FOR EACH ROW EXECUTE FUNCTION ledger.immutable();
CREATE TABLE ledger.receiving_threshold_default (
    singleton boolean PRIMARY KEY DEFAULT true CHECK(singleton),
    percentage numeric(8,6) NOT NULL CHECK(percentage>=0 AND percentage<100),
    revision uuid NOT NULL UNIQUE REFERENCES ledger.receiving_threshold_history(revision)
);
CREATE TABLE ledger.receiving_threshold_overrides (
    company_id uuid PRIMARY KEY REFERENCES identity.organizations("Id"),
    percentage numeric(8,6) NOT NULL CHECK(percentage>=0 AND percentage<100),
    revision uuid NOT NULL UNIQUE REFERENCES ledger.receiving_threshold_history(revision)
);
WITH initialized AS (
    INSERT INTO ledger.receiving_threshold_history(revision,scope,action,new_percentage,reason,migration_id)
    VALUES(gen_random_uuid(),'SYSTEM_DEFAULT','INITIALIZE',2,'Initialize receiving transport-loss default','20261019000000_ReceivingLossThreshold')
    RETURNING revision,new_percentage
)
INSERT INTO ledger.receiving_threshold_default(singleton,percentage,revision) SELECT true,new_percentage,revision FROM initialized;
REVOKE ALL ON ledger.receiving_threshold_default,ledger.receiving_threshold_overrides,ledger.receiving_threshold_history FROM PUBLIC;

ALTER TABLE ledger.arrival_measurement_sets
    ADD COLUMN threshold_company_id uuid REFERENCES identity.organizations("Id"),
    ADD COLUMN threshold_percentage numeric(8,6),
    ADD COLUMN threshold_source text,
    ADD COLUMN threshold_revision uuid REFERENCES ledger.receiving_threshold_history(revision),
    ADD CONSTRAINT arrival_threshold_shape CHECK (
        (threshold_company_id IS NULL AND threshold_percentage IS NULL AND threshold_source IS NULL AND threshold_revision IS NULL)
        OR (threshold_company_id IS NOT NULL AND threshold_percentage IS NOT NULL AND threshold_percentage>=0 AND threshold_percentage<100
            AND threshold_source IS NOT NULL AND threshold_source IN ('SYSTEM_DEFAULT','COMPANY_OVERRIDE') AND threshold_revision IS NOT NULL));

CREATE FUNCTION ledger.receiving_threshold_snapshot_valid(c uuid,p numeric,s text,r uuid) RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger AS $$
    SELECT c IS NOT NULL AND p IS NOT NULL AND s IS NOT NULL AND r IS NOT NULL
        AND EXISTS(SELECT 1 FROM ledger.receiving_threshold_history h WHERE h.revision=r AND h.scope=s
            AND h.new_percentage=p AND h.action<>'DELETE'
            AND ((s='SYSTEM_DEFAULT' AND h.company_id IS NULL) OR (s='COMPANY_OVERRIDE' AND h.company_id=c)))
$$;

CREATE FUNCTION ledger.receiving_threshold_effective(c uuid) RETURNS jsonb
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
DECLARE d ledger.receiving_threshold_default; o ledger.receiving_threshold_overrides;
BEGIN
    PERFORM ledger.lock_writes();
    SELECT * INTO d FROM ledger.receiving_threshold_default WHERE singleton;
    IF NOT FOUND OR ledger.receiving_threshold_snapshot_valid(c,d.percentage,'SYSTEM_DEFAULT',d.revision) IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'Receiving threshold default missing or invalid' USING ERRCODE='23514';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM identity.organizations WHERE "Id"=c AND "Kind"='Customer') THEN
        RAISE EXCEPTION 'Customer procurement owner required' USING ERRCODE='23514';
    END IF;
    SELECT * INTO o FROM ledger.receiving_threshold_overrides WHERE company_id=c;
    IF FOUND THEN
        IF ledger.receiving_threshold_snapshot_valid(c,o.percentage,'COMPANY_OVERRIDE',o.revision) IS DISTINCT FROM true THEN
            RAISE EXCEPTION 'Receiving threshold override invalid' USING ERRCODE='23514';
        END IF;
        RETURN jsonb_build_object('companyId',c,'percentage',o.percentage::text,'source','COMPANY_OVERRIDE','revision',o.revision);
    END IF;
    RETURN jsonb_build_object('companyId',c,'percentage',d.percentage::text,'source','SYSTEM_DEFAULT','revision',d.revision);
END $$;

CREATE FUNCTION ledger.receiving_threshold_insert_guard() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
DECLARE c uuid; effective jsonb;
BEGIN
    PERFORM ledger.lock_writes();
    SELECT company_id INTO STRICT c FROM capture.procurements WHERE id=NEW.procurement_id;
    effective:=ledger.receiving_threshold_effective(c);
    IF NEW.threshold_company_id IS DISTINCT FROM c
       OR NEW.threshold_percentage IS DISTINCT FROM (effective->>'percentage')::numeric
       OR NEW.threshold_source IS DISTINCT FROM effective->>'source'
       OR NEW.threshold_revision IS DISTINCT FROM (effective->>'revision')::uuid
       OR ledger.receiving_threshold_snapshot_valid(NEW.threshold_company_id,NEW.threshold_percentage,NEW.threshold_source,NEW.threshold_revision) IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'New arrival measurement requires current server-selected threshold snapshot' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER receiving_threshold_snapshot BEFORE INSERT ON ledger.arrival_measurement_sets FOR EACH ROW EXECUTE FUNCTION ledger.receiving_threshold_insert_guard();

CREATE FUNCTION ledger.receiving_threshold_read(a uuid,k text,j jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE c uuid; d ledger.receiving_threshold_default; p integer; n integer; result jsonb; scope_filter text;
BEGIN
    PERFORM ledger.lock_writes();
    IF k='own-company' THEN
        SELECT u."OrganizationId" INTO c FROM identity.users u JOIN identity.organizations o ON o."Id"=u."OrganizationId"
        WHERE u."Id"=a AND u."IsActive" AND u."Role" IN ('CustomerManager','ExportStaff','PurchasingStaff') AND o."Kind"='Customer';
        IF c IS NULL THEN RAISE EXCEPTION 'Own company threshold authority denied' USING ERRCODE='42501'; END IF;
        RETURN ledger.receiving_threshold_effective(c);
    END IF;
    IF ledger.role(a) IS DISTINCT FROM 'Administrator' THEN RAISE EXCEPTION 'Threshold setup authority denied' USING ERRCODE='42501'; END IF;
    IF k='settings' THEN
        SELECT * INTO d FROM ledger.receiving_threshold_default WHERE singleton;
        IF NOT FOUND OR NOT EXISTS(SELECT 1 FROM ledger.receiving_threshold_history h WHERE h.revision=d.revision AND h.scope='SYSTEM_DEFAULT' AND h.new_percentage=d.percentage AND h.action IN ('INITIALIZE','UPDATE')) THEN
            RAISE EXCEPTION 'Receiving threshold default missing or invalid' USING ERRCODE='23514';
        END IF;
        IF EXISTS(SELECT 1 FROM ledger.receiving_threshold_overrides o WHERE ledger.receiving_threshold_snapshot_valid(o.company_id,o.percentage,'COMPANY_OVERRIDE',o.revision) IS DISTINCT FROM true) THEN
            RAISE EXCEPTION 'Receiving threshold override invalid' USING ERRCODE='23514';
        END IF;
        RETURN jsonb_build_object('systemDefault',jsonb_build_object('percentage',d.percentage::text,'version',d.revision),
            'companyOverrides',COALESCE((SELECT jsonb_agg(jsonb_build_object('companyId',o.company_id,'companyName',c."Name",'percentage',o.percentage::text,'version',o.revision) ORDER BY c."Name",o.company_id)
                FROM ledger.receiving_threshold_overrides o JOIN identity.organizations c ON c."Id"=o.company_id),'[]'::jsonb));
    END IF;
    p:=COALESCE((j->>'page')::integer,1); n:=COALESCE((j->>'pageSize')::integer,25);
    IF p<1 OR n NOT BETWEEN 1 AND 100 OR (p::bigint-1)*n>2147483647 THEN RAISE EXCEPTION 'Invalid threshold pagination' USING ERRCODE='22023'; END IF;
    IF k='companies' THEN
        WITH filtered AS MATERIALIZED (SELECT "Id" id,"Name" name FROM identity.organizations WHERE "Kind"='Customer'
            AND (j->>'search' IS NULL OR "Name" ILIKE '%'||(j->>'search')||'%')),
        selected AS (SELECT * FROM filtered ORDER BY name,id LIMIT n OFFSET (p::bigint-1)*n)
        SELECT jsonb_build_object('items',COALESCE((SELECT jsonb_agg(jsonb_build_object('id',id,'name',name) ORDER BY name,id) FROM selected),'[]'::jsonb),
            'page',p,'pageSize',n,'totalCount',(SELECT count(*) FROM filtered),'hasMore',(SELECT count(*) FROM filtered)>p::bigint*n) INTO result;
        RETURN result;
    ELSIF k='history' THEN
        c:=(j->>'companyId')::uuid; scope_filter:=j->>'scope';
        IF scope_filter IS NOT NULL AND scope_filter NOT IN ('SYSTEM_DEFAULT','COMPANY_OVERRIDE') THEN RAISE EXCEPTION 'Invalid threshold history scope' USING ERRCODE='22023'; END IF;
        WITH filtered AS MATERIALIZED (SELECT h.*,o."Name" company_name,u."DisplayName" actor_name FROM ledger.receiving_threshold_history h
            LEFT JOIN identity.organizations o ON o."Id"=h.company_id LEFT JOIN identity.users u ON u."Id"=h.actor_id
            WHERE (c IS NULL OR h.company_id=c) AND (scope_filter IS NULL OR h.scope=scope_filter)),
        selected AS (SELECT * FROM filtered ORDER BY recorded_at DESC,revision DESC LIMIT n OFFSET (p::bigint-1)*n)
        SELECT jsonb_build_object('items',COALESCE((SELECT jsonb_agg(jsonb_build_object('revision',revision,'companyId',company_id,'companyName',company_name,
                'scope',scope,'action',action,'oldPercentage',old_percentage::text,'newPercentage',new_percentage::text,'actorId',actor_id,'actorName',actor_name,
                'recordedAt',recorded_at,'reason',reason,'migrationId',migration_id) ORDER BY recorded_at DESC,revision DESC) FROM selected),'[]'::jsonb),
            'page',p,'pageSize',n,'totalCount',(SELECT count(*) FROM filtered),'hasMore',(SELECT count(*) FROM filtered)>p::bigint*n) INTO result;
        RETURN result;
    END IF;
    RAISE EXCEPTION 'Unsupported threshold read' USING ERRCODE='22023';
END $$;

CREATE FUNCTION ledger.receiving_threshold_command(a uuid,k text,j jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE v_company_id uuid; v_percentage numeric(8,6); v_old_percentage numeric(8,6); v_old_revision uuid; v_revision uuid:=gen_random_uuid(); v_reason text;
BEGIN
    PERFORM ledger.lock_writes();
    IF ledger.role(a) IS DISTINCT FROM 'Administrator' THEN RAISE EXCEPTION 'Threshold setup authority denied' USING ERRCODE='42501'; END IF;
    IF k IS NULL OR k NOT IN ('default','create','update','delete') THEN RAISE EXCEPTION 'Unsupported threshold mutation' USING ERRCODE='22023'; END IF;
    v_reason:=j->>'reason';
    IF jsonb_typeof(j->'reason') IS DISTINCT FROM 'string' OR length(trim(v_reason)) NOT BETWEEN 1 AND 4000 THEN RAISE EXCEPTION 'Threshold change reason required' USING ERRCODE='22023'; END IF;
    IF k<>'delete' THEN
        IF jsonb_typeof(j->'percentage') IS DISTINCT FROM 'string' OR j->>'percentage' !~ '^[0-9]+(\.[0-9]{1,6})?$' THEN
            RAISE EXCEPTION 'Percentage must be decimal string with at most six fractional digits' USING ERRCODE='22023';
        END IF;
        IF (j->>'percentage')::numeric<0 OR (j->>'percentage')::numeric>=100 THEN RAISE EXCEPTION 'Threshold percentage must be at least zero and below 100' USING ERRCODE='22023'; END IF;
        v_percentage:=(j->>'percentage')::numeric(8,6);
    END IF;
    -- Required default must remain valid even when an override is targeted.
    PERFORM ledger.receiving_threshold_read(a,'settings','{}'::jsonb);
    IF k='default' THEN
        SELECT d.percentage,d.revision INTO v_old_percentage,v_old_revision FROM ledger.receiving_threshold_default d WHERE singleton;
    ELSE
        v_company_id:=(j->>'companyId')::uuid;
        IF NOT EXISTS(SELECT 1 FROM identity.organizations WHERE "Id"=v_company_id AND "Kind"='Customer') THEN RAISE EXCEPTION 'Customer override target required' USING ERRCODE='22023'; END IF;
        SELECT o.percentage,o.revision INTO v_old_percentage,v_old_revision FROM ledger.receiving_threshold_overrides o WHERE company_id=v_company_id;
        IF k='create' AND FOUND THEN RAISE EXCEPTION 'Company threshold already exists' USING ERRCODE='23514'; END IF;
        IF k IN ('update','delete') AND NOT FOUND THEN RAISE EXCEPTION 'Company threshold missing or stale' USING ERRCODE='23514'; END IF;
    END IF;
    IF k<>'create' THEN
        IF jsonb_typeof(j->'version') IS DISTINCT FROM 'string' OR (j->>'version')::uuid IS DISTINCT FROM v_old_revision THEN RAISE EXCEPTION 'Threshold version stale' USING ERRCODE='23514'; END IF;
    END IF;
    INSERT INTO ledger.receiving_threshold_history(revision,company_id,scope,action,old_percentage,new_percentage,actor_id,reason)
    VALUES(v_revision,v_company_id,CASE WHEN k='default' THEN 'SYSTEM_DEFAULT' ELSE 'COMPANY_OVERRIDE' END,
        CASE k WHEN 'create' THEN 'CREATE' WHEN 'delete' THEN 'DELETE' ELSE 'UPDATE' END,v_old_percentage,v_percentage,a,v_reason);
    IF k='default' THEN
        UPDATE ledger.receiving_threshold_default SET percentage=v_percentage,revision=v_revision WHERE singleton;
    ELSIF k='create' THEN
        INSERT INTO ledger.receiving_threshold_overrides(company_id,percentage,revision) VALUES(v_company_id,v_percentage,v_revision);
    ELSIF k='update' THEN
        UPDATE ledger.receiving_threshold_overrides SET percentage=v_percentage,revision=v_revision WHERE company_id=v_company_id;
    ELSE
        DELETE FROM ledger.receiving_threshold_overrides WHERE company_id=v_company_id;
    END IF;
    RETURN ledger.receiving_threshold_read(a,'settings','{}'::jsonb);
END $$;

ALTER TABLE ledger.receiving DROP CONSTRAINT receiving_status_check;
ALTER TABLE ledger.receiving DROP CONSTRAINT receiving_check;
ALTER TABLE ledger.receiving ADD CONSTRAINT receiving_status_check CHECK(status IN ('PENDING_ARRIVAL','AWAITING_MANAGER_REVIEW','REMEASUREMENT_REQUESTED','RECEIVED_MATCHED','RECEIVED_WITHIN_TOLERANCE','RECEIVED_SHORTAGE_APPROVED','BLOCKED_UNRESOLVED'));
ALTER TABLE ledger.receiving ADD CONSTRAINT receiving_check CHECK((status IN ('RECEIVED_MATCHED','RECEIVED_WITHIN_TOLERANCE','RECEIVED_SHORTAGE_APPROVED'))=(accepted_measurement_set_id IS NOT NULL));

REVOKE ALL ON FUNCTION ledger.receiving_threshold_snapshot_valid(uuid,numeric,text,uuid),ledger.receiving_threshold_effective(uuid),
    ledger.receiving_threshold_insert_guard(),ledger.receiving_threshold_read(uuid,text,jsonb),ledger.receiving_threshold_command(uuid,text,jsonb) FROM PUBLIC;

-- Validate immutable source, measurement, review, stock, event and lineage package.
-- Only new snapshots use policy; historical observations keep their original meaning.
CREATE OR REPLACE FUNCTION ledger.procurement_package_complete(r uuid) RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE h capture.procurements; ev capture.business_events; snapshot jsonb; state ledger.receiving; row record; item jsonb; loc uuid; commodity uuid; n integer; actual numeric; applied ledger.arrival_measurement_sets;
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
           ((SELECT count(*) FROM ledger.arrival_measurements WHERE measurement_set_id=ms.id)<>n
            OR (ms.threshold_revision IS NOT NULL AND (ms.threshold_company_id IS DISTINCT FROM h.company_id
                OR ledger.receiving_threshold_snapshot_valid(ms.threshold_company_id,ms.threshold_percentage,ms.threshold_source,ms.threshold_revision) IS DISTINCT FROM true))))
       OR EXISTS(SELECT 1 FROM ledger.arrival_measurements m JOIN ledger.arrival_measurement_sets ms ON ms.id=m.measurement_set_id
           JOIN capture.procurement_items i ON i.id=m.procurement_item_id WHERE ms.procurement_id=r AND i.procurement_id<>r)
       OR EXISTS(SELECT 1 FROM ledger.arrival_reviews v JOIN ledger.evidence e ON e.id=v.evidence_id
           WHERE v.procurement_id=r AND (e.owner_id<>h.company_id OR e.facility_id<>h.collection_site_id)) THEN RETURN false; END IF;
    SELECT * INTO applied FROM ledger.arrival_measurement_sets WHERE id=state.accepted_measurement_set_id;
    IF state.status='RECEIVED_WITHIN_TOLERANCE' AND
       (applied.threshold_revision IS NULL OR applied.threshold_company_id IS DISTINCT FROM h.company_id
        OR ledger.receiving_threshold_snapshot_valid(applied.threshold_company_id,applied.threshold_percentage,applied.threshold_source,applied.threshold_revision) IS DISTINCT FROM true
        OR state.accepted_review_id IS NOT NULL
        OR EXISTS(SELECT 1 FROM ledger.arrival_reviews WHERE measurement_set_id=state.accepted_measurement_set_id AND action='APPROVE_SHORTAGE')
        OR NOT EXISTS(SELECT 1 FROM ledger.arrival_measurements m JOIN capture.procurement_items i ON i.id=m.procurement_item_id
            WHERE m.measurement_set_id=state.accepted_measurement_set_id AND m.actual_kg<i.net_mass_kg)) THEN RETURN false; END IF;
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
           OR (item->>'LotId')::uuid IS DISTINCT FROM row.lot_id OR item->>'LotCode' IS DISTINCT FROM ledger.source_lot_code(r,row.lot_id)
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
               OR (state.status='RECEIVED_WITHIN_TOLERANCE' AND (actual>row.net_mass_kg OR (row.net_mass_kg-actual)*100>row.net_mass_kg*applied.threshold_percentage))
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

CREATE OR REPLACE FUNCTION ledger.arrival_command(a uuid,r uuid,k text,j jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE h capture.procurements; s ledger.receiving; prior ledger.arrival_operations; op uuid; set_id uuid; review_id uuid; x jsonb; v numeric; kg numeric; unit text; action text; result jsonb; normalized jsonb; threshold jsonb; percentage numeric(8,6);
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
        threshold:=ledger.receiving_threshold_effective(h.company_id);
        percentage:=(threshold->>'percentage')::numeric(8,6);
        set_id:=gen_random_uuid();
        INSERT INTO ledger.arrival_measurement_sets(id,procurement_id,operation_id,operator_id,captured_at,observations,expected_version,
            threshold_company_id,threshold_percentage,threshold_source,threshold_revision)
        VALUES(set_id,r,op,a,(j->>'capturedAt')::timestamptz,j->>'observations',s.version,
            h.company_id,percentage,threshold->>'source',(threshold->>'revision')::uuid);
        FOR x IN SELECT value FROM jsonb_array_elements(j->'items') LOOP
            IF NOT EXISTS(SELECT 1 FROM capture.procurement_items WHERE id=(x->>'procurementItemId')::uuid AND procurement_id=r)
               OR jsonb_typeof(x->'measuredValue') IS DISTINCT FROM 'string' OR x->>'measuredValue' !~ '^[0-9]{1,12}(\.[0-9]{1,12})?$' THEN RAISE EXCEPTION 'Invalid arrival line or decimal precision' USING ERRCODE='22023'; END IF;
            v:=(x->>'measuredValue')::numeric; unit:=x->>'measuredUnit';
            kg:=CASE unit WHEN 'KG' THEN v WHEN 'G' THEN v/1000 WHEN 'T' THEN v*1000 END;
            IF kg IS NULL OR kg<=0 OR kg>=100000000000000 OR trunc(kg,6)<>kg THEN RAISE EXCEPTION 'Arrival mass must be positive exact numeric(20,6) kg; zero/full loss unsupported' USING ERRCODE='22023'; END IF;
            IF NOT EXISTS(SELECT 1 FROM catalog.measurement_units WHERE code=unit AND quantity_kind='MASS' AND NOT is_deleted) THEN RAISE EXCEPTION 'Arrival mass unit not configured' USING ERRCODE='23514'; END IF;
            INSERT INTO ledger.arrival_measurements VALUES(set_id,(x->>'procurementItemId')::uuid,v,unit,kg);
        END LOOP;
        IF EXISTS(SELECT 1 FROM ledger.arrival_measurements m JOIN capture.procurement_items i ON i.id=m.procurement_item_id
            WHERE m.measurement_set_id=set_id AND (m.actual_kg>i.net_mass_kg OR (i.net_mass_kg-m.actual_kg)*100>i.net_mass_kg*percentage)) THEN
            UPDATE ledger.receiving SET status='AWAITING_MANAGER_REVIEW',version=version+1,latest_measurement_set_id=set_id WHERE procurement_id=r;
        ELSE
            UPDATE ledger.receiving SET status=CASE WHEN EXISTS(SELECT 1 FROM ledger.arrival_measurements m JOIN capture.procurement_items i ON i.id=m.procurement_item_id
                WHERE m.measurement_set_id=set_id AND m.actual_kg<i.net_mass_kg) THEN 'RECEIVED_WITHIN_TOLERANCE' ELSE 'RECEIVED_MATCHED' END,
                version=version+1,latest_measurement_set_id=set_id,accepted_measurement_set_id=set_id WHERE procurement_id=r;
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
DECLARE h capture.procurements; s ledger.receiving; snap jsonb; result jsonb; effective jsonb;
BEGIN
    PERFORM ledger.lock_writes();
    SELECT * INTO STRICT h FROM capture.procurements WHERE id=r AND receiving_workflow IS NOT NULL;
    PERFORM ledger.require_staff(a,h.collection_site_id,ARRAY['MillOperator','MillManager']);
    SELECT * INTO STRICT s FROM ledger.receiving WHERE procurement_id=r;
    SELECT payload->'result' INTO snap FROM capture.business_events WHERE id=h.event_id;
    PERFORM ledger.require_active_location((snap->>'ReceivingLocationId')::uuid);
    effective:=ledger.receiving_threshold_effective(h.company_id);
    SELECT jsonb_build_object('id',h.id,'code',h.code,'ownerCompanyId',h.company_id,'ownerCompanyName',o."Name",
        'facilityId',h.collection_site_id,'facilityName',f.name,'receivingLocationId',l.id,'receivingLocationName',l.name,
        'status',s.status,'receivingVersion',s.version,'receivedAt',h.received_at,'expectedTotalKg',snap->>'TotalNetMassKg',
        'effectiveThreshold',effective,
        'actualTotalKg',(SELECT sum(actual_kg)::text FROM ledger.arrival_measurements WHERE measurement_set_id=s.latest_measurement_set_id),
        'usableStockKg',CASE WHEN s.accepted_measurement_set_id IS NULL THEN '0' ELSE COALESCE((SELECT sum(p.quantity_kg)::text FROM ledger.positions p JOIN capture.source_lots sl ON sl.lot_id=p.lot_id JOIN capture.procurement_items i ON i.id=sl.procurement_item_id WHERE i.procurement_id=r AND NOT ledger.blocked(p.lot_id)),'0') END,
        'latestMeasurementSetId',s.latest_measurement_set_id,
        'items',(SELECT jsonb_agg(jsonb_build_object('id',i.id,'lineNo',i.line_no,'sourceLotId',sl.id,'lotId',sl.lot_id,'lotCode',ledger.source_lot_code(r,sl.lot_id),
            'plotSeasonId',i.plot_season_id,'riceVariety',ps.rice_variety,'expectedValue',i.measured_value::text,'expectedUnit',i.measured_unit,'expectedKg',i.net_mass_kg::text,
            'actualValue',m.measured_value::text,'actualUnit',m.measured_unit,'actualKg',m.actual_kg::text) ORDER BY i.line_no)
            FROM capture.procurement_items i JOIN capture.source_lots sl ON sl.procurement_item_id=i.id JOIN capture.plot_seasons ps ON ps.id=i.plot_season_id
            LEFT JOIN ledger.arrival_measurements m ON m.procurement_item_id=i.id AND m.measurement_set_id=s.latest_measurement_set_id WHERE i.procurement_id=r),
        'measurementSets',COALESCE((SELECT jsonb_agg(jsonb_build_object('id',ms.id,'operationId',ms.operation_id,'operatorId',ms.operator_id,'capturedAt',ms.captured_at,'recordedAt',ms.recorded_at,'observations',ms.observations,
            'threshold',CASE WHEN ms.threshold_revision IS NULL THEN NULL ELSE jsonb_build_object('companyId',ms.threshold_company_id,'percentage',ms.threshold_percentage::text,'source',ms.threshold_source,'revision',ms.threshold_revision) END,
            'items',(SELECT jsonb_agg(jsonb_build_object('procurementItemId',m.procurement_item_id,'measuredValue',m.measured_value::text,'measuredUnit',m.measured_unit,'actualKg',m.actual_kg::text) ORDER BY i.line_no) FROM ledger.arrival_measurements m JOIN capture.procurement_items i ON i.id=m.procurement_item_id WHERE m.measurement_set_id=ms.id)) ORDER BY ms.recorded_at,ms.id) FROM ledger.arrival_measurement_sets ms WHERE ms.procurement_id=r),'[]'::jsonb),
        'reviews',COALESCE((SELECT jsonb_agg(jsonb_build_object('id',v.id,'operationId',v.operation_id,'measurementSetId',v.measurement_set_id,'action',v.action,'reason',v.reason,'evidenceId',v.evidence_id,'managerId',v.manager_id,'recordedAt',v.recorded_at) ORDER BY v.recorded_at,v.id) FROM ledger.arrival_reviews v WHERE v.procurement_id=r),'[]'::jsonb)) INTO result
    FROM identity.organizations o JOIN ledger.facilities f ON f.id=h.collection_site_id JOIN ledger.locations l ON l.id=(snap->>'ReceivingLocationId')::uuid WHERE o."Id"=h.company_id;
    RETURN result;
END $$;

CREATE OR REPLACE FUNCTION ledger.receiving_tasks_page(a uuid,j jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE
    f uuid;
    owner_filter uuid;
    location_filter uuid;
    p integer;
    n integer;
    sort_key text;
    direction text;
    result jsonb;
BEGIN
    PERFORM ledger.lock_writes();
    f:=(j->>'facilityId')::uuid; owner_filter:=(j->>'ownerId')::uuid; location_filter:=(j->>'locationId')::uuid;
    p:=coalesce((j->>'page')::integer,1); n:=coalesce((j->>'pageSize')::integer,25);
    sort_key:=coalesce(j->>'sortBy','receivedAt'); direction:=coalesce(j->>'sortDirection','desc');
    IF f IS NULL THEN RAISE EXCEPTION 'Facility required' USING ERRCODE='22023'; END IF;
    PERFORM ledger.require_staff(a,f,ARRAY['MillOperator','MillManager']);
    IF p<1 OR n NOT BETWEEN 1 AND 100 OR (p::bigint-1)*n>2147483647 OR sort_key NOT IN ('receivedAt','status','owner') OR direction NOT IN ('asc','desc') THEN RAISE EXCEPTION 'Invalid receiving pagination or sort' USING ERRCODE='22023'; END IF;
    IF j ? 'status' AND j->>'status' NOT IN ('PENDING_ARRIVAL','AWAITING_MANAGER_REVIEW','REMEASUREMENT_REQUESTED','RECEIVED_MATCHED','RECEIVED_WITHIN_TOLERANCE','RECEIVED_SHORTAGE_APPROVED','BLOCKED_UNRESOLVED') THEN RAISE EXCEPTION 'Invalid receiving status' USING ERRCODE='22023'; END IF;
    IF j ?| ARRAY['kind','stage','lotId','ids','queueOnly'] THEN RAISE EXCEPTION 'Unsupported receiving filter' USING ERRCODE='22023'; END IF;
    WITH filtered AS MATERIALIZED (
        SELECT cp.id,cp.received_at,r.status,o."Name" owner_name
        FROM capture.procurements cp JOIN ledger.receiving r ON r.procurement_id=cp.id JOIN identity.organizations o ON o."Id"=cp.company_id
        WHERE cp.collection_site_id=f AND cp.receiving_workflow IS NOT NULL
            AND (owner_filter IS NULL OR cp.company_id=owner_filter)
            AND (j->>'status' IS NULL OR r.status=j->>'status')
            AND (location_filter IS NULL OR EXISTS(SELECT 1 FROM capture.business_events ev WHERE ev.id=cp.event_id AND ev.payload->'result'->>'ReceivingLocationId'=location_filter::text))
            AND (j->>'search' IS NULL OR concat_ws(' ',cp.code,o."Name",r.status,cp.received_at::text) ILIKE '%'||(j->>'search')||'%')
    ), ordered AS (
        SELECT x.*,row_number() OVER(ORDER BY
            CASE WHEN direction='asc' AND sort_key='receivedAt' THEN received_at END ASC NULLS LAST,
            CASE WHEN direction='desc' AND sort_key='receivedAt' THEN received_at END DESC NULLS LAST,
            CASE WHEN direction='asc' THEN CASE sort_key WHEN 'status' THEN status WHEN 'owner' THEN owner_name END END ASC NULLS LAST,
            CASE WHEN direction='desc' THEN CASE sort_key WHEN 'status' THEN status WHEN 'owner' THEN owner_name END END DESC NULLS LAST,id) ordinal FROM filtered x
    ), selected AS MATERIALIZED (
        SELECT * FROM ordered ORDER BY ordinal LIMIT n OFFSET (p::bigint-1)*n
    )
    SELECT jsonb_build_object('items',coalesce((SELECT jsonb_agg(ledger.receiving_detail(a,x.id) ORDER BY x.ordinal) FROM selected x),'[]'::jsonb),
        'page',p,'pageSize',n,'totalCount',(SELECT count(*) FROM filtered),'hasMore',(SELECT count(*) FROM filtered)>p::bigint*n) INTO result;
    RETURN result;
END $$;

REVOKE ALL ON FUNCTION ledger.procurement_package_complete(uuid),ledger.arrival_command(uuid,uuid,text,jsonb),
    ledger.receiving_detail(uuid,uuid),ledger.receiving_tasks_page(uuid,jsonb) FROM PUBLIC;
