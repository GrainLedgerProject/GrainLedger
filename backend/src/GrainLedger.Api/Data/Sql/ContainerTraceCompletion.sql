ALTER TABLE grainledger.ledger_container_holds ADD COLUMN evidence_id uuid REFERENCES grainledger.ledger_evidence(id);

-- Retain history while ending the unused authority associated with an affected plan.
CREATE FUNCTION grainledger.ledger_halt_container_approval(actor uuid, target uuid, hold_reason text, hold_id uuid) RETURNS boolean
LANGUAGE plpgsql SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE proposal uuid; reservation record;
BEGIN
 SELECT proposal_id INTO proposal FROM grainledger.exports_active_blend_approvals WHERE container_id=target;
 IF NOT FOUND THEN RETURN false; END IF;
 INSERT INTO grainledger.ledger_container_scope_reviews(id,container_id,proposal_id,actor_id,scope,decision,reason,basis)
 VALUES(gen_random_uuid(),target,proposal,actor,'ALL_REMAINING','STALE',hold_reason,jsonb_build_object('holdId',hold_id));
 FOR reservation IN
  SELECT lot_id,location_id FROM grainledger.ledger_blend_reservations WHERE proposal_id=proposal ORDER BY lot_id,location_id
 LOOP
  PERFORM grainledger.ledger_container_set_reservation(actor,proposal,reservation.lot_id,reservation.location_id,0);
 END LOOP;
 DELETE FROM grainledger.exports_active_blend_approvals WHERE container_id=target;
 RETURN true;
END $$;

-- Test the specific new HOLD against the same positive-source ancestry used by execution.
CREATE FUNCTION grainledger.ledger_container_hold_affects(target uuid, hold_id uuid) RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
 WITH RECURSIVE paths(lot_id,source_lot_id) AS (
  SELECT target,source_lot_id FROM grainledger.ledger_contributions WHERE lot_id=target AND quantity_kg>0
  UNION
  SELECT parent.lot_id,path.source_lot_id
  FROM paths path JOIN grainledger.ledger_lots child ON child.id=path.lot_id
  CROSS JOIN LATERAL (
   SELECT edge.input_lot_id lot_id FROM grainledger.ledger_custody_edges edge WHERE edge.output_lot_id=child.id AND edge.quantity_kg>0
   UNION
   SELECT (input->>'lotId')::uuid FROM grainledger.ledger_events event
   CROSS JOIN LATERAL jsonb_array_elements(coalesce(event.details->'resolvedInputs',event.details->'content'->'inputs','[]'::jsonb)) input
   WHERE event.operation_id=child.operation_id AND event.kind IN ('DRYING','MILLING','MERGE','BLENDING','BAGGING') AND (input->>'quantityKg')::numeric>0
  ) parent JOIN grainledger.ledger_contributions c ON c.lot_id=parent.lot_id AND c.source_lot_id=path.source_lot_id AND c.quantity_kg>0
 ), related(lot_id) AS (
  SELECT target UNION SELECT lot_id FROM paths UNION SELECT source_lot_id FROM paths
 )
 SELECT EXISTS (
  SELECT 1 FROM related l CROSS JOIN LATERAL grainledger.ledger_effective_hold_ids(l.lot_id) h WHERE h=hold_id
 )
$$;

CREATE FUNCTION grainledger.ledger_finish_lot_hold() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE
 target_hold uuid := (NEW.details->>'id')::uuid;
 evidence uuid := (NEW.details->>'evidenceId')::uuid;
 owner uuid;
 facility uuid;
 shipment record;
