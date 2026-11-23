-- Export contexts are separate from immutable decisions and physical stock.
CREATE TABLE grainledger.exports_residue_freshness (
 target_kind text NOT NULL CHECK(target_kind IN ('PLAN','CONTAINER')),
 target_id uuid NOT NULL, context_key text NOT NULL, company_id uuid NOT NULL,
 risk_event_id uuid NOT NULL REFERENCES grainledger.ledger_residue_risk_events(id),
 snapshot_at timestamptz NOT NULL, PRIMARY KEY(target_kind,target_id,context_key)
);

-- This private builder is only reachable through a fenced job capability.
CREATE FUNCTION grainledger.exports_residue_context(entry uuid,at_time timestamptz) RETURNS jsonb
LANGUAGE plpgsql STABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE t grainledger.ledger_residue_change_targets; q grainledger.exports_blend_plan_proposals;
 d grainledger.exports_blend_plan_drafts; o grainledger.exports_export_orders;
 c grainledger.exports_containers; facility uuid; compatibility uuid; ctx jsonb; sections jsonb; lots uuid[];
 result jsonb; quality jsonb; revision text;
BEGIN
 SELECT * INTO STRICT t FROM grainledger.ledger_residue_change_targets WHERE id=entry AND target_kind IN ('PLAN','CONTAINER');
 SELECT * INTO STRICT q FROM grainledger.exports_blend_plan_proposals WHERE id=t.context_key::uuid AND company_id=t.company_id;
 SELECT * INTO STRICT d FROM grainledger.exports_blend_plan_drafts WHERE id=q.plan_id AND company_id=t.company_id;
 SELECT * INTO STRICT o FROM grainledger.exports_export_orders WHERE id=d.order_id AND company_id=t.company_id;
 SELECT * INTO STRICT c FROM grainledger.exports_containers WHERE id=d.container_id AND company_id=t.company_id;
 SELECT (request_json->>'FacilityId')::uuid INTO facility FROM grainledger.exports_blend_plan_previews WHERE id=q.preview_id;
 compatibility:=(q.snapshot_json->'selection'->>'compatibilityPolicyId')::uuid;
 IF t.target_kind='PLAN' THEN
  sections:=jsonb_build_array(jsonb_build_object('scope','PLANNED_ALLOCATION','mode',q.snapshot_json->'selection'->>'mode',
    'targetKg',d.target_mass_kg::text,'allocations',q.snapshot_json->'selection'->'allocations'));
 ELSE
  ctx:=grainledger.ledger_container_review_context(q.id);
  sections:='[]'::jsonb;
  IF c.status<>'CLOSED' AND ctx->>'mode'='BLEND_INPUT_STOCK' AND (ctx->>'remainingProductionKg')::numeric>0 THEN
   sections:=sections||jsonb_build_array(jsonb_build_object('scope','REMAINING_INPUT','mode','BLEND_INPUT_STOCK',
    'targetKg',ctx->>'remainingProductionKg','allocations',(SELECT coalesce(jsonb_agg(i),'[]') FROM jsonb_array_elements(ctx->'inputs') i WHERE (i->>'quantityKg')::numeric>0)));
  END IF;
  -- Loaded stock must remain visible even after a claim has been consumed or handed off.
  sections:=sections||coalesce((SELECT jsonb_agg(jsonb_build_object('scope','ACTUAL_LOADING','mode','DIRECT_STOCK',
    'targetKg',x.qty::text,'allocations',x.items)) FROM (
     SELECT sum(l.quantity_kg) qty,jsonb_agg(jsonb_build_object('lotId',l.lot_id,'locationId',l.location_id,'quantityKg',l.quantity_kg::text) ORDER BY l.lot_id,l.location_id) items
     FROM (SELECT l.lot_id,l.location_id,sum(l.quantity_kg) quantity_kg FROM grainledger.ledger_container_load_lines l
      JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=c.id AND h.proposal_id=q.id
      GROUP BY l.lot_id,l.location_id) l HAVING sum(l.quantity_kg)>0) x),'[]');
  IF c.status<>'CLOSED' THEN
   sections:=sections||coalesce((SELECT jsonb_agg(jsonb_build_object('scope','ACTUAL_OUTPUT','mode','DIRECT_STOCK',
     'targetKg',x.qty::text,'allocations',x.items)) FROM (SELECT sum((i->>'remainingKg')::numeric) qty,
      jsonb_agg(i||jsonb_build_object('quantityKg',i->>'remainingKg') ORDER BY i->>'lotId',i->>'locationId') items
      FROM jsonb_array_elements(ctx->'outputs') i WHERE (i->>'remainingKg')::numeric>0 HAVING sum((i->>'remainingKg')::numeric)>0) x),'[]');
  END IF;
 END IF;
 SELECT coalesce(array_agg(DISTINCT (a->>'lotId')::uuid ORDER BY (a->>'lotId')::uuid),'{}') INTO lots
 FROM jsonb_array_elements(sections) s,jsonb_array_elements(s->'allocations') a;
 IF EXISTS(SELECT 1 FROM unnest(lots) l WHERE NOT EXISTS(SELECT 1 FROM grainledger.ledger_lots WHERE id=l AND owner_id=t.company_id)) THEN
  RAISE EXCEPTION 'Export context owner mismatch' USING ERRCODE='42501'; END IF;
 SELECT coalesce(jsonb_agg(to_jsonb(x) ORDER BY x.lot_id,x.attribute_id),'[]') INTO quality FROM (
  SELECT DISTINCT ON(lot_id,attribute_id) id,lot_id,attribute_id,version_no,measured_value::text,unit,evidence_id,measured_at,confirmed_at
  FROM grainledger.ledger_planning_quality_observations WHERE owner_id=t.company_id AND facility_id=facility AND lot_id=ANY(lots) AND confirmed_at<=at_time
  ORDER BY lot_id,attribute_id,version_no DESC,id) x;
 result:=jsonb_build_object('companyId',t.company_id,'targetKind',t.target_kind,'targetId',t.target_id,'proposalId',q.id,
  'marketId',o.market_id,'commodityId',o.commodity_id,'facilityId',facility,'containerStatus',c.status,
  'targetProduct',d.demand_snapshot::jsonb->>'targetProductIdentity','sections',sections,'quality',quality,
  'savedDependencyRevision',q.snapshot_json->>'dependencyRevision',
  'historicalDecisionVersion',(SELECT max(version) FROM grainledger.exports_blend_plan_decisions WHERE proposal_id=q.id),
  'historicalDossierId',(SELECT container_id FROM grainledger.ledger_container_closures WHERE container_id=c.id),
  'orderVersion',o.version,'specificationVersion',o.current_specification_version,
  'demandCurrent',d.demand_snapshot::jsonb->>'orderVersion'=o.version::text AND d.demand_snapshot::jsonb->>'specificationVersion'=o.current_specification_version::text AND d.target_mass_kg=c.target_mass_kg,
  'limits',coalesce((SELECT jsonb_agg(to_jsonb(l) ORDER BY l.attribute_id) FROM grainledger.exports_specification_limits l
   JOIN grainledger.exports_order_specifications s ON s.id=l.specification_id WHERE s.order_id=o.id AND s.version_no=o.current_specification_version),'[]'),
  'compatibility',(SELECT to_jsonb(k)||jsonb_build_object('outputCatalogCommodityId',m.catalog_commodity_id) FROM grainledger.ledger_compatibilities k
   JOIN grainledger.ledger_catalog_commodities m ON m.ledger_commodity_id=k.output_commodity_id
   WHERE k.id=compatibility AND k.facility_id=facility AND k.effective_from<=at_time),
  'yield',(SELECT jsonb_build_object('id',y.id,'facilityId',y.facility_id,'compatibilityPolicyId',y.compatibility_id,'versionNo',y.version_no,
    'outputFraction',y.output_fraction::text,'effectiveFrom',y.effective_from,'effectiveTo',y.effective_to,'evidenceId',y.evidence_id,'publishedAt',y.published_at)
   FROM grainledger.ledger_planning_yield_rules y WHERE y.facility_id=facility AND y.compatibility_id=compatibility AND y.published_at<=at_time
    AND y.effective_from<=at_time AND (y.effective_to IS NULL OR y.effective_to>at_time) ORDER BY y.version_no DESC,y.id LIMIT 1),
  'lotCommodities',(SELECT jsonb_object_agg(id::text,commodity_id) FROM grainledger.ledger_lots WHERE id=ANY(lots)),
  'graphs',coalesce((SELECT jsonb_agg(jsonb_build_object('lotId',l,'nodes',grainledger.ledger_residue_graph_core(l)) ORDER BY l) FROM unnest(lots) l),'[]'),
  'commodityCodes',(SELECT jsonb_object_agg(id::text,code) FROM grainledger.catalog_commodities),
  'lotRevisions',coalesce((SELECT jsonb_object_agg(l::text,grainledger.ledger_residue_basis(l,at_time)) FROM unnest(lots) l),'{}'),
  'policyBasis',coalesce((SELECT jsonb_agg(to_jsonb(p) ORDER BY p.version_no,p.id) FROM grainledger.catalog_blend_assessment_policies p
    WHERE p.company_id=t.company_id AND p.market_id=o.market_id AND p.commodity_id=o.commodity_id AND p.published_at<=at_time AND p.effective_from<=at_time AND (p.effective_to IS NULL OR p.effective_to>at_time)),'[]'),
  'mrlBasis',coalesce((SELECT jsonb_agg(to_jsonb(v)||jsonb_build_object('limits',(SELECT jsonb_agg(to_jsonb(l) ORDER BY l.substance_id) FROM grainledger.catalog_mrl_limits l WHERE l.mrl_version_id=v.id)) ORDER BY v.version_no,v.id)
   FROM grainledger.catalog_mrl_versions v WHERE v.market_id=o.market_id AND v.commodity_id=o.commodity_id AND v.status='PUBLISHED' AND v.published_at<=at_time AND v.effective_from<=at_time AND (v.effective_to IS NULL OR v.effective_to>at_time)),'[]'));
 revision:=md5(result::text);
 RETURN result||jsonb_build_object('revision',revision,'snapshotAt',at_time);
