SELECT pg_advisory_xact_lock(714928352);

-- System evaluations have explicit job provenance; no synthetic user/session.
ALTER TABLE grainledger.ledger_residue_evaluations ALTER COLUMN actor_id DROP NOT NULL;
ALTER TABLE grainledger.ledger_residue_evaluations ADD COLUMN change_target_id uuid REFERENCES grainledger.ledger_residue_change_targets(id),
 ADD COLUMN dependency_revision text;
ALTER TABLE grainledger.ledger_residue_evaluations ADD CONSTRAINT residue_evaluation_origin CHECK (
 (actor_id IS NOT NULL AND change_target_id IS NULL AND dependency_revision IS NULL) OR
 (actor_id IS NULL AND change_target_id IS NOT NULL AND dependency_revision IS NOT NULL));
CREATE UNIQUE INDEX residue_system_evaluation_revision ON grainledger.ledger_residue_evaluations(change_target_id,dependency_revision)
 WHERE change_target_id IS NOT NULL;
ALTER TABLE grainledger.ledger_residue_change_targets ADD COLUMN substance_ids uuid[] NOT NULL DEFAULT '{}';
CREATE TABLE grainledger.ledger_residue_frontier (
 change_id uuid NOT NULL REFERENCES grainledger.ledger_residue_changes(id),
 lot_id uuid NOT NULL REFERENCES grainledger.ledger_lots(id),
 child_after uuid, expanded boolean NOT NULL DEFAULT false, PRIMARY KEY(change_id,lot_id));
CREATE TABLE grainledger.ledger_residue_current (
 lot_id uuid NOT NULL REFERENCES grainledger.ledger_lots(id),
 panel text NOT NULL, evaluation_id uuid NOT NULL REFERENCES grainledger.ledger_residue_evaluations(id),
 snapshot_at timestamptz NOT NULL, dependency_revision text NOT NULL, PRIMARY KEY(lot_id,panel));
REVOKE ALL ON grainledger.ledger_residue_frontier,grainledger.ledger_residue_current FROM PUBLIC;

ALTER TABLE grainledger.ledger_residue_changes DROP CONSTRAINT ledger_residue_changes_cause_kind_check;
ALTER TABLE grainledger.ledger_residue_changes ADD CONSTRAINT ledger_residue_changes_cause_kind_check CHECK
 (cause_kind IN ('REPORT_SELECTION','SOURCE_GROUP','PROFILE_PUBLISHED','PROFILE_RETIRED','POLICY_ADOPTION',
 'PROCESSING_COMMITTED','MATERIAL_CHANGED','REPORT_CONFIRMED'));

-- A confirmed retest can make an old selection conflicting without publishing a
-- replacement selection. Associations are visible only at the end of confirmation.
CREATE FUNCTION grainledger.ledger_residue_confirmation_journal() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE roots uuid[]; substances uuid[];
BEGIN
 PERFORM pg_advisory_xact_lock(714928352);
 SELECT coalesce(array_agg(DISTINCT sl.lot_id ORDER BY sl.lot_id),'{}'),
        coalesce(array_agg(DISTINCT r.substance_id ORDER BY r.substance_id) FILTER(WHERE r.substance_id IS NOT NULL),'{}')
 INTO roots,substances FROM grainledger.capture_lab_confirmed_associations a
 JOIN grainledger.capture_samples s ON s.id=a.sample_id AND s.company_id=NEW.company_id
 JOIN grainledger.capture_source_lots sl ON sl.id=s.source_lot_id AND sl.company_id=NEW.company_id
 JOIN grainledger.capture_lab_results r ON r.result_set_id=a.result_set_id AND r.company_id=NEW.company_id
 WHERE a.confirmation_id=NEW.id AND a.company_id=NEW.company_id;
 IF cardinality(substances)>0 THEN
 INSERT INTO grainledger.ledger_residue_changes(company_id,cause_kind,subject_id,subject_version,actor_id,origin,
  substance_ids,root_lot_ids,due_at)
 VALUES(NEW.company_id,'REPORT_CONFIRMED',NEW.id,'1',NEW.confirmed_by,
  jsonb_build_object('table','capture_lab_report_confirmations','reportId',NEW.report_id,'confirmedAt',NEW.confirmed_at),
  substances,roots,greatest(clock_timestamp(),NEW.confirmed_at));
 END IF;
 RETURN NEW;
