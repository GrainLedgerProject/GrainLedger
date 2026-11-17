-- Re-review creates a new reserve event, retaining the original reservation and every release.
ALTER TABLE grainledger.ledger_blend_reservation_events DROP CONSTRAINT ledger_blend_reservation_events_action_check;
ALTER TABLE grainledger.ledger_blend_reservation_events ADD CHECK(action IN ('RELEASE','CONSUME','RESERVE'));
CREATE OR REPLACE FUNCTION grainledger.ledger_blend_reserved_kg(lot uuid, location uuid) RETURNS numeric
LANGUAGE sql STABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
 SELECT coalesce(sum(r.quantity_kg-coalesce((SELECT sum(CASE WHEN e.action='RESERVE' THEN -e.quantity_kg ELSE e.quantity_kg END)
 FROM grainledger.ledger_blend_reservation_events e WHERE (e.proposal_id,e.lot_id,e.location_id)=(r.proposal_id,r.lot_id,r.location_id)),0)),0)
 FROM grainledger.ledger_blend_reservations r WHERE r.lot_id=lot AND r.location_id=location
$$;
-- One approved blend, one ordinary finished output. All calls use the caller transaction.
CREATE TABLE grainledger.ledger_container_production (
 container_id uuid PRIMARY KEY REFERENCES grainledger.exports_containers(id),
 proposal_id uuid NOT NULL UNIQUE REFERENCES grainledger.exports_blend_plan_proposals(id),
 facility_id uuid NOT NULL REFERENCES grainledger.ledger_facilities(id),
 requested_by uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
 operation_id uuid UNIQUE REFERENCES grainledger.ledger_operations(id),
 output_id uuid UNIQUE REFERENCES grainledger.ledger_lots(id),
 output_location_id uuid REFERENCES grainledger.ledger_locations(id),
 state text NOT NULL CHECK(state IN ('REQUESTED','DRAFT','PRODUCED','APPROVED','REJECTED')),
 version integer NOT NULL DEFAULT 1 CHECK(version>0),
 review_id uuid,
 created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE grainledger.ledger_container_output_reviews (
 id uuid PRIMARY KEY, container_id uuid NOT NULL REFERENCES grainledger.ledger_container_production(container_id),
 actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
 decision text NOT NULL CHECK(decision IN ('APPROVED','REJECTED')),
 reason text NOT NULL CHECK(length(btrim(reason)) BETWEEN 1 AND 1000),
 assessment jsonb NOT NULL, snapshot jsonb, recorded_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
ALTER TABLE grainledger.ledger_container_production ADD FOREIGN KEY(review_id) REFERENCES grainledger.ledger_container_output_reviews(id);
CREATE TABLE grainledger.ledger_container_production_requests (
 actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"), operation_id uuid NOT NULL,
 container_id uuid NOT NULL REFERENCES grainledger.exports_containers(id), action text NOT NULL,
 payload jsonb NOT NULL, result jsonb NOT NULL, PRIMARY KEY(actor_id,operation_id)
);
CREATE TABLE grainledger.ledger_container_production_events (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), container_id uuid NOT NULL REFERENCES grainledger.exports_containers(id),
 actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"), action text NOT NULL,
 details jsonb NOT NULL, recorded_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
-- Production is distinct from loading. Its marker freezes demand and revocation even while loaded=0.
CREATE FUNCTION grainledger.ledger_protect_production_scope() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
BEGIN
 IF TG_TABLE_NAME='exports_containers' THEN
  IF EXISTS(SELECT 1 FROM grainledger.ledger_container_production p WHERE p.container_id=OLD.id AND p.output_id IS NOT NULL)
   AND (NEW.target_mass_kg<>OLD.target_mass_kg OR NEW.order_id<>OLD.order_id) THEN
   RAISE EXCEPTION 'Production has started; demand revision requires an explicit workflow' USING ERRCODE='23514'; END IF;
 ELSIF TG_TABLE_NAME='exports_export_orders' THEN
  IF EXISTS(SELECT 1 FROM grainledger.ledger_container_production p JOIN grainledger.exports_containers c ON c.id=p.container_id WHERE c.order_id=OLD.id AND p.output_id IS NOT NULL) THEN
   RAISE EXCEPTION 'Production has started; order revision requires an explicit workflow' USING ERRCODE='23514'; END IF;
 ELSE
  IF EXISTS(SELECT 1 FROM grainledger.ledger_container_production p WHERE p.proposal_id=OLD.proposal_id AND p.output_id IS NOT NULL) THEN
   RAISE EXCEPTION 'Produced approval cannot be revoked as unused stock' USING ERRCODE='23514'; END IF;
 END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
END $$;
CREATE TRIGGER ledger_protect_production_scope BEFORE UPDATE ON grainledger.exports_containers FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_protect_production_scope();
CREATE TRIGGER ledger_protect_production_scope BEFORE UPDATE ON grainledger.exports_export_orders FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_protect_production_scope();
CREATE TRIGGER ledger_protect_production_scope BEFORE DELETE ON grainledger.exports_active_blend_approvals FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_protect_production_scope();

CREATE FUNCTION grainledger.ledger_production_assert_basis(b jsonb, company uuid) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog,grainledger,pg_temp AS $$
BEGIN
 IF b->>'assessmentStatus' IS DISTINCT FROM 'PASS' THEN RAISE EXCEPTION 'Current PASS assessment required' USING ERRCODE='23514'; END IF;
 IF (SELECT count(*) FROM grainledger.catalog_mrl_versions m WHERE m.market_id=(b->'evidence'->'applicableMrl'->>'marketId')::uuid
   AND m.commodity_id=(b->'evidence'->'applicableMrl'->>'commodityId')::uuid AND m.status='PUBLISHED'
   AND m.published_at<=clock_timestamp() AND m.effective_from<=clock_timestamp() AND (m.effective_to IS NULL OR clock_timestamp()<m.effective_to))<>1
 OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_mrl_versions m WHERE m.id=(b->'evidence'->'applicableMrl'->>'versionId')::uuid
   AND m.status='PUBLISHED' AND m.effective_from<=clock_timestamp() AND (m.effective_to IS NULL OR clock_timestamp()<m.effective_to))
 OR (SELECT id FROM grainledger.catalog_blend_assessment_policies q WHERE q.company_id=company
   AND q.market_id=(b->'evidence'->'assessmentPolicy'->>'marketId')::uuid AND q.commodity_id=(b->'evidence'->'assessmentPolicy'->>'commodityId')::uuid
   AND q.published_at<=clock_timestamp() AND q.effective_from<=clock_timestamp() AND (q.effective_to IS NULL OR clock_timestamp()<q.effective_to)
   ORDER BY q.version_no DESC,q.id LIMIT 1) IS DISTINCT FROM (b->'evidence'->'assessmentPolicy'->>'id')::uuid THEN
  RAISE EXCEPTION 'Assessment dependencies changed at commit' USING ERRCODE='23514'; END IF;
 IF b->'evidence'->'outputYieldRule'->>'id' IS NOT NULL AND (SELECT y.id FROM grainledger.ledger_planning_yield_rules y
   WHERE y.compatibility_id=(b->'evidence'->'outputYieldRule'->>'compatibilityPolicyId')::uuid
   AND y.facility_id=(b->'evidence'->'outputYieldRule'->>'facilityId')::uuid AND y.published_at<=clock_timestamp()
   AND y.effective_from<=clock_timestamp() AND (y.effective_to IS NULL OR clock_timestamp()<y.effective_to)
   ORDER BY y.version_no DESC,y.id LIMIT 1) IS DISTINCT FROM (b->'evidence'->'outputYieldRule'->>'id')::uuid THEN
  RAISE EXCEPTION 'Yield policy changed at commit' USING ERRCODE='23514'; END IF;
END $$;

CREATE FUNCTION grainledger.ledger_container_production(a uuid,target uuid,action text,p jsonb DEFAULT '{}'::jsonb,basis jsonb DEFAULT NULL) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
#variable_conflict use_variable
DECLARE task grainledger.ledger_container_production; c grainledger.exports_containers; prop grainledger.exports_blend_plan_proposals;
 role_name text:=grainledger.ledger_role(a); facility uuid; decision integer; prior grainledger.ledger_container_production_requests;
 operation uuid; item record; op grainledger.ledger_operations; content jsonb; result jsonb; output jsonb;
 compatibility grainledger.ledger_compatibilities; qty numeric; outstanding numeric; quality jsonb; yield jsonb; review uuid;
BEGIN
 IF action NOT IN ('list','detail','context') THEN PERFORM grainledger.ledger_lock_writes(); role_name:=grainledger.ledger_role(a); END IF;
 IF role_name IS NULL THEN RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501'; END IF;
 IF action='list' THEN
  IF role_name NOT IN ('MillOperator','MillManager') THEN RAISE EXCEPTION 'Assigned mill actor required' USING ERRCODE='42501'; END IF;
  RETURN jsonb_build_object('items',coalesce((SELECT jsonb_agg(jsonb_build_object('containerId',t.container_id,'code',x.internal_code,'facilityId',t.facility_id,'state',t.state,'version',t.version))
   FROM grainledger.ledger_container_production t JOIN grainledger.exports_containers x ON x.id=t.container_id
   WHERE grainledger.ledger_assigned(a,t.facility_id) AND x.status<>'CLOSED'),'[]'::jsonb));
 END IF;
 SELECT * INTO c FROM grainledger.exports_containers WHERE id=target;
 SELECT * INTO task FROM grainledger.ledger_container_production WHERE container_id=target;
 IF c.id IS NULL OR NOT (role_name IN ('ExportStaff','CustomerManager') AND grainledger.ledger_own(a,c.company_id)
   OR role_name IN ('MillOperator','MillManager') AND task.container_id IS NOT NULL AND grainledger.ledger_assigned(a,task.facility_id)) THEN
  RAISE EXCEPTION 'Production unavailable in current scope' USING ERRCODE='42501'; END IF;
 SELECT q.* INTO prop FROM grainledger.exports_blend_plan_proposals q JOIN grainledger.exports_active_blend_approvals active ON active.proposal_id=q.id
  WHERE active.container_id=target AND active.company_id=c.company_id;
 IF action='detail' THEN
  IF task.container_id IS NULL THEN RETURN jsonb_build_object('containerId',target,'state','NOT_REQUESTED','version',0); END IF;
  RETURN jsonb_build_object('containerId',target,'code',c.internal_code,'state',CASE WHEN prop.id IS DISTINCT FROM task.proposal_id AND task.output_id IS NULL THEN 'APPROVAL_CHANGED' ELSE task.state END,'version',task.version,'facilityId',task.facility_id,
   'ownerId',c.company_id,'outputCommodityId',(SELECT output_commodity_id FROM grainledger.ledger_compatibilities WHERE id=(prop.snapshot_json->'selection'->>'compatibilityPolicyId')::uuid),
   'proposalId',task.proposal_id,'operationId',task.operation_id,'outputId',task.output_id,'outputLocationId',task.output_location_id,
   'outputCode',(SELECT code FROM grainledger.ledger_lots WHERE id=task.output_id),'targetKg',c.target_mass_kg::text,'containerVersion',c.version,
   'operation',CASE WHEN task.operation_id IS NOT NULL THEN grainledger.ledger_operation_json(task.operation_id) ELSE NULL END,
   'inputs',coalesce((SELECT jsonb_agg(jsonb_build_object('lotId',i->>'lotId','locationId',i->>'locationId','quantityKg',i->>'quantityKg','lotCode',i->>'lotCode')) FROM jsonb_array_elements(CASE WHEN prop.id=task.proposal_id THEN prop.snapshot_json->'selection'->'allocations' ELSE '[]'::jsonb END) i),'[]'::jsonb));
 END IF;
 -- Internal assessment material is protected by the same task/owner/facility checks, not request-supplied company IDs.
 IF action='context' THEN
  IF basis IS NOT NULL THEN PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id); END IF;
  IF prop.id IS NULL THEN RAISE EXCEPTION 'Active approval required' USING ERRCODE='23514'; END IF;
  facility:=(SELECT (v.request_json->>'FacilityId')::uuid FROM grainledger.exports_blend_plan_previews v WHERE v.id=prop.preview_id);
  IF role_name IN ('MillOperator','MillManager') AND (task.proposal_id IS DISTINCT FROM prop.id OR NOT grainledger.ledger_assigned(a,facility)) THEN RAISE EXCEPTION 'Task authority changed' USING ERRCODE='42501'; END IF;
  SELECT * INTO compatibility FROM grainledger.ledger_compatibilities WHERE id=(prop.snapshot_json->'selection'->>'compatibilityPolicyId')::uuid;
  SELECT jsonb_build_object('id',y.id,'facilityId',y.facility_id,'compatibilityPolicyId',y.compatibility_id,'versionNo',y.version_no,
   'outputFraction',y.output_fraction::text,'effectiveFrom',y.effective_from,'effectiveTo',y.effective_to,'evidenceId',y.evidence_id,'publishedAt',y.published_at)
   INTO yield FROM grainledger.ledger_planning_yield_rules y WHERE y.facility_id=facility AND y.compatibility_id=compatibility.id
   AND y.published_at<=statement_timestamp() AND y.effective_from<=statement_timestamp() AND (y.effective_to IS NULL OR statement_timestamp()<y.effective_to) ORDER BY y.version_no DESC,y.id LIMIT 1;
  SELECT coalesce(jsonb_agg(to_jsonb(q) ORDER BY q.lot_id,q.attribute_id),'[]'::jsonb) INTO quality FROM (
   SELECT DISTINCT ON (q.lot_id,q.attribute_id) q.id,q.lot_id,q.attribute_id,q.version_no,q.measured_value::text,q.unit,q.evidence_id,q.measured_at,q.confirmed_at
   FROM grainledger.ledger_planning_quality_observations q WHERE q.owner_id=c.company_id AND q.facility_id=facility
   AND q.lot_id IN (SELECT (i->>'lotId')::uuid FROM jsonb_array_elements(prop.snapshot_json->'selection'->'allocations') i)
   AND q.confirmed_at<=statement_timestamp() ORDER BY q.lot_id,q.attribute_id,q.version_no DESC,q.id) q;
  RETURN jsonb_build_object('proposalId',prop.id,'snapshot',prop.snapshot_json,'facilityId',facility,'outputCommodityId',compatibility.output_commodity_id,'yieldRule',yield,'quality',quality,
   'operationId',task.operation_id,'outputId',task.output_id,'outputLocationId',task.output_location_id,'state',task.state);
 END IF;
 PERFORM grainledger.ledger_lock_writes();
 SELECT * INTO c FROM grainledger.exports_containers WHERE id=target FOR UPDATE;
 SELECT * INTO task FROM grainledger.ledger_container_production WHERE container_id=target FOR UPDATE;
 IF (action='request' AND role_name<>'ExportStaff') OR (action IN ('draft','commit') AND role_name<>'MillOperator')
  OR (action IN ('approve','reject') AND role_name<>'CustomerManager') OR action NOT IN ('request','draft','commit','approve','reject','replay') THEN
  RAISE EXCEPTION 'Production action authority denied' USING ERRCODE='42501'; END IF;
 IF action='replay' THEN
  -- The API checks the intended action permission before this lookup.
  operation:=(p->>'operationId')::uuid;
 ELSE operation:=(p->>'operationId')::uuid; END IF;
 IF operation IS NULL OR operation='00000000-0000-0000-0000-000000000000' THEN RAISE EXCEPTION 'Stable operation ID required' USING ERRCODE='22023'; END IF;
 SELECT * INTO prior FROM grainledger.ledger_container_production_requests WHERE actor_id=a AND operation_id=operation;
 IF FOUND THEN
  IF (prior.action='request' AND role_name<>'ExportStaff') OR (prior.action IN ('draft','commit') AND role_name<>'MillOperator') OR (prior.action IN ('approve','reject') AND role_name<>'CustomerManager') THEN RAISE EXCEPTION 'Replay authority changed' USING ERRCODE='42501'; END IF;
  IF prior.container_id<>target OR prior.payload<>p OR (action='replay' AND prior.action IS DISTINCT FROM basis->>'action') OR (action<>'replay' AND prior.action<>action) THEN RAISE EXCEPTION 'Operation ID content conflict' USING ERRCODE='23514'; END IF;
  RETURN prior.result;
 END IF;
 IF action='replay' THEN RETURN jsonb_build_object('replayMissing',true); END IF;
 IF jsonb_typeof(p) IS DISTINCT FROM 'object' OR EXISTS(SELECT 1 FROM jsonb_object_keys(p) k WHERE NOT k=ANY(CASE action WHEN 'request' THEN ARRAY['operationId','version','proposalId'] WHEN 'draft' THEN ARRAY['operationId','version','draft'] WHEN 'commit' THEN ARRAY['operationId','version'] ELSE ARRAY['operationId','version','reason'] END)) THEN RAISE EXCEPTION 'Unknown production fields' USING ERRCODE='22023'; END IF;
 IF c.status<>'PLANNED' OR EXISTS(SELECT 1 FROM grainledger.ledger_container_loads WHERE container_id=target) THEN RAISE EXCEPTION 'Production/review is only supported before loading' USING ERRCODE='23514'; END IF;
 IF EXISTS(SELECT 1 FROM grainledger.ledger_container_holds WHERE container_id=target) THEN RAISE EXCEPTION 'Container HOLD blocks production/review' USING ERRCODE='23514'; END IF;
 IF prop.id IS NULL OR prop.snapshot_json->'selection'->>'mode' IS DISTINCT FROM 'BLEND_INPUT_STOCK' THEN RAISE EXCEPTION 'Active approved blend inputs required' USING ERRCODE='23514'; END IF;
 facility:=(SELECT (v.request_json->>'FacilityId')::uuid FROM grainledger.exports_blend_plan_previews v WHERE v.id=prop.preview_id);
 IF action='request' THEN
  IF (task.container_id IS NOT NULL AND (task.proposal_id=prop.id OR task.output_id IS NOT NULL)) OR (p->>'version')::integer<>c.version OR prop.id IS DISTINCT FROM (p->>'proposalId')::uuid THEN RAISE EXCEPTION 'Production request is stale or already exists' USING ERRCODE='23514'; END IF;
  PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id);
  IF EXISTS(SELECT 1 FROM jsonb_array_elements(prop.snapshot_json->'selection'->'allocations') i
   LEFT JOIN grainledger.ledger_positions pos ON pos.lot_id=(i->>'lotId')::uuid AND pos.location_id=(i->>'locationId')::uuid
   JOIN grainledger.ledger_locations loc ON loc.id=(i->>'locationId')::uuid
   WHERE pos.quantity_kg IS NULL OR pos.quantity_kg<grainledger.ledger_kg(i->>'quantityKg') OR loc.mixed_bulk OR loc.is_deleted
    OR grainledger.ledger_mixed_bulk_custody_derived((i->>'lotId')::uuid) OR grainledger.ledger_container_lot_blocked((i->>'lotId')::uuid)) THEN
   RAISE EXCEPTION 'Production inputs require current ordinary unrestricted stock' USING ERRCODE='23514'; END IF;
  INSERT INTO grainledger.ledger_container_production(container_id,proposal_id,facility_id,requested_by,state) VALUES(target,prop.id,facility,a,'REQUESTED')
   ON CONFLICT(container_id) DO UPDATE SET proposal_id=excluded.proposal_id,facility_id=excluded.facility_id,requested_by=excluded.requested_by,operation_id=NULL,state='REQUESTED',version=grainledger.ledger_container_production.version+1;
 ELSE
  IF task.proposal_id IS DISTINCT FROM prop.id OR task.version IS DISTINCT FROM (p->>'version')::integer THEN RAISE EXCEPTION 'Production version changed; reload' USING ERRCODE='23514'; END IF;
  IF action IN ('draft','commit') THEN
   PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillOperator']);
   IF task.state NOT IN ('REQUESTED','DRAFT') THEN RAISE EXCEPTION 'Only one production execution is supported' USING ERRCODE='23514'; END IF;
   SELECT * INTO compatibility FROM grainledger.ledger_compatibilities WHERE id=(prop.snapshot_json->'selection'->>'compatibilityPolicyId')::uuid AND facility_id=facility;
   IF compatibility.id IS NULL OR compatibility.effective_from>clock_timestamp() THEN RAISE EXCEPTION 'Current blend compatibility required' USING ERRCODE='23514'; END IF;
   IF action='draft' THEN
    content:=p->'draft';
    IF content->>'kind' IS DISTINCT FROM 'BLENDING' OR (content->>'ownerId')::uuid IS DISTINCT FROM c.company_id OR (content->>'facilityId')::uuid IS DISTINCT FROM facility
      OR jsonb_array_length(content->'outputs')<>1 OR (task.operation_id IS NOT NULL AND task.operation_id IS DISTINCT FROM (content->>'id')::uuid) THEN RAISE EXCEPTION 'Single approved blend draft required' USING ERRCODE='23514'; END IF;
   ELSE
    SELECT * INTO op FROM grainledger.ledger_operations WHERE id=task.operation_id;
    content:=op.content;
    IF op.id IS NULL OR op.status<>'DRAFT' OR op.is_deleted OR op.kind<>'BLENDING' OR op.owner_id<>c.company_id OR op.facility_id<>facility THEN RAISE EXCEPTION 'Current matching production draft required' USING ERRCODE='23514'; END IF;
   END IF;
   IF content IS NULL OR jsonb_typeof(content->'inputs') IS DISTINCT FROM 'array' OR jsonb_typeof(content->'outputs') IS DISTINCT FROM 'array'
    OR jsonb_typeof(content->'declaredLossKg') IS DISTINCT FROM 'string' THEN RAISE EXCEPTION 'Exact processing arrays and decimal strings required' USING ERRCODE='22023'; END IF;
   IF EXISTS(SELECT 1 FROM jsonb_array_elements(content->'inputs') i WHERE jsonb_typeof(i->'quantityKg') IS DISTINCT FROM 'string')
    OR EXISTS(SELECT 1 FROM jsonb_array_elements(content->'outputs') o WHERE jsonb_typeof(o->'quantityKg') IS DISTINCT FROM 'string') THEN RAISE EXCEPTION 'Processing mass must be a decimal string' USING ERRCODE='22023'; END IF;
   IF jsonb_array_length(content->'inputs') IS DISTINCT FROM jsonb_array_length(prop.snapshot_json->'selection'->'allocations')
    OR EXISTS(SELECT 1 FROM jsonb_array_elements(prop.snapshot_json->'selection'->'allocations') expected
     WHERE (SELECT count(*) FROM jsonb_array_elements(content->'inputs') i WHERE i->>'lotId'=expected->>'lotId' AND i->>'locationId'=expected->>'locationId'
      AND grainledger.ledger_kg(i->>'quantityKg')=grainledger.ledger_kg(expected->>'quantityKg'))<>1) THEN RAISE EXCEPTION 'Production inputs must exactly match approved allocations' USING ERRCODE='23514'; END IF;
   output:=content->'outputs'->0;
   IF output->>'stage' IS DISTINCT FROM 'BLENDED_RICE' OR output->>'productIdentity' IS DISTINCT FROM 'BLENDED_RICE'
    OR (output->>'commodityId')::uuid IS DISTINCT FROM compatibility.output_commodity_id OR grainledger.ledger_kg(output->>'quantityKg')<>c.target_mass_kg
    OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_locations loc WHERE loc.id=(output->>'locationId')::uuid AND loc.facility_id=facility AND NOT loc.mixed_bulk AND NOT loc.is_deleted AND (loc.owner_id IS NULL OR loc.owner_id=c.company_id)) THEN RAISE EXCEPTION 'Exact-target ordinary finished output required' USING ERRCODE='23514'; END IF;
   FOR item IN SELECT * FROM grainledger.ledger_blend_reservations WHERE proposal_id=prop.id LOOP
    outstanding:=item.quantity_kg-coalesce((SELECT sum(CASE WHEN action='RESERVE' THEN -quantity_kg ELSE quantity_kg END) FROM grainledger.ledger_blend_reservation_events e WHERE e.proposal_id=item.proposal_id AND e.lot_id=item.lot_id AND e.location_id=item.location_id),0);
    IF outstanding<>item.quantity_kg OR grainledger.ledger_container_lot_blocked(item.lot_id)
     OR grainledger.ledger_mixed_bulk_custody_derived(item.lot_id) OR EXISTS(SELECT 1 FROM grainledger.ledger_locations loc WHERE loc.id=item.location_id AND loc.mixed_bulk) THEN RAISE EXCEPTION 'Approved input is changed, restricted or requires custody handoff' USING ERRCODE='23514'; END IF;
   END LOOP;
   IF action='draft' THEN
    result:=grainledger.ledger_command(a,'draft',content);
    UPDATE grainledger.ledger_container_production SET operation_id=(result->>'id')::uuid,state='DRAFT',version=version+1 WHERE container_id=target;
   ELSE
    PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id);
    -- Consume before stock debit so the existing reservation guard permits only this protected execution.
    INSERT INTO grainledger.ledger_blend_reservation_events(id,proposal_id,lot_id,location_id,action,quantity_kg,actor_id,recorded_at)
     SELECT gen_random_uuid(),proposal_id,lot_id,location_id,'CONSUME',quantity_kg,a,clock_timestamp() FROM grainledger.ledger_blend_reservations WHERE proposal_id=prop.id;
    result:=grainledger.ledger_commit_operation(a,task.operation_id);
    INSERT INTO grainledger.ledger_blend_reservations(proposal_id,lot_id,location_id,quantity_kg) VALUES(prop.id,(output->>'id')::uuid,(output->>'locationId')::uuid,c.target_mass_kg);
    UPDATE grainledger.ledger_container_production SET output_id=(output->>'id')::uuid,output_location_id=(output->>'locationId')::uuid,state='PRODUCED',version=version+1 WHERE container_id=target;
   END IF;
  ELSE
   IF task.output_id IS NULL OR task.state NOT IN ('PRODUCED','APPROVED','REJECTED') OR length(btrim(coalesce(p->>'reason',''))) NOT BETWEEN 1 AND 1000 THEN RAISE EXCEPTION 'Produced output and review reason required' USING ERRCODE='23514'; END IF;
   IF action='approve' THEN
    PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id);
    IF grainledger.ledger_container_lot_blocked(task.output_id) THEN RAISE EXCEPTION 'Output restricted' USING ERRCODE='23514'; END IF;
    SELECT quantity_kg-coalesce((SELECT sum(CASE WHEN e.action='RESERVE' THEN -e.quantity_kg ELSE e.quantity_kg END) FROM grainledger.ledger_blend_reservation_events e WHERE e.proposal_id=r.proposal_id AND e.lot_id=r.lot_id AND e.location_id=r.location_id),0)
     INTO outstanding FROM grainledger.ledger_blend_reservations r WHERE proposal_id=prop.id AND lot_id=task.output_id AND location_id=task.output_location_id;
    IF task.state='REJECTED' THEN
     IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_positions pos WHERE pos.lot_id=task.output_id AND pos.location_id=task.output_location_id
      AND pos.quantity_kg-grainledger.ledger_blend_reserved_kg(pos.lot_id,pos.location_id)>=c.target_mass_kg) THEN RAISE EXCEPTION 'Output is no longer available for re-review' USING ERRCODE='23514'; END IF;
     INSERT INTO grainledger.ledger_blend_reservation_events(id,proposal_id,lot_id,location_id,action,quantity_kg,actor_id,recorded_at)
      VALUES(gen_random_uuid(),prop.id,task.output_id,task.output_location_id,'RESERVE',c.target_mass_kg,a,clock_timestamp());
     outstanding:=c.target_mass_kg;
    END IF;
    IF outstanding<>c.target_mass_kg OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE lot_id=task.output_id AND location_id=task.output_location_id AND quantity_kg>=outstanding) THEN RAISE EXCEPTION 'Output reservation changed' USING ERRCODE='23514'; END IF;
   ELSE
    INSERT INTO grainledger.ledger_blend_reservation_events(id,proposal_id,lot_id,location_id,action,quantity_kg,actor_id,recorded_at)
     SELECT gen_random_uuid(),prop.id,task.output_id,task.output_location_id,'RELEASE',r.quantity_kg-coalesce((SELECT sum(CASE WHEN e.action='RESERVE' THEN -e.quantity_kg ELSE e.quantity_kg END) FROM grainledger.ledger_blend_reservation_events e WHERE e.proposal_id=r.proposal_id AND e.lot_id=r.lot_id AND e.location_id=r.location_id),0),a,clock_timestamp()
     FROM grainledger.ledger_blend_reservations r WHERE r.proposal_id=prop.id AND r.lot_id=task.output_id AND r.location_id=task.output_location_id
     AND r.quantity_kg>coalesce((SELECT sum(CASE WHEN e.action='RESERVE' THEN -e.quantity_kg ELSE e.quantity_kg END) FROM grainledger.ledger_blend_reservation_events e WHERE e.proposal_id=r.proposal_id AND e.lot_id=r.lot_id AND e.location_id=r.location_id),0);
   END IF;
   review:=operation;
   INSERT INTO grainledger.ledger_container_output_reviews VALUES(review,target,a,CASE action WHEN 'approve' THEN 'APPROVED' ELSE 'REJECTED' END,btrim(p->>'reason'),coalesce(basis,'{}'::jsonb),basis->'outputSnapshot',clock_timestamp());
   UPDATE grainledger.ledger_container_production SET state=CASE action WHEN 'approve' THEN 'APPROVED' ELSE 'REJECTED' END,review_id=review,version=version+1 WHERE container_id=target;
  END IF;
 END IF;
 IF action IN ('commit','approve','reject') THEN UPDATE grainledger.exports_containers SET version=version+1 WHERE id=target; END IF;
 INSERT INTO grainledger.ledger_container_production_events(container_id,actor_id,action,details) VALUES(target,a,action,jsonb_build_object('request',p,'basis',basis));
 IF action IN ('request','commit','approve') THEN PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id); END IF;
 result:=grainledger.ledger_container_production(a,target,'detail');
 INSERT INTO grainledger.ledger_container_production_requests VALUES(a,operation,target,action,p,result);
 RETURN result;
