-- Forward-only cutover. Historical receipt rows, restrictions and capture_mock data stay untouched.
CREATE TABLE ledger.catalog_commodities (
    catalog_commodity_id uuid PRIMARY KEY REFERENCES catalog.commodities(id),
    ledger_commodity_id uuid NOT NULL UNIQUE REFERENCES ledger.commodities(id)
);
CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON ledger.catalog_commodities
FOR EACH ROW EXECUTE FUNCTION ledger.immutable();
CREATE UNIQUE INDEX lots_owner_id_id ON ledger.lots(owner_id,id);
ALTER TABLE capture.source_lots ADD CONSTRAINT source_lots_ledger_lot
FOREIGN KEY(company_id,lot_id) REFERENCES ledger.lots(owner_id,id)
DEFERRABLE INITIALLY DEFERRED NOT VALID;

CREATE FUNCTION ledger.procurement_package_complete(r uuid) RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE
    h capture.procurements;
    ev capture.business_events;
    snapshot jsonb;
    row record;
    item_snapshot jsonb;
    commodity uuid;
    location uuid;
    count_items integer;
BEGIN
    SELECT * INTO h FROM capture.procurements WHERE id=r AND status='COMMITTED';
    IF NOT FOUND OR h.event_id IS NULL THEN RETURN false; END IF;
    SELECT * INTO ev FROM capture.business_events WHERE id=h.event_id;
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
       OR NOT EXISTS(SELECT 1 FROM catalog.commodities WHERE id=commodity AND code='PADDY')
       OR NOT EXISTS(SELECT 1 FROM ledger.locations WHERE id=location AND facility_id=h.collection_site_id)
       OR NOT EXISTS(SELECT 1 FROM capture.offline_operations o WHERE o.applied_event_id=ev.id
           AND o.company_id=h.company_id AND o.submitted_by=h.received_by AND o.status='APPLIED'
           AND o.client_operation_id=ev.correlation_id AND o.result_payload=snapshot
           AND o.captured_at=(snapshot->>'CapturedAt')::timestamptz
           AND o.payload->>'operationType'='PROCUREMENT_COMMITTED') THEN RETURN false; END IF;
    SELECT count(*) INTO count_items FROM capture.procurement_items WHERE procurement_id=r;
    IF count_items NOT BETWEEN 1 AND 100 OR jsonb_typeof(snapshot->'Items') IS DISTINCT FROM 'array'
       OR jsonb_array_length(snapshot->'Items')<>count_items
       OR (SELECT count(*) FROM ledger.lots WHERE receipt_id=r)<>count_items
       OR (snapshot->>'TotalNetMassKg')::numeric IS DISTINCT FROM
          (SELECT sum(net_mass_kg) FROM capture.procurement_items WHERE procurement_id=r)
       OR (SELECT count(DISTINCT (x->>'Id')::uuid) FROM jsonb_array_elements(snapshot->'Items') x)<>count_items
       THEN RETURN false; END IF;
    FOR row IN
        SELECT i.*,sl.id source_id,sl.lot_id,ps.plot_id,ps.season_id,ps.rice_variety,p.farmer_id
        FROM capture.procurement_items i
        LEFT JOIN capture.source_lots sl ON sl.procurement_item_id=i.id AND sl.company_id=i.company_id
        JOIN capture.plot_seasons ps ON ps.id=i.plot_season_id AND ps.company_id=i.company_id
        JOIN capture.plots p ON p.id=ps.plot_id AND p.company_id=ps.company_id
        JOIN capture.crop_seasons s ON s.id=ps.season_id AND s.company_id=ps.company_id
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
           OR item_snapshot->>'LotCode' IS DISTINCT FROM upper('LL-'||replace(row.lot_id::text,'-',''))
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
           OR NOT EXISTS(SELECT 1 FROM catalog.measurement_units WHERE code=row.measured_unit AND quantity_kind='MASS')
           OR NOT EXISTS(SELECT 1 FROM ledger.lots l JOIN ledger.catalog_commodities m ON m.ledger_commodity_id=l.commodity_id
               WHERE l.id=row.lot_id AND l.owner_id=h.company_id AND l.receipt_id=r AND l.receipt_line_id=row.id
               AND l.code=item_snapshot->>'LotCode' AND l.stage='RAW_PADDY' AND l.product_identity='RAW_PADDY'
               AND m.catalog_commodity_id=commodity)
           OR (SELECT count(*) FROM ledger.events e WHERE e.lot_id=row.lot_id AND e.kind='RECEIPT')<>1
           OR NOT EXISTS(SELECT 1 FROM ledger.events e JOIN ledger.postings pp ON pp.event_id=e.id
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
               AND (SELECT count(*) FROM ledger.postings WHERE event_id=e.id)=1)
           OR NOT EXISTS(SELECT 1 FROM ledger.contributions WHERE lot_id=row.lot_id AND source_lot_id=row.lot_id AND quantity_kg=row.net_mass_kg)
           OR (SELECT count(*) FROM ledger.contributions WHERE lot_id=row.lot_id)<>1 THEN RETURN false; END IF;
    END LOOP;
    RETURN true;