END $$;
CREATE CONSTRAINT TRIGGER residue_confirmation_journal AFTER INSERT ON grainledger.capture_lab_report_confirmations
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_residue_confirmation_journal();

CREATE FUNCTION grainledger.ledger_residue_graph_core(target uuid) RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE result jsonb;
BEGIN
    WITH RECURSIVE closure(id) AS (
        SELECT target
        UNION
        SELECT parent.id FROM closure c JOIN grainledger.ledger_lots l ON l.id=c.id
        CROSS JOIN LATERAL (
            SELECT edge.input_lot_id id FROM grainledger.ledger_custody_edges edge WHERE edge.output_lot_id=l.id
            UNION
            SELECT (input->>'lotId')::uuid FROM grainledger.ledger_events e
            CROSS JOIN LATERAL jsonb_array_elements(COALESCE(e.details->'resolvedInputs',e.details->'content'->'inputs','[]'::jsonb)) input
            WHERE e.operation_id=l.operation_id AND e.kind IN ('DRYING','MILLING','MERGE','BLENDING','BAGGING')
        ) parent
    )
    SELECT jsonb_agg(jsonb_build_object(
        'lotId',l.id,'companyId',l.owner_id,'commodityId',COALESCE(mapping.catalog_commodity_id,l.commodity_id),'stage',l.stage,
        'productIdentity',l.product_identity,'at',COALESCE(o.occurred_at,custody.at,l.created_at),
        'kind',COALESCE('CUSTODY_'||custody.action,o.kind,'SOURCE'),
        'operationId',l.operation_id,'eventId',COALESCE(custody.event_id,physical.id),
        'conditions',physical.details->'content'->>'residueConditions',
        'inconsistent',COALESCE(l.owner_id<>(SELECT owner_id FROM grainledger.ledger_lots WHERE id=target)
            OR l.operation_id IS NOT NULL AND (physical.id IS NULL OR physical.event_count<>1 OR o.status NOT IN ('COMMITTED','CORRECTED'))
            OR custody.event_count<>1 OR custody.event_id IS NOT NULL AND l.operation_id IS NOT NULL
            OR l.id<>target AND o.status='CORRECTED',false),
        'parents',COALESCE(custody.parents,physical.parents,'[]'::jsonb)
    ) ORDER BY l.id) INTO result
    FROM closure c JOIN grainledger.ledger_lots l ON l.id=c.id
    LEFT JOIN grainledger.ledger_operations o ON o.id=l.operation_id
    LEFT JOIN grainledger.ledger_catalog_commodities mapping ON mapping.ledger_commodity_id=l.commodity_id
    LEFT JOIN LATERAL (
        SELECT e.id,e.details,count(*) OVER () event_count,
            jsonb_agg(jsonb_build_object('lotId',input->>'lotId','quantityKg',input->>'quantityKg')) parents
        FROM grainledger.ledger_events e CROSS JOIN LATERAL jsonb_array_elements(COALESCE(e.details->'resolvedInputs',e.details->'content'->'inputs','[]'::jsonb)) input
        WHERE e.operation_id=l.operation_id AND e.kind IN ('DRYING','MILLING','MERGE','BLENDING','BAGGING')
        GROUP BY e.id,e.details ORDER BY e.id LIMIT 1
    ) physical ON true
    LEFT JOIN LATERAL (
        SELECT ce.event_id,ce.action,ce.created_at at,count(*) OVER () event_count,
            jsonb_agg(jsonb_build_object('lotId',edge.input_lot_id,'quantityKg',edge.quantity_kg::text)) parents
        FROM grainledger.ledger_custody_edges edge JOIN grainledger.ledger_custody_events ce ON ce.event_id=edge.event_id
        WHERE edge.output_lot_id=l.id GROUP BY ce.event_id,ce.action,ce.created_at
        ORDER BY ce.event_id LIMIT 1
    ) custody ON true;
    RETURN COALESCE(result,'[]'::jsonb);
