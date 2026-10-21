-- Forward repair for databases that installed threshold functions with hardcoded LL codes.
-- Keep threshold validation/projections intact; preserve all persisted history and snapshots.
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

REVOKE ALL ON FUNCTION ledger.procurement_package_complete(uuid),ledger.receiving_detail(uuid,uuid) FROM PUBLIC;
