-- A reviewed measurement names its finished Ledger lot explicitly. Capture
-- source-sample results are not promoted to finished-product measurements.
CREATE TABLE ledger.planning_quality_observations (
    id uuid PRIMARY KEY,
    owner_id uuid NOT NULL REFERENCES identity.organizations("Id"),
    facility_id uuid NOT NULL REFERENCES ledger.facilities(id),
    lot_id uuid NOT NULL REFERENCES ledger.lots(id),
    attribute_id uuid NOT NULL REFERENCES catalog.quality_attributes(id),
    version_no integer NOT NULL CHECK (version_no > 0),
    measured_value numeric(24,12) NOT NULL CHECK (measured_value >= 0),
    unit text NOT NULL CHECK (length(btrim(unit)) > 0),
    measured_at timestamptz NOT NULL,
    evidence_id uuid NOT NULL REFERENCES ledger.evidence(id),
    report_reference text NOT NULL CHECK (length(btrim(report_reference)) > 0),
    reviewed_by text NOT NULL CHECK (length(btrim(reviewed_by)) > 0),
    review_authority text NOT NULL CHECK (length(btrim(review_authority)) > 0),
    reviewed_at timestamptz NOT NULL,
    confirmed_by uuid NOT NULL REFERENCES identity.users("Id"),
    confirmed_at timestamptz NOT NULL,
    CONSTRAINT uq_planning_quality_version UNIQUE (owner_id,lot_id,attribute_id,version_no)
);
CREATE INDEX ix_planning_quality_current ON ledger.planning_quality_observations
    (owner_id,lot_id,attribute_id,version_no DESC);

CREATE FUNCTION ledger.protect_planning_quality_observation() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog AS $$
BEGIN
    RAISE EXCEPTION 'Confirmed planning quality observations are immutable' USING ERRCODE='23514';
END $$;
CREATE TRIGGER immutable_planning_quality_observation BEFORE UPDATE OR DELETE
    ON ledger.planning_quality_observations FOR EACH ROW
    EXECUTE FUNCTION ledger.protect_planning_quality_observation();