END $$;

CREATE OR REPLACE FUNCTION grainledger.ledger_residue_graph(a uuid,target uuid) RETURNS jsonb
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
BEGIN
 IF NOT grainledger.ledger_residue_authorized(a,target) THEN
  RAISE EXCEPTION 'Residue lot access denied' USING ERRCODE='42501'; END IF;
 RETURN grainledger.ledger_residue_graph_core(target);
END $$;
CREATE OR REPLACE FUNCTION grainledger.ledger_residue_replay(a uuid, l uuid, evaluation uuid, p jsonb) RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE old grainledger.ledger_residue_evaluations;
BEGIN
    IF NOT grainledger.ledger_residue_authorized(a,l) THEN RAISE EXCEPTION 'Residue lot access denied' USING ERRCODE='42501'; END IF;
    SELECT * INTO old FROM grainledger.ledger_residue_evaluations WHERE id=evaluation;
    IF NOT FOUND THEN RETURN NULL; END IF;
    IF old.actor_id IS DISTINCT FROM a OR old.lot_id<>l OR old.request<>p THEN
        RAISE EXCEPTION 'Residue evaluation ID reused with different actor or payload' USING ERRCODE='23505';
    END IF;
    RETURN old.snapshot;
END $$;


-- A company/global journal vector plus temporal visibility and the actual graph.
-- Operational lease/retry/progress changes do not change the scientific basis.
CREATE FUNCTION grainledger.ledger_residue_basis(lot uuid,at_time timestamptz) RETURNS text
LANGUAGE sql STABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
 SELECT md5(jsonb_build_object('graph',grainledger.ledger_residue_graph_core(lot),
  'sequence',coalesce(max(c.sequence_no),0),
  'boundaries',coalesce(jsonb_agg(jsonb_build_array(c.id,
     (c.origin->>'effectiveFrom')::timestamptz<=at_time,
     (c.origin->>'effectiveTo')::timestamptz<=at_time,
     (c.origin->>'confirmedAt')::timestamptz<=at_time) ORDER BY c.id),'[]'))::text)
 FROM grainledger.ledger_residue_changes c
 WHERE c.company_id=(SELECT owner_id FROM grainledger.ledger_lots WHERE id=lot) OR c.company_id IS NULL
$$;

CREATE FUNCTION grainledger.ledger_residue_job_check(job uuid,token uuid,gen integer) RETURNS grainledger.ledger_residue_changes
LANGUAGE plpgsql STABLE SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE j grainledger.ledger_residue_changes;
BEGIN
 SELECT * INTO j FROM grainledger.ledger_residue_changes WHERE id=job;
 IF j.id IS NULL OR j.state<>'PROCESSING' OR j.lease_id IS DISTINCT FROM token OR j.generation IS DISTINCT FROM gen
  OR j.lease_until<=clock_timestamp() THEN RAISE EXCEPTION 'Residue job lease lost' USING ERRCODE='55000'; END IF;
 RETURN j;
END $$;

