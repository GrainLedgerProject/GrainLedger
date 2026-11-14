CREATE TABLE grainledger.catalog_residue_rule_retirements (
    profile_id uuid PRIMARY KEY REFERENCES grainledger.catalog_residue_rule_versions(id),
    code text NOT NULL,
    company_id uuid REFERENCES grainledger.identity_organizations("Id"),
    reason text NOT NULL CHECK (length(btrim(reason)) BETWEEN 1 AND 1000),
    retired_by uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
    retired_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE NULLS NOT DISTINCT (company_id, code)
);
CREATE TRIGGER catalog_retirement_immutable BEFORE UPDATE OR DELETE ON grainledger.catalog_residue_rule_retirements
    FOR EACH ROW EXECUTE FUNCTION grainledger.catalog_residue_immutable();
CREATE TRIGGER catalog_retirement_immutable_truncate BEFORE TRUNCATE ON grainledger.catalog_residue_rule_retirements
    FOR EACH STATEMENT EXECUTE FUNCTION grainledger.catalog_residue_immutable();

CREATE FUNCTION grainledger.catalog_retire_residue_rule(a uuid, profile uuid, why text) RETURNS timestamptz
    LANGUAGE plpgsql SECURITY DEFINER SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp' AS $$
DECLARE r grainledger.catalog_residue_rule_versions%ROWTYPE; retired timestamptz;
BEGIN
    PERFORM pg_advisory_xact_lock(714928352);
    SELECT * INTO r FROM grainledger.catalog_residue_rule_versions WHERE id=profile
        AND payload->>'Kind'='FLOW' AND payload->>'SchemaVersion'='2';
    IF NOT FOUND THEN RAISE EXCEPTION 'Profile not found' USING ERRCODE='P0002'; END IF;
    IF NOT EXISTS(SELECT 1 FROM grainledger.identity_users u JOIN grainledger.identity_organizations o ON o."Id"=u."OrganizationId"
        WHERE u."Id"=a AND u."IsActive" AND
        (r.company_id IS NULL AND u."Role"='Administrator' AND o."Kind"='Mill'
         OR r.company_id IS NOT NULL AND u."Role"='CustomerManager' AND o."Kind"='Customer' AND o."Id"=r.company_id)) THEN
        RAISE EXCEPTION 'Residue policy retirement denied' USING ERRCODE='42501';
    END IF;
    IF why IS NULL OR length(btrim(why)) NOT BETWEEN 1 AND 1000 THEN
        RAISE EXCEPTION 'Retirement reason required' USING ERRCODE='22023';
    END IF;
    INSERT INTO grainledger.catalog_residue_rule_retirements(profile_id,code,company_id,reason,retired_by)
    VALUES(profile,r.code,r.company_id,btrim(why),a) RETURNING retired_at INTO retired;
    RETURN retired;
END $$;
REVOKE ALL ON FUNCTION grainledger.catalog_retire_residue_rule(uuid,uuid,text) FROM PUBLIC;

-- Protected publishers already take the shared transaction lock. These guards also
-- prevent bypass through their SQL entry points.
CREATE FUNCTION grainledger.catalog_residue_retirement_guard() RETURNS trigger
    LANGUAGE plpgsql SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp' AS $$
DECLARE target_code text; target_company uuid;
BEGIN
    PERFORM pg_advisory_xact_lock(714928352);
    IF TG_TABLE_NAME='catalog_residue_rule_versions' THEN
        target_code:=NEW.code; target_company:=NEW.company_id;
    ELSE
        target_code:=NEW.rule_code;
        target_company:=CASE WHEN NEW.payload->>'Mode'='SYSTEM_DEFAULT' THEN NULL ELSE NEW.company_id END;
    END IF;
    IF EXISTS(SELECT 1 FROM grainledger.catalog_residue_rule_retirements
        WHERE code=target_code AND company_id IS NOT DISTINCT FROM target_company) THEN
        RAISE EXCEPTION 'Residue profile series is retired' USING ERRCODE='23505';
    END IF;
    RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION grainledger.catalog_residue_retirement_guard() FROM PUBLIC;
CREATE TRIGGER catalog_residue_retired_rule BEFORE INSERT ON grainledger.catalog_residue_rule_versions
    FOR EACH ROW EXECUTE FUNCTION grainledger.catalog_residue_retirement_guard();