BEGIN
 IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_holds h WHERE h.id=target_hold AND h.lot_id=NEW.lot_id) THEN
  RAISE EXCEPTION 'Recorded lot HOLD required' USING ERRCODE='23514';
 END IF;
 SELECT owner_id INTO owner FROM grainledger.ledger_lots WHERE id=NEW.lot_id;
 IF evidence IS NOT NULL THEN
  SELECT e.facility_id INTO facility FROM grainledger.ledger_evidence e
  WHERE e.id=evidence AND e.owner_id=owner AND EXISTS(
   SELECT 1 FROM grainledger.ledger_positions p JOIN grainledger.ledger_locations l ON l.id=p.location_id
   WHERE p.lot_id=NEW.lot_id AND l.facility_id=e.facility_id);
  PERFORM grainledger.ledger_require_evidence(NEW.actor_id,evidence,owner,facility);
 END IF;

 INSERT INTO grainledger.ledger_notifications(user_id,hold_id,lot_id,message)
 SELECT u."Id",target_hold,NEW.lot_id,NEW.details->>'reason'
 FROM grainledger.identity_users u WHERE u."IsActive" AND u."Role"='ExportStaff' AND u."OrganizationId"=owner;

 -- The original lot notifier covers direct stock. A consumed source can still
 -- affect a different lot that a mill is reserving or has already loaded.
 WITH shipment_positions AS (
  SELECT res.lot_id,res.location_id
  FROM grainledger.exports_active_blend_approvals active
  JOIN grainledger.ledger_blend_reservations res ON res.proposal_id=active.proposal_id
  WHERE active.company_id=owner AND grainledger.ledger_container_outstanding(res.proposal_id,res.lot_id,res.location_id)>0
  UNION
  SELECT line.lot_id,line.location_id
  FROM grainledger.exports_containers c
  JOIN grainledger.ledger_container_loads load ON load.container_id=c.id
  JOIN grainledger.ledger_container_load_lines line ON line.load_id=load.id
  WHERE c.company_id=owner
 ), affected AS MATERIALIZED (
  SELECT DISTINCT pos.lot_id,loc.facility_id
  FROM shipment_positions pos JOIN grainledger.ledger_locations loc ON loc.id=pos.location_id
  WHERE grainledger.ledger_container_hold_affects(pos.lot_id,target_hold)
   AND (pos.lot_id<>NEW.lot_id OR NOT EXISTS(
    SELECT 1 FROM grainledger.ledger_positions current_pos
    JOIN grainledger.ledger_locations current_loc ON current_loc.id=current_pos.location_id
    WHERE current_pos.lot_id=NEW.lot_id AND current_pos.quantity_kg>0 AND current_loc.facility_id=loc.facility_id))
 )
 INSERT INTO grainledger.ledger_notifications(user_id,hold_id,lot_id,message)
 SELECT DISTINCT u."Id",target_hold,pos.lot_id,NEW.details->>'reason'
 FROM affected pos JOIN grainledger.identity_users u ON u."IsActive" AND u."Role"='MillManager'
 WHERE grainledger.ledger_assigned(u."Id",pos.facility_id);

 FOR shipment IN
  SELECT active.container_id
  FROM grainledger.exports_active_blend_approvals active
  JOIN grainledger.exports_containers c ON c.id=active.container_id
  WHERE active.company_id=owner AND c.status<>'CLOSED'
   AND (
    EXISTS(SELECT 1 FROM grainledger.ledger_blend_reservations res WHERE res.proposal_id=active.proposal_id
      AND grainledger.ledger_container_outstanding(res.proposal_id,res.lot_id,res.location_id)>0
      AND grainledger.ledger_container_hold_affects(res.lot_id,target_hold))
    OR EXISTS(SELECT 1 FROM grainledger.ledger_container_loads load
      JOIN grainledger.ledger_container_load_lines line ON line.load_id=load.id
      WHERE load.container_id=c.id AND grainledger.ledger_container_hold_affects(line.lot_id,target_hold))
   )
  ORDER BY active.container_id
 LOOP
  IF grainledger.ledger_halt_container_approval(NEW.actor_id,shipment.container_id,'Lot HOLD blocks the remaining scope',target_hold) THEN
   UPDATE grainledger.exports_containers SET version=version+1 WHERE id=shipment.container_id;
  END IF;
 END LOOP;
 RETURN NEW;
