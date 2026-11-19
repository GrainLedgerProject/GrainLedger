-- Container preparation only. No path here grants export authority or posts inventory.
CREATE TABLE grainledger.ledger_containers (
 id uuid PRIMARY KEY,
 owner_id uuid NOT NULL REFERENCES grainledger.identity_organizations("Id"),
 facility_id uuid NOT NULL REFERENCES grainledger.ledger_facilities,
 code text NOT NULL CHECK(length(code) BETWEEN 1 AND 80),
 order_id uuid CHECK(order_id IS NULL OR order_id<>'00000000-0000-0000-0000-000000000000'),
 target_kg numeric(20,6) NOT NULL CHECK(target_kg>0),
 container_number text CHECK(length(container_number) BETWEEN 1 AND 80),
 seal_number text CHECK(length(seal_number) BETWEEN 1 AND 80),
 status text NOT NULL DEFAULT 'OPEN' CHECK(status='OPEN'),
 version uuid NOT NULL DEFAULT gen_random_uuid(),
 created_by uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
 created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(owner_id,code)
);
CREATE INDEX ledger_containers_owner_created ON grainledger.ledger_containers(owner_id,created_at DESC,id);
CREATE TABLE grainledger.ledger_container_loading_drafts (
 id uuid PRIMARY KEY,
 container_id uuid NOT NULL REFERENCES grainledger.ledger_containers,
 loaded_at timestamptz NOT NULL,
 lines jsonb NOT NULL CHECK(jsonb_typeof(lines)='array' AND jsonb_array_length(lines) BETWEEN 1 AND 100),
 recorded_by uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
 recorded_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ledger_container_drafts_parent ON grainledger.ledger_container_loading_drafts(container_id,id);
CREATE TABLE grainledger.ledger_container_events (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 container_id uuid NOT NULL REFERENCES grainledger.ledger_containers,
 actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
 kind text NOT NULL,
 details jsonb NOT NULL,
 recorded_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ledger_container_events_parent ON grainledger.ledger_container_events(container_id,recorded_at DESC,id);
CREATE TABLE grainledger.ledger_container_requests (
 actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
 request_id uuid NOT NULL,
 action text NOT NULL,
 payload jsonb NOT NULL,
 result jsonb NOT NULL,
 PRIMARY KEY(actor_id,request_id)
);
CREATE TRIGGER ledger_immutable BEFORE UPDATE OR DELETE ON grainledger.ledger_container_events FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();
CREATE TRIGGER ledger_immutable BEFORE UPDATE OR DELETE ON grainledger.ledger_container_requests FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();
REVOKE ALL ON TABLE grainledger.ledger_containers,grainledger.ledger_container_loading_drafts,grainledger.ledger_container_events,grainledger.ledger_container_requests FROM PUBLIC;

-- Forward preparation upgrade only; never posts stock, consumes reservations or closes containers.
CREATE OR REPLACE FUNCTION grainledger.ledger_container_json(target uuid) RETURNS jsonb
LANGUAGE sql STABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
 SELECT jsonb_build_object('preparationVersion',2,'id',c.id,'ownerId',c.owner_id,'facilityId',c.facility_id,
  'code',c.code,'orderId',c.order_id,'targetKg',c.target_kg::text,'containerNumber',c.container_number,
  'sealNumber',c.seal_number,'status',c.status,'version',c.version,'createdAt',c.created_at,
  'committedLoadedKg','0.000000','remainingKg',c.target_kg::text,
  'drafts',coalesce((SELECT jsonb_agg(jsonb_build_object('id',d.id,'loadedAt',d.loaded_at,'lines',d.lines,
   'totalKg',(SELECT sum(grainledger.ledger_kg(l->>'quantityKg'))::text FROM jsonb_array_elements(d.lines) l),
   'recordedAt',d.recorded_at) ORDER BY d.recorded_at,d.id) FROM grainledger.ledger_container_loading_drafts d WHERE d.container_id=c.id),'[]'::jsonb))
 FROM grainledger.ledger_containers c WHERE c.id=target
$$;

CREATE OR REPLACE FUNCTION grainledger.ledger_container_action(a uuid, action text, p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE
 r text:=grainledger.ledger_role(a); actor_owner uuid; c grainledger.ledger_containers; line jsonb; pos grainledger.ledger_positions;
 loc grainledger.ledger_locations; lot grainledger.ledger_lots; target uuid; draft uuid; req uuid; previous grainledger.ledger_container_requests;
 result jsonb; findings jsonb:='[]'; normalized jsonb:='[]'; amount numeric; total numeric:=0;
 n integer; size integer; page integer; needle text; mutation boolean; allowed text[];
 pool grainledger.ledger_mixed_bulk_pools; observed_revision bigint;
BEGIN
 IF r IS NULL OR r NOT IN ('ExportStaff','CustomerManager','MillOperator','MillManager') THEN
  RAISE EXCEPTION 'Container access denied' USING ERRCODE='42501';
 END IF;
 SELECT "OrganizationId" INTO actor_owner FROM grainledger.identity_users WHERE "Id"=a AND "IsActive";
 IF p IS NULL OR jsonb_typeof(p)<>'object' THEN RAISE EXCEPTION 'Object required' USING ERRCODE='22023'; END IF;
 mutation:=action IN ('create','metadata','loading-draft','commit-loading','close');
 IF mutation AND r<>'ExportStaff' THEN RAISE EXCEPTION 'Customer Export Staff authority required' USING ERRCODE='42501'; END IF;
 allowed:=CASE action
  WHEN 'list' THEN ARRAY['page','pageSize','search']
  WHEN 'history' THEN ARRAY['id','page','pageSize']
  WHEN 'detail' THEN ARRAY['id']
  WHEN 'create' THEN ARRAY['id','requestId','code','facilityId','targetKg','orderId','containerNumber','sealNumber']
  WHEN 'metadata' THEN ARRAY['id','requestId','version','orderId','containerNumber','sealNumber']
  WHEN 'loading-draft' THEN ARRAY['id','requestId','version','draftId','loadedAt','lines']
  WHEN 'loading-check' THEN ARRAY['id','draftId']
  WHEN 'close-check' THEN ARRAY['id']
  WHEN 'commit-loading' THEN ARRAY['id','draftId','version','requestId']
  WHEN 'close' THEN ARRAY['id','version','requestId'] END;
 IF allowed IS NULL OR EXISTS(SELECT 1 FROM jsonb_object_keys(p) k WHERE NOT k=ANY(allowed)) THEN
  RAISE EXCEPTION 'Unsupported container action or fields' USING ERRCODE='22023';
 END IF;
 IF action IN ('list','history') THEN
  IF coalesce(p->>'page','1') !~ '^[1-9][0-9]{0,5}$' OR coalesce(p->>'pageSize','25') !~ '^[1-9][0-9]{0,2}$' THEN
   RAISE EXCEPTION 'Invalid pagination' USING ERRCODE='22023'; END IF;
  page:=coalesce((p->>'page')::int,1); size:=coalesce((p->>'pageSize')::int,25);
  IF size>100 THEN RAISE EXCEPTION 'Page size exceeds 100' USING ERRCODE='22023'; END IF;
 END IF;
 IF action='list' THEN
  needle:=lower(btrim(coalesce(p->>'search','')));
  IF length(needle)>160 THEN RAISE EXCEPTION 'Search too long' USING ERRCODE='22023'; END IF;
  WITH scoped AS MATERIALIZED (
   SELECT x.* FROM grainledger.ledger_containers x WHERE
    (r IN ('ExportStaff','CustomerManager') AND grainledger.ledger_own(a,x.owner_id) OR r IN ('MillOperator','MillManager') AND grainledger.ledger_assigned(a,x.facility_id))
    AND (needle='' OR strpos(lower(x.code||' '||coalesce(x.container_number,'')),needle)>0)
  ), rows AS (SELECT * FROM scoped ORDER BY created_at DESC,id OFFSET (page-1)*size LIMIT size)
  SELECT jsonb_build_object('items',coalesce((SELECT jsonb_agg(jsonb_build_object('id',id,'code',code,'status',status,'targetKg',target_kg::text,'facilityId',facility_id,'containerNumber',container_number) ORDER BY created_at DESC,id) FROM rows),'[]'::jsonb),
   'page',page,'pageSize',size,'totalCount',(SELECT count(*) FROM scoped),'hasMore',(SELECT count(*) FROM scoped)>page*size) INTO result;
  RETURN result;
 END IF;
 IF mutation THEN
  PERFORM grainledger.ledger_lock_writes();
  -- Authority read before waiting on stock serialization must not authorize a later write.
  IF NOT EXISTS(SELECT 1 FROM grainledger.identity_users u WHERE u."Id"=a AND u."IsActive"
       AND u."Role"='ExportStaff' AND u."OrganizationId" IS NOT DISTINCT FROM actor_owner) THEN
   RAISE EXCEPTION 'Container authority changed while waiting; reload your session' USING ERRCODE='42501';
  END IF;
 END IF;
 target:=(p->>'id')::uuid;
 IF target IS NULL OR target='00000000-0000-0000-0000-000000000000' THEN RAISE EXCEPTION 'Container ID required' USING ERRCODE='22023'; END IF;
 IF action<>'create' THEN
  SELECT * INTO c FROM grainledger.ledger_containers WHERE id=target;
  IF NOT FOUND OR NOT (r IN ('ExportStaff','CustomerManager') AND grainledger.ledger_own(a,c.owner_id) OR r IN ('MillOperator','MillManager') AND grainledger.ledger_assigned(a,c.facility_id)) THEN
   RAISE EXCEPTION 'Container unavailable in current scope' USING ERRCODE='42501'; END IF;
 END IF;
 IF action='detail' THEN RETURN grainledger.ledger_container_json(target); END IF;
 IF action='history' THEN
  SELECT count(*) INTO n FROM grainledger.ledger_container_events WHERE container_id=target;
  SELECT jsonb_build_object('items',coalesce(jsonb_agg(to_jsonb(e) ORDER BY recorded_at DESC,id),'[]'::jsonb),'page',page,'pageSize',size,'totalCount',n,'hasMore',n>page*size)
   INTO result FROM (SELECT id,kind,actor_id AS "actorId",details,recorded_at FROM grainledger.ledger_container_events WHERE container_id=target ORDER BY recorded_at DESC,id OFFSET (page-1)*size LIMIT size) e;
  RETURN result;
 END IF;
 IF mutation THEN
  req:=(p->>'requestId')::uuid;
  IF req IS NULL OR req='00000000-0000-0000-0000-000000000000' THEN RAISE EXCEPTION 'Stable request ID required' USING ERRCODE='22023'; END IF;
  SELECT * INTO previous FROM grainledger.ledger_container_requests WHERE actor_id=a AND request_id=req;
  IF FOUND THEN
   IF previous.action<>action OR previous.payload<>p THEN RAISE EXCEPTION 'Request ID reused with different content' USING ERRCODE='23514'; END IF;
   IF previous.result->>'ownerId' IS DISTINCT FROM actor_owner::text THEN RAISE EXCEPTION 'Request outside current company' USING ERRCODE='42501'; END IF;
   RETURN previous.result;
  END IF;
 END IF;
 IF action='create' THEN
  IF actor_owner IS NULL OR NOT grainledger.ledger_own(a,actor_owner) THEN RAISE EXCEPTION 'Customer company required' USING ERRCODE='42501'; END IF;
  IF nullif(btrim(p->>'code'),'') IS NULL OR length(btrim(p->>'code'))>80 THEN RAISE EXCEPTION 'Container code required, maximum 80 characters' USING ERRCODE='22023'; END IF;
  IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_facilities WHERE id=(p->>'facilityId')::uuid AND NOT is_deleted) THEN
   RAISE EXCEPTION 'Active facility required' USING ERRCODE='22023'; END IF;
  IF jsonb_typeof(p->'targetKg') IS DISTINCT FROM 'string' THEN RAISE EXCEPTION 'Target kg must be a decimal string' USING ERRCODE='22023'; END IF;
  amount:=grainledger.ledger_kg(p->>'targetKg');
  IF amount<=0 THEN RAISE EXCEPTION 'Positive exact target kg required' USING ERRCODE='22023'; END IF;
  INSERT INTO grainledger.ledger_containers(id,owner_id,facility_id,code,target_kg,order_id,container_number,seal_number,created_by)
   VALUES(target,actor_owner,(p->>'facilityId')::uuid,btrim(p->>'code'),amount,(nullif(p->>'orderId',''))::uuid,nullif(btrim(p->>'containerNumber'),''),nullif(btrim(p->>'sealNumber'),''),a);
 ELSE
  IF action IN ('metadata','loading-draft','commit-loading','close') AND c.version IS DISTINCT FROM (p->>'version')::uuid THEN
   RAISE EXCEPTION 'Container changed; reload before saving' USING ERRCODE='23514'; END IF;
  IF action='metadata' THEN
   UPDATE grainledger.ledger_containers SET container_number=nullif(btrim(p->>'containerNumber'),''),seal_number=nullif(btrim(p->>'sealNumber'),''),order_id=(nullif(p->>'orderId',''))::uuid,version=gen_random_uuid() WHERE id=target;
  ELSIF action='loading-draft' THEN
   draft:=(p->>'draftId')::uuid;
   IF draft IS NULL OR draft='00000000-0000-0000-0000-000000000000' OR p->>'loadedAt' IS NULL THEN RAISE EXCEPTION 'Draft ID and recorded loading time required' USING ERRCODE='22023'; END IF;
   IF jsonb_typeof(p->'lines') IS DISTINCT FROM 'array' OR jsonb_array_length(p->'lines') NOT BETWEEN 1 AND 100 THEN RAISE EXCEPTION 'One to 100 loading lines required' USING ERRCODE='22023'; END IF;
   IF EXISTS(SELECT 1 FROM grainledger.ledger_container_loading_drafts WHERE id=draft AND container_id<>target) THEN RAISE EXCEPTION 'Draft identity unavailable' USING ERRCODE='42501'; END IF;
   IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_loading_drafts WHERE id=draft) AND (SELECT count(*) FROM grainledger.ledger_container_loading_drafts WHERE container_id=target)>=100 THEN RAISE EXCEPTION 'Draft limit reached' USING ERRCODE='23514'; END IF;
   FOR line IN SELECT * FROM jsonb_array_elements(p->'lines') LOOP
    IF jsonb_typeof(line)<>'object' OR EXISTS(SELECT 1 FROM jsonb_object_keys(line) k WHERE k NOT IN ('lotId','locationId','quantityKg','poolRevision')) THEN RAISE EXCEPTION 'Invalid loading line fields' USING ERRCODE='22023'; END IF;
    SELECT * INTO lot FROM grainledger.ledger_lots WHERE id=(line->>'lotId')::uuid AND owner_id=c.owner_id;
    SELECT * INTO loc FROM grainledger.ledger_locations WHERE id=(line->>'locationId')::uuid AND facility_id=c.facility_id;
    IF lot.id IS NULL OR loc.id IS NULL OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE lot_id=lot.id AND location_id=loc.id) THEN RAISE EXCEPTION 'Loading position outside container owner/facility' USING ERRCODE='42501'; END IF;
    PERFORM grainledger.ledger_require_active_location(loc.id);
    SELECT * INTO pos FROM grainledger.ledger_positions WHERE lot_id=lot.id AND location_id=loc.id;
    IF pos.quantity_kg<=0 THEN RAISE EXCEPTION 'Loading position has no current physical stock; reload and reselect' USING ERRCODE='23514'; END IF;
    IF grainledger.ledger_blocked(lot.id) THEN RAISE EXCEPTION 'Loading lot is restricted; reload before preparing' USING ERRCODE='23514'; END IF;
    observed_revision:=NULL;
    IF loc.mixed_bulk THEN
     IF jsonb_typeof(line->'poolRevision') IS DISTINCT FROM 'string'
        OR coalesce(line->>'poolRevision','') !~ '^[1-9][0-9]{0,18}$' THEN
      RAISE EXCEPTION 'Mixed-bulk draft requires an observed pool revision string; reload and reselect' USING ERRCODE='22023'; END IF;
     IF (line->>'poolRevision')::numeric>9223372036854775807 THEN RAISE EXCEPTION 'Pool revision out of range' USING ERRCODE='22023'; END IF;
     observed_revision:=(line->>'poolRevision')::bigint;
     SELECT * INTO pool FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=loc.id;
     IF pool.lot_id IS DISTINCT FROM lot.id OR pool.revision IS DISTINCT FROM observed_revision THEN
      RAISE EXCEPTION 'Pool changed; reload, reselect and save the draft again' USING ERRCODE='23514'; END IF;
    ELSIF line->>'poolRevision' IS NOT NULL THEN
     RAISE EXCEPTION 'Ordinary position cannot carry a pool revision' USING ERRCODE='22023';
    END IF;
    IF jsonb_typeof(line->'quantityKg') IS DISTINCT FROM 'string' THEN RAISE EXCEPTION 'Loading kg must be a decimal string' USING ERRCODE='22023'; END IF;
    amount:=grainledger.ledger_kg(line->>'quantityKg');
    IF amount<=0 THEN RAISE EXCEPTION 'Loading kg must be positive' USING ERRCODE='22023'; END IF;
    total:=total+amount;
    normalized:=normalized||jsonb_build_array(jsonb_build_object('lotId',lot.id,'lotCode',lot.code,'locationId',loc.id,'quantityKg',amount::numeric(20,6)::text,'poolRevision',observed_revision::text));
   END LOOP;
   IF total>c.target_kg THEN RAISE EXCEPTION 'Draft exceeds container target' USING ERRCODE='23514'; END IF;
   -- Multiple drafts are alternative preparations, not committed loaded mass or reservations.
   INSERT INTO grainledger.ledger_container_loading_drafts(id,container_id,loaded_at,lines,recorded_by) VALUES(draft,target,(p->>'loadedAt')::timestamptz,normalized,a)
    ON CONFLICT(id) DO UPDATE SET loaded_at=excluded.loaded_at,lines=excluded.lines,recorded_by=a,recorded_at=now();
   UPDATE grainledger.ledger_containers SET version=gen_random_uuid() WHERE id=target;
  ELSIF action IN ('loading-check','close-check','commit-loading','close') THEN
   findings:=jsonb_build_array(
    jsonb_build_object('code','ORDER_PLAN_AUTHORITY_UNAVAILABLE','message','Approved order and blend output authority is not connected.'),
    jsonb_build_object('code','OUTPUT_RESERVATION_UNAVAILABLE','message','Output stock reservations are not connected. Drafts do not hold stock.'),
    jsonb_build_object('code','EXPORT_ASSESSMENT_UNAVAILABLE','message','Current export assessment and evidence freshness are not connected.'),
    jsonb_build_object('code','CONTAINER_RESTRICTIONS_UNAVAILABLE','message','Container HOLD and risk checks are not connected.'));
   IF c.order_id IS NULL THEN findings:=findings||jsonb_build_array(jsonb_build_object('code','ORDER_REFERENCE_MISSING','message','An order reference is required before loading.')); END IF;
   IF c.container_number IS NULL OR c.seal_number IS NULL THEN findings:=findings||jsonb_build_array(jsonb_build_object('code','SEAL_METADATA_MISSING','message','Container number and seal are required before closure.')); END IF;
   IF action IN ('loading-check','commit-loading') THEN
    draft:=(p->>'draftId')::uuid;
    IF draft IS NULL OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_loading_drafts WHERE id=draft AND container_id=target) THEN RAISE EXCEPTION 'Loading draft unavailable' USING ERRCODE='22023'; END IF;
    FOR line IN SELECT value FROM grainledger.ledger_container_loading_drafts d CROSS JOIN LATERAL jsonb_array_elements(d.lines) WHERE d.id=draft LOOP
     total:=total+grainledger.ledger_kg(line->>'quantityKg');
    END LOOP;
    -- Retained draft observations never change when a current pool advances.
    FOR line IN SELECT value FROM grainledger.ledger_container_loading_drafts d CROSS JOIN LATERAL jsonb_array_elements(d.lines) WHERE d.id=draft LOOP
     SELECT * INTO lot FROM grainledger.ledger_lots WHERE id=(line->>'lotId')::uuid;
     SELECT * INTO loc FROM grainledger.ledger_locations WHERE id=(line->>'locationId')::uuid;
     IF lot.owner_id IS DISTINCT FROM c.owner_id OR loc.facility_id IS DISTINCT FROM c.facility_id
        OR loc.is_deleted OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_facilities f WHERE f.id=loc.facility_id AND NOT f.is_deleted) THEN
      findings:=findings||jsonb_build_array(jsonb_build_object('code','POSITION_UNAVAILABLE','message','Draft position is unavailable; reload and reselect.','lotId',lot.id));
     END IF;
     IF loc.mixed_bulk THEN
      SELECT * INTO pool FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=loc.id;
      IF line->>'poolRevision' IS NULL THEN
       findings:=findings||jsonb_build_array(jsonb_build_object('code','POOL_REVISION_NOT_RECORDED','message','Legacy mixed-bulk draft has no observed revision; reload, reselect and save again.','lotId',lot.id));
      ELSIF pool.lot_id IS DISTINCT FROM lot.id OR pool.revision::text IS DISTINCT FROM line->>'poolRevision' THEN
       findings:=findings||jsonb_build_array(jsonb_build_object('code','POOL_CHANGED','message','Pool changed since draft selection; reload, reselect and save again.','lotId',lot.id));
      END IF;
     END IF;
    END LOOP;
    -- Aggregate duplicate lot/location rows before comparing with the current physical stock.
    FOR line IN SELECT jsonb_build_object('lotId',l->>'lotId','locationId',l->>'locationId','quantityKg',sum(grainledger.ledger_kg(l->>'quantityKg'))::text)
     FROM grainledger.ledger_container_loading_drafts d CROSS JOIN LATERAL jsonb_array_elements(d.lines) l WHERE d.id=draft GROUP BY l->>'lotId',l->>'locationId' LOOP
     SELECT * INTO lot FROM grainledger.ledger_lots WHERE id=(line->>'lotId')::uuid;
     SELECT * INTO pos FROM grainledger.ledger_positions WHERE lot_id=lot.id AND location_id=(line->>'locationId')::uuid;
     IF pos.lot_id IS NULL OR pos.quantity_kg<grainledger.ledger_kg(line->>'quantityKg') THEN findings:=findings||jsonb_build_array(jsonb_build_object('code','INSUFFICIENT_PHYSICAL_STOCK','message','Draft exceeds current physical stock.','lotId',lot.id)); END IF;
     IF grainledger.ledger_blocked(lot.id) THEN findings:=findings||jsonb_build_array(jsonb_build_object('code','LOT_RESTRICTED','message','A source lot has a HOLD, receiving or correction restriction.','lotId',lot.id)); END IF;
    END LOOP;
   ELSE
    findings:=findings||jsonb_build_array(jsonb_build_object('code','EXACT_TARGET_NOT_LOADED','message','No committed loading exists. Draft quantities do not count toward closure.'));
   END IF;
   RETURN jsonb_build_object('preparationVersion',2,'checkedAt',clock_timestamp(),'scope',CASE WHEN action IN ('loading-check','commit-loading') THEN 'LOADING_DRAFT' ELSE 'CLOSURE' END,'stockChecksPerformed',action IN ('loading-check','commit-loading'),'ready',false,'complete',false,'containerId',target,'version',c.version,'draftKg',total::numeric(20,6)::text,'committedLoadedKg','0.000000','targetKg',c.target_kg::text,'findings',findings);
  ELSE RAISE EXCEPTION 'Unsupported container action' USING ERRCODE='22023';
  END IF;
 END IF;
 result:=grainledger.ledger_container_json(target);
 INSERT INTO grainledger.ledger_container_events(container_id,actor_id,kind,details) VALUES(target,a,'CONTAINER_'||upper(replace(action,'-','_')),jsonb_build_object('requestId',req,'before',CASE WHEN action='create' THEN NULL ELSE to_jsonb(c) END,'after',result));
 INSERT INTO grainledger.ledger_container_requests VALUES(a,req,action,p,result);
 RETURN result;
