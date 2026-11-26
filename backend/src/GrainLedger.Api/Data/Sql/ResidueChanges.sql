CREATE SEQUENCE grainledger.ledger_residue_change_serial;

CREATE TABLE grainledger.ledger_residue_changes (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 sequence_no bigint NOT NULL DEFAULT nextval('grainledger.ledger_residue_change_serial') UNIQUE,
 company_id uuid REFERENCES grainledger.identity_organizations("Id"),
 cause_kind text NOT NULL CHECK (cause_kind IN ('REPORT_SELECTION','SOURCE_GROUP','PROFILE_PUBLISHED','PROFILE_RETIRED','POLICY_ADOPTION','PROCESSING_COMMITTED')),
 subject_id uuid NOT NULL CHECK (subject_id <> '00000000-0000-0000-0000-000000000000'),
 subject_version text NOT NULL CHECK (length(subject_version) BETWEEN 1 AND 100),
 phase text NOT NULL DEFAULT 'PUBLICATION' CHECK (phase IN ('PUBLICATION','EFFECTIVE_FROM','EFFECTIVE_TO')),
 actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
 origin jsonb NOT NULL CHECK (jsonb_typeof(origin)='object'),
 substance_ids uuid[] NOT NULL DEFAULT '{}',
 root_lot_ids uuid[] NOT NULL DEFAULT '{}',
 known_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 due_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 state text NOT NULL DEFAULT 'PENDING' CHECK (state IN ('PENDING','PROCESSING','COMPLETED','FAILED_RETRYABLE')),
 lease_id uuid,
 lease_until timestamptz,
 generation integer NOT NULL DEFAULT 0 CHECK (generation >= 0),
 attempts integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
 discovery_complete boolean NOT NULL DEFAULT false,
 discovery_cursor jsonb NOT NULL DEFAULT '{}' CHECK (jsonb_typeof(discovery_cursor)='object'),
 last_error text CHECK (length(last_error) <= 2000),
 updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 UNIQUE NULLS NOT DISTINCT (cause_kind,subject_id,subject_version,company_id,phase),
 CHECK (company_id IS NOT NULL OR cause_kind IN ('PROFILE_PUBLISHED','PROFILE_RETIRED')),
 CHECK (array_position(substance_ids,'00000000-0000-0000-0000-000000000000'::uuid) IS NULL AND array_position(substance_ids,NULL) IS NULL),
 CHECK (array_position(root_lot_ids,'00000000-0000-0000-0000-000000000000'::uuid) IS NULL AND array_position(root_lot_ids,NULL) IS NULL),
 CHECK ((state='PROCESSING' AND lease_id IS NOT NULL AND lease_until IS NOT NULL) OR
        (state<>'PROCESSING' AND lease_id IS NULL AND lease_until IS NULL)),
 CHECK (state<>'COMPLETED' OR discovery_complete)
);
CREATE INDEX ledger_residue_changes_due ON grainledger.ledger_residue_changes(due_at,sequence_no)
 WHERE state IN ('PENDING','PROCESSING','FAILED_RETRYABLE');
CREATE INDEX ledger_residue_changes_company ON grainledger.ledger_residue_changes(company_id,sequence_no DESC);

CREATE TABLE grainledger.ledger_residue_change_targets (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 change_id uuid NOT NULL REFERENCES grainledger.ledger_residue_changes(id),
 company_id uuid NOT NULL REFERENCES grainledger.identity_organizations("Id"),
 target_kind text NOT NULL CHECK (target_kind IN ('LOT','PLAN','CONTAINER')),
 target_id uuid NOT NULL CHECK (target_id <> '00000000-0000-0000-0000-000000000000'),
 context_key text NOT NULL DEFAULT '' CHECK (length(context_key) <= 300),
 relationship_basis text NOT NULL CHECK (relationship_basis IN ('MATERIAL','PLANNED_ALLOCATION','ACTUAL_LOADING')),
 state text NOT NULL DEFAULT 'PENDING' CHECK (state IN ('PENDING','PROCESSING','COMPLETED','FAILED_RETRYABLE')),
 dependency_revision text,
 evaluation_id uuid REFERENCES grainledger.ledger_residue_evaluations(id),
 last_error text CHECK (length(last_error) <= 2000),
 discovered_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 UNIQUE (change_id,target_kind,target_id,context_key)
);
CREATE INDEX ledger_residue_targets_company ON grainledger.ledger_residue_change_targets(company_id,change_id,id);
CREATE INDEX ledger_residue_targets_entity ON grainledger.ledger_residue_change_targets(company_id,target_kind,target_id);

