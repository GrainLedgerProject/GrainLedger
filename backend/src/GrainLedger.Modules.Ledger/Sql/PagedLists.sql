-- Only list projections are paged. Accounting routines retain complete stock sets.
CREATE FUNCTION ledger.operation_for_actor(a uuid,op uuid) RETURNS jsonb
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger,identity AS $$
    SELECT ledger.operation_json(x.id)||jsonb_build_object(
        'ownerName',o."Name",'createdBy',x.created_by,'boundCorrectionId',(SELECT c.id FROM ledger.corrections c WHERE c.replacement_operation_id=x.id AND c.status IN ('REQUESTED','RECONCILED')),
        'canEdit',coalesce(x.created_by=a AND x.status='DRAFT' AND ledger.role(a)='MillOperator' AND ledger.assigned(a,x.facility_id),false),
        'canDelete',coalesce(x.created_by=a AND x.status='DRAFT' AND ledger.role(a)='MillOperator' AND ledger.assigned(a,x.facility_id)
            AND NOT ledger.record_in_use('operations',x.id),false))
    FROM ledger.operations x JOIN identity.organizations o ON o."Id"=x.owner_id
    WHERE x.id=op AND NOT x.is_deleted AND ledger.can_read_operation(a,x.id)
$$;

CREATE OR REPLACE FUNCTION ledger.read(a uuid,action text,p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE
    result jsonb;
    requested_lot uuid:=NULLIF(p->>'lotId','')::uuid;
    requested_facility uuid:=NULLIF(p->>'facilityId','')::uuid;
    requested_owner uuid:=NULLIF(p->>'ownerId','')::uuid;
    requested_location uuid:=NULLIF(p->>'locationId','')::uuid;
    page_number integer:=coalesce((p->>'page')::integer,1);
    page_size integer:=coalesce((p->>'pageSize')::integer,25);
    sort_key text:=coalesce(p->>'sortBy',CASE action WHEN 'lots' THEN 'code' WHEN 'operations' THEN 'occurredAt' WHEN 'history' THEN 'occurredAt' WHEN 'corrections' THEN 'requestedAt' WHEN 'receiving-tasks' THEN 'receivedAt' ELSE 'createdAt' END);
    direction text:=coalesce(p->>'sortDirection',CASE WHEN action IN ('lots','history') THEN 'asc' ELSE 'desc' END);
    requested_ids uuid[];
BEGIN
    IF ledger.role(a) IS NULL THEN RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501'; END IF;
    IF action IN ('setup','lookups') THEN
        IF action='setup' AND ledger.role(a) NOT IN ('Administrator','MillManager','MillOperator') THEN RAISE EXCEPTION 'Setup access denied' USING ERRCODE='42501'; END IF;
        SELECT jsonb_build_object(
            'facilities',coalesce((SELECT jsonb_agg(jsonb_build_object('id',x.id,'name',x.name) || CASE WHEN action='setup' THEN jsonb_build_object('createdBy',x.created_by,'version',x.version,
                'canEdit',coalesce(x.created_by=a AND ledger.role(a)='Administrator',false),
                'canDelete',coalesce(x.created_by=a AND ledger.role(a)='Administrator',false)) ELSE '{}'::jsonb END ORDER BY x.name,x.id)
                FROM ledger.facilities x WHERE NOT x.is_deleted AND (ledger.role(a) IN ('Administrator','CustomerManager','ExportStaff','PurchasingStaff') OR ledger.assigned(a,x.id))),'[]'::jsonb),
            'locations',coalesce((SELECT jsonb_agg(jsonb_build_object('id',x.id,'facilityId',x.facility_id,'name',x.name,'kind',x.kind)
                || CASE WHEN action='setup' THEN jsonb_build_object('createdBy',x.created_by,'version',x.version,
                'canEdit',coalesce(x.created_by=a AND ledger.role(a)='Administrator',false),
                'canDelete',coalesce(x.created_by=a AND ledger.role(a)='Administrator',false),
                'ownerId',x.owner_id,'cleared',x.cleared) ELSE '{}'::jsonb END ORDER BY x.name,x.id)
                FROM ledger.locations x WHERE NOT x.is_deleted AND EXISTS(SELECT 1 FROM ledger.facilities f WHERE f.id=x.facility_id AND NOT f.is_deleted) AND (ledger.role(a) IN ('Administrator','CustomerManager','ExportStaff','PurchasingStaff') OR ledger.assigned(a,x.facility_id))),'[]'::jsonb),
            'commodities',coalesce((SELECT jsonb_agg(jsonb_build_object('id',lc.id,'name',coalesce(cc.name,lc.name),'code',cc.code,'catalogId',cc.id) ORDER BY coalesce(cc.name,lc.name),lc.id) FROM ledger.commodities lc LEFT JOIN ledger.catalog_commodities m ON m.ledger_commodity_id=lc.id LEFT JOIN catalog.commodities cc ON cc.id=m.catalog_commodity_id WHERE cc.id IS NULL OR NOT cc.is_archived),'[]'::jsonb),
            'owners',coalesce((SELECT jsonb_agg(jsonb_build_object('id',o."Id",'name',o."Name") ORDER BY o."Name",o."Id") FROM identity.organizations o WHERE o."Kind"='Customer' AND (ledger.own(a,o."Id") OR EXISTS(SELECT 1 FROM ledger.lots l WHERE l.owner_id=o."Id" AND ledger.can_read_lot(a,l.id)) OR EXISTS(SELECT 1 FROM ledger.operations op WHERE op.owner_id=o."Id" AND NOT op.is_deleted AND ledger.can_read_operation(a,op.id)))),'[]'::jsonb),
            'equipment',coalesce((SELECT jsonb_agg(jsonb_build_object('id',x.id,'facilityId',x.facility_id,'name',x.name) || CASE WHEN action='setup' THEN jsonb_build_object('createdBy',x.created_by,'version',x.version,
                'canEdit',coalesce(x.created_by=a AND ledger.role(a)='Administrator',false),
                'canDelete',coalesce(x.created_by=a AND ledger.role(a)='Administrator',false)) ELSE '{}'::jsonb END ORDER BY x.name,x.id) FROM ledger.equipment x WHERE NOT x.is_deleted AND EXISTS(SELECT 1 FROM ledger.facilities f WHERE f.id=x.facility_id AND NOT f.is_deleted) AND (ledger.role(a)='Administrator' OR ledger.role(a) IN ('MillManager','MillOperator') AND ledger.assigned(a,x.facility_id))),'[]'::jsonb)) INTO result;
        IF action='setup' THEN result:=result||jsonb_build_object(
            'assignments',coalesce((SELECT jsonb_agg(jsonb_build_object('userId',user_id,'facilityId',facility_id,'createdBy',created_by,'version',version,'canEdit',coalesce(created_by=a AND ledger.role(a)='Administrator',false),'canDelete',coalesce(created_by=a AND ledger.role(a)='Administrator',false)) ORDER BY facility_id,user_id) FROM ledger.assignments aa WHERE NOT aa.is_deleted AND EXISTS(SELECT 1 FROM ledger.facilities f WHERE f.id=aa.facility_id AND NOT f.is_deleted) AND (ledger.role(a)='Administrator' OR user_id=a)),'[]'::jsonb),
            'policies',coalesce((SELECT jsonb_agg(jsonb_build_object('id',id,'facilityId',facility_id,'kind',kind,'tolerancePercent',tolerance_percent::text,'effectiveFrom',effective_from,'evidenceId',evidence_id)) FROM ledger.policies WHERE ledger.role(a)='Administrator' OR ledger.assigned(a,facility_id)),'[]'::jsonb),
            'compatibilities',coalesce((SELECT jsonb_agg(jsonb_build_object('id',id,'facilityId',facility_id,'inputProducts',input_products,'outputCommodityId',output_commodity_id,'effectiveFrom',effective_from,'evidenceId',evidence_id)) FROM ledger.compatibilities WHERE ledger.role(a)='Administrator' OR ledger.assigned(a,facility_id)),'[]'::jsonb)); END IF;
        RETURN result;
    END IF;
    IF action='lot' THEN
        IF NOT ledger.can_read_lot(a,requested_lot) THEN RAISE EXCEPTION 'Lot access denied' USING ERRCODE='42501'; END IF;
        RETURN ledger.lot_json(a,requested_lot,true)||jsonb_build_object('history',ledger.read(a,'history',p));
    END IF;
    IF action='operation' THEN
        IF NOT EXISTS(SELECT 1 FROM ledger.operations x WHERE x.id=(p->>'id')::uuid AND NOT x.is_deleted AND ledger.can_read_operation(a,x.id)) THEN RAISE EXCEPTION 'Operation access denied' USING ERRCODE='42501'; END IF;
        RETURN ledger.operation_for_actor(a,(p->>'id')::uuid);
    END IF;
    IF page_number<1 OR page_size NOT BETWEEN 1 AND 100 OR (page_number::bigint-1)*page_size>2147483647 OR direction NOT IN ('asc','desc') THEN RAISE EXCEPTION 'Invalid pagination or sort direction' USING ERRCODE='22023'; END IF;
    IF NOT sort_key=ANY(CASE action WHEN 'lots' THEN ARRAY['code','stage','owner','quantity'] WHEN 'operations' THEN ARRAY['occurredAt','kind','owner','status'] WHEN 'holds' THEN ARRAY['createdAt','status','owner'] WHEN 'corrections' THEN ARRAY['requestedAt','status','owner'] WHEN 'notifications' THEN ARRAY['createdAt','readAt'] WHEN 'history' THEN ARRAY['occurredAt','kind','quantity'] WHEN 'receiving-tasks' THEN ARRAY['receivedAt','status','owner'] ELSE ARRAY[]::text[] END) THEN RAISE EXCEPTION 'Unsupported ledger sort' USING ERRCODE='22023'; END IF;
    IF p ? 'ids' THEN
        IF action NOT IN ('lots','operations') THEN RAISE EXCEPTION 'IDs filter not supported' USING ERRCODE='22023'; END IF;
        requested_ids:=string_to_array(p->>'ids',',')::uuid[];
        IF cardinality(requested_ids) NOT BETWEEN 1 AND 100 THEN RAISE EXCEPTION 'IDs requires 1–100 UUIDs' USING ERRCODE='22023'; END IF;
    END IF;
    IF action='receiving-tasks' THEN RETURN ledger.receiving_tasks_page(a,p); END IF;
    IF action='history' AND requested_lot IS NOT NULL AND NOT ledger.can_read_lot(a,requested_lot) THEN RAISE EXCEPTION 'History lot access denied' USING ERRCODE='42501'; END IF;
    IF p ? 'stage' AND (action<>'lots' OR p->>'stage' NOT IN ('RAW_PADDY','DRIED_PADDY','HEAD_RICE','BROKEN_RICE','BRAN','HUSK','BLENDED_RICE','BAGGED_RICE')) THEN RAISE EXCEPTION 'Invalid stock stage' USING ERRCODE='22023'; END IF;
    IF p ? 'status' AND NOT (action='operations' AND p->>'status' IN ('DRAFT','COMMITTED','CORRECTED') OR action='holds' AND p->>'status' IN ('ACTIVE','ACKNOWLEDGED','ISOLATED','RESPONDED') OR action='corrections' AND p->>'status' IN ('REQUESTED','RECONCILED','CANCELLED','COMMITTED') OR action='notifications' AND p->>'status' IN ('READ','UNREAD')) THEN RAISE EXCEPTION 'Invalid status filter' USING ERRCODE='22023'; END IF;
    IF p ? 'kind' AND (action NOT IN ('operations','history','corrections') OR action IN ('operations','corrections') AND p->>'kind' NOT IN ('DRYING','MILLING','MERGE','BLENDING','BAGGING')) THEN RAISE EXCEPTION 'Invalid kind filter' USING ERRCODE='22023'; END IF;
    IF p ? 'queueOnly' AND (action<>'operations' OR p->>'queueOnly' NOT IN ('true','false')) THEN RAISE EXCEPTION 'Invalid queue filter' USING ERRCODE='22023'; END IF;
    WITH history_event_ids AS MATERIALIZED (
        SELECT id FROM ledger.events WHERE action='history' AND requested_lot IS NULL
        UNION
        SELECT id FROM ledger.events WHERE action='history' AND lot_id=requested_lot
        UNION
        SELECT id FROM ledger.events WHERE action='history' AND operation_id=(SELECT operation_id FROM ledger.lots WHERE id=requested_lot)
        UNION
        SELECT event_id FROM ledger.postings WHERE action='history' AND lot_id=requested_lot
    ), candidates AS MATERIALIZED (
        SELECT x.id,NULL::uuid posting_id,x.owner_id,o."Name" owner_name,x.code code,x.stage kind,NULL::text status,x.created_at at,NULL::timestamptz read_at,
            CASE WHEN sort_key='quantity' THEN coalesce((SELECT sum(pos.quantity_kg) FROM ledger.positions pos JOIN ledger.locations lc ON lc.id=pos.location_id WHERE pos.lot_id=x.id AND (ledger.own(a,x.owner_id) OR ledger.assigned(a,lc.facility_id)) AND (requested_facility IS NULL OR lc.facility_id=requested_facility) AND (requested_location IS NULL OR lc.id=requested_location)),0) END quantity
        FROM ledger.lots x JOIN identity.organizations o ON o."Id"=x.owner_id
        WHERE action='lots' AND ledger.can_read_lot(a,x.id) AND (requested_ids IS NULL OR x.id=ANY(requested_ids)) AND (requested_lot IS NULL OR x.id=requested_lot) AND (p->>'stage' IS NULL OR x.stage=p->>'stage')
            AND (requested_facility IS NULL AND requested_location IS NULL OR EXISTS(SELECT 1 FROM ledger.positions pos JOIN ledger.locations lc ON lc.id=pos.location_id WHERE pos.lot_id=x.id AND (ledger.own(a,x.owner_id) OR ledger.assigned(a,lc.facility_id)) AND (requested_facility IS NULL OR lc.facility_id=requested_facility) AND (requested_location IS NULL OR lc.id=requested_location)))
        UNION ALL
        SELECT x.id,NULL,x.owner_id,o."Name",x.id::text,x.kind,x.status,x.occurred_at,NULL,NULL
        FROM ledger.operations x JOIN identity.organizations o ON o."Id"=x.owner_id
        WHERE action='operations' AND NOT x.is_deleted AND ledger.can_read_operation(a,x.id) AND (requested_ids IS NULL OR x.id=ANY(requested_ids)) AND (requested_facility IS NULL OR x.facility_id=requested_facility)
            AND (requested_lot IS NULL OR EXISTS(SELECT 1 FROM jsonb_array_elements(x.content->'inputs') i WHERE i->>'lotId'=requested_lot::text) OR EXISTS(SELECT 1 FROM ledger.lots l WHERE l.operation_id=x.id AND l.id=requested_lot))
            AND (requested_location IS NULL OR EXISTS(SELECT 1 FROM jsonb_array_elements(x.content->'inputs') i WHERE i->>'locationId'=requested_location::text) OR EXISTS(SELECT 1 FROM jsonb_array_elements(x.content->'outputs') o WHERE o->>'locationId'=requested_location::text))
            AND (coalesce(p->>'queueOnly','false')='false' OR x.status='DRAFT' AND x.confirmed_version IS DISTINCT FROM x.version
                AND (SELECT sum(ledger.kg(i->>'quantityKg')) FROM jsonb_array_elements(x.content->'inputs') i)
                    -(SELECT sum(ledger.kg(o->>'quantityKg')) FROM jsonb_array_elements(x.content->'outputs') o)-ledger.kg(x.content->>'declaredLossKg')<>0)
        UNION ALL
        SELECT h.id,NULL,l.owner_id,o."Name",l.code,NULL,h.status,h.created_at,NULL,NULL
        FROM ledger.holds h JOIN ledger.lots l ON l.id=h.lot_id JOIN identity.organizations o ON o."Id"=l.owner_id
        WHERE action='holds' AND ledger.can_read_lot(a,l.id) AND (requested_lot IS NULL OR l.id=requested_lot) AND (requested_facility IS NULL AND requested_location IS NULL OR EXISTS(SELECT 1 FROM ledger.positions pos JOIN ledger.locations lc ON lc.id=pos.location_id WHERE pos.lot_id=l.id AND (ledger.own(a,l.owner_id) OR ledger.assigned(a,lc.facility_id)) AND (requested_facility IS NULL OR lc.facility_id=requested_facility) AND (requested_location IS NULL OR lc.id=requested_location)))
        UNION ALL
        SELECT c.id,NULL,op.owner_id,o."Name",op.id::text,op.kind,c.status,(SELECT min(e.occurred_at) FROM ledger.events e WHERE e.operation_id=op.id AND e.kind='CORRECTION_REQUEST'),NULL,NULL
        FROM ledger.corrections c JOIN ledger.operations op ON op.id=c.operation_id JOIN identity.organizations o ON o."Id"=op.owner_id
        WHERE action='corrections' AND NOT op.is_deleted AND ledger.can_read_operation(a,op.id) AND (requested_facility IS NULL OR op.facility_id=requested_facility) AND (requested_lot IS NULL OR EXISTS(SELECT 1 FROM ledger.lots l WHERE l.id=requested_lot AND l.operation_id=op.id))
            AND (requested_location IS NULL OR EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'inputs') i WHERE i->>'locationId'=requested_location::text) OR EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'outputs') o WHERE o->>'locationId'=requested_location::text))
        UNION ALL
        SELECT n.id,NULL,l.owner_id,o."Name",l.code,NULL,CASE WHEN n.read_at IS NULL THEN 'UNREAD' ELSE 'READ' END,n.created_at,n.read_at,NULL
        FROM ledger.notifications n JOIN ledger.lots l ON l.id=n.lot_id JOIN identity.organizations o ON o."Id"=l.owner_id
        WHERE action='notifications' AND n.user_id=a AND ledger.can_read_lot(a,l.id) AND (requested_lot IS NULL OR l.id=requested_lot) AND (requested_facility IS NULL AND requested_location IS NULL OR EXISTS(SELECT 1 FROM ledger.positions pos JOIN ledger.locations lc ON lc.id=pos.location_id WHERE pos.lot_id=l.id AND (ledger.own(a,l.owner_id) OR ledger.assigned(a,lc.facility_id)) AND (requested_facility IS NULL OR lc.facility_id=requested_facility) AND (requested_location IS NULL OR lc.id=requested_location)))
        UNION ALL
        SELECT ee.id,pp.id,coalesce(l.owner_id,op.owner_id),o."Name",coalesce(l.code,op.id::text),ee.kind,NULL,ee.occurred_at,NULL,pp.quantity_kg
        FROM history_event_ids hi JOIN ledger.events ee ON ee.id=hi.id LEFT JOIN ledger.postings pp ON pp.event_id=ee.id LEFT JOIN ledger.locations lc ON lc.id=pp.location_id LEFT JOIN ledger.lots l ON l.id=coalesce(pp.lot_id,ee.lot_id) LEFT JOIN ledger.operations op ON op.id=ee.operation_id LEFT JOIN identity.organizations o ON o."Id"=coalesce(l.owner_id,op.owner_id)
        WHERE action='history' AND (requested_lot IS NULL OR coalesce(pp.lot_id,ee.lot_id)=requested_lot OR ee.operation_id=(SELECT operation_id FROM ledger.lots WHERE id=requested_lot))
            AND (requested_facility IS NULL OR lc.facility_id=requested_facility OR op.facility_id=requested_facility) AND (requested_location IS NULL OR pp.location_id=requested_location)
            AND CASE WHEN pp.id IS NOT NULL THEN ledger.can_read_lot(a,pp.lot_id) AND (ledger.own(a,l.owner_id) OR ledger.assigned(a,lc.facility_id)) WHEN ee.lot_id IS NOT NULL THEN ledger.can_read_lot(a,ee.lot_id) ELSE ledger.can_read_operation(a,ee.operation_id) END
    ), filtered AS MATERIALIZED (
        SELECT * FROM candidates x WHERE (requested_owner IS NULL OR x.owner_id=requested_owner) AND (p->>'status' IS NULL OR x.status=p->>'status') AND (p->>'kind' IS NULL OR x.kind=p->>'kind')
            AND (p->>'search' IS NULL
                OR concat_ws(' ',x.code,x.owner_name,x.kind,x.status) ILIKE '%'||(p->>'search')||'%'
                OR action IN ('operations','corrections') AND EXISTS (
                    SELECT 1 FROM ledger.operations op
                    CROSS JOIN LATERAL jsonb_array_elements(op.content->'outputs') output
                    WHERE op.id=CASE WHEN action='operations' THEN x.id ELSE (SELECT c.operation_id FROM ledger.corrections c WHERE c.id=x.id) END
                        AND output->>'code' ILIKE '%'||(p->>'search')||'%'
                )
                OR action='holds' AND EXISTS(SELECT 1 FROM ledger.holds h WHERE h.id=x.id AND h.reason ILIKE '%'||(p->>'search')||'%')
                OR action='corrections' AND EXISTS(SELECT 1 FROM ledger.corrections c WHERE c.id=x.id AND c.reason ILIKE '%'||(p->>'search')||'%')
                OR action='notifications' AND EXISTS(SELECT 1 FROM ledger.notifications n WHERE n.id=x.id AND n.message ILIKE '%'||(p->>'search')||'%'))
    ), ordered AS (
        SELECT x.*,row_number() OVER (ORDER BY
            CASE WHEN direction='asc' THEN CASE sort_key WHEN 'code' THEN x.code WHEN 'stage' THEN x.kind WHEN 'kind' THEN x.kind WHEN 'status' THEN x.status WHEN 'owner' THEN x.owner_name END END ASC NULLS LAST,
            CASE WHEN direction='desc' THEN CASE sort_key WHEN 'code' THEN x.code WHEN 'stage' THEN x.kind WHEN 'kind' THEN x.kind WHEN 'status' THEN x.status WHEN 'owner' THEN x.owner_name END END DESC NULLS LAST,
            CASE WHEN direction='asc' AND sort_key IN ('createdAt','requestedAt','occurredAt') THEN x.at WHEN direction='asc' AND sort_key='readAt' THEN x.read_at END ASC NULLS LAST,
            CASE WHEN direction='desc' AND sort_key IN ('createdAt','requestedAt','occurredAt') THEN x.at WHEN direction='desc' AND sort_key='readAt' THEN x.read_at END DESC NULLS LAST,
            CASE WHEN direction='asc' AND sort_key='quantity' THEN x.quantity END ASC NULLS LAST,
            CASE WHEN direction='desc' AND sort_key='quantity' THEN x.quantity END DESC NULLS LAST,x.id,x.posting_id NULLS FIRST) ordinal FROM filtered x
    ), selected AS MATERIALIZED (
        SELECT * FROM ordered ORDER BY ordinal LIMIT page_size OFFSET (page_number::bigint-1)*page_size
    )
    SELECT jsonb_build_object('items',coalesce((SELECT jsonb_agg(
        CASE action
            WHEN 'lots' THEN ledger.lot_json(a,x.id,false)||jsonb_build_object('ownerName',x.owner_name)
            WHEN 'operations' THEN ledger.operation_for_actor(a,x.id)
            WHEN 'holds' THEN (SELECT jsonb_build_object('id',h.id,'lotId',h.lot_id,'lotCode',x.code,'reason',h.reason,'status',h.status,'ownerId',x.owner_id,'ownerName',x.owner_name,'evidenceId',CASE WHEN ledger.role(a)<>'MillOperator' THEN h.evidence_id END,'response',h.response,'createdAt',h.created_at) FROM ledger.holds h WHERE h.id=x.id)
            WHEN 'corrections' THEN (SELECT jsonb_build_object('id',c.id,'operationId',c.operation_id,'kind',x.kind,'ownerId',x.owner_id,'ownerName',x.owner_name,'requestedAt',x.at,'reason',c.reason,'status',c.status,'evidenceId',CASE WHEN ledger.role(a)<>'MillOperator' THEN c.evidence_id END,'reconciliation',CASE WHEN ledger.role(a)<>'MillOperator' THEN c.reconciliation END,'replacementOperationId',c.replacement_operation_id) FROM ledger.corrections c WHERE c.id=x.id)
            WHEN 'notifications' THEN (SELECT jsonb_build_object('id',n.id,'holdId',n.hold_id,'lotId',n.lot_id,'ownerId',x.owner_id,'ownerName',x.owner_name,'message',n.message,'createdAt',n.created_at,'readAt',n.read_at) FROM ledger.notifications n WHERE n.id=x.id)
            WHEN 'history' THEN (SELECT jsonb_build_object('id',ee.id,'postingId',pp.id,'kind',ee.kind,'occurredAt',ee.occurred_at,'actorId',ee.actor_id,'operationId',ee.operation_id,'lotId',coalesce(pp.lot_id,ee.lot_id),'locationId',pp.location_id,'ownerId',x.owner_id,'ownerName',x.owner_name,'quantityKg',pp.quantity_kg::text,'reversesPostingId',pp.reverses_posting_id,'details',CASE WHEN ledger.role(a)='MillOperator' THEN jsonb_build_object('declaredLossKg',ee.details->>'declaredLossKg','varianceKg',ee.details->>'varianceKg') ELSE ee.details END) FROM ledger.events ee LEFT JOIN ledger.postings pp ON pp.id=x.posting_id WHERE ee.id=x.id)
        END ORDER BY x.ordinal) FROM selected x),'[]'::jsonb),'page',page_number,'pageSize',page_size,'totalCount',(SELECT count(*) FROM filtered),'hasMore',(SELECT count(*) FROM filtered)>page_number::bigint*page_size) INTO result;
    RETURN result;
END $$;
REVOKE ALL ON FUNCTION ledger.operation_for_actor(uuid,uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION ledger.read(uuid,text,jsonb) FROM PUBLIC;
