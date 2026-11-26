-- Public projections contain approved fields, never private job rows or raw lab payloads.
CREATE FUNCTION grainledger.ledger_residue_public_scalar(value jsonb) RETURNS jsonb
LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
 SELECT CASE WHEN jsonb_typeof(value) IN ('string','number','boolean','null') THEN value ELSE NULL END
$$;

CREATE FUNCTION grainledger.ledger_residue_public_assessment(s jsonb,manager boolean) RETURNS jsonb
LANGUAGE sql IMMUTABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
 SELECT jsonb_build_object(
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

CREATE FUNCTION grainledger.ledger_residue_public_risk(risk uuid,manager boolean) RETURNS jsonb
LANGUAGE sql STABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
 SELECT jsonb_build_object('id',r.id,'changeId',t.change_id,'targetType',t.target_kind,'targetId',t.target_id,
  'relationshipBasis',t.relationship_basis,'assessmentStatus',r.assessment_status,'snapshotAt',r.snapshot_at,
  'evaluatedAt',r.evaluated_at,'evaluationId',r.evaluation_id,'dependencyRevision',r.dependency_revision,
  'assessment',grainledger.ledger_residue_public_assessment(r.snapshot,manager))
 FROM grainledger.ledger_residue_risk_events r JOIN grainledger.ledger_residue_change_targets t ON t.id=r.target_entry_id
 WHERE r.id=risk
$$;

CREATE OR REPLACE FUNCTION grainledger.ledger_residue_change_read(a uuid,action text,target uuid,p jsonb) RETURNS jsonb
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE owner uuid; actor_role text; result jsonb; j grainledger.ledger_residue_changes;
 page_no integer:=1; page_size integer:=25; total bigint; done bigint;
 filter_kind text; target_id_filter uuid; filter_status text; target_state text;
 date_from timestamptz; date_to timestamptz; scoped_state text;
BEGIN
 SELECT u."OrganizationId",u."Role" INTO owner,actor_role FROM grainledger.identity_users u
 JOIN grainledger.identity_organizations o ON o."Id"=u."OrganizationId"
 WHERE u."Id"=a AND u."IsActive" AND u."Role" IN ('CustomerManager','ExportStaff') AND o."Kind"='Customer';
 IF owner IS NULL THEN RAISE EXCEPTION 'Residue change access denied' USING ERRCODE='42501'; END IF;
 IF action IS NULL OR action NOT IN ('change','targets','risks','risk') OR jsonb_typeof(p) IS DISTINCT FROM 'object' THEN
  RAISE EXCEPTION 'Invalid residue read request' USING ERRCODE='22023'; END IF;
 IF EXISTS(SELECT 1 FROM jsonb_object_keys(p) k WHERE NOT (
   action='targets' AND k IN ('page','pageSize','targetKind','targetId','state') OR
   action='risks' AND k IN ('page','pageSize','targetKind','targetId','assessmentStatus','from','to'))) THEN
  RAISE EXCEPTION 'Unknown residue read filter' USING ERRCODE='22023'; END IF;
 IF action<>'risks' AND (target IS NULL OR target='00000000-0000-0000-0000-000000000000') THEN
  RAISE EXCEPTION 'Nonempty resource ID required' USING ERRCODE='22023'; END IF;
 IF p ? 'page' AND (jsonb_typeof(p->'page') IS DISTINCT FROM 'number' OR p->>'page' !~ '^[0-9]+$') OR
    p ? 'pageSize' AND (jsonb_typeof(p->'pageSize') IS DISTINCT FROM 'number' OR p->>'pageSize' !~ '^[0-9]+$') THEN
  RAISE EXCEPTION 'Invalid pagination' USING ERRCODE='22023'; END IF;
 page_no:=coalesce((p->>'page')::integer,1); page_size:=coalesce((p->>'pageSize')::integer,25);
 IF page_no NOT BETWEEN 1 AND 1000000 OR page_size NOT BETWEEN 1 AND 100 THEN
  RAISE EXCEPTION 'Invalid pagination' USING ERRCODE='22023'; END IF;
 IF p ? 'targetKind' AND coalesce(p->>'targetKind','') NOT IN ('LOT','PLAN','CONTAINER') OR
    p ? 'assessmentStatus' AND coalesce(p->>'assessmentStatus','') NOT IN ('COMPLETE','UNRESOLVED','PASS','FAIL','INCOMPLETE') OR
    p ? 'state' AND coalesce(p->>'state','') NOT IN ('PENDING','PROCESSING','COMPLETED','FAILED_RETRYABLE') OR
    p ? 'targetId' AND (coalesce(p->>'targetId','') !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
       OR p->>'targetId'='00000000-0000-0000-0000-000000000000') THEN
  RAISE EXCEPTION 'Invalid residue read filters' USING ERRCODE='22023'; END IF;
 IF EXISTS(SELECT 1 FROM unnest(ARRAY['from','to']) k WHERE p ? k AND coalesce(p->>k,'') !~
   '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})$') THEN
  RAISE EXCEPTION 'Timestamp must include an explicit offset' USING ERRCODE='22023'; END IF;
 filter_kind:=p->>'targetKind'; target_id_filter:=(p->>'targetId')::uuid;
 filter_status:=p->>'assessmentStatus'; target_state:=p->>'state';
 date_from:=(p->>'from')::timestamptz; date_to:=(p->>'to')::timestamptz;
 IF date_from>=date_to THEN RAISE EXCEPTION 'Invalid date interval' USING ERRCODE='22023'; END IF;
 IF action IN ('change','targets') THEN
  SELECT * INTO j FROM grainledger.ledger_residue_changes c WHERE c.id=target AND
   (c.company_id=owner OR c.company_id IS NULL AND EXISTS(
      SELECT 1 FROM grainledger.ledger_residue_change_targets t WHERE t.change_id=c.id AND t.company_id=owner));
  IF j.id IS NULL THEN RAISE EXCEPTION 'Residue change not found' USING ERRCODE='P0002'; END IF;
  IF action='change' THEN
   SELECT count(*),count(*) FILTER(WHERE t.state='COMPLETED') INTO total,done
    FROM grainledger.ledger_residue_change_targets t WHERE t.change_id=target AND t.company_id=owner;
   scoped_state:=CASE WHEN j.company_id IS NULL AND j.discovery_complete AND total=done THEN 'COMPLETED' ELSE j.state END;
   RETURN jsonb_build_object('id',j.id,'companyId',owner,'globalCause',j.company_id IS NULL,
    'changeKind',j.cause_kind,'subjectId',j.subject_id,'subjectVersion',j.subject_version,'phase',j.phase,
    'knownAt',j.known_at,'dueAt',j.due_at,'state',scoped_state,'discoveryComplete',j.discovery_complete,
    'progress',jsonb_build_object('totalTargets',total,'completedTargets',done,'remainingTargets',total-done),
    'errorCode',CASE WHEN scoped_state='FAILED_RETRYABLE' THEN
      CASE WHEN j.last_error='F12_RETRY_REQUIRED' THEN j.last_error ELSE 'F12_PROCESSING_ERROR' END ELSE NULL END,
    'originReferences',jsonb_strip_nulls(jsonb_build_object('code',grainledger.ledger_residue_public_scalar(j.origin->'code'),'reportId',grainledger.ledger_residue_public_scalar(j.origin->'reportId'),
     'eventId',grainledger.ledger_residue_public_scalar(j.origin->'eventId'),'eventKind',grainledger.ledger_residue_public_scalar(j.origin->'eventKind'),'operationId',grainledger.ledger_residue_public_scalar(j.origin->'operationId'),
     'effectiveFrom',grainledger.ledger_residue_public_scalar(j.origin->'effectiveFrom'),'effectiveTo',grainledger.ledger_residue_public_scalar(j.origin->'effectiveTo'),'confirmedAt',grainledger.ledger_residue_public_scalar(j.origin->'confirmedAt'))));
  END IF;
  WITH permitted AS (
   SELECT t.* FROM grainledger.ledger_residue_change_targets t WHERE t.change_id=target AND t.company_id=owner
    AND (filter_kind IS NULL OR t.target_kind=filter_kind) AND (target_id_filter IS NULL OR t.target_id=target_id_filter)
    AND (target_state IS NULL OR t.state=target_state)
  ) SELECT jsonb_build_object('items',coalesce((SELECT jsonb_agg(row ORDER BY id) FROM (
    SELECT t.id,jsonb_build_object('id',t.id,'changeId',t.change_id,'targetType',t.target_kind,'targetId',t.target_id,
     'relationshipBasis',t.relationship_basis,'state',t.state,'evaluationId',t.evaluation_id,
     'dependencyRevision',t.dependency_revision,'substanceIds',t.substance_ids,'discoveredAt',t.discovered_at,
     'updatedAt',t.updated_at,'errorCode',CASE WHEN t.last_error IS NULL THEN NULL ELSE 'F12_PROCESSING_ERROR' END,
     'latestRiskEventId',(SELECT r.id FROM grainledger.ledger_residue_risk_events r WHERE r.target_entry_id=t.id ORDER BY r.evaluated_at DESC,r.id LIMIT 1)) row
    FROM permitted t ORDER BY t.id LIMIT page_size OFFSET (page_no-1)*page_size) page),'[]'),
    'page',page_no,'pageSize',page_size,'total',(SELECT count(*) FROM permitted),
    'hasNext',(page_no::bigint*page_size)<(SELECT count(*) FROM permitted)) INTO result;
  RETURN result;
 END IF;
 IF action='risk' THEN
  IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_residue_risk_events r JOIN grainledger.ledger_residue_change_targets t ON t.id=r.target_entry_id
    WHERE r.id=target AND t.company_id=owner) THEN RAISE EXCEPTION 'Residue risk event not found' USING ERRCODE='P0002'; END IF;
  RETURN grainledger.ledger_residue_public_risk(target,actor_role='CustomerManager');
 END IF;
 WITH permitted AS (
  SELECT r.id,r.evaluated_at FROM grainledger.ledger_residue_risk_events r JOIN grainledger.ledger_residue_change_targets t ON t.id=r.target_entry_id
  WHERE t.company_id=owner AND (filter_kind IS NULL OR t.target_kind=filter_kind)
   AND (target_id_filter IS NULL OR t.target_id=target_id_filter)
   AND (filter_status IS NULL OR r.assessment_status=filter_status)
   AND (date_from IS NULL OR r.evaluated_at>=date_from) AND (date_to IS NULL OR r.evaluated_at<date_to)
 ) SELECT jsonb_build_object('items',coalesce((SELECT jsonb_agg(grainledger.ledger_residue_public_risk(id,actor_role='CustomerManager') ORDER BY evaluated_at DESC,id)
   FROM (SELECT * FROM permitted ORDER BY evaluated_at DESC,id LIMIT page_size OFFSET (page_no-1)*page_size) page),'[]'),
   'page',page_no,'pageSize',page_size,'total',(SELECT count(*) FROM permitted),
   'hasNext',(page_no::bigint*page_size)<(SELECT count(*) FROM permitted)) INTO result;
 RETURN result;
END $$;
REVOKE ALL ON FUNCTION grainledger.ledger_residue_public_scalar(jsonb),grainledger.ledger_residue_public_assessment(jsonb,boolean),grainledger.ledger_residue_public_risk(uuid,boolean) FROM PUBLIC;
-- CREATE OR REPLACE retains the existing restricted read function grant.
CREATE INDEX ledger_residue_risks_time ON grainledger.ledger_residue_risk_events(evaluated_at DESC,id);