CREATE TABLE grainledger.ledger_residue_risk_events (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 target_entry_id uuid NOT NULL REFERENCES grainledger.ledger_residue_change_targets(id),
 dependency_revision text NOT NULL CHECK (length(dependency_revision) BETWEEN 1 AND 200),
 assessment_status text NOT NULL CHECK (assessment_status IN ('COMPLETE','UNRESOLVED','PASS','FAIL','INCOMPLETE')),
 snapshot_at timestamptz NOT NULL,
 evaluated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 evaluation_id uuid REFERENCES grainledger.ledger_residue_evaluations(id),
 snapshot jsonb NOT NULL CHECK (jsonb_typeof(snapshot)='object'),
 UNIQUE (target_entry_id,dependency_revision)
);
CREATE INDEX ledger_residue_risks_target ON grainledger.ledger_residue_risk_events(target_entry_id,evaluated_at DESC,id);

CREATE FUNCTION grainledger.ledger_residue_change_guard() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,grainledger,pg_temp AS $$
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Residue change history cannot be deleted' USING ERRCODE='23514'; END IF;
 IF TG_TABLE_NAME='ledger_residue_changes' AND
   (to_jsonb(NEW)-ARRAY['state','lease_id','lease_until','generation','attempts','discovery_complete','discovery_cursor','last_error','updated_at','due_at'])
   IS DISTINCT FROM
   (to_jsonb(OLD)-ARRAY['state','lease_id','lease_until','generation','attempts','discovery_complete','discovery_cursor','last_error','updated_at','due_at']) THEN
  RAISE EXCEPTION 'Residue change cause is immutable' USING ERRCODE='23514';
 END IF;
 IF TG_TABLE_NAME='ledger_residue_change_targets' AND
   (to_jsonb(NEW)-ARRAY['state','dependency_revision','evaluation_id','last_error','updated_at'])
   IS DISTINCT FROM
   (to_jsonb(OLD)-ARRAY['state','dependency_revision','evaluation_id','last_error','updated_at']) THEN
  RAISE EXCEPTION 'Residue target identity is immutable' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER residue_change_guard BEFORE UPDATE OR DELETE ON grainledger.ledger_residue_changes
 FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_residue_change_guard();
CREATE TRIGGER residue_target_guard BEFORE UPDATE OR DELETE ON grainledger.ledger_residue_change_targets
 FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_residue_change_guard();
CREATE TRIGGER residue_risk_immutable BEFORE UPDATE OR DELETE ON grainledger.ledger_residue_risk_events
 FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE TRIGGER residue_change_no_truncate BEFORE TRUNCATE ON grainledger.ledger_residue_changes
 FOR EACH STATEMENT EXECUTE FUNCTION grainledger.ledger_immutable();
CREATE TRIGGER residue_target_no_truncate BEFORE TRUNCATE ON grainledger.ledger_residue_change_targets
 FOR EACH STATEMENT EXECUTE FUNCTION grainledger.ledger_immutable();
CREATE TRIGGER residue_risk_no_truncate BEFORE TRUNCATE ON grainledger.ledger_residue_risk_events
 FOR EACH STATEMENT EXECUTE FUNCTION grainledger.ledger_immutable();

CREATE FUNCTION grainledger.ledger_residue_target_guard() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE actual_owner uuid; change_owner uuid; evaluation_lot uuid;
BEGIN
 SELECT company_id INTO change_owner FROM grainledger.ledger_residue_changes WHERE id=NEW.change_id;
 IF change_owner IS NOT NULL AND change_owner<>NEW.company_id THEN
  RAISE EXCEPTION 'Residue change owner mismatch' USING ERRCODE='23514'; END IF;
 IF NEW.target_kind='LOT' THEN SELECT owner_id INTO actual_owner FROM grainledger.ledger_lots WHERE id=NEW.target_id;
 ELSIF NEW.target_kind='PLAN' THEN SELECT company_id INTO actual_owner FROM grainledger.exports_blend_plan_drafts WHERE id=NEW.target_id;
 ELSE SELECT company_id INTO actual_owner FROM grainledger.exports_containers WHERE id=NEW.target_id;
 END IF;
 IF actual_owner IS DISTINCT FROM NEW.company_id THEN
  RAISE EXCEPTION 'Residue target owner mismatch' USING ERRCODE='23514'; END IF;
 IF NEW.target_kind='LOT' AND NEW.relationship_basis<>'MATERIAL' OR
    NEW.target_kind='PLAN' AND NEW.relationship_basis<>'PLANNED_ALLOCATION' OR
    NEW.target_kind='CONTAINER' AND NEW.relationship_basis<>'ACTUAL_LOADING' THEN
  RAISE EXCEPTION 'Residue target relationship mismatch' USING ERRCODE='23514'; END IF;
 IF NEW.evaluation_id IS NOT NULL THEN
  SELECT lot_id INTO evaluation_lot FROM grainledger.ledger_residue_evaluations WHERE id=NEW.evaluation_id;
  IF NEW.target_kind<>'LOT' OR evaluation_lot IS DISTINCT FROM NEW.target_id THEN
   RAISE EXCEPTION 'Residue evaluation target mismatch' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER residue_target_owner BEFORE INSERT OR UPDATE ON grainledger.ledger_residue_change_targets
 FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_residue_target_guard();

