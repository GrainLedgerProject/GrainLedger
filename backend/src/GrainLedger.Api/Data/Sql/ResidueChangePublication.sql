-- Journal at the persistence boundary so automatic/manual bindings and batch
-- publishers share the caller's transaction, including rollback and replay.
ALTER TABLE grainledger.ledger_residue_changes DROP CONSTRAINT ledger_residue_changes_cause_kind_check;
ALTER TABLE grainledger.ledger_residue_changes ADD CONSTRAINT ledger_residue_changes_cause_kind_check
 CHECK (cause_kind IN ('REPORT_SELECTION','SOURCE_GROUP','PROFILE_PUBLISHED','PROFILE_RETIRED',
                      'POLICY_ADOPTION','PROCESSING_COMMITTED','MATERIAL_CHANGED'));

CREATE FUNCTION grainledger.ledger_residue_publication_journal() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE company uuid; actor uuid; subject uuid; version text; cause text;
 substances uuid[]; roots uuid[]:='{}'; sources uuid[]:='{}'; origin jsonb;
 effective_from timestamptz; effective_to timestamptz; known timestamptz:=clock_timestamp();
 code text;
BEGIN
 -- Publishers already hold this lock. Retain lock order for all database paths.
 PERFORM pg_advisory_xact_lock(714928352);
 IF TG_TABLE_NAME='catalog_residue_rule_retirements' THEN
  SELECT r.substance_id,r.version_no::text INTO STRICT subject,version
    FROM grainledger.catalog_residue_rule_versions r WHERE r.id=NEW.profile_id;
  substances:=ARRAY[subject]; subject:=NEW.profile_id;
  company:=NEW.company_id; actor:=NEW.retired_by; cause:='PROFILE_RETIRED'; code:=NEW.code;
 ELSE
  company:=NEW.company_id; actor:=NEW.published_by; subject:=NEW.id;
  effective_from:=NEW.effective_from; effective_to:=NEW.effective_to;
  IF TG_TABLE_NAME='capture_residue_report_selections' THEN
   cause:='REPORT_SELECTION'; version:='1'; substances:=ARRAY[NEW.substance_id]; sources:=ARRAY[NEW.source_id];
  ELSIF TG_TABLE_NAME='capture_residue_source_group_versions' THEN
   cause:='SOURCE_GROUP'; version:=NEW.version_no::text; substances:=ARRAY[NEW.substance_id]; sources:=NEW.source_ids; code:=NEW.code;
  ELSIF TG_TABLE_NAME='catalog_residue_rule_versions' THEN
   IF NEW.payload->>'Kind' IS DISTINCT FROM 'FLOW' OR NEW.payload->>'SchemaVersion' IS DISTINCT FROM '2' THEN RETURN NEW; END IF;
   cause:='PROFILE_PUBLISHED'; version:=NEW.version_no::text; substances:=ARRAY[NEW.substance_id]; code:=NEW.code;
  ELSE
   cause:='POLICY_ADOPTION'; version:=NEW.version_no::text; code:=NEW.rule_code;
   SELECT coalesce(array_agg(DISTINCT r.substance_id ORDER BY r.substance_id),'{}') INTO substances
    FROM grainledger.catalog_residue_rule_versions r WHERE r.code=NEW.rule_code
     AND (r.company_id IS NULL OR r.company_id=company)
     AND r.payload->>'Kind'='FLOW' AND r.payload->>'SchemaVersion'='2';
  END IF;
 END IF;
 SELECT coalesce(array_agg(DISTINCT s.lot_id ORDER BY s.lot_id),'{}') INTO roots
  FROM grainledger.capture_source_lots s WHERE s.company_id=company AND s.id=ANY(sources);
 origin:=jsonb_strip_nulls(jsonb_build_object('table',TG_TABLE_NAME,'subjectId',subject,
     'code',code,'sourceIds',to_jsonb(sources),'effectiveFrom',effective_from,'effectiveTo',effective_to));
 -- Profile/adoption roots are resolved from their scope by the worker, not from
 -- current stock or an arbitrarily capped trace page. Global profile jobs stay internal.
 INSERT INTO grainledger.ledger_residue_changes(company_id,cause_kind,subject_id,subject_version,phase,
     actor_id,origin,substance_ids,root_lot_ids,known_at,due_at)
 SELECT company,cause,subject,version,s.phase,actor,origin,substances,roots,known,s.due
 FROM (VALUES ('PUBLICATION',known),('EFFECTIVE_FROM',effective_from),('EFFECTIVE_TO',effective_to)) s(phase,due)
 WHERE s.phase='PUBLICATION' OR s.due>known;
 RETURN NEW;
