CREATE FUNCTION ledger.procurement_stock_bulk(o uuid, source_ids uuid[])
RETURNS TABLE(source_lot_id uuid, current_stock_lot_id uuid, current_stock_lot_code text,
    location_id uuid, on_hand_kg numeric, available_kg numeric, pool_total_kg numeric,
    is_mixed_bulk boolean, pool_revision bigint)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,ledger,capture AS $$
    WITH sources AS MATERIALIZED (
        SELECT s.lot_id AS root_lot_id
        FROM capture.source_lots s
        JOIN ledger.lots root ON root.id=s.lot_id AND root.owner_id=s.company_id
        WHERE s.company_id=o AND s.lot_id=ANY(source_ids)
    ), shares AS MATERIALIZED (
        SELECT c.lot_id,c.source_lot_id,sum(c.quantity_kg) AS creation_kg
        FROM ledger.contributions c
        GROUP BY c.lot_id,c.source_lot_id
    ), creation_totals AS MATERIALIZED (
        SELECT lot_id,sum(creation_kg) AS quantity_kg
        FROM shares GROUP BY lot_id
    ), positions AS MATERIALIZED (
        SELECT p.lot_id,p.location_id,p.quantity_kg,lc.facility_id,lc.mixed_bulk,
            CASE WHEN lc.mixed_bulk THEN mbp.revision END AS pool_revision
        FROM ledger.positions p
        JOIN ledger.locations lc ON lc.id=p.location_id
        LEFT JOIN ledger.mixed_bulk_pools mbp ON mbp.location_id=lc.id
        WHERE p.quantity_kg>0 AND (NOT lc.mixed_bulk OR mbp.lot_id=p.lot_id)
    ), raw_shares AS MATERIALIZED (
        SELECT p.*,sh.source_lot_id,
            sh.creation_kg*p.quantity_kg/NULLIF(t.quantity_kg,0) AS raw_kg
        FROM positions p
        JOIN shares sh ON sh.lot_id=p.lot_id
        JOIN creation_totals t ON t.lot_id=p.lot_id
        WHERE sh.creation_kg>0
    ), ranked_shares AS MATERIALIZED (
        SELECT r.*,
            trunc(r.raw_kg*1000000) AS whole_micro_kg,
            row_number() OVER (PARTITION BY r.lot_id,r.location_id
                ORDER BY r.raw_kg*1000000-trunc(r.raw_kg*1000000) DESC,r.source_lot_id) AS remainder_rank,
            round(r.quantity_kg*1000000)
                - sum(trunc(r.raw_kg*1000000)) OVER (PARTITION BY r.lot_id,r.location_id) AS remainder_micro_kg
        FROM raw_shares r
    ), allocated AS MATERIALIZED (
        SELECT r.*, (r.whole_micro_kg+
            CASE WHEN r.remainder_rank<=r.remainder_micro_kg THEN 1 ELSE 0 END)/1000000 AS allocated_kg
        FROM ranked_shares r
    )
    SELECT s.root_lot_id,l.id,l.code,p.location_id,a.allocated_kg,
        CASE WHEN ledger.blocked(l.id) THEN 0::numeric ELSE a.allocated_kg END,
        CASE WHEN p.mixed_bulk THEN p.quantity_kg ELSE 0::numeric END,
        p.mixed_bulk,p.pool_revision
    FROM sources s
    JOIN allocated a ON a.source_lot_id=s.root_lot_id
    JOIN positions p ON p.lot_id=a.lot_id AND p.location_id=a.location_id
    JOIN ledger.lots l ON l.id=p.lot_id AND l.owner_id=o
    WHERE a.allocated_kg>0
    ORDER BY s.root_lot_id,p.location_id,l.id
$$;