CREATE FUNCTION grainledger.ledger_residue_risk_guard() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE t grainledger.ledger_residue_change_targets; evaluation_lot uuid;
BEGIN
 SELECT * INTO t FROM grainledger.ledger_residue_change_targets WHERE id=NEW.target_entry_id;
 IF t.target_kind='LOT' AND NEW.assessment_status NOT IN ('COMPLETE','UNRESOLVED') OR
    t.target_kind<>'LOT' AND NEW.assessment_status NOT IN ('PASS','FAIL','INCOMPLETE') THEN
  RAISE EXCEPTION 'Residue assessment context mismatch' USING ERRCODE='23514'; END IF;
 IF NEW.evaluation_id IS NOT NULL THEN
  SELECT lot_id INTO evaluation_lot FROM grainledger.ledger_residue_evaluations WHERE id=NEW.evaluation_id;
  IF t.target_kind<>'LOT' OR evaluation_lot IS DISTINCT FROM t.target_id THEN
   RAISE EXCEPTION 'Residue evaluation target mismatch' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER residue_risk_context BEFORE INSERT ON grainledger.ledger_residue_risk_events
 FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_residue_risk_guard();

CREATE FUNCTION grainledger.ledger_residue_change_claim(token uuid, batch_size integer, lease_seconds integer)
RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE result jsonb; claimed_at timestamptz:=clock_timestamp();
BEGIN
 IF token IS NULL OR token='00000000-0000-0000-0000-000000000000' OR
    batch_size IS NULL OR batch_size NOT BETWEEN 1 AND 100 OR
    lease_seconds IS NULL OR lease_seconds NOT BETWEEN 5 AND 300 THEN
  RAISE EXCEPTION 'Invalid residue job claim' USING ERRCODE='22023'; END IF;
 WITH candidates AS (
  SELECT id FROM grainledger.ledger_residue_changes
  WHERE due_at<=claimed_at AND (state IN ('PENDING','FAILED_RETRYABLE') OR
    state='PROCESSING' AND lease_until<=claimed_at)
  ORDER BY due_at,sequence_no FOR UPDATE SKIP LOCKED LIMIT batch_size
 ), updated AS (
  UPDATE grainledger.ledger_residue_changes j SET state='PROCESSING',lease_id=token,
   lease_until=claimed_at+make_interval(secs=>lease_seconds),generation=generation+1,
   attempts=attempts+1,updated_at=claimed_at
  FROM candidates c WHERE j.id=c.id RETURNING j.*
 ) SELECT coalesce(jsonb_agg(to_jsonb(updated) ORDER BY sequence_no),'[]'::jsonb) INTO result FROM updated;
 RETURN result;
END $$;

CREATE FUNCTION grainledger.ledger_residue_change_read(a uuid, action text, target uuid, p jsonb)
RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE owner uuid; result jsonb; page_no integer:=coalesce((p->>'page')::integer,1);
 page_size integer:=coalesce((p->>'pageSize')::integer,25);
