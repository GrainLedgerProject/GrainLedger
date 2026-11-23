-- Follow persisted per-source allocations where available. Older ordinary processing only recorded
-- its committed inputs and resulting composition, so use their positive source intersection.
CREATE FUNCTION grainledger.ledger_hold_ancestry(target uuid) RETURNS SETOF uuid
LANGUAGE sql STABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
 WITH RECURSIVE paths(lot_id,source_lot_id) AS (
  (SELECT target,source_lot_id FROM grainledger.ledger_contributions WHERE lot_id=target AND quantity_kg>0
   UNION
   SELECT target,source_lot_id FROM grainledger.ledger_custody_allocations WHERE output_lot_id=target AND quantity_kg>0
   UNION
   SELECT target,source_lot_id FROM grainledger.ledger_custody_processing_allocations WHERE output_lot_id=target AND quantity_kg>0)
  UNION
  SELECT parent.lot_id,path.source_lot_id
  FROM paths path JOIN grainledger.ledger_lots child ON child.id=path.lot_id
  CROSS JOIN LATERAL (
   SELECT custody.input_lot_id lot_id
   FROM grainledger.ledger_custody_allocations custody
   WHERE custody.output_lot_id=child.id AND custody.source_lot_id=path.source_lot_id AND custody.quantity_kg>0
   UNION
   SELECT processing.input_lot_id
   FROM grainledger.ledger_custody_processing_allocations processing
   WHERE processing.output_lot_id=child.id AND processing.source_lot_id=path.source_lot_id AND processing.quantity_kg>0
   UNION
   SELECT contribution.lot_id
   FROM grainledger.ledger_events event
   CROSS JOIN LATERAL jsonb_array_elements(coalesce(event.details->'resolvedInputs',event.details->'content'->'inputs','[]'::jsonb)) input
   JOIN grainledger.ledger_contributions contribution ON contribution.lot_id=(input->>'lotId')::uuid
    AND contribution.source_lot_id=path.source_lot_id AND contribution.quantity_kg>0
   WHERE event.operation_id=child.operation_id AND event.kind IN ('DRYING','MILLING','MERGE','BLENDING','BAGGING')
    AND (input->>'quantityKg')::numeric>0 AND NOT EXISTS(
     SELECT 1 FROM grainledger.ledger_custody_processing_allocations exact WHERE exact.output_lot_id=child.id)
  ) parent JOIN grainledger.ledger_lots parent_lot ON parent_lot.id=parent.lot_id AND parent_lot.owner_id=child.owner_id
 )
 SELECT target UNION SELECT lot_id FROM paths UNION SELECT source_lot_id FROM paths
$$;
REVOKE ALL ON FUNCTION grainledger.ledger_hold_ancestry(uuid) FROM PUBLIC;

-- Ordinary stock keeps its direct-HOLD behavior outside a retained shipment scope.
CREATE INDEX ledger_container_output_claims_lot ON grainledger.ledger_container_output_claims(lot_id);

CREATE FUNCTION grainledger.ledger_ancestral_hold_ids(target uuid) RETURNS SETOF uuid
LANGUAGE sql STABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
 SELECT h.id
 FROM grainledger.ledger_holds h JOIN grainledger.ledger_lots root ON root.id=h.lot_id
 JOIN grainledger.ledger_lots lot ON lot.id=target AND lot.owner_id=root.owner_id
 WHERE h.lot_id IN (SELECT grainledger.ledger_hold_ancestry(target))
$$;
REVOKE ALL ON FUNCTION grainledger.ledger_ancestral_hold_ids(uuid) FROM PUBLIC;

CREATE OR REPLACE FUNCTION grainledger.ledger_effective_hold_ids(l uuid) RETURNS SETOF uuid
LANGUAGE plpgsql STABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
BEGIN
 IF grainledger.ledger_mixed_bulk_custody_derived(l)
  OR EXISTS(SELECT 1 FROM grainledger.ledger_blend_reservations r WHERE r.lot_id=l)
  OR EXISTS(SELECT 1 FROM grainledger.ledger_container_output_claims o WHERE o.lot_id=l)
  OR EXISTS(SELECT 1 FROM grainledger.ledger_container_load_lines line WHERE line.lot_id=l) THEN
  RETURN QUERY SELECT grainledger.ledger_ancestral_hold_ids(l);
 ELSE
  RETURN QUERY SELECT h.id FROM grainledger.ledger_holds h WHERE h.lot_id=l;
 END IF;
END $$;

CREATE OR REPLACE FUNCTION grainledger.ledger_container_hold_affects(target uuid, hold_id uuid) RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
 SELECT EXISTS(SELECT 1 FROM grainledger.ledger_ancestral_hold_ids(target) h WHERE h=hold_id)
$$;

CREATE OR REPLACE FUNCTION grainledger.ledger_container_lot_blocked(target uuid) RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
 SELECT EXISTS(SELECT 1 FROM grainledger.ledger_ancestral_hold_ids(target))
 OR EXISTS(SELECT 1 FROM grainledger.ledger_hold_ancestry(target) ancestor WHERE grainledger.ledger_mixed_bulk_independent_blocked(ancestor))
$$;

-- Old requests did not retain reliable document provenance.
ALTER TABLE grainledger.ledger_container_documents ADD COLUMN origin text NOT NULL DEFAULT 'UNKNOWN'
 CHECK (origin IN ('GENERATED','UPLOADED','UNKNOWN'));

CREATE OR REPLACE FUNCTION grainledger.ledger_container_workflow(a uuid,target uuid,action text,p jsonb DEFAULT '{}'::jsonb,basis jsonb DEFAULT NULL) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
#variable_conflict use_variable
DECLARE c grainledger.exports_containers; prop grainledger.exports_blend_plan_proposals; plan grainledger.exports_blend_plan_drafts;
 role_name text; facility uuid; operation uuid; prior grainledger.ledger_container_workflow_requests;
 review grainledger.ledger_container_scope_reviews; ctx jsonb; item jsonb; result jsonb; prev uuid; decision integer;
 qty numeric; own_mass numeric; physical numeric; remaining_mass numeric; source uuid; destination uuid; output uuid;
 handoff grainledger.ledger_container_handoffs; content jsonb; actual jsonb; source_lot grainledger.ledger_lots; shares jsonb;
 revision bigint; source_pool uuid; recipient_count integer; hold uuid;