END $$;

CREATE FUNCTION ledger.procurement_receipt(a uuid,o uuid,ev uuid,r uuid,c uuid,f uuid,d uuid,lines jsonb) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE
    h capture.procurements;
    snapshot jsonb;
    commodity uuid;
    row record;
    event_id uuid;
    lot_code text;
BEGIN
    PERFORM ledger.lock_writes();
    IF ledger.role(a) IS DISTINCT FROM 'PurchasingStaff' OR NOT ledger.own(a,o)
       OR NOT EXISTS(SELECT 1 FROM identity.organizations WHERE "Id"=o AND "Kind"='Customer') THEN
        RAISE EXCEPTION 'Procurement company authority denied' USING ERRCODE='42501';
    END IF;
    SELECT * INTO STRICT h FROM capture.procurements WHERE id=r AND company_id=o AND status='COMMITTED';
    SELECT payload->'result' INTO STRICT snapshot FROM capture.business_events
    WHERE id=ev AND id=h.event_id AND company_id=o AND actor_id=a AND event_type='PROCUREMENT_COMMITTED';
    IF h.received_by<>a OR h.collection_site_id<>f
       OR (snapshot->>'CommodityId')::uuid IS DISTINCT FROM c
       OR (snapshot->>'ReceivingLocationId')::uuid IS DISTINCT FROM d
       OR NOT EXISTS(SELECT 1 FROM catalog.commodities WHERE id=c AND code='PADDY')
       OR NOT EXISTS(SELECT 1 FROM ledger.locations WHERE id=d AND facility_id=f AND kind IN ('STORE','SILO','COMPARTMENT')
           AND (owner_id IS NULL AND (kind<>'COMPARTMENT' OR cleared) OR owner_id=o))
       OR jsonb_typeof(lines) IS DISTINCT FROM 'array' OR jsonb_array_length(lines) NOT BETWEEN 1 AND 100
       OR jsonb_array_length(lines)<>(SELECT count(*) FROM capture.procurement_items WHERE procurement_id=r)
       OR (SELECT count(DISTINCT (x->>'procurementItemId')::uuid) FROM jsonb_array_elements(lines) x)<>jsonb_array_length(lines)
       OR EXISTS(SELECT 1 FROM jsonb_array_elements(lines) x WHERE NOT EXISTS(
           SELECT 1 FROM capture.procurement_items i JOIN capture.source_lots sl ON sl.procurement_item_id=i.id
           WHERE i.procurement_id=r AND i.company_id=o AND sl.company_id=o
           AND i.id=(x->>'procurementItemId')::uuid AND sl.id=(x->>'sourceLotId')::uuid
           AND sl.lot_id=(x->>'lotId')::uuid AND i.net_mass_kg=(x->>'netMassKg')::numeric
           AND x->>'lotCode'=upper('LL-'||replace(sl.lot_id::text,'-','')))) THEN
        RAISE EXCEPTION 'Persisted procurement package or receiving option mismatch' USING ERRCODE='23514';
    END IF;
    IF EXISTS(SELECT 1 FROM ledger.lots WHERE receipt_id=r) THEN
        IF ledger.procurement_package_complete(r) IS DISTINCT FROM true THEN
            RAISE EXCEPTION 'Procurement replay conflicts with committed stock' USING ERRCODE='23514';
        END IF;
        RETURN;
    END IF;
    SELECT ledger_commodity_id INTO commodity FROM ledger.catalog_commodities WHERE catalog_commodity_id=c;
    IF NOT FOUND THEN
        commodity:=gen_random_uuid();
        INSERT INTO ledger.commodities(id,name) SELECT commodity,'Catalog '||code||' ['||id::text||']' FROM catalog.commodities WHERE id=c;
        INSERT INTO ledger.catalog_commodities VALUES(c,commodity);
    END IF;
    PERFORM ledger.credit_location(d,o);
    FOR row IN
        SELECT i.*,sl.id source_id,sl.lot_id,ps.plot_id,ps.season_id,ps.rice_variety
        FROM capture.procurement_items i JOIN capture.source_lots sl ON sl.procurement_item_id=i.id AND sl.company_id=i.company_id
        JOIN capture.plot_seasons ps ON ps.id=i.plot_season_id AND ps.company_id=i.company_id
        WHERE i.procurement_id=r ORDER BY i.line_no
    LOOP
        lot_code:=upper('LL-'||replace(row.lot_id::text,'-',''));
        INSERT INTO ledger.lots(id,code,owner_id,commodity_id,stage,product_identity,receipt_id,receipt_line_id,code_origin_id)
        VALUES(row.lot_id,lot_code,o,commodity,'RAW_PADDY','RAW_PADDY',r,row.id,ledger.register_code(lot_code,row.lot_id,NULL,NULL));
        event_id:=gen_random_uuid();
        INSERT INTO ledger.events(id,kind,actor_id,lot_id,details,occurred_at)
        VALUES(event_id,'RECEIPT',a,row.lot_id,jsonb_build_object('receiptId',r,'receiptLineId',row.id,
            'captureEventId',ev,'sourceLotId',row.source_id,'catalogCommodityId',c,'farmerId',h.farmer_id,
            'plotSeasonId',row.plot_season_id,'plotId',row.plot_id,'seasonId',row.season_id,'riceVariety',row.rice_variety,
            'originalQuantity',row.measured_value::text,'originalUnit',row.measured_unit,
            'quantityKg',row.net_mass_kg::text,'moisturePercent',row.moisture_percent::text),h.received_at);
        PERFORM ledger.post(event_id,row.lot_id,d,row.net_mass_kg);
        INSERT INTO ledger.contributions VALUES(row.lot_id,row.lot_id,row.net_mass_kg);
    END LOOP;
    IF ledger.procurement_package_complete(r) IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'Procurement, provenance and stock package incomplete' USING ERRCODE='23514';
    END IF;