END $$;

CREATE FUNCTION grainledger.exports_residue_job(job uuid,token uuid,gen integer,action text,target uuid,p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE j grainledger.ledger_residue_changes; t grainledger.ledger_residue_change_targets; q record;
 last_id uuid; n integer:=0; batch integer:=coalesce((p->>'batchSize')::integer,25); ctx jsonb; snap jsonb; risk uuid; at_time timestamptz; persisted_snapshot timestamptz;
BEGIN
 IF action IS NULL OR action NOT IN ('DISCOVER','NEXT','READ','APPEND') OR jsonb_typeof(p) IS DISTINCT FROM 'object' OR batch NOT BETWEEN 1 AND 100 THEN
  RAISE EXCEPTION 'Invalid export residue request' USING ERRCODE='22023'; END IF;
 IF action IN ('READ','NEXT') THEN j:=grainledger.ledger_residue_job_check(job,token,gen);
 ELSE
  IF current_setting('transaction_isolation')<>'read committed' THEN RAISE EXCEPTION 'READ COMMITTED required' USING ERRCODE='23514'; END IF;
  PERFORM pg_advisory_xact_lock(714928352);
  IF action='APPEND' THEN
   SELECT * INTO t FROM grainledger.ledger_residue_change_targets WHERE id=target AND change_id=job AND target_kind IN ('PLAN','CONTAINER');
   IF t.id IS NULL THEN RAISE EXCEPTION 'Export target unavailable' USING ERRCODE='55000'; END IF;
   PERFORM pg_advisory_xact_lock(hashtextextended('export:'||replace(t.company_id::text,'-',''),0));
  END IF;
  PERFORM 1 FROM grainledger.ledger_residue_changes WHERE id=job FOR UPDATE;
  j:=grainledger.ledger_residue_job_check(job,token,gen);
 END IF;
 IF action='DISCOVER' THEN
  IF NOT j.discovery_complete OR coalesce((j.discovery_cursor->>'exportsDone')::boolean,false) THEN RETURN '{}'::jsonb; END IF;
  last_id:=(j.discovery_cursor->>'exportAfter')::uuid;
  FOR q IN SELECT p.id,p.plan_id,p.company_id,d.container_id FROM grainledger.exports_blend_plan_proposals p
   JOIN grainledger.exports_blend_plan_drafts d ON d.id=p.plan_id
   WHERE (last_id IS NULL OR p.id>last_id) AND (j.company_id IS NULL OR p.company_id=j.company_id)
    AND (EXISTS(SELECT 1 FROM jsonb_array_elements(p.snapshot_json->'selection'->'allocations') a
      JOIN grainledger.ledger_residue_frontier f ON f.lot_id=(a->>'lotId')::uuid WHERE f.change_id=job)
     OR EXISTS(SELECT 1 FROM grainledger.ledger_container_output_claims c JOIN grainledger.ledger_residue_frontier f ON f.lot_id=c.lot_id WHERE c.proposal_id=p.id AND f.change_id=job)
     OR EXISTS(SELECT 1 FROM grainledger.ledger_container_loads h JOIN grainledger.ledger_container_load_lines l ON l.load_id=h.id
      JOIN grainledger.ledger_residue_frontier f ON f.lot_id=l.lot_id WHERE h.proposal_id=p.id AND f.change_id=job))
   ORDER BY p.id LIMIT batch LOOP
   INSERT INTO grainledger.ledger_residue_change_targets(change_id,company_id,target_kind,target_id,context_key,relationship_basis)
    VALUES(job,q.company_id,'PLAN',q.plan_id,q.id::text,'PLANNED_ALLOCATION') ON CONFLICT DO NOTHING;
   IF EXISTS(SELECT 1 FROM grainledger.ledger_container_batches WHERE proposal_id=q.id)
     OR EXISTS(SELECT 1 FROM grainledger.ledger_container_loads WHERE proposal_id=q.id)
     OR EXISTS(SELECT 1 FROM grainledger.ledger_container_output_claims WHERE proposal_id=q.id) THEN
    INSERT INTO grainledger.ledger_residue_change_targets(change_id,company_id,target_kind,target_id,context_key,relationship_basis)
     VALUES(job,q.company_id,'CONTAINER',q.container_id,q.id::text,'ACTUAL_LOADING') ON CONFLICT DO NOTHING;
   END IF;
   n:=n+1; last_id:=q.id;
  END LOOP;
  UPDATE grainledger.ledger_residue_changes SET discovery_cursor=discovery_cursor||jsonb_build_object('exportAfter',last_id,'exportsDone',n<batch),lease_until=clock_timestamp()+interval '60 seconds',updated_at=clock_timestamp() WHERE id=job;
  RETURN jsonb_build_object('discoveryComplete',n<batch);
 ELSIF action='NEXT' THEN
  RETURN coalesce((SELECT jsonb_agg(id ORDER BY id) FROM (SELECT id FROM grainledger.ledger_residue_change_targets WHERE change_id=job AND target_kind IN ('PLAN','CONTAINER') AND state<>'COMPLETED' ORDER BY id LIMIT batch) x),'[]');
 END IF;
 SELECT * INTO t FROM grainledger.ledger_residue_change_targets WHERE id=target AND change_id=job AND target_kind IN ('PLAN','CONTAINER');
 IF t.id IS NULL THEN RAISE EXCEPTION 'Export target unavailable' USING ERRCODE='55000'; END IF;
 at_time:=clock_timestamp(); ctx:=grainledger.exports_residue_context(t.id,at_time);
 IF action='READ' THEN RETURN ctx; END IF;
 IF ctx->>'revision' IS DISTINCT FROM p->>'revision' THEN RAISE EXCEPTION 'Export residue basis changed' USING ERRCODE='40001'; END IF;
 snap:=p->'snapshot';
 IF snap->>'targetId' IS DISTINCT FROM t.target_id::text OR snap->>'targetKind' IS DISTINCT FROM t.target_kind
  OR snap->>'marketId' IS DISTINCT FROM ctx->>'marketId' OR snap->>'commodityId' IS DISTINCT FROM ctx->>'commodityId'
  OR snap->>'proposalId' IS DISTINCT FROM t.context_key OR coalesce(snap->>'assessmentStatus','') NOT IN ('PASS','FAIL','INCOMPLETE')
  OR snap->>'engineVersion' IS DISTINCT FROM 'export-residue-reassessment-1' OR jsonb_typeof(snap->'findings') IS DISTINCT FROM 'array'
  OR snap->>'snapshotAt' IS NULL OR (snap->>'snapshotAt')::timestamptz<j.known_at OR (snap->>'snapshotAt')::timestamptz>at_time THEN
  RAISE EXCEPTION 'Invalid export risk snapshot' USING ERRCODE='22023'; END IF;
 INSERT INTO grainledger.ledger_residue_risk_events(target_entry_id,dependency_revision,assessment_status,snapshot_at,snapshot)
  VALUES(t.id,p->>'revision',snap->>'assessmentStatus',(snap->>'snapshotAt')::timestamptz,snap) ON CONFLICT DO NOTHING;
 SELECT id,snapshot_at INTO STRICT risk,persisted_snapshot FROM grainledger.ledger_residue_risk_events WHERE target_entry_id=t.id AND dependency_revision=p->>'revision';
 INSERT INTO grainledger.exports_residue_freshness(target_kind,target_id,context_key,company_id,risk_event_id,snapshot_at)
  VALUES(t.target_kind,t.target_id,t.context_key,t.company_id,risk,persisted_snapshot)
  ON CONFLICT(target_kind,target_id,context_key) DO UPDATE SET risk_event_id=excluded.risk_event_id,snapshot_at=excluded.snapshot_at
  WHERE excluded.snapshot_at>=exports_residue_freshness.snapshot_at;
 UPDATE grainledger.ledger_residue_change_targets SET state='COMPLETED',dependency_revision=p->>'revision',last_error=NULL,updated_at=at_time WHERE id=t.id;
 UPDATE grainledger.ledger_residue_changes SET lease_until=clock_timestamp()+interval '60 seconds',updated_at=clock_timestamp() WHERE id=job;
 RETURN jsonb_build_object('riskEventId',risk);
END $$;

ALTER FUNCTION grainledger.ledger_residue_job(uuid,uuid,integer,text,uuid,jsonb) RENAME TO ledger_residue_lot_job;
REVOKE ALL ON FUNCTION grainledger.ledger_residue_lot_job(uuid,uuid,integer,text,uuid,jsonb) FROM PUBLIC;
-- Existing runtime grants follow the renamed function; remove them explicitly.
DO $$ DECLARE r record; BEGIN FOR r IN SELECT grantee FROM information_schema.routine_privileges WHERE routine_schema='grainledger' AND routine_name='ledger_residue_lot_job' AND grantee<>current_user AND grantee<>'PUBLIC' LOOP
 EXECUTE format('REVOKE ALL ON FUNCTION grainledger.ledger_residue_lot_job(uuid,uuid,integer,text,uuid,jsonb) FROM %I',r.grantee); END LOOP; END $$;
CREATE FUNCTION grainledger.ledger_residue_job(job uuid,token uuid,gen integer,action text,target uuid,p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE j grainledger.ledger_residue_changes;
BEGIN
 IF action='FINISH' THEN
  j:=grainledger.ledger_residue_job_check(job,token,gen);
  IF NOT coalesce((j.discovery_cursor->>'exportsDone')::boolean,false) THEN RETURN jsonb_build_object('completed',false); END IF;
 END IF;
 RETURN grainledger.ledger_residue_lot_job(job,token,gen,action,target,p);
END $$;
REVOKE ALL ON FUNCTION grainledger.exports_residue_context(uuid,timestamptz),grainledger.exports_residue_job(uuid,uuid,integer,text,uuid,jsonb),grainledger.ledger_residue_job(uuid,uuid,integer,text,uuid,jsonb) FROM PUBLIC;

-- Reopen only orchestration metadata. Completed LOT evaluations remain immutable.
UPDATE grainledger.ledger_residue_changes SET state='PENDING',lease_id=NULL,lease_until=NULL,generation=generation+1,
 due_at=least(due_at,clock_timestamp()),updated_at=clock_timestamp()
 WHERE state='COMPLETED' AND discovery_complete AND NOT discovery_cursor ? 'exportsDone';

CREATE OR REPLACE FUNCTION grainledger.ledger_residue_public_assessment(s jsonb,manager boolean) RETURNS jsonb
LANGUAGE sql IMMUTABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
 SELECT jsonb_build_object('historicalDecisionVersion',grainledger.ledger_residue_public_scalar(s->'historicalDecisionVersion'),'freshness',grainledger.ledger_residue_public_scalar(s->'freshness'),'proposalId',grainledger.ledger_residue_public_scalar(s->'proposalId'),
  'engineVersion',grainledger.ledger_residue_public_scalar(s->'engineVersion'),'marketId',grainledger.ledger_residue_public_scalar(s->'marketId'),'commodityId',grainledger.ledger_residue_public_scalar(s->'commodityId'),
  'materialBasis',grainledger.ledger_residue_public_scalar(s->'materialBasis'),'mrlVersionId',grainledger.ledger_residue_public_scalar(s->'mrlVersionId'),'marginPolicyId',grainledger.ledger_residue_public_scalar(s->'marginPolicyId'),
  'historicalDecisionId',grainledger.ledger_residue_public_scalar(s->'historicalDecisionId'),'historicalDossierId',grainledger.ledger_residue_public_scalar(s->'historicalDossierId'),
  'findings',coalesce((SELECT jsonb_agg(jsonb_strip_nulls(jsonb_build_object(
    'code',grainledger.ledger_residue_public_scalar(f->'code'),'status',grainledger.ledger_residue_public_scalar(f->'status'),'actual',grainledger.ledger_residue_public_scalar(f->'actual'),'limit',grainledger.ledger_residue_public_scalar(f->'limit'),'unit',grainledger.ledger_residue_public_scalar(f->'unit'),
    'substanceId',grainledger.ledger_residue_public_scalar(f->'substanceId'),'attributeId',grainledger.ledger_residue_public_scalar(f->'attributeId'))) ORDER BY ord)
   FROM jsonb_array_elements(CASE WHEN jsonb_typeof(s->'findings')='array' THEN s->'findings' ELSE '[]' END)
    WITH ORDINALITY a(f,ord) WHERE jsonb_typeof(f)='object'),'[]'),
  'results',coalesce((SELECT jsonb_agg(jsonb_strip_nulls(jsonb_build_object(
    'substanceId',grainledger.ledger_residue_public_scalar(r->'substanceId'),'status',grainledger.ledger_residue_public_scalar(r->'status'),'lower',grainledger.ledger_residue_public_scalar(r->'lower'),'upper',grainledger.ledger_residue_public_scalar(r->'upper'),'unit',grainledger.ledger_residue_public_scalar(r->'unit'),
    'profileCode',grainledger.ledger_residue_public_scalar(r->'profileCode'),'commodityId',grainledger.ledger_residue_public_scalar(r->'commodityId'),'productStage',grainledger.ledger_residue_public_scalar(r->'productStage'),'productIdentity',grainledger.ledger_residue_public_scalar(r->'productIdentity'),
    'reasons',coalesce((SELECT jsonb_agg(reason ORDER BY n) FROM jsonb_array_elements(
       CASE WHEN jsonb_typeof(r->'reasons')='array' THEN r->'reasons' ELSE '[]' END) WITH ORDINALITY a(reason,n)
       WHERE jsonb_typeof(reason)='string'),'[]'),
    'rules',coalesce((SELECT jsonb_agg(jsonb_strip_nulls(jsonb_build_object(
      'id',grainledger.ledger_residue_public_scalar(rule->'id'),'version',grainledger.ledger_residue_public_scalar(rule->'version'),'kind',grainledger.ledger_residue_public_scalar(rule->'kind'),'policyOrigin',grainledger.ledger_residue_public_scalar(rule->'policyOrigin'),
      'policySettingId',grainledger.ledger_residue_public_scalar(rule->'policySettingId'),'policySettingVersion',grainledger.ledger_residue_public_scalar(rule->'policySettingVersion'),
      'ruleCode',grainledger.ledger_residue_public_scalar(rule->'ruleCode'),'componentKey',grainledger.ledger_residue_public_scalar(rule->'componentKey'))) ORDER BY n)
      FROM jsonb_array_elements(CASE WHEN jsonb_typeof(r->'rules')='array' THEN r->'rules' ELSE '[]' END)
       WITH ORDINALITY a(rule,n) WHERE jsonb_typeof(rule)='object'),'[]'),
    'provenance',CASE WHEN manager THEN coalesce((SELECT jsonb_agg(jsonb_strip_nulls(jsonb_build_object(
      'kind',grainledger.ledger_residue_public_scalar(v->'kind'),'sourceLotId',grainledger.ledger_residue_public_scalar(v->'sourceLotId'),'reportId',grainledger.ledger_residue_public_scalar(v->'reportId'),'resultId',grainledger.ledger_residue_public_scalar(v->'resultId'),
      'groupVersionId',grainledger.ledger_residue_public_scalar(v->'groupVersionId'),'selectionId',grainledger.ledger_residue_public_scalar(v->'selectionId'),'ruleId',grainledger.ledger_residue_public_scalar(v->'ruleId'),
      'sourceId',grainledger.ledger_residue_public_scalar(v->'sourceId'),'associationId',grainledger.ledger_residue_public_scalar(v->'associationId'),'resultSetId',grainledger.ledger_residue_public_scalar(v->'resultSetId'))) ORDER BY n)
      FROM jsonb_array_elements(CASE WHEN jsonb_typeof(r->'provenance')='array' THEN r->'provenance' ELSE '[]' END)
       WITH ORDINALITY a(v,n) WHERE jsonb_typeof(v)='object'),'[]') ELSE NULL END
   )) || jsonb_build_object('lower',grainledger.ledger_residue_public_scalar(r->'lower'),'upper',grainledger.ledger_residue_public_scalar(r->'upper'),'unit',grainledger.ledger_residue_public_scalar(r->'unit')) ORDER BY ord) FROM jsonb_array_elements(CASE WHEN jsonb_typeof(s->'results')='array' THEN s->'results' ELSE '[]' END)
    WITH ORDINALITY a(r,ord) WHERE jsonb_typeof(r)='object'),'[]'))
$$;


CREATE FUNCTION grainledger.exports_residue_freshness_read(a uuid,kind text,target uuid,context text) RETURNS jsonb
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE owner uuid;
BEGIN
 SELECT u."OrganizationId" INTO owner FROM grainledger.identity_users u JOIN grainledger.identity_organizations o ON o."Id"=u."OrganizationId"
 WHERE u."Id"=a AND u."IsActive" AND u."Role" IN ('CustomerManager','ExportStaff') AND o."Kind"='Customer';
 IF owner IS NULL THEN RAISE EXCEPTION 'Export freshness access denied' USING ERRCODE='42501'; END IF;
 RETURN (SELECT jsonb_build_object('riskEventId',r.id,'assessedAt',r.snapshot_at,'assessmentStatus',r.assessment_status,
   'freshness',CASE WHEN r.dependency_revision=grainledger.exports_residue_context(r.target_entry_id,statement_timestamp())->>'revision'
     THEN r.snapshot->>'freshness' ELSE 'NOT_RECHECKED' END,'baselineUnchanged',r.snapshot->'baselineUnchanged')
 FROM grainledger.exports_residue_freshness f JOIN grainledger.ledger_residue_risk_events r ON r.id=f.risk_event_id
 WHERE f.company_id=owner AND f.target_kind=kind AND f.target_id=target AND f.context_key=context);
END $$;
REVOKE ALL ON FUNCTION grainledger.exports_residue_freshness_read(uuid,text,uuid,text) FROM PUBLIC;

-- Preserve the engine cutover guard while fencing SYSTEM evaluations by actor.
CREATE OR REPLACE FUNCTION grainledger.ledger_residue_replay(a uuid,l uuid,evaluation uuid,p jsonb) RETURNS jsonb
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE old grainledger.ledger_residue_evaluations;
BEGIN
 IF NOT grainledger.ledger_residue_authorized(a,l) THEN RAISE EXCEPTION 'Residue lot access denied' USING ERRCODE='42501'; END IF;
 SELECT * INTO old FROM grainledger.ledger_residue_evaluations WHERE id=evaluation;
 IF NOT FOUND THEN RETURN NULL; END IF;
 IF old.snapshot->>'engineVersion' IS DISTINCT FROM 'residue-flow-as-received-3' THEN
  RAISE EXCEPTION 'Evaluation ID belongs to an unsupported residue engine; use a new ID' USING ERRCODE='23505'; END IF;
 IF old.actor_id IS DISTINCT FROM a OR old.lot_id<>l OR old.request<>p THEN
  RAISE EXCEPTION 'Residue evaluation ID reused with different actor or payload' USING ERRCODE='23505'; END IF;
 RETURN old.snapshot;
END $$;
REVOKE ALL ON FUNCTION grainledger.ledger_residue_replay(uuid,uuid,uuid,jsonb) FROM PUBLIC;