CREATE FUNCTION ledger.procurement_source_lineage(o uuid,source_id uuid)
RETURNS TABLE(lot_id uuid,lot_code text,location_id uuid,is_current_pool boolean,
    pool_revision bigint,quantity_kg numeric)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,ledger,capture AS $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM capture.source_lots s
        JOIN ledger.lots root ON root.id=s.lot_id AND root.owner_id=s.company_id
        WHERE s.company_id=o AND s.lot_id=source_id
    ) AND NOT EXISTS (
        SELECT 1 FROM ledger.positions p WHERE p.lot_id=source_id
    ) AND NOT EXISTS (
        SELECT 1 FROM ledger.procurement_stock_bulk(o,ARRAY[source_id]) stock WHERE stock.on_hand_kg>0
    ) THEN
        RAISE EXCEPTION 'Accepted source lot has no stock position' USING ERRCODE='GL001';
    END IF;
    RETURN QUERY
    WITH RECURSIVE source_root AS MATERIALIZED (
        SELECT s.lot_id FROM capture.source_lots s
        JOIN ledger.lots root ON root.id=s.lot_id AND root.owner_id=s.company_id
        WHERE s.company_id=o AND s.lot_id=source_id
    ), lineage_edges(input_lot_id,output_lot_id) AS (
        SELECT ce.input_lot_id,ce.output_lot_id
        FROM ledger.custody_edges ce
        JOIN ledger.custody_allocations ca ON ca.event_id=ce.event_id AND ca.output_lot_id=ce.output_lot_id
        WHERE ca.source_lot_id=source_id AND ca.quantity_kg>0
        UNION
        SELECT cpa.input_lot_id,cpa.output_lot_id
        FROM ledger.custody_processing_allocations cpa
        WHERE cpa.source_lot_id=source_id AND cpa.quantity_kg>0
        UNION
        SELECT c.source_lot_id,c.lot_id
        FROM ledger.contributions c WHERE c.quantity_kg>0
    ), descendants(lot_id) AS (
        SELECT sr.lot_id FROM source_root sr
        UNION
        SELECT edge.output_lot_id FROM descendants d
        JOIN lineage_edges edge ON edge.input_lot_id=d.lot_id
    ), stock AS MATERIALIZED (
        SELECT * FROM ledger.procurement_stock_bulk(o,ARRAY[source_id])
    ), scoped AS MATERIALIZED (
        SELECT d.lot_id,l.code,
            coalesce(stock.location_id,(SELECT ce.location_id FROM ledger.custody_events ce
                WHERE ce.input_lot_id=d.lot_id OR ce.output_lot_id=d.lot_id
                ORDER BY ce.created_at DESC,ce.event_id DESC LIMIT 1)) AS location_id,
            coalesce(stock.is_mixed_bulk,false) AS is_mixed_bulk,
            stock.pool_revision,coalesce(stock.on_hand_kg,0::numeric) AS quantity_kg
        FROM descendants d
        JOIN ledger.lots l ON l.id=d.lot_id AND l.owner_id=o
        LEFT JOIN stock ON stock.current_stock_lot_id=d.lot_id
    )
    SELECT s.lot_id,s.code,s.location_id,
        s.is_mixed_bulk AND s.pool_revision IS NOT NULL,
        CASE WHEN s.is_mixed_bulk THEN s.pool_revision END,
        s.quantity_kg
    FROM scoped s
    ORDER BY s.lot_id,s.location_id;
END $$;