BEGIN
 IF action NOT IN ('context','detail','document-file','notifications','assert-basis','planning','work-items','handoff-quality') THEN PERFORM grainledger.ledger_lock_writes(); END IF;
 role_name:=grainledger.ledger_role(a);
 IF role_name IS NULL THEN RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501'; END IF;
 IF action='work-items' THEN
  IF role_name NOT IN ('MillOperator','MillManager') THEN RAISE EXCEPTION 'Assigned mill actor required' USING ERRCODE='42501'; END IF;
  RETURN jsonb_build_object('items',coalesce((SELECT jsonb_agg(jsonb_build_object('containerId',x.id,'code',x.internal_code,'proposalId',q.id,
   'facilityId',(v.request_json->>'FacilityId')::uuid,'mode',q.snapshot_json->'selection'->>'mode','state',coalesce(t.state,'HANDOFF_AVAILABLE')) ORDER BY x.internal_code)
   FROM grainledger.exports_active_blend_approvals active JOIN grainledger.exports_containers x ON x.id=active.container_id
   JOIN grainledger.exports_blend_plan_proposals q ON q.id=active.proposal_id JOIN grainledger.exports_blend_plan_previews v ON v.id=q.preview_id
   LEFT JOIN grainledger.ledger_container_production t ON t.container_id=x.id AND t.proposal_id=q.id
   WHERE x.status<>'CLOSED' AND grainledger.ledger_assigned(a,(v.request_json->>'FacilityId')::uuid)),'[]'::jsonb));
 END IF;
 IF action='notifications' THEN
  RETURN jsonb_build_object('items',coalesce((SELECT jsonb_agg(jsonb_build_object('id',n.id,'holdId',n.hold_id,'containerId',n.container_id,
   'code',x.internal_code,'message',n.message,'createdAt',n.created_at,'readAt',n.read_at) ORDER BY n.created_at DESC,n.id)
   FROM grainledger.ledger_container_notices n JOIN grainledger.exports_containers x ON x.id=n.container_id
   WHERE n.recipient_id=a AND (role_name IN ('CustomerManager','ExportStaff') AND grainledger.ledger_own(a,x.company_id)
    OR role_name='MillManager' AND EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_proposals q JOIN grainledger.exports_blend_plan_previews v ON v.id=q.preview_id
     WHERE EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_drafts scope_plan WHERE scope_plan.id=q.plan_id AND scope_plan.container_id=x.id) AND grainledger.ledger_assigned(a,(v.request_json->>'FacilityId')::uuid)))),'[]'::jsonb));
 END IF;
 SELECT * INTO c FROM grainledger.exports_containers WHERE id=target;
 IF c.id IS NULL OR NOT (role_name IN ('CustomerManager','ExportStaff') AND grainledger.ledger_own(a,c.company_id)
  OR role_name IN ('MillOperator','MillManager') AND EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_proposals q
   JOIN grainledger.exports_blend_plan_previews v ON v.id=q.preview_id WHERE EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_drafts scope_plan WHERE scope_plan.id=q.plan_id AND scope_plan.container_id=target) AND grainledger.ledger_assigned(a,(v.request_json->>'FacilityId')::uuid))) THEN
  RAISE EXCEPTION 'Container workflow unavailable in current scope' USING ERRCODE='42501'; END IF;
 IF action='planning' THEN RETURN jsonb_build_object('unplannedKg',grainledger.ledger_container_unplanned_kg(target)::numeric(20,6)::text,
  'orderStarted',EXISTS(SELECT 1 FROM grainledger.exports_containers x WHERE x.order_id=c.order_id AND (x.status<>'PLANNED'
   OR EXISTS(SELECT 1 FROM grainledger.ledger_container_batches b WHERE b.container_id=x.id)))); END IF;
 IF action='assert-basis' THEN
  PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id); RETURN jsonb_build_object('valid',true);
 END IF;
 IF action='document-file' THEN
  IF role_name NOT IN ('CustomerManager','ExportStaff') THEN RAISE EXCEPTION 'Customer document access required' USING ERRCODE='42501'; END IF;
  SELECT jsonb_build_object('filename',d.filename,'contentType',d.content_type,'content',encode(d.content,'base64'),'sha256',d.sha256) INTO result
   FROM grainledger.ledger_container_documents d WHERE d.id=(p->>'id')::uuid AND d.container_id=target;
  IF result IS NULL THEN RAISE EXCEPTION 'Document unavailable' USING ERRCODE='42501'; END IF; RETURN result;
 END IF;
 IF action='context' THEN
  SELECT * INTO prop FROM grainledger.exports_blend_plan_proposals WHERE id=(p->>'proposalId')::uuid AND plan_id IN (SELECT id FROM grainledger.exports_blend_plan_drafts WHERE container_id=target) AND company_id=c.company_id;
  IF prop.id IS NULL OR role_name NOT IN ('CustomerManager','ExportStaff') THEN RAISE EXCEPTION 'Own-company proposal required' USING ERRCODE='42501'; END IF;
  RETURN grainledger.ledger_container_review_context(prop.id);
 END IF;
 -- Internal scientific input for an existing scoped handoff; never exposed by an HTTP read route.
 IF action='handoff-quality' THEN
  SELECT * INTO handoff FROM grainledger.ledger_container_handoffs h WHERE h.id=(p->>'handoffId')::uuid AND h.container_id=target;
  IF handoff.id IS NULL OR role_name NOT IN ('CustomerManager','MillOperator') THEN RAISE EXCEPTION 'Handoff assessment denied' USING ERRCODE='42501'; END IF;
  IF role_name='MillOperator' THEN PERFORM grainledger.ledger_require_staff(a,handoff.facility_id,ARRAY['MillOperator']); END IF;
  RETURN coalesce((SELECT jsonb_agg(to_jsonb(q) ORDER BY q.lot_id,q.attribute_id) FROM (
   SELECT DISTINCT ON (q.lot_id,q.attribute_id) q.id,q.lot_id,q.attribute_id,q.version_no,q.measured_value::text,q.unit
   FROM grainledger.ledger_planning_quality_observations q WHERE q.owner_id=c.company_id AND q.facility_id=handoff.facility_id
   AND q.lot_id=(handoff.content->>'lotId')::uuid AND q.confirmed_at<=(p->>'at')::timestamptz
   ORDER BY q.lot_id,q.attribute_id,q.version_no DESC,q.id) q),'[]'::jsonb);
 END IF;
 IF action='detail' THEN
  RETURN jsonb_build_object('containerId',target,'ownerId',c.company_id,'status',c.status,'version',c.version,
   'reviews',CASE WHEN role_name IN ('CustomerManager','ExportStaff') THEN coalesce((SELECT jsonb_agg(jsonb_build_object('id',r.id,'proposalId',r.proposal_id,
    'scope',r.scope,'decision',r.decision,'reason',r.reason,'recordedAt',r.recorded_at) ORDER BY r.recorded_at,r.id) FROM grainledger.ledger_container_scope_reviews r WHERE r.container_id=target),'[]'::jsonb) ELSE '[]'::jsonb END,
   'proposals',coalesce((SELECT jsonb_agg(jsonb_build_object('id',q.id,'planId',q.plan_id,'version',d.version_no,
    'context',grainledger.ledger_container_review_context(q.id)) ORDER BY d.version_no) FROM grainledger.exports_blend_plan_proposals q
    JOIN grainledger.exports_blend_plan_drafts d ON d.id=q.plan_id WHERE d.container_id=target
    AND (role_name IN ('CustomerManager','ExportStaff') OR grainledger.ledger_assigned(a,(SELECT (v.request_json->>'FacilityId')::uuid FROM grainledger.exports_blend_plan_previews v WHERE v.id=q.preview_id)))),'[]'::jsonb),
   'handoffs',coalesce((SELECT jsonb_agg(jsonb_build_object('id',h.id,'proposalId',h.proposal_id,'facilityId',h.facility_id,'kind',h.kind,'state',h.state,'version',h.version,'content',h.content)
    ORDER BY h.recorded_at,h.id) FROM grainledger.ledger_container_handoffs h WHERE h.container_id=target
    AND (role_name IN ('CustomerManager','ExportStaff') OR grainledger.ledger_assigned(a,h.facility_id))),'[]'::jsonb),
   'documents',CASE WHEN role_name IN ('CustomerManager','ExportStaff') THEN coalesce((SELECT jsonb_agg(jsonb_build_object('id',d.id,'documentType',d.document_type,
    'version',d.version,'origin',d.origin,'filename',d.filename,'contentType',d.content_type,'sha256',d.sha256,'reason',d.reason,'actorId',d.actor_id,'recordedAt',d.recorded_at)
    ORDER BY d.document_type,d.version) FROM grainledger.ledger_container_documents d WHERE d.container_id=target),'[]'::jsonb) ELSE '[]'::jsonb END,
   'supplements',CASE WHEN role_name IN ('CustomerManager','ExportStaff') THEN coalesce((SELECT jsonb_agg(jsonb_build_object('id',s.id,'evidenceId',s.evidence_id,
    'reason',s.reason,'actorId',s.actor_id,'recordedAt',s.recorded_at) ORDER BY s.recorded_at,s.id) FROM grainledger.ledger_container_supplements s WHERE s.container_id=target),'[]'::jsonb) ELSE '[]'::jsonb END,
   'holds',coalesce((SELECT jsonb_agg(jsonb_build_object('id',h.id,'reason',h.reason,'recordedAt',h.recorded_at)) FROM grainledger.ledger_container_holds h WHERE h.container_id=target),'[]'::jsonb),
   'holdResponses',coalesce((SELECT jsonb_agg(jsonb_build_object('id',r.id,'holdId',r.hold_id,'action',r.action,'reason',r.reason,'evidenceId',r.evidence_id,'recordedAt',r.recorded_at)
    ORDER BY r.recorded_at,r.id) FROM grainledger.ledger_container_hold_responses r WHERE r.hold_id IN (SELECT id FROM grainledger.ledger_container_holds WHERE container_id=target)),'[]'::jsonb));
 END IF;
 PERFORM grainledger.ledger_lock_writes(); SELECT * INTO c FROM grainledger.exports_containers WHERE id=target FOR UPDATE;
 operation:=(p->>'operationId')::uuid;
 IF operation IS NULL OR operation='00000000-0000-0000-0000-000000000000' THEN RAISE EXCEPTION 'Stable operation ID required' USING ERRCODE='22023'; END IF;
 IF (action IN ('review','handoff-approve') AND role_name<>'CustomerManager')
  OR (action IN ('handoff-draft','handoff-commit') AND role_name<>'MillOperator')
  OR (action IN ('document','supplement') AND role_name<>'ExportStaff')
  OR (action='hold-response' AND role_name<>'MillManager') THEN RAISE EXCEPTION 'Workflow action authority denied' USING ERRCODE='42501'; END IF;
 SELECT * INTO prior FROM grainledger.ledger_container_workflow_requests WHERE actor_id=a AND operation_id=operation;
 IF FOUND THEN
  IF prior.container_id<>target OR prior.action IS DISTINCT FROM coalesce(p->>'replayAction',action) OR prior.payload IS DISTINCT FROM (p-'replayAction') THEN
   RAISE EXCEPTION 'Workflow operation content conflict' USING ERRCODE='23514'; END IF;
  IF prior.action IN ('handoff-draft','handoff-commit') THEN
   SELECT facility_id INTO facility FROM grainledger.ledger_container_handoffs WHERE id=CASE WHEN prior.action='handoff-draft' THEN prior.operation_id ELSE (prior.payload->>'handoffId')::uuid END;
   PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillOperator']);
  ELSIF prior.action='hold-response' THEN PERFORM grainledger.ledger_require_staff(a,(prior.payload->>'facilityId')::uuid,ARRAY['MillManager']); END IF;
  RETURN prior.result;
 END IF;
 IF action='replay' THEN RETURN jsonb_build_object('replayMissing',true); END IF;
 IF c.version IS DISTINCT FROM (p->>'version')::integer THEN RAISE EXCEPTION 'Container version changed; reload' USING ERRCODE='23514'; END IF;
 IF action IN ('review','handoff-draft','handoff-approve','handoff-commit') AND (c.status='CLOSED'
  OR EXISTS(SELECT 1 FROM grainledger.ledger_container_holds WHERE container_id=target)) THEN RAISE EXCEPTION 'Closed or held container cannot execute/review' USING ERRCODE='23514'; END IF;
 IF action='invalidate' THEN
  IF role_name NOT IN ('CustomerManager','ExportStaff','MillOperator','MillManager') OR basis->>'assessmentStatus' IS DISTINCT FROM 'INCOMPLETE' THEN RAISE EXCEPTION 'Scoped current stale basis required' USING ERRCODE='42501'; END IF;
  SELECT proposal_id INTO prev FROM grainledger.exports_active_blend_approvals WHERE container_id=target;
  IF prev IS NULL THEN RETURN grainledger.ledger_container_workflow(a,target,'detail'); END IF;
  IF prev IS NOT NULL THEN
   INSERT INTO grainledger.ledger_container_scope_reviews(id,container_id,proposal_id,actor_id,scope,decision,reason,basis)
    VALUES(operation,target,prev,a,'ALL_REMAINING','STALE','Current evidence or restriction invalidated unfinished authority',basis);
   FOR item IN SELECT jsonb_build_object('lotId',r.lot_id,'locationId',r.location_id) FROM grainledger.ledger_blend_reservations r WHERE r.proposal_id=prev LOOP
    PERFORM grainledger.ledger_container_set_reservation(a,prev,(item->>'lotId')::uuid,(item->>'locationId')::uuid,0);
   END LOOP;
   DELETE FROM grainledger.exports_active_blend_approvals WHERE container_id=target;
  END IF;
 ELSIF action='review' THEN
  IF p->>'scope' NOT IN ('ALL_REMAINING','OUTPUT_ONLY') OR p->>'decision' NOT IN ('APPROVED','REJECTED') OR length(btrim(coalesce(p->>'reason',''))) NOT BETWEEN 1 AND 1000 THEN
   RAISE EXCEPTION 'Review scope, decision and reason required' USING ERRCODE='22023'; END IF;
  SELECT * INTO prop FROM grainledger.exports_blend_plan_proposals WHERE id=(p->>'proposalId')::uuid AND plan_id IN (SELECT id FROM grainledger.exports_blend_plan_drafts WHERE container_id=target) AND company_id=c.company_id;
  IF prop.id IS NULL THEN RAISE EXCEPTION 'Own-company proposal required' USING ERRCODE='42501'; END IF;
  SELECT * INTO plan FROM grainledger.exports_blend_plan_drafts WHERE id=prop.plan_id;
  ctx:=grainledger.ledger_container_review_context(prop.id); facility:=(ctx->>'facilityId')::uuid;
  IF p->>'decision'='APPROVED' THEN
   PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id);
   IF basis->'context' IS DISTINCT FROM ctx OR basis->>'scope' IS DISTINCT FROM p->>'scope' THEN RAISE EXCEPTION 'Review scope changed' USING ERRCODE='23514'; END IF;
   IF p->>'scope'='ALL_REMAINING' AND jsonb_array_length(ctx->'inputs')>0 AND grainledger.ledger_kg(ctx->>'remainingProductionKg')>grainledger.ledger_container_unplanned_kg(target) THEN
    RAISE EXCEPTION 'Another execution already produced the planned remaining demand' USING ERRCODE='23514'; END IF;
   IF p->>'scope'='OUTPUT_ONLY' AND jsonb_array_length(ctx->'outputs')=0 THEN RAISE EXCEPTION 'OUTPUT_ONLY requires existing output' USING ERRCODE='23514'; END IF;
   IF EXISTS(SELECT 1 FROM jsonb_array_elements((ctx->'inputs')||(ctx->'outputs')) i JOIN grainledger.ledger_container_output_claims claimed
    ON claimed.lot_id=(i->>'lotId')::uuid AND claimed.container_id=target AND claimed.proposal_id<>prop.id) THEN
    RAISE EXCEPTION 'Previously produced output keeps its original plan; re-review that plan OUTPUT_ONLY' USING ERRCODE='23514'; END IF;
  END IF;
  INSERT INTO grainledger.ledger_container_scope_reviews(id,container_id,proposal_id,actor_id,scope,decision,reason,basis)
   VALUES(operation,target,prop.id,a,p->>'scope',p->>'decision',btrim(p->>'reason'),coalesce(basis,'{}'::jsonb));
  SELECT proposal_id INTO prev FROM grainledger.exports_active_blend_approvals WHERE container_id=target;
  -- Freeze the old permission and release only its unspent reservations. Immutable loads retain their original plan.
  IF prev IS NOT NULL AND (p->>'decision'='APPROVED' OR prev=prop.id) THEN
   FOR item IN SELECT jsonb_build_object('lotId',r.lot_id,'locationId',r.location_id) FROM grainledger.ledger_blend_reservations r WHERE r.proposal_id=prev LOOP
    PERFORM grainledger.ledger_container_set_reservation(a,prev,(item->>'lotId')::uuid,(item->>'locationId')::uuid,0);
   END LOOP;
   DELETE FROM grainledger.exports_active_blend_approvals WHERE container_id=target;
  END IF;
  SELECT coalesce(max(version),0)+1 INTO decision FROM grainledger.exports_blend_plan_decisions WHERE proposal_id=prop.id;
  INSERT INTO grainledger.exports_blend_plan_decisions(proposal_id,version,company_id,container_id,plan_id,action,actor_id,recorded_at,reason)
   VALUES(prop.id,decision,c.company_id,target,plan.id,p->>'decision',a,clock_timestamp(),CASE WHEN p->>'decision'='REJECTED' THEN btrim(p->>'reason') END);
  IF p->>'decision'='APPROVED' THEN
   INSERT INTO grainledger.exports_active_blend_approvals(company_id,container_id,proposal_id,decision_version) VALUES(c.company_id,target,prop.id,decision);
   FOR item IN SELECT x FROM jsonb_array_elements(CASE WHEN p->>'scope'='ALL_REMAINING' THEN ctx->'inputs' ELSE '[]'::jsonb END) x
    UNION ALL SELECT x||jsonb_build_object('quantityKg',x->>'remainingKg') FROM jsonb_array_elements(ctx->'outputs') x LOOP
    qty:=grainledger.ledger_kg(item->>'quantityKg');
    IF qty>0 THEN
     IF grainledger.ledger_container_lot_blocked((item->>'lotId')::uuid) OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_positions pos
      JOIN grainledger.ledger_lots lot ON lot.id=pos.lot_id JOIN grainledger.ledger_locations loc ON loc.id=pos.location_id
      WHERE pos.lot_id=(item->>'lotId')::uuid AND pos.location_id=(item->>'locationId')::uuid AND lot.owner_id=c.company_id AND loc.facility_id=facility
       AND NOT loc.is_deleted AND (loc.owner_id IS NULL OR loc.owner_id=c.company_id)
       AND (NOT loc.mixed_bulk OR EXISTS(SELECT 1 FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.lot_id=pos.lot_id AND pool.location_id=loc.id))
       AND pos.quantity_kg-grainledger.ledger_blend_reserved_kg(pos.lot_id,pos.location_id)>=qty) THEN
      RAISE EXCEPTION 'Reviewed remaining stock is restricted or unavailable' USING ERRCODE='23514'; END IF;
     PERFORM grainledger.ledger_container_set_reservation(a,prop.id,(item->>'lotId')::uuid,(item->>'locationId')::uuid,qty);
    END IF;
   END LOOP;
  END IF;
 ELSIF action='handoff-draft' THEN
  SELECT * INTO prop FROM grainledger.exports_blend_plan_proposals WHERE id=(p->>'proposalId')::uuid AND plan_id IN (SELECT id FROM grainledger.exports_blend_plan_drafts WHERE container_id=target) AND company_id=c.company_id;
  ctx:=grainledger.ledger_container_review_context(prop.id); facility:=(ctx->>'facilityId')::uuid;
  PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillOperator']);
  IF NOT EXISTS(SELECT 1 FROM grainledger.exports_active_blend_approvals WHERE proposal_id=prop.id) THEN RAISE EXCEPTION 'Active plan required for handoff' USING ERRCODE='23514'; END IF;
  content:=p->'content'; qty:=grainledger.ledger_kg(content->>'quantityKg'); source:=(content->>'fromLocationId')::uuid; destination:=(content->>'toLocationId')::uuid;
  IF p->>'kind' NOT IN ('TRANSFER','BAGGING') OR qty<=0 OR NOT EXISTS(SELECT 1 FROM jsonb_array_elements(ctx->'outputs') i
   WHERE i->>'lotId'=content->>'lotId' AND (i->>'locationId')::uuid=source AND grainledger.ledger_kg(i->>'remainingKg')>=qty) THEN
   RAISE EXCEPTION 'Handoff must name current output and remaining quota' USING ERRCODE='23514'; END IF;
  IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_locations l WHERE l.id=destination AND l.facility_id=facility AND NOT l.is_deleted AND (l.owner_id IS NULL OR l.owner_id=c.company_id)) THEN
   RAISE EXCEPTION 'Handoff destination outside scope' USING ERRCODE='42501'; END IF;
  IF p->>'kind'='TRANSFER' AND source=destination THEN RAISE EXCEPTION 'Transfer requires a different destination' USING ERRCODE='22023'; END IF;
  IF p->>'kind'='BAGGING' THEN
   SELECT * INTO source_lot FROM grainledger.ledger_lots WHERE id=(content->>'lotId')::uuid;
   IF grainledger.ledger_kg(content->>'outputKg')<>qty OR grainledger.ledger_kg(content->>'declaredLossKg')<>0 THEN RAISE EXCEPTION 'Quantity exception is outside reviewed handoff scope' USING ERRCODE='23514'; END IF;
   actual:=grainledger.ledger_command(a,'draft',jsonb_build_object('id',operation,'ownerId',c.company_id,'facilityId',facility,'kind','BAGGING',
    'equipmentId',content->>'equipmentId','occurredAt',content->>'occurredAt','declaredLossKg',content->>'declaredLossKg',
    'inputs',jsonb_build_array(jsonb_build_object('lotId',content->>'lotId','locationId',source,'quantityKg',content->>'quantityKg')
     ||CASE WHEN content ? 'poolRevision' AND content->'poolRevision'<>'null'::jsonb THEN jsonb_build_object('poolRevision',content->'poolRevision') ELSE '{}'::jsonb END),
    'outputs',jsonb_build_array(jsonb_build_object('id',gen_random_uuid(),'code','','commodityId',source_lot.commodity_id,'stage','BAGGED_RICE',
     'productIdentity',source_lot.product_identity,'locationId',destination,'quantityKg',content->>'outputKg'))));
   content:=content||jsonb_build_object('processingOperationId',operation);
  END IF;
  INSERT INTO grainledger.ledger_container_handoffs(id,container_id,proposal_id,facility_id,actor_id,kind,content,state)
   VALUES(operation,target,prop.id,facility,a,p->>'kind',content,'DRAFT');
 ELSIF action IN ('handoff-approve','handoff-commit') THEN
  SELECT * INTO handoff FROM grainledger.ledger_container_handoffs WHERE id=(p->>'handoffId')::uuid AND container_id=target FOR UPDATE;
  IF handoff.id IS NULL OR handoff.version IS DISTINCT FROM (p->>'handoffVersion')::integer THEN RAISE EXCEPTION 'Handoff version changed' USING ERRCODE='23514'; END IF;
  IF NOT EXISTS(SELECT 1 FROM grainledger.exports_active_blend_approvals WHERE proposal_id=handoff.proposal_id) THEN RAISE EXCEPTION 'Handoff plan no longer active' USING ERRCODE='23514'; END IF;
  content:=handoff.content; facility:=handoff.facility_id; source:=(content->>'fromLocationId')::uuid; destination:=(content->>'toLocationId')::uuid;
  qty:=grainledger.ledger_kg(content->>'quantityKg'); SELECT * INTO source_lot FROM grainledger.ledger_lots WHERE id=(content->>'lotId')::uuid;
  IF action='handoff-approve' THEN
   IF handoff.state<>'DRAFT' OR length(btrim(coalesce(p->>'reason',''))) NOT BETWEEN 1 AND 1000 THEN RAISE EXCEPTION 'Draft handoff and review reason required' USING ERRCODE='23514'; END IF;
   PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id);
   UPDATE grainledger.ledger_container_handoffs SET state='APPROVED',reviewed_by=a,review_basis=basis,review_reason=btrim(p->>'reason'),version=version+1 WHERE id=handoff.id;
  ELSE
   PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillOperator']);
   IF handoff.state<>'APPROVED' OR grainledger.ledger_container_lot_blocked(source_lot.id) THEN RAISE EXCEPTION 'Current unrestricted approved handoff required' USING ERRCODE='23514'; END IF;
   PERFORM grainledger.ledger_production_assert_basis(basis,c.company_id);
   IF basis->>'dependencyRevision' IS DISTINCT FROM handoff.review_basis->>'dependencyRevision' THEN RAISE EXCEPTION 'Handoff review evidence changed' USING ERRCODE='23514'; END IF;
   own_mass:=grainledger.ledger_container_outstanding(handoff.proposal_id,source_lot.id,source);
   SELECT quantity_kg INTO physical FROM grainledger.ledger_positions WHERE lot_id=source_lot.id AND location_id=source FOR UPDATE;
   IF own_mass IS NULL OR own_mass<qty OR physical<qty THEN RAISE EXCEPTION 'Handoff exceeds reservation or physical stock' USING ERRCODE='23514'; END IF;
   IF grainledger.ledger_mixed_bulk_custody_derived(source_lot.id) OR (SELECT mixed_bulk FROM grainledger.ledger_locations WHERE id=source) THEN
    IF grainledger.ledger_blend_reserved_kg(source_lot.id,source)<>own_mass THEN RAISE EXCEPTION 'Other-plan pool reservations require a separate reviewed handoff' USING ERRCODE='23514'; END IF;
   ELSE
    PERFORM grainledger.ledger_container_scope_sources(source_lot.id,source);
    SELECT jsonb_agg(jsonb_build_object('sourceLotId',s.source_lot_id,'quantityKg',s.quantity_kg::numeric(20,6)::text)) INTO shares
     FROM grainledger.ledger_container_allocate_sources(qty,physical,(SELECT jsonb_agg(jsonb_build_object('sourceLotId',source_lot_id,'quantityKg',quantity_kg::text))
      FROM grainledger.ledger_container_source_balances WHERE lot_id=source_lot.id AND location_id=source AND quantity_kg>0)) s;
   END IF;
   INSERT INTO grainledger.ledger_container_claim_transitions(handoff_id,proposal_id,lot_id,location_id,quantity_kg,purpose,basis)
    VALUES(handoff.id,handoff.proposal_id,source_lot.id,source,qty,'INPUT_AUTHORIZATION',handoff.review_basis);
   -- Explicitly release OUTPUT and establish the reviewed INPUT authority before its single consumption.
   PERFORM grainledger.ledger_container_set_reservation(a,handoff.proposal_id,source_lot.id,source,0);
   PERFORM grainledger.ledger_container_set_reservation(a,handoff.proposal_id,source_lot.id,source,qty);
   INSERT INTO grainledger.ledger_blend_reservation_events VALUES(gen_random_uuid(),handoff.proposal_id,source_lot.id,source,'CONSUME',qty,a,clock_timestamp());
   IF handoff.kind='TRANSFER' THEN
    actual:=grainledger.ledger_command(a,'transfer',jsonb_build_object('id',handoff.id,'lotId',source_lot.id,'fromLocationId',source,'toLocationId',destination,
     'quantityKg',content->>'quantityKg','occurredAt',content->>'occurredAt')
     ||CASE WHEN content ? 'poolRevision' AND content->'poolRevision'<>'null'::jsonb THEN jsonb_build_object('poolRevision',content->'poolRevision') ELSE '{}'::jsonb END);
    output:=coalesce((actual->>'currentPoolLotId')::uuid,(actual->>'actualLotId')::uuid,source_lot.id);
   ELSE
    actual:=grainledger.ledger_commit_operation(a,(content->>'processingOperationId')::uuid);
    output:=coalesce((actual->'poolOutputs'->0->>'currentPoolLotId')::uuid,(actual->'outputs'->0->>'id')::uuid);
   END IF;
   IF shares IS NOT NULL THEN
    IF (SELECT coalesce(sum(quantity_kg),0) FROM grainledger.ledger_container_source_balances WHERE lot_id=source_lot.id AND location_id=source)>0 THEN
     UPDATE grainledger.ledger_container_source_balances b SET quantity_kg=b.quantity_kg-grainledger.ledger_kg(s->>'quantityKg') FROM jsonb_array_elements(shares) s
      WHERE b.lot_id=source_lot.id AND b.location_id=source AND b.source_lot_id=(s->>'sourceLotId')::uuid;
    END IF;
    IF handoff.kind='TRANSFER' AND output=source_lot.id AND NOT (SELECT mixed_bulk FROM grainledger.ledger_locations WHERE id=destination) THEN
     INSERT INTO grainledger.ledger_container_source_balances(lot_id,location_id,source_lot_id,quantity_kg)
      SELECT output,destination,(s->>'sourceLotId')::uuid,grainledger.ledger_kg(s->>'quantityKg') FROM jsonb_array_elements(shares) s
      ON CONFLICT(lot_id,location_id,source_lot_id) DO UPDATE SET quantity_kg=grainledger.ledger_container_source_balances.quantity_kg+excluded.quantity_kg;
    END IF;
   END IF;
   IF own_mass>qty THEN
    source_pool:=coalesce((actual->>'sourceCurrentPoolLotId')::uuid,
     (SELECT pool.lot_id FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.location_id=source AND pool.lot_id<>source_lot.id),
     (SELECT edge.output_lot_id FROM grainledger.ledger_custody_events ce JOIN grainledger.ledger_custody_edges edge ON edge.event_id=ce.event_id
      JOIN grainledger.ledger_positions pos ON pos.lot_id=edge.output_lot_id AND pos.location_id=source AND pos.quantity_kg>0
      WHERE ce.command_id=handoff.id AND ce.action='WITHDRAW' AND edge.input_lot_id=source_lot.id
       AND edge.output_lot_id<>ce.output_lot_id LIMIT 1),source_lot.id);
    IF source_pool<>source_lot.id THEN
     INSERT INTO grainledger.ledger_container_claim_transitions(handoff_id,proposal_id,lot_id,location_id,quantity_kg,purpose,basis)
      VALUES(handoff.id,handoff.proposal_id,source_lot.id,source,own_mass-qty,'REMAINDER_HANDOFF',handoff.review_basis);
     INSERT INTO grainledger.ledger_container_output_claims(container_id,proposal_id,lot_id,location_id,quantity_kg,operation_id)
      VALUES(target,handoff.proposal_id,source_pool,source,own_mass-qty,handoff.id);
    END IF;
    PERFORM grainledger.ledger_container_set_reservation(a,handoff.proposal_id,source_pool,source,own_mass-qty);
   END IF;
   INSERT INTO grainledger.ledger_container_output_claims(container_id,proposal_id,lot_id,location_id,quantity_kg,operation_id)
    VALUES(target,handoff.proposal_id,output,destination,qty,handoff.id);
   PERFORM grainledger.ledger_container_set_reservation(a,handoff.proposal_id,output,destination,coalesce(grainledger.ledger_container_outstanding(handoff.proposal_id,output,destination),0)+qty);
   UPDATE grainledger.ledger_container_handoffs SET state='COMMITTED',result=actual,version=version+1 WHERE id=handoff.id;
   -- Loading requires a new actual-output review for the resulting identity/position; no inherited PASS.
  END IF;
 ELSIF action='document' THEN
  IF c.status<>'CLOSED' OR length(btrim(coalesce(p->>'documentType',''))) NOT BETWEEN 1 AND 80 OR length(btrim(coalesce(p->>'reason',''))) NOT BETWEEN 1 AND 1000
   OR length(coalesce(p->>'filename','')) NOT BETWEEN 1 AND 200 OR length(coalesce(p->>'contentType','')) NOT BETWEEN 1 AND 120 THEN
   RAISE EXCEPTION 'Closed container, document metadata and reason required' USING ERRCODE='22023'; END IF;
  SELECT coalesce(max(version),0)+1 INTO decision FROM grainledger.ledger_container_documents WHERE container_id=target AND document_type=btrim(p->>'documentType');
  INSERT INTO grainledger.ledger_container_documents(id,container_id,document_type,version,actor_id,filename,content_type,content,reason,sha256,origin)
   VALUES(operation,target,btrim(p->>'documentType'),decision,a,p->>'filename',p->>'contentType',decode(p->>'content','base64'),btrim(p->>'reason'),p->>'sha256',
    CASE WHEN basis->>'documentOrigin'='GENERATED' THEN 'GENERATED' ELSE 'UPLOADED' END);
 ELSIF action='supplement' THEN
  IF c.status<>'CLOSED' OR length(btrim(coalesce(p->>'reason',''))) NOT BETWEEN 1 AND 1000 THEN RAISE EXCEPTION 'Closed dossier and supplement reason required' USING ERRCODE='22023'; END IF;
  IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_evidence e WHERE e.id=(p->>'evidenceId')::uuid AND e.owner_id=c.company_id AND
   (EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_proposals q JOIN grainledger.exports_blend_plan_previews v ON v.id=q.preview_id WHERE EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_drafts scope_plan WHERE scope_plan.id=q.plan_id AND scope_plan.container_id=target) AND (v.request_json->>'FacilityId')::uuid=e.facility_id))) THEN
   RAISE EXCEPTION 'Supplement evidence outside company/facility' USING ERRCODE='42501'; END IF;
  INSERT INTO grainledger.ledger_container_supplements(id,container_id,actor_id,evidence_id,reason) VALUES(operation,target,a,(p->>'evidenceId')::uuid,btrim(p->>'reason'));
 ELSIF action='notice-read' THEN
  UPDATE grainledger.ledger_container_notices SET read_at=coalesce(read_at,clock_timestamp()) WHERE id=(p->>'noticeId')::uuid AND recipient_id=a AND container_id=target;
  IF NOT FOUND THEN RAISE EXCEPTION 'Notice unavailable for current recipient' USING ERRCODE='42501'; END IF;
 ELSIF action='hold-response' THEN
  hold:=(p->>'holdId')::uuid; facility:=(p->>'facilityId')::uuid;
  PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillManager']);
  PERFORM grainledger.ledger_require_evidence(a,(p->>'evidenceId')::uuid,c.company_id,facility);
  IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_holds WHERE id=hold AND container_id=target)
   OR NOT EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_proposals q JOIN grainledger.exports_blend_plan_previews v ON v.id=q.preview_id WHERE EXISTS(SELECT 1 FROM grainledger.exports_blend_plan_drafts scope_plan WHERE scope_plan.id=q.plan_id AND scope_plan.container_id=target) AND (v.request_json->>'FacilityId')::uuid=facility)
   OR p->>'responseAction' NOT IN ('ACKNOWLEDGE','ISOLATE','RECORD_RESPONSE') OR length(btrim(coalesce(p->>'reason',''))) NOT BETWEEN 1 AND 1000 THEN
   RAISE EXCEPTION 'Scoped HOLD handling and evidence required; release unsupported' USING ERRCODE='23514'; END IF;
  IF p->>'responseAction'='ISOLATE' THEN RAISE EXCEPTION 'Container unloading/isolation movement is outside scope; use lot HOLD isolation for remaining stock' USING ERRCODE='23514'; END IF;
  INSERT INTO grainledger.ledger_container_hold_responses(id,hold_id,actor_id,facility_id,action,evidence_id,reason)
   VALUES(operation,hold,a,facility,p->>'responseAction',(p->>'evidenceId')::uuid,btrim(p->>'reason'));
 ELSE RAISE EXCEPTION 'Unsupported workflow action' USING ERRCODE='22023'; END IF;
 UPDATE grainledger.exports_containers SET version=version+1 WHERE id=target;
 INSERT INTO grainledger.ledger_container_execution_events(container_id,actor_id,kind,details)
  VALUES(target,a,upper(replace(action,'-','_')),jsonb_build_object('operationId',operation,'request',p-'content','basis',basis));
 result:=grainledger.ledger_container_workflow(a,target,'detail');
 INSERT INTO grainledger.ledger_container_workflow_requests VALUES(a,operation,target,action,p,result);
 RETURN result;