-- Only a claimed job can reach these operations. No actor/company arguments.
CREATE FUNCTION grainledger.ledger_residue_job(job uuid,token uuid,gen integer,action text,target uuid,p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE j grainledger.ledger_residue_changes; t grainledger.ledger_residue_change_targets;
 children uuid[]; seeds uuid[]; all_substances uuid[]; root_after uuid;
 f grainledger.ledger_residue_frontier; finished boolean; at_time timestamptz;
 revision text; evaluation uuid; snapshot jsonb; partition_row record; result jsonb; batch integer:=coalesce((p->>'batchSize')::integer,25);
BEGIN
 IF action IS NULL OR action NOT IN ('DISCOVER','NEXT','READ','APPEND','YIELD','RETRY','FINISH')
  OR jsonb_typeof(p) IS DISTINCT FROM 'object' OR batch NOT BETWEEN 1 AND 100 THEN
  RAISE EXCEPTION 'Invalid residue worker request' USING ERRCODE='22023'; END IF;
 IF action IN ('NEXT','READ') THEN
  j:=grainledger.ledger_residue_job_check(job,token,gen);
 ELSE
  IF current_setting('transaction_isolation')<>'read committed' THEN
   RAISE EXCEPTION 'Residue writes require READ COMMITTED' USING ERRCODE='23514'; END IF;
  PERFORM pg_advisory_xact_lock(714928352);
  PERFORM 1 FROM grainledger.ledger_residue_changes WHERE id=job FOR UPDATE;
  j:=grainledger.ledger_residue_job_check(job,token,gen);
 END IF;
 IF action='DISCOVER' THEN
  IF j.discovery_complete THEN RETURN jsonb_build_object('discoveryComplete',true); END IF;
  IF NOT j.discovery_cursor ? 'substances' THEN
   all_substances:=j.substance_ids;
   IF cardinality(all_substances)=0 THEN
    SELECT coalesce(array_agg(DISTINCT substance ORDER BY substance),'{}') INTO all_substances FROM (
     SELECT r.substance_id substance FROM grainledger.catalog_residue_rule_versions r
      JOIN grainledger.catalog_residue_policy_setting_versions s ON s.rule_code=r.code AND s.company_id=j.company_id
      WHERE r.company_id IS NULL OR r.company_id=j.company_id
     UNION SELECT substance_id FROM grainledger.capture_residue_report_selections WHERE company_id=j.company_id
     UNION SELECT substance_id FROM grainledger.capture_residue_source_group_versions WHERE company_id=j.company_id
     UNION SELECT substance_id FROM grainledger.capture_lab_results WHERE company_id=j.company_id AND substance_id IS NOT NULL
    ) a;
   END IF;
   j.discovery_cursor:=j.discovery_cursor||jsonb_build_object('substances',to_jsonb(all_substances));
  END IF;
  IF NOT coalesce((j.discovery_cursor->>'seedsDone')::boolean,false) THEN
   root_after:=(j.discovery_cursor->>'rootAfter')::uuid;
   SELECT coalesce(array_agg(id ORDER BY id),'{}') INTO seeds FROM (
    SELECT l.id FROM grainledger.ledger_lots l WHERE (root_after IS NULL OR l.id>root_after)
     AND (j.company_id IS NULL OR l.owner_id=j.company_id) AND (
      l.id=ANY(j.root_lot_ids) OR
      coalesce((j.origin->>'cutover')::boolean,false) OR
      j.cause_kind IN ('PROFILE_PUBLISHED','PROFILE_RETIRED','POLICY_ADOPTION') AND EXISTS(
       SELECT 1 FROM grainledger.catalog_residue_policy_setting_versions s
       WHERE s.company_id=l.owner_id AND s.rule_code=j.origin->>'code') OR
      j.cause_kind='REPORT_SELECTION' AND EXISTS(
       SELECT 1 FROM grainledger.capture_residue_report_selections s JOIN grainledger.capture_source_lots sl ON sl.id=s.source_id
       WHERE s.id=j.subject_id AND sl.lot_id=l.id AND sl.company_id=l.owner_id) OR
      j.cause_kind='SOURCE_GROUP' AND EXISTS(
       SELECT 1 FROM grainledger.capture_residue_source_group_versions g JOIN grainledger.capture_source_lots sl ON sl.id=ANY(g.source_ids)
       WHERE g.company_id=l.owner_id AND g.code=j.origin->>'code' AND sl.lot_id=l.id AND sl.company_id=g.company_id) OR
      j.cause_kind='REPORT_CONFIRMED' AND EXISTS(
       SELECT 1 FROM grainledger.capture_lab_confirmed_associations a JOIN grainledger.capture_samples s ON s.id=a.sample_id
        JOIN grainledger.capture_source_lots sl ON sl.id=s.source_lot_id
       WHERE a.confirmation_id=j.subject_id AND a.company_id=l.owner_id AND s.company_id=l.owner_id
        AND sl.company_id=l.owner_id AND sl.lot_id=l.id)
     ) ORDER BY l.id LIMIT batch
   ) root_page;
   INSERT INTO grainledger.ledger_residue_frontier(change_id,lot_id) SELECT job,unnest(seeds) ON CONFLICT DO NOTHING;
   j.discovery_cursor:=j.discovery_cursor||jsonb_build_object('seedsDone',cardinality(seeds)<batch);
   IF cardinality(seeds)>0 THEN j.discovery_cursor:=j.discovery_cursor||jsonb_build_object('rootAfter',seeds[cardinality(seeds)]); END IF;
  END IF;
  SELECT * INTO f FROM grainledger.ledger_residue_frontier WHERE change_id=job AND NOT expanded ORDER BY lot_id LIMIT 1;
  IF f.lot_id IS NOT NULL THEN
   SELECT coalesce(array_agg(id ORDER BY id),'{}') INTO children FROM (
    SELECT DISTINCT child.id FROM (
     SELECT e.output_lot_id id FROM grainledger.ledger_custody_edges e WHERE e.input_lot_id=f.lot_id
     UNION
     SELECT l.id FROM grainledger.ledger_events e
      CROSS JOIN LATERAL jsonb_array_elements(coalesce(e.details->'resolvedInputs',e.details->'content'->'inputs','[]')) i
      JOIN grainledger.ledger_lots l ON l.operation_id=e.operation_id
      WHERE e.kind IN ('DRYING','MILLING','MERGE','BLENDING','BAGGING') AND (i->>'lotId')::uuid=f.lot_id
    ) child JOIN grainledger.ledger_lots l ON l.id=child.id
    WHERE (f.child_after IS NULL OR child.id>f.child_after) AND (j.company_id IS NULL OR l.owner_id=j.company_id)
    ORDER BY child.id LIMIT batch
   ) child_page;
   INSERT INTO grainledger.ledger_residue_frontier(change_id,lot_id) SELECT job,unnest(children) ON CONFLICT DO NOTHING;
   UPDATE grainledger.ledger_residue_frontier SET expanded=cardinality(children)<batch,
     child_after=CASE WHEN cardinality(children)>0 THEN children[cardinality(children)] ELSE child_after END
    WHERE change_id=job AND lot_id=f.lot_id;
   SELECT coalesce(array_agg(value::uuid ORDER BY value::uuid),'{}') INTO all_substances
    FROM jsonb_array_elements_text(j.discovery_cursor->'substances');
   FOR partition_row IN SELECT (n-1)/200 panel_no,array_agg(substance ORDER BY substance) substances
     FROM unnest(all_substances) WITH ORDINALITY s(substance,n) GROUP BY (n-1)/200
   LOOP
    INSERT INTO grainledger.ledger_residue_change_targets(change_id,company_id,target_kind,target_id,context_key,relationship_basis,substance_ids)
     SELECT job,l.owner_id,'LOT',l.id,'panel:'||md5(partition_row.substances::text),'MATERIAL',partition_row.substances
     FROM grainledger.ledger_lots l WHERE l.id=f.lot_id ON CONFLICT DO NOTHING;
   END LOOP;
  END IF;
  finished:=coalesce((j.discovery_cursor->>'seedsDone')::boolean,false) AND NOT EXISTS(
    SELECT 1 FROM grainledger.ledger_residue_frontier WHERE change_id=job AND NOT expanded);
  UPDATE grainledger.ledger_residue_changes SET discovery_cursor=j.discovery_cursor,discovery_complete=finished,
    lease_until=clock_timestamp()+interval '60 seconds',updated_at=clock_timestamp() WHERE id=job;
  RETURN jsonb_build_object('discoveryComplete',finished);
 ELSIF action='NEXT' THEN
  SELECT coalesce(jsonb_agg(id ORDER BY id),'[]') INTO result FROM (
   SELECT id FROM grainledger.ledger_residue_change_targets WHERE change_id=job AND target_kind='LOT' AND state<>'COMPLETED'
    ORDER BY id LIMIT batch) page;
  RETURN result;
 ELSIF action IN ('READ','APPEND') THEN
  SELECT * INTO t FROM grainledger.ledger_residue_change_targets WHERE id=target AND change_id=job AND target_kind='LOT';
  IF t.id IS NULL THEN RAISE EXCEPTION 'Residue job target mismatch' USING ERRCODE='42501'; END IF;
  at_time:=clock_timestamp(); revision:=grainledger.ledger_residue_basis(t.target_id,at_time);
  IF action='READ' THEN RETURN jsonb_build_object('lotId',t.target_id,'companyId',t.company_id,'substanceIds',t.substance_ids,
    'graph',grainledger.ledger_residue_graph_core(t.target_id),'revision',revision,'snapshotAt',at_time); END IF;
  IF revision IS DISTINCT FROM p->>'revision' THEN RAISE EXCEPTION 'Residue basis changed' USING ERRCODE='40001'; END IF;
  snapshot:=p->'snapshot';
  IF snapshot->>'lotId' IS DISTINCT FROM t.target_id::text OR snapshot->>'companyId' IS DISTINCT FROM t.company_id::text
   OR snapshot->>'engineVersion' IS DISTINCT FROM 'residue-flow-as-received-3'
   OR snapshot->>'snapshotAt' IS NULL OR (snapshot->>'snapshotAt')::timestamptz<j.known_at
   OR (snapshot->>'snapshotAt')::timestamptz>at_time OR jsonb_typeof(snapshot->'results') IS DISTINCT FROM 'array'
   OR EXISTS(SELECT 1 FROM jsonb_array_elements(snapshot->'results') r
      WHERE coalesce(r->>'status','') NOT IN ('COMPLETE','UNRESOLVED')
       OR r->>'status'='COMPLETE' AND (coalesce(r->>'lower','') !~ '^[0-9]+(\.[0-9]+)?$'
         OR coalesce(r->>'upper','') !~ '^[0-9]+(\.[0-9]+)?$' OR r->>'unit' IS DISTINCT FROM 'mg/kg')
       OR r->>'status'='COMPLETE' AND CASE WHEN r->>'lower' ~ '^[0-9]+(\.[0-9]+)?$' AND r->>'upper' ~ '^[0-9]+(\.[0-9]+)?$'
          THEN (r->>'lower')::numeric>(r->>'upper')::numeric ELSE false END
       OR r->>'status'='UNRESOLVED' AND (r->>'lower' IS NOT NULL OR r->>'upper' IS NOT NULL))
   OR (SELECT array_agg((r->>'substanceId')::uuid ORDER BY (r->>'substanceId')::uuid) FROM jsonb_array_elements(snapshot->'results') r)
      IS DISTINCT FROM t.substance_ids THEN RAISE EXCEPTION 'Invalid system residue snapshot' USING ERRCODE='22023'; END IF;
  SELECT id INTO evaluation FROM grainledger.ledger_residue_evaluations WHERE change_target_id=t.id AND dependency_revision=revision;
  IF evaluation IS NULL THEN
   evaluation:=gen_random_uuid();
   snapshot:=snapshot||jsonb_build_object('id',evaluation,'persistedAt',at_time,'createdByKind','SYSTEM',
     'changeId',job,'targetEntryId',t.id,'dependencyRevision',revision);
   INSERT INTO grainledger.ledger_residue_evaluations(id,lot_id,actor_id,request,snapshot,persisted_at,change_target_id,dependency_revision)
    VALUES(evaluation,t.target_id,NULL,jsonb_build_object('substanceIds',t.substance_ids),snapshot,at_time,t.id,revision);
   INSERT INTO grainledger.ledger_residue_risk_events(target_entry_id,dependency_revision,assessment_status,snapshot_at,evaluation_id,snapshot)
    VALUES(t.id,revision,CASE WHEN EXISTS(SELECT 1 FROM jsonb_array_elements(snapshot->'results') r WHERE r->>'status'<>'COMPLETE')
      THEN 'UNRESOLVED' ELSE 'COMPLETE' END,(snapshot->>'snapshotAt')::timestamptz,evaluation,snapshot);
   INSERT INTO grainledger.ledger_residue_current(lot_id,panel,evaluation_id,snapshot_at,dependency_revision)
    VALUES(t.target_id,t.context_key,evaluation,(snapshot->>'snapshotAt')::timestamptz,revision)
    ON CONFLICT(lot_id,panel) DO UPDATE SET evaluation_id=excluded.evaluation_id,snapshot_at=excluded.snapshot_at,dependency_revision=excluded.dependency_revision
      WHERE excluded.snapshot_at>=ledger_residue_current.snapshot_at;
  END IF;
  UPDATE grainledger.ledger_residue_change_targets SET state='COMPLETED',evaluation_id=evaluation,
    dependency_revision=revision,last_error=NULL,updated_at=at_time WHERE id=t.id;
  UPDATE grainledger.ledger_residue_changes SET lease_until=clock_timestamp()+interval '60 seconds',updated_at=clock_timestamp() WHERE id=job;
  RETURN jsonb_build_object('evaluationId',evaluation);
 ELSIF action='FINISH' THEN
  finished:=j.discovery_complete AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_residue_change_targets WHERE change_id=job AND state<>'COMPLETED');
  IF finished THEN UPDATE grainledger.ledger_residue_changes SET state='COMPLETED',lease_id=NULL,lease_until=NULL,last_error=NULL,updated_at=clock_timestamp() WHERE id=job; END IF;
  RETURN jsonb_build_object('completed',finished);
 ELSE
  UPDATE grainledger.ledger_residue_changes SET state=CASE WHEN action='RETRY' THEN 'FAILED_RETRYABLE' ELSE 'PENDING' END,
   lease_id=NULL,lease_until=NULL,due_at=clock_timestamp()+CASE WHEN action='RETRY' THEN interval '5 seconds' ELSE interval '0 seconds' END,
   last_error=CASE WHEN action='RETRY' THEN 'F12_RETRY_REQUIRED' ELSE NULL END,updated_at=clock_timestamp() WHERE id=job;
  RETURN '{}'::jsonb;
 END IF;