REVOKE ALL ON FUNCTION ledger.procurement_stock_bulk(uuid,uuid[]) FROM PUBLIC;
REVOKE ALL ON FUNCTION ledger.procurement_source_lineage(uuid,uuid) FROM PUBLIC;
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
        'usableStockKg',CASE WHEN s.accepted_measurement_set_id IS NULL THEN '0' ELSE COALESCE((
            SELECT sum(stock.available_kg)::text
            FROM ledger.procurement_stock_bulk(h.company_id,(
                SELECT array_agg(sl.lot_id) FROM capture.source_lots sl
                JOIN capture.procurement_items i ON i.id=sl.procurement_item_id
                WHERE i.procurement_id=r AND sl.company_id=h.company_id
            )) stock
        ),'0') END,
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
ALTER FUNCTION ledger.read(uuid,text,jsonb) RENAME TO read_before_mixed_bulk;
REVOKE ALL ON FUNCTION ledger.read_before_mixed_bulk(uuid,text,jsonb) FROM PUBLIC;
DO $$
DECLARE granted record;
BEGIN
    FOR granted IN
        SELECT DISTINCT role.rolname FROM pg_proc p
        CROSS JOIN LATERAL aclexplode(p.proacl) acl
        JOIN pg_roles role ON role.oid=acl.grantee
        WHERE p.oid='ledger.read_before_mixed_bulk(uuid,text,jsonb)'::regprocedure
          AND acl.privilege_type='EXECUTE' AND acl.grantee<>p.proowner
    LOOP
        EXECUTE format('REVOKE EXECUTE ON FUNCTION ledger.read_before_mixed_bulk(uuid,text,jsonb) FROM %I',granted.rolname);
    END LOOP;
END $$;