BEGIN
 SELECT u."OrganizationId" INTO owner FROM grainledger.identity_users u
 JOIN grainledger.identity_organizations o ON o."Id"=u."OrganizationId"
 WHERE u."Id"=a AND u."IsActive" AND u."Role" IN ('CustomerManager','ExportStaff') AND o."Kind"='Customer';
 IF owner IS NULL THEN RAISE EXCEPTION 'Residue change access denied' USING ERRCODE='42501'; END IF;
 IF action NOT IN ('change','targets','risks','risk') OR page_no NOT BETWEEN 1 AND 1000000 OR page_size NOT BETWEEN 1 AND 100 OR
    jsonb_typeof(p) IS DISTINCT FROM 'object' OR EXISTS(
     SELECT 1 FROM jsonb_object_keys(p) k WHERE k NOT IN ('page','pageSize','targetKind','targetId','assessmentStatus','from','to')) THEN
  RAISE EXCEPTION 'Invalid residue change filters' USING ERRCODE='22023'; END IF;
 IF p ? 'targetKind' AND p->>'targetKind' NOT IN ('LOT','PLAN','CONTAINER') OR
    p ? 'assessmentStatus' AND p->>'assessmentStatus' NOT IN ('COMPLETE','UNRESOLVED','PASS','FAIL','INCOMPLETE') THEN
  RAISE EXCEPTION 'Invalid residue change filters' USING ERRCODE='22023'; END IF;
 IF action IN ('change','targets') THEN
  -- Global policy parents are internal. Only per-company change records are public.
  IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_residue_changes WHERE id=target AND company_id=owner) THEN
   RAISE EXCEPTION 'Residue change not found' USING ERRCODE='P0002'; END IF;
  IF action='change' THEN
   SELECT to_jsonb(c)-ARRAY['lease_id','lease_until','discovery_cursor','origin','root_lot_ids'] INTO result
   FROM grainledger.ledger_residue_changes c WHERE id=target;
   RETURN result;
  END IF;
  SELECT jsonb_build_object('items',coalesce(jsonb_agg(row ORDER BY id),'[]'::jsonb),'page',page_no,'pageSize',page_size,
   'total',(SELECT count(*) FROM grainledger.ledger_residue_change_targets WHERE change_id=target AND company_id=owner)) INTO result
  FROM (SELECT id,to_jsonb(t)-'last_error' row FROM grainledger.ledger_residue_change_targets t
    WHERE change_id=target AND company_id=owner ORDER BY id LIMIT page_size OFFSET (page_no-1)*page_size) rows;
  RETURN result;
 END IF;
 WITH permitted AS (
  SELECT r.id,r.evaluated_at,jsonb_build_object('id',r.id,'changeId',t.change_id,'targetType',t.target_kind,
   'targetId',t.target_id,'relationshipBasis',t.relationship_basis,'assessmentStatus',r.assessment_status,
   'snapshotAt',r.snapshot_at,'evaluatedAt',r.evaluated_at,'evaluationId',r.evaluation_id,
   'dependencyRevision',r.dependency_revision,'assessment',jsonb_build_object(
    'findings',r.snapshot->'findings','marketId',r.snapshot->'marketId','commodityId',r.snapshot->'commodityId',
    'mrlVersionId',r.snapshot->'mrlVersionId','marginPolicyId',r.snapshot->'marginPolicyId',
    'results',r.snapshot->'results','historicalDecisionId',r.snapshot->'historicalDecisionId',
    'historicalDossierId',r.snapshot->'historicalDossierId')) row
  FROM grainledger.ledger_residue_risk_events r JOIN grainledger.ledger_residue_change_targets t ON t.id=r.target_entry_id
  WHERE t.company_id=owner AND (action<>'risk' OR r.id=target)
   AND (NOT p ? 'targetKind' OR t.target_kind=p->>'targetKind')
   AND (NOT p ? 'targetId' OR t.target_id=(p->>'targetId')::uuid)
   AND (NOT p ? 'assessmentStatus' OR r.assessment_status=p->>'assessmentStatus')
   AND (NOT p ? 'from' OR r.evaluated_at>=(p->>'from')::timestamptz)
   AND (NOT p ? 'to' OR r.evaluated_at<(p->>'to')::timestamptz)
 ) SELECT CASE WHEN action='risk' THEN (SELECT row FROM permitted)
 ELSE jsonb_build_object('items',coalesce((SELECT jsonb_agg(row ORDER BY evaluated_at DESC,id)
  FROM (SELECT * FROM permitted ORDER BY evaluated_at DESC,id LIMIT page_size OFFSET (page_no-1)*page_size) page),'[]'::jsonb),
  'page',page_no,'pageSize',page_size,'total',(SELECT count(*) FROM permitted)) END INTO result;
 IF result IS NULL THEN RAISE EXCEPTION 'Residue risk event not found' USING ERRCODE='P0002'; END IF;
 RETURN result;
END $$;

REVOKE ALL ON grainledger.ledger_residue_changes,grainledger.ledger_residue_change_targets,grainledger.ledger_residue_risk_events FROM PUBLIC;
REVOKE ALL ON SEQUENCE grainledger.ledger_residue_change_serial FROM PUBLIC;
REVOKE ALL ON FUNCTION grainledger.ledger_residue_change_guard(),grainledger.ledger_residue_target_guard(),grainledger.ledger_residue_risk_guard(),
 grainledger.ledger_residue_change_claim(uuid,integer,integer),grainledger.ledger_residue_change_read(uuid,text,uuid,jsonb) FROM PUBLIC;
