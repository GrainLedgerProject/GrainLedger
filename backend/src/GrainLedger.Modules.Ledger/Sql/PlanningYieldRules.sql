-- An approved prospective output yield is distinct from the posting balance
-- tolerance and from F12 residue concentration factors. No rule is seeded.
CREATE TABLE ledger.planning_yield_rules (
    id uuid PRIMARY KEY,
    facility_id uuid NOT NULL REFERENCES ledger.facilities(id),
    compatibility_id uuid NOT NULL REFERENCES ledger.compatibilities(id),
    version_no integer NOT NULL CHECK (version_no > 0),
    output_fraction numeric(18,12) NOT NULL CHECK (output_fraction > 0 AND output_fraction <= 1),
    effective_from timestamptz NOT NULL,
    effective_to timestamptz,
    evidence_id uuid NOT NULL REFERENCES ledger.evidence(id),
    reviewed_by text NOT NULL CHECK (length(btrim(reviewed_by)) > 0),
    review_authority text NOT NULL CHECK (length(btrim(review_authority)) > 0),
    reviewed_at timestamptz NOT NULL,
    published_by uuid NOT NULL REFERENCES identity.users("Id"),
    published_at timestamptz NOT NULL,
    CONSTRAINT uq_planning_yield_version UNIQUE(facility_id,compatibility_id,version_no),
    CONSTRAINT ck_planning_yield_window CHECK (effective_to IS NULL OR effective_to > effective_from)
);
CREATE INDEX ix_planning_yield_current ON ledger.planning_yield_rules
    (facility_id,compatibility_id,version_no DESC);

CREATE FUNCTION ledger.protect_planning_yield_rule() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog AS $$
BEGIN
    RAISE EXCEPTION 'Published planning yield rules are immutable' USING ERRCODE='23514';
END $$;
CREATE TRIGGER immutable_planning_yield_rule BEFORE UPDATE OR DELETE
    ON ledger.planning_yield_rules FOR EACH ROW
    EXECUTE FUNCTION ledger.protect_planning_yield_rule();

CREATE FUNCTION ledger.publish_planning_yield_rule(a uuid, p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE
    f uuid := (p->>'facilityId')::uuid;
    c uuid := (p->>'compatibilityPolicyId')::uuid;
    e uuid := (p->>'evidenceId')::uuid;
    rid uuid := (p->>'id')::uuid;
    v integer := (p->>'versionNo')::integer;
    fraction numeric := (p->>'outputFraction')::numeric;
    start_at timestamptz := (p->>'effectiveFrom')::timestamptz;
    end_at timestamptz := NULLIF(p->>'effectiveTo','')::timestamptz;
    review_at timestamptz := (p->>'reviewedAt')::timestamptz;
    current_version integer;
BEGIN
    PERFORM ledger.lock_writes();
    IF ledger.role(a) <> 'MillManager' THEN
        RAISE EXCEPTION 'Mill manager required' USING ERRCODE='42501';
    END IF;
    PERFORM ledger.require_staff(a,f,ARRAY['MillManager']);
    IF rid IS NULL OR c IS NULL OR e IS NULL OR f IS NULL OR v IS NULL OR
       fraction IS NULL OR fraction <= 0 OR fraction > 1 OR
       scale(fraction) > 12 OR start_at IS NULL OR
       (end_at IS NOT NULL AND end_at <= start_at) OR
       review_at IS NULL OR review_at > statement_timestamp() OR
       length(btrim(coalesce(p->>'reviewedBy',''))) = 0 OR
       length(btrim(coalesce(p->>'reviewAuthority',''))) = 0 THEN
        RAISE EXCEPTION 'Invalid reviewed prospective yield rule' USING ERRCODE='22023';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM ledger.compatibilities WHERE id=c AND facility_id=f)
       OR NOT EXISTS(SELECT 1 FROM ledger.evidence WHERE id=e AND facility_id=f) THEN
        RAISE EXCEPTION 'Compatibility and evidence must belong to facility' USING ERRCODE='22023';
    END IF;
    SELECT COALESCE(max(version_no),0) INTO current_version
      FROM ledger.planning_yield_rules WHERE facility_id=f AND compatibility_id=c;
    IF v <> current_version + 1 THEN
        RAISE EXCEPTION 'Yield rule version must follow current version' USING ERRCODE='23514';
    END IF;
    INSERT INTO ledger.planning_yield_rules(id,facility_id,compatibility_id,version_no,
      output_fraction,effective_from,effective_to,evidence_id,reviewed_by,
      review_authority,reviewed_at,published_by,published_at)
    VALUES(rid,f,c,v,fraction,start_at,end_at,e,p->>'reviewedBy',
      p->>'reviewAuthority',review_at,a,statement_timestamp());
    RETURN jsonb_build_object('id',rid,'facilityId',f,'compatibilityPolicyId',c,
      'versionNo',v,'outputFraction',fraction::text,'effectiveFrom',start_at,
      'effectiveTo',end_at,'evidenceId',e,'publishedBy',a);
END $$;

CREATE FUNCTION ledger.planning_yield_rule(a uuid, o uuid, f uuid, c uuid, at_time timestamptz)
RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE r ledger.planning_yield_rules;
BEGIN
    IF ledger.role(a) NOT IN ('ExportStaff','CustomerManager') OR NOT ledger.own(a,o) THEN
        RAISE EXCEPTION 'Planning yield access denied' USING ERRCODE='42501';
    END IF;
    SELECT * INTO r FROM ledger.planning_yield_rules
      WHERE facility_id=f AND compatibility_id=c AND published_at<=at_time
        AND effective_from<=at_time AND (effective_to IS NULL OR at_time<effective_to)
      ORDER BY version_no DESC,id LIMIT 1;
    IF NOT FOUND THEN RETURN NULL; END IF;
    RETURN jsonb_build_object('id',r.id,'facilityId',r.facility_id,
      'compatibilityPolicyId',r.compatibility_id,'versionNo',r.version_no,
      'outputFraction',r.output_fraction::text,'effectiveFrom',r.effective_from,
      'effectiveTo',r.effective_to,'evidenceId',r.evidence_id,
      'publishedAt',r.published_at);
END $$;

REVOKE ALL ON FUNCTION ledger.publish_planning_yield_rule(uuid,jsonb) FROM PUBLIC;
REVOKE ALL ON FUNCTION ledger.planning_yield_rule(uuid,uuid,uuid,uuid,timestamptz) FROM PUBLIC;