CREATE OR REPLACE FUNCTION ledger.lot_json(a uuid,l uuid,detail boolean DEFAULT false) RETURNS jsonb
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger AS $$
    SELECT jsonb_build_object(
        'id',x.id,'code',x.code,'ownerId',x.owner_id,'commodityId',x.commodity_id,
        'stage',x.stage,'productIdentity',x.product_identity,
        'positions',coalesce((
            SELECT jsonb_agg(jsonb_build_object(
                'locationId',p.location_id,'facilityId',lc.facility_id,
                'quantityKg',p.quantity_kg::text,
                'availableKg',CASE WHEN ledger.blocked(l) THEN '0.000000' ELSE p.quantity_kg::text END
            ) || CASE WHEN lc.mixed_bulk AND pool.lot_id=l THEN jsonb_build_object('poolRevision',pool.revision) ELSE '{}'::jsonb END)
            FROM ledger.positions p JOIN ledger.locations lc ON lc.id=p.location_id
            LEFT JOIN ledger.mixed_bulk_pools pool ON pool.location_id=lc.id
            WHERE p.lot_id=l AND (ledger.own(a,x.owner_id) OR ledger.assigned(a,lc.facility_id))
        ),'[]'::jsonb),
        'restrictions',to_jsonb(array_remove(ARRAY[
            CASE WHEN EXISTS(SELECT 1 FROM ledger.effective_hold_ids(l)) THEN 'HOLD' END,
            CASE WHEN EXISTS (
                SELECT 1 FROM ledger.corrections
                WHERE operation_id=x.operation_id AND status IN ('REQUESTED','RECONCILED')
            ) OR EXISTS (
                SELECT 1 FROM ledger.receipt_correction_blocks WHERE receipt_id=x.receipt_id
            ) THEN 'CORRECTION' END,
            CASE WHEN ledger.receiving_restricted(l) THEN 'RECEIVING_DISCREPANCY' END,
            CASE WHEN EXISTS (
                SELECT 1 FROM ledger.receipt_reversals WHERE receipt_id=x.receipt_id
            ) OR EXISTS (
                SELECT 1 FROM ledger.operations WHERE id=x.operation_id AND status='CORRECTED'
            ) THEN 'SUPERSEDED' END
        ],NULL)),
        'mixedBulk',EXISTS(SELECT 1 FROM ledger.custody_edges ce WHERE ce.input_lot_id=l OR ce.output_lot_id=l)
            OR EXISTS(SELECT 1 FROM ledger.positions p JOIN ledger.locations lc ON lc.id=p.location_id WHERE p.lot_id=l AND lc.mixed_bulk),
        'poolLocationId',coalesce(
            (SELECT pool.location_id FROM ledger.mixed_bulk_pools pool WHERE pool.lot_id=l),
            (SELECT ce.location_id FROM ledger.custody_events ce WHERE ce.input_lot_id=l OR ce.output_lot_id=l ORDER BY ce.created_at DESC LIMIT 1)),
        'poolRevision',(SELECT pool.revision FROM ledger.mixed_bulk_pools pool WHERE pool.lot_id=l),
        'isCurrentPool',EXISTS(SELECT 1 FROM ledger.mixed_bulk_pools pool WHERE pool.lot_id=l),
        'custodyDerived',ledger.mixed_bulk_custody_derived(l),
        'sourceComposition',CASE WHEN detail THEN coalesce((
            SELECT jsonb_agg(jsonb_build_object('sourceLotId',shares.source_lot_id,'sourceLotCode',source.code,'quantityKg',shares.quantity_kg::text) ORDER BY shares.source_lot_id)
            FROM (
                SELECT ca.source_lot_id,sum(ca.quantity_kg) quantity_kg FROM ledger.custody_allocations ca
                WHERE ca.output_lot_id=l GROUP BY ca.source_lot_id
                UNION ALL
                SELECT cpa.source_lot_id,sum(cpa.quantity_kg) FROM ledger.custody_processing_allocations cpa
                WHERE cpa.output_lot_id=l GROUP BY cpa.source_lot_id
                UNION ALL
                SELECT c.source_lot_id,sum(c.quantity_kg) FROM ledger.contributions c
                WHERE c.lot_id=l
                  AND NOT EXISTS(SELECT 1 FROM ledger.custody_allocations ca WHERE ca.output_lot_id=l)
                  AND NOT EXISTS(SELECT 1 FROM ledger.custody_processing_allocations cpa WHERE cpa.output_lot_id=l)
                GROUP BY c.source_lot_id
            ) shares JOIN ledger.lots source ON source.id=shares.source_lot_id
        ),'[]'::jsonb) ELSE '[]'::jsonb END
    ) || CASE WHEN detail AND ledger.role(a)<>'MillOperator' THEN
        jsonb_build_object(
            'contributions',coalesce((
                SELECT jsonb_agg(jsonb_build_object('sourceLotId',source_lot_id,'quantityKg',quantity_kg::text))
                FROM ledger.contributions WHERE lot_id=l
            ),'[]'::jsonb),
            'custodyLinks',coalesce((
                SELECT jsonb_agg(jsonb_build_object('eventId',links.event_id,'inputLotId',links.input_lot_id,'outputLotId',links.output_lot_id,'quantityKg',links.quantity_kg::text,'locationId',links.location_id,'poolRevision',links.revision) ORDER BY links.created_at,links.event_id)
                FROM (
                    SELECT ce.event_id,ce.input_lot_id,ce.output_lot_id,ce.quantity_kg,ce.location_id,ce.revision,ce.created_at
                    FROM ledger.custody_events ce WHERE ce.input_lot_id=l OR ce.output_lot_id=l
                    UNION ALL
                    SELECT ce.event_id,cpa.input_lot_id,cpa.output_lot_id,cpa.quantity_kg,ce.location_id,ce.revision,ce.created_at
                    FROM ledger.custody_processing_allocations cpa JOIN ledger.custody_events ce ON ce.event_id=cpa.event_id
                    WHERE cpa.input_lot_id=l OR cpa.output_lot_id=l
                ) links
                WHERE ledger.can_read_lot(a,coalesce(links.input_lot_id,links.output_lot_id))
            ),'[]'::jsonb)
        )
    ELSE '{}'::jsonb END
    FROM ledger.lots x
    WHERE x.id=l AND ledger.can_read_lot(a,l)
$$;