END $$;

CREATE OR REPLACE FUNCTION ledger.complete_receipt_package() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
BEGIN
    IF TG_TABLE_NAME<>'lots' THEN
        RAISE EXCEPTION 'Procurement corrections are unsupported by official Capture' USING ERRCODE='23514';
    END IF;
    IF NEW.receipt_id IS NOT NULL AND ledger.procurement_package_complete(NEW.receipt_id) IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'Procurement, source lot, event and credit must complete atomically' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END $$;
CREATE OR REPLACE FUNCTION ledger.complete_receiving_resolution() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
BEGIN
    RAISE EXCEPTION 'Arrival and receiving reconciliation are unsupported by official Capture' USING ERRCODE='23514';
END $$;
CREATE TRIGGER preserve_receiving_restrictions BEFORE UPDATE OR DELETE ON ledger.receiving_blocks
FOR EACH ROW EXECUTE FUNCTION ledger.immutable();
CREATE TRIGGER preserve_receipt_restrictions BEFORE INSERT OR UPDATE OR DELETE ON ledger.receipt_correction_blocks
FOR EACH ROW EXECUTE FUNCTION ledger.immutable();
DROP FUNCTION ledger.receipt(uuid,jsonb);
DROP FUNCTION ledger.receipt_correction_request(uuid,uuid,uuid,uuid);
DROP FUNCTION ledger.reverse_receipt(uuid,uuid,uuid,uuid,uuid,text);
DROP FUNCTION ledger.receiving_block(uuid,uuid,uuid,boolean,uuid,text);