END $$;

CREATE TRIGGER residue_selection_journal AFTER INSERT ON grainledger.capture_residue_report_selections
 FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_residue_publication_journal();
CREATE TRIGGER residue_group_journal AFTER INSERT ON grainledger.capture_residue_source_group_versions
 FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_residue_publication_journal();
CREATE TRIGGER residue_profile_journal AFTER INSERT ON grainledger.catalog_residue_rule_versions
 FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_residue_publication_journal();
CREATE TRIGGER residue_adoption_journal AFTER INSERT ON grainledger.catalog_residue_policy_setting_versions
 FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_residue_publication_journal();
CREATE TRIGGER residue_retirement_journal AFTER INSERT ON grainledger.catalog_residue_rule_retirements
 FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_residue_publication_journal();

CREATE FUNCTION grainledger.ledger_residue_topology_journal() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE scope record; roots uuid[];
BEGIN
 PERFORM pg_advisory_xact_lock(714928352);
 -- Deferred execution sees outputs and physical edges inserted after the event.
 -- Include exhausted inputs and zero-accounting-share physical dependencies.
 FOR scope IN
  WITH affected AS (
   SELECT l.id,l.owner_id FROM grainledger.ledger_lots l
    WHERE l.id=NEW.lot_id OR (NEW.operation_id IS NOT NULL AND l.operation_id=NEW.operation_id)
   UNION
   SELECT l.id,l.owner_id FROM grainledger.ledger_custody_edges e
    JOIN grainledger.ledger_lots l ON l.id IN (e.input_lot_id,e.output_lot_id) WHERE e.event_id=NEW.id
   UNION
   SELECT l.id,l.owner_id FROM jsonb_array_elements(coalesce(NEW.details->'resolvedInputs','[]')) i
    JOIN grainledger.ledger_lots l ON l.id=(i->>'lotId')::uuid
   UNION
   SELECT l.id,l.owner_id FROM jsonb_array_elements(coalesce(NEW.details->'content'->'inputs','[]')) i
    JOIN grainledger.ledger_lots l ON l.id=(i->>'lotId')::uuid
  ) SELECT owner_id,array_agg(id ORDER BY id) AS roots FROM affected GROUP BY owner_id
 LOOP
  roots:=scope.roots;
  INSERT INTO grainledger.ledger_residue_changes(company_id,cause_kind,subject_id,subject_version,actor_id,
    origin,substance_ids,root_lot_ids)
  VALUES(scope.owner_id,CASE WHEN NEW.kind IN ('DRYING','MILLING','MERGE','BLENDING','BAGGING')
    THEN 'PROCESSING_COMMITTED' ELSE 'MATERIAL_CHANGED' END,NEW.id,'1',NEW.actor_id,
    jsonb_strip_nulls(jsonb_build_object('table','ledger_events','eventId',NEW.id,'eventKind',NEW.kind,
       'operationId',NEW.operation_id)), '{}',roots);
 END LOOP;
 RETURN NEW;
END $$;
CREATE CONSTRAINT TRIGGER residue_material_journal AFTER INSERT ON grainledger.ledger_events
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
 WHEN (NEW.kind IN ('RECEIPT','DRYING','MILLING','MERGE','BLENDING','BAGGING','POOL_INGRESS','POOL_WITHDRAW','PROCESSING_COMPENSATION'))
 EXECUTE FUNCTION grainledger.ledger_residue_topology_journal();

-- Neither runtime nor PUBLIC may enqueue arbitrary causes or impersonate an actor.
-- Trigger authority only borrows the transaction of an already authorized publisher.
REVOKE ALL ON FUNCTION grainledger.ledger_residue_publication_journal(),grainledger.ledger_residue_topology_journal() FROM PUBLIC;