END $$;
REVOKE ALL ON TABLE grainledger.ledger_container_production,grainledger.ledger_container_output_reviews,grainledger.ledger_container_production_requests,grainledger.ledger_container_production_events FROM PUBLIC;
REVOKE ALL ON FUNCTION grainledger.ledger_protect_production_scope(),grainledger.ledger_production_assert_basis(jsonb,uuid),grainledger.ledger_container_production(uuid,uuid,text,jsonb,jsonb) FROM PUBLIC;

CREATE TRIGGER ledger_container_output_reviews_immutable BEFORE UPDATE OR DELETE ON grainledger.ledger_container_output_reviews FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER ledger_container_production_requests_immutable BEFORE UPDATE OR DELETE ON grainledger.ledger_container_production_requests FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER ledger_container_production_events_immutable BEFORE UPDATE OR DELETE ON grainledger.ledger_container_production_events FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

-- Updated views retain the original proposal and add a separate output authorization.
CREATE OR REPLACE FUNCTION grainledger.ledger_export_container_read(a uuid,target uuid,action text DEFAULT 'detail',page_number integer DEFAULT 1,page_size integer DEFAULT 25) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
#variable_conflict use_variable
DECLARE c grainledger.exports_containers; ext grainledger.ledger_container_execution; r text; result jsonb;
BEGIN
 r:=grainledger.ledger_role(a);
 SELECT * INTO c FROM grainledger.exports_containers WHERE id=target;
 SELECT * INTO ext FROM grainledger.ledger_container_execution WHERE container_id=target;
 IF c.id IS NULL OR r IS NULL OR NOT (
   r IN ('ExportStaff','CustomerManager') AND grainledger.ledger_own(a,c.company_id)
   OR r IN ('MillOperator','MillManager') AND grainledger.ledger_assigned(a,ext.facility_id)
   OR r='Administrator' AND action='trace') THEN
  RAISE EXCEPTION 'Container unavailable in current scope' USING ERRCODE='42501'; END IF;
 IF action='trace' AND r NOT IN ('CustomerManager','Administrator') THEN
  RAISE EXCEPTION 'Backward source composition is not permitted' USING ERRCODE='42501'; END IF;
 IF action='history' THEN
  IF page_number<1 OR page_size NOT BETWEEN 1 AND 100 OR (page_number::bigint-1)*page_size>2147483647 THEN
   RAISE EXCEPTION 'Invalid history pagination' USING ERRCODE='22023'; END IF;
  RETURN jsonb_build_object('items',coalesce((SELECT jsonb_agg(to_jsonb(e) ORDER BY e.recorded_at DESC,e.id)
   FROM (SELECT id,kind,actor_id,details,recorded_at FROM grainledger.ledger_container_execution_events WHERE container_id=target
    ORDER BY recorded_at DESC,id LIMIT page_size OFFSET (page_number::bigint-1)*page_size) e),'[]'::jsonb),
    'page',page_number,'pageSize',page_size,'totalCount',(SELECT count(*) FROM grainledger.ledger_container_execution_events WHERE container_id=target),
    'hasMore',(SELECT count(*) FROM grainledger.ledger_container_execution_events WHERE container_id=target)>page_number::bigint*page_size);
 END IF;
 IF action='dossier' THEN
  SELECT dossier INTO result FROM grainledger.ledger_container_closures WHERE container_id=target;
  IF result IS NULL THEN RAISE EXCEPTION 'Container has no immutable closure dossier' USING ERRCODE='23514'; END IF;
  IF r<>'CustomerManager' THEN
   result:=(result-'sourceContributions'-'assessment'-'loads')||jsonb_build_object('redacted',true,
     'assessment',(result->'assessment')-'evidence',
     'loads',(SELECT jsonb_agg(item-'assessment' ORDER BY item->>'recorded_at',item->>'id') FROM jsonb_array_elements(result->'loads') item));
  END IF;
  RETURN result;
 END IF;
 IF action NOT IN ('detail','trace') THEN RAISE EXCEPTION 'Unsupported container read' USING ERRCODE='22023'; END IF;
 SELECT jsonb_build_object('id',c.id,'orderId',c.order_id,'code',c.internal_code,'companyId',c.company_id,
  'targetKg',c.target_mass_kg::text,'status',c.status,'version',c.version,
  'facilityId',ext.facility_id,'proposalId',ext.proposal_id,'containerNumber',ext.container_number,'sealNumber',ext.seal_number,
  'loadedKg',coalesce((SELECT sum(l.quantity_kg) FROM grainledger.ledger_container_load_lines l
   JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target),0)::numeric(20,6)::text,
  'holds',coalesce((SELECT jsonb_agg(jsonb_build_object('id',id,'reason',reason,'recordedAt',recorded_at) ORDER BY recorded_at,id)
   FROM grainledger.ledger_container_holds WHERE container_id=target),'[]'::jsonb),
  'loads',coalesce((SELECT jsonb_agg(jsonb_build_object('id',h.id,'proposalId',h.proposal_id,'loadedAt',h.loaded_at,'recordedAt',h.recorded_at,
   'lines',(SELECT jsonb_agg(jsonb_build_object('lotId',l.lot_id,'lotCode',x.code,'locationId',l.location_id,'quantityKg',l.quantity_kg::text)
    ORDER BY l.lot_id,l.location_id) FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_lots x ON x.id=l.lot_id WHERE l.load_id=h.id))
   ORDER BY h.recorded_at,h.id) FROM grainledger.ledger_container_loads h WHERE h.container_id=target),'[]'::jsonb),
  'approval',(SELECT jsonb_build_object('proposalId',p.id,'planId',p.plan_id,'decisionVersion',d.version,
     'snapshot',p.snapshot_json,'previewRequest',v.request_json,'demandSnapshot',plan.demand_snapshot::text,
     'productionState',(SELECT t.state FROM grainledger.ledger_container_production t WHERE t.container_id=target),
     'productionOperationId',(SELECT t.operation_id FROM grainledger.ledger_container_production t WHERE t.container_id=target),
     'outputReviewId',(SELECT t.review_id FROM grainledger.ledger_container_production t WHERE t.container_id=target AND t.state='APPROVED'),
     'outputSnapshot',(SELECT rev.snapshot FROM grainledger.ledger_container_production t JOIN grainledger.ledger_container_output_reviews rev ON rev.id=t.review_id WHERE t.container_id=target AND t.state='APPROVED'),
     'specificationId',plan.specification_id,'mode',p.snapshot_json->'selection'->>'mode',
     'allocations',coalesce((SELECT jsonb_agg(jsonb_build_object('lotId',res.lot_id,'lotCode',lot.code,'locationId',res.location_id,
       'restricted',grainledger.ledger_container_lot_blocked(res.lot_id),
       'loadingPositionReady',EXISTS(SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_locations loc ON loc.id=pos.location_id JOIN grainledger.ledger_facilities f ON f.id=loc.facility_id
         WHERE pos.lot_id=res.lot_id AND pos.location_id=res.location_id AND pos.quantity_kg>0 AND NOT loc.is_deleted AND NOT f.is_deleted
          AND NOT loc.mixed_bulk AND NOT grainledger.ledger_mixed_bulk_custody_derived(pos.lot_id)
          AND (loc.owner_id IS NULL OR loc.owner_id=c.company_id)
          AND (EXISTS(SELECT 1 FROM grainledger.ledger_container_source_balances WHERE lot_id=pos.lot_id AND location_id=pos.location_id) OR (SELECT count(*) FROM grainledger.ledger_contributions WHERE lot_id=pos.lot_id AND quantity_kg>0)=1 OR
           ((SELECT count(*) FROM grainledger.ledger_positions WHERE lot_id=pos.lot_id AND quantity_kg>0)=1 AND
            (SELECT sum(quantity_kg) FROM grainledger.ledger_contributions WHERE lot_id=pos.lot_id)=pos.quantity_kg))
          AND (NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_source_balances WHERE lot_id=pos.lot_id AND location_id=pos.location_id)
           OR (SELECT sum(quantity_kg) FROM grainledger.ledger_container_source_balances WHERE lot_id=pos.lot_id AND location_id=pos.location_id)=pos.quantity_kg)),
       'reservedKg',res.quantity_kg::text,'remainingKg',(res.quantity_kg-coalesce((SELECT sum(CASE WHEN action='RESERVE' THEN -quantity_kg ELSE quantity_kg END) FROM grainledger.ledger_blend_reservation_events ev
         WHERE ev.proposal_id=res.proposal_id AND ev.lot_id=res.lot_id AND ev.location_id=res.location_id),0))::numeric(20,6)::text)
       ORDER BY res.lot_id,res.location_id) FROM grainledger.ledger_blend_reservations res JOIN grainledger.ledger_lots lot ON lot.id=res.lot_id WHERE res.proposal_id=p.id AND (p.snapshot_json->'selection'->>'mode'='DIRECT_STOCK' OR res.lot_id=(SELECT t.output_id FROM grainledger.ledger_container_production t WHERE t.container_id=target))),'[]'::jsonb))
    FROM grainledger.exports_active_blend_approvals active
    JOIN grainledger.exports_blend_plan_proposals p ON p.id=active.proposal_id
    JOIN grainledger.exports_blend_plan_drafts plan ON plan.id=p.plan_id
    JOIN grainledger.exports_blend_plan_previews v ON v.id=p.preview_id
    JOIN grainledger.exports_blend_plan_decisions d ON d.proposal_id=p.id AND d.version=active.decision_version AND d.action='APPROVED'
    WHERE active.container_id=target AND active.company_id=c.company_id),
  'dossierAvailable',EXISTS(SELECT 1 FROM grainledger.ledger_container_closures WHERE container_id=target)) INTO result;
 result:=result||jsonb_build_object('remainingKg',(c.target_mass_kg-(result->>'loadedKg')::numeric)::numeric(20,6)::text);
 IF action='trace' THEN
  IF (SELECT count(*) FROM grainledger.ledger_container_load_sources s JOIN grainledger.ledger_container_load_lines l ON l.id=s.line_id
       JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target)>50000 THEN
   RAISE EXCEPTION 'Container trace exceeds the supported complete result size' USING ERRCODE='22023'; END IF;
  result:=result-'approval'||jsonb_build_object('sourceContributions',coalesce((SELECT jsonb_agg(jsonb_build_object('sourceLotId',source_lot_id,'quantityKg',kg::numeric(20,6)::text) ORDER BY source_lot_id)
   FROM (SELECT s.source_lot_id,sum(s.quantity_kg) kg FROM grainledger.ledger_container_load_sources s
    JOIN grainledger.ledger_container_load_lines l ON l.id=s.line_id JOIN grainledger.ledger_container_loads h ON h.id=l.load_id
    WHERE h.container_id=target GROUP BY s.source_lot_id) totals),'[]'::jsonb));
  INSERT INTO grainledger.ledger_events(kind,actor_id,details)
   VALUES('TRACEBACK',a,jsonb_build_object('containerId',target,'ownerId',c.company_id,'facilityId',ext.facility_id,'direction','backward'));
 END IF;
 RETURN result;