CREATE FUNCTION ledger.capture_procurement_immutable() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE committed boolean;
BEGIN
    IF TG_TABLE_NAME='procurements' THEN committed:=OLD.status='COMMITTED';
    ELSIF TG_TABLE_NAME='procurement_items' THEN
        SELECT status='COMMITTED' INTO committed FROM capture.procurements WHERE id=OLD.procurement_id;
    ELSIF TG_TABLE_NAME='source_lots' THEN
        SELECT h.status='COMMITTED' INTO committed FROM capture.procurement_items i
        JOIN capture.procurements h ON h.id=i.procurement_id WHERE i.id=OLD.procurement_item_id;
    ELSIF TG_TABLE_NAME='business_events' THEN committed:=OLD.event_type='PROCUREMENT_COMMITTED';
    ELSE committed:=OLD.payload->>'operationType'='PROCUREMENT_COMMITTED'; END IF;
    IF committed THEN RAISE EXCEPTION 'Committed procurement history is immutable' USING ERRCODE='23514'; END IF;
    IF TG_OP='DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END $$;
CREATE FUNCTION ledger.capture_procurement_package() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE receipt uuid;
BEGIN
    IF TG_TABLE_NAME='procurements' THEN receipt:=NEW.id;
    ELSIF TG_TABLE_NAME='procurement_items' THEN receipt:=NEW.procurement_id;
    ELSIF TG_TABLE_NAME='source_lots' THEN
        SELECT procurement_id INTO receipt FROM capture.procurement_items WHERE id=NEW.procurement_item_id;
    ELSIF TG_TABLE_NAME='business_events' THEN
        IF NEW.event_type<>'PROCUREMENT_COMMITTED' THEN RETURN NEW; END IF;
        receipt:=(NEW.payload->>'procurementId')::uuid;
    ELSE
        IF NEW.payload->>'operationType' IS DISTINCT FROM 'PROCUREMENT_COMMITTED' THEN RETURN NEW; END IF;
        SELECT id INTO receipt FROM capture.procurements WHERE event_id=NEW.applied_event_id;
    END IF;
    IF TG_TABLE_NAME IN ('business_events','offline_operations') OR receipt IS NULL
       OR EXISTS(SELECT 1 FROM capture.procurements WHERE id=receipt AND status='COMMITTED') THEN
        IF ledger.procurement_package_complete(receipt) IS DISTINCT FROM true THEN
            RAISE EXCEPTION 'Committed procurement requires complete immutable provenance and Ledger stock' USING ERRCODE='23514';
        END IF;
    END IF;
    RETURN NEW;
END $$;
DO $$ DECLARE t text; BEGIN
    FOREACH t IN ARRAY ARRAY['procurements','procurement_items','source_lots','business_events','offline_operations'] LOOP
        EXECUTE format('CREATE TRIGGER procurement_immutable BEFORE UPDATE OR DELETE ON capture.%I FOR EACH ROW EXECUTE FUNCTION ledger.capture_procurement_immutable()',t);
        EXECUTE format('CREATE CONSTRAINT TRIGGER procurement_package AFTER INSERT OR UPDATE ON capture.%I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ledger.capture_procurement_package()',t);
    END LOOP;
END $$;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA ledger FROM PUBLIC;