END $$;
CREATE TRIGGER ledger_finish_lot_hold AFTER INSERT ON grainledger.ledger_events
FOR EACH ROW WHEN (NEW.kind='HOLD') EXECUTE FUNCTION grainledger.ledger_finish_lot_hold();

REVOKE ALL ON FUNCTION grainledger.ledger_halt_container_approval(uuid,uuid,text,uuid), grainledger.ledger_container_hold_affects(uuid,uuid),
 grainledger.ledger_finish_lot_hold() FROM PUBLIC;

-- Validate new actual loading against the recorded creation time without changing historical loads.
CREATE FUNCTION grainledger.ledger_validate_loaded_at() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,grainledger,pg_temp AS $$
BEGIN
 IF EXISTS (
  SELECT 1 FROM grainledger.ledger_container_loads load
  JOIN grainledger.ledger_lots lot ON lot.id=NEW.lot_id
  WHERE load.id=NEW.load_id AND (load.loaded_at<lot.created_at OR load.loaded_at>clock_timestamp())
 ) THEN
  RAISE EXCEPTION 'Actual loading must be recorded between lot creation and the current time' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION grainledger.ledger_validate_loaded_at() FROM PUBLIC;
CREATE TRIGGER ledger_validate_loaded_at BEFORE INSERT ON grainledger.ledger_container_load_lines
FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_validate_loaded_at();