END $$;

CREATE OR REPLACE FUNCTION grainledger.ledger_business_history(actor uuid, q jsonb) RETURNS jsonb
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE
 role_name text:=grainledger.ledger_role(actor);
 company uuid;
 category_filter text:=q->>'category';
 event_filter text:=q->>'eventType';
 search_text text:=lower(q->>'search');
 start_at timestamptz:=(q->>'from')::timestamptz;
 end_at timestamptz:=(q->>'before')::timestamptz;
 page_number integer:=coalesce((q->>'page')::integer,1);
 page_size integer:=coalesce((q->>'pageSize')::integer,25);
 result jsonb;
BEGIN
 IF role_name IS NULL OR role_name NOT IN ('Administrator','CustomerManager','ExportStaff','PurchasingStaff','MillManager','MillOperator') THEN
  RAISE EXCEPTION 'Business history authority denied' USING ERRCODE='42501';
 END IF;
 IF page_number<1 OR page_size NOT BETWEEN 1 AND 100 OR start_at>=end_at
  OR length(event_filter)>100 OR length(search_text)>100 OR
  (category_filter IS NOT NULL AND category_filter NOT IN ('PROCUREMENT','LAB','PLAN','RESERVATION','CONTAINER','HOLD','TRACE','LEDGER','ADMINISTRATION')) THEN
  RAISE EXCEPTION 'Invalid business history filters' USING ERRCODE='22023';
 END IF;
 SELECT u."OrganizationId" INTO company FROM grainledger.identity_users u WHERE u."Id"=actor AND u."IsActive";
 WITH events(category,event_type,event_id,subject_id,reference,actor_id,recorded_at,context,evidence_id) AS (
  SELECT 'PROCUREMENT',e.event_type,e.id::text,p.id,p.code,e.actor_id,e.recorded_at,'{}'::jsonb,NULL::uuid
  FROM grainledger.capture_procurements p JOIN grainledger.capture_business_events e ON e.id=p.event_id AND e.company_id=p.company_id
  WHERE e.event_type='PROCUREMENT_COMMITTED' AND
   (role_name IN ('PurchasingStaff','CustomerManager') AND p.company_id=company OR
    role_name IN ('MillOperator','MillManager') AND grainledger.ledger_assigned(actor,p.collection_site_id))
  UNION ALL
  SELECT 'PROCUREMENT','ARRIVAL_MEASURED',m.id::text,p.id,p.code,m.operator_id,m.recorded_at,jsonb_build_object('version',m.expected_version),NULL
  FROM grainledger.ledger_arrival_measurement_sets m JOIN grainledger.capture_procurements p ON p.id=m.procurement_id
  WHERE (role_name IN ('PurchasingStaff','CustomerManager') AND p.company_id=company OR
   role_name IN ('MillOperator','MillManager') AND grainledger.ledger_assigned(actor,p.collection_site_id))
  UNION ALL
  SELECT 'PROCUREMENT','ARRIVAL_'||r.action,r.id::text,p.id,p.code,r.manager_id,r.recorded_at,jsonb_build_object('version',r.expected_version),r.evidence_id
  FROM grainledger.ledger_arrival_reviews r JOIN grainledger.capture_procurements p ON p.id=r.procurement_id
  WHERE (role_name IN ('PurchasingStaff','CustomerManager') AND p.company_id=company OR
   role_name IN ('MillOperator','MillManager') AND grainledger.ledger_assigned(actor,p.collection_site_id))
  UNION ALL
  SELECT 'LAB',e.event_type,e.id::text,r.id,r.report_number,e.actor_id,e.recorded_at,
   jsonb_build_object('reportKind',r.report_kind,'relatedReportId',r.related_report_id),NULL
  FROM grainledger.capture_business_events e JOIN grainledger.capture_lab_reports r ON r.id=CASE WHEN e.event_type LIKE 'LAB_REPORT_%' THEN (e.payload->>'reportId')::uuid END AND r.company_id=e.company_id
  WHERE role_name IN ('CustomerManager','ExportStaff') AND e.company_id=company
   AND e.event_type IN ('LAB_REPORT_RECEIVED','LAB_REPORT_REVISED','LAB_REPORT_CONFIRMED','LAB_REPORT_DELETED')
  UNION ALL
  SELECT 'PLAN',e.action,e.id::text,d.id,c.internal_code,e.actor_id,e.recorded_at,
   jsonb_build_object('version',d.version_no),NULL
  FROM grainledger.exports_blend_plan_events e JOIN grainledger.exports_blend_plan_drafts d ON d.id=e.plan_id
  JOIN grainledger.exports_containers c ON c.id=d.container_id
  WHERE role_name IN ('CustomerManager','ExportStaff') AND e.company_id=company
  UNION ALL
  SELECT 'PLAN','SCOPE_'||r.decision,r.id::text,r.proposal_id,c.internal_code,r.actor_id,r.recorded_at,
   jsonb_build_object('scope',r.scope,'holdId',r.basis->'holdId'),NULL
  FROM grainledger.ledger_container_scope_reviews r JOIN grainledger.exports_containers c ON c.id=r.container_id
  WHERE role_name IN ('CustomerManager','ExportStaff') AND c.company_id=company
  UNION ALL
  SELECT 'PLAN',e.action,e.id::text,o.id,o.code,e.actor_id,e.recorded_at,'{}'::jsonb,NULL
  FROM grainledger.exports_events e JOIN grainledger.exports_export_orders o ON o.id=e.order_id
  WHERE role_name IN ('CustomerManager','ExportStaff') AND e.company_id=company AND e.action NOT LIKE 'BLEND_%'
  UNION ALL
  SELECT 'RESERVATION','RESERVE',res.proposal_id::text||'/'||res.lot_id||'/'||res.location_id,p.id,c.internal_code,d.actor_id,d.recorded_at,jsonb_build_object('quantityKg',res.quantity_kg::text),NULL
  FROM grainledger.ledger_blend_reservations res JOIN grainledger.exports_blend_plan_proposals p ON p.id=res.proposal_id
  JOIN grainledger.exports_blend_plan_drafts plan ON plan.id=p.plan_id JOIN grainledger.exports_containers c ON c.id=plan.container_id
  JOIN grainledger.exports_blend_plan_decisions d ON d.proposal_id=p.id AND d.version=1 AND d.action='APPROVED'
  JOIN grainledger.ledger_locations loc ON loc.id=res.location_id
  WHERE role_name IN ('CustomerManager','ExportStaff') AND c.company_id=company OR
   role_name IN ('MillOperator','MillManager') AND grainledger.ledger_assigned(actor,loc.facility_id)
  UNION ALL
  SELECT 'RESERVATION',e.action,e.id::text,p.id,c.internal_code,e.actor_id,e.recorded_at,
   jsonb_build_object('quantityKg',e.quantity_kg::text),NULL
  FROM grainledger.ledger_blend_reservation_events e JOIN grainledger.exports_blend_plan_proposals p ON p.id=e.proposal_id
  JOIN grainledger.exports_blend_plan_drafts plan ON plan.id=p.plan_id JOIN grainledger.exports_containers c ON c.id=plan.container_id
  JOIN grainledger.ledger_locations loc ON loc.id=e.location_id
  WHERE role_name IN ('CustomerManager','ExportStaff') AND c.company_id=company OR
   role_name IN ('MillOperator','MillManager') AND grainledger.ledger_assigned(actor,loc.facility_id)
  UNION ALL
  SELECT CASE WHEN e.kind IN ('HOLD','HOLD_RESPONSE') THEN 'HOLD' ELSE 'CONTAINER' END,
   CASE WHEN e.kind='HOLD' THEN 'CONTAINER_HOLD' ELSE e.kind END,e.id::text,c.id,c.internal_code,e.actor_id,e.recorded_at,
   jsonb_build_object('version',e.details->'request'->'version','proposalId',e.details->'request'->'proposalId',
    'documentType',e.details->'request'->'documentType'),(e.details->'request'->>'evidenceId')::uuid
  FROM grainledger.ledger_container_execution_events e JOIN grainledger.exports_containers c ON c.id=e.container_id
  LEFT JOIN grainledger.ledger_container_execution x ON x.container_id=c.id
  WHERE (role_name IN ('CustomerManager','ExportStaff') AND c.company_id=company OR
   role_name IN ('MillOperator','MillManager') AND CASE e.kind
    WHEN 'HOLD' THEN grainledger.ledger_assigned(actor,x.facility_id) OR role_name='MillManager' AND EXISTS(
     SELECT 1 FROM grainledger.ledger_container_holds held
     WHERE held.container_id=c.id AND held.id=(e.details->>'operationId')::uuid
      AND EXISTS(
       SELECT 1 FROM grainledger.exports_blend_plan_drafts draft
       JOIN grainledger.exports_blend_plan_proposals proposal ON proposal.plan_id=draft.id
       JOIN grainledger.exports_blend_plan_previews preview ON preview.id=proposal.preview_id
       WHERE draft.container_id=c.id AND draft.company_id=c.company_id AND proposal.proposed_at<=held.recorded_at
        AND grainledger.ledger_assigned(actor,(preview.request_json->>'FacilityId')::uuid)))
    WHEN 'HOLD_RESPONSE' THEN grainledger.ledger_assigned(actor,(e.details->'request'->>'facilityId')::uuid)
    ELSE grainledger.ledger_assigned(actor,x.facility_id)
   END)
   AND e.kind NOT IN ('NOTICE_READ','REVIEW','INVALIDATE')
  UNION ALL
  SELECT CASE WHEN e.kind='TRACEBACK' THEN 'TRACE' ELSE 'HOLD' END,e.kind,e.id::text,l.id,l.code,e.actor_id,e.occurred_at,
   jsonb_build_object('direction',e.details->'direction','holdId',CASE WHEN e.kind IN ('HOLD','HOLD_RESPONSE') THEN e.details->'id' END),
   CASE WHEN e.kind IN ('HOLD','HOLD_RESPONSE') THEN (e.details->>'evidenceId')::uuid END
  FROM grainledger.ledger_events e JOIN grainledger.ledger_lots l ON l.id=e.lot_id
  WHERE e.kind IN ('HOLD','HOLD_RESPONSE','TRACEBACK') AND
   (role_name IN ('CustomerManager','ExportStaff','MillOperator','MillManager') AND grainledger.ledger_can_read_lot(actor,l.id)
    OR role_name='Administrator' AND e.kind='TRACEBACK')
  UNION ALL
  SELECT 'TRACE','TRACEBACK',e.id::text,c.id,c.internal_code,e.actor_id,e.occurred_at,
   jsonb_build_object('direction',e.details->'direction'),NULL
  FROM grainledger.ledger_events e JOIN grainledger.exports_containers c ON c.id=CASE WHEN e.kind='TRACEBACK' THEN (e.details->>'containerId')::uuid END
  WHERE e.kind='TRACEBACK' AND (role_name='Administrator' OR role_name IN ('CustomerManager','ExportStaff') AND c.company_id=company)
  UNION ALL
  SELECT 'LEDGER',e.kind,e.id::text,op.id,op.kind,e.actor_id,e.occurred_at,
   jsonb_build_object('correctionId',CASE WHEN e.kind LIKE 'CORRECTION_%' THEN e.details->'id' END,
    'reversesPostingId',e.details->'reversesPostingId'),
   CASE WHEN e.kind LIKE 'CORRECTION_%' THEN (e.details->>'evidenceId')::uuid END
  FROM grainledger.ledger_events e JOIN grainledger.ledger_operations op ON op.id=e.operation_id
  WHERE (e.kind LIKE 'CORRECTION_%' OR e.kind IN ('DRYING','MILLING','MERGE','BLENDING','BAGGING','REVERSAL'))
   AND grainledger.ledger_can_read_operation(actor,op.id)
  UNION ALL
  SELECT 'ADMINISTRATION',e."Action",e."Id"::text,e."SubjectUserId",e."SubjectUserId"::text,e."ActorUserId",e."OccurredAt",'{}'::jsonb,NULL
  FROM grainledger.identity_audit_events e WHERE role_name='Administrator'
  UNION ALL
  SELECT 'ADMINISTRATION',e.event_type,e.id::text,e.resource_id,e.resource_type,e.actor_id,e.occurred_at,'{}'::jsonb,NULL
  FROM grainledger.catalog_catalog_events e WHERE role_name='Administrator'
 ), filtered AS MATERIALIZED (
  SELECT * FROM events e
  WHERE (category_filter IS NULL OR e.category=category_filter)
   AND (event_filter IS NULL OR upper(e.event_type)=upper(event_filter))
   AND (start_at IS NULL OR e.recorded_at>=start_at) AND (end_at IS NULL OR e.recorded_at<end_at)
   AND (search_text IS NULL OR strpos(lower(concat_ws(' ',e.event_type,e.event_id,e.subject_id,e.reference,e.context)),search_text)>0)
 ), selected AS (
  SELECT * FROM filtered ORDER BY recorded_at DESC,category,event_id
  LIMIT page_size OFFSET (page_number::bigint-1)*page_size
 )
 SELECT jsonb_build_object('items',coalesce((SELECT jsonb_agg(jsonb_build_object(
  'id',event_id,'category',category,'eventType',event_type,'subjectId',subject_id,'reference',reference,
  'actorId',selected.actor_id,'actorName',u."DisplayName",'recordedAt',recorded_at,'context',jsonb_strip_nulls(context),
  'evidenceId',CASE WHEN role_name='CustomerManager' AND evidence.owner_id=company OR
     role_name='MillManager' AND grainledger.ledger_assigned(actor,evidence.facility_id) THEN evidence.id END)
  ORDER BY recorded_at DESC,category,event_id) FROM selected
  LEFT JOIN grainledger.identity_users u ON u."Id"=selected.actor_id
  LEFT JOIN grainledger.ledger_evidence evidence ON evidence.id=selected.evidence_id),'[]'::jsonb),
  'page',page_number,'pageSize',page_size,'totalCount',(SELECT count(*) FROM filtered),
  'hasMore',(SELECT count(*) FROM filtered)>page_number::bigint*page_size) INTO result;
 RETURN result;