CREATE TRIGGER catalog_residue_retired_setting BEFORE INSERT ON grainledger.catalog_residue_policy_setting_versions
    FOR EACH ROW EXECUTE FUNCTION grainledger.catalog_residue_retirement_guard();

CREATE FUNCTION grainledger.capture_residue_retirement_guard() RETURNS trigger
    LANGUAGE plpgsql SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp' AS $$
BEGIN
    PERFORM pg_advisory_xact_lock(714928352);
    IF EXISTS (
        SELECT 1 FROM grainledger.catalog_residue_rule_versions r
        JOIN grainledger.catalog_residue_rule_retirements t ON t.code=r.code AND t.company_id IS NOT DISTINCT FROM r.company_id
        WHERE r.id IN (
            SELECT (NEW.payload->>'ProfileRuleId')::uuid WHERE NEW.payload->>'ProfileRuleId' IS NOT NULL
            UNION ALL
            SELECT (binding->>'ProfileRuleId')::uuid FROM jsonb_array_elements(COALESCE(NEW.payload->'ProfileBindings','[]'::jsonb)) binding
        )
    ) THEN RAISE EXCEPTION 'Residue profile series is retired' USING ERRCODE='23505'; END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER capture_residue_retired_selection BEFORE INSERT ON grainledger.capture_residue_report_selections
    FOR EACH ROW EXECUTE FUNCTION grainledger.capture_residue_retirement_guard();
REVOKE ALL ON FUNCTION grainledger.capture_residue_retirement_guard() FROM PUBLIC;
CREATE TRIGGER capture_residue_retired_group BEFORE INSERT ON grainledger.capture_residue_source_group_versions
    FOR EACH ROW EXECUTE FUNCTION grainledger.capture_residue_retirement_guard();

CREATE OR REPLACE FUNCTION grainledger.ledger_residue_append(a uuid, l uuid, evaluation uuid, p jsonb, s jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE old jsonb; persisted timestamptz;
BEGIN
    IF current_setting('transaction_isolation')<>'read committed' THEN
        RAISE EXCEPTION 'Residue persistence requires READ COMMITTED isolation' USING ERRCODE='23514';
    END IF;
    PERFORM grainledger.ledger_lock_writes();
    old:=grainledger.ledger_residue_replay(a,l,evaluation,p);
    IF old IS NOT NULL THEN RETURN old; END IF;
    IF s->>'id' IS DISTINCT FROM evaluation::text OR s->>'lotId' IS DISTINCT FROM l::text
       OR s->>'companyId' IS DISTINCT FROM (SELECT owner_id::text FROM grainledger.ledger_lots WHERE id=l) THEN
        RAISE EXCEPTION 'Residue snapshot identity mismatch' USING ERRCODE='23514';
    END IF;
    IF s->>'engineVersion' IS DISTINCT FROM 'residue-flow-as-received-3' THEN
        RAISE EXCEPTION 'Unsupported residue engine' USING ERRCODE='23514';
    END IF;
    IF jsonb_typeof(s->'retiredProfileIds') IS DISTINCT FROM 'array' OR EXISTS (
        SELECT 1 FROM grainledger.catalog_residue_rule_retirements t
        JOIN grainledger.catalog_residue_rule_versions r ON r.code=t.code AND r.company_id IS NOT DISTINCT FROM t.company_id
        WHERE (r.company_id IS NULL OR r.company_id=(s->>'companyId')::uuid)
          AND r.substance_id IN (SELECT value::uuid FROM jsonb_array_elements_text(p->'substanceIds'))
          AND r.published_at <= (s->>'snapshotAt')::timestamptz
          AND r.effective_from <= (s->>'snapshotAt')::timestamptz
          AND NOT (s->'retiredProfileIds' ? t.profile_id::text)
    ) THEN
        RAISE EXCEPTION 'Residue retirement changed during evaluation' USING ERRCODE='40001';
    END IF;
    persisted:=clock_timestamp();
    s:=(s-'retiredProfileIds')||jsonb_build_object('persistedAt',persisted);
    INSERT INTO grainledger.ledger_residue_evaluations(id,lot_id,actor_id,request,snapshot,persisted_at)
    VALUES(evaluation,l,a,p,s,persisted);
    RETURN s;
END $$;