CREATE OR REPLACE FUNCTION grainledger.ledger_export_container_read(a uuid,target uuid,action text DEFAULT 'detail',page_number integer DEFAULT 1,page_size integer DEFAULT 25) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
#variable_conflict use_variable
DECLARE c grainledger.exports_containers; ext grainledger.ledger_container_execution; r text; result jsonb; scope_review grainledger.ledger_container_scope_reviews; closure_scopes jsonb; audit_id uuid;
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
   FROM (SELECT id,kind,actor_id,CASE WHEN r='CustomerManager' THEN details ELSE details-'basis' END details,recorded_at FROM grainledger.ledger_container_execution_events WHERE container_id=target
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
  'holds',coalesce((SELECT jsonb_agg(jsonb_build_object('id',id,'reason',reason,'evidenceId',CASE WHEN r IN ('CustomerManager','Administrator') THEN evidence_id END,'recordedAt',recorded_at) ORDER BY recorded_at,id)
   FROM grainledger.ledger_container_holds WHERE container_id=target),'[]'::jsonb),
  'loads',coalesce((SELECT jsonb_agg(jsonb_build_object('id',h.id,'proposalId',h.proposal_id,'loadedAt',h.loaded_at,'recordedAt',h.recorded_at,
   'lines',(SELECT jsonb_agg(jsonb_build_object('lotId',l.lot_id,'lotCode',x.code,'locationId',l.location_id,'quantityKg',l.quantity_kg::text,'restricted',grainledger.ledger_container_lot_blocked(l.lot_id))
    ORDER BY l.lot_id,l.location_id) FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_lots x ON x.id=l.lot_id WHERE l.load_id=h.id))
   ORDER BY h.recorded_at,h.id) FROM grainledger.ledger_container_loads h WHERE h.container_id=target),'[]'::jsonb),
  'approval',(SELECT jsonb_build_object('proposalId',p.id,'planId',p.plan_id,'decisionVersion',d.version,
     'snapshot',p.snapshot_json,'previewRequest',v.request_json,'demandSnapshot',plan.demand_snapshot::text,
     'productionState',(SELECT t.state FROM grainledger.ledger_container_production t WHERE t.container_id=target AND t.proposal_id=p.id),
     'productionOperationId',(SELECT t.operation_id FROM grainledger.ledger_container_production t WHERE t.container_id=target AND t.proposal_id=p.id),
     'outputReviewId',(SELECT t.review_id FROM grainledger.ledger_container_production t WHERE t.container_id=target AND t.proposal_id=p.id AND t.state='APPROVED'),
     'outputSnapshot',(SELECT rev.snapshot FROM grainledger.ledger_container_production t JOIN grainledger.ledger_container_output_reviews rev ON rev.id=t.review_id WHERE t.container_id=target AND t.proposal_id=p.id AND t.state='APPROVED'),
     'productionOperations',(SELECT coalesce(jsonb_agg(b.operation_id ORDER BY b.recorded_at,b.operation_id),'[]'::jsonb) FROM grainledger.ledger_container_batches b WHERE b.proposal_id=p.id),
     'specificationId',plan.specification_id,'mode',p.snapshot_json->'selection'->>'mode',
     'allocations',coalesce((SELECT jsonb_agg(jsonb_build_object('lotId',res.lot_id,'lotCode',lot.code,'locationId',res.location_id,
       'restricted',grainledger.ledger_container_lot_blocked(res.lot_id),
       'loadingPositionReady',EXISTS(SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_locations loc ON loc.id=pos.location_id JOIN grainledger.ledger_facilities f ON f.id=loc.facility_id
         WHERE pos.lot_id=res.lot_id AND pos.location_id=res.location_id AND pos.quantity_kg>0 AND NOT loc.is_deleted AND NOT f.is_deleted
          AND NOT loc.mixed_bulk
          AND (loc.owner_id IS NULL OR loc.owner_id=c.company_id)
          AND (EXISTS(SELECT 1 FROM grainledger.ledger_container_source_balances WHERE lot_id=pos.lot_id AND location_id=pos.location_id) OR (SELECT count(*) FROM grainledger.ledger_contributions WHERE lot_id=pos.lot_id AND quantity_kg>0)=1 OR
           ((SELECT count(*) FROM grainledger.ledger_positions WHERE lot_id=pos.lot_id AND quantity_kg>0)=1 AND
            (SELECT sum(quantity_kg) FROM grainledger.ledger_contributions WHERE lot_id=pos.lot_id)=pos.quantity_kg))
          AND (NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_source_balances WHERE lot_id=pos.lot_id AND location_id=pos.location_id)
           OR (SELECT sum(quantity_kg) FROM grainledger.ledger_container_source_balances WHERE lot_id=pos.lot_id AND location_id=pos.location_id)=pos.quantity_kg)),
       'reservedKg',res.quantity_kg::text,'remainingKg',(res.quantity_kg-coalesce((SELECT sum(CASE WHEN ev.action='RESERVE' THEN -ev.quantity_kg ELSE ev.quantity_kg END) FROM grainledger.ledger_blend_reservation_events ev
         WHERE ev.proposal_id=res.proposal_id AND ev.lot_id=res.lot_id AND ev.location_id=res.location_id),0))::numeric(20,6)::text)
       ORDER BY res.lot_id,res.location_id) FROM grainledger.ledger_blend_reservations res JOIN grainledger.ledger_lots lot ON lot.id=res.lot_id WHERE res.proposal_id=p.id AND EXISTS(SELECT 1 FROM jsonb_array_elements(grainledger.ledger_container_review_context(p.id)->'outputs') o WHERE (o->>'lotId')::uuid=res.lot_id AND (o->>'locationId')::uuid=res.location_id)),'[]'::jsonb))
    FROM grainledger.exports_active_blend_approvals active
    JOIN grainledger.exports_blend_plan_proposals p ON p.id=active.proposal_id
    JOIN grainledger.exports_blend_plan_drafts plan ON plan.id=p.plan_id
    JOIN grainledger.exports_blend_plan_previews v ON v.id=p.preview_id
    JOIN grainledger.exports_blend_plan_decisions d ON d.proposal_id=p.id AND d.version=active.decision_version AND d.action='APPROVED'
    WHERE active.container_id=target AND active.company_id=c.company_id),
  'dossierAvailable',EXISTS(SELECT 1 FROM grainledger.ledger_container_closures WHERE container_id=target)) INTO result;
 result:=result||jsonb_build_object('remainingKg',(c.target_mass_kg-(result->>'loadedKg')::numeric)::numeric(20,6)::text);
 IF result->'approval'<>'null'::jsonb THEN
  SELECT * INTO scope_review FROM grainledger.ledger_container_scope_reviews WHERE proposal_id=(result->'approval'->>'proposalId')::uuid ORDER BY recorded_at DESC,id DESC LIMIT 1;
  IF scope_review.id IS NOT NULL THEN
   result:=jsonb_set(result,'{approval}',(result->'approval')||jsonb_build_object('scopeReviewId',scope_review.id,'scope',scope_review.scope,
    'scopeReviewDecision',scope_review.decision,'outputReviewId',scope_review.id,
    'outputSnapshot',CASE WHEN scope_review.decision='APPROVED' THEN scope_review.basis->'outputSnapshot' ELSE NULL END));
  END IF;
 END IF;
 SELECT coalesce(jsonb_agg(jsonb_build_object('proposalId',q.id,'facilityId',(v.request_json->>'FacilityId')::uuid,'specificationId',d.specification_id,
  'snapshot',coalesce((SELECT r.basis->'outputSnapshot' FROM grainledger.ledger_container_scope_reviews r WHERE r.proposal_id=q.id AND r.decision='APPROVED' ORDER BY r.recorded_at DESC,r.id DESC LIMIT 1),
   CASE WHEN q.snapshot_json->'selection'->>'mode'='DIRECT_STOCK' THEN q.snapshot_json END),
  'reviewId',(SELECT r.id FROM grainledger.ledger_container_scope_reviews r WHERE r.proposal_id=q.id ORDER BY r.recorded_at DESC,r.id DESC LIMIT 1),
  'productionOperations',(SELECT coalesce(jsonb_agg(b.operation_id ORDER BY b.recorded_at,b.operation_id),'[]'::jsonb) FROM grainledger.ledger_container_batches b WHERE b.proposal_id=q.id),
  'latestDecision',(SELECT r.decision FROM grainledger.ledger_container_scope_reviews r WHERE r.proposal_id=q.id ORDER BY r.recorded_at DESC,r.id DESC LIMIT 1))),
  '[]'::jsonb) INTO closure_scopes
 FROM grainledger.exports_blend_plan_proposals q JOIN grainledger.exports_blend_plan_previews v ON v.id=q.preview_id
 JOIN grainledger.exports_blend_plan_drafts d ON d.id=q.plan_id
 WHERE q.id IN (SELECT proposal_id FROM grainledger.ledger_container_loads WHERE container_id=target);
 result:=result||jsonb_build_object('closingScopes',closure_scopes,'workflowAvailable',true);
 IF action='trace' THEN
  IF (SELECT count(*) FROM grainledger.ledger_container_load_sources s JOIN grainledger.ledger_container_load_lines l ON l.id=s.line_id
       JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target)>50000 THEN
   RAISE EXCEPTION 'Container trace exceeds the supported complete result size' USING ERRCODE='22023'; END IF;
  result:=result-'approval'||jsonb_build_object('sourceContributions',coalesce((SELECT jsonb_agg(jsonb_build_object('sourceLotId',source_lot_id,'quantityKg',kg::numeric(20,6)::text) ORDER BY source_lot_id)
   FROM (SELECT s.source_lot_id,sum(s.quantity_kg) kg FROM grainledger.ledger_container_load_sources s
    JOIN grainledger.ledger_container_load_lines l ON l.id=s.line_id JOIN grainledger.ledger_container_loads h ON h.id=l.load_id
    WHERE h.container_id=target GROUP BY s.source_lot_id) totals),'[]'::jsonb));
  INSERT INTO grainledger.ledger_events(kind,actor_id,details)
   VALUES('TRACEBACK',a,jsonb_build_object('containerId',target,'ownerId',c.company_id,'facilityId',ext.facility_id,'direction','backward')) RETURNING id INTO audit_id;
  result:=result||jsonb_build_object('traceId',audit_id);
 END IF;
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
   active.proposal_id IS NOT NULL active_approval
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
   'activeApproval',active_approval,'basis','SAVED_PLANNING') ORDER BY proposed_at DESC,proposal_id) FROM affected_plans),'[]'::jsonb),
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

