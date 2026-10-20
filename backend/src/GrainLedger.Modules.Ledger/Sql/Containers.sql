-- Container preparation only. No path here grants export authority or posts inventory.
CREATE TABLE ledger.containers (
 id uuid PRIMARY KEY,
 owner_id uuid NOT NULL REFERENCES identity.organizations("Id"),
 facility_id uuid NOT NULL REFERENCES ledger.facilities,
 code text NOT NULL CHECK(length(code) BETWEEN 1 AND 80),
 order_id uuid CHECK(order_id IS NULL OR order_id<>'00000000-0000-0000-0000-000000000000'),
 target_kg numeric(20,6) NOT NULL CHECK(target_kg>0),
 container_number text CHECK(length(container_number) BETWEEN 1 AND 80),
 seal_number text CHECK(length(seal_number) BETWEEN 1 AND 80),
 status text NOT NULL DEFAULT 'OPEN' CHECK(status='OPEN'),
 version uuid NOT NULL DEFAULT gen_random_uuid(),
 created_by uuid NOT NULL REFERENCES identity.users("Id"),
 created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(owner_id,code)
);
CREATE INDEX containers_owner_created ON ledger.containers(owner_id,created_at DESC,id);
CREATE TABLE ledger.container_loading_drafts (
 id uuid PRIMARY KEY,
 container_id uuid NOT NULL REFERENCES ledger.containers,
 loaded_at timestamptz NOT NULL,
 lines jsonb NOT NULL CHECK(jsonb_typeof(lines)='array' AND jsonb_array_length(lines) BETWEEN 1 AND 100),
 recorded_by uuid NOT NULL REFERENCES identity.users("Id"),
 recorded_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX container_drafts_parent ON ledger.container_loading_drafts(container_id,id);
CREATE TABLE ledger.container_events (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 container_id uuid NOT NULL REFERENCES ledger.containers,
 actor_id uuid NOT NULL REFERENCES identity.users("Id"),
 kind text NOT NULL,
 details jsonb NOT NULL,
 recorded_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX container_events_parent ON ledger.container_events(container_id,recorded_at DESC,id);
CREATE TABLE ledger.container_requests (
 actor_id uuid NOT NULL REFERENCES identity.users("Id"),
 request_id uuid NOT NULL,
 action text NOT NULL,
 payload jsonb NOT NULL,
 result jsonb NOT NULL,
 PRIMARY KEY(actor_id,request_id)
);
CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON ledger.container_events FOR EACH ROW EXECUTE FUNCTION ledger.immutable();
CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON ledger.container_requests FOR EACH ROW EXECUTE FUNCTION ledger.immutable();
REVOKE ALL ON TABLE ledger.containers,ledger.container_loading_drafts,ledger.container_events,ledger.container_requests FROM PUBLIC;

CREATE FUNCTION ledger.container_json(target uuid) RETURNS jsonb
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger AS $$
 SELECT jsonb_build_object('id',c.id,'ownerId',c.owner_id,'facilityId',c.facility_id,
  'code',c.code,'orderId',c.order_id,'targetKg',c.target_kg::text,'containerNumber',c.container_number,
  'sealNumber',c.seal_number,'status',c.status,'version',c.version,'createdAt',c.created_at,
  'committedLoadedKg','0.000000','remainingKg',c.target_kg::text,
  'drafts',coalesce((SELECT jsonb_agg(jsonb_build_object('id',d.id,'loadedAt',d.loaded_at,'lines',d.lines,
   'totalKg',(SELECT sum(ledger.kg(l->>'quantityKg'))::text FROM jsonb_array_elements(d.lines) l),
   'recordedAt',d.recorded_at) ORDER BY d.recorded_at,d.id) FROM ledger.container_loading_drafts d WHERE d.container_id=c.id),'[]'::jsonb))
 FROM ledger.containers c WHERE c.id=target
$$;

CREATE FUNCTION ledger.container_action(a uuid, action text, p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE
 r text:=ledger.role(a); actor_owner uuid; c ledger.containers; line jsonb; pos ledger.positions;
 loc ledger.locations; lot ledger.lots; target uuid; draft uuid; req uuid; previous ledger.container_requests;
 result jsonb; findings jsonb:='[]'; normalized jsonb:='[]'; amount numeric; total numeric:=0;
 n integer; size integer; page integer; needle text; mutation boolean; allowed text[];
BEGIN
 IF r IS NULL OR r NOT IN ('ExportStaff','CustomerManager','MillOperator','MillManager') THEN
  RAISE EXCEPTION 'Container access denied' USING ERRCODE='42501';
 END IF;
 SELECT "OrganizationId" INTO actor_owner FROM identity.users WHERE "Id"=a AND "IsActive";
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
   SELECT x.* FROM ledger.containers x WHERE
    (r IN ('ExportStaff','CustomerManager') AND ledger.own(a,x.owner_id) OR r IN ('MillOperator','MillManager') AND ledger.assigned(a,x.facility_id))
    AND (needle='' OR strpos(lower(x.code||' '||coalesce(x.container_number,'')),needle)>0)
  ), rows AS (SELECT * FROM scoped ORDER BY created_at DESC,id OFFSET (page-1)*size LIMIT size)
  SELECT jsonb_build_object('items',coalesce((SELECT jsonb_agg(jsonb_build_object('id',id,'code',code,'status',status,'targetKg',target_kg::text,'facilityId',facility_id,'containerNumber',container_number) ORDER BY created_at DESC,id) FROM rows),'[]'::jsonb),
   'page',page,'pageSize',size,'totalCount',(SELECT count(*) FROM scoped),'hasMore',(SELECT count(*) FROM scoped)>page*size) INTO result;
  RETURN result;
 END IF;
 IF mutation THEN PERFORM ledger.lock_writes(); END IF;
 target:=(p->>'id')::uuid;
 IF target IS NULL OR target='00000000-0000-0000-0000-000000000000' THEN RAISE EXCEPTION 'Container ID required' USING ERRCODE='22023'; END IF;
 IF action<>'create' THEN
  SELECT * INTO c FROM ledger.containers WHERE id=target;
  IF NOT FOUND OR NOT (r IN ('ExportStaff','CustomerManager') AND ledger.own(a,c.owner_id) OR r IN ('MillOperator','MillManager') AND ledger.assigned(a,c.facility_id)) THEN
   RAISE EXCEPTION 'Container unavailable in current scope' USING ERRCODE='42501'; END IF;
 END IF;
 IF action='detail' THEN RETURN ledger.container_json(target); END IF;
 IF action='history' THEN
  SELECT count(*) INTO n FROM ledger.container_events WHERE container_id=target;
  SELECT jsonb_build_object('items',coalesce(jsonb_agg(to_jsonb(e) ORDER BY recorded_at DESC,id),'[]'::jsonb),'page',page,'pageSize',size,'totalCount',n,'hasMore',n>page*size)
   INTO result FROM (SELECT id,kind,actor_id AS "actorId",details,recorded_at FROM ledger.container_events WHERE container_id=target ORDER BY recorded_at DESC,id OFFSET (page-1)*size LIMIT size) e;
  RETURN result;
 END IF;
 IF mutation THEN
  req:=(p->>'requestId')::uuid;
  IF req IS NULL OR req='00000000-0000-0000-0000-000000000000' THEN RAISE EXCEPTION 'Stable request ID required' USING ERRCODE='22023'; END IF;
  SELECT * INTO previous FROM ledger.container_requests WHERE actor_id=a AND request_id=req;
  IF FOUND THEN
   IF previous.action<>action OR previous.payload<>p THEN RAISE EXCEPTION 'Request ID reused with different content' USING ERRCODE='23514'; END IF;
   IF previous.result->>'ownerId' IS DISTINCT FROM actor_owner::text THEN RAISE EXCEPTION 'Request outside current company' USING ERRCODE='42501'; END IF;
   RETURN previous.result;
  END IF;
 END IF;
 IF action='create' THEN
  IF actor_owner IS NULL OR NOT ledger.own(a,actor_owner) THEN RAISE EXCEPTION 'Customer company required' USING ERRCODE='42501'; END IF;
  IF nullif(btrim(p->>'code'),'') IS NULL OR length(btrim(p->>'code'))>80 THEN RAISE EXCEPTION 'Container code required, maximum 80 characters' USING ERRCODE='22023'; END IF;
  IF NOT EXISTS(SELECT 1 FROM ledger.facilities WHERE id=(p->>'facilityId')::uuid AND NOT is_deleted) THEN
   RAISE EXCEPTION 'Active facility required' USING ERRCODE='22023'; END IF;
  IF jsonb_typeof(p->'targetKg') IS DISTINCT FROM 'string' THEN RAISE EXCEPTION 'Target kg must be a decimal string' USING ERRCODE='22023'; END IF;
  amount:=ledger.kg(p->>'targetKg');
  IF amount<=0 THEN RAISE EXCEPTION 'Positive exact target kg required' USING ERRCODE='22023'; END IF;
  INSERT INTO ledger.containers(id,owner_id,facility_id,code,target_kg,order_id,container_number,seal_number,created_by)
   VALUES(target,actor_owner,(p->>'facilityId')::uuid,btrim(p->>'code'),amount,(nullif(p->>'orderId',''))::uuid,nullif(btrim(p->>'containerNumber'),''),nullif(btrim(p->>'sealNumber'),''),a);
 ELSE
  IF action IN ('metadata','loading-draft','commit-loading','close') AND c.version IS DISTINCT FROM (p->>'version')::uuid THEN
   RAISE EXCEPTION 'Container changed; reload before saving' USING ERRCODE='23514'; END IF;
  IF action='metadata' THEN
   UPDATE ledger.containers SET container_number=nullif(btrim(p->>'containerNumber'),''),seal_number=nullif(btrim(p->>'sealNumber'),''),order_id=(nullif(p->>'orderId',''))::uuid,version=gen_random_uuid() WHERE id=target;
  ELSIF action='loading-draft' THEN
   draft:=(p->>'draftId')::uuid;
   IF draft IS NULL OR draft='00000000-0000-0000-0000-000000000000' OR p->>'loadedAt' IS NULL THEN RAISE EXCEPTION 'Draft ID and recorded loading time required' USING ERRCODE='22023'; END IF;
   IF jsonb_typeof(p->'lines') IS DISTINCT FROM 'array' OR jsonb_array_length(p->'lines') NOT BETWEEN 1 AND 100 THEN RAISE EXCEPTION 'One to 100 loading lines required' USING ERRCODE='22023'; END IF;
   IF EXISTS(SELECT 1 FROM ledger.container_loading_drafts WHERE id=draft AND container_id<>target) THEN RAISE EXCEPTION 'Draft identity unavailable' USING ERRCODE='42501'; END IF;
   IF NOT EXISTS(SELECT 1 FROM ledger.container_loading_drafts WHERE id=draft) AND (SELECT count(*) FROM ledger.container_loading_drafts WHERE container_id=target)>=100 THEN RAISE EXCEPTION 'Draft limit reached' USING ERRCODE='23514'; END IF;
   FOR line IN SELECT * FROM jsonb_array_elements(p->'lines') LOOP
    IF jsonb_typeof(line)<>'object' OR EXISTS(SELECT 1 FROM jsonb_object_keys(line) k WHERE k NOT IN ('lotId','locationId','quantityKg')) THEN RAISE EXCEPTION 'Invalid loading line fields' USING ERRCODE='22023'; END IF;
    SELECT * INTO lot FROM ledger.lots WHERE id=(line->>'lotId')::uuid AND owner_id=c.owner_id;
    SELECT * INTO loc FROM ledger.locations WHERE id=(line->>'locationId')::uuid AND facility_id=c.facility_id;
    IF lot.id IS NULL OR loc.id IS NULL OR NOT EXISTS(SELECT 1 FROM ledger.positions WHERE lot_id=lot.id AND location_id=loc.id) THEN RAISE EXCEPTION 'Loading position outside container owner/facility' USING ERRCODE='42501'; END IF;
    PERFORM ledger.require_active_location(loc.id);
    IF jsonb_typeof(line->'quantityKg') IS DISTINCT FROM 'string' THEN RAISE EXCEPTION 'Loading kg must be a decimal string' USING ERRCODE='22023'; END IF;
    amount:=ledger.kg(line->>'quantityKg');
    IF amount<=0 THEN RAISE EXCEPTION 'Loading kg must be positive' USING ERRCODE='22023'; END IF;
    total:=total+amount;
    normalized:=normalized||jsonb_build_array(jsonb_build_object('lotId',lot.id,'lotCode',lot.code,'locationId',loc.id,'quantityKg',amount::numeric(20,6)::text));
   END LOOP;
   IF total>c.target_kg THEN RAISE EXCEPTION 'Draft exceeds container target' USING ERRCODE='23514'; END IF;
   -- Multiple drafts are alternative preparations, not committed loaded mass or reservations.
   INSERT INTO ledger.container_loading_drafts(id,container_id,loaded_at,lines,recorded_by) VALUES(draft,target,(p->>'loadedAt')::timestamptz,normalized,a)
    ON CONFLICT(id) DO UPDATE SET loaded_at=excluded.loaded_at,lines=excluded.lines,recorded_by=a,recorded_at=now();
   UPDATE ledger.containers SET version=gen_random_uuid() WHERE id=target;
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
    IF draft IS NULL OR NOT EXISTS(SELECT 1 FROM ledger.container_loading_drafts WHERE id=draft AND container_id=target) THEN RAISE EXCEPTION 'Loading draft unavailable' USING ERRCODE='22023'; END IF;
    FOR line IN SELECT value FROM ledger.container_loading_drafts d CROSS JOIN LATERAL jsonb_array_elements(d.lines) WHERE d.id=draft LOOP
     total:=total+ledger.kg(line->>'quantityKg');
    END LOOP;
    -- Aggregate duplicate lot/location rows before comparing with the current physical stock.
    FOR line IN SELECT jsonb_build_object('lotId',l->>'lotId','locationId',l->>'locationId','quantityKg',sum(ledger.kg(l->>'quantityKg'))::text)
     FROM ledger.container_loading_drafts d CROSS JOIN LATERAL jsonb_array_elements(d.lines) l WHERE d.id=draft GROUP BY l->>'lotId',l->>'locationId' LOOP
     SELECT * INTO lot FROM ledger.lots WHERE id=(line->>'lotId')::uuid;
     SELECT * INTO pos FROM ledger.positions WHERE lot_id=lot.id AND location_id=(line->>'locationId')::uuid;
     IF pos.lot_id IS NULL OR pos.quantity_kg<ledger.kg(line->>'quantityKg') THEN findings:=findings||jsonb_build_array(jsonb_build_object('code','INSUFFICIENT_PHYSICAL_STOCK','message','Draft exceeds current physical stock.','lotId',lot.id)); END IF;
     IF ledger.blocked(lot.id) THEN findings:=findings||jsonb_build_array(jsonb_build_object('code','LOT_RESTRICTED','message','A source lot has a HOLD, receiving or correction restriction.','lotId',lot.id)); END IF;
    END LOOP;
   ELSE
    findings:=findings||jsonb_build_array(jsonb_build_object('code','EXACT_TARGET_NOT_LOADED','message','No committed loading exists. Draft quantities do not count toward closure.'));
   END IF;
   RETURN jsonb_build_object('ready',false,'complete',false,'containerId',target,'version',c.version,'draftKg',total::numeric(20,6)::text,'committedLoadedKg','0.000000','targetKg',c.target_kg::text,'findings',findings);
  ELSE RAISE EXCEPTION 'Unsupported container action' USING ERRCODE='22023';
  END IF;
 END IF;
 result:=ledger.container_json(target);
 INSERT INTO ledger.container_events(container_id,actor_id,kind,details) VALUES(target,a,'CONTAINER_'||upper(replace(action,'-','_')),jsonb_build_object('requestId',req,'before',CASE WHEN action='create' THEN NULL ELSE to_jsonb(c) END,'after',result));
 INSERT INTO ledger.container_requests VALUES(a,req,action,p,result);
 RETURN result;
END $$;
REVOKE ALL ON FUNCTION ledger.container_json(uuid),ledger.container_action(uuid,text,jsonb) FROM PUBLIC;
