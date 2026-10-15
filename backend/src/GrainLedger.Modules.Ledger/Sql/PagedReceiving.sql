CREATE FUNCTION ledger.receiving_tasks_page(a uuid,j jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE
    f uuid:=(j->>'facilityId')::uuid;
    owner_filter uuid:=(j->>'ownerId')::uuid;
    location_filter uuid:=(j->>'locationId')::uuid;
    p integer:=coalesce((j->>'page')::integer,1);
    n integer:=coalesce((j->>'pageSize')::integer,25);
    sort_key text:=coalesce(j->>'sortBy','receivedAt');
    direction text:=coalesce(j->>'sortDirection','desc');
    result jsonb;
BEGIN
    IF f IS NULL THEN RAISE EXCEPTION 'Facility required' USING ERRCODE='22023'; END IF;
    PERFORM ledger.require_staff(a,f,ARRAY['MillOperator','MillManager']);
    IF p<1 OR n NOT BETWEEN 1 AND 100 OR (p::bigint-1)*n>2147483647 OR sort_key NOT IN ('receivedAt','status','owner') OR direction NOT IN ('asc','desc') THEN RAISE EXCEPTION 'Invalid receiving pagination or sort' USING ERRCODE='22023'; END IF;
    IF j ? 'status' AND j->>'status' NOT IN ('PENDING_ARRIVAL','AWAITING_MANAGER_REVIEW','REMEASUREMENT_REQUESTED','RECEIVED_MATCHED','RECEIVED_SHORTAGE_APPROVED','BLOCKED_UNRESOLVED') THEN RAISE EXCEPTION 'Invalid receiving status' USING ERRCODE='22023'; END IF;
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
-- Retain existing granted routine for restricted direct callers; same bounded contract.
CREATE OR REPLACE FUNCTION ledger.receiving_tasks(a uuid,f uuid,p integer,n integer) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
BEGIN
    IF p IS NULL OR n IS NULL THEN RAISE EXCEPTION 'Invalid pagination' USING ERRCODE='22023'; END IF;
    RETURN ledger.receiving_tasks_page(a,jsonb_build_object('facilityId',f,'page',p,'pageSize',n));
END $$;
REVOKE ALL ON FUNCTION ledger.receiving_tasks_page(uuid,jsonb) FROM PUBLIC;
REVOKE ALL ON FUNCTION ledger.receiving_tasks(uuid,uuid,integer,integer) FROM PUBLIC;