CREATE FUNCTION ledger.confirm_planning_quality_observation(a uuid,p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,catalog,identity AS $$
DECLARE
    v_owner uuid := (p->>'ownerId')::uuid;
    v_facility uuid := (p->>'facilityId')::uuid;
    v_lot uuid := (p->>'lotId')::uuid;
    v_attribute uuid := (p->>'attributeId')::uuid;
    v_evidence uuid := (p->>'evidenceId')::uuid;
    observation_id uuid := (p->>'id')::uuid;
    version_no integer := (p->>'versionNo')::integer;
    measurement numeric := (p->>'measuredValue')::numeric;
    measured_at timestamptz := (p->>'measuredAt')::timestamptz;
    reviewed_at timestamptz := (p->>'reviewedAt')::timestamptz;
    expected_unit text;
    current_version integer;
BEGIN
    PERFORM ledger.lock_writes();
    IF ledger.role(a) <> 'CustomerManager' OR NOT ledger.own(a,v_owner) THEN
        RAISE EXCEPTION 'Customer manager of the owner required' USING ERRCODE='42501';
    END IF;
    IF observation_id IS NULL OR v_owner IS NULL OR v_facility IS NULL OR v_lot IS NULL OR
       v_attribute IS NULL OR v_evidence IS NULL OR version_no IS NULL OR
       measurement IS NULL OR measurement < 0 OR scale(measurement) > 12 OR
       measured_at IS NULL OR measured_at > statement_timestamp() OR
       reviewed_at IS NULL OR reviewed_at < measured_at OR
       reviewed_at > statement_timestamp() OR
       p->>'subjectConfirmed' IS DISTINCT FROM 'true' OR
       length(btrim(coalesce(p->>'reportReference',''))) = 0 OR
       length(btrim(coalesce(p->>'reviewedBy',''))) = 0 OR
       length(btrim(coalesce(p->>'reviewAuthority',''))) = 0 THEN
        RAISE EXCEPTION 'Invalid confirmed finished-lot quality observation' USING ERRCODE='22023';
    END IF;
    SELECT unit INTO expected_unit FROM catalog.quality_attributes WHERE id=v_attribute;
    IF expected_unit IS NULL OR expected_unit IS DISTINCT FROM p->>'unit' OR
       NOT EXISTS (SELECT 1 FROM ledger.lots WHERE id=v_lot AND owner_id=v_owner
                   AND stage IN ('HEAD_RICE','BROKEN_RICE','BLENDED_RICE','BAGGED_RICE')) OR
       NOT EXISTS (SELECT 1 FROM ledger.positions q JOIN ledger.locations l ON l.id=q.location_id
                   WHERE q.lot_id=v_lot AND l.facility_id=v_facility) OR
       NOT EXISTS (SELECT 1 FROM ledger.evidence WHERE id=v_evidence
                   AND owner_id=v_owner AND facility_id=v_facility) THEN
        RAISE EXCEPTION 'Lot, facility, attribute unit and evidence must match' USING ERRCODE='22023';
    END IF;
    SELECT coalesce(max(q.version_no),0) INTO current_version
      FROM ledger.planning_quality_observations q
      WHERE q.owner_id=v_owner AND q.lot_id=v_lot AND q.attribute_id=v_attribute;
    IF version_no <> current_version + 1 THEN
        RAISE EXCEPTION 'Quality observation version must follow current version' USING ERRCODE='23514';
    END IF;
    INSERT INTO ledger.planning_quality_observations
      (id,owner_id,facility_id,lot_id,attribute_id,version_no,measured_value,
       unit,measured_at,evidence_id,report_reference,reviewed_by,review_authority,
       reviewed_at,confirmed_by,confirmed_at)
    VALUES (observation_id,v_owner,v_facility,v_lot,v_attribute,version_no,
       measurement,expected_unit,measured_at,v_evidence,p->>'reportReference',
       p->>'reviewedBy',p->>'reviewAuthority',reviewed_at,a,statement_timestamp());
    RETURN jsonb_build_object('id',observation_id,'ownerId',v_owner,
        'lotId',v_lot,'attributeId',v_attribute,'versionNo',version_no,
        'measuredValue',measurement::text,'unit',expected_unit,
        'evidenceId',v_evidence,'confirmedBy',a);
END $$;

CREATE FUNCTION ledger.planning_quality_observations(a uuid,o uuid,f uuid,lots uuid[],at_time timestamptz)
RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
BEGIN
    IF ledger.role(a) NOT IN ('ExportStaff','CustomerManager') OR NOT ledger.own(a,o) OR
       cardinality(lots) < 1 OR cardinality(lots) > 100 OR
       EXISTS (SELECT 1 FROM unnest(lots) AS requested(lot_id)
               WHERE NOT EXISTS(SELECT 1 FROM ledger.lots l WHERE l.id=requested.lot_id AND l.owner_id=o)) THEN
        RAISE EXCEPTION 'Planning quality access denied' USING ERRCODE='42501';
    END IF;
    RETURN coalesce((SELECT jsonb_agg(to_jsonb(current_row) ORDER BY current_row.lot_id,current_row.attribute_id)
       FROM (SELECT DISTINCT ON (q.lot_id,q.attribute_id)
                    q.id,q.lot_id,q.attribute_id,q.version_no,q.measured_value::text,
                    q.unit,q.evidence_id,q.measured_at,q.confirmed_at
             FROM ledger.planning_quality_observations q
             WHERE q.owner_id=o AND q.facility_id=f AND q.lot_id=ANY(lots)
                   AND q.confirmed_at<=coalesce(at_time,statement_timestamp())
             ORDER BY q.lot_id,q.attribute_id,q.version_no DESC,q.id) current_row), '[]'::jsonb);
END $$;

REVOKE ALL ON FUNCTION ledger.confirm_planning_quality_observation(uuid,jsonb) FROM PUBLIC;
REVOKE ALL ON FUNCTION ledger.planning_quality_observations(uuid,uuid,uuid,uuid[],timestamptz) FROM PUBLIC;