END $$;
REVOKE ALL ON FUNCTION grainledger.ledger_container_json(uuid),grainledger.ledger_container_action(uuid,text,jsonb) FROM PUBLIC;

CREATE OR REPLACE FUNCTION grainledger.ledger_available_lots_page(a uuid,p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
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
    IF grainledger.ledger_role(a) IS NULL THEN RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501'; END IF;
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
            coalesce((SELECT sum(pos.quantity_kg) FROM grainledger.ledger_positions pos
                JOIN grainledger.ledger_locations lc ON lc.id=pos.location_id
                LEFT JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.location_id=lc.id
                WHERE pos.lot_id=l.id AND pos.quantity_kg>0
                  AND (NOT lc.mixed_bulk OR pool.lot_id=l.id)
                  AND (grainledger.ledger_own(a,l.owner_id) OR grainledger.ledger_assigned(a,lc.facility_id))
                  AND (requested_facility IS NULL OR lc.facility_id=requested_facility)
                  AND (requested_location IS NULL OR lc.id=requested_location)),0) quantity
        FROM grainledger.ledger_lots l JOIN grainledger.identity_organizations o ON o."Id"=l.owner_id
        WHERE grainledger.ledger_can_read_lot(a,l.id)
          AND (requested_owner IS NULL OR l.owner_id=requested_owner)
          AND (p->>'stage' IS NULL OR l.stage=p->>'stage')
          AND (requested_facility IS NULL AND requested_location IS NULL OR EXISTS(
              SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_locations lc ON lc.id=pos.location_id
              WHERE pos.lot_id=l.id
                AND (grainledger.ledger_own(a,l.owner_id) OR grainledger.ledger_assigned(a,lc.facility_id))
                AND (requested_facility IS NULL OR lc.facility_id=requested_facility)
                AND (requested_location IS NULL OR lc.id=requested_location)))
          AND (requested_ids IS NULL OR l.id=ANY(requested_ids))
          AND (requested_ids IS NOT NULL OR NOT (
              EXISTS(SELECT 1 FROM grainledger.ledger_custody_events ce WHERE ce.input_lot_id=l.id OR ce.output_lot_id=l.id)
              OR EXISTS(SELECT 1 FROM grainledger.ledger_custody_allocations ca WHERE ca.input_lot_id=l.id OR ca.output_lot_id=l.id)
              OR grainledger.ledger_mixed_bulk_custody_derived(l.id)
          ) OR EXISTS(
              SELECT 1 FROM grainledger.ledger_positions pos
              JOIN grainledger.ledger_locations lc ON lc.id=pos.location_id
              LEFT JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.location_id=lc.id
              WHERE pos.lot_id=l.id AND pos.quantity_kg>0
                AND (NOT lc.mixed_bulk OR pool.lot_id=l.id)
                AND (grainledger.ledger_own(a,l.owner_id) OR grainledger.ledger_assigned(a,lc.facility_id))
                AND (requested_facility IS NULL OR lc.facility_id=requested_facility)
                AND (requested_location IS NULL OR lc.id=requested_location)))
          AND (NOT coalesce((p->>'availableOnly')::boolean,false) OR (
              NOT grainledger.ledger_blocked(l.id) AND EXISTS(
                  SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_locations lc ON lc.id=pos.location_id
                  JOIN grainledger.ledger_facilities f ON f.id=lc.facility_id
                  LEFT JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.location_id=lc.id
                  WHERE pos.lot_id=l.id AND pos.quantity_kg>grainledger.ledger_blend_reserved_kg(pos.lot_id,pos.location_id) AND NOT lc.is_deleted AND NOT f.is_deleted
                    AND (NOT lc.mixed_bulk OR pool.lot_id=l.id)
                    AND (grainledger.ledger_own(a,l.owner_id) OR grainledger.ledger_assigned(a,lc.facility_id))
                    AND (requested_facility IS NULL OR lc.facility_id=requested_facility)
                    AND (requested_location IS NULL OR lc.id=requested_location))))
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
        'items',coalesce((SELECT jsonb_agg(grainledger.ledger_lot_json(a,x.id,false)||jsonb_build_object('ownerName',x.owner_name) ORDER BY x.ordinal) FROM selected x),'[]'::jsonb),
        'page',page_number,'pageSize',page_size,'totalCount',(SELECT count(*) FROM candidates),
        'hasMore',(SELECT count(*) FROM candidates)>page_number::bigint*page_size)
    INTO result;
    RETURN result;
END $$;