END $$;
REVOKE ALL ON FUNCTION grainledger.ledger_residue_graph_core(uuid),grainledger.ledger_residue_basis(uuid,timestamptz),
 grainledger.ledger_residue_job_check(uuid,uuid,integer),grainledger.ledger_residue_job(uuid,uuid,integer,text,uuid,jsonb),
 grainledger.ledger_residue_confirmation_journal() FROM PUBLIC;

-- Cutover creates causes only; immutable publications/evaluations are untouched.
-- ON CONFLICT preserves already journaled subjects and their original known time.
WITH publications AS (
 SELECT company_id,'REPORT_SELECTION'::text kind,id subject,'1'::text version,published_by actor,
   jsonb_build_object('table','capture_residue_report_selections','subjectId',id,'sourceIds',jsonb_build_array(source_id),
     'effectiveFrom',effective_from,'effectiveTo',effective_to) origin,ARRAY[substance_id] substances,effective_from,effective_to
 FROM grainledger.capture_residue_report_selections WHERE payload->>'SchemaVersion'='2'
 UNION ALL SELECT company_id,'SOURCE_GROUP',id,version_no::text,published_by,
   jsonb_build_object('table','capture_residue_source_group_versions','subjectId',id,'code',code,'sourceIds',source_ids,
     'effectiveFrom',effective_from,'effectiveTo',effective_to),ARRAY[substance_id],effective_from,effective_to
 FROM grainledger.capture_residue_source_group_versions WHERE payload->>'SchemaVersion'='2'
 UNION ALL SELECT company_id,'PROFILE_PUBLISHED',id,version_no::text,published_by,
   jsonb_build_object('table','catalog_residue_rule_versions','subjectId',id,'code',code,
     'effectiveFrom',effective_from,'effectiveTo',effective_to),ARRAY[substance_id],effective_from,effective_to
 FROM grainledger.catalog_residue_rule_versions WHERE payload->>'Kind'='FLOW' AND payload->>'SchemaVersion'='2'
 UNION ALL SELECT s.company_id,'POLICY_ADOPTION',s.id,s.version_no::text,s.published_by,
   jsonb_build_object('table','catalog_residue_policy_setting_versions','subjectId',s.id,'code',s.rule_code,
     'effectiveFrom',s.effective_from,'effectiveTo',s.effective_to),
   ARRAY(SELECT DISTINCT r.substance_id FROM grainledger.catalog_residue_rule_versions r WHERE r.code=s.rule_code
     AND (r.company_id IS NULL OR r.company_id=s.company_id) ORDER BY r.substance_id),s.effective_from,s.effective_to
 FROM grainledger.catalog_residue_policy_setting_versions s
 UNION ALL SELECT t.company_id,'PROFILE_RETIRED',t.profile_id,r.version_no::text,t.retired_by,
   jsonb_build_object('table','catalog_residue_rule_retirements','subjectId',t.profile_id,'code',t.code),ARRAY[r.substance_id],NULL,NULL
 FROM grainledger.catalog_residue_rule_retirements t JOIN grainledger.catalog_residue_rule_versions r ON r.id=t.profile_id
)
INSERT INTO grainledger.ledger_residue_changes(company_id,cause_kind,subject_id,subject_version,actor_id,origin,substance_ids,phase,due_at)
 SELECT company_id,kind,subject,version,actor,origin,substances,t.phase,t.due
 FROM publications CROSS JOIN LATERAL (VALUES ('PUBLICATION',clock_timestamp()),
   ('EFFECTIVE_FROM',effective_from),('EFFECTIVE_TO',effective_to)) t(phase,due)
 WHERE t.phase='PUBLICATION' OR t.due>clock_timestamp() ON CONFLICT DO NOTHING;