CREATE FUNCTION ledger.available_lots_page(a uuid,p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE
    page_number integer:=coalesce((p->>'page')::integer,1);
    page_size integer:=coalesce((p->>'pageSize')::integer,25);
    sort_key text:=coalesce(p->>'sortBy','code');
    direction text:=coalesce(p->>'sortDirection','asc');
    requested_owner uuid:=NULLIF(p->>'ownerId','')::uuid;
    requested_facility uuid:=NULLIF(p->>'facilityId','')::uuid;
    requested_location uuid:=NULLIF(p->>'locationId','')::uuid;
    requested_ids uuid[];
    result jsonb;
BEGIN
    IF ledger.role(a) IS NULL THEN RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501'; END IF;
    IF page_number<1 OR page_size NOT BETWEEN 1 AND 100 OR (page_number::bigint-1)*page_size>2147483647
       OR direction NOT IN ('asc','desc') OR NOT sort_key=ANY(ARRAY['code','stage','owner','quantity']) THEN
        RAISE EXCEPTION 'Invalid pagination or sort direction' USING ERRCODE='22023';
    END IF;
    IF p ? 'ids' THEN
        requested_ids:=string_to_array(p->>'ids',',')::uuid[];
        IF cardinality(requested_ids) NOT BETWEEN 1 AND 100 THEN RAISE EXCEPTION 'IDs requires 1–100 UUIDs' USING ERRCODE='22023'; END IF;
    END IF;
    IF p ? 'stage' AND p->>'stage' NOT IN ('RAW_PADDY','DRIED_PADDY','HEAD_RICE','BROKEN_RICE','BRAN','HUSK','BLENDED_RICE','BAGGED_RICE') THEN
        RAISE EXCEPTION 'Invalid stock stage' USING ERRCODE='22023';
    END IF;
    IF p ? 'status' THEN RAISE EXCEPTION 'Invalid status filter' USING ERRCODE='22023'; END IF;
    IF p ? 'kind' THEN RAISE EXCEPTION 'Invalid kind filter' USING ERRCODE='22023'; END IF;
    IF p ? 'queueOnly' THEN RAISE EXCEPTION 'Invalid queue filter' USING ERRCODE='22023'; END IF;
    WITH candidates AS MATERIALIZED (
        SELECT l.id,l.owner_id,o."Name" owner_name,l.code,l.stage,
            coalesce((SELECT sum(pos.quantity_kg) FROM ledger.positions pos
                JOIN ledger.locations lc ON lc.id=pos.location_id
                LEFT JOIN ledger.mixed_bulk_pools pool ON pool.location_id=lc.id
                WHERE pos.lot_id=l.id AND pos.quantity_kg>0
                  AND (NOT lc.mixed_bulk OR pool.lot_id=l.id)
                  AND (ledger.own(a,l.owner_id) OR ledger.assigned(a,lc.facility_id))
                  AND (requested_facility IS NULL OR lc.facility_id=requested_facility)
                  AND (requested_location IS NULL OR lc.id=requested_location)),0) quantity
        FROM ledger.lots l JOIN identity.organizations o ON o."Id"=l.owner_id
        WHERE ledger.can_read_lot(a,l.id)
          AND (requested_owner IS NULL OR l.owner_id=requested_owner)
          AND (p->>'stage' IS NULL OR l.stage=p->>'stage')
          AND (requested_facility IS NULL AND requested_location IS NULL OR EXISTS(
              SELECT 1 FROM ledger.positions pos JOIN ledger.locations lc ON lc.id=pos.location_id
              WHERE pos.lot_id=l.id
                AND (ledger.own(a,l.owner_id) OR ledger.assigned(a,lc.facility_id))
                AND (requested_facility IS NULL OR lc.facility_id=requested_facility)
                AND (requested_location IS NULL OR lc.id=requested_location)))
          AND (requested_ids IS NULL OR l.id=ANY(requested_ids))
          AND (requested_ids IS NOT NULL OR NOT (
              EXISTS(SELECT 1 FROM ledger.custody_events ce WHERE ce.input_lot_id=l.id OR ce.output_lot_id=l.id)
              OR EXISTS(SELECT 1 FROM ledger.custody_allocations ca WHERE ca.input_lot_id=l.id OR ca.output_lot_id=l.id)
              OR ledger.mixed_bulk_custody_derived(l.id)
          ) OR EXISTS(
              SELECT 1 FROM ledger.positions pos
              JOIN ledger.locations lc ON lc.id=pos.location_id
              LEFT JOIN ledger.mixed_bulk_pools pool ON pool.location_id=lc.id
              WHERE pos.lot_id=l.id AND pos.quantity_kg>0
                AND (NOT lc.mixed_bulk OR pool.lot_id=l.id)
                AND (ledger.own(a,l.owner_id) OR ledger.assigned(a,lc.facility_id))
                AND (requested_facility IS NULL OR lc.facility_id=requested_facility)
                AND (requested_location IS NULL OR lc.id=requested_location)))
          AND (p->>'search' IS NULL OR concat_ws(' ',l.code,l.stage,o."Name") ILIKE '%'||(p->>'search')||'%')
    ), ordered AS (
        SELECT c.*,row_number() OVER (ORDER BY
            CASE WHEN direction='asc' THEN CASE sort_key WHEN 'code' THEN c.code WHEN 'stage' THEN c.stage WHEN 'owner' THEN c.owner_name END END ASC NULLS LAST,
            CASE WHEN direction='desc' THEN CASE sort_key WHEN 'code' THEN c.code WHEN 'stage' THEN c.stage WHEN 'owner' THEN c.owner_name END END DESC NULLS LAST,
            CASE WHEN direction='asc' AND sort_key='quantity' THEN c.quantity END ASC NULLS LAST,
            CASE WHEN direction='desc' AND sort_key='quantity' THEN c.quantity END DESC NULLS LAST,c.id) ordinal
        FROM candidates c
    ), selected AS MATERIALIZED (
        SELECT * FROM ordered WHERE ordinal>(page_number::bigint-1)*page_size AND ordinal<=page_number::bigint*page_size
    )
    SELECT jsonb_build_object(
        'items',coalesce((SELECT jsonb_agg(ledger.lot_json(a,x.id,false)||jsonb_build_object('ownerName',x.owner_name) ORDER BY x.ordinal) FROM selected x),'[]'::jsonb),
        'page',page_number,'pageSize',page_size,'totalCount',(SELECT count(*) FROM candidates),
        'hasMore',(SELECT count(*) FROM candidates)>page_number::bigint*page_size)
    INTO result;
    RETURN result;
END $$;

CREATE FUNCTION ledger.read(a uuid,action text,p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE
    result jsonb;
    page_number integer:=coalesce((p->>'page')::integer,1);
    page_size integer:=coalesce((p->>'pageSize')::integer,25);
    page_result jsonb;
    all_items jsonb:='[]'::jsonb;
    new_items jsonb:='[]'::jsonb;
    page_count integer;
    total_count integer:=0;
    n integer;
    requested_lot uuid:=NULLIF(p->>'lotId','')::uuid;
    requested_facility uuid:=NULLIF(p->>'facilityId','')::uuid;
    requested_location uuid:=NULLIF(p->>'locationId','')::uuid;
BEGIN
    IF action='history' THEN
        result:=ledger.read_before_mixed_bulk(a,action,p);
        page_result:=ledger.read_before_mixed_bulk(a,action,
            jsonb_set(jsonb_set(p,'{page}','1'::jsonb,true),'{pageSize}','100'::jsonb,true));
        total_count:=(page_result->>'totalCount')::integer;
        all_items:=coalesce(page_result->'items','[]'::jsonb);
        page_count:=ceil(total_count::numeric/100)::integer;
        FOR n IN 2..page_count LOOP
            page_result:=ledger.read_before_mixed_bulk(a,action,
                jsonb_set(jsonb_set(p,'{page}',to_jsonb(n),true),'{pageSize}','100'::jsonb,true));
            all_items:=all_items||coalesce(page_result->'items','[]'::jsonb);
        END LOOP;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',ce.event_id,'postingId',NULL,'kind',ce.command_kind,'occurredAt',ce.created_at,
            'actorId',ce.actor_id,'operationId',NULL,'lotId',requested_lot,'locationId',ce.location_id,
            'ownerId',event_lot.owner_id,'ownerName',org."Name",'quantityKg',ce.quantity_kg::text,
            'reversesPostingId',NULL,
            'details',jsonb_build_object('action',ce.action,'poolRevision',ce.revision,
                'inputLotId',ce.input_lot_id,'outputLotId',ce.output_lot_id,
                'allocations',coalesce((SELECT jsonb_agg(jsonb_build_object(
                    'inputLotId',allocation.input_lot_id,'outputLotId',allocation.output_lot_id,
                    'sourceLotId',allocation.source_lot_id,'quantityKg',allocation.quantity_kg::text)
                    ORDER BY allocation.input_lot_id,allocation.output_lot_id,allocation.source_lot_id)
                    FROM (
                        SELECT ca.input_lot_id,ca.output_lot_id,ca.source_lot_id,ca.quantity_kg FROM ledger.custody_allocations ca WHERE ca.event_id=ce.event_id
                        UNION ALL
                        SELECT cpa.input_lot_id,cpa.output_lot_id,cpa.source_lot_id,cpa.quantity_kg FROM ledger.custody_processing_allocations cpa WHERE cpa.event_id=ce.event_id
                    ) allocation),'[]'::jsonb))
        ) ORDER BY ce.created_at,ce.event_id),'[]'::jsonb)
        INTO new_items
        FROM ledger.custody_events ce
        JOIN ledger.lots event_lot ON event_lot.id=coalesce(ce.output_lot_id,ce.input_lot_id)
        JOIN identity.organizations org ON org."Id"=event_lot.owner_id
        WHERE (requested_lot IS NULL OR ce.input_lot_id=requested_lot OR ce.output_lot_id=requested_lot)
          AND (requested_facility IS NULL OR EXISTS(SELECT 1 FROM ledger.locations lc WHERE lc.id=ce.location_id AND lc.facility_id=requested_facility))
          AND (requested_location IS NULL OR ce.location_id=requested_location)
          AND (ledger.own(a,event_lot.owner_id) OR ledger.assigned(a,(SELECT lc.facility_id FROM ledger.locations lc WHERE lc.id=ce.location_id)))
          AND (requested_lot IS NULL OR ledger.can_read_lot(a,requested_lot))
          AND (p->>'ownerId' IS NULL OR event_lot.owner_id=(p->>'ownerId')::uuid)
          AND (p->>'kind' IS NULL OR ce.command_kind=p->>'kind')
          AND (p->>'search' IS NULL OR concat_ws(' ',event_lot.code,ce.command_kind,ce.action) ILIKE '%'||(p->>'search')||'%')
          AND NOT EXISTS (
              SELECT 1 FROM jsonb_array_elements(all_items) old
              WHERE old->>'id'=ce.event_id::text AND old->>'postingId' IS NULL
          );
        all_items:=all_items||new_items;
        WITH candidates AS MATERIALIZED (
            SELECT item FROM jsonb_array_elements(all_items) item
        ), ordered AS (
            SELECT item,row_number() OVER (ORDER BY
                CASE WHEN coalesce(p->>'sortDirection','asc')='asc' AND coalesce(p->>'sortBy','occurredAt')='occurredAt' THEN (item->>'occurredAt')::timestamptz END ASC NULLS LAST,
                CASE WHEN coalesce(p->>'sortDirection','asc')='desc' AND coalesce(p->>'sortBy','occurredAt')='occurredAt' THEN (item->>'occurredAt')::timestamptz END DESC NULLS LAST,
                CASE WHEN coalesce(p->>'sortDirection','asc')='asc' AND coalesce(p->>'sortBy','occurredAt')='kind' THEN item->>'kind' END ASC NULLS LAST,
                CASE WHEN coalesce(p->>'sortDirection','asc')='desc' AND coalesce(p->>'sortBy','occurredAt')='kind' THEN item->>'kind' END DESC NULLS LAST,
                CASE WHEN coalesce(p->>'sortDirection','asc')='asc' AND coalesce(p->>'sortBy','occurredAt')='quantity' THEN NULLIF(item->>'quantityKg','')::numeric END ASC NULLS LAST,
                CASE WHEN coalesce(p->>'sortDirection','asc')='desc' AND coalesce(p->>'sortBy','occurredAt')='quantity' THEN NULLIF(item->>'quantityKg','')::numeric END DESC NULLS LAST,
                item->>'id',item->>'postingId') ordinal
            FROM candidates
        ), selected AS (
            SELECT item,ordinal FROM ordered WHERE ordinal>(page_number::bigint-1)*page_size AND ordinal<=page_number::bigint*page_size
        )
        SELECT jsonb_build_object(
            'items',coalesce((SELECT jsonb_agg(item ORDER BY ordinal) FROM selected),'[]'::jsonb),
            'page',page_number,'pageSize',page_size,
            'totalCount',(SELECT count(*) FROM candidates),
            'hasMore',(SELECT count(*) FROM candidates)>page_number::bigint*page_size)
        INTO result;
        RETURN result;
    END IF;
    IF p ? 'availableOnly' AND (action<>'lots' OR p->>'availableOnly' NOT IN ('true','false')) THEN
        RAISE EXCEPTION 'Invalid available-only filter' USING ERRCODE='22023';
    END IF;
    IF action='lots' AND p->>'availableOnly'='true' THEN
        RETURN ledger.available_lots_page(a,p);
    END IF;
    result:=ledger.read_before_mixed_bulk(a,action,p);
    IF action IN ('setup','lookups') THEN
        result:=jsonb_set(result,'{locations}',coalesce((
            SELECT jsonb_agg(loc || jsonb_build_object(
                'mixedBulk',lc.mixed_bulk,
                'currentPoolLotId',CASE WHEN pool.lot_id IS NULL OR ledger.can_read_lot(a,pool.lot_id) THEN pool.lot_id END,
                'poolRevision',CASE WHEN ledger.assigned(a,lc.facility_id) OR ledger.own(a,lc.owner_id) THEN pool.revision END) ORDER BY loc->>'name',loc->>'id')
            FROM jsonb_array_elements(coalesce(result->'locations','[]'::jsonb)) loc
            JOIN ledger.locations lc ON lc.id=(loc->>'id')::uuid
            LEFT JOIN ledger.mixed_bulk_pools pool ON pool.location_id=lc.id
        ),'[]'::jsonb),true);
        IF action='setup' THEN
            result:=result||jsonb_build_object('mixedBulkPolicies',coalesce((
                SELECT jsonb_agg(jsonb_build_object(
                    'id',mp.id,'locationId',mp.location_id,'ownerId',mp.owner_id,
                    'commodityId',mp.commodity_id,'stage',mp.stage,'productIdentity',mp.product_identity,
                    'effectiveFrom',mp.effective_from,'evidenceId',mp.evidence_id,'approvedBy',mp.approved_by))
                FROM ledger.mixed_bulk_policies mp
                JOIN ledger.locations lc ON lc.id=mp.location_id
                WHERE ledger.role(a)='Administrator' OR ledger.assigned(a,lc.facility_id)
            ),'[]'::jsonb));
        END IF;
    END IF;
    RETURN result;
END $$;

REVOKE ALL ON FUNCTION ledger.read(uuid,text,jsonb) FROM PUBLIC;
REVOKE ALL ON FUNCTION ledger.available_lots_page(uuid,jsonb) FROM PUBLIC;