CREATE OR REPLACE FUNCTION grainledger.ledger_container_trace_graph(a uuid,target uuid) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE r text:=grainledger.ledger_role(a); c grainledger.exports_containers; roots uuid[];
 result jsonb; loading jsonb; header jsonb;
BEGIN
 SELECT * INTO c FROM grainledger.exports_containers WHERE id=target;
 IF r IS NULL OR r NOT IN ('CustomerManager','Administrator') OR c.id IS NULL
  OR (r<>'Administrator' AND NOT grainledger.ledger_own(a,c.company_id)) THEN
  RAISE EXCEPTION 'Container sources unavailable in current scope' USING ERRCODE='42501'; END IF;
 IF (SELECT count(*) FROM grainledger.ledger_container_load_lines l
  JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target)>20000 THEN
  RAISE EXCEPTION 'Container trace exceeds complete loading limit' USING ERRCODE='23514'; END IF;
 SELECT array_agg(DISTINCT l.lot_id) INTO roots FROM grainledger.ledger_container_load_lines l
 JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target;
 IF roots IS NULL THEN
  result:=jsonb_build_object('schemaVersion',2,'traceId',gen_random_uuid(),'ownerId',c.company_id,
   'asOf',statement_timestamp(),'snapshot',pg_current_snapshot()::text,'direction','backward','view','current',
   'graphBasis','RECORDED_MATERIAL_RELATIONSHIPS','sourceContributionsVisible',true,'complete',true,
   'lotCount',0,'edgeCount',0,'lots','[]'::jsonb,'edges','[]'::jsonb,'operations','[]'::jsonb,
   'custodyEvents','[]'::jsonb,'corrections','[]'::jsonb);
 ELSE
  result:=grainledger.ledger_trace_graph(a,roots,'backward','current');
  IF (result->>'ownerId')::uuid<>c.company_id THEN
   RAISE EXCEPTION 'Container manifest owner mismatch' USING ERRCODE='23514'; END IF;
 END IF;
 SELECT coalesce(jsonb_agg(edge ORDER BY lot_id),'[]'::jsonb) INTO loading FROM (
  SELECT l.lot_id,jsonb_build_object('containerId',target,'lotId',l.lot_id,'basis','ACTUAL_LOADING',
   'quantityKg',sum(l.quantity_kg)::numeric(20,6)::text,
   'lines',jsonb_agg(jsonb_build_object('lineId',l.id,'loadId',h.id,'locationId',l.location_id,
    'proposalId',h.proposal_id,'loadedAt',h.loaded_at,'quantityKg',l.quantity_kg::numeric(20,6)::text,
    'sourceContributions',coalesce((SELECT jsonb_agg(jsonb_build_object('sourceLotId',s.source_lot_id,
      'quantityKg',s.quantity_kg::numeric(20,6)::text) ORDER BY s.source_lot_id)
     FROM grainledger.ledger_container_load_sources s WHERE s.line_id=l.id),'[]'::jsonb)) ORDER BY h.loaded_at,h.id,l.id)) edge
  FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id
  WHERE h.container_id=target GROUP BY l.lot_id) grouped;
 header:=jsonb_build_object('containerId',target,'code',c.internal_code,'status',c.status,'basis','ACTUAL_LOADING',
  'linkedLoadedKg',coalesce((SELECT sum(l.quantity_kg) FROM grainledger.ledger_container_load_lines l
   JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target),0)::numeric(20,6)::text,
  'held',EXISTS(SELECT 1 FROM grainledger.ledger_container_holds WHERE container_id=target),
  'restricted',EXISTS(SELECT 1 FROM unnest(roots) lot WHERE grainledger.ledger_container_lot_blocked(lot)));
 RETURN (result-'targetLotId')||jsonb_build_object('root',jsonb_build_object('type','container','id',target),
  'actualContainers',jsonb_build_array(header),'loadingEdges',loading,'containerCoverage','ACTUAL_LOADING');
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
  WHEN 'hold' THEN ARRAY['operationId','version','reason','evidenceId'] WHEN 'load' THEN ARRAY['operationId','version','proposalId','decisionVersion','loadedAt','lines']
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
  IF p->>'evidenceId' IS NOT NULL AND NOT EXISTS(
   SELECT 1 FROM grainledger.ledger_evidence e JOIN grainledger.ledger_facilities f ON f.id=e.facility_id AND NOT f.is_deleted
   WHERE e.id=(p->>'evidenceId')::uuid AND e.owner_id=c.company_id AND (
    e.facility_id=ext.facility_id OR EXISTS(
     SELECT 1 FROM grainledger.exports_blend_plan_proposals proposal
     JOIN grainledger.exports_blend_plan_drafts draft ON draft.id=proposal.plan_id
     JOIN grainledger.exports_blend_plan_previews preview ON preview.id=proposal.preview_id
     WHERE draft.container_id=target AND draft.company_id=c.company_id
      AND (preview.request_json->>'FacilityId')::uuid=e.facility_id))) THEN
   RAISE EXCEPTION 'HOLD evidence outside company/facility' USING ERRCODE='42501'; END IF;
  INSERT INTO grainledger.ledger_container_holds(id,container_id,actor_id,reason,evidence_id)
  VALUES(operation,target,a,btrim(p->>'reason'),(p->>'evidenceId')::uuid);
  IF c.status<>'CLOSED' THEN
   PERFORM grainledger.ledger_halt_container_approval(a,target,'Container HOLD blocks the remaining scope',operation);
  END IF;
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
  IF selected_proposal IS NULL THEN RAISE EXCEPTION 'Current pinned approval required' USING ERRCODE='23514'; END IF;
  IF action='load' THEN
   IF selected_proposal IS DISTINCT FROM (p->>'proposalId')::uuid OR NOT EXISTS(SELECT 1 FROM grainledger.exports_active_blend_approvals
    WHERE proposal_id=selected_proposal AND decision_version=(p->>'decisionVersion')::integer) THEN RAISE EXCEPTION 'Approval decision changed' USING ERRCODE='23514'; END IF;
   SELECT (v.request_json->>'FacilityId')::uuid INTO facility FROM grainledger.exports_blend_plan_proposals prop
    JOIN grainledger.exports_blend_plan_previews v ON v.id=prop.preview_id
    WHERE prop.id=selected_proposal AND (prop.snapshot_json->'selection'->>'mode'='DIRECT_STOCK' OR EXISTS(SELECT 1 FROM grainledger.ledger_container_scope_reviews r WHERE r.container_id=target AND r.proposal_id=prop.id AND r.decision='APPROVED' AND r.basis->'outputSnapshot'<>'null'::jsonb));
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
    IF NOT EXISTS(SELECT 1 FROM jsonb_array_elements(grainledger.ledger_container_review_context(selected_proposal)->'outputs') o
     WHERE (o->>'lotId')::uuid=(line->>'lotId')::uuid AND (o->>'locationId')::uuid=(line->>'locationId')::uuid) THEN
     RAISE EXCEPTION 'Only current reviewed output positions may be loaded' USING ERRCODE='23514'; END IF;
    SELECT * INTO lot FROM grainledger.ledger_lots WHERE id=(line->>'lotId')::uuid AND owner_id=company;
    SELECT * INTO loc FROM grainledger.ledger_locations WHERE id=(line->>'locationId')::uuid AND facility_id=facility;
    IF lot.id IS NULL OR loc.id IS NULL OR (loc.owner_id IS NOT NULL AND loc.owner_id<>company) THEN RAISE EXCEPTION 'Loading position outside approved scope' USING ERRCODE='42501'; END IF;
    PERFORM grainledger.ledger_require_active_location(loc.id);
    IF loc.mixed_bulk THEN RAISE EXCEPTION 'Assigned operator must hand off current pool stock to a reviewed ordinary loading position' USING ERRCODE='23514'; END IF;
    IF grainledger.ledger_container_lot_blocked(lot.id) THEN RAISE EXCEPTION 'Loading lot is restricted' USING ERRCODE='23514'; END IF;
    SELECT quantity_kg INTO physical FROM grainledger.ledger_positions WHERE lot_id=lot.id AND location_id=loc.id FOR UPDATE;
    SELECT res.quantity_kg-coalesce((SELECT sum(CASE WHEN e.action='RESERVE' THEN -e.quantity_kg ELSE e.quantity_kg END) FROM grainledger.ledger_blend_reservation_events e
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
 IF action IN ('load','close') THEN PERFORM grainledger.ledger_production_assert_basis(basis,company); END IF;
 result:=grainledger.ledger_export_container_read(a,target,'detail');
 INSERT INTO grainledger.ledger_container_execution_requests VALUES(a,operation,target,action,p,result);
 RETURN result;
END $$;

CREATE OR REPLACE FUNCTION grainledger.exports_protect_plan_draft() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Plan history cannot be deleted' USING ERRCODE='23514';
    END IF;
    IF TG_OP='INSERT' THEN PERFORM grainledger.ledger_lock_writes(); END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended('export:' || replace(NEW.company_id::text, '-', ''), 0));
    IF TG_OP = 'UPDATE' THEN
        IF OLD.status <> 'DRAFT' OR NEW.status <> 'CANCELLED' OR NEW.version <> OLD.version + 1
            OR (to_jsonb(NEW) - ARRAY['status','version','cancelled_by','cancelled_at'])
               IS DISTINCT FROM (to_jsonb(OLD) - ARRAY['status','version','cancelled_by','cancelled_at']) THEN
            RAISE EXCEPTION 'Plan demand is immutable; only cancellation is supported' USING ERRCODE='23514';
        END IF;
    ELSE
        IF NEW.status <> 'DRAFT' OR NEW.version <> 1 OR NOT EXISTS (
            SELECT 1 FROM grainledger.exports_containers c
            JOIN grainledger.exports_export_orders o ON o.id=c.order_id
            JOIN grainledger.exports_order_specifications s ON s.order_id=o.id AND s.version_no=o.current_specification_version
            WHERE c.id=NEW.container_id AND c.company_id=NEW.company_id AND o.id=NEW.order_id
              AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_container_holds h WHERE h.container_id=c.id)
              AND s.id=NEW.specification_id AND c.status<>'CLOSED' AND NEW.target_mass_kg>0 AND grainledger.ledger_container_unplanned_kg(c.id)=NEW.target_mass_kg
        ) THEN
            RAISE EXCEPTION 'Plan must reference current container demand' USING ERRCODE='23514';
        END IF;
    END IF;
    RETURN NEW;