INSERT INTO grainledger.ledger_residue_changes(company_id,cause_kind,subject_id,subject_version,actor_id,origin,substance_ids,due_at)
 SELECT c.company_id,'REPORT_CONFIRMED',c.id,'1',c.confirmed_by,
  jsonb_build_object('table','capture_lab_report_confirmations','reportId',c.report_id,'confirmedAt',c.confirmed_at),
  panel.ids,greatest(c.confirmed_at,clock_timestamp())
 FROM grainledger.capture_lab_report_confirmations c CROSS JOIN LATERAL (
  SELECT array_agg(DISTINCT r.substance_id ORDER BY r.substance_id) ids FROM grainledger.capture_lab_results r
  JOIN grainledger.capture_lab_result_sets s ON s.id=r.result_set_id AND s.company_id=c.company_id
  WHERE s.report_id=c.report_id AND r.company_id=c.company_id AND r.substance_id IS NOT NULL
 ) panel WHERE cardinality(panel.ids)>0 ON CONFLICT DO NOTHING;

INSERT INTO grainledger.ledger_residue_changes(company_id,cause_kind,subject_id,subject_version,actor_id,origin)
 SELECT owners.owner_id,'MATERIAL_CHANGED',event.id,'cutover-20261123',event.actor_id,
  jsonb_build_object('cutover',true,'migration','20261123020000_ResiduePropagation','eventId',event.id)
 FROM (SELECT DISTINCT owner_id FROM grainledger.ledger_lots) owners CROSS JOIN LATERAL (
  SELECT e.id,e.actor_id FROM grainledger.ledger_events e
  WHERE EXISTS(SELECT 1 FROM grainledger.ledger_lots l WHERE l.owner_id=owners.owner_id AND l.id=e.lot_id)
    OR EXISTS(SELECT 1 FROM grainledger.ledger_operations o WHERE o.owner_id=owners.owner_id AND o.id=e.operation_id)
  ORDER BY e.id LIMIT 1
 ) event ON CONFLICT DO NOTHING;