END $$;

CREATE OR REPLACE FUNCTION grainledger.ledger_container_targets(a uuid,root uuid,lot_ids uuid[]) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE r text:=grainledger.ledger_role(a); result jsonb; source_root boolean; owner uuid;
BEGIN
 IF r IS NULL OR r NOT IN ('CustomerManager','ExportStaff','Administrator') OR lot_ids IS NULL OR root IS NULL OR array_position(lot_ids,NULL) IS NOT NULL OR cardinality(lot_ids) NOT BETWEEN 1 AND 10000
  OR NOT root=ANY(lot_ids) OR EXISTS(SELECT 1 FROM unnest(lot_ids) x WHERE r<>'Administrator' AND NOT grainledger.ledger_can_read_lot(a,x)) THEN
  RAISE EXCEPTION 'Actual container targets unavailable in current trace scope' USING ERRCODE='42501'; END IF;
 SELECT owner_id INTO owner FROM grainledger.ledger_lots WHERE id=root;
 SELECT EXISTS(SELECT 1 FROM grainledger.ledger_contributions WHERE lot_id=root AND source_lot_id=root AND quantity_kg>0) INTO source_root;
 WITH linked AS MATERIALIZED (
  SELECT c.id,c.internal_code,c.status,c.version,l.quantity_kg,l.id line_id,l.lot_id,l.location_id,h.id load_id,h.proposal_id,h.loaded_at,
   CASE WHEN source_root AND r IN ('CustomerManager','Administrator') THEN
    (SELECT s.quantity_kg FROM grainledger.ledger_container_load_sources s WHERE s.line_id=l.id AND s.source_lot_id=root)
   END source_kg
  FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id
  JOIN grainledger.exports_containers c ON c.id=h.container_id
  WHERE l.lot_id=ANY(lot_ids) AND c.company_id=owner
   AND (NOT source_root OR EXISTS(SELECT 1 FROM grainledger.ledger_container_load_sources s WHERE s.line_id=l.id AND s.source_lot_id=root AND s.quantity_kg>0))
 ), totals AS (
  SELECT id,internal_code,status,version,sum(quantity_kg) linked_kg,sum(source_kg) source_kg FROM linked GROUP BY id,internal_code,status,version
 ), all_manifest_lots AS MATERIALIZED (
  SELECT DISTINCT h.container_id,l.lot_id FROM grainledger.ledger_container_loads h
  JOIN totals t ON t.id=h.container_id JOIN grainledger.ledger_container_load_lines l ON l.load_id=h.id
 ), affected_plans AS MATERIALIZED (
  SELECT p.id proposal_id,d.id plan_id,d.version_no,c.id container_id,c.internal_code,c.version container_version,p.proposed_at,coalesce(review.decision,decision.action) action,
   active.proposal_id IS NOT NULL active_approval,
   EXISTS(SELECT 1 FROM grainledger.ledger_container_holds held WHERE held.container_id=c.id) held
  FROM grainledger.exports_blend_plan_proposals p
  JOIN grainledger.exports_blend_plan_drafts d ON d.id=p.plan_id
  JOIN grainledger.exports_containers c ON c.id=d.container_id AND c.company_id=p.company_id
  LEFT JOIN grainledger.exports_active_blend_approvals active ON active.proposal_id=p.id AND active.company_id=p.company_id
  LEFT JOIN LATERAL (
   SELECT b.action FROM grainledger.exports_blend_plan_decisions b WHERE b.proposal_id=p.id ORDER BY b.version DESC LIMIT 1
  ) decision ON true
  LEFT JOIN LATERAL (
   SELECT v.decision FROM grainledger.ledger_container_scope_reviews v WHERE v.container_id=c.id AND v.proposal_id=p.id
   ORDER BY v.recorded_at DESC,v.id DESC LIMIT 1
  ) review ON true
  WHERE c.company_id=owner
   AND EXISTS (
    SELECT 1 FROM (
     SELECT (allocation->>'lotId')::uuid lot_id
     FROM jsonb_array_elements(p.snapshot_json->'selection'->'allocations') allocation
     UNION
     SELECT reservation.lot_id FROM grainledger.ledger_blend_reservations reservation WHERE reservation.proposal_id=p.id
    ) planned
    WHERE planned.lot_id=ANY(lot_ids)
     AND (NOT source_root OR EXISTS(SELECT 1 FROM grainledger.ledger_contributions s
      WHERE s.lot_id=planned.lot_id AND s.source_lot_id=root AND s.quantity_kg>0))
   )
  LIMIT 10001
 ), restrictions AS MATERIALIZED (
  SELECT lot_id,grainledger.ledger_container_lot_blocked(lot_id) blocked FROM (SELECT DISTINCT lot_id FROM all_manifest_lots) lots
 ) SELECT jsonb_build_object('complete',(SELECT count(*) FROM totals)<=10000 AND (SELECT count(*) FROM linked)<=20000
  AND (SELECT count(*) FROM affected_plans)<=10000,
  'plans',coalesce((SELECT jsonb_agg(jsonb_build_object('proposalId',proposal_id,'planId',plan_id,'version',version_no,
   'containerId',container_id,'containerCode',internal_code,'containerVersion',container_version,'status',coalesce(action,'PENDING'),
   'activeApproval',active_approval,'held',held,'basis','SAVED_PLANNING') ORDER BY proposed_at DESC,proposal_id) FROM affected_plans),'[]'::jsonb),
  'loadingEdges',coalesce((SELECT jsonb_agg(edge ORDER BY container_id,lot_id) FROM (
   SELECT id container_id,lot_id,jsonb_build_object('containerId',id,'lotId',lot_id,
    'quantityKg',sum(quantity_kg)::numeric(20,6)::text,'basis','ACTUAL_LOADING',
    'lines',jsonb_agg(jsonb_build_object('lineId',line_id,'loadId',load_id,'locationId',location_id,
     'proposalId',proposal_id,'loadedAt',loaded_at,'quantityKg',quantity_kg::numeric(20,6)::text) ORDER BY loaded_at,load_id,line_id)) edge
   FROM linked GROUP BY id,lot_id) grouped),'[]'::jsonb), 'items',
  CASE WHEN (SELECT count(*) FROM totals)>10000 THEN '[]'::jsonb ELSE coalesce((SELECT jsonb_agg(jsonb_build_object('containerId',id,'code',internal_code,'status',status,'version',version,
    'linkedLoadedKg',linked_kg::numeric(20,6)::text,'sourceLoadedKg',source_kg::numeric(20,6)::text,'basis','ACTUAL_LOADING',
    'held',EXISTS(SELECT 1 FROM grainledger.ledger_container_holds h WHERE h.container_id=totals.id),
    'restricted',EXISTS(SELECT 1 FROM all_manifest_lots l JOIN restrictions r ON r.lot_id=l.lot_id WHERE l.container_id=totals.id AND r.blocked)) ORDER BY internal_code,id) FROM totals),'[]'::jsonb) END) INTO result;
 IF NOT (result->>'complete')::boolean THEN RAISE EXCEPTION 'Actual container target set exceeds complete result limit' USING ERRCODE='22023'; END IF;
 RETURN result;