END; $$;

CREATE FUNCTION grainledger.ledger_closed_order_kg(actor uuid, target uuid) RETURNS numeric
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
BEGIN
 IF coalesce(grainledger.ledger_role(actor),'') NOT IN ('ExportStaff','CustomerManager') OR NOT EXISTS(
  SELECT 1 FROM grainledger.exports_export_orders o WHERE o.id=target AND grainledger.ledger_own(actor,o.company_id)
 ) THEN
  RAISE EXCEPTION 'Export order unavailable in current company' USING ERRCODE='42501';
 END IF;
 RETURN coalesce((
  SELECT sum(line.quantity_kg) FROM grainledger.exports_containers c
  JOIN grainledger.ledger_container_closures closure ON closure.container_id=c.id
  JOIN grainledger.ledger_container_loads load ON load.container_id=c.id
  JOIN grainledger.ledger_container_load_lines line ON line.load_id=load.id
  WHERE c.order_id=target AND c.status='CLOSED'
 ),0);
END $$;

CREATE FUNCTION grainledger.ledger_business_history(actor uuid, q jsonb) RETURNS jsonb
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
   role_name IN ('MillOperator','MillManager') AND grainledger.ledger_assigned(actor,x.facility_id))
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

REVOKE ALL ON FUNCTION grainledger.ledger_closed_order_kg(uuid,uuid), grainledger.ledger_business_history(uuid,jsonb) FROM PUBLIC;