END $$;


CREATE OR REPLACE FUNCTION grainledger.ledger_export_container_command(a uuid,target uuid,action text,p jsonb,basis jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
#variable_conflict use_variable
DECLARE c grainledger.exports_containers; ext grainledger.ledger_container_execution; req grainledger.ledger_container_execution_requests;
 r text; company uuid; operation uuid; line jsonb; lot grainledger.ledger_lots; loc grainledger.ledger_locations;
 quantity numeric; total numeric:=0; physical numeric; outstanding numeric; source_count integer; line_id uuid;
 event_id uuid:=gen_random_uuid(); selected_proposal uuid; facility uuid; result jsonb; allowed text[];
BEGIN
 PERFORM grainledger.ledger_lock_writes();
 SELECT "OrganizationId" INTO company FROM grainledger.identity_users WHERE "Id"=a AND "IsActive" FOR SHARE;
 r:=grainledger.ledger_role(a);
 IF r IS NULL OR (action='hold' AND r<>'CustomerManager') OR (action<>'hold' AND r<>'ExportStaff') THEN
  RAISE EXCEPTION 'Container mutation authority denied' USING ERRCODE='42501'; END IF;
 SELECT * INTO c FROM grainledger.exports_containers WHERE id=target AND company_id=company FOR UPDATE;
 IF c.id IS NULL OR NOT grainledger.ledger_own(a,c.company_id) THEN RAISE EXCEPTION 'Container unavailable in current company' USING ERRCODE='42501'; END IF;
 allowed:=CASE action WHEN 'metadata' THEN ARRAY['operationId','version','containerNumber','sealNumber']
  WHEN 'hold' THEN ARRAY['operationId','version','reason'] WHEN 'load' THEN ARRAY['operationId','version','proposalId','decisionVersion','loadedAt','lines']
  WHEN 'close' THEN ARRAY['operationId','version'] END;
 IF allowed IS NULL OR jsonb_typeof(p) IS DISTINCT FROM 'object' OR EXISTS(SELECT 1 FROM jsonb_object_keys(p) k WHERE NOT k=ANY(allowed)) THEN
  RAISE EXCEPTION 'Unsupported container command fields' USING ERRCODE='22023'; END IF;
 operation:=(p->>'operationId')::uuid;
 IF operation IS NULL OR operation='00000000-0000-0000-0000-000000000000' THEN RAISE EXCEPTION 'Stable operation ID required' USING ERRCODE='22023'; END IF;
 SELECT * INTO req FROM grainledger.ledger_container_execution_requests WHERE actor_id=a AND operation_id=operation;
 IF FOUND THEN
  IF req.container_id<>target OR req.action<>action OR req.payload<>p THEN RAISE EXCEPTION 'Operation ID reused with different content' USING ERRCODE='23514'; END IF;
  RETURN req.result;
 END IF;
 IF basis IS NULL AND action IN ('load','close') THEN RETURN jsonb_build_object('replayMissing',true); END IF;
 IF c.version IS DISTINCT FROM (p->>'version')::integer THEN RAISE EXCEPTION 'Container version changed; reload' USING ERRCODE='23514'; END IF;
 IF c.status='CLOSED' AND action<>'hold' THEN RAISE EXCEPTION 'Closed container cannot be changed or loaded' USING ERRCODE='23514'; END IF;
 INSERT INTO grainledger.ledger_container_execution(container_id) VALUES(target) ON CONFLICT DO NOTHING;
 SELECT * INTO ext FROM grainledger.ledger_container_execution WHERE container_id=target;
 IF action='metadata' THEN
  UPDATE grainledger.ledger_container_execution SET container_number=nullif(btrim(p->>'containerNumber'),''),seal_number=nullif(btrim(p->>'sealNumber'),'') WHERE container_id=target;
 ELSIF action='hold' THEN
  INSERT INTO grainledger.ledger_container_holds(id,container_id,actor_id,reason) VALUES(operation,target,a,btrim(p->>'reason'));
 ELSE
  IF EXISTS(SELECT 1 FROM grainledger.ledger_container_holds WHERE container_id=target) THEN RAISE EXCEPTION 'Container HOLD blocks loading and closure' USING ERRCODE='23514'; END IF;
  IF basis->>'assessmentStatus' IS DISTINCT FROM 'PASS' OR basis->>'phase' IS DISTINCT FROM (CASE WHEN action='load' THEN 'LOADING' ELSE 'CLOSE' END) THEN
   RAISE EXCEPTION 'Current container assessment is required' USING ERRCODE='23514'; END IF;
  -- Future-dated publications can become effective while calculations run even under the writer lock.
  IF (SELECT count(*) FROM grainledger.catalog_mrl_versions m
       WHERE m.market_id=(basis->'evidence'->'applicableMrl'->>'marketId')::uuid
         AND m.commodity_id=(basis->'evidence'->'applicableMrl'->>'commodityId')::uuid AND m.status='PUBLISHED'
         AND m.published_at<=clock_timestamp() AND m.effective_from<=clock_timestamp()
         AND (m.effective_to IS NULL OR clock_timestamp()<m.effective_to))<>1
     OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_mrl_versions m
       WHERE m.id=(basis->'evidence'->'applicableMrl'->>'versionId')::uuid AND m.status='PUBLISHED'
         AND m.effective_from<=clock_timestamp() AND (m.effective_to IS NULL OR clock_timestamp()<m.effective_to))
     OR (SELECT id FROM grainledger.catalog_blend_assessment_policies policy WHERE policy.company_id=company
       AND policy.market_id=(basis->'evidence'->'assessmentPolicy'->>'marketId')::uuid
       AND policy.commodity_id=(basis->'evidence'->'assessmentPolicy'->>'commodityId')::uuid
       AND policy.published_at<=clock_timestamp() AND policy.effective_from<=clock_timestamp()
       AND (policy.effective_to IS NULL OR clock_timestamp()<policy.effective_to)
       ORDER BY policy.version_no DESC,policy.id LIMIT 1) IS DISTINCT FROM (basis->'evidence'->'assessmentPolicy'->>'id')::uuid THEN
   RAISE EXCEPTION 'Assessment policy or legal version changed at commit; refresh and review approval' USING ERRCODE='23514'; END IF;
  SELECT active.proposal_id INTO selected_proposal FROM grainledger.exports_active_blend_approvals active
   JOIN grainledger.exports_blend_plan_decisions d ON d.proposal_id=active.proposal_id AND d.version=active.decision_version AND d.action='APPROVED'
   WHERE active.container_id=target AND active.company_id=company;
  IF selected_proposal IS NULL OR (ext.proposal_id IS NOT NULL AND ext.proposal_id<>selected_proposal) THEN RAISE EXCEPTION 'Current pinned approval required' USING ERRCODE='23514'; END IF;
  IF action='load' THEN
   IF selected_proposal IS DISTINCT FROM (p->>'proposalId')::uuid OR NOT EXISTS(SELECT 1 FROM grainledger.exports_active_blend_approvals
    WHERE proposal_id=selected_proposal AND decision_version=(p->>'decisionVersion')::integer) THEN RAISE EXCEPTION 'Approval decision changed' USING ERRCODE='23514'; END IF;
   SELECT (v.request_json->>'FacilityId')::uuid INTO facility FROM grainledger.exports_blend_plan_proposals prop
    JOIN grainledger.exports_blend_plan_previews v ON v.id=prop.preview_id
    WHERE prop.id=selected_proposal AND (prop.snapshot_json->'selection'->>'mode'='DIRECT_STOCK' OR EXISTS(SELECT 1 FROM grainledger.ledger_container_production t WHERE t.container_id=target AND t.proposal_id=prop.id AND t.state='APPROVED'));
   IF facility IS NULL THEN RAISE EXCEPTION 'Approved blend execution/output handoff is required before loading' USING ERRCODE='23514'; END IF;
   IF jsonb_typeof(p->'lines') IS DISTINCT FROM 'array' OR jsonb_array_length(p->'lines') NOT BETWEEN 1 AND 100 OR p->>'loadedAt' IS NULL THEN RAISE EXCEPTION 'Loading time and one to 100 lines required' USING ERRCODE='22023'; END IF;
   IF (SELECT count(*) FROM grainledger.ledger_container_loads WHERE container_id=target)>=1000 THEN RAISE EXCEPTION 'Container load limit reached' USING ERRCODE='23514'; END IF;
   IF EXISTS(SELECT 1 FROM jsonb_array_elements(p->'lines') x GROUP BY x->>'lotId',x->>'locationId' HAVING count(*)>1) THEN RAISE EXCEPTION 'Combine duplicate positions before loading' USING ERRCODE='22023'; END IF;
   FOR line IN SELECT * FROM jsonb_array_elements(p->'lines') LOOP
    IF jsonb_typeof(line) IS DISTINCT FROM 'object' OR EXISTS(SELECT 1 FROM jsonb_object_keys(line) k WHERE k NOT IN ('lotId','locationId','quantityKg')) OR jsonb_typeof(line->'quantityKg') IS DISTINCT FROM 'string' THEN RAISE EXCEPTION 'Exact loading line fields and kg string required' USING ERRCODE='22023'; END IF;
    quantity:=grainledger.ledger_kg(line->>'quantityKg');
    IF quantity<=0 THEN RAISE EXCEPTION 'Positive loading kg required' USING ERRCODE='22023'; END IF;
    total:=total+quantity;
   END LOOP;
   IF total+(SELECT coalesce(sum(l.quantity_kg),0) FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target)>c.target_mass_kg THEN RAISE EXCEPTION 'Loading exceeds container target' USING ERRCODE='23514'; END IF;
   INSERT INTO grainledger.ledger_events(id,kind,actor_id,lot_id,details)
    VALUES(event_id,'CONTAINER_LOADING',a,(p->'lines'->0->>'lotId')::uuid,jsonb_build_object('containerId',target,'ownerId',company,'facilityId',facility,'proposalId',selected_proposal,'operationId',operation));
   INSERT INTO grainledger.ledger_container_loads(id,container_id,proposal_id,loaded_at,actor_id,assessment)
    VALUES(event_id,target,selected_proposal,(p->>'loadedAt')::timestamptz,a,basis);
   FOR line IN SELECT * FROM jsonb_array_elements(p->'lines') LOOP
    quantity:=grainledger.ledger_kg(line->>'quantityKg');
    IF EXISTS(SELECT 1 FROM grainledger.ledger_container_production t WHERE t.container_id=target)
      AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_production t WHERE t.container_id=target AND t.state='APPROVED' AND t.output_id=(line->>'lotId')::uuid AND t.output_location_id=(line->>'locationId')::uuid) THEN RAISE EXCEPTION 'Only the reviewed production output may be loaded' USING ERRCODE='23514'; END IF;
    SELECT * INTO lot FROM grainledger.ledger_lots WHERE id=(line->>'lotId')::uuid AND owner_id=company;
    SELECT * INTO loc FROM grainledger.ledger_locations WHERE id=(line->>'locationId')::uuid AND facility_id=facility;
    IF lot.id IS NULL OR loc.id IS NULL OR (loc.owner_id IS NOT NULL AND loc.owner_id<>company) THEN RAISE EXCEPTION 'Loading position outside approved scope' USING ERRCODE='42501'; END IF;
    PERFORM grainledger.ledger_require_active_location(loc.id);
    IF loc.mixed_bulk OR grainledger.ledger_mixed_bulk_custody_derived(lot.id) THEN RAISE EXCEPTION 'Pool/custody reservation handoff is not available for loading' USING ERRCODE='23514'; END IF;
    IF grainledger.ledger_container_lot_blocked(lot.id) THEN RAISE EXCEPTION 'Loading lot is restricted' USING ERRCODE='23514'; END IF;
    SELECT quantity_kg INTO physical FROM grainledger.ledger_positions WHERE lot_id=lot.id AND location_id=loc.id FOR UPDATE;
    SELECT res.quantity_kg-coalesce((SELECT sum(CASE WHEN action='RESERVE' THEN -quantity_kg ELSE quantity_kg END) FROM grainledger.ledger_blend_reservation_events e
      WHERE e.proposal_id=res.proposal_id AND e.lot_id=res.lot_id AND e.location_id=res.location_id),0)
     INTO outstanding FROM grainledger.ledger_blend_reservations res WHERE res.proposal_id=selected_proposal AND res.lot_id=lot.id AND res.location_id=loc.id;
    IF physical IS NULL OR outstanding IS NULL OR quantity>physical OR quantity>outstanding THEN RAISE EXCEPTION 'Loading exceeds stock or approved reservation' USING ERRCODE='23514'; END IF;
    IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_source_balances WHERE lot_id=lot.id AND location_id=loc.id) THEN
     SELECT count(*) INTO source_count FROM grainledger.ledger_contributions WHERE lot_id=lot.id AND quantity_kg>0;
     IF source_count=0 OR source_count>10000 OR (source_count>1 AND (
       (SELECT sum(quantity_kg) FROM grainledger.ledger_contributions WHERE lot_id=lot.id)<>physical
       OR (SELECT count(*) FROM grainledger.ledger_positions WHERE lot_id=lot.id AND quantity_kg>0)<>1)) THEN
      RAISE EXCEPTION 'Position source attribution is unresolved' USING ERRCODE='23514'; END IF;
     INSERT INTO grainledger.ledger_container_source_balances(lot_id,location_id,source_lot_id,quantity_kg)
      SELECT lot.id,loc.id,source_lot_id,CASE WHEN source_count=1 THEN physical ELSE quantity_kg END
      FROM grainledger.ledger_contributions WHERE lot_id=lot.id AND quantity_kg>0;
    END IF;
    IF (SELECT sum(quantity_kg) FROM grainledger.ledger_container_source_balances WHERE lot_id=lot.id AND location_id=loc.id) IS DISTINCT FROM physical THEN
     RAISE EXCEPTION 'External movement changed the retained source complement; reconciliation required' USING ERRCODE='23514'; END IF;
    INSERT INTO grainledger.ledger_container_load_lines(load_id,lot_id,location_id,quantity_kg) VALUES(event_id,lot.id,loc.id,quantity) RETURNING id INTO line_id;
    INSERT INTO grainledger.ledger_container_load_sources(line_id,source_lot_id,quantity_kg)
     SELECT line_id,shares.source_lot_id,shares.quantity_kg FROM grainledger.ledger_container_allocate_sources(quantity,physical,
       (SELECT jsonb_agg(jsonb_build_object('sourceLotId',source_lot_id,'quantityKg',quantity_kg::text))
        FROM grainledger.ledger_container_source_balances WHERE lot_id=lot.id AND location_id=loc.id AND quantity_kg>0)) shares;
    UPDATE grainledger.ledger_container_source_balances b SET quantity_kg=b.quantity_kg-s.quantity_kg
     FROM grainledger.ledger_container_load_sources s WHERE s.line_id=line_id AND b.lot_id=lot.id AND b.location_id=loc.id AND b.source_lot_id=s.source_lot_id;
    IF (SELECT sum(quantity_kg) FROM grainledger.ledger_container_load_sources WHERE ledger_container_load_sources.line_id=line_id) IS DISTINCT FROM quantity THEN RAISE EXCEPTION 'Source allocation did not conserve mass' USING ERRCODE='23514'; END IF;
    INSERT INTO grainledger.ledger_blend_reservation_events(id,proposal_id,lot_id,location_id,action,quantity_kg,actor_id,recorded_at)
      VALUES(gen_random_uuid(),selected_proposal,lot.id,loc.id,'CONSUME',quantity,a,clock_timestamp());
    PERFORM grainledger.ledger_post(event_id,lot.id,loc.id,-quantity);
   END LOOP;
   UPDATE grainledger.ledger_container_execution SET facility_id=facility,proposal_id=selected_proposal WHERE container_id=target;
   UPDATE grainledger.exports_containers SET status='LOADING' WHERE id=target;
  ELSIF action='close' THEN
   IF c.status<>'LOADING' OR (SELECT coalesce(sum(l.quantity_kg),0) FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target)<>c.target_mass_kg THEN RAISE EXCEPTION 'Exact target must be actually loaded before closure' USING ERRCODE='23514'; END IF;
   IF ext.container_number IS NULL OR ext.seal_number IS NULL THEN RAISE EXCEPTION 'Container number and seal required before closure' USING ERRCODE='23514'; END IF;
   IF EXISTS(SELECT 1 FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target AND grainledger.ledger_container_lot_blocked(l.lot_id)) THEN RAISE EXCEPTION 'A loaded lot is currently restricted' USING ERRCODE='23514'; END IF;
   IF (SELECT sum(target_mass_kg) FROM grainledger.exports_containers WHERE order_id=c.order_id)>(SELECT required_mass_kg FROM grainledger.exports_export_orders WHERE id=c.order_id) THEN RAISE EXCEPTION 'Container targets exceed order mass' USING ERRCODE='23514'; END IF;
   INSERT INTO grainledger.ledger_container_closures(container_id,actor_id,dossier) VALUES(target,a,jsonb_build_object(
    'schemaVersion',1,'containerId',target,'orderId',c.order_id,'companyId',company,'containerNumber',ext.container_number,'sealNumber',ext.seal_number,
    'proposalId',selected_proposal,'targetKg',c.target_mass_kg::text,'closedBy',a,'closedAt',clock_timestamp(),'assessment',basis,
    'loads',(SELECT jsonb_agg(to_jsonb(h) ORDER BY h.recorded_at,h.id) FROM grainledger.ledger_container_loads h WHERE container_id=target),
    'lines',(SELECT jsonb_agg(to_jsonb(l) ORDER BY l.id) FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target),
    'sourceContributions',(SELECT jsonb_agg(jsonb_build_object('sourceLotId',source_lot_id,'quantityKg',kg::numeric(20,6)::text,'percentage',(kg*100/c.target_mass_kg)::numeric(18,6)::text) ORDER BY source_lot_id)
     FROM (SELECT s.source_lot_id,sum(s.quantity_kg) kg FROM grainledger.ledger_container_load_sources s JOIN grainledger.ledger_container_load_lines l ON l.id=s.line_id JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target GROUP BY s.source_lot_id) totals)));
   UPDATE grainledger.exports_containers SET status='CLOSED' WHERE id=target;
  ELSE RAISE EXCEPTION 'Unsupported execution command' USING ERRCODE='22023'; END IF;
 END IF;
 UPDATE grainledger.exports_containers SET version=version+1 WHERE id=target;
 INSERT INTO grainledger.ledger_container_execution_events(container_id,actor_id,kind,details)
   VALUES(target,a,upper(action),jsonb_build_object('operationId',operation,'request',p));
 result:=grainledger.ledger_export_container_read(a,target,'detail');
 INSERT INTO grainledger.ledger_container_execution_requests VALUES(a,operation,target,action,p,result);
 RETURN result;
END $$;