END $$;

CREATE OR REPLACE FUNCTION grainledger.ledger_reserve_approved_blend(a uuid, proposal uuid) RETURNS integer
LANGUAGE plpgsql SECURITY DEFINER SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
AS $$
DECLARE approved record; allocation record; available numeric; allocation_count integer := 0;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    SELECT p.company_id, (v.request_json->>'FacilityId')::uuid AS facility_id,
           p.snapshot_json->'selection'->'allocations' AS allocations
    INTO approved
    FROM grainledger.exports_blend_plan_proposals p
    JOIN grainledger.exports_blend_plan_previews v ON v.id = p.preview_id AND v.company_id = p.company_id
    JOIN grainledger.exports_active_blend_approvals active ON active.proposal_id = p.id AND active.company_id = p.company_id
    JOIN grainledger.exports_blend_plan_decisions d ON d.proposal_id = p.id AND d.version = 1
    WHERE p.id = proposal AND d.action = 'APPROVED' AND d.actor_id = a;
    IF NOT FOUND OR grainledger.ledger_role(a) IS DISTINCT FROM 'CustomerManager'
       OR NOT COALESCE(grainledger.ledger_own(a,approved.company_id),false) THEN
        RAISE EXCEPTION 'Blend reservation access denied' USING ERRCODE='42501';
    END IF;
    IF approved.facility_id IS NULL OR jsonb_typeof(approved.allocations) IS DISTINCT FROM 'array' THEN
        RAISE EXCEPTION 'Missing approved stock allocations' USING ERRCODE='23514';
    END IF;
    FOR allocation IN
        SELECT x."lotId" AS lot_id, x."locationId" AS location_id, x."quantityKg" AS quantity_kg
        FROM jsonb_to_recordset(approved.allocations) AS x("lotId" uuid, "locationId" uuid, "quantityKg" numeric)
        ORDER BY 1, 2
    LOOP
        IF allocation.quantity_kg IS NULL OR allocation.quantity_kg <= 0
            OR allocation.quantity_kg > 99999999999999.999999
            OR trunc(allocation.quantity_kg,6) <> allocation.quantity_kg THEN
            RAISE EXCEPTION 'Invalid approved stock quantity' USING ERRCODE='23514';
        END IF;
        SELECT pos.quantity_kg - grainledger.ledger_blend_reserved_kg(pos.lot_id,pos.location_id)
        INTO available
        FROM grainledger.ledger_positions pos
        JOIN grainledger.ledger_lots lot ON lot.id = pos.lot_id
        JOIN grainledger.ledger_locations loc ON loc.id = pos.location_id
        LEFT JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.location_id = loc.id
        WHERE pos.lot_id = allocation.lot_id AND pos.location_id = allocation.location_id
            AND lot.owner_id = approved.company_id AND loc.facility_id = approved.facility_id AND NOT loc.is_deleted
            AND (loc.owner_id IS NULL OR loc.owner_id = approved.company_id)
            AND (NOT loc.mixed_bulk OR pool.lot_id = pos.lot_id)
            AND NOT grainledger.ledger_container_lot_blocked(pos.lot_id)
        FOR UPDATE OF pos;
        IF NOT FOUND OR available < allocation.quantity_kg THEN
            RAISE EXCEPTION 'Approved stock position is no longer available' USING ERRCODE='23514';
        END IF;
        INSERT INTO grainledger.ledger_blend_reservations(proposal_id,lot_id,location_id,quantity_kg)
        VALUES (proposal, allocation.lot_id, allocation.location_id, allocation.quantity_kg);
        allocation_count := allocation_count + 1;
    END LOOP;
    IF allocation_count = 0 THEN
        RAISE EXCEPTION 'Missing approved stock allocations' USING ERRCODE='23514';
    END IF;
    RETURN allocation_count;
END;
$$;

CREATE OR REPLACE FUNCTION grainledger.ledger_planning_candidates(a uuid, o uuid, f uuid) RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE result jsonb; position_count bigint; node_count bigint; edge_count bigint;
BEGIN
    IF grainledger.ledger_role(a) NOT IN ('ExportStaff','CustomerManager') OR NOT grainledger.ledger_own(a,o) THEN
        RAISE EXCEPTION 'Candidate access denied' USING ERRCODE='42501';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_facilities WHERE id=f) THEN
        RAISE EXCEPTION 'Unknown facility' USING ERRCODE='22023';
    END IF;
    SELECT count(*) INTO position_count
    FROM grainledger.ledger_positions p JOIN grainledger.ledger_locations loc ON loc.id=p.location_id
    JOIN grainledger.ledger_lots l ON l.id=p.lot_id
    WHERE l.owner_id=o AND loc.facility_id=f AND p.quantity_kg>0;
    IF position_count>10000 THEN
        RAISE EXCEPTION 'Candidate position scope too large' USING ERRCODE='GL014';
    END IF;

    WITH RECURSIVE
    stock AS MATERIALIZED (
        SELECT p.lot_id,p.location_id,p.quantity_kg,loc.name location_name,
            loc.kind location_kind,loc.cleared,loc.mixed_bulk,loc.owner_id location_owner_id,
            pool.lot_id current_pool_lot_id,pool.revision pool_revision
        FROM grainledger.ledger_positions p JOIN grainledger.ledger_locations loc ON loc.id=p.location_id
        JOIN grainledger.ledger_lots lot ON lot.id=p.lot_id
        LEFT JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.location_id=loc.id
        WHERE lot.owner_id=o AND loc.facility_id=f AND p.quantity_kg>0
    ),
    eligibility AS MATERIALIZED (
        SELECT lot_id, EXISTS(SELECT 1 FROM grainledger.ledger_ancestral_hold_ids(lot_id)) inherited_hold
        FROM (SELECT DISTINCT lot_id FROM stock) scoped
    ),
    graph_edges AS MATERIALIZED (
        SELECT 'CUSTODY_'||ce.action kind,ca.event_id,NULL::uuid operation_id,
            ca.input_lot_id,ca.output_lot_id,ca.source_lot_id,ca.quantity_kg attributed_kg,
            ce.created_at occurred_at
        FROM grainledger.ledger_custody_allocations ca JOIN grainledger.ledger_custody_events ce ON ce.event_id=ca.event_id
        JOIN grainledger.ledger_lots outlot ON outlot.id=ca.output_lot_id AND outlot.owner_id=o
        WHERE ca.quantity_kg>0
        UNION ALL
        SELECT 'PROCESSING',cpa.event_id,cpa.operation_id,cpa.input_lot_id,cpa.output_lot_id,
            cpa.source_lot_id,cpa.quantity_kg,op.occurred_at
        FROM grainledger.ledger_custody_processing_allocations cpa
        JOIN grainledger.ledger_operations op ON op.id=cpa.operation_id AND op.owner_id=o
        WHERE cpa.quantity_kg>0
        UNION ALL
        -- Pre-mixed-bulk operations have input/output links, but no exact
        -- input-to-source allocation. Expose them as unproven, never fabricate it.
        SELECT 'PROCESSING_UNATTRIBUTED',ev.id,op.id,(input.value->>'lotId')::uuid,
            output_lot.id,NULL::uuid,NULL::numeric,op.occurred_at
        FROM grainledger.ledger_lots output_lot JOIN grainledger.ledger_operations op ON op.id=output_lot.operation_id
        JOIN LATERAL jsonb_array_elements(op.content->'inputs') input(value) ON true
        LEFT JOIN LATERAL (SELECT id FROM grainledger.ledger_events WHERE operation_id=op.id ORDER BY occurred_at,id LIMIT 1) ev ON true
        WHERE output_lot.owner_id=o AND op.status IN ('COMMITTED','CORRECTED')
          AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_custody_processing_allocations cpa WHERE cpa.output_lot_id=output_lot.id)
    ),
    ancestors(id) AS (
        SELECT DISTINCT lot_id FROM stock
        UNION
        SELECT edge.input_lot_id FROM graph_edges edge JOIN ancestors n ON n.id=edge.output_lot_id
    ),
    scoped_edges AS MATERIALIZED (
        SELECT edge.* FROM graph_edges edge JOIN ancestors n ON n.id=edge.output_lot_id
    ),
    scoped_nodes AS MATERIALIZED (
        SELECT lot.id,lot.code,lot.stage,lot.product_identity,lot.operation_id,
            source.id capture_source_lot_id
        FROM ancestors n JOIN grainledger.ledger_lots lot ON lot.id=n.id AND lot.owner_id=o
        LEFT JOIN grainledger.capture_source_lots source ON source.lot_id=lot.id AND source.company_id=o
    ),
    scope_counts AS (
        SELECT (SELECT count(*) FROM scoped_nodes) nodes,(SELECT count(*) FROM scoped_edges) edges
    )
    SELECT jsonb_build_object(
        'positionCount',position_count,
        'nodeCount',(SELECT nodes FROM scope_counts),
        'edgeCount',(SELECT edges FROM scope_counts),
        'positions',COALESCE((SELECT jsonb_agg(
            (CASE WHEN eligibility.inherited_hold THEN snapshot.data || jsonb_build_object(
                'restrictions',CASE WHEN snapshot.data->'restrictions' ? 'HOLD' THEN snapshot.data->'restrictions'
                    ELSE (snapshot.data->'restrictions') || '["HOLD"]'::jsonb END,
                'positions',(SELECT jsonb_agg(position.value || '{"availableKg":"0.000000"}'::jsonb ORDER BY ordinal)
                    FROM jsonb_array_elements(snapshot.data->'positions') WITH ORDINALITY position(value,ordinal)))
             ELSE snapshot.data END) || jsonb_build_object(
                'locationId',s.location_id,'locationName',s.location_name,
                'locationKind',s.location_kind,'locationCleared',s.cleared,
                'locationMixedBulk',s.mixed_bulk,'locationOwnerId',s.location_owner_id,
                'onHandKg',s.quantity_kg::text,
                'isCurrentPoolAtLocation',COALESCE(s.current_pool_lot_id=s.lot_id,false),
                'positionPoolRevision',CASE WHEN s.current_pool_lot_id=s.lot_id THEN s.pool_revision::text END,
                'catalogCommodityId',mapping.catalog_commodity_id)
            ORDER BY s.lot_id,s.location_id)
            FROM stock s JOIN grainledger.ledger_lots lot ON lot.id=s.lot_id
            JOIN eligibility ON eligibility.lot_id=s.lot_id
            CROSS JOIN LATERAL (SELECT grainledger.ledger_lot_json(a,s.lot_id,false) data) snapshot
            LEFT JOIN grainledger.ledger_catalog_commodities mapping ON mapping.ledger_commodity_id=lot.commodity_id),'[]'::jsonb),
        'nodes',COALESCE((SELECT jsonb_agg(jsonb_build_object(
            'lotId',n.id,'lotCode',n.code,'stage',n.stage,'productIdentity',n.product_identity,
            'operationId',n.operation_id,'captureSourceLotId',n.capture_source_lot_id)
            ORDER BY n.id) FROM scoped_nodes n),'[]'::jsonb),
        'edges',COALESCE((SELECT jsonb_agg(jsonb_build_object(
            'kind',e.kind,'eventId',e.event_id,'operationId',e.operation_id,
            'inputLotId',e.input_lot_id,'outputLotId',e.output_lot_id,
            'sourceLotId',e.source_lot_id,'attributedQuantityKg',e.attributed_kg::text,
            'occurredAt',e.occurred_at) ORDER BY e.output_lot_id,e.input_lot_id,e.source_lot_id,e.event_id)
            FROM scoped_edges e),'[]'::jsonb),
        'composition',COALESCE((SELECT jsonb_agg(jsonb_build_object(
            'lotId',c.lot_id,'ledgerSourceLotId',c.source_lot_id,
            'captureSourceLotId',source.id,'lotAttributedKg',c.quantity_kg::text)
            ORDER BY c.lot_id,c.source_lot_id)
            FROM grainledger.ledger_contributions c JOIN (SELECT DISTINCT lot_id FROM stock) s ON s.lot_id=c.lot_id
            LEFT JOIN grainledger.capture_source_lots source ON source.lot_id=c.source_lot_id AND source.company_id=o
            WHERE c.quantity_kg>0),'[]'::jsonb),
        'operations',COALESCE((SELECT jsonb_agg(jsonb_build_object(
            'id',op.id,'kind',op.kind,'status',op.status,'occurredAt',op.occurred_at,
            'content',op.content,'varianceKg',op.variance_kg::text,
            'allowedVarianceKg',op.allowed_variance_kg::text)
            ORDER BY op.id)
            FROM grainledger.ledger_operations op WHERE op.id IN
                (SELECT DISTINCT operation_id FROM scoped_nodes WHERE operation_id IS NOT NULL)),'[]'::jsonb)
    ) INTO result;

    node_count:=(result->>'nodeCount')::bigint;
    edge_count:=(result->>'edgeCount')::bigint;
    IF node_count>50000 OR edge_count>100000 THEN
        RAISE EXCEPTION 'Candidate ancestry scope too large' USING ERRCODE='GL014';
    END IF;
    RETURN result;
END $$;
