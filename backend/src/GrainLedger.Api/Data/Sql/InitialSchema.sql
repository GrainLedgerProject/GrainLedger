CREATE SCHEMA IF NOT EXISTS grainledger;
REVOKE CREATE ON SCHEMA grainledger FROM PUBLIC;

CREATE FUNCTION grainledger.capture_check_lab_confirmation_complete() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM grainledger.capture_lab_result_sets s WHERE s.company_id = NEW.company_id AND s.report_id = NEW.report_id)
        OR EXISTS (SELECT 1 FROM grainledger.capture_lab_result_sets s WHERE s.company_id = NEW.company_id AND s.report_id = NEW.report_id
        AND (NOT EXISTS (SELECT 1 FROM grainledger.capture_lab_confirmed_associations a WHERE a.company_id = NEW.company_id AND a.result_set_id = s.id AND a.confirmation_id = NEW.id)
        OR NOT EXISTS (SELECT 1 FROM grainledger.capture_lab_results r WHERE r.company_id = NEW.company_id AND r.result_set_id = s.id))) THEN
        RAISE EXCEPTION 'Laboratory confirmation must cover every nonempty result set.' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END $$;

CREATE FUNCTION grainledger.capture_guard_lab_active_report() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE target_report uuid;
BEGIN
    PERFORM pg_advisory_xact_lock(714928352);
    IF TG_TABLE_NAME = 'capture_lab_report_confirmations' THEN target_report := NEW.report_id;
    ELSE target_report := NEW.related_report_id;
    END IF;
    IF EXISTS (SELECT 1 FROM grainledger.capture_lab_report_retirements WHERE company_id=NEW.company_id AND report_id=target_report) THEN
        RAISE EXCEPTION 'Laboratory report is retired.' USING ERRCODE = '55000';
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION grainledger.capture_guard_lab_association() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE s grainledger.capture_lab_result_sets%ROWTYPE; p grainledger.capture_samples%ROWTYPE; lab text;
    ws CONSTANT text := chr(9) || chr(10) || chr(11) || chr(12) || chr(13) || ' ' || chr(133) || chr(160) || chr(5760)
        || chr(8192) || chr(8193) || chr(8194) || chr(8195) || chr(8196) || chr(8197) || chr(8198) || chr(8199)
        || chr(8200) || chr(8201) || chr(8202) || chr(8232) || chr(8233) || chr(8239) || chr(8287) || chr(12288);
BEGIN
    SELECT rs.* INTO s FROM grainledger.capture_lab_result_sets rs JOIN grainledger.capture_lab_report_confirmations c ON c.company_id = rs.company_id AND c.report_id = rs.report_id
        WHERE rs.company_id = NEW.company_id AND rs.id = NEW.result_set_id AND c.id = NEW.confirmation_id AND c.creation_transaction_id = pg_current_xact_id();
    IF NOT FOUND THEN RAISE EXCEPTION 'Invalid laboratory confirmation association.' USING ERRCODE = '23514'; END IF;
    SELECT * INTO p FROM grainledger.capture_samples WHERE company_id = NEW.company_id AND id = NEW.sample_id;
    IF NOT FOUND OR (nullif(btrim(s.stated_sample_code, ws),'') IS NOT NULL AND btrim(s.stated_sample_code, ws) <> btrim(p.code, ws))
        OR (nullif(btrim(s.stated_seal_code, ws),'') IS NOT NULL AND btrim(s.stated_seal_code, ws) <> btrim(p.seal_code, ws)) THEN
        RAISE EXCEPTION 'Laboratory sample identity is unresolved.' USING ERRCODE = '23514';
    END IF;
    SELECT laboratory_name INTO lab FROM grainledger.capture_lab_reports WHERE company_id = NEW.company_id AND id = s.report_id;
    IF (NEW.identity_basis = 'SAMPLE_CODE' AND nullif(btrim(s.stated_sample_code, ws),'') IS NOT NULL AND btrim(s.stated_sample_code, ws) = btrim(p.code, ws))
        OR (NEW.identity_basis = 'SEAL_CODE' AND nullif(btrim(s.stated_seal_code, ws),'') IS NOT NULL AND btrim(s.stated_seal_code, ws) = btrim(p.seal_code, ws))
        OR (NEW.identity_basis = 'LAB_REFERENCE' AND nullif(btrim(NEW.evidence_ref, ws),'') IS NOT NULL AND EXISTS (
            SELECT 1 FROM grainledger.capture_sample_handovers h WHERE h.company_id = NEW.company_id AND h.sample_id = NEW.sample_id
            AND h.handover_kind IN ('LAB_DISPATCH','LAB_ACKNOWLEDGEMENT') AND h.laboratory_name = lab
            AND btrim(h.laboratory_sample_reference, ws) = btrim(s.laboratory_sample_reference, ws)
            AND btrim(h.evidence_ref, ws) = btrim(NEW.evidence_ref, ws))) THEN
        RETURN NEW;
    END IF;
    RAISE EXCEPTION 'Laboratory sample identity is unresolved.' USING ERRCODE = '23514';
END $$;

CREATE FUNCTION grainledger.capture_guard_lab_original_insert() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE owner_report uuid;
BEGIN
    IF TG_TABLE_NAME = 'capture_lab_result_sets' THEN owner_report := NEW.report_id;
    ELSE SELECT s.report_id INTO owner_report FROM grainledger.capture_lab_result_sets s WHERE s.company_id = NEW.company_id AND s.id = NEW.result_set_id;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM grainledger.capture_lab_reports r WHERE r.id = owner_report AND r.company_id = NEW.company_id AND r.creation_transaction_id = pg_current_xact_id())
       OR EXISTS (SELECT 1 FROM grainledger.capture_lab_report_confirmations c WHERE c.report_id = owner_report AND c.company_id = NEW.company_id) THEN
        RAISE EXCEPTION 'Laboratory evidence is immutable.' USING ERRCODE = '55000';
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION grainledger.capture_guard_lab_retirement() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    PERFORM pg_advisory_xact_lock(714928352);
    IF EXISTS (SELECT 1 FROM grainledger.capture_lab_report_confirmations WHERE company_id=NEW.company_id AND report_id=NEW.report_id)
        OR EXISTS (SELECT 1 FROM grainledger.capture_lab_report_retirements WHERE company_id=NEW.company_id AND report_id=NEW.report_id) THEN
        RAISE EXCEPTION 'Laboratory report is confirmed or retired.' USING ERRCODE = '55000';
    END IF;
    IF NEW.status = 'DELETED' AND EXISTS (
        SELECT 1 FROM grainledger.capture_lab_reports WHERE company_id=NEW.company_id AND related_report_id=NEW.report_id) THEN
        RAISE EXCEPTION 'Laboratory report is referenced.' USING ERRCODE = '23514';
    END IF;
    IF NEW.replacement_report_id IS NOT NULL AND (
        NOT EXISTS (SELECT 1 FROM grainledger.capture_lab_reports WHERE company_id=NEW.company_id AND id=NEW.replacement_report_id
            AND creation_transaction_id=pg_current_xact_id())
        OR EXISTS (SELECT 1 FROM grainledger.capture_lab_report_confirmations WHERE company_id=NEW.company_id AND report_id=NEW.replacement_report_id)
        OR EXISTS (SELECT 1 FROM grainledger.capture_lab_report_retirements WHERE company_id=NEW.company_id AND report_id=NEW.replacement_report_id)) THEN
        RAISE EXCEPTION 'Replacement must be a new unconfirmed report in this transaction.' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION grainledger.capture_publish_residue_group(a uuid, p jsonb) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $_$
DECLARE members uuid[]; previous jsonb;
BEGIN
    PERFORM pg_advisory_xact_lock(714928352);
    PERFORM grainledger.capture_residue_review_gate(a,p);
    IF jsonb_typeof(p->'SourceIds') IS DISTINCT FROM 'array' OR jsonb_array_length(p->'SourceIds') NOT BETWEEN 1 AND 1000
        OR jsonb_typeof(p->'SupportingResultIds') IS DISTINCT FROM 'array' OR jsonb_array_length(p->'SupportingResultIds')>1000
        OR COALESCE((p->>'VersionNo')::int,0)<1 OR length(btrim(COALESCE(p->>'Code','')))=0
        OR p->>'ProductStage' IS DISTINCT FROM 'RAW_PADDY' OR p->>'ProductIdentity' IS DISTINCT FROM 'RAW_PADDY'
        OR COALESCE((p->>'DecimalPlaces')::int,-1) NOT BETWEEN 0 AND 18
        OR p->>'Unit' IS DISTINCT FROM 'mg/kg' OR p->>'Precedence' IS NULL
        OR p->>'Precedence' NOT IN ('DIRECT_FIRST','GROUP_FIRST','UNTESTED_ONLY')
        OR p->>'Lower' IS NULL OR p->>'Upper' IS NULL OR p->>'Lower' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$'
        OR p->>'Upper' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$' OR (p->>'Lower')::numeric>(p->>'Upper')::numeric
    THEN RAISE EXCEPTION 'Invalid reviewed residue group' USING ERRCODE='22023'; END IF;
    SELECT array_agg(value::uuid) INTO members FROM jsonb_array_elements_text(p->'SourceIds');
    IF cardinality(members)<>(SELECT count(DISTINCT x) FROM unnest(members) x)
        OR EXISTS(SELECT 1 FROM unnest(members) member WHERE NOT EXISTS(
            SELECT 1 FROM grainledger.capture_source_lots sl JOIN grainledger.capture_procurement_items i ON i.id=sl.procurement_item_id AND i.company_id=sl.company_id
            JOIN grainledger.capture_procurements procurement ON procurement.id=i.procurement_id AND procurement.company_id=i.company_id
            WHERE sl.id=member AND sl.company_id=(p->>'CompanyId')::uuid
                AND procurement.received_at >= (p->>'EffectiveFrom')::timestamptz
                AND (p->>'EffectiveTo' IS NULL OR procurement.received_at<(p->>'EffectiveTo')::timestamptz)))
        OR EXISTS(SELECT 1 FROM jsonb_array_elements_text(p->'SupportingResultIds') result WHERE NOT EXISTS(
            SELECT 1 FROM unnest(members) member WHERE grainledger.capture_residue_confirmed_result((p->>'CompanyId')::uuid,member,result::uuid,(p->>'SubstanceId')::uuid)))
    THEN RAISE EXCEPTION 'Group membership or supporting evidence not applicable' USING ERRCODE='22023'; END IF;
    IF EXISTS(SELECT 1 FROM grainledger.capture_residue_source_group_versions WHERE company_id=(p->>'CompanyId')::uuid
        AND code=p->>'Code' AND substance_id<>(p->>'SubstanceId')::uuid)
    THEN RAISE EXCEPTION 'Group code must retain substance scope' USING ERRCODE='23505'; END IF;
    IF EXISTS(SELECT 1 FROM grainledger.capture_residue_source_group_versions WHERE company_id=(p->>'CompanyId')::uuid
        AND code=p->>'Code' AND version_no >= (p->>'VersionNo')::int)
    THEN RAISE EXCEPTION 'Group version must advance' USING ERRCODE='23505'; END IF;
    IF p->>'SupersedesId' IS NOT NULL THEN
        SELECT payload INTO previous FROM grainledger.capture_residue_source_group_versions WHERE id=(p->>'SupersedesId')::uuid;
        IF previous IS NULL OR previous->>'CompanyId' IS DISTINCT FROM p->>'CompanyId' OR previous->>'Code' IS DISTINCT FROM p->>'Code'
            OR previous->>'SubstanceId' IS DISTINCT FROM p->>'SubstanceId'
            OR EXISTS(SELECT 1 FROM grainledger.capture_residue_source_group_versions WHERE payload->>'SupersedesId'=p->>'SupersedesId')
        THEN RAISE EXCEPTION 'Invalid reviewed group supersession' USING ERRCODE='23505'; END IF;
    END IF;
    INSERT INTO grainledger.capture_residue_source_group_versions VALUES((p->>'Id')::uuid,(p->>'CompanyId')::uuid,p->>'Code',
        (p->>'VersionNo')::int,(p->>'SubstanceId')::uuid,members,(p->>'EffectiveFrom')::timestamptz,(p->>'EffectiveTo')::timestamptz,
        a,(p->>'PublishedAt')::timestamptz,p);
END $_$;

CREATE FUNCTION grainledger.capture_publish_residue_selection(a uuid, p jsonb) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $_$
DECLARE previous jsonb; raw_result grainledger.capture_lab_results;
BEGIN
    PERFORM pg_advisory_xact_lock(714928352);
    PERFORM grainledger.capture_residue_review_gate(a,p);
    IF NOT grainledger.capture_residue_confirmed_result((p->>'CompanyId')::uuid,(p->>'SourceId')::uuid,
        (p->>'SelectedResultId')::uuid,(p->>'SubstanceId')::uuid)
        OR jsonb_typeof(p->'SupersededResultIds') IS DISTINCT FROM 'array' OR jsonb_array_length(p->'SupersededResultIds')>1000
        OR EXISTS(SELECT 1 FROM jsonb_array_elements_text(p->'SupersededResultIds') r WHERE r=p->>'SelectedResultId'
            OR NOT grainledger.capture_residue_confirmed_result((p->>'CompanyId')::uuid,(p->>'SourceId')::uuid,r::uuid,(p->>'SubstanceId')::uuid))
    THEN RAISE EXCEPTION 'Selection evidence not confirmed for same source/substance/company' USING ERRCODE='22023'; END IF;
    SELECT * INTO raw_result FROM grainledger.capture_lab_results WHERE id=(p->>'SelectedResultId')::uuid;
    IF p->>'AbsoluteUncertainty' IS NOT NULL AND (p->>'AbsoluteUncertainty' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$'
        OR raw_result.result_kind<>'NUMERIC' OR length(btrim(COALESCE(raw_result.uncertainty,'')))=0)
        OR p->>'ReportingThreshold' IS NOT NULL AND (p->>'ReportingThreshold' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$'
            OR (p->>'ReportingThreshold')::numeric<=0 OR raw_result.result_kind<>'NOT_DETECTED')
    THEN RAISE EXCEPTION 'Normalized uncertainty/threshold lacks applicable raw evidence' USING ERRCODE='22023'; END IF;
    IF p->>'SupersedesId' IS NOT NULL THEN
        SELECT payload INTO previous FROM grainledger.capture_residue_report_selections WHERE id=(p->>'SupersedesId')::uuid;
        IF previous IS NULL OR previous->>'CompanyId' IS DISTINCT FROM p->>'CompanyId' OR previous->>'SourceId' IS DISTINCT FROM p->>'SourceId'
            OR previous->>'SubstanceId' IS DISTINCT FROM p->>'SubstanceId'
            OR EXISTS(SELECT 1 FROM grainledger.capture_residue_report_selections WHERE payload->>'SupersedesId'=p->>'SupersedesId')
        THEN RAISE EXCEPTION 'Invalid reviewed selection supersession' USING ERRCODE='23505'; END IF;
    END IF;
    INSERT INTO grainledger.capture_residue_report_selections VALUES((p->>'Id')::uuid,(p->>'CompanyId')::uuid,
        (p->>'SourceId')::uuid,(p->>'SubstanceId')::uuid,(p->>'EffectiveFrom')::timestamptz,(p->>'EffectiveTo')::timestamptz,
        a,(p->>'PublishedAt')::timestamptz,p);
END $_$;

CREATE FUNCTION grainledger.capture_reject_lab_evidence_mutation() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    RAISE EXCEPTION 'Laboratory evidence is immutable.' USING ERRCODE = '55000';
END $$;

CREATE FUNCTION grainledger.capture_residue_confirmed_result(c uuid, s uuid, r uuid, substance uuid) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT EXISTS(SELECT 1 FROM grainledger.capture_lab_results result
        JOIN grainledger.capture_lab_result_sets rs ON rs.id=result.result_set_id AND rs.company_id=result.company_id
        JOIN grainledger.capture_lab_confirmed_associations association ON association.result_set_id=rs.id AND association.company_id=rs.company_id
        JOIN grainledger.capture_lab_report_confirmations confirmation ON confirmation.id=association.confirmation_id AND confirmation.company_id=association.company_id
            AND confirmation.report_id=rs.report_id AND confirmation.identity_checked AND confirmation.transcription_checked
        JOIN grainledger.capture_samples sample ON sample.id=association.sample_id AND sample.company_id=association.company_id
        WHERE result.id=r AND result.company_id=c AND result.substance_id=substance AND sample.source_lot_id=s)
$$;

CREATE FUNCTION grainledger.capture_residue_immutable() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN RAISE EXCEPTION 'Reviewed residue records are immutable' USING ERRCODE='55000'; END $$;

CREATE FUNCTION grainledger.capture_residue_review_gate(a uuid, p jsonb) RETURNS void
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM grainledger.identity_users u JOIN grainledger.identity_organizations o ON o."Id"=u."OrganizationId"
        WHERE u."Id"=a AND u."IsActive" AND u."Role"='CustomerManager' AND o."Kind"='Customer'
            AND u."OrganizationId"=(p->>'CompanyId')::uuid) THEN
        RAISE EXCEPTION 'Residue evidence publication denied' USING ERRCODE='42501';
    END IF;
    IF p->>'Id' IS NULL OR (p->>'Id')::uuid='00000000-0000-0000-0000-000000000000'
        OR (p->>'PublishedBy')::uuid IS DISTINCT FROM a OR (p->>'PublishedAt')::timestamptz IS NULL
        OR (p->>'PublishedAt')::timestamptz > clock_timestamp()
        OR (p->>'ReviewedAt')::timestamptz IS NULL OR (p->>'ReviewedAt')::timestamptz > clock_timestamp()
        OR (p->>'EffectiveFrom')::timestamptz IS NULL
        OR (p->>'EffectiveTo')::timestamptz <= (p->>'EffectiveFrom')::timestamptz
        OR EXISTS(SELECT 1 FROM unnest(ARRAY['EvidenceRef','ApplicabilityStatement','ReviewedBy','ReviewAuthority','MaterialBasis','ResidueDefinition']) k
            WHERE length(btrim(COALESCE(p->>k,'')))=0)
        OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_active_substances WHERE id=(p->>'SubstanceId')::uuid)
    THEN RAISE EXCEPTION 'Incomplete reviewed residue evidence' USING ERRCODE='22023'; END IF;
END $$;

CREATE FUNCTION grainledger.capture_stamp_lab_evidence_transaction() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    NEW.creation_transaction_id := pg_current_xact_id();
    RETURN NEW;
END $$;

CREATE FUNCTION grainledger.catalog_publish_blend_assessment_policy(a uuid, p jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE
    company uuid := (p->>'companyId')::uuid;
    market uuid := (p->>'marketId')::uuid;
    commodity uuid := (p->>'commodityId')::uuid;
    policy_id uuid := (p->>'id')::uuid;
    version_no integer := (p->>'versionNo')::integer;
    margin numeric := (p->>'mrlMarginFraction')::numeric;
    from_at timestamptz := (p->>'effectiveFrom')::timestamptz;
    to_at timestamptz := NULLIF(p->>'effectiveTo','')::timestamptz;
    review_at timestamptz := (p->>'reviewedAt')::timestamptz;
    required uuid[];
    current_version integer;
BEGIN
    PERFORM pg_advisory_xact_lock(714928352);
    IF NOT EXISTS(SELECT 1 FROM grainledger.identity_users u JOIN grainledger.identity_organizations o
        ON o."Id"=u."OrganizationId" WHERE u."Id"=a AND u."IsActive"
        AND u."Role"='CustomerManager' AND u."OrganizationId"=company
        AND o."Kind"='Customer') THEN
        RAISE EXCEPTION 'Customer manager for company required' USING ERRCODE='42501';
    END IF;
    IF jsonb_typeof(p->'requiredSubstanceIds') <> 'array' OR
       jsonb_typeof(p->'qualityRules') <> 'array' THEN
        RAISE EXCEPTION 'Policy arrays required' USING ERRCODE='22023';
    END IF;
    SELECT array_agg(value::uuid) INTO required
      FROM jsonb_array_elements_text(p->'requiredSubstanceIds') AS value;
    IF policy_id IS NULL OR market IS NULL OR commodity IS NULL OR
       version_no IS NULL OR version_no < 1 OR from_at IS NULL OR
       (to_at IS NOT NULL AND to_at<=from_at) OR
       required IS NULL OR cardinality(required) NOT BETWEEN 1 AND 200 OR
       (SELECT count(DISTINCT x) FROM unnest(required) AS x)<>cardinality(required) OR
       margin IS NULL OR margin<0 OR margin>=1 OR scale(margin)>12 OR
       length(btrim(coalesce(p->>'residueBasis',''))) NOT BETWEEN 1 AND 100 OR
       length(btrim(coalesce(p->>'evidenceReference',''))) NOT BETWEEN 1 AND 1000 OR
       length(btrim(coalesce(p->>'reviewedBy','')))=0 OR
       length(btrim(coalesce(p->>'reviewAuthority','')))=0 OR
       review_at IS NULL OR review_at>statement_timestamp() THEN
        RAISE EXCEPTION 'Invalid reviewed assessment policy' USING ERRCODE='22023';
    END IF;
    IF EXISTS(SELECT 1 FROM unnest(required) AS x WHERE NOT EXISTS
        (SELECT 1 FROM grainledger.catalog_active_substances WHERE id=x)) OR
       EXISTS(SELECT 1 FROM jsonb_array_elements(p->'qualityRules') AS x
         WHERE x->>'calculation'<>'MASS_WEIGHTED_MEAN' OR
           NOT EXISTS(SELECT 1 FROM grainledger.catalog_quality_attributes
             WHERE id=(x->>'attributeId')::uuid AND unit=x->>'unit')) THEN
        RAISE EXCEPTION 'Unknown policy catalog reference' USING ERRCODE='22023';
    END IF;
    SELECT COALESCE(max(b.version_no),0) INTO current_version
      FROM grainledger.catalog_blend_assessment_policies b
     WHERE b.company_id=company AND b.market_id=market AND b.commodity_id=commodity;
    IF version_no<>current_version+1 THEN
        RAISE EXCEPTION 'Assessment policy version must follow current version' USING ERRCODE='23514';
    END IF;
    INSERT INTO grainledger.catalog_blend_assessment_policies
        (id,company_id,market_id,commodity_id,version_no,effective_from,effective_to,
         required_substance_ids,margin_fraction,residue_basis,quality_rules,evidence_reference,
         reviewed_by,review_authority,reviewed_at,published_by,published_at)
    VALUES(policy_id,company,market,commodity,version_no,from_at,to_at,
        required,margin,p->>'residueBasis',p->'qualityRules',p->>'evidenceReference',
        p->>'reviewedBy',p->>'reviewAuthority',review_at,a,statement_timestamp());
    RETURN jsonb_build_object('id',policy_id,'versionNo',version_no,
        'companyId',company,'publishedBy',a);
END $$;

CREATE FUNCTION grainledger.catalog_publish_residue_policy_setting(a uuid, p jsonb) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $_$
DECLARE previous grainledger.catalog_residue_policy_setting_versions%ROWTYPE; company uuid;
BEGIN
    PERFORM pg_advisory_xact_lock(714928352);
    IF jsonb_typeof(p) IS DISTINCT FROM 'object' OR jsonb_typeof(p->'CompanyId') IS DISTINCT FROM 'string'
    THEN RAISE EXCEPTION 'Explicit company setting scope required' USING ERRCODE='22023'; END IF;
    company := (p->>'CompanyId')::uuid;
    IF NOT EXISTS(SELECT 1 FROM grainledger.identity_users u JOIN grainledger.identity_organizations o ON o."Id"=u."OrganizationId"
        WHERE u."Id"=a AND u."IsActive" AND u."Role"='CustomerManager' AND o."Kind"='Customer' AND o."Id"=company)
    THEN RAISE EXCEPTION 'Company residue setting publication denied' USING ERRCODE='42501'; END IF;
    PERFORM grainledger.catalog_validate_residue_policy_review(p);
    IF (p->>'PublishedBy')::uuid IS DISTINCT FROM a
        OR jsonb_typeof(p->'RuleCode') IS DISTINCT FROM 'string' OR length(btrim(p->>'RuleCode'))=0 OR length(p->>'RuleCode')>100
        OR jsonb_typeof(p->'VersionNo') IS DISTINCT FROM 'number' OR p->>'VersionNo' !~ '^[0-9]+$'
        OR p->>'Mode' IS NULL OR p->>'Mode' NOT IN ('SYSTEM_DEFAULT','COMPANY_OVERRIDE')
    THEN RAISE EXCEPTION 'Incomplete reviewed company residue setting' USING ERRCODE='22023'; END IF;
    IF (p->>'VersionNo')::int<1 OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_residue_rule_versions
        WHERE code=p->>'RuleCode' AND (company_id IS NULL OR company_id=company))
    THEN RAISE EXCEPTION 'Reviewed setting requires existing visible profile series' USING ERRCODE='22023'; END IF;
    IF EXISTS(SELECT 1 FROM grainledger.catalog_residue_policy_setting_versions
        WHERE company_id=company AND rule_code=p->>'RuleCode' AND version_no >= (p->>'VersionNo')::int)
    THEN RAISE EXCEPTION 'Company setting version must advance' USING ERRCODE='23505'; END IF;
    IF p->>'SupersedesId' IS NOT NULL THEN
        SELECT * INTO previous FROM grainledger.catalog_residue_policy_setting_versions WHERE id=(p->>'SupersedesId')::uuid;
        IF previous.id IS NULL OR previous.company_id IS DISTINCT FROM company OR previous.rule_code IS DISTINCT FROM p->>'RuleCode'
            OR EXISTS(SELECT 1 FROM grainledger.catalog_residue_policy_setting_versions WHERE (payload->>'SupersedesId')::uuid=(p->>'SupersedesId')::uuid)
        THEN RAISE EXCEPTION 'Invalid reviewed setting supersession' USING ERRCODE='23505'; END IF;
    END IF;
    INSERT INTO grainledger.catalog_residue_policy_setting_versions(id,company_id,rule_code,version_no,effective_from,effective_to,published_by,published_at,payload)
        VALUES((p->>'Id')::uuid,company,p->>'RuleCode',(p->>'VersionNo')::int,(p->>'EffectiveFrom')::timestamptz,
            (p->>'EffectiveTo')::timestamptz,a,(p->>'PublishedAt')::timestamptz,p);
END $_$;

CREATE FUNCTION grainledger.catalog_publish_residue_rule(a uuid, p jsonb) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $_$
DECLARE previous grainledger.catalog_residue_rule_versions%ROWTYPE; company uuid;
BEGIN
    PERFORM pg_advisory_xact_lock(714928352);
    IF jsonb_typeof(p) IS DISTINCT FROM 'object'
        OR COALESCE(jsonb_typeof(p->'CompanyId'),'null') NOT IN ('null','string')
    THEN RAISE EXCEPTION 'Invalid residue policy scope' USING ERRCODE='22023'; END IF;
    company := (p->>'CompanyId')::uuid;
    IF NOT EXISTS(SELECT 1 FROM grainledger.identity_users u JOIN grainledger.identity_organizations o ON o."Id"=u."OrganizationId"
        WHERE u."Id"=a AND u."IsActive" AND
            (company IS NULL AND u."Role"='Administrator' AND o."Kind"='Mill'
            OR company IS NOT NULL AND u."Role"='CustomerManager' AND o."Kind"='Customer' AND o."Id"=company))
    THEN RAISE EXCEPTION 'Residue policy publication denied' USING ERRCODE='42501'; END IF;
    PERFORM grainledger.catalog_validate_residue_policy_review(p);
    IF (p->>'PublishedBy')::uuid IS DISTINCT FROM a
        OR EXISTS(SELECT 1 FROM unnest(ARRAY['Code','Kind','SubstanceId','CommodityId','ResidueDefinition',
            'InputStage','OutputStage','InputBasis','OutputBasis','Unit']) k
            WHERE jsonb_typeof(p->k) IS DISTINCT FROM 'string' OR length(btrim(p->>k))=0)
        OR length(p->>'Code')>100
        OR jsonb_typeof(p->'VersionNo') IS DISTINCT FROM 'number' OR p->>'VersionNo' !~ '^[0-9]+$'
        OR jsonb_typeof(p->'DecimalPlaces') IS DISTINCT FROM 'number' OR p->>'DecimalPlaces' !~ '^[0-9]+$'
        OR jsonb_typeof(p->'HomogeneousMixing') IS DISTINCT FROM 'boolean'
        OR COALESCE(jsonb_typeof(p->'RelativeUncertaintyAtZero'),'boolean') IS DISTINCT FROM 'boolean'
        OR COALESCE(jsonb_typeof(p->'RelativeUncertaintyPercent'),'null') NOT IN ('null','string')
        OR p->>'Unit' IS DISTINCT FROM 'mg/kg'
    THEN RAISE EXCEPTION 'Incomplete reviewed residue policy' USING ERRCODE='22023'; END IF;
    IF (p->>'VersionNo')::int<1 OR (p->>'DecimalPlaces')::int NOT BETWEEN 0 AND 18
        OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_active_substances WHERE id=(p->>'SubstanceId')::uuid)
        OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_commodities WHERE id=(p->>'CommodityId')::uuid)
    THEN RAISE EXCEPTION 'Invalid residue policy catalog or precision' USING ERRCODE='22023'; END IF;
    IF p->>'Kind'='INTERPRETATION' THEN
        IF p->>'InterpretationKind' IS NULL OR p->>'InterpretationKind' NOT IN
                ('NUMERIC_ABSOLUTE_UNCERTAINTY','NUMERIC_RELATIVE_UNCERTAINTY','BELOW_LIMIT','ND')
            OR jsonb_typeof(p->'AnalyticalMethod') IS DISTINCT FROM 'string' OR length(btrim(p->>'AnalyticalMethod'))=0
            OR p->>'InputBasis' IS DISTINCT FROM p->>'OutputBasis' OR p->>'InputStage' IS DISTINCT FROM p->>'OutputStage'
            OR p->>'FactorLower' IS NOT NULL OR p->>'FactorUpper' IS NOT NULL OR p->>'FactorSemantics' IS NOT NULL
            OR (p->>'HomogeneousMixing')::boolean
            OR p->>'InterpretationKind'='ND' AND (p->>'NdThresholdKind' IS NULL OR p->>'NdThresholdKind' NOT IN ('REPORTING_THRESHOLD','LOD','LOQ'))
            OR p->>'InterpretationKind'<>'ND' AND p->>'NdThresholdKind' IS NOT NULL
        THEN RAISE EXCEPTION 'Invalid residue interpretation' USING ERRCODE='22023'; END IF;
    ELSIF p->>'Kind'='PROCESS' THEN
        IF jsonb_typeof(p->'FactorLower') IS DISTINCT FROM 'string' OR jsonb_typeof(p->'FactorUpper') IS DISTINCT FROM 'string'
            OR p->>'FactorLower' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$' OR p->>'FactorUpper' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$'
            OR p->>'FactorSemantics' IS NULL OR p->>'FactorSemantics' NOT IN ('STEP_CONCENTRATION','CUMULATIVE_CONCENTRATION')
            OR EXISTS(SELECT 1 FROM unnest(ARRAY['Process','OutputFraction','Conditions']) k
                WHERE jsonb_typeof(p->k) IS DISTINCT FROM 'string' OR length(btrim(p->>k))=0)
            OR p->>'InterpretationKind' IS NOT NULL OR p->>'NdThresholdKind' IS NOT NULL
        THEN RAISE EXCEPTION 'Invalid residue process factor' USING ERRCODE='22023'; END IF;
        IF (p->>'FactorLower')::numeric > (p->>'FactorUpper')::numeric
        THEN RAISE EXCEPTION 'Invalid residue process interval' USING ERRCODE='22023'; END IF;
    ELSE RAISE EXCEPTION 'Unknown residue policy kind' USING ERRCODE='22023'; END IF;
    IF p->>'Kind'='INTERPRETATION' AND p->>'InterpretationKind'='NUMERIC_RELATIVE_UNCERTAINTY' THEN
        IF jsonb_typeof(p->'RelativeUncertaintyPercent') IS DISTINCT FROM 'string'
            OR p->>'RelativeUncertaintyPercent' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$'
        THEN RAISE EXCEPTION 'Explicit nonnegative relative uncertainty percentage required' USING ERRCODE='22023'; END IF;
    ELSIF p->>'RelativeUncertaintyPercent' IS NOT NULL OR COALESCE((p->>'RelativeUncertaintyAtZero')::boolean,false) THEN
        RAISE EXCEPTION 'Relative uncertainty only belongs to numeric relative interpretation' USING ERRCODE='22023';
    END IF;
    IF EXISTS(SELECT 1 FROM grainledger.catalog_residue_rule_versions r WHERE r.code=p->>'Code'
        AND (r.company_id IS NOT DISTINCT FROM company OR r.company_id IS NULL OR company IS NULL)
        AND (r.substance_id IS DISTINCT FROM (p->>'SubstanceId')::uuid
            OR r.payload->>'Kind' IS DISTINCT FROM p->>'Kind' OR r.commodity_id IS DISTINCT FROM (p->>'CommodityId')::uuid))
    THEN RAISE EXCEPTION 'Rule code must retain substance, kind and commodity scope' USING ERRCODE='23505'; END IF;
    IF EXISTS(SELECT 1 FROM grainledger.catalog_residue_rule_versions WHERE company_id IS NOT DISTINCT FROM company
        AND code=p->>'Code' AND version_no >= (p->>'VersionNo')::int)
    THEN RAISE EXCEPTION 'Residue version must advance in its scope' USING ERRCODE='23505'; END IF;
    IF p->>'SupersedesId' IS NOT NULL THEN
        SELECT * INTO previous FROM grainledger.catalog_residue_rule_versions WHERE id=(p->>'SupersedesId')::uuid;
        IF previous.id IS NULL OR previous.code IS DISTINCT FROM p->>'Code' OR previous.company_id IS DISTINCT FROM company
            OR previous.substance_id IS DISTINCT FROM (p->>'SubstanceId')::uuid OR previous.payload->>'Kind' IS DISTINCT FROM p->>'Kind'
            OR EXISTS(SELECT 1 FROM grainledger.catalog_residue_rule_versions WHERE (payload->>'SupersedesId')::uuid=(p->>'SupersedesId')::uuid)
        THEN RAISE EXCEPTION 'Invalid reviewed rule supersession' USING ERRCODE='23505'; END IF;
    END IF;
    INSERT INTO grainledger.catalog_residue_rule_versions(id,code,version_no,substance_id,commodity_id,effective_from,effective_to,published_by,published_at,payload,company_id)
        VALUES((p->>'Id')::uuid,p->>'Code',(p->>'VersionNo')::int,(p->>'SubstanceId')::uuid,(p->>'CommodityId')::uuid,
            (p->>'EffectiveFrom')::timestamptz,(p->>'EffectiveTo')::timestamptz,a,(p->>'PublishedAt')::timestamptz,p,company);
END $_$;

CREATE FUNCTION grainledger.catalog_reject_blend_assessment_change() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    RAISE EXCEPTION 'Published blend assessment policies are immutable' USING ERRCODE='23514';
END $$;

CREATE FUNCTION grainledger.catalog_reject_catalog_event_mutation() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'catalog_events is append-only';
END;
$$;

CREATE FUNCTION grainledger.catalog_residue_immutable() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN RAISE EXCEPTION 'Reviewed residue records are immutable' USING ERRCODE='55000'; END $$;

CREATE FUNCTION grainledger.catalog_validate_residue_policy_review(p jsonb) RETURNS void
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $_$
DECLARE now_at timestamptz := clock_timestamp();
BEGIN
    IF jsonb_typeof(p) IS DISTINCT FROM 'object'
        OR EXISTS(SELECT 1 FROM unnest(ARRAY['Id','EffectiveFrom','ReviewedAt','PublishedBy','PublishedAt',
            'EvidenceRef','ApplicabilityStatement','ReviewedBy','ReviewAuthority']) k
            WHERE jsonb_typeof(p->k) IS DISTINCT FROM 'string')
        OR p->>'Id'='00000000-0000-0000-0000-000000000000'
        OR EXISTS(SELECT 1 FROM unnest(ARRAY['EvidenceRef','ApplicabilityStatement','ReviewedBy','ReviewAuthority']) k
            WHERE length(btrim(p->>k))=0 OR length(p->>k)>4000)
        OR COALESCE(jsonb_typeof(p->'EffectiveTo'),'null') NOT IN ('null','string')
        OR COALESCE(jsonb_typeof(p->'SupersedesId'),'null') NOT IN ('null','string')
        OR EXISTS(SELECT 1 FROM unnest(ARRAY['Id','PublishedBy','SupersedesId','CompanyId','SubstanceId','CommodityId']) k
            WHERE p->>k IS NOT NULL AND (jsonb_typeof(p->k) IS DISTINCT FROM 'string'
                OR p->>k !~ '^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$'))
    THEN RAISE EXCEPTION 'Incomplete reviewed residue metadata' USING ERRCODE='22023'; END IF;
    -- PostgreSQL accepts relative dates and offsets outside DateTimeOffset's range;
    -- payloads must also be readable by System.Text.Json without normalization.
    IF EXISTS(SELECT 1 FROM unnest(ARRAY['EffectiveFrom','EffectiveTo','ReviewedAt','PublishedAt']) k
        WHERE p->>k IS NOT NULL AND p->>k !~
            '^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])T([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9](\.[0-9]{1,7})?(Z|[+-](0[0-9]|1[0-3]):[0-5][0-9]|[+-]14:00)$')
    THEN RAISE EXCEPTION 'Residue dates require JSON ISO DateTimeOffset syntax' USING ERRCODE='22023'; END IF;
    IF EXISTS(SELECT 1 FROM unnest(ARRAY['EffectiveFrom','EffectiveTo','ReviewedAt','PublishedAt']) k
        WHERE p->>k IS NOT NULL AND ((p->>k)::timestamptz < '0001-01-01 00:00:00+00'::timestamptz
            OR (p->>k)::timestamptz >= '10000-01-01 00:00:00+00'::timestamptz))
    THEN RAISE EXCEPTION 'Residue dates exceed DateTimeOffset range' USING ERRCODE='22023'; END IF;
    IF NOT isfinite((p->>'EffectiveFrom')::timestamptz)
        OR (p->>'EffectiveFrom')::timestamptz='0001-01-01 00:00:00+00'::timestamptz
        OR (p->>'EffectiveTo') IS NOT NULL AND (NOT isfinite((p->>'EffectiveTo')::timestamptz)
            OR (p->>'EffectiveTo')::timestamptz <= (p->>'EffectiveFrom')::timestamptz)
        OR NOT isfinite((p->>'ReviewedAt')::timestamptz)
        OR (p->>'ReviewedAt')::timestamptz='0001-01-01 00:00:00+00'::timestamptz
        OR (p->>'ReviewedAt')::timestamptz>now_at
        OR NOT isfinite((p->>'PublishedAt')::timestamptz)
        OR (p->>'PublishedAt')::timestamptz='0001-01-01 00:00:00+00'::timestamptz
        OR (p->>'PublishedAt')::timestamptz>now_at
    THEN RAISE EXCEPTION 'Invalid reviewed residue dates' USING ERRCODE='22023'; END IF;
END $_$;

CREATE FUNCTION grainledger.exports_protect_plan_draft() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Plan history cannot be deleted' USING ERRCODE='23514';
    END IF;
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
              AND s.id=NEW.specification_id AND c.status='PLANNED' AND c.target_mass_kg=NEW.target_mass_kg
        ) THEN
            RAISE EXCEPTION 'Plan must reference current container demand' USING ERRCODE='23514';
        END IF;
    END IF;
    RETURN NEW;
END; $$;

CREATE FUNCTION grainledger.exports_reject_historical_change() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'Export history is immutable' USING ERRCODE='23514';
END;
$$;

CREATE FUNCTION grainledger.ledger_accept_arrival(r uuid) RETURNS void
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE h grainledger.capture_procurements; s grainledger.ledger_receiving; ms grainledger.ledger_arrival_measurement_sets; snap jsonb; commodity uuid; c uuid; loc uuid; row record; event_id uuid; review grainledger.ledger_arrival_reviews; code text;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    SELECT * INTO STRICT h FROM grainledger.capture_procurements WHERE id=r;
    SELECT * INTO STRICT s FROM grainledger.ledger_receiving WHERE procurement_id=r AND accepted_measurement_set_id IS NOT NULL;
    SELECT * INTO STRICT ms FROM grainledger.ledger_arrival_measurement_sets WHERE id=s.accepted_measurement_set_id;
    SELECT * INTO review FROM grainledger.ledger_arrival_reviews WHERE id=s.accepted_review_id;
    SELECT payload->'result' INTO snap FROM grainledger.capture_business_events WHERE id=h.event_id;
    c:=(snap->>'CommodityId')::uuid; loc:=(snap->>'ReceivingLocationId')::uuid;
    PERFORM grainledger.ledger_require_active_location(loc);
    IF EXISTS(SELECT 1 FROM grainledger.ledger_lots WHERE receipt_id=r) THEN RAISE EXCEPTION 'Arrival already posted' USING ERRCODE='23514'; END IF;
    IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_locations WHERE id=loc AND facility_id=h.collection_site_id AND kind IN ('STORE','SILO','COMPARTMENT') AND (owner_id=h.company_id OR owner_id IS NULL AND (kind<>'COMPARTMENT' OR cleared))) THEN RAISE EXCEPTION 'Receiving location no longer eligible' USING ERRCODE='23514'; END IF;
    SELECT ledger_commodity_id INTO commodity FROM grainledger.ledger_catalog_commodities WHERE catalog_commodity_id=c;
    IF NOT FOUND THEN PERFORM grainledger.ledger_provision_catalog_commodity(c); SELECT ledger_commodity_id INTO STRICT commodity FROM grainledger.ledger_catalog_commodities WHERE catalog_commodity_id=c; END IF;
    PERFORM grainledger.ledger_credit_location(loc,h.company_id);
    FOR row IN SELECT i.*,sl.id source_id,sl.lot_id,ps.plot_id,ps.season_id,ps.rice_variety,m.actual_kg,m.measured_value actual_value,m.measured_unit actual_unit
        FROM grainledger.capture_procurement_items i JOIN grainledger.capture_source_lots sl ON sl.procurement_item_id=i.id JOIN grainledger.capture_plot_seasons ps ON ps.id=i.plot_season_id
        JOIN grainledger.ledger_arrival_measurements m ON m.procurement_item_id=i.id AND m.measurement_set_id=ms.id WHERE i.procurement_id=r ORDER BY i.line_no
    LOOP
        code:=grainledger.ledger_source_lot_code(r,row.lot_id);
        INSERT INTO grainledger.ledger_lots(id,code,owner_id,commodity_id,stage,product_identity,receipt_id,receipt_line_id,code_origin_id)
        VALUES(row.lot_id,code,h.company_id,commodity,'RAW_PADDY','RAW_PADDY',r,row.id,grainledger.ledger_register_code(code,row.lot_id,NULL,NULL));
        event_id:=gen_random_uuid();
        INSERT INTO grainledger.ledger_events(id,kind,actor_id,lot_id,details,occurred_at) VALUES(event_id,'RECEIPT',ms.operator_id,row.lot_id,
            jsonb_build_object('receiptId',r,'receiptLineId',row.id,'captureEventId',h.event_id,'sourceLotId',row.source_id,'catalogCommodityId',c,
                'farmerId',h.farmer_id,'plotSeasonId',row.plot_season_id,'plotId',row.plot_id,'seasonId',row.season_id,'riceVariety',row.rice_variety,
                'originalQuantity',row.measured_value::text,'originalUnit',row.measured_unit,'sourceQuantityKg',row.net_mass_kg::text,
                'arrivalQuantity',row.actual_value::text,'arrivalUnit',row.actual_unit,'quantityKg',row.actual_kg::text,'moisturePercent',row.moisture_percent::text,
                'measurementSetId',ms.id,'managerReviewId',review.id,'managerId',review.manager_id,'evidenceId',review.evidence_id,'receivingStatus',s.status),ms.captured_at);
        PERFORM grainledger.ledger_post(event_id,row.lot_id,loc,row.actual_kg);
        INSERT INTO grainledger.ledger_contributions VALUES(row.lot_id,row.lot_id,row.actual_kg);
        PERFORM grainledger.ledger_pool_ingress(COALESCE(review.manager_id,ms.operator_id),COALESCE(review.operation_id,ms.operation_id),
            'ARRIVAL',loc,row.lot_id,row.actual_kg);
    END LOOP;
END $$;

CREATE FUNCTION grainledger.ledger_arrival_command(a uuid, r uuid, k text, j jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $_$
DECLARE h grainledger.capture_procurements; s grainledger.ledger_receiving; prior grainledger.ledger_arrival_operations; op uuid; set_id uuid; review_id uuid; x jsonb; v numeric; kg numeric; unit text; action text; result jsonb; normalized jsonb; threshold jsonb; percentage numeric(8,6);
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    SELECT * INTO STRICT h FROM grainledger.capture_procurements WHERE id=r AND receiving_workflow='PHYSICAL_ARRIVAL_V1';
    PERFORM grainledger.ledger_require_staff(a,h.collection_site_id,CASE k WHEN 'measurements' THEN ARRAY['MillOperator'] WHEN 'reviews' THEN ARRAY['MillManager'] ELSE ARRAY[]::text[] END);
    PERFORM grainledger.ledger_require_active_location((SELECT (payload->'result'->>'ReceivingLocationId')::uuid FROM grainledger.capture_business_events WHERE id=h.event_id));
    op:=(j->>'operationId')::uuid;
    IF op IS NULL OR op='00000000-0000-0000-0000-000000000000' THEN RAISE EXCEPTION 'Operation ID required' USING ERRCODE='22023'; END IF;
    normalized:=j;
    IF k='measurements' AND jsonb_typeof(j->'items')='array' THEN normalized:=jsonb_set(j,'{items}',(SELECT jsonb_agg(entry.value ORDER BY entry.value->>'procurementItemId') FROM jsonb_array_elements(j->'items') entry)); END IF;
    SELECT * INTO prior FROM grainledger.ledger_arrival_operations WHERE operation_id=op;
    IF FOUND THEN
        IF prior.actor_id<>a THEN RAISE EXCEPTION 'Operation actor mismatch' USING ERRCODE='42501'; END IF;
        IF prior.procurement_id<>r OR prior.kind<>k OR prior.payload<>normalized THEN RAISE EXCEPTION 'Operation ID used with different content' USING ERRCODE='23514'; END IF;
        RETURN prior.result;
    END IF;
    SELECT * INTO STRICT s FROM grainledger.ledger_receiving WHERE procurement_id=r FOR UPDATE;
    IF (j->>'expectedReceivingVersion')::integer IS DISTINCT FROM s.version THEN RAISE EXCEPTION 'Receiving version stale' USING ERRCODE='23514'; END IF;
    IF k='measurements' THEN
        IF s.status NOT IN ('PENDING_ARRIVAL','REMEASUREMENT_REQUESTED') THEN RAISE EXCEPTION 'Arrival measurement not allowed in current state' USING ERRCODE='23514'; END IF;
        IF jsonb_typeof(j->'items') IS DISTINCT FROM 'array' OR jsonb_array_length(j->'items') NOT BETWEEN 1 AND 100
           OR jsonb_array_length(j->'items')<>(SELECT count(*) FROM grainledger.capture_procurement_items WHERE procurement_id=r)
           OR (SELECT count(DISTINCT (entry.value->>'procurementItemId')::uuid) FROM jsonb_array_elements(j->'items') entry)<>jsonb_array_length(j->'items')
           OR j->>'capturedAt' IS NULL OR NOT isfinite((j->>'capturedAt')::timestamptz)
           OR j->>'observations' IS NULL OR length(j->>'observations')>4000 THEN RAISE EXCEPTION 'Complete arrival set, finite timestamp and observations required' USING ERRCODE='22023'; END IF;
        threshold:=grainledger.ledger_receiving_threshold_effective(h.company_id);
        percentage:=(threshold->>'percentage')::numeric(8,6);
        set_id:=gen_random_uuid();
        INSERT INTO grainledger.ledger_arrival_measurement_sets(id,procurement_id,operation_id,operator_id,captured_at,observations,expected_version,
            threshold_company_id,threshold_percentage,threshold_source,threshold_revision)
        VALUES(set_id,r,op,a,(j->>'capturedAt')::timestamptz,j->>'observations',s.version,
            h.company_id,percentage,threshold->>'source',(threshold->>'revision')::uuid);
        FOR x IN SELECT value FROM jsonb_array_elements(j->'items') LOOP
            IF NOT EXISTS(SELECT 1 FROM grainledger.capture_procurement_items WHERE id=(x->>'procurementItemId')::uuid AND procurement_id=r)
               OR jsonb_typeof(x->'measuredValue') IS DISTINCT FROM 'string' OR x->>'measuredValue' !~ '^[0-9]{1,12}(\.[0-9]{1,12})?$' THEN RAISE EXCEPTION 'Invalid arrival line or decimal precision' USING ERRCODE='22023'; END IF;
            v:=(x->>'measuredValue')::numeric; unit:=x->>'measuredUnit';
            kg:=CASE unit WHEN 'KG' THEN v WHEN 'G' THEN v/1000 WHEN 'T' THEN v*1000 END;
            IF kg IS NULL OR kg<=0 OR kg>=100000000000000 OR trunc(kg,6)<>kg THEN RAISE EXCEPTION 'Arrival mass must be positive exact numeric(20,6) kg; zero/full loss unsupported' USING ERRCODE='22023'; END IF;
            IF NOT EXISTS(SELECT 1 FROM grainledger.catalog_measurement_units WHERE code=unit AND quantity_kind='MASS' AND NOT is_deleted) THEN RAISE EXCEPTION 'Arrival mass unit not configured' USING ERRCODE='23514'; END IF;
            INSERT INTO grainledger.ledger_arrival_measurements VALUES(set_id,(x->>'procurementItemId')::uuid,v,unit,kg);
        END LOOP;
        IF EXISTS(SELECT 1 FROM grainledger.ledger_arrival_measurements m JOIN grainledger.capture_procurement_items i ON i.id=m.procurement_item_id
            WHERE m.measurement_set_id=set_id AND (m.actual_kg>i.net_mass_kg OR (i.net_mass_kg-m.actual_kg)*100>i.net_mass_kg*percentage)) THEN
            UPDATE grainledger.ledger_receiving SET status='AWAITING_MANAGER_REVIEW',version=version+1,latest_measurement_set_id=set_id WHERE procurement_id=r;
        ELSE
            UPDATE grainledger.ledger_receiving SET status=CASE WHEN EXISTS(SELECT 1 FROM grainledger.ledger_arrival_measurements m JOIN grainledger.capture_procurement_items i ON i.id=m.procurement_item_id
                WHERE m.measurement_set_id=set_id AND m.actual_kg<i.net_mass_kg) THEN 'RECEIVED_WITHIN_TOLERANCE' ELSE 'RECEIVED_MATCHED' END,
                version=version+1,latest_measurement_set_id=set_id,accepted_measurement_set_id=set_id WHERE procurement_id=r;
            PERFORM grainledger.ledger_accept_arrival(r);
        END IF;
    ELSE
        IF s.status NOT IN ('AWAITING_MANAGER_REVIEW','BLOCKED_UNRESOLVED') OR (j->>'measurementSetId')::uuid IS DISTINCT FROM s.latest_measurement_set_id THEN RAISE EXCEPTION 'Review state or measurement set stale' USING ERRCODE='23514'; END IF;
        PERFORM grainledger.ledger_require_evidence(a,(j->>'evidenceId')::uuid,h.company_id,h.collection_site_id);
        IF j->>'reason' IS NULL OR length(trim(j->>'reason')) NOT BETWEEN 1 AND 4000 THEN RAISE EXCEPTION 'Review reason required' USING ERRCODE='22023'; END IF;
        action:=j->>'action';
        IF action IS NULL OR action NOT IN ('APPROVE_SHORTAGE','REQUEST_REMEASUREMENT','KEEP_BLOCKED') THEN RAISE EXCEPTION 'Unsupported receiving review action' USING ERRCODE='22023'; END IF;
        IF action='APPROVE_SHORTAGE' AND (EXISTS(SELECT 1 FROM grainledger.ledger_arrival_measurements m JOIN grainledger.capture_procurement_items i ON i.id=m.procurement_item_id WHERE m.measurement_set_id=s.latest_measurement_set_id AND m.actual_kg>i.net_mass_kg)
            OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_arrival_measurements m JOIN grainledger.capture_procurement_items i ON i.id=m.procurement_item_id WHERE m.measurement_set_id=s.latest_measurement_set_id AND m.actual_kg<i.net_mass_kg)) THEN RAISE EXCEPTION 'Only measured shortage without any surplus can be approved' USING ERRCODE='23514'; END IF;
        review_id:=gen_random_uuid();
        INSERT INTO grainledger.ledger_arrival_reviews(id,procurement_id,measurement_set_id,operation_id,expected_version,action,reason,evidence_id,manager_id)
        VALUES(review_id,r,s.latest_measurement_set_id,op,s.version,action,j->>'reason',(j->>'evidenceId')::uuid,a);
        UPDATE grainledger.ledger_receiving SET status=CASE action WHEN 'APPROVE_SHORTAGE' THEN 'RECEIVED_SHORTAGE_APPROVED' WHEN 'REQUEST_REMEASUREMENT' THEN 'REMEASUREMENT_REQUESTED' ELSE 'BLOCKED_UNRESOLVED' END,
            version=version+1,accepted_measurement_set_id=CASE WHEN action='APPROVE_SHORTAGE' THEN latest_measurement_set_id END,
            accepted_review_id=CASE WHEN action='APPROVE_SHORTAGE' THEN review_id END WHERE procurement_id=r;
        IF action='APPROVE_SHORTAGE' THEN PERFORM grainledger.ledger_accept_arrival(r); END IF;
    END IF;
    IF grainledger.ledger_procurement_package_complete(r) IS DISTINCT FROM true THEN RAISE EXCEPTION 'Receiving package incomplete' USING ERRCODE='23514'; END IF;
    result:=grainledger.ledger_receiving_detail(a,r);
    INSERT INTO grainledger.ledger_arrival_operations(operation_id,procurement_id,actor_id,kind,payload,result) VALUES(op,r,a,k,normalized,result);
    RETURN result;
END $_$;

CREATE FUNCTION grainledger.ledger_arrival_package_guard() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE r uuid;
BEGIN
    IF TG_TABLE_NAME='ledger_arrival_measurements' THEN SELECT procurement_id INTO r FROM grainledger.ledger_arrival_measurement_sets WHERE id=NEW.measurement_set_id;
    ELSE r:=NEW.procurement_id; END IF;
    IF grainledger.ledger_procurement_package_complete(r) IS DISTINCT FROM true THEN RAISE EXCEPTION 'Receiving source/stock package incomplete' USING ERRCODE='23514'; END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION grainledger.ledger_assigned(a uuid, f uuid) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT EXISTS(SELECT 1 FROM grainledger.ledger_assignments x JOIN grainledger.ledger_facilities facility ON facility.id=x.facility_id
        JOIN grainledger.identity_users u ON u."Id"=x.user_id
        WHERE x.user_id=a AND x.facility_id=f AND NOT x.is_deleted AND NOT facility.is_deleted
          AND u."IsActive" AND u."Role" IN ('MillOperator','MillManager'))
$$;

CREATE FUNCTION grainledger.ledger_available_lots_page(a uuid, p jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE
    page_number integer:=coalesce((p->>'page')::integer,1);
    page_size integer:=coalesce((p->>'pageSize')::integer,25);
    sort_key text:=coalesce(p->>'sortBy','code');
    direction text:=coalesce(p->>'sortDirection','asc');
    requested_owner uuid:=NULLIF(p->>'ownerId','')::uuid;
    requested_facility uuid:=NULLIF(p->>'facilityId','')::uuid;
    requested_location uuid:=NULLIF(p->>'locationId','')::uuid;
    requested_ids uuid[];
    result jsonb;
BEGIN
    IF grainledger.ledger_role(a) IS NULL THEN RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501'; END IF;
    IF page_number<1 OR page_size NOT BETWEEN 1 AND 100 OR (page_number::bigint-1)*page_size>2147483647
       OR direction NOT IN ('asc','desc') OR NOT sort_key=ANY(ARRAY['code','stage','owner','quantity']) THEN
        RAISE EXCEPTION 'Invalid pagination or sort direction' USING ERRCODE='22023';
    END IF;
    IF p ? 'ids' THEN
        requested_ids:=string_to_array(p->>'ids',',')::uuid[];
        IF cardinality(requested_ids) NOT BETWEEN 1 AND 100 THEN RAISE EXCEPTION 'IDs requires 1–100 UUIDs' USING ERRCODE='22023'; END IF;
    END IF;
    IF p ? 'stage' AND p->>'stage' NOT IN ('RAW_PADDY','DRIED_PADDY','HEAD_RICE','BROKEN_RICE','BRAN','HUSK','BLENDED_RICE','BAGGED_RICE') THEN
        RAISE EXCEPTION 'Invalid stock stage' USING ERRCODE='22023';
    END IF;
    IF p ? 'status' THEN RAISE EXCEPTION 'Invalid status filter' USING ERRCODE='22023'; END IF;
    IF p ? 'kind' THEN RAISE EXCEPTION 'Invalid kind filter' USING ERRCODE='22023'; END IF;
    IF p ? 'queueOnly' THEN RAISE EXCEPTION 'Invalid queue filter' USING ERRCODE='22023'; END IF;
    WITH candidates AS MATERIALIZED (
        SELECT l.id,l.owner_id,o."Name" owner_name,l.code,l.stage,
            coalesce((SELECT sum(pos.quantity_kg) FROM grainledger.ledger_positions pos
                JOIN grainledger.ledger_locations lc ON lc.id=pos.location_id
                LEFT JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.location_id=lc.id
                WHERE pos.lot_id=l.id AND pos.quantity_kg>0
                  AND (NOT lc.mixed_bulk OR pool.lot_id=l.id)
                  AND (grainledger.ledger_own(a,l.owner_id) OR grainledger.ledger_assigned(a,lc.facility_id))
                  AND (requested_facility IS NULL OR lc.facility_id=requested_facility)
                  AND (requested_location IS NULL OR lc.id=requested_location)),0) quantity
        FROM grainledger.ledger_lots l JOIN grainledger.identity_organizations o ON o."Id"=l.owner_id
        WHERE grainledger.ledger_can_read_lot(a,l.id)
          AND (requested_owner IS NULL OR l.owner_id=requested_owner)
          AND (p->>'stage' IS NULL OR l.stage=p->>'stage')
          AND (requested_facility IS NULL AND requested_location IS NULL OR EXISTS(
              SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_locations lc ON lc.id=pos.location_id
              WHERE pos.lot_id=l.id
                AND (grainledger.ledger_own(a,l.owner_id) OR grainledger.ledger_assigned(a,lc.facility_id))
                AND (requested_facility IS NULL OR lc.facility_id=requested_facility)
                AND (requested_location IS NULL OR lc.id=requested_location)))
          AND (requested_ids IS NULL OR l.id=ANY(requested_ids))
          AND (requested_ids IS NOT NULL OR NOT (
              EXISTS(SELECT 1 FROM grainledger.ledger_custody_events ce WHERE ce.input_lot_id=l.id OR ce.output_lot_id=l.id)
              OR EXISTS(SELECT 1 FROM grainledger.ledger_custody_allocations ca WHERE ca.input_lot_id=l.id OR ca.output_lot_id=l.id)
              OR grainledger.ledger_mixed_bulk_custody_derived(l.id)
          ) OR EXISTS(
              SELECT 1 FROM grainledger.ledger_positions pos
              JOIN grainledger.ledger_locations lc ON lc.id=pos.location_id
              LEFT JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.location_id=lc.id
              WHERE pos.lot_id=l.id AND pos.quantity_kg>0
                AND (NOT lc.mixed_bulk OR pool.lot_id=l.id)
                AND (grainledger.ledger_own(a,l.owner_id) OR grainledger.ledger_assigned(a,lc.facility_id))
                AND (requested_facility IS NULL OR lc.facility_id=requested_facility)
                AND (requested_location IS NULL OR lc.id=requested_location)))
          AND (p->>'search' IS NULL OR concat_ws(' ',l.code,l.stage,o."Name") ILIKE '%'||(p->>'search')||'%')
    ), ordered AS (
        SELECT c.*,row_number() OVER (ORDER BY
            CASE WHEN direction='asc' THEN CASE sort_key WHEN 'code' THEN c.code WHEN 'stage' THEN c.stage WHEN 'owner' THEN c.owner_name END END ASC NULLS LAST,
            CASE WHEN direction='desc' THEN CASE sort_key WHEN 'code' THEN c.code WHEN 'stage' THEN c.stage WHEN 'owner' THEN c.owner_name END END DESC NULLS LAST,
            CASE WHEN direction='asc' AND sort_key='quantity' THEN c.quantity END ASC NULLS LAST,
            CASE WHEN direction='desc' AND sort_key='quantity' THEN c.quantity END DESC NULLS LAST,c.id) ordinal
        FROM candidates c
    ), selected AS MATERIALIZED (
        SELECT * FROM ordered WHERE ordinal>(page_number::bigint-1)*page_size AND ordinal<=page_number::bigint*page_size
    )
    SELECT jsonb_build_object(
        'items',coalesce((SELECT jsonb_agg(grainledger.ledger_lot_json(a,x.id,false)||jsonb_build_object('ownerName',x.owner_name) ORDER BY x.ordinal) FROM selected x),'[]'::jsonb),
        'page',page_number,'pageSize',page_size,'totalCount',(SELECT count(*) FROM candidates),
        'hasMore',(SELECT count(*) FROM candidates)>page_number::bigint*page_size)
    INTO result;
    RETURN result;
END $$;

CREATE FUNCTION grainledger.ledger_blocked(l uuid, ignore_correction uuid DEFAULT NULL::uuid) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT EXISTS(SELECT 1 FROM grainledger.ledger_effective_hold_ids(l))
        OR grainledger.ledger_mixed_bulk_independent_blocked(l,ignore_correction)
$$;

CREATE FUNCTION grainledger.ledger_can_read_lot(a uuid, l uuid) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT EXISTS (
        SELECT 1 FROM grainledger.ledger_lots x WHERE x.id=l AND (
            grainledger.ledger_role(a) IN ('CustomerManager','ExportStaff','PurchasingStaff')
            AND grainledger.ledger_own(a,x.owner_id)
            OR grainledger.ledger_role(a) IN ('MillManager','MillOperator') AND EXISTS (
                SELECT 1 FROM grainledger.ledger_positions p
                JOIN grainledger.ledger_locations loc ON loc.id=p.location_id
                WHERE p.lot_id=l AND grainledger.ledger_assigned(a,loc.facility_id)
            )
        )
    )
$$;

CREATE FUNCTION grainledger.ledger_can_read_operation(a uuid, op uuid) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT EXISTS (
        SELECT 1 FROM grainledger.ledger_operations x WHERE x.id=op AND NOT x.is_deleted
          AND EXISTS(SELECT 1 FROM grainledger.ledger_facilities WHERE id=x.facility_id AND NOT is_deleted) AND (
            grainledger.ledger_role(a) IN ('CustomerManager','ExportStaff','PurchasingStaff')
            AND grainledger.ledger_own(a,x.owner_id)
            OR grainledger.ledger_role(a) IN ('MillManager','MillOperator')
            AND grainledger.ledger_assigned(a,x.facility_id)
        )
    )
$$;

CREATE FUNCTION grainledger.ledger_capture_procurement_immutable() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE committed boolean;
BEGIN
    IF TG_TABLE_NAME='capture_procurements' THEN committed:=OLD.status='COMMITTED';
    ELSIF TG_TABLE_NAME='capture_procurement_items' THEN
        SELECT status='COMMITTED' INTO committed FROM grainledger.capture_procurements WHERE id=OLD.procurement_id;
    ELSIF TG_TABLE_NAME='capture_source_lots' THEN
        SELECT h.status='COMMITTED' INTO committed FROM grainledger.capture_procurement_items i
        JOIN grainledger.capture_procurements h ON h.id=i.procurement_id WHERE i.id=OLD.procurement_item_id;
    ELSIF TG_TABLE_NAME='capture_business_events' THEN committed:=OLD.event_type='PROCUREMENT_COMMITTED';
    ELSE committed:=OLD.payload->>'operationType'='PROCUREMENT_COMMITTED'; END IF;
    IF committed THEN RAISE EXCEPTION 'Committed procurement history is immutable' USING ERRCODE='23514'; END IF;
    IF TG_OP='DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION grainledger.ledger_capture_procurement_package() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE receipt uuid;
BEGIN
    IF TG_TABLE_NAME='capture_procurements' THEN receipt:=NEW.id;
    ELSIF TG_TABLE_NAME='capture_procurement_items' THEN receipt:=NEW.procurement_id;
    ELSIF TG_TABLE_NAME='capture_source_lots' THEN
        SELECT procurement_id INTO receipt FROM grainledger.capture_procurement_items WHERE id=NEW.procurement_item_id;
    ELSIF TG_TABLE_NAME='capture_business_events' THEN
        IF NEW.event_type<>'PROCUREMENT_COMMITTED' THEN RETURN NEW; END IF;
        receipt:=(NEW.payload->>'procurementId')::uuid;
    ELSE
        IF NEW.payload->>'operationType' IS DISTINCT FROM 'PROCUREMENT_COMMITTED' THEN RETURN NEW; END IF;
        SELECT id INTO receipt FROM grainledger.capture_procurements WHERE event_id=NEW.applied_event_id;
    END IF;
    IF TG_TABLE_NAME IN ('capture_business_events','capture_offline_operations') OR receipt IS NULL
       OR EXISTS(SELECT 1 FROM grainledger.capture_procurements WHERE id=receipt AND status='COMMITTED') THEN
        IF grainledger.ledger_procurement_package_complete(receipt) IS DISTINCT FROM true THEN
            RAISE EXCEPTION 'Committed procurement requires complete immutable provenance and Ledger stock' USING ERRCODE='23514';
        END IF;
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION grainledger.ledger_command(a uuid, action text, p jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE selected_equipment grainledger.ledger_equipment; result jsonb;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    IF grainledger.ledger_role(a) IS NULL THEN RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501'; END IF;
    IF p ?| ARRAY['createdBy','created_by'] THEN
        RAISE EXCEPTION 'Creator derives from authenticated actor' USING ERRCODE='22023';
    END IF;
    IF action='equipment_retire' THEN
        IF grainledger.ledger_role(a)<>'Administrator' THEN
            RAISE EXCEPTION 'Setup authority denied' USING ERRCODE='42501';
        END IF;
        SELECT * INTO STRICT selected_equipment FROM grainledger.ledger_equipment WHERE id=(p->>'id')::uuid FOR UPDATE;
        IF selected_equipment.created_by IS DISTINCT FROM a THEN
            RAISE EXCEPTION 'Only creator may manage setup record' USING ERRCODE='42501';
        END IF;
        IF selected_equipment.is_deleted THEN
            RAISE EXCEPTION 'Removed setup record cannot be changed or restored' USING ERRCODE='23514';
        END IF;
        IF jsonb_typeof(p->'version') IS DISTINCT FROM 'string' OR NULLIF(p->>'version','') IS NULL THEN
            RAISE EXCEPTION 'Row version string required' USING ERRCODE='22023';
        END IF;
        IF selected_equipment.version IS DISTINCT FROM (p->>'version')::uuid THEN
            RAISE EXCEPTION 'Setup record version stale' USING ERRCODE='23514';
        END IF;
        IF grainledger.ledger_equipment_has_pending_work(selected_equipment.id) THEN
            RAISE EXCEPTION 'Equipment has active drafts or pending corrections; resolve them before retirement' USING ERRCODE='23514';
        END IF;
        UPDATE grainledger.ledger_equipment SET is_deleted=true,version=gen_random_uuid() WHERE id=selected_equipment.id;
        RETURN jsonb_build_object('id',selected_equipment.id);
    END IF;
    -- Check before idempotent draft caches too: replay cannot revive retired equipment.
    IF action='draft' AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_equipment e
        WHERE e.id=(p->>'equipmentId')::uuid AND e.facility_id=(p->>'facilityId')::uuid AND NOT e.is_deleted) THEN
        RAISE EXCEPTION 'Draft equipment outside assigned facility' USING ERRCODE='42501';
    END IF;
    result:=grainledger.ledger_command_before_equipment_retirement(a,action,p);
    IF action IN ('equipment','equipment_update') THEN
        SELECT * INTO STRICT selected_equipment FROM grainledger.ledger_equipment WHERE id=(result->>'id')::uuid;
        result:=result||jsonb_build_object('canRetire',coalesce(selected_equipment.created_by=a
            AND grainledger.ledger_role(a)='Administrator' AND NOT selected_equipment.is_deleted
            AND grainledger.ledger_record_in_use('equipment',selected_equipment.id)
            AND NOT grainledger.ledger_equipment_has_pending_work(selected_equipment.id),false));
    END IF;
    RETURN result;
END $$;

CREATE FUNCTION grainledger.ledger_command_before_equipment_retirement(a uuid, action text, p jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $_$
<<mixed_command>>
DECLARE
    result jsonb; item jsonb; saved grainledger.ledger_operations; location grainledger.ledger_locations; source grainledger.ledger_lots;
    source_actual uuid; destination uuid; source_location uuid; facility uuid; current_pool uuid;
    command_id uuid; quantity numeric; expected_revision bigint; selected_pool grainledger.ledger_mixed_bulk_pools;
    prior grainledger.ledger_events; cache grainledger.ledger_mixed_bulk_command_results; cache_action text;
    hold grainledger.ledger_holds; affected_facility uuid; source_mixed boolean; destination_mixed boolean;
    requested_mode boolean;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    IF grainledger.ledger_role(a) IS NULL THEN RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501'; END IF;
    IF p ?| ARRAY['createdBy','created_by'] THEN
        RAISE EXCEPTION 'Creator derives from authenticated actor' USING ERRCODE='22023';
    END IF;
    command_id:=NULLIF(p->>'id','')::uuid;
    cache_action:=CASE WHEN action IN ('draft','locations_update') THEN action||':'||COALESCE(p->>'version','create')
        WHEN action IN ('locations','mixed-bulk-policies','transfer','commit','hold') THEN action ELSE NULL END;
    IF cache_action IS NOT NULL THEN
        SELECT * INTO cache FROM grainledger.ledger_mixed_bulk_command_results c
        WHERE c.action=cache_action AND c.command_id=mixed_command.command_id;
        IF FOUND THEN
            IF cache.actor_id IS DISTINCT FROM a OR cache.request IS DISTINCT FROM p THEN
                RAISE EXCEPTION 'Command retry conflicts with original actor or payload' USING ERRCODE='23514';
            END IF;
            IF action IN ('draft','commit') THEN
                SELECT * INTO STRICT saved FROM grainledger.ledger_operations WHERE id=command_id;
                PERFORM grainledger.ledger_require_staff(a,saved.facility_id,ARRAY['MillOperator']);
            ELSIF action='transfer' THEN
                SELECT facility_id INTO STRICT facility FROM grainledger.ledger_locations WHERE id=(p->>'fromLocationId')::uuid;
                PERFORM grainledger.ledger_require_staff(a,facility,CASE WHEN p->>'holdId' IS NULL THEN ARRAY['MillOperator'] ELSE ARRAY['MillManager'] END);
                SELECT facility_id INTO STRICT facility FROM grainledger.ledger_locations WHERE id=(p->>'toLocationId')::uuid;
                PERFORM grainledger.ledger_require_staff(a,facility,CASE WHEN p->>'holdId' IS NULL THEN ARRAY['MillOperator'] ELSE ARRAY['MillManager'] END);
            ELSIF action='hold' THEN
                SELECT * INTO STRICT source FROM grainledger.ledger_lots WHERE id=(p->>'lotId')::uuid;
                IF grainledger.ledger_role(a)<>'CustomerManager' OR NOT grainledger.ledger_own(a,source.owner_id) THEN
                    RAISE EXCEPTION 'HOLD authority denied' USING ERRCODE='42501';
                END IF;
            ELSIF grainledger.ledger_role(a)<>'Administrator' THEN
                RAISE EXCEPTION 'Setup authority denied' USING ERRCODE='42501';
            END IF;
            IF action IN ('locations','locations_update') THEN
                SELECT * INTO STRICT location FROM grainledger.ledger_locations WHERE id=command_id FOR UPDATE;
                IF location.is_deleted THEN
                    RAISE EXCEPTION 'Removed setup record cannot be changed or restored' USING ERRCODE='23514';
                END IF;
                IF location.version IS DISTINCT FROM (cache.result->>'version')::uuid THEN
                    RAISE EXCEPTION 'Setup record version stale' USING ERRCODE='23514';
                END IF;
            END IF;
            RETURN cache.result;
        END IF;
    END IF;

    IF action='mixed-bulk-policies' THEN
        IF grainledger.ledger_role(a)<>'Administrator' THEN RAISE EXCEPTION 'Setup authority denied' USING ERRCODE='42501'; END IF;
        IF p->'identityScopeConfirmed' IS DISTINCT FROM 'true'::jsonb THEN
            RAISE EXCEPTION 'Identity scope confirmation required' USING ERRCODE='23514';
        END IF;
        SELECT * INTO STRICT location FROM grainledger.ledger_locations WHERE id=(p->>'locationId')::uuid FOR UPDATE;
        PERFORM grainledger.ledger_require_active_location(location.id);
        IF NOT location.mixed_bulk OR location.kind NOT IN ('SILO','STORE','COMPARTMENT') THEN
            RAISE EXCEPTION 'Policy requires explicitly enabled eligible storage location' USING ERRCODE='23514';
        END IF;
        IF NOT EXISTS(SELECT 1 FROM grainledger.identity_organizations WHERE "Id"=(p->>'ownerId')::uuid AND "Kind"='Customer')
           OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_commodities WHERE id=(p->>'commodityId')::uuid)
           OR NULLIF(trim(p->>'productIdentity'),'') IS NULL OR p->>'effectiveFrom' IS NULL THEN
            RAISE EXCEPTION 'Explicit customer, commodity, stage, product and effective date required' USING ERRCODE='23514';
        END IF;
        PERFORM grainledger.ledger_require_evidence(a,(p->>'evidenceId')::uuid,(p->>'ownerId')::uuid,location.facility_id);
        INSERT INTO grainledger.ledger_mixed_bulk_policies(id,location_id,owner_id,commodity_id,stage,product_identity,
            location_kind,effective_from,evidence_id,approved_by)
        VALUES(command_id,location.id,(p->>'ownerId')::uuid,(p->>'commodityId')::uuid,p->>'stage',p->>'productIdentity',
            location.kind,(p->>'effectiveFrom')::timestamptz,(p->>'evidenceId')::uuid,a);
        result:=p||jsonb_build_object('approvedBy',a,'locationKind',location.kind);
    ELSIF action IN ('locations','locations_update') THEN
        IF p ? 'mixedBulk' AND jsonb_typeof(p->'mixedBulk') IS DISTINCT FROM 'boolean' THEN
            RAISE EXCEPTION 'mixedBulk must be boolean' USING ERRCODE='22023';
        END IF;
        IF action='locations_update' THEN
            SELECT * INTO STRICT location FROM grainledger.ledger_locations WHERE id=command_id FOR UPDATE;
            requested_mode:=COALESCE((p->>'mixedBulk')::boolean,location.mixed_bulk);
            IF requested_mode IS DISTINCT FROM location.mixed_bulk THEN
                IF EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE location_id=command_id AND quantity_kg>0)
                   OR EXISTS(SELECT 1 FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=command_id AND lot_id IS NOT NULL)
                   OR EXISTS(SELECT 1 FROM grainledger.ledger_operations op WHERE NOT op.is_deleted AND op.status='DRAFT' AND (
                       EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'inputs') x WHERE (x->>'locationId')::uuid=command_id)
                       OR EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'outputs') x WHERE (x->>'locationId')::uuid=command_id)))
                   OR EXISTS(SELECT 1 FROM grainledger.ledger_corrections c WHERE c.status IN ('REQUESTED','RECONCILED') AND (
                       EXISTS(SELECT 1 FROM jsonb_array_elements(COALESCE(c.reconciliation,'[]'::jsonb)) x WHERE (x->>'locationId')::uuid=command_id)
                       OR EXISTS(SELECT 1 FROM grainledger.ledger_operations op WHERE op.id=c.operation_id AND (
                           EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'inputs') x WHERE (x->>'locationId')::uuid=command_id)
                           OR EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'outputs') x WHERE (x->>'locationId')::uuid=command_id))))) THEN
                    RAISE EXCEPTION 'Mixed-bulk mode change requires empty storage without unresolved dependent work' USING ERRCODE='23514';
                END IF;
                IF location.mixed_bulk AND NOT location.cleared AND EXISTS(
                    SELECT 1 FROM grainledger.ledger_custody_events ce WHERE ce.location_id=mixed_command.command_id) THEN
                    RAISE EXCEPTION 'Mixed-bulk mode change requires recorded clearance after drain' USING ERRCODE='23514';
                END IF;
            END IF;
        ELSE requested_mode:=COALESCE((p->>'mixedBulk')::boolean,false);
        END IF;
        result:=grainledger.ledger_command_pre_mixed(a,action,p);
        UPDATE grainledger.ledger_locations SET mixed_bulk=requested_mode WHERE id=command_id;
        result:=result-'mixed_bulk'||jsonb_build_object('mixedBulk',requested_mode);
    ELSIF action='draft' THEN
        result:=grainledger.ledger_command_pre_mixed(a,action,p);
        SELECT * INTO STRICT saved FROM grainledger.ledger_operations WHERE id=command_id FOR UPDATE;
        FOR item IN SELECT value FROM jsonb_array_elements(saved.content->'inputs') LOOP
            SELECT * INTO STRICT location FROM grainledger.ledger_locations WHERE id=(item->>'locationId')::uuid FOR UPDATE;
            IF location.mixed_bulk THEN
                IF jsonb_typeof(item->'poolRevision') IS DISTINCT FROM 'number' OR item->>'poolRevision' !~ '^[0-9]+$' THEN
                    RAISE EXCEPTION 'Current pool revision required for mixed-bulk input' USING ERRCODE='23514';
                END IF;
                SELECT * INTO STRICT selected_pool FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.location_id=location.id FOR UPDATE;
                IF selected_pool.lot_id IS DISTINCT FROM (item->>'lotId')::uuid OR selected_pool.revision<>(item->>'poolRevision')::bigint THEN
                    RAISE EXCEPTION 'Mixed-bulk input generation changed; reload and resave draft' USING ERRCODE='40001';
                END IF;
                SELECT * INTO STRICT source FROM grainledger.ledger_lots WHERE id=selected_pool.lot_id;
                IF grainledger.ledger_mixed_bulk_eligible(location.id,saved.owner_id,source.commodity_id,source.stage,
                    source.product_identity,clock_timestamp()) IS NULL THEN
                    RAISE EXCEPTION 'No active evidence-backed mixed-bulk identity policy' USING ERRCODE='23514';
                END IF;
            END IF;
        END LOOP;
    ELSIF action='transfer' THEN
        SELECT * INTO STRICT source FROM grainledger.ledger_lots WHERE id=(p->>'lotId')::uuid FOR UPDATE;
        source_location:=(p->>'fromLocationId')::uuid; destination:=(p->>'toLocationId')::uuid;
        PERFORM grainledger.ledger_require_active_location(source_location);
        PERFORM grainledger.ledger_require_active_location(destination);
        SELECT * INTO STRICT location FROM grainledger.ledger_locations WHERE id=source_location FOR UPDATE;
        SELECT loc.facility_id,loc.mixed_bulk INTO STRICT facility,destination_mixed FROM grainledger.ledger_locations loc WHERE loc.id=destination FOR UPDATE;
        source_mixed:=location.mixed_bulk;
        PERFORM grainledger.ledger_require_staff(a,location.facility_id,CASE WHEN p->>'holdId' IS NULL THEN ARRAY['MillOperator'] ELSE ARRAY['MillManager'] END);
        PERFORM grainledger.ledger_require_staff(a,facility,CASE WHEN p->>'holdId' IS NULL THEN ARRAY['MillOperator'] ELSE ARRAY['MillManager'] END);
        SELECT * INTO prior FROM grainledger.ledger_events WHERE id=command_id;
        IF FOUND THEN
            IF prior.kind<>'TRANSFER' OR prior.actor_id IS DISTINCT FROM a OR prior.details IS DISTINCT FROM p THEN
                RAISE EXCEPTION 'Transfer retry payload conflicts' USING ERRCODE='23514';
            END IF;
            RETURN jsonb_build_object('id',command_id);
        END IF;
        quantity:=grainledger.ledger_kg(p->>'quantityKg');
        IF quantity<=0 OR source_location=destination THEN
            RAISE EXCEPTION 'Positive transfer between different locations required' USING ERRCODE='23514';
        END IF;
        expected_revision:=NULL;
        IF source_mixed THEN
            IF jsonb_typeof(p->'poolRevision') IS DISTINCT FROM 'number' OR p->>'poolRevision' !~ '^[0-9]+$' THEN
                RAISE EXCEPTION 'Current pool revision required for mixed-bulk input' USING ERRCODE='23514';
            END IF;
            expected_revision:=(p->>'poolRevision')::bigint;
            SELECT * INTO STRICT selected_pool FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.location_id=source_location FOR UPDATE;
            IF selected_pool.lot_id IS DISTINCT FROM source.id OR selected_pool.revision<>expected_revision THEN
                RAISE EXCEPTION 'Mixed-bulk input generation changed; reload current stock' USING ERRCODE='40001';
            END IF;
        END IF;
        IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE lot_id=source.id AND location_id=source_location AND quantity_kg>=quantity) THEN
            RAISE EXCEPTION 'Insufficient stock' USING ERRCODE='23514';
        END IF;
        IF p->>'holdId' IS NULL THEN
            IF grainledger.ledger_blocked(source.id) THEN RAISE EXCEPTION 'Lot restricted from transfer' USING ERRCODE='23514'; END IF;
        ELSE
            IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_effective_hold_ids(source.id) h WHERE h=(p->>'holdId')::uuid)
               OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_locations WHERE id=destination AND kind='ISOLATION') THEN
                RAISE EXCEPTION 'HOLD transfer requires active effective HOLD and isolation location' USING ERRCODE='23514';
            END IF;
            IF grainledger.ledger_mixed_bulk_independent_blocked(source.id) THEN
                RAISE EXCEPTION 'Independent correction/receiving block prevents isolation movement' USING ERRCODE='23514';
            END IF;
        END IF;
        source_actual:=grainledger.ledger_pool_withdraw(a,command_id,CASE WHEN destination_mixed THEN 'TRANSFER_TO_MIXED' ELSE 'TRANSFER' END,
            source.id,source_location,quantity,expected_revision);
        PERFORM grainledger.ledger_credit_location(destination,source.owner_id);
        -- Keep ordinary TRANSFER details exactly equal to authenticated logical request.
        INSERT INTO grainledger.ledger_events(id,kind,actor_id,lot_id,details,occurred_at)
        VALUES(command_id,'TRANSFER',a,source_actual,p,(p->>'occurredAt')::timestamptz);
        PERFORM grainledger.ledger_post(command_id,source_actual,source_location,-quantity);
        PERFORM grainledger.ledger_post(command_id,source_actual,destination,quantity);
        current_pool:=grainledger.ledger_pool_ingress(a,command_id,'TRANSFER',destination,source_actual,quantity);
        result:=jsonb_build_object('id',command_id);
        IF source_actual<>source.id OR source_mixed OR destination_mixed THEN
            result:=result||jsonb_build_object('actualLotId',source_actual);
            IF destination_mixed THEN result:=result||jsonb_build_object('currentPoolLotId',current_pool); END IF;
            IF source_mixed THEN result:=result||jsonb_build_object('sourceCurrentPoolLotId',
                (SELECT pool.lot_id FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.location_id=source_location)); END IF;
        END IF;
    ELSIF action='hold' THEN
        result:=grainledger.ledger_command_pre_mixed(a,action,p);
        -- Notify every affected facility, including stock derived from intermediate pool lots.
        INSERT INTO grainledger.ledger_notifications(user_id,hold_id,lot_id,message)
        SELECT DISTINCT u."Id",command_id,pos.lot_id,p->>'reason'
        FROM grainledger.ledger_positions pos JOIN grainledger.ledger_locations loc ON loc.id=pos.location_id
        JOIN grainledger.identity_users u ON u."IsActive" AND u."Role" IN ('MillManager','MillOperator')
        WHERE pos.quantity_kg>0 AND grainledger.ledger_assigned(u."Id",loc.facility_id)
          AND EXISTS(SELECT 1 FROM grainledger.ledger_effective_hold_ids(pos.lot_id) h WHERE h=command_id)
          AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_notifications n WHERE n.user_id=u."Id" AND n.hold_id=command_id AND n.lot_id=pos.lot_id);
    ELSIF action='hold_response' THEN
        SELECT * INTO STRICT hold FROM grainledger.ledger_holds WHERE id=command_id FOR UPDATE;
        SELECT * INTO STRICT source FROM grainledger.ledger_lots WHERE id=hold.lot_id;
        SELECT ev.facility_id INTO facility FROM grainledger.ledger_evidence ev
        WHERE ev.id=(p->>'evidenceId')::uuid AND ev.owner_id=source.owner_id AND EXISTS(
            SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_locations loc ON loc.id=pos.location_id
            WHERE loc.facility_id=ev.facility_id AND (pos.lot_id=source.id OR EXISTS(
                SELECT 1 FROM grainledger.ledger_effective_hold_ids(pos.lot_id) h WHERE h=command_id))
              AND (pos.quantity_kg>0 OR NOT EXISTS(
                  SELECT 1 FROM grainledger.ledger_positions active WHERE active.quantity_kg>0 AND EXISTS(
                      SELECT 1 FROM grainledger.ledger_effective_hold_ids(active.lot_id) h WHERE h=command_id))));
        IF facility IS NULL THEN RAISE EXCEPTION 'HOLD response requires evidence scoped to affected current or historical facility' USING ERRCODE='42501'; END IF;
        PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillManager']);
        PERFORM grainledger.ledger_require_evidence(a,(p->>'evidenceId')::uuid,source.owner_id,facility);
        FOR affected_facility IN SELECT DISTINCT loc.facility_id FROM grainledger.ledger_positions pos
            JOIN grainledger.ledger_locations loc ON loc.id=pos.location_id WHERE pos.quantity_kg>0
              AND EXISTS(SELECT 1 FROM grainledger.ledger_effective_hold_ids(pos.lot_id) h WHERE h=command_id)
        LOOP PERFORM grainledger.ledger_require_staff(a,affected_facility,ARRAY['MillManager']); END LOOP;
        SELECT * INTO prior FROM grainledger.ledger_events ev WHERE ev.kind='HOLD_RESPONSE'
            AND ev.lot_id=source.id AND ev.details=p ORDER BY ev.occurred_at,ev.id LIMIT 1;
        IF FOUND THEN
            IF prior.actor_id IS DISTINCT FROM a THEN
                RAISE EXCEPTION 'HOLD response retry conflicts with original actor' USING ERRCODE='23514';
            END IF;
            RETURN jsonb_build_object('id',command_id);
        END IF;
        IF p->>'status' IS NULL OR p->>'status' NOT IN ('ACKNOWLEDGED','ISOLATED','RESPONDED') THEN
            RAISE EXCEPTION 'HOLD release unsupported' USING ERRCODE='23514';
        END IF;
        IF p->>'status'='ISOLATED' AND EXISTS(SELECT 1 FROM grainledger.ledger_positions pos
            JOIN grainledger.ledger_locations loc ON loc.id=pos.location_id WHERE pos.quantity_kg>0 AND loc.kind<>'ISOLATION'
              AND EXISTS(SELECT 1 FROM grainledger.ledger_effective_hold_ids(pos.lot_id) h WHERE h=command_id)) THEN
            RAISE EXCEPTION 'Affected HOLD stock remains outside isolation' USING ERRCODE='23514';
        END IF;
        UPDATE grainledger.ledger_holds SET status=p->>'status',response=p->>'response',evidence_id=(p->>'evidenceId')::uuid WHERE id=command_id;
        INSERT INTO grainledger.ledger_events(kind,actor_id,lot_id,details) VALUES('HOLD_RESPONSE',a,source.id,p);
        RETURN jsonb_build_object('id',command_id);
    ELSE
        IF action IN ('correction_request','correction_reconcile','correction_cancel','correction_commit') THEN
            PERFORM grainledger.ledger_require_mixed_correction_safe(action,p||jsonb_build_object('_correctionId',command_id));
        END IF;
        result:=grainledger.ledger_command_pre_mixed(a,action,p);
    END IF;
    IF cache_action IS NOT NULL THEN
        INSERT INTO grainledger.ledger_mixed_bulk_command_results(action,command_id,actor_id,request,result)
        VALUES(cache_action,command_id,a,p,result);
    END IF;
    RETURN result;
END $_$;

CREATE FUNCTION grainledger.ledger_command_core(a uuid, action text, p jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $_$
DECLARE
    command_id uuid;
    facility uuid;
    owner uuid;
    location grainledger.ledger_locations;
    lot grainledger.ledger_lots;
    operation grainledger.ledger_operations;
    correction grainledger.ledger_corrections;
    item jsonb;
    quantity numeric;
    event_id uuid:=gen_random_uuid();
    hold grainledger.ledger_holds;
    original_posting record;
    physical_position record;
    offset_quantity numeric;
    evidence_bytes bytea;
    evidence_hash text;
    setup_record record;
    requested_version uuid;
    new_user uuid;
    new_facility uuid;
    resource text;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    IF grainledger.ledger_role(a) IS NULL THEN
        RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501';
    END IF;
    command_id:=NULLIF(p->>'id','')::uuid;
    IF p ?| ARRAY['createdBy','created_by'] THEN
        RAISE EXCEPTION 'Creator derives from authenticated actor' USING ERRCODE='22023';
    END IF;

    IF action IN ('facilities_update','facilities_delete','locations_update','locations_delete',
                  'equipment_update','equipment_delete','assignments_update','assignments_delete') THEN
        IF grainledger.ledger_role(a)<>'Administrator' THEN
            RAISE EXCEPTION 'Setup authority denied' USING ERRCODE='42501';
        END IF;
        resource:=split_part(action,'_',1);
        CASE resource
            WHEN 'facilities' THEN SELECT * INTO STRICT setup_record FROM grainledger.ledger_facilities WHERE id=command_id FOR UPDATE;
            WHEN 'locations' THEN SELECT * INTO STRICT setup_record FROM grainledger.ledger_locations WHERE id=command_id FOR UPDATE;
            WHEN 'equipment' THEN SELECT * INTO STRICT setup_record FROM grainledger.ledger_equipment WHERE id=command_id FOR UPDATE;
            WHEN 'assignments' THEN SELECT * INTO STRICT setup_record FROM grainledger.ledger_assignments
                WHERE user_id=(p->>'userId')::uuid AND facility_id=(p->>'facilityId')::uuid FOR UPDATE;
        END CASE;
        IF setup_record.created_by IS DISTINCT FROM a THEN
            RAISE EXCEPTION 'Only creator may manage setup record' USING ERRCODE='42501';
        END IF;
        IF setup_record.is_deleted THEN
            RAISE EXCEPTION 'Removed setup record cannot be changed or restored' USING ERRCODE='23514';
        END IF;
        IF jsonb_typeof(p->'version') IS DISTINCT FROM 'string' OR NULLIF(p->>'version','') IS NULL THEN
            RAISE EXCEPTION 'Row version string required' USING ERRCODE='22023';
        END IF;
        requested_version:=(p->>'version')::uuid;
        IF setup_record.version IS DISTINCT FROM requested_version THEN
            RAISE EXCEPTION 'Setup record version stale' USING ERRCODE='23514';
        END IF;
        IF right(action,7)='_delete' THEN
            IF resource<>'assignments' AND grainledger.ledger_record_in_use(resource,command_id) THEN
                RAISE EXCEPTION 'Setup record referenced; deletion denied' USING ERRCODE='23514';
            END IF;
            CASE resource
                WHEN 'facilities' THEN UPDATE grainledger.ledger_facilities SET is_deleted=true,version=gen_random_uuid() WHERE id=command_id;
                WHEN 'locations' THEN UPDATE grainledger.ledger_locations SET is_deleted=true,version=gen_random_uuid() WHERE id=command_id;
                WHEN 'equipment' THEN UPDATE grainledger.ledger_equipment SET is_deleted=true,version=gen_random_uuid() WHERE id=command_id;
                WHEN 'assignments' THEN UPDATE grainledger.ledger_assignments SET is_deleted=true,version=gen_random_uuid()
                    WHERE user_id=setup_record.user_id AND facility_id=setup_record.facility_id;
            END CASE;
            IF resource='assignments' THEN
                RETURN jsonb_build_object('userId',setup_record.user_id,'facilityId',setup_record.facility_id);
            END IF;
            RETURN jsonb_build_object('id',command_id);
        END IF;
        IF resource='assignments' THEN
            new_user:=CASE WHEN p ? 'newUserId' THEN (p->>'newUserId')::uuid ELSE setup_record.user_id END;
            new_facility:=CASE WHEN p ? 'newFacilityId' THEN (p->>'newFacilityId')::uuid ELSE setup_record.facility_id END;
            IF new_facility IS NULL OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_facilities WHERE id=new_facility AND NOT is_deleted)
               OR NOT EXISTS(SELECT 1 FROM grainledger.identity_users WHERE "Id"=new_user AND "IsActive" AND "Role" IN ('MillOperator','MillManager')) THEN
                RAISE EXCEPTION 'Active operational staff and existing facility required' USING ERRCODE='23514';
            END IF;
            UPDATE grainledger.ledger_assignments SET user_id=new_user,facility_id=new_facility,version=gen_random_uuid()
            WHERE user_id=setup_record.user_id AND facility_id=setup_record.facility_id
            RETURNING * INTO setup_record;
            RETURN jsonb_build_object('userId',setup_record.user_id,'facilityId',setup_record.facility_id,
                'version',setup_record.version,'createdBy',setup_record.created_by,'canEdit',true,'canDelete',true);
        END IF;
        IF jsonb_typeof(p->'name') IS DISTINCT FROM 'string' OR length(trim(p->>'name')) NOT BETWEEN 1 AND 160 THEN
            RAISE EXCEPTION 'Name of 1–160 characters required' USING ERRCODE='22023';
        END IF;
        CASE resource
            WHEN 'facilities' THEN
                UPDATE grainledger.ledger_facilities SET name=p->>'name',version=gen_random_uuid() WHERE id=command_id RETURNING * INTO setup_record;
            WHEN 'locations' THEN
                new_facility:=CASE WHEN p ? 'facilityId' THEN (p->>'facilityId')::uuid ELSE setup_record.facility_id END;
                IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_facilities WHERE id=new_facility AND NOT is_deleted) THEN
                    RAISE EXCEPTION 'Active facility required' USING ERRCODE='23514';
                END IF;
                IF (new_facility IS DISTINCT FROM setup_record.facility_id
                    OR CASE WHEN p ? 'kind' THEN p->>'kind' ELSE setup_record.kind END IS DISTINCT FROM setup_record.kind)
                    AND grainledger.ledger_record_in_use(resource,command_id) THEN
                    RAISE EXCEPTION 'Referenced location facility/kind immutable' USING ERRCODE='23514';
                END IF;
                UPDATE grainledger.ledger_locations SET name=p->>'name',facility_id=new_facility,
                    kind=CASE WHEN p ? 'kind' THEN p->>'kind' ELSE kind END,version=gen_random_uuid()
                WHERE id=command_id RETURNING * INTO setup_record;
            WHEN 'equipment' THEN
                new_facility:=CASE WHEN p ? 'facilityId' THEN (p->>'facilityId')::uuid ELSE setup_record.facility_id END;
                IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_facilities WHERE id=new_facility AND NOT is_deleted) THEN
                    RAISE EXCEPTION 'Active facility required' USING ERRCODE='23514';
                END IF;
                IF new_facility IS DISTINCT FROM setup_record.facility_id AND grainledger.ledger_record_in_use(resource,command_id) THEN
                    RAISE EXCEPTION 'Referenced equipment facility immutable' USING ERRCODE='23514';
                END IF;
                UPDATE grainledger.ledger_equipment SET name=p->>'name',facility_id=new_facility,version=gen_random_uuid()
                WHERE id=command_id RETURNING * INTO setup_record;
        END CASE;
        RETURN to_jsonb(setup_record)-'created_by'-'facility_id'-'owner_id' || jsonb_build_object(
            'createdBy',setup_record.created_by,'canEdit',true,'canDelete',NOT grainledger.ledger_record_in_use(resource,command_id))
            || CASE WHEN resource='facilities' THEN '{}'::jsonb ELSE jsonb_build_object('facilityId',to_jsonb(setup_record)->'facility_id') END;
    END IF;

    IF action='draft_delete' THEN
        SELECT * INTO STRICT operation FROM grainledger.ledger_operations WHERE id=command_id FOR UPDATE;
        PERFORM grainledger.ledger_require_staff(a,operation.facility_id,ARRAY['MillOperator']);
        IF operation.created_by IS DISTINCT FROM a THEN
            RAISE EXCEPTION 'Only creator may manage operation draft' USING ERRCODE='42501';
        END IF;
        IF jsonb_typeof(p->'version') IS DISTINCT FROM 'string' OR NULLIF(p->>'version','') IS NULL THEN
            RAISE EXCEPTION 'Row version string required' USING ERRCODE='22023';
        END IF;
        IF operation.is_deleted OR operation.status<>'DRAFT' OR operation.version IS DISTINCT FROM (p->>'version')::uuid THEN
            RAISE EXCEPTION 'Only current draft version may be deleted' USING ERRCODE='23514';
        END IF;
        IF grainledger.ledger_record_in_use('operations',command_id) THEN
            RAISE EXCEPTION 'Draft referenced by correction or history; deletion denied' USING ERRCODE='23514';
        END IF;
        UPDATE grainledger.ledger_operations SET is_deleted=true,version=gen_random_uuid() WHERE id=command_id;
        RETURN jsonb_build_object('id',command_id);
    END IF;

    -- Setup does not confer operational, reconciliation or HOLD authority.
    IF action IN ('facilities','locations','equipment','assignments','policies','compatibilities','clearance') THEN
        IF grainledger.ledger_role(a)<>'Administrator' THEN
            RAISE EXCEPTION 'Setup authority denied' USING ERRCODE='42501';
        END IF;
        IF action IN ('locations','equipment','assignments','policies','compatibilities') AND NOT EXISTS(
            SELECT 1 FROM grainledger.ledger_facilities WHERE id=(p->>'facilityId')::uuid AND NOT is_deleted
        ) THEN
            RAISE EXCEPTION 'Active facility required' USING ERRCODE='23514';
        END IF;
        CASE action
            WHEN 'facilities' THEN
                INSERT INTO grainledger.ledger_facilities(id,name,created_by) VALUES(command_id,p->>'name',a);
            WHEN 'locations' THEN
                INSERT INTO grainledger.ledger_locations(id,facility_id,name,kind,created_by)
                VALUES(command_id,(p->>'facilityId')::uuid,p->>'name',p->>'kind',a);
            WHEN 'equipment' THEN
                INSERT INTO grainledger.ledger_equipment(id,facility_id,name,created_by) VALUES(command_id,(p->>'facilityId')::uuid,p->>'name',a);
            WHEN 'assignments' THEN
                IF EXISTS(SELECT 1 FROM grainledger.ledger_assignments WHERE user_id=(p->>'userId')::uuid
                    AND facility_id=(p->>'facilityId')::uuid AND is_deleted) THEN
                    RAISE EXCEPTION 'Removed assignment cannot be recreated or restored' USING ERRCODE='23514';
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM grainledger.identity_users WHERE "Id"=(p->>'userId')::uuid
                      AND "IsActive" AND "Role" IN ('MillOperator','MillManager')
                ) THEN
                    RAISE EXCEPTION 'Only active operational mill staff may be assigned' USING ERRCODE='23514';
                END IF;
                INSERT INTO grainledger.ledger_assignments(user_id,facility_id,created_by) VALUES((p->>'userId')::uuid,(p->>'facilityId')::uuid,a)
                ON CONFLICT DO NOTHING;
            WHEN 'policies' THEN
                facility:=(p->>'facilityId')::uuid;
                SELECT owner_id INTO STRICT owner FROM grainledger.ledger_evidence
                WHERE id=(p->>'evidenceId')::uuid AND facility_id=facility;
                PERFORM grainledger.ledger_require_evidence(a,(p->>'evidenceId')::uuid,owner,facility);
                INSERT INTO grainledger.ledger_policies VALUES(command_id,facility,p->>'kind',
                    grainledger.ledger_kg(p->>'tolerancePercent'),(p->>'effectiveFrom')::timestamptz,(p->>'evidenceId')::uuid);
            WHEN 'compatibilities' THEN
                facility:=(p->>'facilityId')::uuid;
                SELECT owner_id INTO STRICT owner FROM grainledger.ledger_evidence
                WHERE id=(p->>'evidenceId')::uuid AND facility_id=facility;
                PERFORM grainledger.ledger_require_evidence(a,(p->>'evidenceId')::uuid,owner,facility);
                IF jsonb_typeof(p->'inputProducts') IS DISTINCT FROM 'array' THEN
                    RAISE EXCEPTION 'Explicit blend compatibility products required' USING ERRCODE='23514';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM jsonb_array_elements_text(p->'inputProducts') product
                    WHERE product !~ '^[0-9a-fA-F-]{36}:(HEAD_RICE|BROKEN_RICE|BLENDED_RICE)$'
                       OR NOT EXISTS (
                           SELECT 1 FROM grainledger.ledger_commodities WHERE id=split_part(product,':',1)::uuid
                       )
                       OR EXISTS (
                           SELECT 1 FROM grainledger.ledger_catalog_commodities map JOIN grainledger.catalog_commodities c ON c.id=map.catalog_commodity_id
                           WHERE map.ledger_commodity_id=split_part(product,':',1)::uuid AND c.is_archived
                       )
                ) THEN
                    RAISE EXCEPTION 'Blend compatibility requires explicit registered commodity/product identities'
                        USING ERRCODE='23514';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM grainledger.ledger_catalog_commodities map JOIN grainledger.catalog_commodities c ON c.id=map.catalog_commodity_id
                    WHERE map.ledger_commodity_id=(p->>'outputCommodityId')::uuid AND c.is_archived
                ) THEN
                    RAISE EXCEPTION 'Archived Catalog commodity unavailable for compatibility' USING ERRCODE='23514';
                END IF;
                INSERT INTO grainledger.ledger_compatibilities VALUES(command_id,facility,
                    ARRAY(SELECT jsonb_array_elements_text(p->'inputProducts')),
                    (p->>'outputCommodityId')::uuid,(p->>'effectiveFrom')::timestamptz,(p->>'evidenceId')::uuid);
            WHEN 'clearance' THEN
                SELECT * INTO STRICT location FROM grainledger.ledger_locations WHERE id=command_id FOR UPDATE;
                IF location.is_deleted OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_facilities WHERE id=location.facility_id AND NOT is_deleted) THEN
                    RAISE EXCEPTION 'Active location and facility required' USING ERRCODE='23514';
                END IF;
                IF EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE location_id=location.id AND quantity_kg>0) THEN
                    RAISE EXCEPTION 'Clearance requires empty stock' USING ERRCODE='23514';
                END IF;
                SELECT owner_id INTO STRICT owner FROM grainledger.ledger_evidence
                WHERE id=(p->>'evidenceId')::uuid AND facility_id=location.facility_id;
                PERFORM grainledger.ledger_require_evidence(a,(p->>'evidenceId')::uuid,owner,location.facility_id);
                IF p->>'reason' IS NULL OR length(trim(p->>'reason'))=0 THEN
                    RAISE EXCEPTION 'Clearance confirmation reason required' USING ERRCODE='23514';
                END IF;
                UPDATE grainledger.ledger_locations SET cleared=true,owner_id=NULL,version=gen_random_uuid() WHERE id=location.id;
                INSERT INTO grainledger.ledger_events(kind,actor_id,details) VALUES('CLEARANCE',a,p);
        END CASE;
        IF action IN ('facilities','locations','equipment','assignments') THEN
            CASE action
                WHEN 'facilities' THEN SELECT * INTO STRICT setup_record FROM grainledger.ledger_facilities WHERE id=command_id;
                WHEN 'locations' THEN SELECT * INTO STRICT setup_record FROM grainledger.ledger_locations WHERE id=command_id;
                WHEN 'equipment' THEN SELECT * INTO STRICT setup_record FROM grainledger.ledger_equipment WHERE id=command_id;
                WHEN 'assignments' THEN SELECT * INTO STRICT setup_record FROM grainledger.ledger_assignments
                    WHERE user_id=(p->>'userId')::uuid AND facility_id=(p->>'facilityId')::uuid;
            END CASE;
            IF action='assignments' THEN
                RETURN jsonb_build_object('userId',setup_record.user_id,'facilityId',setup_record.facility_id,
                    'version',setup_record.version,'createdBy',setup_record.created_by,
                    'canEdit',setup_record.created_by IS NOT DISTINCT FROM a,'canDelete',setup_record.created_by IS NOT DISTINCT FROM a);
            END IF;
            RETURN to_jsonb(setup_record)-'created_by'-'facility_id'-'owner_id' || jsonb_build_object(
                'createdBy',setup_record.created_by,'canEdit',true,'canDelete',NOT grainledger.ledger_record_in_use(action,command_id))
                || CASE WHEN action='facilities' THEN '{}'::jsonb ELSE jsonb_build_object('facilityId',to_jsonb(setup_record)->'facility_id') END;
        END IF;
        RETURN jsonb_build_object('id',command_id);
    END IF;

    IF action='evidence' THEN
        facility:=(p->>'facilityId')::uuid;
        owner:=(p->>'ownerId')::uuid;
        IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_facilities WHERE id=facility AND NOT is_deleted) THEN
            RAISE EXCEPTION 'Active facility required' USING ERRCODE='23514';
        END IF;
        IF grainledger.ledger_role(a)<>'Administrator' AND NOT grainledger.ledger_own(a,owner) THEN
            PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillManager','MillOperator']);
        END IF;
        IF NOT EXISTS(SELECT 1 FROM grainledger.identity_organizations WHERE "Id"=owner) THEN
            RAISE EXCEPTION 'Unknown evidence owner' USING ERRCODE='23514';
        END IF;
        evidence_bytes:=decode(p->>'contentBase64','base64');
        evidence_hash:=encode(sha256(evidence_bytes),'hex');
        -- Same immutable upload can be retried after a timeout; content/scope cannot be overwritten.
        IF EXISTS(SELECT 1 FROM grainledger.ledger_evidence WHERE id=command_id) THEN
            IF NOT EXISTS (
                SELECT 1 FROM grainledger.ledger_evidence WHERE id=command_id AND actor_id=a
                  AND owner_id=owner AND facility_id=facility AND file_name=p->>'fileName'
                  AND content_type=p->>'contentType' AND content=evidence_bytes
            ) THEN
                RAISE EXCEPTION 'Evidence retry conflicts with immutable content or scope' USING ERRCODE='23514';
            END IF;
            RETURN jsonb_build_object('id',command_id,'sha256',evidence_hash);
        END IF;
        INSERT INTO grainledger.ledger_evidence VALUES(command_id,owner,facility,p->>'fileName',p->>'contentType',
                                           evidence_bytes,evidence_hash,a,now());
        RETURN jsonb_build_object('id',command_id,'sha256',evidence_hash);
    END IF;

    IF action='draft' THEN
        facility:=(p->>'facilityId')::uuid;
        owner:=(p->>'ownerId')::uuid;
        PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillOperator']);
        IF NOT EXISTS(SELECT 1 FROM grainledger.identity_organizations WHERE "Id"=owner AND "Kind"='Customer') THEN
            RAISE EXCEPTION 'Operation owner must be customer' USING ERRCODE='23514';
        END IF;
        SELECT * INTO operation FROM grainledger.ledger_operations WHERE id=command_id FOR UPDATE;
        IF FOUND THEN
            IF operation.is_deleted OR operation.status<>'DRAFT' OR operation.owner_id<>owner OR operation.facility_id<>facility THEN
                RAISE EXCEPTION 'Committed operation immutable or draft scope conflict' USING ERRCODE='23514';
            END IF;
            IF operation.created_by IS DISTINCT FROM a THEN
                RAISE EXCEPTION 'Only creator may manage operation draft' USING ERRCODE='42501';
            END IF;
            IF jsonb_typeof(p->'version') IS DISTINCT FROM 'string' OR NULLIF(p->>'version','') IS NULL THEN
                RAISE EXCEPTION 'Row version string required' USING ERRCODE='22023';
            END IF;
            IF operation.version IS DISTINCT FROM (p->>'version')::uuid THEN
                RAISE EXCEPTION 'Draft version stale' USING ERRCODE='23514';
            END IF;
        ELSIF p ? 'version' THEN
            RAISE EXCEPTION 'Operation draft no longer exists' USING ERRCODE='23514';
        END IF;
        IF jsonb_typeof(p->'inputs') IS DISTINCT FROM 'array'
           OR jsonb_typeof(p->'outputs') IS DISTINCT FROM 'array' THEN
            RAISE EXCEPTION 'Input/output arrays required' USING ERRCODE='23514';
        END IF;
        IF jsonb_array_length(p->'inputs')=0 OR jsonb_array_length(p->'outputs')=0 THEN
            RAISE EXCEPTION 'Nonempty input/output arrays required; loss-only operations unsupported'
                USING ERRCODE='23514';
        END IF;
        FOR item IN SELECT value FROM jsonb_array_elements(p->'inputs') LOOP
            IF NOT EXISTS (
                SELECT 1 FROM grainledger.ledger_lots source JOIN grainledger.ledger_positions pos ON pos.lot_id=source.id
                JOIN grainledger.ledger_locations loc ON loc.id=pos.location_id
                WHERE source.id=(item->>'lotId')::uuid AND source.owner_id=owner
                  AND loc.id=(item->>'locationId')::uuid AND loc.facility_id=facility AND NOT loc.is_deleted
            ) THEN
                RAISE EXCEPTION 'Draft input outside persisted owner/facility scope' USING ERRCODE='42501';
            END IF;
            PERFORM grainledger.ledger_kg(item->>'quantityKg');
        END LOOP;
        FOR item IN SELECT value FROM jsonb_array_elements(p->'outputs') LOOP
            PERFORM grainledger.ledger_kg(item->>'quantityKg');
            IF EXISTS (
                SELECT 1 FROM grainledger.ledger_catalog_commodities map JOIN grainledger.catalog_commodities c ON c.id=map.catalog_commodity_id
                WHERE map.ledger_commodity_id=(item->>'commodityId')::uuid AND c.is_archived
            ) THEN
                RAISE EXCEPTION 'Archived Catalog commodity unavailable for draft output' USING ERRCODE='23514';
            END IF;
            IF NOT EXISTS (
                SELECT 1 FROM grainledger.ledger_locations WHERE id=(item->>'locationId')::uuid
                  AND NOT is_deleted AND grainledger.ledger_assigned(a,facility_id)
            ) THEN
                RAISE EXCEPTION 'Draft output outside assigned facility' USING ERRCODE='42501';
            END IF;
        END LOOP;
        PERFORM grainledger.ledger_kg(p->>'declaredLossKg');
        IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_equipment WHERE id=(p->>'equipmentId')::uuid AND facility_id=facility AND NOT is_deleted) THEN
            RAISE EXCEPTION 'Draft equipment outside assigned facility' USING ERRCODE='42501';
        END IF;
        INSERT INTO grainledger.ledger_operations(id,owner_id,facility_id,kind,occurred_at,equipment_id,content,version,created_by)
        VALUES(command_id,owner,facility,p->>'kind',(p->>'occurredAt')::timestamptz,
               (p->>'equipmentId')::uuid,p-'version'-'canEdit'-'canDelete'-'isDeleted'-'is_deleted',gen_random_uuid(),a)
        ON CONFLICT ON CONSTRAINT "ledger_operations_pkey" DO UPDATE SET
            kind=excluded.kind,occurred_at=excluded.occurred_at,equipment_id=excluded.equipment_id,
            content=excluded.content,version=excluded.version,confirmed_version=NULL,
            confirmed_by=NULL,confirmation_evidence_id=NULL;
        WITH invalidated AS (
            UPDATE grainledger.ledger_corrections SET status='REQUESTED'
            WHERE replacement_operation_id=command_id AND status='RECONCILED'
            RETURNING operation_id,id
        ) INSERT INTO grainledger.ledger_events(kind,actor_id,operation_id,details)
        SELECT 'RECONCILIATION_INVALIDATED',a,operation_id,
               jsonb_build_object('correctionId',id,'replacementOperationId',command_id) FROM invalidated;
        RETURN grainledger.ledger_operation_json(command_id) || jsonb_build_object('createdBy',a,'canEdit',true,
            'canDelete',NOT grainledger.ledger_record_in_use('operations',command_id));
    END IF;

    IF action IN ('confirm','commit') THEN
        SELECT * INTO STRICT operation FROM grainledger.ledger_operations WHERE id=command_id FOR UPDATE;
        PERFORM grainledger.ledger_require_active_operation(command_id);
        IF action='commit' THEN RETURN grainledger.ledger_commit_operation(a,command_id); END IF;
        PERFORM grainledger.ledger_require_staff(a,operation.facility_id,ARRAY['MillManager']);
        PERFORM grainledger.ledger_require_evidence(a,(p->>'evidenceId')::uuid,operation.owner_id,operation.facility_id);
        IF operation.status<>'DRAFT' OR operation.version IS DISTINCT FROM (p->>'version')::uuid THEN
            RAISE EXCEPTION 'Confirmation review stale or version omitted' USING ERRCODE='23514';
        END IF;
        UPDATE grainledger.ledger_operations SET confirmed_version=version,confirmed_by=a,
            confirmation_evidence_id=(p->>'evidenceId')::uuid WHERE id=command_id;
        INSERT INTO grainledger.ledger_events(kind,actor_id,operation_id,details)
        VALUES('VARIANCE_CONFIRMATION',a,command_id,p||jsonb_build_object('reviewedContent',operation.content));
        RETURN grainledger.ledger_operation_json(command_id);
    END IF;

    IF action='transfer' THEN
        SELECT * INTO STRICT lot FROM grainledger.ledger_lots WHERE id=(p->>'lotId')::uuid FOR UPDATE;
        PERFORM grainledger.ledger_require_active_location((p->>'fromLocationId')::uuid);
        PERFORM grainledger.ledger_require_active_location((p->>'toLocationId')::uuid);
        SELECT * INTO STRICT location FROM grainledger.ledger_locations WHERE id=(p->>'fromLocationId')::uuid FOR UPDATE;
        SELECT facility_id INTO STRICT facility FROM grainledger.ledger_locations WHERE id=(p->>'toLocationId')::uuid;
        IF EXISTS(SELECT 1 FROM grainledger.ledger_events WHERE id=command_id) THEN
            PERFORM grainledger.ledger_require_staff(a,location.facility_id,
                CASE WHEN p->>'holdId' IS NULL THEN ARRAY['MillOperator'] ELSE ARRAY['MillManager'] END);
            PERFORM grainledger.ledger_require_staff(a,facility,
                CASE WHEN p->>'holdId' IS NULL THEN ARRAY['MillOperator'] ELSE ARRAY['MillManager'] END);
            IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_events WHERE id=command_id AND kind='TRANSFER' AND details=p) THEN
                RAISE EXCEPTION 'Transfer retry payload conflicts' USING ERRCODE='23514';
            END IF;
            RETURN jsonb_build_object('id',command_id);
        END IF;
        IF p->>'holdId' IS NULL THEN
            PERFORM grainledger.ledger_require_staff(a,location.facility_id,ARRAY['MillOperator']);
            PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillOperator']);
            IF grainledger.ledger_blocked(lot.id) THEN
                RAISE EXCEPTION 'Lot restricted from transfer' USING ERRCODE='23514';
            END IF;
        ELSE
            PERFORM grainledger.ledger_require_staff(a,location.facility_id,ARRAY['MillManager']);
            PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillManager']);
            IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_holds WHERE id=(p->>'holdId')::uuid AND lot_id=lot.id)
               OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_locations WHERE id=(p->>'toLocationId')::uuid AND kind='ISOLATION') THEN
                RAISE EXCEPTION 'HOLD transfer requires active HOLD and isolation location' USING ERRCODE='23514';
            END IF;
            IF grainledger.ledger_receiving_restricted(lot.id) OR EXISTS (
                SELECT 1 FROM grainledger.ledger_corrections WHERE operation_id=lot.operation_id
                  AND status IN ('REQUESTED','RECONCILED')
            ) OR EXISTS(SELECT 1 FROM grainledger.ledger_receipt_correction_blocks WHERE receipt_id=lot.receipt_id) THEN
                RAISE EXCEPTION 'Independent correction/receiving block prevents isolation movement'
                    USING ERRCODE='23514';
            END IF;
        END IF;
        quantity:=grainledger.ledger_kg(p->>'quantityKg');
        IF quantity<=0 OR location.id=(p->>'toLocationId')::uuid THEN
            RAISE EXCEPTION 'Positive transfer between different locations required' USING ERRCODE='23514';
        END IF;
        PERFORM grainledger.ledger_credit_location((p->>'toLocationId')::uuid,lot.owner_id);
        INSERT INTO grainledger.ledger_events(id,kind,actor_id,lot_id,details,occurred_at)
        VALUES(command_id,'TRANSFER',a,lot.id,p,(p->>'occurredAt')::timestamptz);
        PERFORM grainledger.ledger_post(command_id,lot.id,location.id,-quantity);
        PERFORM grainledger.ledger_post(command_id,lot.id,(p->>'toLocationId')::uuid,quantity);
        RETURN jsonb_build_object('id',command_id);
    END IF;

    IF action='hold' THEN
        SELECT * INTO STRICT lot FROM grainledger.ledger_lots WHERE id=(p->>'lotId')::uuid FOR UPDATE;
        IF grainledger.ledger_role(a)<>'CustomerManager' OR NOT grainledger.ledger_own(a,lot.owner_id) THEN
            RAISE EXCEPTION 'HOLD authority denied' USING ERRCODE='42501';
        END IF;
        INSERT INTO grainledger.ledger_holds(id,lot_id,reason) VALUES(command_id,lot.id,p->>'reason');
        INSERT INTO grainledger.ledger_events(kind,actor_id,lot_id,details) VALUES('HOLD',a,lot.id,p);
        INSERT INTO grainledger.ledger_notifications(user_id,hold_id,lot_id,message)
        SELECT u."Id",command_id,lot.id,p->>'reason' FROM grainledger.identity_users u
        WHERE u."IsActive" AND (
            u."Role"='CustomerManager' AND u."OrganizationId"=lot.owner_id
            OR u."Role" IN ('MillManager','MillOperator') AND EXISTS (
                SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_locations loc ON loc.id=pos.location_id
                WHERE pos.lot_id=lot.id AND pos.quantity_kg>0 AND grainledger.ledger_assigned(u."Id",loc.facility_id)
            )
        );
        RETURN jsonb_build_object('id',command_id);
    END IF;
    IF action='hold_response' THEN
        SELECT * INTO STRICT hold FROM grainledger.ledger_holds WHERE id=command_id;
        SELECT * INTO STRICT lot FROM grainledger.ledger_lots WHERE id=hold.lot_id;
        -- Authority and evidence remain mandatory even when stock is completely consumed.
        SELECT ev.facility_id INTO facility FROM grainledger.ledger_evidence ev
        WHERE ev.id=(p->>'evidenceId')::uuid AND ev.owner_id=lot.owner_id AND EXISTS (
            SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_locations loc ON loc.id=pos.location_id
            WHERE pos.lot_id=lot.id AND loc.facility_id=ev.facility_id AND (
                pos.quantity_kg>0 OR NOT EXISTS (
                    SELECT 1 FROM grainledger.ledger_positions active WHERE active.lot_id=lot.id AND active.quantity_kg>0
                )
            )
        );
        IF facility IS NULL THEN
            RAISE EXCEPTION 'HOLD response requires evidence scoped to current or historical lot facility'
                USING ERRCODE='42501';
        END IF;
        PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillManager']);
        PERFORM grainledger.ledger_require_evidence(a,(p->>'evidenceId')::uuid,lot.owner_id,facility);
        FOR facility IN
            SELECT DISTINCT loc.facility_id FROM grainledger.ledger_positions pos
            JOIN grainledger.ledger_locations loc ON loc.id=pos.location_id WHERE pos.lot_id=lot.id AND pos.quantity_kg>0
        LOOP
            PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillManager']);
        END LOOP;
        IF p->>'status' IS NULL OR p->>'status' NOT IN ('ACKNOWLEDGED','ISOLATED','RESPONDED') THEN
            RAISE EXCEPTION 'HOLD release unsupported' USING ERRCODE='23514';
        END IF;
        IF p->>'status'='ISOLATED' AND EXISTS (
            SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_locations loc ON loc.id=pos.location_id
            WHERE pos.lot_id=lot.id AND pos.quantity_kg>0 AND loc.kind<>'ISOLATION'
        ) THEN
            RAISE EXCEPTION 'Isolation requires physically transferred stock' USING ERRCODE='23514';
        END IF;
        UPDATE grainledger.ledger_holds SET status=p->>'status',response=p->>'response',
            evidence_id=(p->>'evidenceId')::uuid WHERE id=command_id;
        INSERT INTO grainledger.ledger_events(kind,actor_id,lot_id,details) VALUES('HOLD_RESPONSE',a,lot.id,p);
        RETURN jsonb_build_object('id',command_id);
    END IF;

    IF action='correction_request' THEN
        SELECT * INTO STRICT operation FROM grainledger.ledger_operations WHERE id=(p->>'operationId')::uuid FOR UPDATE;
        PERFORM grainledger.ledger_require_active_operation(operation.id);
        PERFORM grainledger.ledger_require_staff(a,operation.facility_id,ARRAY['MillOperator']);
        IF operation.status<>'COMMITTED' THEN
            RAISE EXCEPTION 'Only committed original operations can be corrected' USING ERRCODE='23514';
        END IF;
        INSERT INTO grainledger.ledger_corrections(id,operation_id,reason) VALUES(command_id,operation.id,p->>'reason');
        INSERT INTO grainledger.ledger_events(kind,actor_id,operation_id,details) VALUES('CORRECTION_REQUEST',a,operation.id,p);
        RETURN jsonb_build_object('id',command_id);
    END IF;
    IF action IN ('correction_reconcile','correction_cancel','correction_commit') THEN
        SELECT * INTO STRICT correction FROM grainledger.ledger_corrections WHERE id=command_id FOR UPDATE;
        SELECT * INTO STRICT operation FROM grainledger.ledger_operations WHERE id=correction.operation_id FOR UPDATE;
        PERFORM grainledger.ledger_require_active_operation(operation.id);
        IF action='correction_commit' THEN
            PERFORM grainledger.ledger_require_staff(a,operation.facility_id,ARRAY['MillOperator']);
        ELSE
            PERFORM grainledger.ledger_require_staff(a,operation.facility_id,ARRAY['MillManager']);
            PERFORM grainledger.ledger_require_evidence(a,(p->>'evidenceId')::uuid,operation.owner_id,operation.facility_id);
        END IF;
        IF action='correction_commit' AND correction.status='COMMITTED' THEN
            IF correction.replacement_operation_id IS DISTINCT FROM (p->>'replacementOperationId')::uuid THEN
                RAISE EXCEPTION 'Correction retry replacement conflicts' USING ERRCODE='23514';
            END IF;
            RETURN grainledger.ledger_operation_json(correction.replacement_operation_id);
        END IF;
        IF action='correction_cancel' THEN
            IF correction.status NOT IN ('REQUESTED','RECONCILED')
               OR p->>'reason' IS NULL OR length(trim(p->>'reason'))=0 THEN
                RAISE EXCEPTION 'Verified original-correct evidence and reason required' USING ERRCODE='23514';
            END IF;
            UPDATE grainledger.ledger_corrections SET status='CANCELLED',evidence_id=(p->>'evidenceId')::uuid
            WHERE id=command_id;
            INSERT INTO grainledger.ledger_events(kind,actor_id,operation_id,details)
            VALUES('CORRECTION_CANCEL_VERIFIED_ORIGINAL',a,operation.id,p);
            RETURN jsonb_build_object('id',command_id);
        END IF;
        IF correction.status NOT IN ('REQUESTED','RECONCILED') THEN
            RAISE EXCEPTION 'Correction already resolved' USING ERRCODE='23514';
        END IF;
        IF EXISTS (
            SELECT 1 FROM grainledger.ledger_lots output_lot JOIN grainledger.ledger_postings pp ON pp.lot_id=output_lot.id
            JOIN grainledger.ledger_events ee ON ee.id=pp.event_id WHERE output_lot.operation_id=operation.id
              AND ee.operation_id IS DISTINCT FROM operation.id AND ee.kind<>'TRANSFER'
        ) THEN
            RAISE EXCEPTION 'Downstream consumption prevents direct correction' USING ERRCODE='23514';
        END IF;
        IF action='correction_reconcile' THEN
            IF jsonb_typeof(p->'reconciliation') IS DISTINCT FROM 'array' THEN
                RAISE EXCEPTION 'Physical stock reconciliation array required' USING ERRCODE='23514';
            END IF;
            IF jsonb_array_length(p->'reconciliation')=0
               OR p->>'reason' IS NULL OR length(trim(p->>'reason'))=0 THEN
                RAISE EXCEPTION 'Physical stock reconciliation and reason required' USING ERRCODE='23514';
            END IF;
            IF EXISTS (
                SELECT 1 FROM jsonb_array_elements(p->'reconciliation') observed
                GROUP BY (observed->>'lotId')::uuid,(observed->>'locationId')::uuid HAVING count(*)>1
            ) THEN
                RAISE EXCEPTION 'Duplicate physical reconciliation position' USING ERRCODE='23514';
            END IF;
            -- Evidence must cover every actual position, and no invented/extra position.
            IF EXISTS (
                SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_lots output_lot ON output_lot.id=pos.lot_id
                WHERE output_lot.operation_id=operation.id AND pos.quantity_kg>0 AND NOT EXISTS (
                    SELECT 1 FROM jsonb_array_elements(p->'reconciliation') observed
                    WHERE (observed->>'lotId')::uuid=pos.lot_id
                      AND (observed->>'locationId')::uuid=pos.location_id
                      AND grainledger.ledger_kg(observed->>'quantityKg')=pos.quantity_kg
                )
            ) OR EXISTS (
                SELECT 1 FROM jsonb_array_elements(p->'reconciliation') observed WHERE NOT EXISTS (
                    SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_lots output_lot ON output_lot.id=pos.lot_id
                    WHERE output_lot.operation_id=operation.id AND pos.lot_id=(observed->>'lotId')::uuid
                      AND pos.location_id=(observed->>'locationId')::uuid
                      AND pos.quantity_kg=grainledger.ledger_kg(observed->>'quantityKg') AND pos.quantity_kg>0
                )
            ) THEN
                RAISE EXCEPTION 'Physical reconciliation must exactly cover actual current output positions'
                    USING ERRCODE='23514';
            END IF;
            FOR facility IN
                SELECT DISTINCT loc.facility_id FROM jsonb_array_elements(p->'reconciliation') observed
                JOIN grainledger.ledger_locations loc ON loc.id=(observed->>'locationId')::uuid
            LOOP
                PERFORM grainledger.ledger_require_staff(a,facility,ARRAY['MillManager']);
            END LOOP;
            PERFORM grainledger.ledger_review_replacement(a,command_id,(p->>'replacementOperationId')::uuid,
                                               (p->>'replacementVersion')::uuid,p->'reconciliation');
            UPDATE grainledger.ledger_corrections SET status='RECONCILED',reconciliation=p->'reconciliation',
                evidence_id=(p->>'evidenceId')::uuid WHERE id=command_id;
            INSERT INTO grainledger.ledger_events(kind,actor_id,operation_id,details)
            VALUES('PHYSICAL_RECONCILIATION',a,operation.id,p);
            RETURN jsonb_build_object('id',command_id);
        END IF;
        IF correction.status<>'RECONCILED' THEN
            RAISE EXCEPTION 'Correction requires physical reconciliation' USING ERRCODE='23514';
        END IF;
        IF EXISTS (
            SELECT 1 FROM grainledger.ledger_lots output_lot WHERE output_lot.operation_id=operation.id AND (
                EXISTS(SELECT 1 FROM grainledger.ledger_holds WHERE lot_id=output_lot.id)
                OR grainledger.ledger_receiving_restricted(output_lot.id)
            )
        ) THEN
            RAISE EXCEPTION 'Independent HOLD or receiving restriction prevents correction replacement'
                USING ERRCODE='23514';
        END IF;
        IF EXISTS (
            SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_lots output_lot ON output_lot.id=pos.lot_id
            WHERE output_lot.operation_id=operation.id AND pos.quantity_kg>0 AND NOT EXISTS (
                SELECT 1 FROM jsonb_array_elements(correction.reconciliation) observed
                WHERE (observed->>'lotId')::uuid=pos.lot_id
                  AND (observed->>'locationId')::uuid=pos.location_id
                  AND grainledger.ledger_kg(observed->>'quantityKg')=pos.quantity_kg
            )
        ) THEN
            RAISE EXCEPTION 'Physical reconciliation stale' USING ERRCODE='23514';
        END IF;
        SELECT * INTO STRICT operation FROM grainledger.ledger_operations WHERE id=(p->>'replacementOperationId')::uuid;
        PERFORM grainledger.ledger_require_active_operation(operation.id);
        IF operation.id IS DISTINCT FROM correction.replacement_operation_id
           OR operation.version IS DISTINCT FROM correction.replacement_version
           OR operation.content IS DISTINCT FROM correction.replacement_content THEN
            RAISE EXCEPTION 'Replacement changed after physical reconciliation' USING ERRCODE='23514';
        END IF;
        IF operation.owner_id<>(SELECT owner_id FROM grainledger.ledger_operations WHERE id=correction.operation_id)
           OR operation.facility_id<>(SELECT facility_id FROM grainledger.ledger_operations WHERE id=correction.operation_id) THEN
            RAISE EXCEPTION 'Replacement cannot change owner/facility' USING ERRCODE='23514';
        END IF;
        INSERT INTO grainledger.ledger_events(id,kind,actor_id,operation_id,details)
        VALUES(event_id,'PROCESSING_COMPENSATION',a,correction.operation_id,jsonb_build_object(
            'correctionId',command_id,'evidenceId',correction.evidence_id,'reconciliation',correction.reconciliation
        ));

        -- Reverse each original posting once. Transfers remain intact; approved location offsets
        -- drain outputs where physically reconciled, rather than fabricating reverse transfers.
        FOR original_posting IN
            SELECT pp.* FROM grainledger.ledger_postings pp JOIN grainledger.ledger_events ee ON ee.id=pp.event_id
            WHERE ee.operation_id=correction.operation_id
              AND ee.kind IN ('DRYING','MILLING','MERGE','BLENDING','BAGGING') ORDER BY pp.id
        LOOP
            IF original_posting.quantity_kg<0 THEN
                PERFORM grainledger.ledger_credit_location(original_posting.location_id,
                    (SELECT owner_id FROM grainledger.ledger_lots WHERE id=original_posting.lot_id));
                PERFORM grainledger.ledger_post(event_id,original_posting.lot_id,original_posting.location_id,
                                    -original_posting.quantity_kg,original_posting.id);
            ELSE
                INSERT INTO grainledger.ledger_postings(event_id,lot_id,location_id,quantity_kg,reverses_posting_id)
                VALUES(event_id,original_posting.lot_id,original_posting.location_id,
                       -original_posting.quantity_kg,original_posting.id);
                FOR physical_position IN
                    SELECT * FROM grainledger.ledger_positions WHERE lot_id=original_posting.lot_id ORDER BY location_id
                LOOP
                    offset_quantity:=-physical_position.quantity_kg+CASE
                        WHEN physical_position.location_id=original_posting.location_id
                        THEN original_posting.quantity_kg ELSE 0 END;
                    IF offset_quantity<>0 THEN
                        INSERT INTO grainledger.ledger_postings(event_id,lot_id,location_id,quantity_kg)
                        VALUES(event_id,original_posting.lot_id,physical_position.location_id,offset_quantity);
                    END IF;
                    UPDATE grainledger.ledger_positions SET quantity_kg=0
                    WHERE lot_id=original_posting.lot_id AND location_id=physical_position.location_id;
                END LOOP;
            END IF;
        END LOOP;
        PERFORM grainledger.ledger_commit_operation(a,operation.id,command_id);
        UPDATE grainledger.ledger_operations SET status='CORRECTED' WHERE id=correction.operation_id;
        UPDATE grainledger.ledger_corrections SET status='COMMITTED' WHERE id=command_id;
        RETURN grainledger.ledger_operation_json(operation.id);
    END IF;

    IF action='notification_read' THEN
        UPDATE grainledger.ledger_notifications SET read_at=coalesce(read_at,now()) WHERE id=command_id AND user_id=a;
        IF NOT FOUND THEN RAISE EXCEPTION 'Notification unavailable' USING ERRCODE='42501'; END IF;
        RETURN jsonb_build_object('id',command_id);
    END IF;
    RAISE EXCEPTION 'Unsupported ledger command' USING ERRCODE='23514';
END $_$;

CREATE FUNCTION grainledger.ledger_command_pre_mixed(a uuid, action text, p jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE prior grainledger.ledger_operations; saved grainledger.ledger_operations; result jsonb; item jsonb;
    outputs jsonb:='[]'::jsonb; assigned_code text; reservation grainledger.ledger_lot_code_reservations;
    prior_outputs jsonb; requested_outputs jsonb; original_lot grainledger.ledger_lots;
BEGIN
    IF action<>'draft' THEN RETURN grainledger.ledger_command_core(a,action,p); END IF;
    PERFORM grainledger.ledger_lock_writes();
    SELECT * INTO prior FROM grainledger.ledger_operations WHERE id=(p->>'id')::uuid;
    IF FOUND AND prior.lot_code_scheme='LEGACY' THEN
        RAISE EXCEPTION 'Legacy drafts cannot be edited; create a new draft with reserved lot codes' USING ERRCODE='23514';
    END IF;
    IF FOUND AND prior.lot_code_scheme='GENERATED' AND NOT p ? 'version' THEN
        SELECT jsonb_agg(value-'code' ORDER BY ordinal) INTO prior_outputs
        FROM jsonb_array_elements(prior.content->'outputs') WITH ORDINALITY AS x(value,ordinal);
        SELECT jsonb_agg(value-'code' ORDER BY ordinal) INTO requested_outputs
        FROM jsonb_array_elements(p->'outputs') WITH ORDINALITY AS x(value,ordinal);
        IF prior.status='DRAFT' AND prior.created_by=a
           AND jsonb_set(prior.content,'{outputs}',prior_outputs)
               =jsonb_set(p,'{outputs}',requested_outputs) THEN
            PERFORM grainledger.ledger_require_staff(a,prior.facility_id,ARRAY['MillOperator']);
            RETURN grainledger.ledger_operation_json(prior.id)||jsonb_build_object('createdBy',a,'canEdit',true,
                'canDelete',NOT grainledger.ledger_record_in_use('operations',prior.id));
        END IF;
    END IF;
    -- Core command performs persisted role, owner, input, facility and draft-version checks.
    result:=grainledger.ledger_command_core(a,action,p);
    SELECT * INTO STRICT saved FROM grainledger.ledger_operations WHERE id=(p->>'id')::uuid FOR UPDATE;
    IF EXISTS(SELECT 1 FROM jsonb_array_elements(saved.content->'outputs') x
        GROUP BY (x->>'id')::uuid HAVING count(*)>1) THEN
        RAISE EXCEPTION 'Duplicate output lot identifier' USING ERRCODE='23514';
    END IF;
    IF EXISTS(SELECT 1 FROM jsonb_array_elements(saved.content->'outputs') x
        WHERE x ? 'originalLotId' AND x->>'originalLotId' IS NOT NULL
        GROUP BY (x->>'originalLotId')::uuid HAVING count(*)>1) THEN
        RAISE EXCEPTION 'Original output cannot be replaced twice in one draft' USING ERRCODE='23514';
    END IF;
    FOR item IN SELECT value FROM jsonb_array_elements(saved.content->'outputs') LOOP
        IF nullif(item->>'originalLotId','') IS NOT NULL THEN
            SELECT * INTO STRICT original_lot FROM grainledger.ledger_lots WHERE id=(item->>'originalLotId')::uuid;
            IF original_lot.owner_id<>saved.owner_id OR original_lot.stage IS DISTINCT FROM item->>'stage'
               OR original_lot.product_identity IS DISTINCT FROM item->>'productIdentity'
               OR original_lot.operation_id IS NULL OR NOT EXISTS(
                   SELECT 1 FROM grainledger.ledger_operations old
                   JOIN grainledger.ledger_corrections c ON c.operation_id=old.id
                   WHERE old.id=original_lot.operation_id AND old.owner_id=saved.owner_id
                     AND old.facility_id=saved.facility_id AND old.kind=saved.kind
                     AND c.status IN ('REQUESTED','RECONCILED')
               ) THEN
                RAISE EXCEPTION 'Original output mapping is outside a matching correction' USING ERRCODE='23514';
            END IF;
            IF nullif(item->>'code','') IS NOT NULL AND item->>'code'<>original_lot.code THEN
                RAISE EXCEPTION 'Replacement code must match mapped original output' USING ERRCODE='23514';
            END IF;
            outputs:=outputs||jsonb_build_array(item||jsonb_build_object('code',original_lot.code));
            CONTINUE;
        END IF;
        SELECT * INTO reservation FROM grainledger.ledger_lot_code_reservations WHERE lot_id=(item->>'id')::uuid;
        IF FOUND AND item->>'code' IS NOT NULL AND item->>'code'<>reservation.code THEN
            RAISE EXCEPTION 'Output code does not match the reserved code' USING ERRCODE='23514';
        END IF;
        IF NOT FOUND AND nullif(item->>'code','') IS NOT NULL THEN
            RAISE EXCEPTION 'New output code is assigned by the server' USING ERRCODE='23514';
        END IF;
        assigned_code:=grainledger.ledger_reserve_lot_code_internal((item->>'id')::uuid,saved.owner_id,
            'OPERATION',saved.id,item->>'stage',item->>'productIdentity');
        outputs:=outputs||jsonb_build_array(item||jsonb_build_object('code',assigned_code));
    END LOOP;
    UPDATE grainledger.ledger_operations SET content=jsonb_set(content,'{outputs}',outputs),lot_code_scheme='GENERATED'
    WHERE id=saved.id;
    RETURN result||jsonb_build_object('outputs',outputs);
END $$;

CREATE FUNCTION grainledger.ledger_commit_operation(a uuid, op uuid, corr uuid DEFAULT NULL::uuid) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $_$
DECLARE
    operation grainledger.ledger_operations;
    allocation jsonb;
    output jsonb;
    input_lot grainledger.ledger_lots;
    quantity numeric;
    total_input numeric:=0;
    total_output numeric:=0;
    declared_loss numeric;
    variance numeric;
    allowed numeric;
    tolerance numeric;
    first_stage text;
    first_commodity uuid;
    first_product text;
    products text[];
    event_id uuid:=gen_random_uuid();
    policy_id uuid;
    output_facility uuid;
    original_operation uuid;
    resolved_inputs jsonb:='[]'::jsonb;
    actual_id uuid;
    expected_revision bigint;
    selected_pool grainledger.ledger_mixed_bulk_pools;
    actual_allocation jsonb;
    allocated_shares jsonb;
    pool_outputs jsonb:='[]'::jsonb;
    prior_result grainledger.ledger_mixed_bulk_command_results;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    PERFORM grainledger.ledger_require_active_operation(op);
    SELECT * INTO STRICT operation FROM grainledger.ledger_operations WHERE id=op FOR UPDATE;
    PERFORM grainledger.ledger_require_staff(a,operation.facility_id,ARRAY['MillOperator']);
    SELECT * INTO prior_result FROM grainledger.ledger_mixed_bulk_command_results cache
    WHERE cache.action='processing' AND cache.command_id=op;
    IF FOUND THEN
        IF prior_result.actor_id IS DISTINCT FROM a
           OR prior_result.request IS DISTINCT FROM jsonb_build_object('content',operation.content,'correctionId',corr) THEN
            RAISE EXCEPTION 'Processing retry conflicts with original actor or payload' USING ERRCODE='23514';
        END IF;
        RETURN prior_result.result;
    END IF;
    IF operation.status='COMMITTED' AND corr IS NULL THEN RETURN grainledger.ledger_operation_json(op); END IF;
    IF operation.status<>'DRAFT' THEN
        RAISE EXCEPTION 'Only draft operations can commit' USING ERRCODE='23514';
    END IF;
    IF operation.lot_code_scheme<>'GENERATED' THEN
        RAISE EXCEPTION 'Legacy drafts cannot be committed; create a new draft with reserved lot codes' USING ERRCODE='23514';
    END IF;
    IF corr IS NOT NULL THEN
        SELECT operation_id INTO original_operation FROM grainledger.ledger_corrections
        WHERE id=corr AND status='RECONCILED' AND replacement_operation_id=op;
        IF original_operation IS NULL THEN
            RAISE EXCEPTION 'Replacement lacks reconciled correction' USING ERRCODE='23514';
        END IF;
    END IF;
    IF operation.occurred_at>now() THEN
        RAISE EXCEPTION 'Occurrence time cannot be in future' USING ERRCODE='23514';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM grainledger.ledger_equipment
        WHERE id=operation.equipment_id AND facility_id=operation.facility_id
    ) THEN
        RAISE EXCEPTION 'Equipment outside facility' USING ERRCODE='23514';
    END IF;

    -- Missing, null and empty arrays never become a loss-only operation.
    IF jsonb_typeof(operation.content->'inputs') IS DISTINCT FROM 'array'
       OR jsonb_typeof(operation.content->'outputs') IS DISTINCT FROM 'array' THEN
        RAISE EXCEPTION 'Input/output arrays required' USING ERRCODE='23514';
    END IF;
    IF jsonb_array_length(operation.content->'inputs')=0
       OR jsonb_array_length(operation.content->'outputs')=0 THEN
        RAISE EXCEPTION 'Positive inputs and actual outputs required; loss-only operations unsupported'
            USING ERRCODE='23514';
    END IF;
    IF EXISTS (
        SELECT 1 FROM jsonb_array_elements(operation.content->'inputs') i
        GROUP BY (i->>'lotId')::uuid,(i->>'locationId')::uuid HAVING count(*)>1
    ) THEN
        RAISE EXCEPTION 'Duplicate input allocation' USING ERRCODE='23514';
    END IF;
    IF EXISTS (
        SELECT 1 FROM jsonb_array_elements(operation.content->'outputs') o
        GROUP BY o->>'code' HAVING count(*)>1
    ) THEN
        RAISE EXCEPTION 'Duplicate output lot code' USING ERRCODE='23514';
    END IF;

    -- One owner, authorized source facility and current eligibility under the posting lock.
    FOR allocation IN
        SELECT value FROM jsonb_array_elements(operation.content->'inputs')
        ORDER BY (value->>'lotId')::uuid,(value->>'locationId')::uuid
    LOOP
        SELECT * INTO STRICT input_lot FROM grainledger.ledger_lots
        WHERE id=(allocation->>'lotId')::uuid FOR UPDATE;
        quantity:=grainledger.ledger_kg(allocation->>'quantityKg');
        IF quantity<=0 THEN
            RAISE EXCEPTION 'Input must be positive' USING ERRCODE='23514';
        END IF;
        IF input_lot.owner_id<>operation.owner_id OR NOT EXISTS (
            SELECT 1 FROM grainledger.ledger_locations
            WHERE id=(allocation->>'locationId')::uuid AND facility_id=operation.facility_id
        ) THEN
            RAISE EXCEPTION 'Cross-owner or facility input denied' USING ERRCODE='42501';
        END IF;
        expected_revision:=NULL;
        IF (SELECT l.mixed_bulk FROM grainledger.ledger_locations l WHERE l.id=(allocation->>'locationId')::uuid) THEN
            IF jsonb_typeof(allocation->'poolRevision') IS DISTINCT FROM 'number'
               OR allocation->>'poolRevision' !~ '^[0-9]+$' THEN
                RAISE EXCEPTION 'Current pool revision required for mixed-bulk input' USING ERRCODE='23514';
            END IF;
            SELECT * INTO STRICT selected_pool FROM grainledger.ledger_mixed_bulk_pools pool
            WHERE pool.location_id=(allocation->>'locationId')::uuid FOR UPDATE;
            expected_revision:=(allocation->>'poolRevision')::bigint;
            IF selected_pool.lot_id IS DISTINCT FROM input_lot.id OR selected_pool.revision<>expected_revision THEN
                RAISE EXCEPTION 'Mixed-bulk input generation changed; reload and resave draft' USING ERRCODE='40001';
            END IF;
        END IF;
        IF grainledger.ledger_blocked(input_lot.id,corr) THEN
            RAISE EXCEPTION 'Input has HOLD, correction or receiving restriction' USING ERRCODE='23514';
        END IF;
        IF NOT EXISTS (
            SELECT 1 FROM grainledger.ledger_positions WHERE lot_id=input_lot.id
              AND location_id=(allocation->>'locationId')::uuid AND quantity_kg>=quantity
        ) THEN
            RAISE EXCEPTION 'Insufficient stock' USING ERRCODE='23514';
        END IF;
        IF operation.kind='DRYING' AND input_lot.stage<>'RAW_PADDY'
           OR operation.kind='MILLING' AND input_lot.stage<>'DRIED_PADDY'
           OR operation.kind IN ('BLENDING','BAGGING')
              AND input_lot.stage NOT IN ('HEAD_RICE','BROKEN_RICE','BLENDED_RICE') THEN
            RAISE EXCEPTION 'Undefined stage transition' USING ERRCODE='23514';
        END IF;
        IF first_stage IS NULL THEN
            first_stage:=input_lot.stage;
            first_commodity:=input_lot.commodity_id;
            first_product:=input_lot.product_identity;
        END IF;
        IF operation.kind='MERGE' AND (
            input_lot.stage<>first_stage OR input_lot.commodity_id<>first_commodity
            OR input_lot.product_identity<>first_product
        ) THEN
            RAISE EXCEPTION 'Merge requires same stage, commodity and underlying product identity'
                USING ERRCODE='23514';
        END IF;
        IF operation.kind IN ('DRYING','MILLING','BAGGING')
           AND input_lot.commodity_id<>first_commodity THEN
            RAISE EXCEPTION 'Commodity change requires approved blending' USING ERRCODE='23514';
        END IF;
        IF operation.kind='BAGGING' AND input_lot.product_identity<>first_product THEN
            RAISE EXCEPTION 'Bagging cannot mix product identities' USING ERRCODE='23514';
        END IF;
        products:=array_append(products,input_lot.commodity_id::text||':'||input_lot.product_identity);
        total_input:=total_input+quantity;
        actual_id:=grainledger.ledger_pool_withdraw(a,op,'PROCESSING',input_lot.id,
            (allocation->>'locationId')::uuid,quantity,expected_revision);
        resolved_inputs:=resolved_inputs||jsonb_build_array(allocation||jsonb_build_object(
            'logicalLotId',input_lot.id,'actualLotId',actual_id,'lotId',actual_id));
    END LOOP;

    FOR output IN SELECT value FROM jsonb_array_elements(operation.content->'outputs') LOOP
        IF nullif(output->>'originalLotId','') IS NULL THEN
            IF NOT EXISTS (
                SELECT 1 FROM grainledger.ledger_lot_code_reservations r
                WHERE r.lot_id=(output->>'id')::uuid AND r.code=output->>'code'
                  AND r.owner_id=operation.owner_id AND r.context_kind='OPERATION' AND r.context_id=op
                  AND r.stage=output->>'stage' AND r.product_identity=output->>'productIdentity'
            ) THEN
                RAISE EXCEPTION 'Output lot is missing its matching reserved code' USING ERRCODE='23514';
            END IF;
        ELSIF corr IS NULL OR NOT EXISTS (
            SELECT 1 FROM grainledger.ledger_lots l
            WHERE l.id=(output->>'originalLotId')::uuid AND l.operation_id=original_operation
              AND l.owner_id=operation.owner_id AND l.code=output->>'code'
              AND l.stage=output->>'stage' AND l.product_identity=output->>'productIdentity'
        ) THEN
            RAISE EXCEPTION 'Replacement output does not match original lot' USING ERRCODE='23514';
        END IF;
        quantity:=grainledger.ledger_kg(output->>'quantityKg');
        IF quantity<=0 THEN
            RAISE EXCEPTION 'Output must be positive' USING ERRCODE='23514';
        END IF;
        SELECT facility_id INTO STRICT output_facility FROM grainledger.ledger_locations
        WHERE id=(output->>'locationId')::uuid;
        IF output_facility<>operation.facility_id AND NOT (
            corr IS NOT NULL AND EXISTS (
                SELECT 1 FROM grainledger.ledger_corrections c,
                    jsonb_array_elements(c.reconciliation) physical_position
                WHERE c.id=corr AND (physical_position->>'locationId')::uuid=(output->>'locationId')::uuid
            )
        ) THEN
            RAISE EXCEPTION 'Output outside processing facility or reconciled physical locations'
                USING ERRCODE='42501';
        END IF;
        PERFORM grainledger.ledger_require_staff(a,output_facility,ARRAY['MillOperator']);
        IF output->>'stage' IS NULL
           OR operation.kind='DRYING' AND output->>'stage'<>'DRIED_PADDY'
           OR operation.kind='MILLING' AND output->>'stage' NOT IN ('HEAD_RICE','BROKEN_RICE','BRAN','HUSK')
           OR operation.kind='MERGE' AND output->>'stage'<>first_stage
           OR operation.kind='BLENDING' AND output->>'stage'<>'BLENDED_RICE'
           OR operation.kind='BAGGING' AND output->>'stage'<>'BAGGED_RICE' THEN
            RAISE EXCEPTION 'Invalid output stage' USING ERRCODE='23514';
        END IF;
        IF operation.kind<>'BLENDING' AND (output->>'commodityId')::uuid IS DISTINCT FROM first_commodity THEN
            RAISE EXCEPTION 'Output commodity mismatch' USING ERRCODE='23514';
        END IF;
        IF operation.kind IN ('MILLING','DRYING')
           AND output->>'productIdentity' IS DISTINCT FROM output->>'stage' THEN
            RAISE EXCEPTION 'Output product identity must match produced fraction' USING ERRCODE='23514';
        END IF;
        IF operation.kind IN ('BAGGING','MERGE')
           AND output->>'productIdentity' IS DISTINCT FROM first_product THEN
            RAISE EXCEPTION 'Underlying product identity must be retained' USING ERRCODE='23514';
        END IF;
        IF operation.kind='BLENDING' AND (
            output->>'productIdentity' IS DISTINCT FROM 'BLENDED_RICE' OR NOT EXISTS (
                SELECT 1 FROM grainledger.ledger_compatibilities c
                WHERE c.facility_id=operation.facility_id
                  AND c.output_commodity_id=(output->>'commodityId')::uuid
                  AND c.effective_from<=operation.occurred_at
                  AND c.input_products @> products AND c.input_products <@ products
            )
        ) THEN
            RAISE EXCEPTION 'No approved blend compatibility' USING ERRCODE='23514';
        END IF;
        total_output:=total_output+quantity;
    END LOOP;

    -- Threshold floors sub-micro-kg tails; six-decimal variance comparison remains exact.
    declared_loss:=grainledger.ledger_kg(operation.content->>'declaredLossKg');
    IF declared_loss<0 THEN
        RAISE EXCEPTION 'Declared loss cannot be negative' USING ERRCODE='23514';
    END IF;
    SELECT p.id,p.tolerance_percent INTO policy_id,tolerance FROM grainledger.ledger_policies p
    WHERE p.facility_id=operation.facility_id AND p.kind=operation.kind
      AND p.effective_from<=operation.occurred_at
    ORDER BY p.effective_from DESC LIMIT 1;
    IF policy_id IS NULL THEN
        RAISE EXCEPTION 'No applicable confirmed balance policy' USING ERRCODE='23514';
    END IF;
    variance:=total_input-total_output-declared_loss;
    allowed:=floor(total_input*tolerance*10000)/1000000;
    IF abs(variance)>allowed THEN
        RAISE EXCEPTION 'Mass variance exceeds confirmed tolerance' USING ERRCODE='23514';
    END IF;
    IF variance<>0 AND (
        operation.confirmed_version IS DISTINCT FROM operation.version
        OR grainledger.ledger_role(operation.confirmed_by) IS DISTINCT FROM 'MillManager'
        OR NOT grainledger.ledger_assigned(operation.confirmed_by,operation.facility_id)
    ) THEN
        RAISE EXCEPTION 'Nonzero variance requires current manager confirmation' USING ERRCODE='23514';
    END IF;
    INSERT INTO grainledger.ledger_events(id,kind,actor_id,operation_id,details,occurred_at)
    VALUES(event_id,operation.kind,a,op,jsonb_build_object(
        'policyId',policy_id,'declaredLossKg',declared_loss::text,
        'varianceKg',variance::text,'allowedVarianceKg',allowed::text,'content',operation.content,
        'resolvedInputs',resolved_inputs
    ),operation.occurred_at);
    FOR allocation IN SELECT value FROM jsonb_array_elements(resolved_inputs) LOOP
        PERFORM grainledger.ledger_post(event_id,(allocation->>'lotId')::uuid,
                            (allocation->>'locationId')::uuid,-grainledger.ledger_kg(allocation->>'quantityKg'));
    END LOOP;
    FOR output IN SELECT value FROM jsonb_array_elements(operation.content->'outputs') LOOP
        PERFORM grainledger.ledger_credit_location((output->>'locationId')::uuid,operation.owner_id);
        INSERT INTO grainledger.ledger_lots(id,code,owner_id,commodity_id,stage,product_identity,operation_id,code_origin_id)
        VALUES((output->>'id')::uuid,output->>'code',operation.owner_id,
               (output->>'commodityId')::uuid,output->>'stage',output->>'productIdentity',op,
               grainledger.ledger_register_code(output->>'code',(output->>'id')::uuid,NULL,original_operation));
        quantity:=grainledger.ledger_kg(output->>'quantityKg');
        PERFORM grainledger.ledger_post(event_id,(output->>'id')::uuid,(output->>'locationId')::uuid,quantity);

        -- Exact integer micro-kg: output -> input (ties input ID), input -> source (ties source ID).
        WITH input_weights AS (
            SELECT (i->>'lotId')::uuid input_id,sum(grainledger.ledger_kg(i->>'quantityKg')*1000000) mass
            FROM jsonb_array_elements(resolved_inputs) i GROUP BY (i->>'lotId')::uuid
        ), input_bases AS (
            SELECT input_id,div(quantity*1000000*mass,total_input*1000000) base,
                   mod(quantity*1000000*mass,total_input*1000000) remainder FROM input_weights
        ), input_ranked AS (
            SELECT *,row_number() OVER(ORDER BY remainder DESC,input_id) n,
                   quantity*1000000-sum(base) OVER() remaining FROM input_bases
        ), input_shares AS (
            SELECT input_id,base+CASE WHEN n<=remaining THEN 1 ELSE 0 END units FROM input_ranked
        ), source_weights AS (
            SELECT s.input_id,s.units,c.source_lot_id,c.quantity_kg*1000000 mass,
                   sum(c.quantity_kg*1000000) OVER(PARTITION BY s.input_id) total
            FROM input_shares s JOIN grainledger.ledger_contributions c ON c.lot_id=s.input_id
        ), source_bases AS (
            SELECT input_id,units,source_lot_id,div(units*mass,total) base,
                   mod(units*mass,total) remainder FROM source_weights
        ), source_ranked AS (
            SELECT *,row_number() OVER(PARTITION BY input_id ORDER BY remainder DESC,source_lot_id) n,
                   units-sum(base) OVER(PARTITION BY input_id) remaining FROM source_bases
        )
        SELECT COALESCE(jsonb_agg(jsonb_build_object('inputLotId',input_id,'sourceLotId',source_lot_id,
            'quantityKg',(base+CASE WHEN n<=remaining THEN 1 ELSE 0 END)/1000000)), '[]'::jsonb)
        INTO allocated_shares FROM source_ranked;
        -- Ordinary contribution history retains zero-rounded roots; custody ancestry is positive only.
        INSERT INTO grainledger.ledger_contributions(lot_id,source_lot_id,quantity_kg)
        SELECT (output->>'id')::uuid,(share->>'sourceLotId')::uuid,sum((share->>'quantityKg')::numeric)
        FROM jsonb_array_elements(allocated_shares) share GROUP BY (share->>'sourceLotId')::uuid;
        -- Persist actual allocations before custody exists too; later ingress must retain intermediate ancestry.
        INSERT INTO grainledger.ledger_custody_processing_allocations(event_id,operation_id,input_lot_id,output_lot_id,source_lot_id,quantity_kg)
        SELECT event_id,op,(share->>'inputLotId')::uuid,(output->>'id')::uuid,
            (share->>'sourceLotId')::uuid,(share->>'quantityKg')::numeric
        FROM jsonb_array_elements(allocated_shares) share WHERE (share->>'quantityKg')::numeric>0;
        IF (SELECT sum(quantity_kg) FROM grainledger.ledger_contributions WHERE lot_id=(output->>'id')::uuid)
           IS DISTINCT FROM quantity THEN
            RAISE EXCEPTION 'Lineage does not conserve output mass' USING ERRCODE='23514';
        END IF;
        actual_id:=grainledger.ledger_pool_ingress(a,op,'PROCESSING_OUTPUT',(output->>'locationId')::uuid,
            (output->>'id')::uuid,quantity);
    END LOOP;
    SELECT COALESCE(jsonb_agg(jsonb_build_object('outputLotId',o.value->>'id',
        'locationId',o.value->>'locationId','currentPoolLotId',pool.lot_id) ORDER BY o.ordinal),'[]'::jsonb)
    INTO pool_outputs FROM jsonb_array_elements(operation.content->'outputs') WITH ORDINALITY o(value,ordinal)
    JOIN grainledger.ledger_locations loc ON loc.id=(o.value->>'locationId')::uuid AND loc.mixed_bulk
    JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.location_id=loc.id;
    UPDATE grainledger.ledger_operations
    SET status='COMMITTED',variance_kg=variance,allowed_variance_kg=allowed,correction_id=corr
    WHERE id=op;
    actual_allocation:=grainledger.ledger_operation_json(op)||jsonb_build_object('resolvedInputs',resolved_inputs,'poolOutputs',pool_outputs);
    INSERT INTO grainledger.ledger_mixed_bulk_command_results(action,command_id,actor_id,request,result)
    VALUES('processing',op,a,jsonb_build_object('content',operation.content,'correctionId',corr),actual_allocation);
    RETURN actual_allocation;
END $_$;

CREATE FUNCTION grainledger.ledger_complete_receipt_package() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    IF TG_TABLE_NAME<>'ledger_lots' THEN
        RAISE EXCEPTION 'Procurement corrections are unsupported by official Capture' USING ERRCODE='23514';
    END IF;
    IF NEW.receipt_id IS NOT NULL AND grainledger.ledger_procurement_package_complete(NEW.receipt_id) IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'Procurement, source lot, event and credit must complete atomically' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION grainledger.ledger_complete_receiving_resolution() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    RAISE EXCEPTION 'Arrival and receiving reconciliation are unsupported by official Capture' USING ERRCODE='23514';
END $$;

CREATE FUNCTION grainledger.ledger_confirm_planning_quality_observation(a uuid, p jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
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
    PERFORM grainledger.ledger_lock_writes();
    IF grainledger.ledger_role(a) <> 'CustomerManager' OR NOT grainledger.ledger_own(a,v_owner) THEN
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
    SELECT unit INTO expected_unit FROM grainledger.catalog_quality_attributes WHERE id=v_attribute;
    IF expected_unit IS NULL OR expected_unit IS DISTINCT FROM p->>'unit' OR
       NOT EXISTS (SELECT 1 FROM grainledger.ledger_lots WHERE id=v_lot AND owner_id=v_owner
                   AND stage IN ('HEAD_RICE','BROKEN_RICE','BLENDED_RICE','BAGGED_RICE')) OR
       NOT EXISTS (SELECT 1 FROM grainledger.ledger_positions q JOIN grainledger.ledger_locations l ON l.id=q.location_id
                   WHERE q.lot_id=v_lot AND l.facility_id=v_facility) OR
       NOT EXISTS (SELECT 1 FROM grainledger.ledger_evidence WHERE id=v_evidence
                   AND owner_id=v_owner AND facility_id=v_facility) THEN
        RAISE EXCEPTION 'Lot, facility, attribute unit and evidence must match' USING ERRCODE='22023';
    END IF;
    SELECT coalesce(max(q.version_no),0) INTO current_version
      FROM grainledger.ledger_planning_quality_observations q
      WHERE q.owner_id=v_owner AND q.lot_id=v_lot AND q.attribute_id=v_attribute;
    IF version_no <> current_version + 1 THEN
        RAISE EXCEPTION 'Quality observation version must follow current version' USING ERRCODE='23514';
    END IF;
    INSERT INTO grainledger.ledger_planning_quality_observations
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

CREATE FUNCTION grainledger.ledger_credit_location(l uuid, o uuid) RETURNS void
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE
    destination grainledger.ledger_locations;
BEGIN
    PERFORM grainledger.ledger_require_active_location(l);
    SELECT * INTO STRICT destination FROM grainledger.ledger_locations WHERE id=l FOR UPDATE;
    IF destination.kind='COMPARTMENT' THEN
        IF destination.owner_id IS DISTINCT FROM o
           AND (destination.owner_id IS NOT NULL OR NOT destination.cleared) THEN
            RAISE EXCEPTION 'Compartment requires empty-stock recorded clearance before owner switch'
                USING ERRCODE='23514';
        END IF;
        UPDATE grainledger.ledger_locations SET owner_id=o,cleared=false,version=gen_random_uuid() WHERE id=l;
    END IF;
END $$;

CREATE FUNCTION grainledger.ledger_custody_allocation_guard() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE checked_event uuid := COALESCE(NEW.event_id,OLD.event_id);
BEGIN
    IF EXISTS(
        SELECT 1 FROM grainledger.ledger_custody_edges edge
        LEFT JOIN LATERAL (
            SELECT sum(allocation.quantity_kg) quantity
            FROM grainledger.ledger_custody_allocations allocation
            WHERE allocation.event_id=edge.event_id AND allocation.input_lot_id=edge.input_lot_id
              AND allocation.output_lot_id=edge.output_lot_id
        ) actual ON true
        WHERE edge.event_id=checked_event AND actual.quantity IS DISTINCT FROM edge.quantity_kg
    ) THEN
        RAISE EXCEPTION 'Custody source allocations must exactly conserve each input edge' USING ERRCODE='23514';
    END IF;
    IF EXISTS(
        SELECT 1 FROM grainledger.ledger_custody_edges edge
        WHERE edge.event_id=checked_event
        GROUP BY edge.output_lot_id
        HAVING sum(edge.quantity_kg) IS DISTINCT FROM (
            SELECT COALESCE(sum(contribution.quantity_kg),0)
            FROM grainledger.ledger_contributions contribution WHERE contribution.lot_id=edge.output_lot_id
        )
    ) THEN
        RAISE EXCEPTION 'Custody output contribution mass must equal incoming edges' USING ERRCODE='23514';
    END IF;
    IF EXISTS(
        WITH expected AS (
            SELECT output_lot_id,source_lot_id,sum(quantity_kg) quantity
            FROM grainledger.ledger_custody_allocations WHERE event_id=checked_event
            GROUP BY output_lot_id,source_lot_id
        ), actual AS (
            SELECT contribution.lot_id output_lot_id,contribution.source_lot_id,sum(contribution.quantity_kg) quantity
            FROM grainledger.ledger_contributions contribution
            WHERE contribution.lot_id IN (
                SELECT edge.output_lot_id FROM grainledger.ledger_custody_edges edge WHERE edge.event_id=checked_event
            )
            GROUP BY contribution.lot_id,contribution.source_lot_id
        )
        SELECT 1 FROM expected FULL JOIN actual USING(output_lot_id,source_lot_id)
        WHERE expected.quantity IS DISTINCT FROM actual.quantity
    ) THEN
        RAISE EXCEPTION 'Custody output source identity must equal persisted allocation shares' USING ERRCODE='23514';
    END IF;
    RETURN NULL;
END $$;

CREATE FUNCTION grainledger.ledger_custody_processing_allocation_guard() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE checked_event uuid := COALESCE(NEW.event_id,OLD.event_id);
BEGIN
    IF EXISTS(
        SELECT 1 FROM grainledger.ledger_custody_processing_allocations allocation
        JOIN grainledger.ledger_events event ON event.id=allocation.event_id
        WHERE allocation.event_id=checked_event AND event.operation_id IS DISTINCT FROM allocation.operation_id
    ) THEN
        RAISE EXCEPTION 'Processing ancestry operation must match committed event' USING ERRCODE='23514';
    END IF;
    IF EXISTS(
        WITH expected AS (
            SELECT output_lot_id,source_lot_id,sum(quantity_kg) quantity
            FROM grainledger.ledger_custody_processing_allocations WHERE event_id=checked_event
            GROUP BY output_lot_id,source_lot_id
        ), actual AS (
            SELECT contribution.lot_id output_lot_id,contribution.source_lot_id,sum(contribution.quantity_kg) quantity
            FROM grainledger.ledger_contributions contribution
            WHERE contribution.quantity_kg>0 AND contribution.lot_id IN (
                SELECT allocation.output_lot_id FROM grainledger.ledger_custody_processing_allocations allocation
                WHERE allocation.event_id=checked_event
            )
            GROUP BY contribution.lot_id,contribution.source_lot_id
        )
        SELECT 1 FROM expected FULL JOIN actual USING(output_lot_id,source_lot_id)
        WHERE expected.quantity IS DISTINCT FROM actual.quantity
    ) THEN
        RAISE EXCEPTION 'Processing ancestry root shares must equal committed output attribution' USING ERRCODE='23514';
    END IF;
    RETURN NULL;
END $$;

CREATE FUNCTION grainledger.ledger_effective_hold_ids(l uuid) RETURNS SETOF uuid
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    WITH RECURSIVE lineage_allocations(input_lot_id,output_lot_id,source_lot_id,quantity_kg) AS (
        SELECT input_lot_id,output_lot_id,source_lot_id,quantity_kg
        FROM grainledger.ledger_custody_allocations WHERE quantity_kg>0
        UNION
        SELECT input_lot_id,output_lot_id,source_lot_id,quantity_kg
        FROM grainledger.ledger_custody_processing_allocations WHERE quantity_kg>0
    ), source_paths(lot_id,source_lot_id) AS (
        SELECT l,source_lot_id FROM grainledger.ledger_contributions
        WHERE lot_id=l AND quantity_kg>0
        UNION
        SELECT l,source_lot_id FROM grainledger.ledger_custody_allocations
        WHERE output_lot_id=l AND quantity_kg>0
        UNION
        SELECT l,source_lot_id FROM grainledger.ledger_custody_processing_allocations
        WHERE output_lot_id=l AND quantity_kg>0
        UNION
        SELECT allocation.input_lot_id,path.source_lot_id
        FROM source_paths path
        JOIN lineage_allocations allocation
          ON allocation.output_lot_id=path.lot_id
         AND allocation.source_lot_id=path.source_lot_id
         AND allocation.quantity_kg>0
    ), derived(value) AS (
        SELECT grainledger.ledger_mixed_bulk_custody_derived(l)
    ), effective_lots(lot_id) AS (
        SELECT l FROM derived
        UNION
        SELECT path.lot_id FROM source_paths path CROSS JOIN derived WHERE derived.value
        UNION
        SELECT path.source_lot_id FROM source_paths path CROSS JOIN derived WHERE derived.value
    )
    SELECT DISTINCT h.id
    FROM effective_lots related
    JOIN grainledger.ledger_holds h ON h.lot_id=related.lot_id
    WHERE h.lot_id=l
       OR EXISTS (
            SELECT 1 FROM source_paths path
            WHERE path.lot_id=h.lot_id
       )
$$;

CREATE FUNCTION grainledger.ledger_equipment_has_pending_work(equipment_id uuid) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $_$
    SELECT EXISTS(SELECT 1 FROM grainledger.ledger_operations op
        WHERE op.equipment_id=$1 AND op.status='DRAFT' AND NOT op.is_deleted)
        OR EXISTS(SELECT 1 FROM grainledger.ledger_corrections c
            WHERE c.status IN ('REQUESTED','RECONCILED') AND (
                EXISTS(SELECT 1 FROM grainledger.ledger_operations op
                    WHERE op.id IN (c.operation_id,c.replacement_operation_id) AND op.equipment_id=$1)
                OR translate(lower(c.replacement_content->>'equipmentId'),'-{}','')=replace($1::text,'-','')))
$_$;

CREATE FUNCTION grainledger.ledger_immutable() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    RAISE EXCEPTION 'Committed history is immutable' USING ERRCODE='23514';
END $$;

CREATE FUNCTION grainledger.ledger_kg(v text) RETURNS numeric
    LANGUAGE plpgsql IMMUTABLE
    SET search_path TO 'pg_catalog'
    AS $_$
BEGIN
    IF v IS NULL OR v !~ '^-?[0-9]{1,14}(\.[0-9]{1,6})?$' THEN
        RAISE EXCEPTION 'Unsupported decimal precision or overflow' USING ERRCODE='23514';
    END IF;
    RETURN v::numeric(20,6);
END $_$;

CREATE FUNCTION grainledger.ledger_legacy_procurement_package_complete(r uuid) RETURNS boolean
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE
    h grainledger.capture_procurements;
    ev grainledger.capture_business_events;
    snapshot jsonb;
    row record;
    item_snapshot jsonb;
    commodity uuid;
    location uuid;
    count_items integer;
BEGIN
    SELECT * INTO h FROM grainledger.capture_procurements WHERE id=r AND status='COMMITTED';
    IF NOT FOUND OR h.event_id IS NULL THEN RETURN false; END IF;
    SELECT * INTO ev FROM grainledger.capture_business_events WHERE id=h.event_id;
    IF NOT FOUND OR ev.company_id IS DISTINCT FROM h.company_id
       OR ev.actor_id IS DISTINCT FROM h.received_by OR ev.event_type<>'PROCUREMENT_COMMITTED'
       OR ev.occurred_at<>h.received_at THEN RETURN false; END IF;
    snapshot:=ev.payload->'result';
    commodity:=(snapshot->>'CommodityId')::uuid;
    location:=(snapshot->>'ReceivingLocationId')::uuid;
    IF snapshot IS NULL OR (snapshot->>'Id')::uuid IS DISTINCT FROM h.id
       OR snapshot->>'Code' IS DISTINCT FROM h.code
       OR (snapshot->>'FarmerId')::uuid IS DISTINCT FROM h.farmer_id
       OR (snapshot->>'CollectionSiteId')::uuid IS DISTINCT FROM h.collection_site_id
       OR (snapshot->>'ReceivedBy')::uuid IS DISTINCT FROM h.received_by
       OR (snapshot->>'EventId')::uuid IS DISTINCT FROM ev.id
       OR (snapshot->>'ReceivedAt')::timestamptz IS DISTINCT FROM h.received_at
       OR (snapshot->>'RecordedAt')::timestamptz IS DISTINCT FROM ev.recorded_at
       OR snapshot->>'Status' IS DISTINCT FROM 'COMMITTED'
       OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_commodities WHERE id=commodity AND code='PADDY')
       OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_locations WHERE id=location AND facility_id=h.collection_site_id)
       OR NOT EXISTS(SELECT 1 FROM grainledger.capture_offline_operations o WHERE o.applied_event_id=ev.id
           AND o.company_id=h.company_id AND o.submitted_by=h.received_by AND o.status='APPLIED'
           AND o.client_operation_id=ev.correlation_id AND o.result_payload=snapshot
           AND o.captured_at=(snapshot->>'CapturedAt')::timestamptz
           AND o.payload->>'operationType'='PROCUREMENT_COMMITTED') THEN RETURN false; END IF;
    SELECT count(*) INTO count_items FROM grainledger.capture_procurement_items WHERE procurement_id=r;
    IF count_items NOT BETWEEN 1 AND 100 OR jsonb_typeof(snapshot->'Items') IS DISTINCT FROM 'array'
       OR jsonb_array_length(snapshot->'Items')<>count_items
       OR (SELECT count(*) FROM grainledger.ledger_lots WHERE receipt_id=r)<>count_items
       OR (snapshot->>'TotalNetMassKg')::numeric IS DISTINCT FROM
          (SELECT sum(net_mass_kg) FROM grainledger.capture_procurement_items WHERE procurement_id=r)
       OR (SELECT count(DISTINCT (x->>'Id')::uuid) FROM jsonb_array_elements(snapshot->'Items') x)<>count_items
       THEN RETURN false; END IF;
    FOR row IN
        SELECT i.*,sl.id source_id,sl.lot_id,ps.plot_id,ps.season_id,ps.rice_variety,p.farmer_id
        FROM grainledger.capture_procurement_items i
        LEFT JOIN grainledger.capture_source_lots sl ON sl.procurement_item_id=i.id AND sl.company_id=i.company_id
        JOIN grainledger.capture_plot_seasons ps ON ps.id=i.plot_season_id AND ps.company_id=i.company_id
        JOIN grainledger.capture_plots p ON p.id=ps.plot_id AND p.company_id=ps.company_id
        JOIN grainledger.capture_crop_seasons s ON s.id=ps.season_id AND s.company_id=ps.company_id
        WHERE i.procurement_id=r
    LOOP
        SELECT x INTO item_snapshot FROM jsonb_array_elements(snapshot->'Items') x WHERE (x->>'Id')::uuid=row.id;
        IF row.company_id<>h.company_id OR row.farmer_id<>h.farmer_id OR row.source_id IS NULL
           OR row.line_no NOT BETWEEN 1 AND count_items
           OR (item_snapshot->>'LineNo')::integer IS DISTINCT FROM row.line_no
           OR (item_snapshot->>'PlotSeasonId')::uuid IS DISTINCT FROM row.plot_season_id
           OR (item_snapshot->>'PlotId')::uuid IS DISTINCT FROM row.plot_id
           OR (item_snapshot->>'SeasonId')::uuid IS DISTINCT FROM row.season_id
           OR item_snapshot->>'RiceVariety' IS DISTINCT FROM row.rice_variety
           OR (item_snapshot->>'SourceLotId')::uuid IS DISTINCT FROM row.source_id
           OR (item_snapshot->>'LotId')::uuid IS DISTINCT FROM row.lot_id
           OR item_snapshot->>'Stage' IS DISTINCT FROM 'RAW_PADDY'
           OR (item_snapshot->>'MeasuredValue')::numeric IS DISTINCT FROM row.measured_value
           OR item_snapshot->>'MeasuredUnit' IS DISTINCT FROM row.measured_unit
           OR (item_snapshot->>'NetMassKg')::numeric IS DISTINCT FROM row.net_mass_kg
           OR (item_snapshot->>'MoisturePercent')::numeric IS DISTINCT FROM row.moisture_percent
           OR (item_snapshot->'InitialStock'->>'LocationId')::uuid IS DISTINCT FROM location
           OR (item_snapshot->'InitialStock'->>'ReceivedKg')::numeric IS DISTINCT FROM row.net_mass_kg
           OR row.net_mass_kg IS DISTINCT FROM (CASE row.measured_unit
               WHEN 'KG' THEN row.measured_value WHEN 'G' THEN row.measured_value/1000
               WHEN 'T' THEN row.measured_value*1000 END)
           OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_measurement_units WHERE code=row.measured_unit AND quantity_kind='MASS')
           OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_lots l JOIN grainledger.ledger_catalog_commodities m ON m.ledger_commodity_id=l.commodity_id
               WHERE l.id=row.lot_id AND l.owner_id=h.company_id AND l.receipt_id=r AND l.receipt_line_id=row.id
               AND l.code=item_snapshot->>'LotCode' AND l.stage='RAW_PADDY' AND l.product_identity='RAW_PADDY'
               AND m.catalog_commodity_id=commodity)
           OR (SELECT count(*) FROM grainledger.ledger_events e WHERE e.lot_id=row.lot_id AND e.kind='RECEIPT')<>1
           OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_events e JOIN grainledger.ledger_postings pp ON pp.event_id=e.id
               WHERE e.lot_id=row.lot_id AND e.kind='RECEIPT' AND e.actor_id=h.received_by AND e.occurred_at=h.received_at
               AND e.details->>'receiptId'=r::text AND e.details->>'receiptLineId'=row.id::text
               AND e.details->>'captureEventId'=ev.id::text AND e.details->>'sourceLotId'=row.source_id::text
               AND e.details->>'catalogCommodityId'=commodity::text
               AND e.details->>'farmerId'=h.farmer_id::text AND e.details->>'plotSeasonId'=row.plot_season_id::text
               AND e.details->>'plotId'=row.plot_id::text AND e.details->>'seasonId'=row.season_id::text
               AND e.details->>'riceVariety'=row.rice_variety
               AND (e.details->>'originalQuantity')::numeric=row.measured_value AND e.details->>'originalUnit'=row.measured_unit
               AND (e.details->>'quantityKg')::numeric=row.net_mass_kg
               AND (e.details->>'moisturePercent')::numeric=row.moisture_percent
               AND pp.lot_id=row.lot_id AND pp.location_id=location AND pp.quantity_kg=row.net_mass_kg
               AND pp.reverses_posting_id IS NULL
               AND (SELECT count(*) FROM grainledger.ledger_postings WHERE event_id=e.id)=1)
           OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_contributions WHERE lot_id=row.lot_id AND source_lot_id=row.lot_id AND quantity_kg=row.net_mass_kg)
           OR (SELECT count(*) FROM grainledger.ledger_contributions WHERE lot_id=row.lot_id)<>1 THEN RETURN false; END IF;
    END LOOP;
    RETURN true;
END $$;

CREATE FUNCTION grainledger.ledger_lock_writes() RETURNS void
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    -- A fresh eligibility snapshot after the advisory wait is required.
    IF current_setting('transaction_isolation') IS DISTINCT FROM 'read committed' THEN
        RAISE EXCEPTION 'Protected stock writes require READ COMMITTED isolation' USING ERRCODE='23514';
    END IF;
    PERFORM pg_advisory_xact_lock(714928352);
END $$;

CREATE FUNCTION grainledger.ledger_lot_code_token(v_stage text, v_product text) RETURNS text
    LANGUAGE sql IMMUTABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT CASE
        WHEN v_stage='RAW_PADDY' AND v_product='RAW_PADDY' THEN 'RAW'
        WHEN v_stage='DRIED_PADDY' AND v_product='DRIED_PADDY' THEN 'DRY'
        WHEN v_stage='HEAD_RICE' AND v_product='HEAD_RICE' THEN 'HDR'
        WHEN v_stage='BROKEN_RICE' AND v_product='BROKEN_RICE' THEN 'BRK'
        WHEN v_stage='BRAN' AND v_product='BRAN' THEN 'BRN'
        WHEN v_stage='HUSK' AND v_product='HUSK' THEN 'HSK'
        WHEN v_stage='BLENDED_RICE' AND v_product='BLENDED_RICE' THEN 'BLD'
        WHEN v_stage='BAGGED_RICE' AND v_product='HEAD_RICE' THEN 'BGH'
        WHEN v_stage='BAGGED_RICE' AND v_product='BROKEN_RICE' THEN 'BGB'
        WHEN v_stage='BAGGED_RICE' AND v_product='BLENDED_RICE' THEN 'BGL'
    END
$$;

CREATE FUNCTION grainledger.ledger_lot_json(a uuid, l uuid, detail boolean DEFAULT false) RETURNS jsonb
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT jsonb_build_object(
        'id',x.id,'code',x.code,'ownerId',x.owner_id,'commodityId',x.commodity_id,
        'stage',x.stage,'productIdentity',x.product_identity,
        'positions',coalesce((
            SELECT jsonb_agg(jsonb_build_object(
                'locationId',p.location_id,'facilityId',lc.facility_id,
                'quantityKg',p.quantity_kg::text,
                'availableKg',CASE WHEN grainledger.ledger_blocked(l) THEN '0.000000' ELSE p.quantity_kg::text END
            ) || CASE WHEN lc.mixed_bulk AND pool.lot_id=l THEN jsonb_build_object('poolRevision',pool.revision) ELSE '{}'::jsonb END)
            FROM grainledger.ledger_positions p JOIN grainledger.ledger_locations lc ON lc.id=p.location_id
            LEFT JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.location_id=lc.id
            WHERE p.lot_id=l AND (grainledger.ledger_own(a,x.owner_id) OR grainledger.ledger_assigned(a,lc.facility_id))
        ),'[]'::jsonb),
        'restrictions',to_jsonb(array_remove(ARRAY[
            CASE WHEN EXISTS(SELECT 1 FROM grainledger.ledger_effective_hold_ids(l)) THEN 'HOLD' END,
            CASE WHEN EXISTS (
                SELECT 1 FROM grainledger.ledger_corrections
                WHERE operation_id=x.operation_id AND status IN ('REQUESTED','RECONCILED')
            ) OR EXISTS (
                SELECT 1 FROM grainledger.ledger_receipt_correction_blocks WHERE receipt_id=x.receipt_id
            ) THEN 'CORRECTION' END,
            CASE WHEN grainledger.ledger_receiving_restricted(l) THEN 'RECEIVING_DISCREPANCY' END,
            CASE WHEN EXISTS (
                SELECT 1 FROM grainledger.ledger_receipt_reversals WHERE receipt_id=x.receipt_id
            ) OR EXISTS (
                SELECT 1 FROM grainledger.ledger_operations WHERE id=x.operation_id AND status='CORRECTED'
            ) THEN 'SUPERSEDED' END
        ],NULL)),
        'mixedBulk',EXISTS(SELECT 1 FROM grainledger.ledger_custody_edges ce WHERE ce.input_lot_id=l OR ce.output_lot_id=l)
            OR EXISTS(SELECT 1 FROM grainledger.ledger_positions p JOIN grainledger.ledger_locations lc ON lc.id=p.location_id WHERE p.lot_id=l AND lc.mixed_bulk),
        'poolLocationId',coalesce(
            (SELECT pool.location_id FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.lot_id=l),
            (SELECT ce.location_id FROM grainledger.ledger_custody_events ce WHERE ce.input_lot_id=l OR ce.output_lot_id=l ORDER BY ce.created_at DESC LIMIT 1)),
        'poolRevision',(SELECT pool.revision FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.lot_id=l),
        'isCurrentPool',EXISTS(SELECT 1 FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.lot_id=l),
        'custodyDerived',grainledger.ledger_mixed_bulk_custody_derived(l),
        'sourceComposition',CASE WHEN detail THEN coalesce((
            SELECT jsonb_agg(jsonb_build_object('sourceLotId',shares.source_lot_id,'sourceLotCode',source.code,'quantityKg',shares.quantity_kg::text) ORDER BY shares.source_lot_id)
            FROM (
                SELECT ca.source_lot_id,sum(ca.quantity_kg) quantity_kg FROM grainledger.ledger_custody_allocations ca
                WHERE ca.output_lot_id=l GROUP BY ca.source_lot_id
                UNION ALL
                SELECT cpa.source_lot_id,sum(cpa.quantity_kg) FROM grainledger.ledger_custody_processing_allocations cpa
                WHERE cpa.output_lot_id=l GROUP BY cpa.source_lot_id
                UNION ALL
                SELECT c.source_lot_id,sum(c.quantity_kg) FROM grainledger.ledger_contributions c
                WHERE c.lot_id=l
                  AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_custody_allocations ca WHERE ca.output_lot_id=l)
                  AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_custody_processing_allocations cpa WHERE cpa.output_lot_id=l)
                GROUP BY c.source_lot_id
            ) shares JOIN grainledger.ledger_lots source ON source.id=shares.source_lot_id
        ),'[]'::jsonb) ELSE '[]'::jsonb END
    ) || CASE WHEN detail AND grainledger.ledger_role(a)<>'MillOperator' THEN
        jsonb_build_object(
            'contributions',coalesce((
                SELECT jsonb_agg(jsonb_build_object('sourceLotId',source_lot_id,'quantityKg',quantity_kg::text))
                FROM grainledger.ledger_contributions WHERE lot_id=l
            ),'[]'::jsonb),
            'custodyLinks',coalesce((
                SELECT jsonb_agg(jsonb_build_object('eventId',links.event_id,'inputLotId',links.input_lot_id,'outputLotId',links.output_lot_id,'quantityKg',links.quantity_kg::text,'locationId',links.location_id,'poolRevision',links.revision) ORDER BY links.created_at,links.event_id)
                FROM (
                    SELECT ce.event_id,ce.input_lot_id,ce.output_lot_id,ce.quantity_kg,ce.location_id,ce.revision,ce.created_at
                    FROM grainledger.ledger_custody_events ce WHERE ce.input_lot_id=l OR ce.output_lot_id=l
                    UNION ALL
                    SELECT ce.event_id,cpa.input_lot_id,cpa.output_lot_id,cpa.quantity_kg,ce.location_id,ce.revision,ce.created_at
                    FROM grainledger.ledger_custody_processing_allocations cpa JOIN grainledger.ledger_custody_events ce ON ce.event_id=cpa.event_id
                    WHERE cpa.input_lot_id=l OR cpa.output_lot_id=l
                ) links
                WHERE grainledger.ledger_can_read_lot(a,coalesce(links.input_lot_id,links.output_lot_id))
            ),'[]'::jsonb)
        )
    ELSE '{}'::jsonb END
    FROM grainledger.ledger_lots x
    WHERE x.id=l AND grainledger.ledger_can_read_lot(a,l)
$$;

CREATE FUNCTION grainledger.ledger_mixed_bulk_assert_location(loc uuid) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE location grainledger.ledger_locations; pool grainledger.ledger_mixed_bulk_pools; amount numeric; current_lot grainledger.ledger_lots;
BEGIN
    SELECT * INTO STRICT location FROM grainledger.ledger_locations WHERE id=loc FOR UPDATE;
    SELECT * INTO pool FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=loc FOR UPDATE;
    IF location.mixed_bulk AND pool.location_id IS NULL THEN
        INSERT INTO grainledger.ledger_mixed_bulk_pools(location_id) VALUES(loc);
        SELECT * INTO STRICT pool FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=loc FOR UPDATE;
    ELSIF NOT location.mixed_bulk AND pool.location_id IS NOT NULL AND pool.lot_id IS NOT NULL THEN
        RAISE EXCEPTION 'Cannot disable mixed-bulk mode while pool is occupied' USING ERRCODE='23514';
    END IF;
    IF pool.location_id IS NOT NULL AND pool.lot_id IS NOT NULL THEN
        SELECT * INTO STRICT current_lot FROM grainledger.ledger_lots WHERE id=pool.lot_id;
        SELECT quantity_kg INTO amount FROM grainledger.ledger_positions WHERE lot_id=pool.lot_id AND location_id=loc;
        IF amount IS NULL OR amount<=0 OR EXISTS(
            SELECT 1 FROM grainledger.ledger_positions WHERE location_id=loc AND quantity_kg>0 AND lot_id<>pool.lot_id
        ) THEN RAISE EXCEPTION 'Mixed-bulk location must contain only current positive generation' USING ERRCODE='23514'; END IF;
        IF current_lot.owner_id IS DISTINCT FROM location.owner_id AND location.owner_id IS NOT NULL THEN
            RAISE EXCEPTION 'Mixed-bulk pool owner differs from location owner' USING ERRCODE='23514';
        END IF;
    ELSIF pool.location_id IS NOT NULL AND EXISTS(
        SELECT 1 FROM grainledger.ledger_positions WHERE location_id=loc AND quantity_kg>0
    ) THEN
        RAISE EXCEPTION 'Mixed-bulk location has stock without current generation' USING ERRCODE='23514';
    END IF;
END $$;

CREATE FUNCTION grainledger.ledger_mixed_bulk_custody_derived(l uuid) RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    WITH RECURSIVE lineage_allocations(input_lot_id,output_lot_id,source_lot_id,quantity_kg) AS (
        SELECT input_lot_id,output_lot_id,source_lot_id,quantity_kg
        FROM grainledger.ledger_custody_allocations WHERE quantity_kg>0
        UNION
        SELECT input_lot_id,output_lot_id,source_lot_id,quantity_kg
        FROM grainledger.ledger_custody_processing_allocations WHERE quantity_kg>0
    ), source_paths(lot_id,source_lot_id) AS (
        SELECT l,source_lot_id FROM grainledger.ledger_contributions
        WHERE lot_id=l AND quantity_kg>0
        UNION
        SELECT l,source_lot_id FROM grainledger.ledger_custody_allocations
        WHERE output_lot_id=l AND quantity_kg>0
        UNION
        SELECT l,source_lot_id FROM grainledger.ledger_custody_processing_allocations
        WHERE output_lot_id=l AND quantity_kg>0
        UNION
        SELECT allocation.input_lot_id,path.source_lot_id
        FROM source_paths path
        JOIN lineage_allocations allocation
          ON allocation.output_lot_id=path.lot_id
         AND allocation.source_lot_id=path.source_lot_id
         AND allocation.quantity_kg>0
    )
    SELECT EXISTS (
        SELECT 1 FROM grainledger.ledger_mixed_bulk_pools WHERE lot_id=l
    ) OR EXISTS (
        SELECT 1 FROM source_paths path
        JOIN grainledger.ledger_custody_edges edge ON edge.output_lot_id=path.lot_id AND edge.quantity_kg>0
    )
$$;

CREATE FUNCTION grainledger.ledger_mixed_bulk_eligible(loc uuid, owner uuid, commodity uuid, stage text, product text, at_time timestamp with time zone) RETURNS uuid
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $_$
    SELECT p.id FROM grainledger.ledger_locations l
    JOIN grainledger.ledger_mixed_bulk_policies p ON p.location_id=l.id AND p.location_kind=l.kind
      AND p.owner_id=$2 AND p.commodity_id=$3 AND p.stage=$4
      AND p.product_identity=$5 AND p.effective_from<=$6
    ORDER BY p.effective_from DESC,p.id LIMIT 1
$_$;

CREATE FUNCTION grainledger.ledger_mixed_bulk_independent_blocked(l uuid, ignore_correction uuid DEFAULT NULL::uuid) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT grainledger.ledger_receiving_restricted(l)
        OR EXISTS (
            SELECT 1 FROM grainledger.ledger_corrections c JOIN grainledger.ledger_lots x ON x.operation_id=c.operation_id
            WHERE x.id=l AND c.status IN ('REQUESTED','RECONCILED')
              AND c.id IS DISTINCT FROM ignore_correction
        )
        OR EXISTS (
            SELECT 1 FROM grainledger.ledger_receipt_correction_blocks r
            JOIN grainledger.ledger_lots x ON x.receipt_id=r.receipt_id WHERE x.id=l
        )
        OR EXISTS (
            SELECT 1 FROM grainledger.ledger_receipt_reversals r
            JOIN grainledger.ledger_lots x ON x.receipt_id=r.receipt_id WHERE x.id=l
        )
$$;

CREATE FUNCTION grainledger.ledger_mixed_bulk_location_guard() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    IF TG_OP='UPDATE' AND OLD.mixed_bulk AND NEW.cleared
       AND ((NEW.owner_id IS NULL
             AND (OLD.cleared IS DISTINCT FROM NEW.cleared OR OLD.owner_id IS DISTINCT FROM NEW.owner_id))
            OR NOT NEW.mixed_bulk)
       AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE location_id=OLD.id AND quantity_kg>0)
       AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=OLD.id AND lot_id IS NOT NULL) THEN
        UPDATE grainledger.ledger_mixed_bulk_pools
        SET owner_id=NULL,commodity_id=NULL,stage=NULL,product_identity=NULL
        WHERE location_id=OLD.id;
    END IF;
    IF TG_OP='UPDATE' AND (
       NEW.mixed_bulk IS DISTINCT FROM OLD.mixed_bulk
       OR (OLD.mixed_bulk OR NEW.mixed_bulk) AND (
           NEW.kind IS DISTINCT FROM OLD.kind OR NEW.owner_id IS DISTINCT FROM OLD.owner_id
       )
    ) THEN
        IF EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE location_id=OLD.id AND quantity_kg>0)
           OR EXISTS(SELECT 1 FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=OLD.id AND lot_id IS NOT NULL) THEN
            RAISE EXCEPTION 'Occupied mixed-bulk location requires drain and recorded clearance before identity or mode change' USING ERRCODE='23514';
        END IF;
        IF OLD.mixed_bulk AND (
            NEW.kind IS DISTINCT FROM OLD.kind OR NEW.owner_id IS DISTINCT FROM OLD.owner_id
            OR NEW.mixed_bulk IS DISTINCT FROM OLD.mixed_bulk
        ) THEN
            IF NOT NEW.cleared AND NOT (
                OLD.cleared AND OLD.owner_id IS NULL AND NEW.owner_id IS NOT NULL
                AND NEW.kind=OLD.kind AND NEW.mixed_bulk=OLD.mixed_bulk
            ) THEN
                RAISE EXCEPTION 'Mixed-bulk identity or mode change requires recorded clearance' USING ERRCODE='23514';
            END IF;
            IF NEW.owner_id IS NOT NULL THEN NEW.cleared:=false; END IF;
        END IF;
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION grainledger.ledger_mixed_bulk_pool_deferred_guard() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE loc uuid := COALESCE(NEW.location_id,OLD.location_id); current_id uuid; amount numeric;
BEGIN
    IF (SELECT mixed_bulk FROM grainledger.ledger_locations WHERE id=loc) THEN
        IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=loc) THEN
            RAISE EXCEPTION 'Mixed-bulk location lacks pool state' USING ERRCODE='23514';
        END IF;
        SELECT lot_id INTO current_id FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=loc;
        IF current_id IS NULL THEN
            IF EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE location_id=loc AND quantity_kg>0) THEN
                RAISE EXCEPTION 'Positive mixed-bulk stock lacks current generation pointer' USING ERRCODE='23514';
            END IF;
        ELSE
            SELECT quantity_kg INTO amount FROM grainledger.ledger_positions WHERE lot_id=current_id AND location_id=loc;
            IF amount IS NULL OR amount<=0 OR EXISTS(
                SELECT 1 FROM grainledger.ledger_positions WHERE location_id=loc AND quantity_kg>0 AND lot_id<>current_id
            ) THEN RAISE EXCEPTION 'Mixed-bulk current generation does not match physical position' USING ERRCODE='23514'; END IF;
            IF EXISTS(
                SELECT 1 FROM grainledger.ledger_mixed_bulk_pools pool JOIN grainledger.ledger_lots lot ON lot.id=current_id
                WHERE pool.location_id=loc AND (
                    pool.owner_id IS DISTINCT FROM lot.owner_id
                    OR pool.commodity_id IS DISTINCT FROM lot.commodity_id
                    OR pool.stage IS DISTINCT FROM lot.stage
                    OR pool.product_identity IS DISTINCT FROM lot.product_identity
                )
            ) THEN RAISE EXCEPTION 'Mixed-bulk generation identity differs from retained pool identity' USING ERRCODE='23514'; END IF;
            IF (SELECT sum(quantity_kg) FROM grainledger.ledger_contributions WHERE lot_id=current_id) IS DISTINCT FROM amount THEN
                RAISE EXCEPTION 'Mixed-bulk generation contribution mass does not match position' USING ERRCODE='23514';
            END IF;
        END IF;
    ELSE
        IF EXISTS(SELECT 1 FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=loc AND lot_id IS NOT NULL) THEN
            RAISE EXCEPTION 'Disabled mixed-bulk location retains a current generation pointer' USING ERRCODE='23514';
        END IF;
    END IF;
    RETURN NULL;
END $$;

CREATE FUNCTION grainledger.ledger_mixed_bulk_pool_row() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    IF NEW.mixed_bulk THEN
        INSERT INTO grainledger.ledger_mixed_bulk_pools(location_id) VALUES(NEW.id) ON CONFLICT(location_id) DO NOTHING;
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION grainledger.ledger_operation_for_actor(a uuid, op uuid) RETURNS jsonb
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT grainledger.ledger_operation_json(x.id)||jsonb_build_object(
        'ownerName',o."Name",'createdBy',x.created_by,'boundCorrectionId',(SELECT c.id FROM grainledger.ledger_corrections c WHERE c.replacement_operation_id=x.id AND c.status IN ('REQUESTED','RECONCILED')),
        'canEdit',coalesce(x.created_by=a AND x.status='DRAFT' AND grainledger.ledger_role(a)='MillOperator' AND grainledger.ledger_assigned(a,x.facility_id),false),
        'canDelete',coalesce(x.created_by=a AND x.status='DRAFT' AND grainledger.ledger_role(a)='MillOperator' AND grainledger.ledger_assigned(a,x.facility_id)
            AND NOT grainledger.ledger_record_in_use('operations',x.id),false))
    FROM grainledger.ledger_operations x JOIN grainledger.identity_organizations o ON o."Id"=x.owner_id
    WHERE x.id=op AND NOT x.is_deleted AND grainledger.ledger_can_read_operation(a,x.id)
$$;

CREATE FUNCTION grainledger.ledger_operation_json(op uuid) RETURNS jsonb
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT operation.content || jsonb_build_object(
        'id',operation.id,'ownerId',operation.owner_id,'facilityId',operation.facility_id,
        'kind',operation.kind,'occurredAt',operation.occurred_at,'equipmentId',operation.equipment_id,
        'status',operation.status,'version',operation.version,
        'confirmedVersion',operation.confirmed_version,'confirmedBy',operation.confirmed_by,
        'varianceKg',CASE WHEN operation.status='DRAFT'
            THEN (inputs.mass-outputs.mass-grainledger.ledger_kg(operation.content->>'declaredLossKg'))::text
            ELSE operation.variance_kg::text END,
        'allowedVarianceKg',CASE WHEN operation.status='DRAFT'
            THEN (floor(inputs.mass*policy.tolerance_percent*10000)/1000000)::text
            ELSE operation.allowed_variance_kg::text END,
        'correctionId',operation.correction_id
    )
    FROM grainledger.ledger_operations operation
    CROSS JOIN LATERAL (
        SELECT sum(grainledger.ledger_kg(value->>'quantityKg')) mass
        FROM jsonb_array_elements(operation.content->'inputs')
    ) inputs
    CROSS JOIN LATERAL (
        SELECT sum(grainledger.ledger_kg(value->>'quantityKg')) mass
        FROM jsonb_array_elements(operation.content->'outputs')
    ) outputs
    LEFT JOIN LATERAL (
        SELECT tolerance_percent FROM grainledger.ledger_policies
        WHERE facility_id=operation.facility_id AND kind=operation.kind
          AND effective_from<=operation.occurred_at
        ORDER BY effective_from DESC LIMIT 1
    ) policy ON true
    WHERE operation.id=op
$$;

CREATE FUNCTION grainledger.ledger_own(a uuid, o uuid) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT EXISTS (
        SELECT 1 FROM grainledger.identity_users WHERE "Id"=a AND "IsActive" AND "OrganizationId"=o
          AND "Role" IN ('CustomerManager','ExportStaff','PurchasingStaff')
    )
$$;

CREATE FUNCTION grainledger.ledger_planning_candidates(a uuid, o uuid, f uuid) RETURNS jsonb
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
            grainledger.ledger_lot_json(a,s.lot_id,false) || jsonb_build_object(
                'locationId',s.location_id,'locationName',s.location_name,
                'locationKind',s.location_kind,'locationCleared',s.cleared,
                'locationMixedBulk',s.mixed_bulk,'locationOwnerId',s.location_owner_id,
                'onHandKg',s.quantity_kg::text,
                'isCurrentPoolAtLocation',COALESCE(s.current_pool_lot_id=s.lot_id,false),
                'positionPoolRevision',CASE WHEN s.current_pool_lot_id=s.lot_id THEN s.pool_revision::text END,
                'catalogCommodityId',mapping.catalog_commodity_id)
            ORDER BY s.lot_id,s.location_id)
            FROM stock s JOIN grainledger.ledger_lots lot ON lot.id=s.lot_id
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

CREATE FUNCTION grainledger.ledger_planning_compatibilities(a uuid, o uuid, f uuid) RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    IF grainledger.ledger_role(a) NOT IN ('ExportStaff','CustomerManager') OR NOT grainledger.ledger_own(a,o) THEN
        RAISE EXCEPTION 'Candidate access denied' USING ERRCODE='42501';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_facilities WHERE id=f) THEN
        RAISE EXCEPTION 'Unknown facility' USING ERRCODE='22023';
    END IF;
    RETURN COALESCE((
        SELECT jsonb_agg(jsonb_build_object(
            'id',c.id,
            'inputProducts',(SELECT jsonb_agg(product ORDER BY product)
                FROM unnest(c.input_products) AS product),
            'outputCatalogCommodityId',mapping.catalog_commodity_id,
            'outputLedgerCommodityId',c.output_commodity_id,
            'effectiveFrom',c.effective_from
        ) ORDER BY c.id)
        FROM grainledger.ledger_compatibilities c
        LEFT JOIN grainledger.ledger_catalog_commodities mapping
            ON mapping.ledger_commodity_id=c.output_commodity_id
        WHERE c.facility_id=f AND c.effective_from<=statement_timestamp()
    ),'[]'::jsonb);
END $$;

CREATE FUNCTION grainledger.ledger_planning_quality_observations(a uuid, o uuid, f uuid, lots uuid[], at_time timestamp with time zone) RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    IF grainledger.ledger_role(a) NOT IN ('ExportStaff','CustomerManager') OR NOT grainledger.ledger_own(a,o) OR
       cardinality(lots) < 1 OR cardinality(lots) > 100 OR
       EXISTS (SELECT 1 FROM unnest(lots) AS requested(lot_id)
               WHERE NOT EXISTS(SELECT 1 FROM grainledger.ledger_lots l WHERE l.id=requested.lot_id AND l.owner_id=o)) THEN
        RAISE EXCEPTION 'Planning quality access denied' USING ERRCODE='42501';
    END IF;
    RETURN coalesce((SELECT jsonb_agg(to_jsonb(current_row) ORDER BY current_row.lot_id,current_row.attribute_id)
       FROM (SELECT DISTINCT ON (q.lot_id,q.attribute_id)
                    q.id,q.lot_id,q.attribute_id,q.version_no,q.measured_value::text,
                    q.unit,q.evidence_id,q.measured_at,q.confirmed_at
             FROM grainledger.ledger_planning_quality_observations q
             WHERE q.owner_id=o AND q.facility_id=f AND q.lot_id=ANY(lots)
                   AND q.confirmed_at<=coalesce(at_time,statement_timestamp())
             ORDER BY q.lot_id,q.attribute_id,q.version_no DESC,q.id) current_row), '[]'::jsonb);
END $$;

CREATE FUNCTION grainledger.ledger_planning_yield_rule(a uuid, o uuid, f uuid, c uuid, at_time timestamp with time zone) RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE r grainledger.ledger_planning_yield_rules;
BEGIN
    IF grainledger.ledger_role(a) NOT IN ('ExportStaff','CustomerManager') OR NOT grainledger.ledger_own(a,o) THEN
        RAISE EXCEPTION 'Planning yield access denied' USING ERRCODE='42501';
    END IF;
    SELECT * INTO r FROM grainledger.ledger_planning_yield_rules
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

CREATE FUNCTION grainledger.ledger_pool_ingress(a uuid, command_id uuid, command_kind text, loc uuid, incoming_lot uuid, quantity numeric) RETURNS uuid
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $_$
<<pool_ingress_block>>
DECLARE
    destination grainledger.ledger_locations; incoming grainledger.ledger_lots; old_pool grainledger.ledger_mixed_bulk_pools; current_lot grainledger.ledger_lots;
    policy uuid; prior numeric:=0; incoming_position numeric; stock_total numeric;
    event_id uuid:=gen_random_uuid(); output_id uuid:=gen_random_uuid(); output_code text; output_origin uuid;
    req jsonb; result_id uuid;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    IF quantity<=0 OR quantity<>round(quantity,6) OR command_id IS NULL OR command_kind IS NULL THEN
        RAISE EXCEPTION 'Positive exact custody ingress quantity and command identity required' USING ERRCODE='22023';
    END IF;
    SELECT * INTO STRICT destination FROM grainledger.ledger_locations WHERE id=loc FOR UPDATE;
    IF NOT destination.mixed_bulk THEN RETURN incoming_lot; END IF;
    SELECT * INTO incoming FROM grainledger.ledger_lots WHERE id=incoming_lot FOR UPDATE;
    req:=jsonb_build_object('actorId',a,'commandId',command_id,'commandKind',command_kind,'locationId',loc,
        'incomingLotId',incoming_lot,'quantityKg',quantity::text);
    SELECT ce.output_lot_id INTO result_id FROM grainledger.ledger_custody_events ce
    WHERE ce.command_id=$2 AND ce.command_kind=$3 AND ce.action='INGRESS'
      AND ce.location_id=$4 AND ce.input_lot_id IS NOT DISTINCT FROM $5;
    IF FOUND THEN
        IF (SELECT ce.request FROM grainledger.ledger_custody_events ce WHERE ce.command_id=$2
            AND ce.command_kind=$3 AND ce.action='INGRESS' AND ce.location_id=$4
            AND ce.input_lot_id IS NOT DISTINCT FROM $5) IS DISTINCT FROM req THEN
            RAISE EXCEPTION 'Custody command retry conflicts with original payload' USING ERRCODE='23505';
        END IF;
        RETURN result_id;
    END IF;
    IF incoming.id IS NULL THEN RAISE EXCEPTION 'Incoming custody lot unavailable' USING ERRCODE='23503'; END IF;
    IF NOT grainledger.ledger_assigned(a,destination.facility_id) OR grainledger.ledger_role(a) NOT IN ('MillOperator','MillManager') THEN
        RAISE EXCEPTION 'Mixed-bulk custody authority denied' USING ERRCODE='42501';
    END IF;
    IF incoming.owner_id IS DISTINCT FROM destination.owner_id AND destination.owner_id IS NOT NULL THEN
        RAISE EXCEPTION 'Cross-owner mixed-bulk ingress denied' USING ERRCODE='23514';
    END IF;
    incoming_position:=COALESCE((SELECT quantity_kg FROM grainledger.ledger_positions WHERE lot_id=incoming_lot AND location_id=loc),0);
    IF incoming_position<quantity THEN RAISE EXCEPTION 'Selected ingress position has insufficient stock' USING ERRCODE='23514'; END IF;
    SELECT COALESCE(sum(quantity_kg),0) INTO stock_total FROM grainledger.ledger_positions WHERE lot_id=incoming_lot AND quantity_kg>0;
    IF (SELECT count(*) FROM grainledger.ledger_contributions WHERE lot_id=incoming_lot)=0 THEN
        RAISE EXCEPTION 'Incoming lot lacks source attribution' USING ERRCODE='23514';
    END IF;
    IF (SELECT count(*) FROM grainledger.ledger_contributions WHERE lot_id=incoming_lot)>1
       AND (quantity<stock_total OR (SELECT sum(quantity_kg) FROM grainledger.ledger_contributions WHERE lot_id=incoming_lot) IS DISTINCT FROM stock_total) THEN
        RAISE EXCEPTION 'Multi-source lot position attribution is ambiguous for partial mixed-bulk ingress' USING ERRCODE='23514';
    END IF;
    IF (SELECT count(*) FROM grainledger.ledger_contributions WHERE lot_id=incoming_lot)=1
       AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_contributions WHERE lot_id=incoming_lot AND quantity_kg>0) THEN
        RAISE EXCEPTION 'Incoming lot lacks positive source attribution' USING ERRCODE='23514';
    END IF;
    IF grainledger.ledger_blocked(incoming_lot) THEN RAISE EXCEPTION 'Held or restricted stock cannot enter mixed-bulk pool' USING ERRCODE='23514'; END IF;
    policy:=grainledger.ledger_mixed_bulk_eligible(loc,incoming.owner_id,incoming.commodity_id,incoming.stage,incoming.product_identity,clock_timestamp());
    IF policy IS NULL THEN RAISE EXCEPTION 'No active evidence-backed mixed-bulk identity policy' USING ERRCODE='23514'; END IF;
    SELECT * INTO old_pool FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=loc FOR UPDATE;
    IF old_pool.location_id IS NULL THEN
        INSERT INTO grainledger.ledger_mixed_bulk_pools(location_id) VALUES(loc) ON CONFLICT DO NOTHING;
        SELECT * INTO STRICT old_pool FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=loc FOR UPDATE;
    END IF;
    IF old_pool.lot_id IS NOT NULL AND old_pool.lot_id=incoming_lot THEN
        RAISE EXCEPTION 'Current pool generation cannot be re-ingressed as an input lot' USING ERRCODE='23514';
    END IF;
    IF old_pool.lot_id IS NOT NULL THEN
        SELECT * INTO STRICT current_lot FROM grainledger.ledger_lots WHERE id=old_pool.lot_id FOR UPDATE;
        SELECT COALESCE(quantity_kg,0) INTO prior FROM grainledger.ledger_positions WHERE lot_id=old_pool.lot_id AND location_id=loc;
        IF prior<=0 OR EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE location_id=loc AND quantity_kg>0
            AND lot_id<>old_pool.lot_id AND lot_id<>incoming_lot) THEN
            RAISE EXCEPTION 'Mixed-bulk current generation pointer is inconsistent' USING ERRCODE='23514';
        END IF;
        IF current_lot.owner_id<>incoming.owner_id OR current_lot.commodity_id<>incoming.commodity_id
           OR current_lot.stage<>incoming.stage OR current_lot.product_identity<>incoming.product_identity
           OR grainledger.ledger_mixed_bulk_eligible(loc,current_lot.owner_id,current_lot.commodity_id,current_lot.stage,current_lot.product_identity,clock_timestamp()) IS DISTINCT FROM policy THEN
            RAISE EXCEPTION 'Mixed-bulk pool identity or approved policy mismatch' USING ERRCODE='23514';
        END IF;
        IF grainledger.ledger_blocked(current_lot.id) THEN RAISE EXCEPTION 'Held or restricted pool contents cannot be topped up' USING ERRCODE='23514'; END IF;
    ELSIF EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE location_id=loc AND quantity_kg>0 AND lot_id<>incoming_lot) THEN
        RAISE EXCEPTION 'Mixed-bulk location has unrelated positive stock' USING ERRCODE='23514';
    END IF;
    IF old_pool.owner_id IS NOT NULL AND (
        old_pool.owner_id IS DISTINCT FROM incoming.owner_id
        OR old_pool.commodity_id IS DISTINCT FROM incoming.commodity_id
        OR old_pool.stage IS DISTINCT FROM incoming.stage
        OR old_pool.product_identity IS DISTINCT FROM incoming.product_identity
    ) AND NOT destination.cleared THEN
        RAISE EXCEPTION 'Drained mixed-bulk identity requires recorded clearance before replacement' USING ERRCODE='23514';
    END IF;
    PERFORM grainledger.ledger_credit_location(loc,incoming.owner_id);
    output_code:=grainledger.ledger_reserve_lot_code_internal(output_id,incoming.owner_id,'CUSTODY',command_id,incoming.stage,incoming.product_identity);
    output_origin:=grainledger.ledger_register_code(output_code,output_id);
    INSERT INTO grainledger.ledger_lots(id,code,owner_id,commodity_id,stage,product_identity,code_origin_id)
    VALUES(output_id,output_code,incoming.owner_id,incoming.commodity_id,incoming.stage,incoming.product_identity,output_origin);
    INSERT INTO grainledger.ledger_events(id,kind,actor_id,lot_id,details) VALUES(event_id,'POOL_INGRESS',a,output_id,
        jsonb_build_object('commandId',command_id,'commandKind',command_kind,'locationId',loc,
            'incomingLotId',incoming_lot,'quantityKg',quantity::text,'priorPoolLotId',old_pool.lot_id));
    INSERT INTO grainledger.ledger_custody_events(event_id,command_id,command_kind,action,actor_id,location_id,input_lot_id,
        output_lot_id,policy_id,revision,quantity_kg,request)
    VALUES(event_id,command_id,command_kind,'INGRESS',a,loc,incoming_lot,output_id,policy,old_pool.revision+1,quantity,req);
    IF old_pool.lot_id IS NOT NULL THEN
        PERFORM grainledger.ledger_post(event_id,old_pool.lot_id,loc,-prior);
        INSERT INTO grainledger.ledger_custody_edges VALUES(event_id,old_pool.lot_id,output_id,prior);
        INSERT INTO grainledger.ledger_custody_allocations
        SELECT event_id,old_pool.lot_id,output_id,source_lot_id,quantity_kg
        FROM grainledger.ledger_contributions WHERE lot_id=old_pool.lot_id AND quantity_kg>0;
    END IF;
    PERFORM grainledger.ledger_post(event_id,incoming_lot,loc,-quantity);
    INSERT INTO grainledger.ledger_custody_edges VALUES(event_id,incoming_lot,output_id,quantity);
    IF (SELECT count(*) FROM grainledger.ledger_contributions WHERE lot_id=incoming_lot)=1 THEN
        INSERT INTO grainledger.ledger_custody_allocations(event_id,input_lot_id,output_lot_id,source_lot_id,quantity_kg)
        SELECT event_id,incoming_lot,output_id,source_lot_id,quantity FROM grainledger.ledger_contributions WHERE lot_id=incoming_lot;
    ELSE
        INSERT INTO grainledger.ledger_custody_allocations(event_id,input_lot_id,output_lot_id,source_lot_id,quantity_kg)
        SELECT event_id,incoming_lot,output_id,source_lot_id,quantity_kg FROM grainledger.ledger_contributions WHERE lot_id=incoming_lot AND quantity_kg>0;
    END IF;
    INSERT INTO grainledger.ledger_contributions(lot_id,source_lot_id,quantity_kg)
    SELECT output_id,source_lot_id,sum(quantity_kg)
    FROM grainledger.ledger_custody_allocations
    WHERE grainledger.ledger_custody_allocations.event_id=pool_ingress_block.event_id
    GROUP BY source_lot_id HAVING sum(quantity_kg)>0;
    PERFORM grainledger.ledger_post(event_id,output_id,loc,prior+quantity);
    UPDATE grainledger.ledger_mixed_bulk_pools SET lot_id=output_id,revision=revision+1,
        owner_id=incoming.owner_id,commodity_id=incoming.commodity_id,stage=incoming.stage,
        product_identity=incoming.product_identity WHERE location_id=loc;
    UPDATE grainledger.ledger_locations SET cleared=false WHERE id=loc AND cleared;
    IF grainledger.ledger_blocked(output_id) THEN
        RAISE EXCEPTION 'Held or restricted stock cannot enter mixed-bulk pool' USING ERRCODE='23514';
    END IF;
    RETURN output_id;
END $_$;

CREATE FUNCTION grainledger.ledger_pool_withdraw(a uuid, command_id uuid, command_kind text, lot uuid, loc uuid, quantity numeric, expected_revision bigint DEFAULT NULL::bigint) RETURNS uuid
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $_$
DECLARE
    destination grainledger.ledger_locations; source grainledger.ledger_lots; pool grainledger.ledger_mixed_bulk_pools; policy uuid;
    source_position numeric; source_total numeric; event_id uuid:=gen_random_uuid(); aliquot_id uuid:=gen_random_uuid();
    remainder_id uuid:=gen_random_uuid(); actual_id uuid; aliquot_code text; remainder_code text;
    aliquot_origin uuid; remainder_origin uuid; req jsonb; result_id uuid; revision_value bigint:=0;
    output_quantity numeric; source_count integer; force_extract boolean;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    IF quantity<=0 OR quantity<>round(quantity,6) OR command_id IS NULL OR command_kind IS NULL THEN
        RAISE EXCEPTION 'Positive exact custody withdrawal quantity and command identity required' USING ERRCODE='22023';
    END IF;
    SELECT * INTO STRICT destination FROM grainledger.ledger_locations WHERE id=loc FOR UPDATE;
    SELECT * INTO STRICT source FROM grainledger.ledger_lots WHERE id=lot FOR UPDATE;
    SELECT COALESCE(quantity_kg,0) INTO source_position FROM grainledger.ledger_positions WHERE lot_id=lot AND location_id=loc;
    force_extract:=command_kind='TRANSFER_TO_MIXED';
    IF NOT destination.mixed_bulk AND NOT force_extract
       AND NOT grainledger.ledger_mixed_bulk_custody_derived(lot) THEN RETURN lot; END IF;
    req:=jsonb_build_object('actorId',a,'commandId',command_id,'commandKind',command_kind,'lotId',lot,
        'locationId',loc,'quantityKg',quantity::text,'expectedRevision',expected_revision);
    SELECT ce.output_lot_id INTO result_id FROM grainledger.ledger_custody_events ce
    WHERE ce.command_id=$2 AND ce.command_kind=$3 AND ce.action='WITHDRAW'
      AND ce.location_id=$5 AND ce.input_lot_id=$4;
    IF FOUND THEN
        IF (SELECT ce.request FROM grainledger.ledger_custody_events ce WHERE ce.command_id=$2
            AND ce.command_kind=$3 AND ce.action='WITHDRAW' AND ce.location_id=$5 AND ce.input_lot_id=$4)
            IS DISTINCT FROM req THEN RAISE EXCEPTION 'Custody command retry conflicts with original payload' USING ERRCODE='23505'; END IF;
        RETURN result_id;
    END IF;
    IF NOT grainledger.ledger_assigned(a,destination.facility_id) OR grainledger.ledger_role(a) NOT IN ('MillOperator','MillManager') THEN
        RAISE EXCEPTION 'Mixed-bulk custody authority denied' USING ERRCODE='42501';
    END IF;
    IF source_position<quantity THEN RAISE EXCEPTION 'Selected stock position has insufficient quantity' USING ERRCODE='23514'; END IF;
    IF grainledger.ledger_receiving_restricted(lot) OR EXISTS(SELECT 1 FROM grainledger.ledger_receipt_correction_blocks b JOIN grainledger.ledger_lots x ON x.receipt_id=b.receipt_id WHERE x.id=lot)
       OR EXISTS(SELECT 1 FROM grainledger.ledger_receipt_reversals r JOIN grainledger.ledger_lots x ON x.receipt_id=r.receipt_id WHERE x.id=lot)
       OR EXISTS(SELECT 1 FROM grainledger.ledger_corrections c WHERE c.status IN ('REQUESTED','RECONCILED')
          AND EXISTS(SELECT 1 FROM grainledger.ledger_lots x WHERE x.operation_id=c.operation_id AND x.id=lot)) THEN
        RAISE EXCEPTION 'Restricted or correction-bound stock cannot be withdrawn' USING ERRCODE='23514';
    END IF;
    SELECT COALESCE(sum(quantity_kg),0) INTO source_total FROM grainledger.ledger_positions WHERE lot_id=lot AND quantity_kg>0;
    SELECT count(*) INTO source_count FROM grainledger.ledger_contributions WHERE lot_id=lot AND quantity_kg>0;
    IF source_count=0 OR (source_count>1 AND (
        (SELECT sum(quantity_kg) FROM grainledger.ledger_contributions WHERE lot_id=lot) IS DISTINCT FROM source_total
        OR source_position<>source_total
    )) THEN
        RAISE EXCEPTION 'Multi-source stock withdrawal attribution is ambiguous' USING ERRCODE='23514';
    END IF;
    IF destination.mixed_bulk THEN
        SELECT * INTO STRICT pool FROM grainledger.ledger_mixed_bulk_pools WHERE location_id=loc FOR UPDATE;
        IF pool.lot_id IS DISTINCT FROM lot THEN RAISE EXCEPTION 'Selected pool generation is stale' USING ERRCODE='40001'; END IF;
        IF expected_revision IS NULL OR expected_revision<>pool.revision THEN
            RAISE EXCEPTION 'Selected pool revision is stale' USING ERRCODE='40001';
        END IF;
        revision_value:=pool.revision;
        policy:=grainledger.ledger_mixed_bulk_eligible(loc,source.owner_id,source.commodity_id,source.stage,source.product_identity,clock_timestamp());
        IF policy IS NULL THEN RAISE EXCEPTION 'No active evidence-backed mixed-bulk identity policy' USING ERRCODE='23514'; END IF;
        IF source_position<>source_total OR EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE location_id=loc AND quantity_kg>0 AND lot_id<>lot) THEN
            RAISE EXCEPTION 'Mixed-bulk generation position is not a complete physical balance' USING ERRCODE='23514';
        END IF;
    END IF;
    output_quantity:=source_position-quantity;
    actual_id:=aliquot_id;
    INSERT INTO grainledger.ledger_events(id,kind,actor_id,lot_id,details) VALUES(event_id,'POOL_WITHDRAW',a,lot,
        jsonb_build_object('commandId',command_id,'commandKind',command_kind,'locationId',loc,
            'quantityKg',quantity::text,'poolRevision',revision_value,'expectedRevision',expected_revision));
    INSERT INTO grainledger.ledger_custody_events(event_id,command_id,command_kind,action,actor_id,location_id,input_lot_id,
        output_lot_id,policy_id,revision,quantity_kg,request)
    VALUES(event_id,command_id,command_kind,'WITHDRAW',a,loc,lot,aliquot_id,policy,revision_value+1,quantity,req);
    aliquot_code:=grainledger.ledger_reserve_lot_code_internal(aliquot_id,source.owner_id,'CUSTODY',command_id,source.stage,source.product_identity);
    aliquot_origin:=grainledger.ledger_register_code(aliquot_code,aliquot_id);
    INSERT INTO grainledger.ledger_lots(id,code,owner_id,commodity_id,stage,product_identity,code_origin_id)
    VALUES(aliquot_id,aliquot_code,source.owner_id,source.commodity_id,source.stage,source.product_identity,aliquot_origin);
    IF output_quantity>0 THEN
        remainder_code:=grainledger.ledger_reserve_lot_code_internal(remainder_id,source.owner_id,'CUSTODY',command_id,source.stage,source.product_identity);
        remainder_origin:=grainledger.ledger_register_code(remainder_code,remainder_id);
        INSERT INTO grainledger.ledger_lots(id,code,owner_id,commodity_id,stage,product_identity,code_origin_id)
        VALUES(remainder_id,remainder_code,source.owner_id,source.commodity_id,source.stage,source.product_identity,remainder_origin);
    END IF;
    PERFORM grainledger.ledger_post(event_id,lot,loc,-source_position);
    PERFORM grainledger.ledger_post(event_id,aliquot_id,loc,quantity);
    INSERT INTO grainledger.ledger_custody_edges VALUES(event_id,lot,aliquot_id,quantity);
    IF output_quantity>0 THEN
        PERFORM grainledger.ledger_post(event_id,remainder_id,loc,output_quantity);
        INSERT INTO grainledger.ledger_custody_edges VALUES(event_id,lot,remainder_id,output_quantity);
    END IF;
    -- Integer micro-kg largest remainder allocation; remainder is exact source-wise complement.
    WITH source_weights AS (
        SELECT source_lot_id,
               (CASE WHEN source_count=1 THEN source_position ELSE quantity_kg END*1000000)::bigint mass
        FROM grainledger.ledger_contributions WHERE lot_id=lot AND quantity_kg>0
    ), bases AS (
        SELECT source_lot_id,div(quantity*1000000*mass,sum(mass) OVER()) base,
               mod(quantity*1000000*mass,sum(mass) OVER()) rem FROM source_weights
    ), ranked AS (
        SELECT *,row_number() OVER(ORDER BY rem DESC,source_lot_id) rn,
               quantity*1000000-sum(base) OVER() left_units FROM bases
    ), shares AS (
        SELECT source_lot_id,base+CASE WHEN rn<=left_units THEN 1 ELSE 0 END units FROM ranked
    )
    INSERT INTO grainledger.ledger_contributions(lot_id,source_lot_id,quantity_kg)
    SELECT aliquot_id,source_lot_id,units/1000000::numeric FROM shares WHERE units>0;
    INSERT INTO grainledger.ledger_custody_allocations(event_id,input_lot_id,output_lot_id,source_lot_id,quantity_kg)
    SELECT event_id,lot,aliquot_id,source_lot_id,quantity_kg FROM grainledger.ledger_contributions WHERE lot_id=aliquot_id;
    IF output_quantity>0 THEN
        INSERT INTO grainledger.ledger_contributions(lot_id,source_lot_id,quantity_kg)
        SELECT remainder_id,prior.source_lot_id,
               CASE WHEN source_count=1 THEN source_position ELSE prior.quantity_kg END-COALESCE(aliquot.quantity_kg,0)
        FROM grainledger.ledger_contributions prior LEFT JOIN grainledger.ledger_contributions aliquot
          ON aliquot.lot_id=aliquot_id AND aliquot.source_lot_id=prior.source_lot_id
        WHERE prior.lot_id=lot AND
          (CASE WHEN source_count=1 THEN source_position ELSE prior.quantity_kg END)>COALESCE(aliquot.quantity_kg,0);
        INSERT INTO grainledger.ledger_custody_allocations(event_id,input_lot_id,output_lot_id,source_lot_id,quantity_kg)
        SELECT event_id,lot,remainder_id,source_lot_id,quantity_kg FROM grainledger.ledger_contributions WHERE lot_id=remainder_id;
    END IF;
    IF destination.mixed_bulk THEN
        UPDATE grainledger.ledger_mixed_bulk_pools SET lot_id=CASE WHEN output_quantity>0 THEN remainder_id ELSE NULL END,
            revision=revision+1 WHERE location_id=loc;
    END IF;
    RETURN actual_id;
END $_$;

CREATE FUNCTION grainledger.ledger_post(e uuid, l uuid, loc uuid, q numeric, rev uuid DEFAULT NULL::uuid) RETURNS void
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    PERFORM grainledger.ledger_require_active_location(loc);
    INSERT INTO grainledger.ledger_postings(event_id,lot_id,location_id,quantity_kg,reverses_posting_id)
    VALUES(e,l,loc,q,rev);
    UPDATE grainledger.ledger_positions SET quantity_kg=quantity_kg+q WHERE lot_id=l AND location_id=loc;
    IF NOT FOUND THEN INSERT INTO grainledger.ledger_positions VALUES(l,loc,q); END IF;
END $$;

CREATE FUNCTION grainledger.ledger_procurement_package_complete(r uuid) RETURNS boolean
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE h grainledger.capture_procurements; ev grainledger.capture_business_events; snapshot jsonb; state grainledger.ledger_receiving; row record; item jsonb; loc uuid; commodity uuid; n integer; actual numeric; applied grainledger.ledger_arrival_measurement_sets;
BEGIN
    SELECT * INTO h FROM grainledger.capture_procurements WHERE id=r;
    IF NOT FOUND THEN RETURN false; END IF;
    IF h.receiving_workflow IS NULL THEN RETURN grainledger.ledger_legacy_procurement_package_complete(r); END IF;
    SELECT * INTO ev FROM grainledger.capture_business_events WHERE id=h.event_id;
    SELECT * INTO state FROM grainledger.ledger_receiving WHERE procurement_id=r;
    IF ev.id IS NULL OR state.procurement_id IS NULL OR h.status<>'COMMITTED' OR ev.event_type<>'PROCUREMENT_COMMITTED'
       OR ev.company_id IS DISTINCT FROM h.company_id OR ev.actor_id IS DISTINCT FROM h.received_by OR ev.occurred_at<>h.received_at THEN RETURN false; END IF;
    snapshot:=ev.payload->'result'; loc:=(snapshot->>'ReceivingLocationId')::uuid; commodity:=(snapshot->>'CommodityId')::uuid;
    SELECT count(*) INTO n FROM grainledger.capture_procurement_items WHERE procurement_id=r;
    IF snapshot IS NULL OR n NOT BETWEEN 1 AND 100 OR jsonb_typeof(snapshot->'Items') IS DISTINCT FROM 'array'
       OR jsonb_array_length(snapshot->'Items')<>n OR (SELECT count(DISTINCT (x->>'Id')::uuid) FROM jsonb_array_elements(snapshot->'Items') x)<>n
       OR (snapshot->>'Id')::uuid IS DISTINCT FROM h.id OR snapshot->>'Code' IS DISTINCT FROM h.code
       OR (snapshot->>'FarmerId')::uuid IS DISTINCT FROM h.farmer_id OR (snapshot->>'CollectionSiteId')::uuid IS DISTINCT FROM h.collection_site_id
       OR (snapshot->>'ReceivedBy')::uuid IS DISTINCT FROM h.received_by OR (snapshot->>'EventId')::uuid IS DISTINCT FROM ev.id
       OR (snapshot->>'ReceivedAt')::timestamptz IS DISTINCT FROM h.received_at OR (snapshot->>'RecordedAt')::timestamptz IS DISTINCT FROM ev.recorded_at
       OR snapshot->>'Status' IS DISTINCT FROM 'PENDING_ARRIVAL' OR (snapshot->>'ReceivingVersion')::integer IS DISTINCT FROM 0
       OR (snapshot->>'TotalNetMassKg')::numeric IS DISTINCT FROM (SELECT sum(net_mass_kg) FROM grainledger.capture_procurement_items WHERE procurement_id=r)
       OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_commodities WHERE id=commodity AND code='PADDY')
       OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_locations WHERE id=loc AND facility_id=h.collection_site_id)
       OR NOT EXISTS(SELECT 1 FROM grainledger.capture_offline_operations o WHERE o.applied_event_id=ev.id AND o.company_id=h.company_id
           AND o.submitted_by=h.received_by AND o.status='APPLIED' AND o.client_operation_id=ev.correlation_id AND o.result_payload=snapshot
           AND o.captured_at=(snapshot->>'CapturedAt')::timestamptz AND o.payload->>'operationType'='PROCUREMENT_COMMITTED') THEN RETURN false; END IF;
    IF (SELECT count(*) FROM grainledger.ledger_lots WHERE receipt_id=r)<>(CASE WHEN state.accepted_measurement_set_id IS NULL THEN 0 ELSE n END) THEN RETURN false; END IF;
    IF state.latest_measurement_set_id IS NOT NULL AND (SELECT count(*) FROM grainledger.ledger_arrival_measurements WHERE measurement_set_id=state.latest_measurement_set_id)<>n THEN RETURN false; END IF;
    IF state.accepted_measurement_set_id IS NOT NULL AND state.accepted_measurement_set_id IS DISTINCT FROM state.latest_measurement_set_id THEN RETURN false; END IF;
    IF (state.status='PENDING_ARRIVAL') IS DISTINCT FROM (state.version=0 AND state.latest_measurement_set_id IS NULL)
       OR EXISTS(SELECT 1 FROM grainledger.ledger_arrival_measurement_sets ms WHERE ms.procurement_id=r AND
           ((SELECT count(*) FROM grainledger.ledger_arrival_measurements WHERE measurement_set_id=ms.id)<>n
            OR (ms.threshold_revision IS NOT NULL AND (ms.threshold_company_id IS DISTINCT FROM h.company_id
                OR grainledger.ledger_receiving_threshold_snapshot_valid(ms.threshold_company_id,ms.threshold_percentage,ms.threshold_source,ms.threshold_revision) IS DISTINCT FROM true))))
       OR EXISTS(SELECT 1 FROM grainledger.ledger_arrival_measurements m JOIN grainledger.ledger_arrival_measurement_sets ms ON ms.id=m.measurement_set_id
           JOIN grainledger.capture_procurement_items i ON i.id=m.procurement_item_id WHERE ms.procurement_id=r AND i.procurement_id<>r)
       OR EXISTS(SELECT 1 FROM grainledger.ledger_arrival_reviews v JOIN grainledger.ledger_evidence e ON e.id=v.evidence_id
           WHERE v.procurement_id=r AND (e.owner_id<>h.company_id OR e.facility_id<>h.collection_site_id)) THEN RETURN false; END IF;
    SELECT * INTO applied FROM grainledger.ledger_arrival_measurement_sets WHERE id=state.accepted_measurement_set_id;
    IF state.status='RECEIVED_WITHIN_TOLERANCE' AND
       (applied.threshold_revision IS NULL OR applied.threshold_company_id IS DISTINCT FROM h.company_id
        OR grainledger.ledger_receiving_threshold_snapshot_valid(applied.threshold_company_id,applied.threshold_percentage,applied.threshold_source,applied.threshold_revision) IS DISTINCT FROM true
        OR state.accepted_review_id IS NOT NULL
        OR EXISTS(SELECT 1 FROM grainledger.ledger_arrival_reviews WHERE measurement_set_id=state.accepted_measurement_set_id AND action='APPROVE_SHORTAGE')
        OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_arrival_measurements m JOIN grainledger.capture_procurement_items i ON i.id=m.procurement_item_id
            WHERE m.measurement_set_id=state.accepted_measurement_set_id AND m.actual_kg<i.net_mass_kg)) THEN RETURN false; END IF;
    FOR row IN SELECT i.*,sl.id source_id,sl.lot_id,ps.plot_id,ps.season_id,ps.rice_variety,p.farmer_id
        FROM grainledger.capture_procurement_items i LEFT JOIN grainledger.capture_source_lots sl ON sl.procurement_item_id=i.id AND sl.company_id=i.company_id
        JOIN grainledger.capture_plot_seasons ps ON ps.id=i.plot_season_id AND ps.company_id=i.company_id
        JOIN grainledger.capture_plots p ON p.id=ps.plot_id AND p.company_id=ps.company_id
        JOIN grainledger.capture_crop_seasons s ON s.id=ps.season_id AND s.company_id=ps.company_id WHERE i.procurement_id=r
    LOOP
        SELECT x INTO item FROM jsonb_array_elements(snapshot->'Items') x WHERE (x->>'Id')::uuid=row.id;
        IF row.company_id<>h.company_id OR row.farmer_id<>h.farmer_id OR row.source_id IS NULL OR row.line_no NOT BETWEEN 1 AND n
           OR (item->>'LineNo')::integer IS DISTINCT FROM row.line_no OR (item->>'PlotSeasonId')::uuid IS DISTINCT FROM row.plot_season_id
           OR (item->>'PlotId')::uuid IS DISTINCT FROM row.plot_id OR (item->>'SeasonId')::uuid IS DISTINCT FROM row.season_id
           OR item->>'RiceVariety' IS DISTINCT FROM row.rice_variety OR (item->>'SourceLotId')::uuid IS DISTINCT FROM row.source_id
           OR (item->>'LotId')::uuid IS DISTINCT FROM row.lot_id OR item->>'LotCode' IS DISTINCT FROM grainledger.ledger_source_lot_code(r,row.lot_id)
           OR item->>'Stage' IS DISTINCT FROM 'RAW_PADDY' OR (item->>'MeasuredValue')::numeric IS DISTINCT FROM row.measured_value
           OR item->>'MeasuredUnit' IS DISTINCT FROM row.measured_unit OR (item->>'NetMassKg')::numeric IS DISTINCT FROM row.net_mass_kg
           OR (item->>'MoisturePercent')::numeric IS DISTINCT FROM row.moisture_percent OR (item->'InitialStock'->>'LocationId')::uuid IS DISTINCT FROM loc
           OR (item->'InitialStock'->>'ReceivedKg')::numeric IS DISTINCT FROM 0
           OR row.net_mass_kg IS DISTINCT FROM (CASE row.measured_unit WHEN 'KG' THEN row.measured_value WHEN 'G' THEN row.measured_value/1000 WHEN 'T' THEN row.measured_value*1000 END)
           OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_measurement_units WHERE code=row.measured_unit AND quantity_kind='MASS') THEN RETURN false; END IF;
        IF state.accepted_measurement_set_id IS NULL THEN
            IF EXISTS(SELECT 1 FROM grainledger.ledger_lots WHERE id=row.lot_id) THEN RETURN false; END IF;
        ELSE
            SELECT actual_kg INTO actual FROM grainledger.ledger_arrival_measurements WHERE measurement_set_id=state.accepted_measurement_set_id AND procurement_item_id=row.id;
            IF actual IS NULL OR actual<=0 OR (state.status='RECEIVED_MATCHED' AND actual<>row.net_mass_kg)
               OR (state.status='RECEIVED_SHORTAGE_APPROVED' AND actual>row.net_mass_kg)
               OR (state.status='RECEIVED_WITHIN_TOLERANCE' AND (actual>row.net_mass_kg OR (row.net_mass_kg-actual)*100>row.net_mass_kg*applied.threshold_percentage))
               OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_lots l JOIN grainledger.ledger_catalog_commodities m ON m.ledger_commodity_id=l.commodity_id
                    WHERE l.id=row.lot_id AND l.owner_id=h.company_id AND l.receipt_id=r AND l.receipt_line_id=row.id
                    AND l.code=item->>'LotCode' AND l.stage='RAW_PADDY' AND l.product_identity='RAW_PADDY' AND m.catalog_commodity_id=commodity)
               OR (SELECT count(*) FROM grainledger.ledger_events WHERE lot_id=row.lot_id AND kind='RECEIPT')<>1
               OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_events e JOIN grainledger.ledger_postings p ON p.event_id=e.id JOIN grainledger.ledger_arrival_measurement_sets ms ON ms.id=state.accepted_measurement_set_id
                    JOIN grainledger.ledger_arrival_measurements am ON am.measurement_set_id=ms.id AND am.procurement_item_id=row.id
                    LEFT JOIN grainledger.ledger_arrival_reviews rv ON rv.id=state.accepted_review_id
                    WHERE e.lot_id=row.lot_id AND e.kind='RECEIPT' AND e.actor_id=ms.operator_id AND e.occurred_at=ms.captured_at
                    AND e.details->>'measurementSetId'=state.accepted_measurement_set_id::text AND e.details->>'receiptId'=r::text
                    AND e.details->>'receiptLineId'=row.id::text AND e.details->>'sourceLotId'=row.source_id::text
                    AND (e.details->>'quantityKg')::numeric=actual AND (e.details->>'sourceQuantityKg')::numeric=row.net_mass_kg
                    AND e.details->>'captureEventId'=ev.id::text AND e.details->>'catalogCommodityId'=commodity::text
                    AND e.details->>'farmerId'=h.farmer_id::text AND e.details->>'plotSeasonId'=row.plot_season_id::text
                    AND e.details->>'plotId'=row.plot_id::text AND e.details->>'seasonId'=row.season_id::text
                    AND e.details->>'riceVariety'=row.rice_variety AND (e.details->>'originalQuantity')::numeric=row.measured_value
                    AND e.details->>'originalUnit'=row.measured_unit AND (e.details->>'moisturePercent')::numeric=row.moisture_percent
                    AND (e.details->>'arrivalQuantity')::numeric=am.measured_value AND e.details->>'arrivalUnit'=am.measured_unit
                    AND (e.details->>'managerReviewId')::uuid IS NOT DISTINCT FROM state.accepted_review_id
                    AND (e.details->>'managerId')::uuid IS NOT DISTINCT FROM rv.manager_id
                    AND (e.details->>'evidenceId')::uuid IS NOT DISTINCT FROM rv.evidence_id
                    AND e.details->>'receivingStatus'=state.status
                    AND p.lot_id=row.lot_id AND p.location_id=loc AND p.quantity_kg=actual AND p.reverses_posting_id IS NULL
                    AND (SELECT count(*) FROM grainledger.ledger_postings WHERE event_id=e.id)=1)
               OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_contributions WHERE lot_id=row.lot_id AND source_lot_id=row.lot_id AND quantity_kg=actual)
               OR (SELECT count(*) FROM grainledger.ledger_contributions WHERE lot_id=row.lot_id)<>1 THEN RETURN false; END IF;
        END IF;
    END LOOP;
    IF state.status='RECEIVED_SHORTAGE_APPROVED' AND (NOT EXISTS(SELECT 1 FROM grainledger.ledger_arrival_measurements m JOIN grainledger.capture_procurement_items i ON i.id=m.procurement_item_id WHERE m.measurement_set_id=state.accepted_measurement_set_id AND m.actual_kg<i.net_mass_kg)
       OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_arrival_reviews v JOIN grainledger.ledger_evidence e ON e.id=v.evidence_id WHERE v.id=state.accepted_review_id AND v.action='APPROVE_SHORTAGE' AND v.measurement_set_id=state.accepted_measurement_set_id AND e.owner_id=h.company_id AND e.facility_id=h.collection_site_id)) THEN RETURN false; END IF;
    RETURN true;
END $$;

CREATE FUNCTION grainledger.ledger_procurement_source_lineage(o uuid, source_id uuid) RETURNS TABLE(lot_id uuid, lot_code text, location_id uuid, is_current_pool boolean, pool_revision bigint, quantity_kg numeric)
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM grainledger.capture_source_lots s
        JOIN grainledger.ledger_lots root ON root.id=s.lot_id AND root.owner_id=s.company_id
        WHERE s.company_id=o AND s.lot_id=source_id
    ) AND NOT EXISTS (
        SELECT 1 FROM grainledger.ledger_positions p WHERE p.lot_id=source_id
    ) AND NOT EXISTS (
        SELECT 1 FROM grainledger.ledger_procurement_stock_bulk(o,ARRAY[source_id]) stock WHERE stock.on_hand_kg>0
    ) THEN
        RAISE EXCEPTION 'Accepted source lot has no stock position' USING ERRCODE='GL001';
    END IF;
    RETURN QUERY
    WITH RECURSIVE source_root AS MATERIALIZED (
        SELECT s.lot_id FROM grainledger.capture_source_lots s
        JOIN grainledger.ledger_lots root ON root.id=s.lot_id AND root.owner_id=s.company_id
        WHERE s.company_id=o AND s.lot_id=source_id
    ), lineage_edges(input_lot_id,output_lot_id) AS (
        SELECT ce.input_lot_id,ce.output_lot_id
        FROM grainledger.ledger_custody_edges ce
        JOIN grainledger.ledger_custody_allocations ca ON ca.event_id=ce.event_id AND ca.output_lot_id=ce.output_lot_id
        WHERE ca.source_lot_id=source_id AND ca.quantity_kg>0
        UNION
        SELECT cpa.input_lot_id,cpa.output_lot_id
        FROM grainledger.ledger_custody_processing_allocations cpa
        WHERE cpa.source_lot_id=source_id AND cpa.quantity_kg>0
        UNION
        SELECT c.source_lot_id,c.lot_id
        FROM grainledger.ledger_contributions c WHERE c.quantity_kg>0
    ), descendants(lot_id) AS (
        SELECT sr.lot_id FROM source_root sr
        UNION
        SELECT edge.output_lot_id FROM descendants d
        JOIN lineage_edges edge ON edge.input_lot_id=d.lot_id
    ), stock AS MATERIALIZED (
        SELECT * FROM grainledger.ledger_procurement_stock_bulk(o,ARRAY[source_id])
    ), scoped AS MATERIALIZED (
        SELECT d.lot_id,l.code,
            coalesce(stock.location_id,(SELECT ce.location_id FROM grainledger.ledger_custody_events ce
                WHERE ce.input_lot_id=d.lot_id OR ce.output_lot_id=d.lot_id
                ORDER BY ce.created_at DESC,ce.event_id DESC LIMIT 1)) AS location_id,
            coalesce(stock.is_mixed_bulk,false) AS is_mixed_bulk,
            stock.pool_revision,coalesce(stock.on_hand_kg,0::numeric) AS quantity_kg
        FROM descendants d
        JOIN grainledger.ledger_lots l ON l.id=d.lot_id AND l.owner_id=o
        LEFT JOIN stock ON stock.current_stock_lot_id=d.lot_id
    )
    SELECT s.lot_id,s.code,s.location_id,
        s.is_mixed_bulk AND s.pool_revision IS NOT NULL,
        CASE WHEN s.is_mixed_bulk THEN s.pool_revision END,
        s.quantity_kg
    FROM scoped s
    ORDER BY s.lot_id,s.location_id;
END $$;

CREATE FUNCTION grainledger.ledger_procurement_stock(o uuid, l uuid) RETURNS TABLE(location_id uuid, on_hand_kg numeric, available_kg numeric)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT p.location_id,p.quantity_kg,CASE WHEN grainledger.ledger_blocked(p.lot_id) THEN 0 ELSE p.quantity_kg END
    FROM grainledger.capture_source_lots s LEFT JOIN grainledger.ledger_lots x ON x.id=s.lot_id AND x.owner_id=s.company_id
    LEFT JOIN grainledger.ledger_positions p ON p.lot_id=x.id WHERE s.lot_id=l AND s.company_id=o
$$;

CREATE FUNCTION grainledger.ledger_procurement_stock_bulk(o uuid, source_ids uuid[]) RETURNS TABLE(source_lot_id uuid, current_stock_lot_id uuid, current_stock_lot_code text, location_id uuid, on_hand_kg numeric, available_kg numeric, pool_total_kg numeric, is_mixed_bulk boolean, pool_revision bigint)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    WITH sources AS MATERIALIZED (
        SELECT s.lot_id AS root_lot_id
        FROM grainledger.capture_source_lots s
        JOIN grainledger.ledger_lots root ON root.id=s.lot_id AND root.owner_id=s.company_id
        WHERE s.company_id=o AND s.lot_id=ANY(source_ids)
    ), shares AS MATERIALIZED (
        SELECT c.lot_id,c.source_lot_id,sum(c.quantity_kg) AS creation_kg
        FROM grainledger.ledger_contributions c
        GROUP BY c.lot_id,c.source_lot_id
    ), creation_totals AS MATERIALIZED (
        SELECT lot_id,sum(creation_kg) AS quantity_kg
        FROM shares GROUP BY lot_id
    ), positions AS MATERIALIZED (
        SELECT p.lot_id,p.location_id,p.quantity_kg,lc.facility_id,lc.mixed_bulk,
            CASE WHEN lc.mixed_bulk THEN mbp.revision END AS pool_revision
        FROM grainledger.ledger_positions p
        JOIN grainledger.ledger_locations lc ON lc.id=p.location_id
        LEFT JOIN grainledger.ledger_mixed_bulk_pools mbp ON mbp.location_id=lc.id
        WHERE p.quantity_kg>0 AND (NOT lc.mixed_bulk OR mbp.lot_id=p.lot_id)
    ), raw_shares AS MATERIALIZED (
        SELECT p.*,sh.source_lot_id,
            sh.creation_kg*p.quantity_kg/NULLIF(t.quantity_kg,0) AS raw_kg
        FROM positions p
        JOIN shares sh ON sh.lot_id=p.lot_id
        JOIN creation_totals t ON t.lot_id=p.lot_id
        WHERE sh.creation_kg>0
    ), ranked_shares AS MATERIALIZED (
        SELECT r.*,
            trunc(r.raw_kg*1000000) AS whole_micro_kg,
            row_number() OVER (PARTITION BY r.lot_id,r.location_id
                ORDER BY r.raw_kg*1000000-trunc(r.raw_kg*1000000) DESC,r.source_lot_id) AS remainder_rank,
            round(r.quantity_kg*1000000)
                - sum(trunc(r.raw_kg*1000000)) OVER (PARTITION BY r.lot_id,r.location_id) AS remainder_micro_kg
        FROM raw_shares r
    ), allocated AS MATERIALIZED (
        SELECT r.*, (r.whole_micro_kg+
            CASE WHEN r.remainder_rank<=r.remainder_micro_kg THEN 1 ELSE 0 END)/1000000 AS allocated_kg
        FROM ranked_shares r
    )
    SELECT s.root_lot_id,l.id,l.code,p.location_id,a.allocated_kg,
        CASE WHEN grainledger.ledger_blocked(l.id) THEN 0::numeric ELSE a.allocated_kg END,
        CASE WHEN p.mixed_bulk THEN p.quantity_kg ELSE 0::numeric END,
        p.mixed_bulk,p.pool_revision
    FROM sources s
    JOIN allocated a ON a.source_lot_id=s.root_lot_id
    JOIN positions p ON p.lot_id=a.lot_id AND p.location_id=a.location_id
    JOIN grainledger.ledger_lots l ON l.id=p.lot_id AND l.owner_id=o
    WHERE a.allocated_kg>0
    ORDER BY s.root_lot_id,p.location_id,l.id
$$;

CREATE FUNCTION grainledger.ledger_protect_planning_quality_observation() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    RAISE EXCEPTION 'Confirmed planning quality observations are immutable' USING ERRCODE='23514';
END $$;

CREATE FUNCTION grainledger.ledger_protect_planning_yield_rule() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    RAISE EXCEPTION 'Published planning yield rules are immutable' USING ERRCODE='23514';
END $$;

CREATE FUNCTION grainledger.ledger_provision_catalog_commodities(a uuid) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE c uuid;
BEGIN
    IF grainledger.ledger_role(a) IS DISTINCT FROM 'Administrator' THEN
        RAISE EXCEPTION 'Catalog provisioning authority denied' USING ERRCODE='42501';
    END IF;
    PERFORM grainledger.ledger_lock_writes();
    FOR c IN SELECT cc.id FROM grainledger.catalog_commodities cc
        WHERE NOT EXISTS(SELECT 1 FROM grainledger.ledger_catalog_commodities m WHERE m.catalog_commodity_id=cc.id)
        ORDER BY cc.id
    LOOP
        PERFORM grainledger.ledger_provision_catalog_commodity(c);
    END LOOP;
END $$;

CREATE FUNCTION grainledger.ledger_provision_catalog_commodity(c uuid) RETURNS uuid
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE commodity uuid;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    SELECT ledger_commodity_id INTO commodity FROM grainledger.ledger_catalog_commodities WHERE catalog_commodity_id=c;
    IF FOUND THEN RETURN commodity; END IF;
    commodity:=gen_random_uuid();
    INSERT INTO grainledger.ledger_commodities(id,name) SELECT commodity,name FROM grainledger.catalog_commodities WHERE id=c;
    IF NOT FOUND THEN RAISE EXCEPTION 'Catalog commodity not found' USING ERRCODE='23514'; END IF;
    INSERT INTO grainledger.ledger_catalog_commodities VALUES(c,commodity);
    RETURN commodity;
END $$;

CREATE FUNCTION grainledger.ledger_publish_planning_yield_rule(a uuid, p jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
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
    PERFORM grainledger.ledger_lock_writes();
    IF grainledger.ledger_role(a) <> 'MillManager' THEN
        RAISE EXCEPTION 'Mill manager required' USING ERRCODE='42501';
    END IF;
    PERFORM grainledger.ledger_require_staff(a,f,ARRAY['MillManager']);
    IF rid IS NULL OR c IS NULL OR e IS NULL OR f IS NULL OR v IS NULL OR
       fraction IS NULL OR fraction <= 0 OR fraction > 1 OR
       scale(fraction) > 12 OR start_at IS NULL OR
       (end_at IS NOT NULL AND end_at <= start_at) OR
       review_at IS NULL OR review_at > statement_timestamp() OR
       length(btrim(coalesce(p->>'reviewedBy',''))) = 0 OR
       length(btrim(coalesce(p->>'reviewAuthority',''))) = 0 THEN
        RAISE EXCEPTION 'Invalid reviewed prospective yield rule' USING ERRCODE='22023';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_compatibilities WHERE id=c AND facility_id=f)
       OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_evidence WHERE id=e AND facility_id=f) THEN
        RAISE EXCEPTION 'Compatibility and evidence must belong to facility' USING ERRCODE='22023';
    END IF;
    SELECT COALESCE(max(version_no),0) INTO current_version
      FROM grainledger.ledger_planning_yield_rules WHERE facility_id=f AND compatibility_id=c;
    IF v <> current_version + 1 THEN
        RAISE EXCEPTION 'Yield rule version must follow current version' USING ERRCODE='23514';
    END IF;
    INSERT INTO grainledger.ledger_planning_yield_rules(id,facility_id,compatibility_id,version_no,
      output_fraction,effective_from,effective_to,evidence_id,reviewed_by,
      review_authority,reviewed_at,published_by,published_at)
    VALUES(rid,f,c,v,fraction,start_at,end_at,e,p->>'reviewedBy',
      p->>'reviewAuthority',review_at,a,statement_timestamp());
    RETURN jsonb_build_object('id',rid,'facilityId',f,'compatibilityPolicyId',c,
      'versionNo',v,'outputFraction',fraction::text,'effectiveFrom',start_at,
      'effectiveTo',end_at,'evidenceId',e,'publishedBy',a);
END $$;

CREATE FUNCTION grainledger.ledger_read(a uuid, action text, p jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE result jsonb;
BEGIN
    result:=grainledger.ledger_read_before_equipment_retirement(a,action,p);
    IF action='setup' THEN
        SELECT jsonb_set(result,'{equipment}',coalesce(jsonb_agg(entry.value||jsonb_build_object(
            'canDelete',coalesce(e.created_by=a AND grainledger.ledger_role(a)='Administrator'
                AND NOT grainledger.ledger_record_in_use('equipment',e.id),false),
            'canRetire',coalesce(e.created_by=a AND grainledger.ledger_role(a)='Administrator'
                AND grainledger.ledger_record_in_use('equipment',e.id) AND NOT grainledger.ledger_equipment_has_pending_work(e.id),false))
            ORDER BY entry.ordinal),'[]'::jsonb)) INTO result
        FROM jsonb_array_elements(result->'equipment') WITH ORDINALITY entry(value,ordinal)
        JOIN grainledger.ledger_equipment e ON e.id=(entry.value->>'id')::uuid;
    ELSIF action='operation' THEN
        SELECT result||jsonb_build_object('equipmentName',e.name||CASE WHEN e.is_deleted THEN ' (retired)' ELSE '' END)
        INTO result FROM grainledger.ledger_equipment e WHERE e.id=(result->>'equipmentId')::uuid;
    ELSIF action IN ('operations','history') THEN
        -- Decorate only rows already authorized by the scoped reader; never alter stored content or event details.
        SELECT jsonb_set(result,'{items}',coalesce(jsonb_agg(entry.value||CASE WHEN e.id IS NOT NULL THEN
            jsonb_build_object('equipmentName',e.name||CASE WHEN e.is_deleted THEN ' (retired)' ELSE '' END)
            ELSE '{}'::jsonb END ORDER BY entry.ordinal),'[]'::jsonb)) INTO result
        FROM jsonb_array_elements(result->'items') WITH ORDINALITY entry(value,ordinal)
        LEFT JOIN grainledger.ledger_operations op ON op.id=(CASE WHEN action='operations' THEN entry.value->>'id' ELSE entry.value->>'operationId' END)::uuid
        LEFT JOIN grainledger.ledger_events ev ON action='history' AND ev.id=(entry.value->>'id')::uuid
        LEFT JOIN grainledger.ledger_equipment e ON e.id=CASE WHEN ev.kind='VARIANCE_CONFIRMATION' AND op.id IS NOT NULL
            THEN coalesce((ev.details->'reviewedContent'->>'equipmentId')::uuid,op.equipment_id)
            ELSE op.equipment_id END;
    END IF;
    RETURN result;
END $$;

CREATE FUNCTION grainledger.ledger_read_before_equipment_retirement(a uuid, action text, p jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE
    result jsonb;
    page_number integer:=coalesce((p->>'page')::integer,1);
    page_size integer:=coalesce((p->>'pageSize')::integer,25);
    page_result jsonb;
    all_items jsonb:='[]'::jsonb;
    new_items jsonb:='[]'::jsonb;
    page_count integer;
    total_count integer:=0;
    n integer;
    requested_lot uuid:=NULLIF(p->>'lotId','')::uuid;
    requested_facility uuid:=NULLIF(p->>'facilityId','')::uuid;
    requested_location uuid:=NULLIF(p->>'locationId','')::uuid;
BEGIN
    IF action='history' THEN
        result:=grainledger.ledger_read_before_mixed_bulk(a,action,p);
        page_result:=grainledger.ledger_read_before_mixed_bulk(a,action,
            jsonb_set(jsonb_set(p,'{page}','1'::jsonb,true),'{pageSize}','100'::jsonb,true));
        total_count:=(page_result->>'totalCount')::integer;
        all_items:=coalesce(page_result->'items','[]'::jsonb);
        page_count:=ceil(total_count::numeric/100)::integer;
        FOR n IN 2..page_count LOOP
            page_result:=grainledger.ledger_read_before_mixed_bulk(a,action,
                jsonb_set(jsonb_set(p,'{page}',to_jsonb(n),true),'{pageSize}','100'::jsonb,true));
            all_items:=all_items||coalesce(page_result->'items','[]'::jsonb);
        END LOOP;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',ce.event_id,'postingId',NULL,'kind',ce.command_kind,'occurredAt',ce.created_at,
            'actorId',ce.actor_id,'operationId',NULL,'lotId',requested_lot,'locationId',ce.location_id,
            'ownerId',event_lot.owner_id,'ownerName',org."Name",'quantityKg',ce.quantity_kg::text,
            'reversesPostingId',NULL,
            'details',jsonb_build_object('action',ce.action,'poolRevision',ce.revision,
                'inputLotId',ce.input_lot_id,'outputLotId',ce.output_lot_id,
                'allocations',coalesce((SELECT jsonb_agg(jsonb_build_object(
                    'inputLotId',allocation.input_lot_id,'outputLotId',allocation.output_lot_id,
                    'sourceLotId',allocation.source_lot_id,'quantityKg',allocation.quantity_kg::text)
                    ORDER BY allocation.input_lot_id,allocation.output_lot_id,allocation.source_lot_id)
                    FROM (
                        SELECT ca.input_lot_id,ca.output_lot_id,ca.source_lot_id,ca.quantity_kg FROM grainledger.ledger_custody_allocations ca WHERE ca.event_id=ce.event_id
                        UNION ALL
                        SELECT cpa.input_lot_id,cpa.output_lot_id,cpa.source_lot_id,cpa.quantity_kg FROM grainledger.ledger_custody_processing_allocations cpa WHERE cpa.event_id=ce.event_id
                    ) allocation),'[]'::jsonb))
        ) ORDER BY ce.created_at,ce.event_id),'[]'::jsonb)
        INTO new_items
        FROM grainledger.ledger_custody_events ce
        JOIN grainledger.ledger_lots event_lot ON event_lot.id=coalesce(ce.output_lot_id,ce.input_lot_id)
        JOIN grainledger.identity_organizations org ON org."Id"=event_lot.owner_id
        WHERE (requested_lot IS NULL OR ce.input_lot_id=requested_lot OR ce.output_lot_id=requested_lot)
          AND (requested_facility IS NULL OR EXISTS(SELECT 1 FROM grainledger.ledger_locations lc WHERE lc.id=ce.location_id AND lc.facility_id=requested_facility))
          AND (requested_location IS NULL OR ce.location_id=requested_location)
          AND (grainledger.ledger_own(a,event_lot.owner_id) OR grainledger.ledger_assigned(a,(SELECT lc.facility_id FROM grainledger.ledger_locations lc WHERE lc.id=ce.location_id)))
          AND (requested_lot IS NULL OR grainledger.ledger_can_read_lot(a,requested_lot))
          AND (p->>'ownerId' IS NULL OR event_lot.owner_id=(p->>'ownerId')::uuid)
          AND (p->>'kind' IS NULL OR ce.command_kind=p->>'kind')
          AND (p->>'search' IS NULL OR concat_ws(' ',event_lot.code,ce.command_kind,ce.action) ILIKE '%'||(p->>'search')||'%')
          AND NOT EXISTS (
              SELECT 1 FROM jsonb_array_elements(all_items) old
              WHERE old->>'id'=ce.event_id::text AND old->>'postingId' IS NULL
          );
        all_items:=all_items||new_items;
        WITH candidates AS MATERIALIZED (
            SELECT item FROM jsonb_array_elements(all_items) item
        ), ordered AS (
            SELECT item,row_number() OVER (ORDER BY
                CASE WHEN coalesce(p->>'sortDirection','asc')='asc' AND coalesce(p->>'sortBy','occurredAt')='occurredAt' THEN (item->>'occurredAt')::timestamptz END ASC NULLS LAST,
                CASE WHEN coalesce(p->>'sortDirection','asc')='desc' AND coalesce(p->>'sortBy','occurredAt')='occurredAt' THEN (item->>'occurredAt')::timestamptz END DESC NULLS LAST,
                CASE WHEN coalesce(p->>'sortDirection','asc')='asc' AND coalesce(p->>'sortBy','occurredAt')='kind' THEN item->>'kind' END ASC NULLS LAST,
                CASE WHEN coalesce(p->>'sortDirection','asc')='desc' AND coalesce(p->>'sortBy','occurredAt')='kind' THEN item->>'kind' END DESC NULLS LAST,
                CASE WHEN coalesce(p->>'sortDirection','asc')='asc' AND coalesce(p->>'sortBy','occurredAt')='quantity' THEN NULLIF(item->>'quantityKg','')::numeric END ASC NULLS LAST,
                CASE WHEN coalesce(p->>'sortDirection','asc')='desc' AND coalesce(p->>'sortBy','occurredAt')='quantity' THEN NULLIF(item->>'quantityKg','')::numeric END DESC NULLS LAST,
                item->>'id',item->>'postingId') ordinal
            FROM candidates
        ), selected AS (
            SELECT item,ordinal FROM ordered WHERE ordinal>(page_number::bigint-1)*page_size AND ordinal<=page_number::bigint*page_size
        )
        SELECT jsonb_build_object(
            'items',coalesce((SELECT jsonb_agg(item ORDER BY ordinal) FROM selected),'[]'::jsonb),
            'page',page_number,'pageSize',page_size,
            'totalCount',(SELECT count(*) FROM candidates),
            'hasMore',(SELECT count(*) FROM candidates)>page_number::bigint*page_size)
        INTO result;
        RETURN result;
    END IF;
    IF p ? 'availableOnly' AND (action<>'lots' OR p->>'availableOnly' NOT IN ('true','false')) THEN
        RAISE EXCEPTION 'Invalid available-only filter' USING ERRCODE='22023';
    END IF;
    IF action='lots' AND p->>'availableOnly'='true' THEN
        RETURN grainledger.ledger_available_lots_page(a,p);
    END IF;
    result:=grainledger.ledger_read_before_mixed_bulk(a,action,p);
    IF action IN ('setup','lookups') THEN
        result:=jsonb_set(result,'{locations}',coalesce((
            SELECT jsonb_agg(loc || jsonb_build_object(
                'mixedBulk',lc.mixed_bulk,
                'currentPoolLotId',CASE WHEN pool.lot_id IS NULL OR grainledger.ledger_can_read_lot(a,pool.lot_id) THEN pool.lot_id END,
                'poolRevision',CASE WHEN grainledger.ledger_assigned(a,lc.facility_id) OR grainledger.ledger_own(a,lc.owner_id) THEN pool.revision END) ORDER BY loc->>'name',loc->>'id')
            FROM jsonb_array_elements(coalesce(result->'locations','[]'::jsonb)) loc
            JOIN grainledger.ledger_locations lc ON lc.id=(loc->>'id')::uuid
            LEFT JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.location_id=lc.id
        ),'[]'::jsonb),true);
        IF action='setup' THEN
            result:=result||jsonb_build_object('mixedBulkPolicies',coalesce((
                SELECT jsonb_agg(jsonb_build_object(
                    'id',mp.id,'locationId',mp.location_id,'ownerId',mp.owner_id,
                    'commodityId',mp.commodity_id,'stage',mp.stage,'productIdentity',mp.product_identity,
                    'effectiveFrom',mp.effective_from,'evidenceId',mp.evidence_id,'approvedBy',mp.approved_by))
                FROM grainledger.ledger_mixed_bulk_policies mp
                JOIN grainledger.ledger_locations lc ON lc.id=mp.location_id
                WHERE grainledger.ledger_role(a)='Administrator' OR grainledger.ledger_assigned(a,lc.facility_id)
            ),'[]'::jsonb));
        END IF;
    END IF;
    RETURN result;
END $$;

CREATE FUNCTION grainledger.ledger_read_before_mixed_bulk(a uuid, action text, p jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE
    result jsonb;
    requested_lot uuid:=NULLIF(p->>'lotId','')::uuid;
    requested_facility uuid:=NULLIF(p->>'facilityId','')::uuid;
    requested_owner uuid:=NULLIF(p->>'ownerId','')::uuid;
    requested_location uuid:=NULLIF(p->>'locationId','')::uuid;
    page_number integer:=coalesce((p->>'page')::integer,1);
    page_size integer:=coalesce((p->>'pageSize')::integer,25);
    sort_key text:=coalesce(p->>'sortBy',CASE action WHEN 'lots' THEN 'code' WHEN 'operations' THEN 'occurredAt' WHEN 'history' THEN 'occurredAt' WHEN 'corrections' THEN 'requestedAt' WHEN 'receiving-tasks' THEN 'receivedAt' ELSE 'createdAt' END);
    direction text:=coalesce(p->>'sortDirection',CASE WHEN action IN ('lots','history') THEN 'asc' ELSE 'desc' END);
    requested_ids uuid[];
BEGIN
    IF grainledger.ledger_role(a) IS NULL THEN RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501'; END IF;
    IF action IN ('setup','lookups') THEN
        IF action='setup' AND grainledger.ledger_role(a) NOT IN ('Administrator','MillManager','MillOperator') THEN RAISE EXCEPTION 'Setup access denied' USING ERRCODE='42501'; END IF;
        SELECT jsonb_build_object(
            'facilities',coalesce((SELECT jsonb_agg(jsonb_build_object('id',x.id,'name',x.name) || CASE WHEN action='setup' THEN jsonb_build_object('createdBy',x.created_by,'version',x.version,
                'canEdit',coalesce(x.created_by=a AND grainledger.ledger_role(a)='Administrator',false),
                'canDelete',coalesce(x.created_by=a AND grainledger.ledger_role(a)='Administrator',false)) ELSE '{}'::jsonb END ORDER BY x.name,x.id)
                FROM grainledger.ledger_facilities x WHERE NOT x.is_deleted AND (grainledger.ledger_role(a) IN ('Administrator','CustomerManager','ExportStaff','PurchasingStaff') OR grainledger.ledger_assigned(a,x.id))),'[]'::jsonb),
            'locations',coalesce((SELECT jsonb_agg(jsonb_build_object('id',x.id,'facilityId',x.facility_id,'name',x.name,'kind',x.kind)
                || CASE WHEN action='setup' THEN jsonb_build_object('createdBy',x.created_by,'version',x.version,
                'canEdit',coalesce(x.created_by=a AND grainledger.ledger_role(a)='Administrator',false),
                'canDelete',coalesce(x.created_by=a AND grainledger.ledger_role(a)='Administrator',false),
                'ownerId',x.owner_id,'cleared',x.cleared) ELSE '{}'::jsonb END ORDER BY x.name,x.id)
                FROM grainledger.ledger_locations x WHERE NOT x.is_deleted AND EXISTS(SELECT 1 FROM grainledger.ledger_facilities f WHERE f.id=x.facility_id AND NOT f.is_deleted) AND (grainledger.ledger_role(a) IN ('Administrator','CustomerManager','ExportStaff','PurchasingStaff') OR grainledger.ledger_assigned(a,x.facility_id))),'[]'::jsonb),
            'commodities',coalesce((SELECT jsonb_agg(jsonb_build_object('id',lc.id,'name',coalesce(cc.name,lc.name),'code',cc.code,'catalogId',cc.id) ORDER BY coalesce(cc.name,lc.name),lc.id) FROM grainledger.ledger_commodities lc LEFT JOIN grainledger.ledger_catalog_commodities m ON m.ledger_commodity_id=lc.id LEFT JOIN grainledger.catalog_commodities cc ON cc.id=m.catalog_commodity_id WHERE cc.id IS NULL OR NOT cc.is_archived),'[]'::jsonb),
            'owners',coalesce((SELECT jsonb_agg(jsonb_build_object('id',o."Id",'name',o."Name") ORDER BY o."Name",o."Id") FROM grainledger.identity_organizations o WHERE o."Kind"='Customer' AND (grainledger.ledger_own(a,o."Id") OR EXISTS(SELECT 1 FROM grainledger.ledger_lots l WHERE l.owner_id=o."Id" AND grainledger.ledger_can_read_lot(a,l.id)) OR EXISTS(SELECT 1 FROM grainledger.ledger_operations op WHERE op.owner_id=o."Id" AND NOT op.is_deleted AND grainledger.ledger_can_read_operation(a,op.id)))),'[]'::jsonb),
            'equipment',coalesce((SELECT jsonb_agg(jsonb_build_object('id',x.id,'facilityId',x.facility_id,'name',x.name) || CASE WHEN action='setup' THEN jsonb_build_object('createdBy',x.created_by,'version',x.version,
                'canEdit',coalesce(x.created_by=a AND grainledger.ledger_role(a)='Administrator',false),
                'canDelete',coalesce(x.created_by=a AND grainledger.ledger_role(a)='Administrator',false)) ELSE '{}'::jsonb END ORDER BY x.name,x.id) FROM grainledger.ledger_equipment x WHERE NOT x.is_deleted AND EXISTS(SELECT 1 FROM grainledger.ledger_facilities f WHERE f.id=x.facility_id AND NOT f.is_deleted) AND (grainledger.ledger_role(a)='Administrator' OR grainledger.ledger_role(a) IN ('MillManager','MillOperator') AND grainledger.ledger_assigned(a,x.facility_id))),'[]'::jsonb)) INTO result;
        IF action='setup' THEN result:=result||jsonb_build_object(
            'assignments',coalesce((SELECT jsonb_agg(jsonb_build_object('userId',user_id,'facilityId',facility_id,'createdBy',created_by,'version',version,'canEdit',coalesce(created_by=a AND grainledger.ledger_role(a)='Administrator',false),'canDelete',coalesce(created_by=a AND grainledger.ledger_role(a)='Administrator',false)) ORDER BY facility_id,user_id) FROM grainledger.ledger_assignments aa WHERE NOT aa.is_deleted AND EXISTS(SELECT 1 FROM grainledger.ledger_facilities f WHERE f.id=aa.facility_id AND NOT f.is_deleted) AND (grainledger.ledger_role(a)='Administrator' OR user_id=a)),'[]'::jsonb),
            'policies',coalesce((SELECT jsonb_agg(jsonb_build_object('id',id,'facilityId',facility_id,'kind',kind,'tolerancePercent',tolerance_percent::text,'effectiveFrom',effective_from,'evidenceId',evidence_id)) FROM grainledger.ledger_policies WHERE grainledger.ledger_role(a)='Administrator' OR grainledger.ledger_assigned(a,facility_id)),'[]'::jsonb),
            'compatibilities',coalesce((SELECT jsonb_agg(jsonb_build_object('id',id,'facilityId',facility_id,'inputProducts',input_products,'outputCommodityId',output_commodity_id,'effectiveFrom',effective_from,'evidenceId',evidence_id)) FROM grainledger.ledger_compatibilities WHERE grainledger.ledger_role(a)='Administrator' OR grainledger.ledger_assigned(a,facility_id)),'[]'::jsonb)); END IF;
        RETURN result;
    END IF;
    IF action='lot' THEN
        IF NOT grainledger.ledger_can_read_lot(a,requested_lot) THEN RAISE EXCEPTION 'Lot access denied' USING ERRCODE='42501'; END IF;
        RETURN grainledger.ledger_lot_json(a,requested_lot,true)||jsonb_build_object('history',grainledger.ledger_read(a,'history',p));
    END IF;
    IF action='operation' THEN
        IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_operations x WHERE x.id=(p->>'id')::uuid AND NOT x.is_deleted AND grainledger.ledger_can_read_operation(a,x.id)) THEN RAISE EXCEPTION 'Operation access denied' USING ERRCODE='42501'; END IF;
        RETURN grainledger.ledger_operation_for_actor(a,(p->>'id')::uuid);
    END IF;
    IF page_number<1 OR page_size NOT BETWEEN 1 AND 100 OR (page_number::bigint-1)*page_size>2147483647 OR direction NOT IN ('asc','desc') THEN RAISE EXCEPTION 'Invalid pagination or sort direction' USING ERRCODE='22023'; END IF;
    IF NOT sort_key=ANY(CASE action WHEN 'lots' THEN ARRAY['code','stage','owner','quantity'] WHEN 'operations' THEN ARRAY['occurredAt','kind','owner','status'] WHEN 'holds' THEN ARRAY['createdAt','status','owner'] WHEN 'corrections' THEN ARRAY['requestedAt','status','owner'] WHEN 'notifications' THEN ARRAY['createdAt','readAt'] WHEN 'history' THEN ARRAY['occurredAt','kind','quantity'] WHEN 'receiving-tasks' THEN ARRAY['receivedAt','status','owner'] ELSE ARRAY[]::text[] END) THEN RAISE EXCEPTION 'Unsupported ledger sort' USING ERRCODE='22023'; END IF;
    IF p ? 'ids' THEN
        IF action NOT IN ('lots','operations') THEN RAISE EXCEPTION 'IDs filter not supported' USING ERRCODE='22023'; END IF;
        requested_ids:=string_to_array(p->>'ids',',')::uuid[];
        IF cardinality(requested_ids) NOT BETWEEN 1 AND 100 THEN RAISE EXCEPTION 'IDs requires 1–100 UUIDs' USING ERRCODE='22023'; END IF;
    END IF;
    IF action='receiving-tasks' THEN RETURN grainledger.ledger_receiving_tasks_page(a,p); END IF;
    IF action='history' AND requested_lot IS NOT NULL AND NOT grainledger.ledger_can_read_lot(a,requested_lot) THEN RAISE EXCEPTION 'History lot access denied' USING ERRCODE='42501'; END IF;
    IF p ? 'stage' AND (action<>'lots' OR p->>'stage' NOT IN ('RAW_PADDY','DRIED_PADDY','HEAD_RICE','BROKEN_RICE','BRAN','HUSK','BLENDED_RICE','BAGGED_RICE')) THEN RAISE EXCEPTION 'Invalid stock stage' USING ERRCODE='22023'; END IF;
    IF p ? 'status' AND NOT (action='operations' AND p->>'status' IN ('DRAFT','COMMITTED','CORRECTED') OR action='holds' AND p->>'status' IN ('ACTIVE','ACKNOWLEDGED','ISOLATED','RESPONDED') OR action='corrections' AND p->>'status' IN ('REQUESTED','RECONCILED','CANCELLED','COMMITTED') OR action='notifications' AND p->>'status' IN ('READ','UNREAD')) THEN RAISE EXCEPTION 'Invalid status filter' USING ERRCODE='22023'; END IF;
    IF p ? 'kind' AND (action NOT IN ('operations','history','corrections') OR action IN ('operations','corrections') AND p->>'kind' NOT IN ('DRYING','MILLING','MERGE','BLENDING','BAGGING')) THEN RAISE EXCEPTION 'Invalid kind filter' USING ERRCODE='22023'; END IF;
    IF p ? 'queueOnly' AND (action<>'operations' OR p->>'queueOnly' NOT IN ('true','false')) THEN RAISE EXCEPTION 'Invalid queue filter' USING ERRCODE='22023'; END IF;
    WITH history_event_ids AS MATERIALIZED (
        SELECT id FROM grainledger.ledger_events WHERE action='history' AND requested_lot IS NULL
        UNION
        SELECT id FROM grainledger.ledger_events WHERE action='history' AND lot_id=requested_lot
        UNION
        SELECT id FROM grainledger.ledger_events WHERE action='history' AND operation_id=(SELECT operation_id FROM grainledger.ledger_lots WHERE id=requested_lot)
        UNION
        SELECT event_id FROM grainledger.ledger_postings WHERE action='history' AND lot_id=requested_lot
    ), candidates AS MATERIALIZED (
        SELECT x.id,NULL::uuid posting_id,x.owner_id,o."Name" owner_name,x.code code,x.stage kind,NULL::text status,x.created_at at,NULL::timestamptz read_at,
            CASE WHEN sort_key='quantity' THEN coalesce((SELECT sum(pos.quantity_kg) FROM grainledger.ledger_positions pos JOIN grainledger.ledger_locations lc ON lc.id=pos.location_id WHERE pos.lot_id=x.id AND (grainledger.ledger_own(a,x.owner_id) OR grainledger.ledger_assigned(a,lc.facility_id)) AND (requested_facility IS NULL OR lc.facility_id=requested_facility) AND (requested_location IS NULL OR lc.id=requested_location)),0) END quantity
        FROM grainledger.ledger_lots x JOIN grainledger.identity_organizations o ON o."Id"=x.owner_id
        WHERE action='lots' AND grainledger.ledger_can_read_lot(a,x.id) AND (requested_ids IS NULL OR x.id=ANY(requested_ids)) AND (requested_lot IS NULL OR x.id=requested_lot) AND (p->>'stage' IS NULL OR x.stage=p->>'stage')
            AND (requested_facility IS NULL AND requested_location IS NULL OR EXISTS(SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_locations lc ON lc.id=pos.location_id WHERE pos.lot_id=x.id AND (grainledger.ledger_own(a,x.owner_id) OR grainledger.ledger_assigned(a,lc.facility_id)) AND (requested_facility IS NULL OR lc.facility_id=requested_facility) AND (requested_location IS NULL OR lc.id=requested_location)))
        UNION ALL
        SELECT x.id,NULL,x.owner_id,o."Name",x.id::text,x.kind,x.status,x.occurred_at,NULL,NULL
        FROM grainledger.ledger_operations x JOIN grainledger.identity_organizations o ON o."Id"=x.owner_id
        WHERE action='operations' AND NOT x.is_deleted AND grainledger.ledger_can_read_operation(a,x.id) AND (requested_ids IS NULL OR x.id=ANY(requested_ids)) AND (requested_facility IS NULL OR x.facility_id=requested_facility)
            AND (requested_lot IS NULL OR EXISTS(SELECT 1 FROM jsonb_array_elements(x.content->'inputs') i WHERE i->>'lotId'=requested_lot::text) OR EXISTS(SELECT 1 FROM grainledger.ledger_lots l WHERE l.operation_id=x.id AND l.id=requested_lot))
            AND (requested_location IS NULL OR EXISTS(SELECT 1 FROM jsonb_array_elements(x.content->'inputs') i WHERE i->>'locationId'=requested_location::text) OR EXISTS(SELECT 1 FROM jsonb_array_elements(x.content->'outputs') o WHERE o->>'locationId'=requested_location::text))
            AND (coalesce(p->>'queueOnly','false')='false' OR x.status='DRAFT' AND x.confirmed_version IS DISTINCT FROM x.version
                AND (SELECT sum(grainledger.ledger_kg(i->>'quantityKg')) FROM jsonb_array_elements(x.content->'inputs') i)
                    -(SELECT sum(grainledger.ledger_kg(o->>'quantityKg')) FROM jsonb_array_elements(x.content->'outputs') o)-grainledger.ledger_kg(x.content->>'declaredLossKg')<>0)
        UNION ALL
        SELECT h.id,NULL,l.owner_id,o."Name",l.code,NULL,h.status,h.created_at,NULL,NULL
        FROM grainledger.ledger_holds h JOIN grainledger.ledger_lots l ON l.id=h.lot_id JOIN grainledger.identity_organizations o ON o."Id"=l.owner_id
        WHERE action='holds' AND grainledger.ledger_can_read_lot(a,l.id) AND (requested_lot IS NULL OR l.id=requested_lot) AND (requested_facility IS NULL AND requested_location IS NULL OR EXISTS(SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_locations lc ON lc.id=pos.location_id WHERE pos.lot_id=l.id AND (grainledger.ledger_own(a,l.owner_id) OR grainledger.ledger_assigned(a,lc.facility_id)) AND (requested_facility IS NULL OR lc.facility_id=requested_facility) AND (requested_location IS NULL OR lc.id=requested_location)))
        UNION ALL
        SELECT c.id,NULL,op.owner_id,o."Name",op.id::text,op.kind,c.status,(SELECT min(e.occurred_at) FROM grainledger.ledger_events e WHERE e.operation_id=op.id AND e.kind='CORRECTION_REQUEST'),NULL,NULL
        FROM grainledger.ledger_corrections c JOIN grainledger.ledger_operations op ON op.id=c.operation_id JOIN grainledger.identity_organizations o ON o."Id"=op.owner_id
        WHERE action='corrections' AND NOT op.is_deleted AND grainledger.ledger_can_read_operation(a,op.id) AND (requested_facility IS NULL OR op.facility_id=requested_facility) AND (requested_lot IS NULL OR EXISTS(SELECT 1 FROM grainledger.ledger_lots l WHERE l.id=requested_lot AND l.operation_id=op.id))
            AND (requested_location IS NULL OR EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'inputs') i WHERE i->>'locationId'=requested_location::text) OR EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'outputs') o WHERE o->>'locationId'=requested_location::text))
        UNION ALL
        SELECT n.id,NULL,l.owner_id,o."Name",l.code,NULL,CASE WHEN n.read_at IS NULL THEN 'UNREAD' ELSE 'READ' END,n.created_at,n.read_at,NULL
        FROM grainledger.ledger_notifications n JOIN grainledger.ledger_lots l ON l.id=n.lot_id JOIN grainledger.identity_organizations o ON o."Id"=l.owner_id
        WHERE action='notifications' AND n.user_id=a AND grainledger.ledger_can_read_lot(a,l.id) AND (requested_lot IS NULL OR l.id=requested_lot) AND (requested_facility IS NULL AND requested_location IS NULL OR EXISTS(SELECT 1 FROM grainledger.ledger_positions pos JOIN grainledger.ledger_locations lc ON lc.id=pos.location_id WHERE pos.lot_id=l.id AND (grainledger.ledger_own(a,l.owner_id) OR grainledger.ledger_assigned(a,lc.facility_id)) AND (requested_facility IS NULL OR lc.facility_id=requested_facility) AND (requested_location IS NULL OR lc.id=requested_location)))
        UNION ALL
        SELECT ee.id,pp.id,coalesce(l.owner_id,op.owner_id),o."Name",coalesce(l.code,op.id::text),ee.kind,NULL,ee.occurred_at,NULL,pp.quantity_kg
        FROM history_event_ids hi JOIN grainledger.ledger_events ee ON ee.id=hi.id LEFT JOIN grainledger.ledger_postings pp ON pp.event_id=ee.id LEFT JOIN grainledger.ledger_locations lc ON lc.id=pp.location_id LEFT JOIN grainledger.ledger_lots l ON l.id=coalesce(pp.lot_id,ee.lot_id) LEFT JOIN grainledger.ledger_operations op ON op.id=ee.operation_id LEFT JOIN grainledger.identity_organizations o ON o."Id"=coalesce(l.owner_id,op.owner_id)
        WHERE action='history' AND (requested_lot IS NULL OR coalesce(pp.lot_id,ee.lot_id)=requested_lot OR ee.operation_id=(SELECT operation_id FROM grainledger.ledger_lots WHERE id=requested_lot))
            AND (requested_facility IS NULL OR lc.facility_id=requested_facility OR op.facility_id=requested_facility) AND (requested_location IS NULL OR pp.location_id=requested_location)
            AND CASE WHEN pp.id IS NOT NULL THEN grainledger.ledger_can_read_lot(a,pp.lot_id) AND (grainledger.ledger_own(a,l.owner_id) OR grainledger.ledger_assigned(a,lc.facility_id)) WHEN ee.lot_id IS NOT NULL THEN grainledger.ledger_can_read_lot(a,ee.lot_id) ELSE grainledger.ledger_can_read_operation(a,ee.operation_id) END
    ), filtered AS MATERIALIZED (
        SELECT * FROM candidates x WHERE (requested_owner IS NULL OR x.owner_id=requested_owner) AND (p->>'status' IS NULL OR x.status=p->>'status') AND (p->>'kind' IS NULL OR x.kind=p->>'kind')
            AND (p->>'search' IS NULL
                OR concat_ws(' ',x.code,x.owner_name,x.kind,x.status) ILIKE '%'||(p->>'search')||'%'
                OR action IN ('operations','corrections') AND EXISTS (
                    SELECT 1 FROM grainledger.ledger_operations op
                    CROSS JOIN LATERAL jsonb_array_elements(op.content->'outputs') output
                    WHERE op.id=CASE WHEN action='operations' THEN x.id ELSE (SELECT c.operation_id FROM grainledger.ledger_corrections c WHERE c.id=x.id) END
                        AND output->>'code' ILIKE '%'||(p->>'search')||'%'
                )
                OR action='holds' AND EXISTS(SELECT 1 FROM grainledger.ledger_holds h WHERE h.id=x.id AND h.reason ILIKE '%'||(p->>'search')||'%')
                OR action='corrections' AND EXISTS(SELECT 1 FROM grainledger.ledger_corrections c WHERE c.id=x.id AND c.reason ILIKE '%'||(p->>'search')||'%')
                OR action='notifications' AND EXISTS(SELECT 1 FROM grainledger.ledger_notifications n WHERE n.id=x.id AND n.message ILIKE '%'||(p->>'search')||'%'))
    ), ordered AS (
        SELECT x.*,row_number() OVER (ORDER BY
            CASE WHEN direction='asc' THEN CASE sort_key WHEN 'code' THEN x.code WHEN 'stage' THEN x.kind WHEN 'kind' THEN x.kind WHEN 'status' THEN x.status WHEN 'owner' THEN x.owner_name END END ASC NULLS LAST,
            CASE WHEN direction='desc' THEN CASE sort_key WHEN 'code' THEN x.code WHEN 'stage' THEN x.kind WHEN 'kind' THEN x.kind WHEN 'status' THEN x.status WHEN 'owner' THEN x.owner_name END END DESC NULLS LAST,
            CASE WHEN direction='asc' AND sort_key IN ('createdAt','requestedAt','occurredAt') THEN x.at WHEN direction='asc' AND sort_key='readAt' THEN x.read_at END ASC NULLS LAST,
            CASE WHEN direction='desc' AND sort_key IN ('createdAt','requestedAt','occurredAt') THEN x.at WHEN direction='desc' AND sort_key='readAt' THEN x.read_at END DESC NULLS LAST,
            CASE WHEN direction='asc' AND sort_key='quantity' THEN x.quantity END ASC NULLS LAST,
            CASE WHEN direction='desc' AND sort_key='quantity' THEN x.quantity END DESC NULLS LAST,x.id,x.posting_id NULLS FIRST) ordinal FROM filtered x
    ), selected AS MATERIALIZED (
        SELECT * FROM ordered ORDER BY ordinal LIMIT page_size OFFSET (page_number::bigint-1)*page_size
    )
    SELECT jsonb_build_object('items',coalesce((SELECT jsonb_agg(
        CASE action
            WHEN 'lots' THEN grainledger.ledger_lot_json(a,x.id,false)||jsonb_build_object('ownerName',x.owner_name)
            WHEN 'operations' THEN grainledger.ledger_operation_for_actor(a,x.id)
            WHEN 'holds' THEN (SELECT jsonb_build_object('id',h.id,'lotId',h.lot_id,'lotCode',x.code,'reason',h.reason,'status',h.status,'ownerId',x.owner_id,'ownerName',x.owner_name,'evidenceId',CASE WHEN grainledger.ledger_role(a)<>'MillOperator' THEN h.evidence_id END,'response',h.response,'createdAt',h.created_at) FROM grainledger.ledger_holds h WHERE h.id=x.id)
            WHEN 'corrections' THEN (SELECT jsonb_build_object('id',c.id,'operationId',c.operation_id,'kind',x.kind,'ownerId',x.owner_id,'ownerName',x.owner_name,'requestedAt',x.at,'reason',c.reason,'status',c.status,'evidenceId',CASE WHEN grainledger.ledger_role(a)<>'MillOperator' THEN c.evidence_id END,'reconciliation',CASE WHEN grainledger.ledger_role(a)<>'MillOperator' THEN c.reconciliation END,'replacementOperationId',c.replacement_operation_id) FROM grainledger.ledger_corrections c WHERE c.id=x.id)
            WHEN 'notifications' THEN (SELECT jsonb_build_object('id',n.id,'holdId',n.hold_id,'lotId',n.lot_id,'ownerId',x.owner_id,'ownerName',x.owner_name,'message',n.message,'createdAt',n.created_at,'readAt',n.read_at) FROM grainledger.ledger_notifications n WHERE n.id=x.id)
            WHEN 'history' THEN (SELECT jsonb_build_object('id',ee.id,'postingId',pp.id,'kind',ee.kind,'occurredAt',ee.occurred_at,'actorId',ee.actor_id,'operationId',ee.operation_id,'lotId',coalesce(pp.lot_id,ee.lot_id),'locationId',pp.location_id,'ownerId',x.owner_id,'ownerName',x.owner_name,'quantityKg',pp.quantity_kg::text,'reversesPostingId',pp.reverses_posting_id,'details',CASE WHEN grainledger.ledger_role(a)='MillOperator' THEN jsonb_build_object('declaredLossKg',ee.details->>'declaredLossKg','varianceKg',ee.details->>'varianceKg') ELSE ee.details END) FROM grainledger.ledger_events ee LEFT JOIN grainledger.ledger_postings pp ON pp.id=x.posting_id WHERE ee.id=x.id)
        END ORDER BY x.ordinal) FROM selected x),'[]'::jsonb),'page',page_number,'pageSize',page_size,'totalCount',(SELECT count(*) FROM filtered),'hasMore',(SELECT count(*) FROM filtered)>page_number::bigint*page_size) INTO result;
    RETURN result;
END $$;

CREATE FUNCTION grainledger.ledger_receiving_detail(a uuid, r uuid) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE h grainledger.capture_procurements; s grainledger.ledger_receiving; snap jsonb; result jsonb; effective jsonb;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    SELECT * INTO STRICT h FROM grainledger.capture_procurements WHERE id=r AND receiving_workflow IS NOT NULL;
    PERFORM grainledger.ledger_require_staff(a,h.collection_site_id,ARRAY['MillOperator','MillManager']);
    SELECT * INTO STRICT s FROM grainledger.ledger_receiving WHERE procurement_id=r;
    SELECT payload->'result' INTO snap FROM grainledger.capture_business_events WHERE id=h.event_id;
    PERFORM grainledger.ledger_require_active_location((snap->>'ReceivingLocationId')::uuid);
    effective:=grainledger.ledger_receiving_threshold_effective(h.company_id);
    SELECT jsonb_build_object('id',h.id,'code',h.code,'ownerCompanyId',h.company_id,'ownerCompanyName',o."Name",
        'facilityId',h.collection_site_id,'facilityName',f.name,'receivingLocationId',l.id,'receivingLocationName',l.name,
        'status',s.status,'receivingVersion',s.version,'receivedAt',h.received_at,'expectedTotalKg',snap->>'TotalNetMassKg',
        'effectiveThreshold',effective,
        'actualTotalKg',(SELECT sum(actual_kg)::text FROM grainledger.ledger_arrival_measurements WHERE measurement_set_id=s.latest_measurement_set_id),
        'usableStockKg',CASE WHEN s.accepted_measurement_set_id IS NULL THEN '0' ELSE COALESCE((
            SELECT sum(stock.available_kg)::text
            FROM grainledger.ledger_procurement_stock_bulk(h.company_id,(
                SELECT array_agg(sl.lot_id) FROM grainledger.capture_source_lots sl
                JOIN grainledger.capture_procurement_items i ON i.id=sl.procurement_item_id
                WHERE i.procurement_id=r AND sl.company_id=h.company_id
            )) stock
        ),'0') END,
        'latestMeasurementSetId',s.latest_measurement_set_id,
        'items',(SELECT jsonb_agg(jsonb_build_object('id',i.id,'lineNo',i.line_no,'sourceLotId',sl.id,'lotId',sl.lot_id,'lotCode',grainledger.ledger_source_lot_code(r,sl.lot_id),
            'plotSeasonId',i.plot_season_id,'riceVariety',ps.rice_variety,'expectedValue',i.measured_value::text,'expectedUnit',i.measured_unit,'expectedKg',i.net_mass_kg::text,
            'actualValue',m.measured_value::text,'actualUnit',m.measured_unit,'actualKg',m.actual_kg::text) ORDER BY i.line_no)
            FROM grainledger.capture_procurement_items i JOIN grainledger.capture_source_lots sl ON sl.procurement_item_id=i.id JOIN grainledger.capture_plot_seasons ps ON ps.id=i.plot_season_id
            LEFT JOIN grainledger.ledger_arrival_measurements m ON m.procurement_item_id=i.id AND m.measurement_set_id=s.latest_measurement_set_id WHERE i.procurement_id=r),
        'measurementSets',COALESCE((SELECT jsonb_agg(jsonb_build_object('id',ms.id,'operationId',ms.operation_id,'operatorId',ms.operator_id,'capturedAt',ms.captured_at,'recordedAt',ms.recorded_at,'observations',ms.observations,
            'threshold',CASE WHEN ms.threshold_revision IS NULL THEN NULL ELSE jsonb_build_object('companyId',ms.threshold_company_id,'percentage',ms.threshold_percentage::text,'source',ms.threshold_source,'revision',ms.threshold_revision) END,
            'items',(SELECT jsonb_agg(jsonb_build_object('procurementItemId',m.procurement_item_id,'measuredValue',m.measured_value::text,'measuredUnit',m.measured_unit,'actualKg',m.actual_kg::text) ORDER BY i.line_no) FROM grainledger.ledger_arrival_measurements m JOIN grainledger.capture_procurement_items i ON i.id=m.procurement_item_id WHERE m.measurement_set_id=ms.id)) ORDER BY ms.recorded_at,ms.id) FROM grainledger.ledger_arrival_measurement_sets ms WHERE ms.procurement_id=r),'[]'::jsonb),
        'reviews',COALESCE((SELECT jsonb_agg(jsonb_build_object('id',v.id,'operationId',v.operation_id,'measurementSetId',v.measurement_set_id,'action',v.action,'reason',v.reason,'evidenceId',v.evidence_id,'managerId',v.manager_id,'recordedAt',v.recorded_at) ORDER BY v.recorded_at,v.id) FROM grainledger.ledger_arrival_reviews v WHERE v.procurement_id=r),'[]'::jsonb)) INTO result
    FROM grainledger.identity_organizations o JOIN grainledger.ledger_facilities f ON f.id=h.collection_site_id JOIN grainledger.ledger_locations l ON l.id=(snap->>'ReceivingLocationId')::uuid WHERE o."Id"=h.company_id;
    RETURN result;
END $$;

CREATE FUNCTION grainledger.ledger_receiving_restricted(l uuid) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT EXISTS (
        SELECT 1 FROM grainledger.ledger_receiving_blocks rb
        WHERE rb.blocked AND rb.lot_id=l
    ) OR EXISTS (
        SELECT 1 FROM grainledger.ledger_contributions c
        JOIN grainledger.ledger_receiving_blocks rb ON rb.lot_id=c.source_lot_id AND rb.blocked
        WHERE c.lot_id=l
    )
$$;

CREATE FUNCTION grainledger.ledger_receiving_tasks(a uuid, f uuid, p integer, n integer) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    IF p IS NULL OR n IS NULL THEN RAISE EXCEPTION 'Invalid pagination' USING ERRCODE='22023'; END IF;
    RETURN grainledger.ledger_receiving_tasks_page(a,jsonb_build_object('facilityId',f,'page',p,'pageSize',n));
END $$;

CREATE FUNCTION grainledger.ledger_receiving_tasks_page(a uuid, j jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE
    f uuid;
    owner_filter uuid;
    location_filter uuid;
    p integer;
    n integer;
    sort_key text;
    direction text;
    result jsonb;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    f:=(j->>'facilityId')::uuid; owner_filter:=(j->>'ownerId')::uuid; location_filter:=(j->>'locationId')::uuid;
    p:=coalesce((j->>'page')::integer,1); n:=coalesce((j->>'pageSize')::integer,25);
    sort_key:=coalesce(j->>'sortBy','receivedAt'); direction:=coalesce(j->>'sortDirection','desc');
    IF f IS NULL THEN RAISE EXCEPTION 'Facility required' USING ERRCODE='22023'; END IF;
    PERFORM grainledger.ledger_require_staff(a,f,ARRAY['MillOperator','MillManager']);
    IF p<1 OR n NOT BETWEEN 1 AND 100 OR (p::bigint-1)*n>2147483647 OR sort_key NOT IN ('receivedAt','status','owner') OR direction NOT IN ('asc','desc') THEN RAISE EXCEPTION 'Invalid receiving pagination or sort' USING ERRCODE='22023'; END IF;
    IF j ? 'status' AND j->>'status' NOT IN ('PENDING_ARRIVAL','AWAITING_MANAGER_REVIEW','REMEASUREMENT_REQUESTED','RECEIVED_MATCHED','RECEIVED_WITHIN_TOLERANCE','RECEIVED_SHORTAGE_APPROVED','BLOCKED_UNRESOLVED') THEN RAISE EXCEPTION 'Invalid receiving status' USING ERRCODE='22023'; END IF;
    IF j ?| ARRAY['kind','stage','lotId','ids','queueOnly'] THEN RAISE EXCEPTION 'Unsupported receiving filter' USING ERRCODE='22023'; END IF;
    WITH filtered AS MATERIALIZED (
        SELECT cp.id,cp.received_at,r.status,o."Name" owner_name
        FROM grainledger.capture_procurements cp JOIN grainledger.ledger_receiving r ON r.procurement_id=cp.id JOIN grainledger.identity_organizations o ON o."Id"=cp.company_id
        WHERE cp.collection_site_id=f AND cp.receiving_workflow IS NOT NULL
            AND (owner_filter IS NULL OR cp.company_id=owner_filter)
            AND (j->>'status' IS NULL OR r.status=j->>'status')
            AND (location_filter IS NULL OR EXISTS(SELECT 1 FROM grainledger.capture_business_events ev WHERE ev.id=cp.event_id AND ev.payload->'result'->>'ReceivingLocationId'=location_filter::text))
            AND (j->>'search' IS NULL OR concat_ws(' ',cp.code,o."Name",r.status,cp.received_at::text) ILIKE '%'||(j->>'search')||'%')
    ), ordered AS (
        SELECT x.*,row_number() OVER(ORDER BY
            CASE WHEN direction='asc' AND sort_key='receivedAt' THEN received_at END ASC NULLS LAST,
            CASE WHEN direction='desc' AND sort_key='receivedAt' THEN received_at END DESC NULLS LAST,
            CASE WHEN direction='asc' THEN CASE sort_key WHEN 'status' THEN status WHEN 'owner' THEN owner_name END END ASC NULLS LAST,
            CASE WHEN direction='desc' THEN CASE sort_key WHEN 'status' THEN status WHEN 'owner' THEN owner_name END END DESC NULLS LAST,id) ordinal FROM filtered x
    ), selected AS MATERIALIZED (
        SELECT * FROM ordered ORDER BY ordinal LIMIT n OFFSET (p::bigint-1)*n
    )
    SELECT jsonb_build_object('items',coalesce((SELECT jsonb_agg(grainledger.ledger_receiving_detail(a,x.id) ORDER BY x.ordinal) FROM selected x),'[]'::jsonb),
        'page',p,'pageSize',n,'totalCount',(SELECT count(*) FROM filtered),'hasMore',(SELECT count(*) FROM filtered)>p::bigint*n) INTO result;
    RETURN result;
END $$;

CREATE FUNCTION grainledger.ledger_receiving_threshold_command(a uuid, k text, j jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $_$
DECLARE v_company_id uuid; v_percentage numeric(8,6); v_old_percentage numeric(8,6); v_old_revision uuid; v_revision uuid:=gen_random_uuid(); v_reason text;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    IF grainledger.ledger_role(a) IS DISTINCT FROM 'Administrator' THEN RAISE EXCEPTION 'Threshold setup authority denied' USING ERRCODE='42501'; END IF;
    IF k IS NULL OR k NOT IN ('default','create','update','delete') THEN RAISE EXCEPTION 'Unsupported threshold mutation' USING ERRCODE='22023'; END IF;
    v_reason:=j->>'reason';
    IF jsonb_typeof(j->'reason') IS DISTINCT FROM 'string' OR length(trim(v_reason)) NOT BETWEEN 1 AND 4000 THEN RAISE EXCEPTION 'Threshold change reason required' USING ERRCODE='22023'; END IF;
    IF k<>'delete' THEN
        IF jsonb_typeof(j->'percentage') IS DISTINCT FROM 'string' OR j->>'percentage' !~ '^[0-9]+(\.[0-9]{1,6})?$' THEN
            RAISE EXCEPTION 'Percentage must be decimal string with at most six fractional digits' USING ERRCODE='22023';
        END IF;
        IF (j->>'percentage')::numeric<0 OR (j->>'percentage')::numeric>=100 THEN RAISE EXCEPTION 'Threshold percentage must be at least zero and below 100' USING ERRCODE='22023'; END IF;
        v_percentage:=(j->>'percentage')::numeric(8,6);
    END IF;
    -- Required default must remain valid even when an override is targeted.
    PERFORM grainledger.ledger_receiving_threshold_read(a,'settings','{}'::jsonb);
    IF k='default' THEN
        SELECT d.percentage,d.revision INTO v_old_percentage,v_old_revision FROM grainledger.ledger_receiving_threshold_default d WHERE singleton;
    ELSE
        v_company_id:=(j->>'companyId')::uuid;
        IF NOT EXISTS(SELECT 1 FROM grainledger.identity_organizations WHERE "Id"=v_company_id AND "Kind"='Customer') THEN RAISE EXCEPTION 'Customer override target required' USING ERRCODE='22023'; END IF;
        SELECT o.percentage,o.revision INTO v_old_percentage,v_old_revision FROM grainledger.ledger_receiving_threshold_overrides o WHERE company_id=v_company_id;
        IF k='create' AND FOUND THEN RAISE EXCEPTION 'Company threshold already exists' USING ERRCODE='23514'; END IF;
        IF k IN ('update','delete') AND NOT FOUND THEN RAISE EXCEPTION 'Company threshold missing or stale' USING ERRCODE='23514'; END IF;
    END IF;
    IF k<>'create' THEN
        IF jsonb_typeof(j->'version') IS DISTINCT FROM 'string' OR (j->>'version')::uuid IS DISTINCT FROM v_old_revision THEN RAISE EXCEPTION 'Threshold version stale' USING ERRCODE='23514'; END IF;
    END IF;
    INSERT INTO grainledger.ledger_receiving_threshold_history(revision,company_id,scope,action,old_percentage,new_percentage,actor_id,reason)
    VALUES(v_revision,v_company_id,CASE WHEN k='default' THEN 'SYSTEM_DEFAULT' ELSE 'COMPANY_OVERRIDE' END,
        CASE k WHEN 'create' THEN 'CREATE' WHEN 'delete' THEN 'DELETE' ELSE 'UPDATE' END,v_old_percentage,v_percentage,a,v_reason);
    IF k='default' THEN
        UPDATE grainledger.ledger_receiving_threshold_default SET percentage=v_percentage,revision=v_revision WHERE singleton;
    ELSIF k='create' THEN
        INSERT INTO grainledger.ledger_receiving_threshold_overrides(company_id,percentage,revision) VALUES(v_company_id,v_percentage,v_revision);
    ELSIF k='update' THEN
        UPDATE grainledger.ledger_receiving_threshold_overrides SET percentage=v_percentage,revision=v_revision WHERE company_id=v_company_id;
    ELSE
        DELETE FROM grainledger.ledger_receiving_threshold_overrides WHERE company_id=v_company_id;
    END IF;
    RETURN grainledger.ledger_receiving_threshold_read(a,'settings','{}'::jsonb);
END $_$;

CREATE FUNCTION grainledger.ledger_receiving_threshold_effective(c uuid) RETURNS jsonb
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE d grainledger.ledger_receiving_threshold_default; o grainledger.ledger_receiving_threshold_overrides;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    SELECT * INTO d FROM grainledger.ledger_receiving_threshold_default WHERE singleton;
    IF NOT FOUND OR grainledger.ledger_receiving_threshold_snapshot_valid(c,d.percentage,'SYSTEM_DEFAULT',d.revision) IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'Receiving threshold default missing or invalid' USING ERRCODE='23514';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM grainledger.identity_organizations WHERE "Id"=c AND "Kind"='Customer') THEN
        RAISE EXCEPTION 'Customer procurement owner required' USING ERRCODE='23514';
    END IF;
    SELECT * INTO o FROM grainledger.ledger_receiving_threshold_overrides WHERE company_id=c;
    IF FOUND THEN
        IF grainledger.ledger_receiving_threshold_snapshot_valid(c,o.percentage,'COMPANY_OVERRIDE',o.revision) IS DISTINCT FROM true THEN
            RAISE EXCEPTION 'Receiving threshold override invalid' USING ERRCODE='23514';
        END IF;
        RETURN jsonb_build_object('companyId',c,'percentage',o.percentage::text,'source','COMPANY_OVERRIDE','revision',o.revision);
    END IF;
    RETURN jsonb_build_object('companyId',c,'percentage',d.percentage::text,'source','SYSTEM_DEFAULT','revision',d.revision);
END $$;

CREATE FUNCTION grainledger.ledger_receiving_threshold_insert_guard() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE c uuid; effective jsonb;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    SELECT company_id INTO STRICT c FROM grainledger.capture_procurements WHERE id=NEW.procurement_id;
    effective:=grainledger.ledger_receiving_threshold_effective(c);
    IF NEW.threshold_company_id IS DISTINCT FROM c
       OR NEW.threshold_percentage IS DISTINCT FROM (effective->>'percentage')::numeric
       OR NEW.threshold_source IS DISTINCT FROM effective->>'source'
       OR NEW.threshold_revision IS DISTINCT FROM (effective->>'revision')::uuid
       OR grainledger.ledger_receiving_threshold_snapshot_valid(NEW.threshold_company_id,NEW.threshold_percentage,NEW.threshold_source,NEW.threshold_revision) IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'New arrival measurement requires current server-selected threshold snapshot' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END $$;

CREATE FUNCTION grainledger.ledger_receiving_threshold_read(a uuid, k text, j jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE c uuid; d grainledger.ledger_receiving_threshold_default; p integer; n integer; result jsonb; scope_filter text;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    IF k='own-company' THEN
        SELECT u."OrganizationId" INTO c FROM grainledger.identity_users u JOIN grainledger.identity_organizations o ON o."Id"=u."OrganizationId"
        WHERE u."Id"=a AND u."IsActive" AND u."Role" IN ('CustomerManager','ExportStaff','PurchasingStaff') AND o."Kind"='Customer';
        IF c IS NULL THEN RAISE EXCEPTION 'Own company threshold authority denied' USING ERRCODE='42501'; END IF;
        RETURN grainledger.ledger_receiving_threshold_effective(c);
    END IF;
    IF grainledger.ledger_role(a) IS DISTINCT FROM 'Administrator' THEN RAISE EXCEPTION 'Threshold setup authority denied' USING ERRCODE='42501'; END IF;
    IF k='settings' THEN
        SELECT * INTO d FROM grainledger.ledger_receiving_threshold_default WHERE singleton;
        IF NOT FOUND OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_receiving_threshold_history h WHERE h.revision=d.revision AND h.scope='SYSTEM_DEFAULT' AND h.new_percentage=d.percentage AND h.action IN ('INITIALIZE','UPDATE')) THEN
            RAISE EXCEPTION 'Receiving threshold default missing or invalid' USING ERRCODE='23514';
        END IF;
        IF EXISTS(SELECT 1 FROM grainledger.ledger_receiving_threshold_overrides o WHERE grainledger.ledger_receiving_threshold_snapshot_valid(o.company_id,o.percentage,'COMPANY_OVERRIDE',o.revision) IS DISTINCT FROM true) THEN
            RAISE EXCEPTION 'Receiving threshold override invalid' USING ERRCODE='23514';
        END IF;
        RETURN jsonb_build_object('systemDefault',jsonb_build_object('percentage',d.percentage::text,'version',d.revision),
            'companyOverrides',COALESCE((SELECT jsonb_agg(jsonb_build_object('companyId',o.company_id,'companyName',c."Name",'percentage',o.percentage::text,'version',o.revision) ORDER BY c."Name",o.company_id)
                FROM grainledger.ledger_receiving_threshold_overrides o JOIN grainledger.identity_organizations c ON c."Id"=o.company_id),'[]'::jsonb));
    END IF;
    p:=COALESCE((j->>'page')::integer,1); n:=COALESCE((j->>'pageSize')::integer,25);
    IF p<1 OR n NOT BETWEEN 1 AND 100 OR (p::bigint-1)*n>2147483647 THEN RAISE EXCEPTION 'Invalid threshold pagination' USING ERRCODE='22023'; END IF;
    IF k='companies' THEN
        WITH filtered AS MATERIALIZED (SELECT "Id" id,"Name" name FROM grainledger.identity_organizations WHERE "Kind"='Customer'
            AND (j->>'search' IS NULL OR "Name" ILIKE '%'||(j->>'search')||'%')),
        selected AS (SELECT * FROM filtered ORDER BY name,id LIMIT n OFFSET (p::bigint-1)*n)
        SELECT jsonb_build_object('items',COALESCE((SELECT jsonb_agg(jsonb_build_object('id',id,'name',name) ORDER BY name,id) FROM selected),'[]'::jsonb),
            'page',p,'pageSize',n,'totalCount',(SELECT count(*) FROM filtered),'hasMore',(SELECT count(*) FROM filtered)>p::bigint*n) INTO result;
        RETURN result;
    ELSIF k='history' THEN
        c:=(j->>'companyId')::uuid; scope_filter:=j->>'scope';
        IF scope_filter IS NOT NULL AND scope_filter NOT IN ('SYSTEM_DEFAULT','COMPANY_OVERRIDE') THEN RAISE EXCEPTION 'Invalid threshold history scope' USING ERRCODE='22023'; END IF;
        WITH filtered AS MATERIALIZED (SELECT h.*,o."Name" company_name,u."DisplayName" actor_name FROM grainledger.ledger_receiving_threshold_history h
            LEFT JOIN grainledger.identity_organizations o ON o."Id"=h.company_id LEFT JOIN grainledger.identity_users u ON u."Id"=h.actor_id
            WHERE (c IS NULL OR h.company_id=c) AND (scope_filter IS NULL OR h.scope=scope_filter)),
        selected AS (SELECT * FROM filtered ORDER BY recorded_at DESC,revision DESC LIMIT n OFFSET (p::bigint-1)*n)
        SELECT jsonb_build_object('items',COALESCE((SELECT jsonb_agg(jsonb_build_object('revision',revision,'companyId',company_id,'companyName',company_name,
                'scope',scope,'action',action,'oldPercentage',old_percentage::text,'newPercentage',new_percentage::text,'actorId',actor_id,'actorName',actor_name,
                'recordedAt',recorded_at,'reason',reason,'migrationId',migration_id) ORDER BY recorded_at DESC,revision DESC) FROM selected),'[]'::jsonb),
            'page',p,'pageSize',n,'totalCount',(SELECT count(*) FROM filtered),'hasMore',(SELECT count(*) FROM filtered)>p::bigint*n) INTO result;
        RETURN result;
    END IF;
    RAISE EXCEPTION 'Unsupported threshold read' USING ERRCODE='22023';
END $$;

CREATE FUNCTION grainledger.ledger_receiving_threshold_snapshot_valid(c uuid, p numeric, s text, r uuid) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT c IS NOT NULL AND p IS NOT NULL AND s IS NOT NULL AND r IS NOT NULL
        AND EXISTS(SELECT 1 FROM grainledger.ledger_receiving_threshold_history h WHERE h.revision=r AND h.scope=s
            AND h.new_percentage=p AND h.action<>'DELETE'
            AND ((s='SYSTEM_DEFAULT' AND h.company_id IS NULL) OR (s='COMPANY_OVERRIDE' AND h.company_id=c)))
$$;

CREATE FUNCTION grainledger.ledger_record_in_use(resource text, record_id uuid) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT CASE resource
        WHEN 'facilities' THEN
            EXISTS(SELECT 1 FROM grainledger.ledger_locations WHERE facility_id=record_id AND NOT is_deleted)
            OR EXISTS(SELECT 1 FROM grainledger.ledger_equipment WHERE facility_id=record_id AND NOT is_deleted)
            OR EXISTS(SELECT 1 FROM grainledger.ledger_assignments WHERE facility_id=record_id AND NOT is_deleted)
            OR EXISTS(SELECT 1 FROM grainledger.ledger_evidence WHERE facility_id=record_id)
            OR EXISTS(SELECT 1 FROM grainledger.ledger_policies WHERE facility_id=record_id)
            OR EXISTS(SELECT 1 FROM grainledger.ledger_compatibilities WHERE facility_id=record_id)
            OR EXISTS(SELECT 1 FROM grainledger.ledger_operations WHERE facility_id=record_id AND NOT is_deleted)
            OR EXISTS(SELECT 1 FROM grainledger.capture_procurements WHERE collection_site_id=record_id)
        WHEN 'locations' THEN
            EXISTS(SELECT 1 FROM grainledger.ledger_positions WHERE location_id=record_id)
            OR EXISTS(SELECT 1 FROM grainledger.ledger_postings WHERE location_id=record_id)
            OR EXISTS(SELECT 1 FROM grainledger.ledger_operations op WHERE NOT op.is_deleted AND (
                EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'inputs') x WHERE (x->>'locationId')::uuid=record_id)
                OR EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'outputs') x WHERE (x->>'locationId')::uuid=record_id)))
            OR EXISTS(SELECT 1 FROM grainledger.capture_business_events ev WHERE ev.event_type='PROCUREMENT_COMMITTED'
                AND (ev.payload->'result'->>'ReceivingLocationId')::uuid=record_id)
            OR EXISTS(SELECT 1 FROM grainledger.ledger_events ev WHERE CASE ev.kind
                WHEN 'CLEARANCE' THEN translate(lower(ev.details->>'id'),'-{}','')=replace(record_id::text,'-','')
                WHEN 'TRANSFER' THEN translate(lower(ev.details->>'fromLocationId'),'-{}','')=replace(record_id::text,'-','')
                    OR translate(lower(ev.details->>'toLocationId'),'-{}','')=replace(record_id::text,'-','')
                ELSE false END)
            OR EXISTS(SELECT 1 FROM grainledger.ledger_events ev WHERE
                EXISTS(SELECT 1 FROM jsonb_array_elements(CASE WHEN ev.kind='VARIANCE_CONFIRMATION'
                    AND jsonb_typeof(ev.details->'reviewedContent'->'inputs')='array' THEN ev.details->'reviewedContent'->'inputs' ELSE '[]'::jsonb END) x
                    WHERE translate(lower(x->>'locationId'),'-{}','')=replace(record_id::text,'-',''))
                OR EXISTS(SELECT 1 FROM jsonb_array_elements(CASE WHEN ev.kind='VARIANCE_CONFIRMATION'
                    AND jsonb_typeof(ev.details->'reviewedContent'->'outputs')='array' THEN ev.details->'reviewedContent'->'outputs' ELSE '[]'::jsonb END) x
                    WHERE translate(lower(x->>'locationId'),'-{}','')=replace(record_id::text,'-',''))
                OR EXISTS(SELECT 1 FROM jsonb_array_elements(CASE WHEN ev.kind='PHYSICAL_RECONCILIATION'
                    AND jsonb_typeof(ev.details->'reconciliation')='array' THEN ev.details->'reconciliation' ELSE '[]'::jsonb END) x
                    WHERE translate(lower(x->>'locationId'),'-{}','')=replace(record_id::text,'-','')))
            OR EXISTS(SELECT 1 FROM grainledger.ledger_corrections c WHERE
                EXISTS(SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(c.replacement_content->'inputs')='array' THEN c.replacement_content->'inputs' ELSE '[]'::jsonb END) x
                    WHERE translate(lower(x->>'locationId'),'-{}','')=replace(record_id::text,'-',''))
                OR EXISTS(SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(c.replacement_content->'outputs')='array' THEN c.replacement_content->'outputs' ELSE '[]'::jsonb END) x
                    WHERE translate(lower(x->>'locationId'),'-{}','')=replace(record_id::text,'-',''))
                OR EXISTS(SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(c.reconciliation)='array' THEN c.reconciliation ELSE '[]'::jsonb END) x
                    WHERE translate(lower(x->>'locationId'),'-{}','')=replace(record_id::text,'-','')))
        WHEN 'equipment' THEN EXISTS(SELECT 1 FROM grainledger.ledger_operations WHERE equipment_id=record_id AND NOT is_deleted)
            OR EXISTS(SELECT 1 FROM grainledger.ledger_events WHERE kind='VARIANCE_CONFIRMATION'
                AND translate(lower(details->'reviewedContent'->>'equipmentId'),'-{}','')=replace(record_id::text,'-',''))
            OR EXISTS(SELECT 1 FROM grainledger.ledger_corrections WHERE translate(lower(replacement_content->>'equipmentId'),'-{}','')=replace(record_id::text,'-',''))
        WHEN 'operations' THEN
            EXISTS(SELECT 1 FROM grainledger.ledger_lots WHERE operation_id=record_id)
            OR EXISTS(SELECT 1 FROM grainledger.ledger_corrections WHERE operation_id=record_id OR replacement_operation_id=record_id)
            OR EXISTS(SELECT 1 FROM grainledger.ledger_events WHERE operation_id=record_id
                OR kind IN ('PHYSICAL_RECONCILIATION','RECONCILIATION_INVALIDATED')
                AND translate(lower(details->>'replacementOperationId'),'-{}','')=replace(record_id::text,'-',''))
        ELSE true
    END
$$;

CREATE FUNCTION grainledger.ledger_register_code(v_code text, new_lot uuid, original_receipt uuid DEFAULT NULL::uuid, original_operation uuid DEFAULT NULL::uuid) RETURNS uuid
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE origin uuid; reserved grainledger.ledger_lot_code_reservations;
BEGIN
    SELECT * INTO reserved FROM grainledger.ledger_lot_code_reservations WHERE code=v_code;
    IF FOUND AND reserved.lot_id<>new_lot THEN
        IF reserved.context_kind<>'OPERATION' OR original_operation IS NULL OR NOT EXISTS(
            SELECT 1 FROM grainledger.ledger_lots old
            JOIN grainledger.ledger_corrections c ON c.operation_id=old.operation_id
            JOIN grainledger.ledger_operations replacement ON replacement.id=c.replacement_operation_id,
                jsonb_array_elements(replacement.content->'outputs') output
            WHERE old.id=reserved.lot_id AND old.code=v_code AND old.operation_id=original_operation
              AND c.status='RECONCILED' AND (output->>'id')::uuid=new_lot
              AND (output->>'originalLotId')::uuid=old.id
              AND output->>'stage'=reserved.stage AND output->>'productIdentity'=reserved.product_identity
        ) THEN
            RAISE EXCEPTION 'Lot code is reserved for another lineage or product' USING ERRCODE='23505';
        END IF;
    END IF;
    SELECT origin_lot_id INTO origin FROM grainledger.ledger_lot_codes WHERE code=v_code;
    IF origin IS NULL THEN
        INSERT INTO grainledger.ledger_lot_codes VALUES(v_code,new_lot);
        RETURN new_lot;
    END IF;
    IF EXISTS (
        SELECT 1 FROM grainledger.ledger_lots old
        WHERE old.code=v_code AND old.code_origin_id=origin AND (
            original_receipt IS NOT NULL AND old.receipt_id=original_receipt
            OR original_operation IS NOT NULL AND old.operation_id=original_operation
        )
    ) THEN RETURN origin; END IF;
    RAISE EXCEPTION 'Lot code belongs to unrelated provenance; only correction lineage may reuse it'
        USING ERRCODE='23505';
END $$;

CREATE FUNCTION grainledger.ledger_register_pending_procurement(a uuid, r uuid) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE h grainledger.capture_procurements; snapshot jsonb;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    SELECT * INTO STRICT h FROM grainledger.capture_procurements WHERE id=r AND receiving_workflow='PHYSICAL_ARRIVAL_V1';
    IF grainledger.ledger_role(a) IS DISTINCT FROM 'PurchasingStaff' OR NOT grainledger.ledger_own(a,h.company_id) OR h.received_by<>a THEN RAISE EXCEPTION 'Procurement company authority denied' USING ERRCODE='42501'; END IF;
    IF NOT EXISTS(SELECT 1 FROM grainledger.identity_organizations WHERE "Id"=h.company_id AND "Kind"='Customer') THEN RAISE EXCEPTION 'Customer procurement owner required' USING ERRCODE='42501'; END IF;
    SELECT payload->'result' INTO snapshot FROM grainledger.capture_business_events WHERE id=h.event_id;
    IF NOT EXISTS(SELECT 1 FROM grainledger.catalog_commodities WHERE id=(snapshot->>'CommodityId')::uuid AND code='PADDY' AND NOT is_archived) THEN
        RAISE EXCEPTION 'Active PADDY commodity required for new procurement' USING ERRCODE='23514';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_locations WHERE id=(snapshot->>'ReceivingLocationId')::uuid AND facility_id=h.collection_site_id AND NOT is_deleted
        AND kind IN ('STORE','SILO','COMPARTMENT') AND (owner_id=h.company_id OR owner_id IS NULL AND (kind<>'COMPARTMENT' OR cleared)))
        OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_facilities WHERE id=h.collection_site_id AND NOT is_deleted) THEN
        RAISE EXCEPTION 'Receiving location no longer eligible' USING ERRCODE='23514';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM grainledger.capture_farmers WHERE id=h.farmer_id AND company_id=h.company_id AND NOT is_deleted)
        OR EXISTS(SELECT 1 FROM grainledger.capture_procurement_items i WHERE i.procurement_id=r AND NOT EXISTS(
            SELECT 1 FROM grainledger.capture_plot_seasons ps JOIN grainledger.capture_plots p ON p.id=ps.plot_id AND p.company_id=ps.company_id
            JOIN grainledger.capture_crop_seasons season ON season.id=ps.season_id AND season.company_id=ps.company_id
            WHERE ps.id=i.plot_season_id AND ps.company_id=h.company_id AND p.farmer_id=h.farmer_id
                AND NOT ps.is_deleted AND NOT p.is_deleted AND NOT season.is_deleted
        )) THEN
        RAISE EXCEPTION 'Active farmer, plot and season references required for new procurement' USING ERRCODE='23514';
    END IF;
    IF EXISTS(SELECT 1 FROM grainledger.capture_procurement_items i WHERE i.procurement_id=r AND NOT EXISTS(
        SELECT 1 FROM grainledger.catalog_measurement_units u WHERE u.code=i.measured_unit AND u.quantity_kind='MASS' AND NOT u.is_deleted
    )) THEN
        RAISE EXCEPTION 'Active mass units required for new procurement' USING ERRCODE='23514';
    END IF;
    INSERT INTO grainledger.ledger_receiving(procurement_id,status) VALUES(r,'PENDING_ARRIVAL');
    IF grainledger.ledger_procurement_package_complete(r) IS DISTINCT FROM true THEN RAISE EXCEPTION 'Pending source package incomplete' USING ERRCODE='23514'; END IF;
END $$;

CREATE FUNCTION grainledger.ledger_require_active_location(location_id uuid) RETURNS void
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_locations l JOIN grainledger.ledger_facilities f ON f.id=l.facility_id
        WHERE l.id=location_id AND NOT l.is_deleted AND NOT f.is_deleted) THEN
        RAISE EXCEPTION 'Active location and facility required' USING ERRCODE='23514';
    END IF;
END $$;

CREATE FUNCTION grainledger.ledger_require_active_operation(operation_id uuid) RETURNS void
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE op grainledger.ledger_operations; item jsonb;
BEGIN
    SELECT * INTO STRICT op FROM grainledger.ledger_operations WHERE id=operation_id;
    IF op.is_deleted OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_facilities WHERE id=op.facility_id AND NOT is_deleted)
        OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_equipment WHERE id=op.equipment_id AND facility_id=op.facility_id AND NOT is_deleted) THEN
        RAISE EXCEPTION 'Active operation, facility and equipment required' USING ERRCODE='23514';
    END IF;
    FOR item IN SELECT value FROM jsonb_array_elements(op.content->'inputs') UNION ALL
                SELECT value FROM jsonb_array_elements(op.content->'outputs') LOOP
        PERFORM grainledger.ledger_require_active_location((item->>'locationId')::uuid);
    END LOOP;
    IF EXISTS(SELECT 1 FROM jsonb_array_elements(op.content->'outputs') x
        JOIN grainledger.ledger_catalog_commodities map ON map.ledger_commodity_id=(x->>'commodityId')::uuid
        JOIN grainledger.catalog_commodities c ON c.id=map.catalog_commodity_id WHERE c.is_archived) THEN
        RAISE EXCEPTION 'Active output commodities required' USING ERRCODE='23514';
    END IF;
END $$;

CREATE FUNCTION grainledger.ledger_require_evidence(a uuid, e uuid, o uuid, f uuid) RETURNS void
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM grainledger.ledger_evidence WHERE id=e AND facility_id=f AND owner_id=o)
       OR NOT EXISTS(SELECT 1 FROM grainledger.ledger_facilities WHERE id=f AND NOT is_deleted) THEN
        RAISE EXCEPTION 'Evidence missing or outside owner/facility scope' USING ERRCODE='42501';
    END IF;
END $$;

CREATE FUNCTION grainledger.ledger_require_mixed_correction_safe(action text, p jsonb) RETURNS void
    LANGUAGE plpgsql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE
    target_operation uuid;
    correction_id uuid;
    replacement_operation uuid;
BEGIN
    IF action='correction_request' THEN
        target_operation:=(p->>'operationId')::uuid;
    ELSIF p ? '_correctionId' THEN
        correction_id:=(p->>'_correctionId')::uuid;
        SELECT operation_id INTO target_operation FROM grainledger.ledger_corrections WHERE id=correction_id;
    ELSE
        RETURN;
    END IF;
    IF action IN ('correction_reconcile','correction_commit') THEN
        replacement_operation:=NULLIF(p->>'replacementOperationId','')::uuid;
        IF replacement_operation IS NULL AND correction_id IS NOT NULL THEN
            SELECT replacement_operation_id INTO replacement_operation
            FROM grainledger.ledger_corrections WHERE id=correction_id;
        END IF;
    END IF;

    IF target_operation IS NULL THEN
        RETURN;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM grainledger.ledger_operations op
        CROSS JOIN LATERAL jsonb_array_elements(op.content->'outputs') output(value)
        WHERE op.id=target_operation AND (
            EXISTS (SELECT 1 FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.lot_id=(output.value->>'id')::uuid)
            OR EXISTS (SELECT 1 FROM grainledger.ledger_custody_edges edge
                       WHERE edge.input_lot_id=(output.value->>'id')::uuid
                          OR edge.output_lot_id=(output.value->>'id')::uuid)
            OR grainledger.ledger_mixed_bulk_custody_derived((output.value->>'id')::uuid)
            OR EXISTS (
                SELECT 1 FROM jsonb_array_elements(op.content->'outputs') item(value)
                JOIN grainledger.ledger_locations loc ON loc.id=(item.value->>'locationId')::uuid
                WHERE loc.mixed_bulk
            )
        )
    ) OR EXISTS (
        SELECT 1
        FROM grainledger.ledger_operations op
        CROSS JOIN LATERAL jsonb_array_elements(op.content->'inputs') input(value)
        WHERE op.id=target_operation AND (
            EXISTS (SELECT 1 FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.lot_id=(input.value->>'lotId')::uuid)
            OR EXISTS (SELECT 1 FROM grainledger.ledger_custody_edges edge
                       WHERE edge.input_lot_id=(input.value->>'lotId')::uuid
                          OR edge.output_lot_id=(input.value->>'lotId')::uuid)
            OR grainledger.ledger_mixed_bulk_custody_derived((input.value->>'lotId')::uuid)
            OR EXISTS (
                SELECT 1 FROM jsonb_array_elements(op.content->'inputs') item(value)
                JOIN grainledger.ledger_locations loc ON loc.id=(item.value->>'locationId')::uuid
                WHERE loc.mixed_bulk
            )
        )
    ) OR replacement_operation IS NOT NULL AND (
        EXISTS (
            SELECT 1 FROM grainledger.ledger_operations op
            CROSS JOIN LATERAL jsonb_array_elements(op.content->'outputs') output(value)
            WHERE op.id=replacement_operation AND (
                EXISTS (SELECT 1 FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.lot_id=(output.value->>'id')::uuid)
                OR EXISTS (SELECT 1 FROM grainledger.ledger_custody_edges edge
                           WHERE edge.input_lot_id=(output.value->>'id')::uuid
                              OR edge.output_lot_id=(output.value->>'id')::uuid)
                OR grainledger.ledger_mixed_bulk_custody_derived((output.value->>'id')::uuid)
                OR EXISTS (SELECT 1 FROM grainledger.ledger_locations loc
                           WHERE loc.id=(output.value->>'locationId')::uuid AND loc.mixed_bulk)
            )
        ) OR EXISTS (
            SELECT 1 FROM grainledger.ledger_operations op
            CROSS JOIN LATERAL jsonb_array_elements(op.content->'inputs') input(value)
            WHERE op.id=replacement_operation AND (
                EXISTS (SELECT 1 FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.lot_id=(input.value->>'lotId')::uuid)
                OR EXISTS (SELECT 1 FROM grainledger.ledger_custody_edges edge
                           WHERE edge.input_lot_id=(input.value->>'lotId')::uuid
                              OR edge.output_lot_id=(input.value->>'lotId')::uuid)
                OR grainledger.ledger_mixed_bulk_custody_derived((input.value->>'lotId')::uuid)
                OR EXISTS (SELECT 1 FROM grainledger.ledger_locations loc
                           WHERE loc.id=(input.value->>'locationId')::uuid AND loc.mixed_bulk)
            )
        )
    ) THEN
        RAISE EXCEPTION 'Mixed-bulk custody correction unsupported; downstream custody or changed generation prevents compensation'
            USING ERRCODE='23514';
    END IF;
END $$;

CREATE FUNCTION grainledger.ledger_require_staff(a uuid, f uuid, roles text[]) RETURNS void
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    IF grainledger.ledger_role(a) IS NULL OR NOT (grainledger.ledger_role(a)=ANY(roles)) OR NOT grainledger.ledger_assigned(a,f) THEN
        RAISE EXCEPTION 'Facility or operation authority denied' USING ERRCODE='42501';
    END IF;
END $$;

CREATE FUNCTION grainledger.ledger_reserve_lot_code_internal(v_lot uuid, v_owner uuid, v_kind text, v_context uuid, v_stage text, v_product text) RETURNS text
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE old grainledger.ledger_lot_code_reservations; token text; candidate text; serial bigint;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    IF v_lot IS NULL OR v_lot='00000000-0000-0000-0000-000000000000' OR v_owner IS NULL
       OR v_context IS NULL OR v_kind NOT IN ('SOURCE','OPERATION','CUSTODY') THEN
        RAISE EXCEPTION 'Lot code context is incomplete' USING ERRCODE='22023';
    END IF;
    token:=grainledger.ledger_lot_code_token(v_stage,v_product);
    IF token IS NULL THEN RAISE EXCEPTION 'Unsupported lot stage and product identity' USING ERRCODE='23514'; END IF;
    SELECT * INTO old FROM grainledger.ledger_lot_code_reservations WHERE lot_id=v_lot;
    IF FOUND THEN
        IF old.owner_id<>v_owner OR old.context_kind<>v_kind OR old.context_id<>v_context
           OR old.stage<>v_stage OR old.product_identity<>v_product THEN
            RAISE EXCEPTION 'Lot identifier already reserved for different provenance or product' USING ERRCODE='23514';
        END IF;
        RETURN old.code;
    END IF;
    IF EXISTS(SELECT 1 FROM grainledger.ledger_lots WHERE id=v_lot) THEN
        RAISE EXCEPTION 'Lot identifier already has stock history' USING ERRCODE='23514';
    END IF;
    LOOP
        serial:=nextval('grainledger.ledger_lot_code_serial');
        candidate:=token||'-'||to_char(clock_timestamp() AT TIME ZONE 'Asia/Ho_Chi_Minh','YYYYMMDD')||'-'
            ||lpad(serial::text,greatest(6,length(serial::text)),'0');
        EXIT WHEN NOT EXISTS(SELECT 1 FROM grainledger.ledger_lot_codes WHERE code=candidate)
              AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_lot_code_reservations WHERE code=candidate);
    END LOOP;
    INSERT INTO grainledger.ledger_lot_code_reservations(lot_id,code,owner_id,context_kind,context_id,stage,product_identity)
    VALUES(v_lot,candidate,v_owner,v_kind,v_context,v_stage,v_product);
    RETURN candidate;
END $$;

CREATE FUNCTION grainledger.ledger_reserve_source_lot_code(a uuid, o uuid, r uuid, v_lot uuid) RETURNS text
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    IF grainledger.ledger_role(a) IS DISTINCT FROM 'PurchasingStaff' OR NOT grainledger.ledger_own(a,o)
       OR NOT EXISTS(SELECT 1 FROM grainledger.identity_organizations WHERE "Id"=o AND "Kind"='Customer')
       OR EXISTS(SELECT 1 FROM grainledger.capture_procurements WHERE id=r)
       OR EXISTS(SELECT 1 FROM grainledger.capture_source_lots WHERE lot_id=v_lot)
    THEN RAISE EXCEPTION 'Source lot code authority or identifier unavailable' USING ERRCODE='42501'; END IF;
    RETURN grainledger.ledger_reserve_lot_code_internal(v_lot,o,'SOURCE',r,'RAW_PADDY','RAW_PADDY');
END $$;

CREATE FUNCTION grainledger.ledger_residue_append(a uuid, l uuid, evaluation uuid, p jsonb, s jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE old jsonb; persisted timestamptz := clock_timestamp();
BEGIN
    IF current_setting('transaction_isolation')<>'read committed' THEN
        RAISE EXCEPTION 'Residue persistence requires READ COMMITTED isolation' USING ERRCODE='23514';
    END IF;
    -- Authorization mutations use this same lock; recheck their committed state after waiting.
    PERFORM grainledger.ledger_lock_writes();
    old:=grainledger.ledger_residue_replay(a,l,evaluation,p);
    IF old IS NOT NULL THEN RETURN old; END IF;
    persisted:=clock_timestamp();
    IF s->>'id' IS DISTINCT FROM evaluation::text OR s->>'lotId' IS DISTINCT FROM l::text
       OR s->>'companyId' IS DISTINCT FROM (SELECT owner_id::text FROM grainledger.ledger_lots WHERE id=l) THEN
        RAISE EXCEPTION 'Residue snapshot identity mismatch' USING ERRCODE='23514';
    END IF;
    s:=s||jsonb_build_object('persistedAt',persisted);
    INSERT INTO grainledger.ledger_residue_evaluations(id,lot_id,actor_id,request,snapshot,persisted_at)
    VALUES(evaluation,l,a,p,s,persisted);
    RETURN s;
END $$;

CREATE FUNCTION grainledger.ledger_residue_authorized(a uuid, l uuid) RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT COALESCE(grainledger.ledger_role(a) IN ('CustomerManager','ExportStaff','MillManager','MillOperator')
        AND grainledger.ledger_can_read_lot(a,l),false)
$$;

CREATE FUNCTION grainledger.ledger_residue_graph(a uuid, target uuid) RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE result jsonb;
BEGIN
    IF NOT grainledger.ledger_residue_authorized(a,target) THEN
        RAISE EXCEPTION 'Residue lot access denied' USING ERRCODE='42501';
    END IF;
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

CREATE FUNCTION grainledger.ledger_residue_history(a uuid, l uuid, evaluation uuid DEFAULT NULL::uuid) RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE result jsonb;
BEGIN
    IF NOT grainledger.ledger_residue_authorized(a,l) THEN
        RAISE EXCEPTION 'Residue lot access denied' USING ERRCODE='42501';
    END IF;
    IF evaluation IS NOT NULL THEN
        SELECT snapshot INTO result FROM grainledger.ledger_residue_evaluations WHERE id=evaluation AND lot_id=l;
        IF result IS NULL THEN RAISE EXCEPTION 'Residue evaluation not found' USING ERRCODE='P0002'; END IF;
        RETURN result;
    END IF;
    SELECT jsonb_build_object('items',COALESCE(jsonb_agg(snapshot ORDER BY persisted_at DESC,id),'[]'::jsonb)) INTO result
    FROM grainledger.ledger_residue_evaluations WHERE lot_id=l;
    RETURN result;
END $$;

CREATE FUNCTION grainledger.ledger_residue_replay(a uuid, l uuid, evaluation uuid, p jsonb) RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE old grainledger.ledger_residue_evaluations;
BEGIN
    IF NOT grainledger.ledger_residue_authorized(a,l) THEN RAISE EXCEPTION 'Residue lot access denied' USING ERRCODE='42501'; END IF;
    SELECT * INTO old FROM grainledger.ledger_residue_evaluations WHERE id=evaluation;
    IF NOT FOUND THEN RETURN NULL; END IF;
    IF old.actor_id<>a OR old.lot_id<>l OR old.request<>p THEN
        RAISE EXCEPTION 'Residue evaluation ID reused with different actor or payload' USING ERRCODE='23505';
    END IF;
    RETURN old.snapshot;
END $$;

CREATE FUNCTION grainledger.ledger_review_replacement(a uuid, corr uuid, replacement uuid, reviewed uuid, physical_positions jsonb) RETURNS void
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE
    original grainledger.ledger_operations;
    replacement_draft grainledger.ledger_operations;
    output jsonb;
    output_facility uuid;
BEGIN
    SELECT op.* INTO STRICT original
    FROM grainledger.ledger_corrections c JOIN grainledger.ledger_operations op ON op.id=c.operation_id
    WHERE c.id=corr;
    PERFORM grainledger.ledger_require_active_operation(replacement);
    SELECT * INTO STRICT replacement_draft FROM grainledger.ledger_operations
    WHERE id=replacement FOR UPDATE;
    PERFORM grainledger.ledger_require_staff(a,original.facility_id,ARRAY['MillManager']);

    -- Physical evidence reviews exact replacement content, never an inferred latest version.
    IF replacement_draft.status<>'DRAFT'
       OR replacement_draft.owner_id<>original.owner_id
       OR replacement_draft.facility_id<>original.facility_id
       OR replacement_draft.version IS DISTINCT FROM reviewed THEN
        RAISE EXCEPTION 'Replacement review stale or outside original owner/facility'
            USING ERRCODE='23514';
    END IF;
    FOR output IN SELECT value FROM jsonb_array_elements(replacement_draft.content->'outputs') LOOP
        IF NOT EXISTS (
            SELECT 1 FROM jsonb_array_elements(physical_positions) pos
            WHERE pos->>'locationId'=output->>'locationId'
        ) THEN
            RAISE EXCEPTION 'Replacement must retain reconciled current physical locations'
                USING ERRCODE='23514';
        END IF;
        SELECT facility_id INTO STRICT output_facility FROM grainledger.ledger_locations
        WHERE id=(output->>'locationId')::uuid;
        PERFORM grainledger.ledger_require_staff(a,output_facility,ARRAY['MillManager']);
    END LOOP;
    UPDATE grainledger.ledger_corrections
    SET replacement_operation_id=replacement_draft.id,
        replacement_version=replacement_draft.version,
        replacement_content=replacement_draft.content
    WHERE id=corr;
END $$;

CREATE FUNCTION grainledger.ledger_role(a uuid) RETURNS text
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT "Role" FROM grainledger.identity_users WHERE "Id"=a AND "IsActive"
$$;

CREATE FUNCTION grainledger.ledger_source_lot_code(r uuid, v_lot uuid) RETURNS text
    LANGUAGE plpgsql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE reserved text;
BEGIN
    SELECT code INTO reserved FROM grainledger.ledger_lot_code_reservations
    WHERE lot_id=v_lot AND context_kind='SOURCE' AND context_id=r
      AND stage='RAW_PADDY' AND product_identity='RAW_PADDY';
    IF reserved IS NOT NULL THEN RETURN reserved; END IF;
    RAISE EXCEPTION 'Source lot is missing its reserved code' USING ERRCODE='23514';
END $$;

CREATE FUNCTION grainledger.ledger_trace_lot(a uuid, target uuid, direction text, view_mode text) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE
    actor_role text;
    owner uuid;
    result jsonb;
    trace_id uuid:=gen_random_uuid();
BEGIN
    actor_role:=grainledger.ledger_role(a);
    IF actor_role IS NULL OR actor_role NOT IN ('CustomerManager','ExportStaff','Administrator') THEN
        RAISE EXCEPTION 'Trace permission required' USING ERRCODE='42501';
    END IF;
    IF direction NOT IN ('forward','backward') OR view_mode NOT IN ('current','history')
       OR direction IS NULL OR view_mode IS NULL THEN
        RAISE EXCEPTION 'Unsupported trace direction or view' USING ERRCODE='22023';
    END IF;
    IF direction='backward' AND actor_role='ExportStaff' THEN
        RAISE EXCEPTION 'Source-contribution trace requires Customer Manager authority' USING ERRCODE='42501';
    END IF;
    SELECT owner_id INTO owner FROM grainledger.ledger_lots WHERE id=target;
    -- Same error for an absent or inaccessible identity. Pending sources are not Ledger lots.
    IF owner IS NULL OR (actor_role<>'Administrator' AND NOT grainledger.ledger_own(a,owner)) THEN
        RAISE EXCEPTION 'Trace lot unavailable in current scope; use an accepted Ledger lot' USING ERRCODE='42501';
    END IF;

    -- One statement captures graph, stock, restrictions and retained source weights together.
    WITH RECURSIVE
    actor AS MATERIALIZED (
        SELECT u."Role" role FROM grainledger.identity_users u
        WHERE u."Id"=a AND u."IsActive" AND u."Role"=actor_role
          AND (u."Role"='Administrator' OR u."OrganizationId"=owner)
    ),
    operations AS MATERIALIZED (
        SELECT op.* FROM grainledger.ledger_operations op
        WHERE op.owner_id=owner AND op.status IN ('COMMITTED','CORRECTED')
          AND (view_mode='history' OR op.status='COMMITTED')
          AND EXISTS(SELECT 1 FROM actor)
    ),
    processing_inputs AS MATERIALIZED (
        -- Commit evidence resolves pool drafts to actual aliquots. Retain structural
        -- input/output pairs even when their allocated source quantity rounded to zero.
        SELECT op.id operation_id, ev.id event_id,
            CASE WHEN ev.details ? 'resolvedInputs' THEN 'RESOLVED_INPUTS' ELSE 'RECORDED_CONTENT' END basis,
            i.value
        FROM operations op
        LEFT JOIN LATERAL (
            SELECT e.id,e.details FROM grainledger.ledger_events e
            WHERE e.operation_id=op.id AND e.kind=op.kind AND e.details ? 'resolvedInputs'
            ORDER BY e.occurred_at,e.id LIMIT 1
        ) ev ON true
        CROSS JOIN LATERAL jsonb_array_elements(COALESCE(ev.details->'resolvedInputs',op.content->'inputs')) i
    ),
    edges AS MATERIALIZED (
        SELECT 'PROCESSING'::text relation_type,op.id relation_id,op.id operation_id,pi.event_id,
               pi.basis,(pi.value->>'lotId')::uuid input_lot_id,
               (o.value->>'id')::uuid output_lot_id,
               sum((pi.value->>'quantityKg')::numeric) input_kg,
               (o.value->>'quantityKg')::numeric output_kg
        FROM operations op JOIN processing_inputs pi ON pi.operation_id=op.id
        CROSS JOIN LATERAL jsonb_array_elements(op.content->'outputs') o
        JOIN grainledger.ledger_lots il ON il.id=(pi.value->>'lotId')::uuid AND il.owner_id=owner
        JOIN grainledger.ledger_lots ol ON ol.id=(o.value->>'id')::uuid AND ol.owner_id=owner AND ol.operation_id=op.id
        GROUP BY op.id,pi.event_id,pi.basis,(pi.value->>'lotId')::uuid,(o.value->>'id')::uuid,(o.value->>'quantityKg')::numeric
        UNION ALL
        SELECT 'CUSTODY',ce.event_id,NULL::uuid,ce.event_id,'CUSTODY_ALLOCATION',ce.input_lot_id,ce.output_lot_id,
               ce.quantity_kg,(SELECT sum(c.quantity_kg) FROM grainledger.ledger_contributions c WHERE c.lot_id=ce.output_lot_id)
        FROM grainledger.ledger_custody_edges ce
        JOIN grainledger.ledger_lots il ON il.id=ce.input_lot_id AND il.owner_id=owner
        JOIN grainledger.ledger_lots ol ON ol.id=ce.output_lot_id AND ol.owner_id=owner
        WHERE EXISTS(SELECT 1 FROM actor)
    ),
    reached(id) AS (
        SELECT target WHERE EXISTS(SELECT 1 FROM actor)
        UNION
        SELECT CASE WHEN direction='forward' THEN e.output_lot_id ELSE e.input_lot_id END
        FROM reached r JOIN edges e ON
            (direction='forward' AND e.input_lot_id=r.id)
            OR (direction='backward' AND e.output_lot_id=r.id)
    ),
    trace_edges AS MATERIALIZED (
        SELECT e.* FROM edges e JOIN reached i ON i.id=e.input_lot_id JOIN reached o ON o.id=e.output_lot_id
    ),
    allocations AS MATERIALIZED (
        SELECT e.relation_type,e.relation_id,e.input_lot_id,e.output_lot_id,ca.source_lot_id,ca.quantity_kg
        FROM trace_edges e JOIN grainledger.ledger_custody_allocations ca
          ON e.relation_type='CUSTODY' AND ca.event_id=e.event_id
          AND ca.input_lot_id=e.input_lot_id AND ca.output_lot_id=e.output_lot_id
        JOIN grainledger.ledger_lots source ON source.id=ca.source_lot_id AND source.owner_id=owner
        WHERE actor_role<>'ExportStaff'
        UNION ALL
        SELECT e.relation_type,e.relation_id,e.input_lot_id,e.output_lot_id,pa.source_lot_id,pa.quantity_kg
        FROM trace_edges e JOIN grainledger.ledger_custody_processing_allocations pa
          ON e.relation_type='PROCESSING' AND pa.event_id=e.event_id AND pa.operation_id=e.operation_id
          AND pa.input_lot_id=e.input_lot_id AND pa.output_lot_id=e.output_lot_id
        JOIN grainledger.ledger_lots source ON source.id=pa.source_lot_id AND source.owner_id=owner
        WHERE actor_role<>'ExportStaff'
    ),
    positive_paths(lot_id,source_lot_id) AS (
        SELECT target,c.source_lot_id FROM grainledger.ledger_contributions c
        JOIN grainledger.ledger_lots source ON source.id=c.source_lot_id AND source.owner_id=owner
        WHERE c.lot_id=target AND c.quantity_kg>0 AND actor_role<>'ExportStaff'
        UNION
        SELECT CASE WHEN direction='forward' THEN allocation.output_lot_id ELSE allocation.input_lot_id END,path.source_lot_id
        FROM positive_paths path JOIN allocations allocation
          ON allocation.source_lot_id=path.source_lot_id AND allocation.quantity_kg>0
          AND ((direction='forward' AND allocation.input_lot_id=path.lot_id)
            OR (direction='backward' AND allocation.output_lot_id=path.lot_id))
    ),
    lot_metadata AS MATERIALIZED (SELECT * FROM grainledger.ledger_trace_lot_metadata(a,(SELECT array_agg(id) FROM reached))),
    source_weights AS MATERIALIZED (
        SELECT c.* FROM grainledger.ledger_contributions c JOIN reached r ON r.id=c.lot_id
        WHERE actor_role<>'ExportStaff'
    )
    SELECT jsonb_build_object(
        'schemaVersion',2,'graphBasis','RECORDED_MATERIAL_RELATIONSHIPS',
        'positivePathCoverage',CASE WHEN EXISTS(SELECT 1 FROM trace_edges WHERE basis='RECORDED_CONTENT') THEN 'PARTIAL_LEGACY' ELSE 'PERSISTED_ALLOCATIONS' END,
        'traceId',trace_id,'targetLotId',target,'ownerId',owner,'direction',direction,'view',view_mode,
        'asOf',statement_timestamp(),'snapshot',pg_current_snapshot()::text,
        'sourceContributionsVisible',actor_role<>'ExportStaff',
        'lotCount',(SELECT count(*) FROM reached),'edgeCount',(SELECT count(*) FROM trace_edges),
        'lots',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'id',l.id,'code',l.code,'stage',l.stage,'operationId',l.operation_id,
            'kind',meta.lot_kind,'enteredPool',meta.entered_pool,
            'directHold',EXISTS(SELECT 1 FROM grainledger.ledger_holds h WHERE h.lot_id=l.id),
            'effectiveHold',EXISTS(SELECT 1 FROM grainledger.ledger_effective_hold_ids(l.id)),
            'positivePathSourceIds',CASE WHEN actor_role='ExportStaff' THEN NULL ELSE coalesce((
                SELECT jsonb_agg(p.source_lot_id ORDER BY p.source_lot_id) FROM positive_paths p WHERE p.lot_id=l.id),'[]'::jsonb) END,
            'isCurrentPool',meta.is_current_pool,'poolRevision',meta.pool_revision,
            'producedKg',(SELECT coalesce(sum(c.quantity_kg),0)::text FROM grainledger.ledger_contributions c WHERE c.lot_id=l.id),
            'positions',meta.positions,
            'restrictions',to_jsonb(array_remove(ARRAY[
                CASE WHEN EXISTS(SELECT 1 FROM grainledger.ledger_effective_hold_ids(l.id)) THEN 'HOLD' END,
                CASE WHEN EXISTS(SELECT 1 FROM grainledger.ledger_corrections WHERE operation_id=l.operation_id AND status IN ('REQUESTED','RECONCILED'))
                    OR EXISTS(SELECT 1 FROM grainledger.ledger_receipt_correction_blocks WHERE receipt_id=l.receipt_id) THEN 'CORRECTION' END,
                CASE WHEN grainledger.ledger_receiving_restricted(l.id) THEN 'RECEIVING_DISCREPANCY' END,
                CASE WHEN EXISTS(SELECT 1 FROM grainledger.ledger_operations WHERE id=l.operation_id AND status='CORRECTED')
                    OR EXISTS(SELECT 1 FROM grainledger.ledger_receipt_reversals WHERE receipt_id=l.receipt_id) THEN 'SUPERSEDED' END
            ],NULL)),
            'contributions',CASE WHEN actor_role='ExportStaff' THEN NULL ELSE coalesce((
                SELECT jsonb_agg(jsonb_build_object('sourceLotId',s.source_lot_id,'quantityKg',s.quantity_kg::text,
                    'denominatorKg',(SELECT sum(c.quantity_kg)::text FROM grainledger.ledger_contributions c WHERE c.lot_id=l.id),
                    'percentage',round(100*s.quantity_kg/NULLIF((SELECT sum(c.quantity_kg) FROM grainledger.ledger_contributions c WHERE c.lot_id=l.id),0),6)::text
                ) ORDER BY s.source_lot_id) FROM source_weights s WHERE s.lot_id=l.id
            ),'[]'::jsonb) END
        ) ORDER BY l.id) FROM reached r JOIN grainledger.ledger_lots l ON l.id=r.id JOIN lot_metadata meta ON meta.lot_id=l.id
            WHERE (SELECT count(*) FROM reached)<=5000),'[]'::jsonb),
        'edges',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'relationType',relation_type,'relationId',relation_id,'basis',basis,'eventId',event_id,
            'operationId',operation_id,'inputLotId',input_lot_id,'outputLotId',output_lot_id,
            'inputConsumedKg',input_kg::text,'outputProducedKg',output_kg::text,
            'inputEventKg',CASE WHEN relation_type='CUSTODY' THEN (SELECT sum(ce.quantity_kg)::text FROM grainledger.ledger_custody_edges ce
                WHERE ce.event_id=e.event_id AND ce.input_lot_id=e.input_lot_id) ELSE input_kg::text END,
            'sourceAllocations',CASE WHEN actor_role='ExportStaff' OR basis='RECORDED_CONTENT' THEN NULL ELSE coalesce((
                SELECT jsonb_agg(jsonb_build_object('sourceLotId',x.source_lot_id,'quantityKg',x.quantity_kg::text) ORDER BY x.source_lot_id)
                FROM allocations x WHERE x.relation_type=e.relation_type AND x.relation_id=e.relation_id
                  AND x.input_lot_id=e.input_lot_id AND x.output_lot_id=e.output_lot_id),'[]'::jsonb) END
        ) ORDER BY relation_type,relation_id,input_lot_id,output_lot_id) FROM trace_edges e
            WHERE (SELECT count(*) FROM trace_edges)<=20000),'[]'::jsonb),
        'custodyEvents',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'id',ce.event_id,'action',ce.action,'locationId',ce.location_id,'revision',ce.revision,
            'occurredAt',ce.created_at,'primaryOutputLotId',ce.output_lot_id
        ) ORDER BY ce.event_id) FROM grainledger.ledger_custody_events ce WHERE EXISTS(
            SELECT 1 FROM trace_edges e WHERE e.relation_type='CUSTODY' AND e.event_id=ce.event_id)),'[]'::jsonb),
        'operations',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'id',op.id,'kind',op.kind,'status',op.status,'occurredAt',op.occurred_at,
            'declaredLossKg',op.content->>'declaredLossKg','varianceKg',op.variance_kg::text
        ) ORDER BY op.id) FROM operations op WHERE EXISTS(SELECT 1 FROM trace_edges e WHERE e.operation_id=op.id)),'[]'::jsonb),
        'corrections',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'id',c.id,'originalOperationId',c.operation_id,'replacementOperationId',c.replacement_operation_id,'status',c.status
        ) ORDER BY c.id) FROM grainledger.ledger_corrections c JOIN grainledger.ledger_operations op ON op.id=c.operation_id
            WHERE op.owner_id=owner AND EXISTS(SELECT 1 FROM reached r JOIN grainledger.ledger_lots l ON l.id=r.id JOIN lot_metadata meta ON meta.lot_id=l.id
                WHERE l.operation_id=c.operation_id OR l.operation_id=c.replacement_operation_id)),'[]'::jsonb)
    ) INTO result WHERE EXISTS(SELECT 1 FROM actor);

    IF result IS NULL THEN RAISE EXCEPTION 'Trace permission changed' USING ERRCODE='42501'; END IF;
    IF (result->>'lotCount')::integer>5000 OR (result->>'edgeCount')::integer>20000 THEN
        RAISE EXCEPTION 'Trace exceeds interactive limit (5000 lots / 20000 edges); no complete result returned' USING ERRCODE='23514';
    END IF;
    INSERT INTO grainledger.ledger_events(id,kind,actor_id,lot_id,details)
    VALUES(trace_id,'TRACEBACK',a,target,jsonb_build_object(
        'schemaVersion',2,'graphBasis','RECORDED_MATERIAL_RELATIONSHIPS','ownerId',owner,'direction',direction,'view',view_mode,
        'asOf',result->'asOf','snapshot',result->'snapshot','lotCount',result->'lotCount',
        'lotIds',(SELECT jsonb_agg(x->'id') FROM jsonb_array_elements(result->'lots') x)
    ));
    RETURN result||jsonb_build_object('complete',true);
END $$;

CREATE FUNCTION grainledger.ledger_trace_lot_metadata(a uuid, ids uuid[]) RETURNS TABLE(lot_id uuid, code text, owner_id uuid, stage text, lot_kind text, entered_pool boolean, is_current_pool boolean, pool_revision bigint, positions jsonb, quantity_kg numeric)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    WITH actor AS MATERIALIZED (
        SELECT u."Role" role,u."OrganizationId" owner_id FROM grainledger.identity_users u
        WHERE u."Id"=a AND u."IsActive" AND u."Role" IN ('Administrator','CustomerManager','ExportStaff')
    ), scoped AS MATERIALIZED (
        SELECT l.* FROM grainledger.ledger_lots l CROSS JOIN actor
        WHERE (actor.role='Administrator' OR l.owner_id=actor.owner_id) AND (ids IS NULL OR l.id=ANY(ids))
    ), origins AS MATERIALIZED (
        SELECT edge.output_lot_id, max(ce.revision) revision,
            max(CASE WHEN ce.action='INGRESS' THEN 'POOL_GENERATION'
                     WHEN edge.output_lot_id=ce.output_lot_id THEN 'ALIQUOT' ELSE 'REMAINDER' END) kind
        FROM grainledger.ledger_custody_edges edge JOIN grainledger.ledger_custody_events ce ON ce.event_id=edge.event_id
        JOIN scoped s ON s.id=edge.output_lot_id GROUP BY edge.output_lot_id
    ), balances AS MATERIALIZED (
        SELECT p.lot_id,sum(p.quantity_kg) quantity,
            jsonb_agg(jsonb_build_object('locationId',loc.id,'locationName',loc.name,'locationKind',loc.kind,
                'facilityId',facility.id,'facilityName',facility.name,'quantityKg',p.quantity_kg::text,
                'availableKg',CASE WHEN grainledger.ledger_blocked(p.lot_id) THEN '0.000000' ELSE p.quantity_kg::text END
            ) ORDER BY loc.id) positions
        FROM grainledger.ledger_positions p JOIN scoped s ON s.id=p.lot_id
        JOIN grainledger.ledger_locations loc ON loc.id=p.location_id JOIN grainledger.ledger_facilities facility ON facility.id=loc.facility_id
        GROUP BY p.lot_id
    )
    SELECT s.id,s.code,s.owner_id,s.stage,
        CASE WHEN pool.lot_id IS NOT NULL THEN 'CURRENT_POOL' WHEN s.receipt_id IS NOT NULL THEN 'SOURCE'
             WHEN origins.kind IS NOT NULL THEN origins.kind WHEN s.operation_id IS NOT NULL THEN 'PROCESSING_OUTPUT' ELSE 'LOT' END,
        EXISTS(SELECT 1 FROM grainledger.ledger_custody_edges e JOIN grainledger.ledger_custody_events ce ON ce.event_id=e.event_id
               WHERE e.input_lot_id=s.id AND ce.action='INGRESS'),
        pool.lot_id IS NOT NULL,COALESCE(pool.revision,origins.revision),COALESCE(balances.positions,'[]'::jsonb),COALESCE(balances.quantity,0)
    FROM scoped s LEFT JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.lot_id=s.id
    LEFT JOIN origins ON origins.output_lot_id=s.id LEFT JOIN balances ON balances.lot_id=s.id
$$;

CREATE FUNCTION grainledger.ledger_trace_starts(a uuid, mode text, search text, facility uuid, location uuid, page integer, size integer) RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE result jsonb;
BEGIN
    IF grainledger.ledger_role(a) IS NULL OR grainledger.ledger_role(a) NOT IN ('Administrator','CustomerManager','ExportStaff') THEN
        RAISE EXCEPTION 'Trace permission required' USING ERRCODE='42501';
    END IF;
    IF mode IS NULL OR mode NOT IN ('pools','sources','all') OR page IS NULL OR page<1 OR page>1000000
       OR size IS NULL OR size<1 OR size>100 OR length(COALESCE(search,''))>200 THEN
        RAISE EXCEPTION 'Invalid trace starting-lot filters' USING ERRCODE='22023';
    END IF;
    WITH base AS MATERIALIZED (SELECT * FROM grainledger.ledger_trace_lot_metadata(a,NULL)),
    matches AS MATERIALIZED (
        SELECT b.* FROM base b
        WHERE (mode='all' OR mode='pools' AND b.is_current_pool AND b.quantity_kg>0 OR mode='sources' AND b.lot_kind='SOURCE')
          AND (COALESCE(search,'')='' OR position(lower(search) in lower(b.code))>0
               OR EXISTS(SELECT 1 FROM jsonb_array_elements(b.positions) p WHERE position(lower(search) in lower(p->>'locationName'))>0))
          AND ((facility IS NULL AND location IS NULL) OR EXISTS(
              SELECT 1 FROM jsonb_array_elements(b.positions) p
              WHERE (facility IS NULL OR (p->>'facilityId')::uuid=facility)
                AND (location IS NULL OR (p->>'locationId')::uuid=location)))
    ), selected AS (SELECT * FROM matches ORDER BY code,lot_id OFFSET (page-1)*size LIMIT size),
    places AS (SELECT DISTINCT p->>'locationId' id,p->>'locationName' name,p->>'facilityId' facility_id,p->>'facilityName' facility_name
               FROM base CROSS JOIN LATERAL jsonb_array_elements(positions) p)
    SELECT jsonb_build_object('items',COALESCE((SELECT jsonb_agg(jsonb_build_object(
        'id',lot_id,'code',code,'stage',stage,'kind',lot_kind,'enteredPool',entered_pool,
        'ownerId',owner_id,'isCurrentPool',is_current_pool,'poolRevision',pool_revision,'positions',positions,
        'quantityKg',quantity_kg::text) ORDER BY code,lot_id) FROM selected),'[]'::jsonb),
        'page',page,'pageSize',size,'totalCount',(SELECT count(*) FROM matches),'hasMore',(SELECT count(*)>page*size FROM matches),
        'locations',COALESCE((SELECT jsonb_agg(jsonb_build_object('id',id,'name',name,'facilityId',facility_id) ORDER BY name,id) FROM places),'[]'::jsonb),
        'facilities',COALESCE((SELECT jsonb_agg(jsonb_build_object('id',facility_id,'name',facility_name) ORDER BY facility_name,facility_id)
            FROM (SELECT DISTINCT facility_id,facility_name FROM places) f),'[]'::jsonb)) INTO result;
    RETURN result;
END $$;


SET default_tablespace = '';

SET default_table_access_method = heap;

CREATE TABLE grainledger.capture_business_events (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    company_id uuid,
    actor_id uuid,
    event_type character varying(60) NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    correlation_id uuid NOT NULL,
    payload jsonb NOT NULL
);

CREATE TABLE grainledger.capture_code_counters (
    company_id uuid NOT NULL,
    kind character varying(3) NOT NULL,
    scope character varying(32) NOT NULL,
    next_value bigint NOT NULL
);

CREATE TABLE grainledger.capture_crop_seasons (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    company_id uuid NOT NULL,
    name text NOT NULL,
    starts_on date NOT NULL,
    ends_on date NOT NULL,
    created_by uuid,
    version integer DEFAULT 1 NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    CONSTRAINT "capture_ck_crop_seasons_dates" CHECK ((ends_on >= starts_on))
);

CREATE TABLE grainledger.capture_farmers (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    company_id uuid NOT NULL,
    code character varying(40) NOT NULL,
    name text NOT NULL,
    contact text,
    created_at timestamp with time zone NOT NULL,
    created_by uuid,
    version integer DEFAULT 1 NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    code_number bigint
);

CREATE TABLE grainledger.capture_lab_confirmed_associations (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    confirmation_id uuid NOT NULL,
    result_set_id uuid NOT NULL,
    sample_id uuid NOT NULL,
    identity_basis character varying(32) NOT NULL,
    evidence_ref text,
    CONSTRAINT "capture_ck_lab_association_basis" CHECK (((identity_basis)::text = ANY ((ARRAY['SAMPLE_CODE'::character varying, 'SEAL_CODE'::character varying, 'LAB_REFERENCE'::character varying])::text[])))
);

CREATE TABLE grainledger.capture_lab_report_confirmations (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    report_id uuid NOT NULL,
    confirmed_by uuid NOT NULL,
    confirmed_at timestamp with time zone NOT NULL,
    identity_checked boolean NOT NULL,
    transcription_checked boolean NOT NULL,
    creation_transaction_id xid8 DEFAULT pg_current_xact_id() NOT NULL,
    CONSTRAINT "capture_ck_lab_confirmation_checked" CHECK ((identity_checked AND transcription_checked))
);

CREATE TABLE grainledger.capture_lab_report_retirements (
    report_id uuid NOT NULL,
    company_id uuid NOT NULL,
    replacement_report_id uuid,
    status character varying(32) NOT NULL,
    recorded_by uuid NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT "capture_ck_lab_retirement_status" CHECK (((((status)::text = 'DELETED'::text) AND (replacement_report_id IS NULL)) OR (((status)::text = 'SUPERSEDED'::text) AND (replacement_report_id IS NOT NULL) AND (replacement_report_id <> report_id))))
);

CREATE TABLE grainledger.capture_lab_reports (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    laboratory_name character varying(200) NOT NULL,
    report_number character varying(200) NOT NULL,
    issued_on date NOT NULL,
    received_at timestamp with time zone NOT NULL,
    report_kind character varying(32) NOT NULL,
    related_report_id uuid,
    relationship_reason text,
    laboratory_conclusion text,
    stated_standard text,
    file_name character varying(255) NOT NULL,
    content_type character varying(200) NOT NULL,
    file_size integer NOT NULL,
    sha256 character varying(64) NOT NULL,
    content bytea NOT NULL,
    company_only_confirmed boolean NOT NULL,
    creation_transaction_id xid8 DEFAULT pg_current_xact_id() NOT NULL,
    recorded_by uuid NOT NULL,
    captured_at timestamp with time zone NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT "capture_ck_lab_reports_company_only" CHECK (company_only_confirmed),
    CONSTRAINT "capture_ck_lab_reports_file" CHECK (((file_size > 0) AND (file_size <= 10485760) AND (octet_length(content) = file_size))),
    CONSTRAINT "capture_ck_lab_reports_kind" CHECK (((report_kind)::text = ANY ((ARRAY['PARTIAL'::character varying, 'FINAL'::character varying, 'SUPPLEMENTARY'::character varying, 'AMENDMENT'::character varying, 'RETEST'::character varying])::text[]))),
    CONSTRAINT "capture_ck_lab_reports_labels" CHECK (((length(btrim((laboratory_name)::text)) > 0) AND (length(btrim((report_number)::text)) > 0))),
    CONSTRAINT "capture_ck_lab_reports_related" CHECK ((((related_report_id IS NULL) AND ((report_kind)::text <> ALL ((ARRAY['SUPPLEMENTARY'::character varying, 'AMENDMENT'::character varying, 'RETEST'::character varying])::text[]))) OR ((related_report_id IS NOT NULL) AND (related_report_id <> id) AND (relationship_reason IS NOT NULL) AND (length(btrim(relationship_reason)) > 0)))),
    CONSTRAINT "capture_ck_lab_reports_times" CHECK ((received_at <= captured_at))
);

CREATE TABLE grainledger.capture_lab_result_sets (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    report_id uuid NOT NULL,
    "position" integer NOT NULL,
    sample_id uuid,
    laboratory_sample_reference text,
    stated_sample_code text,
    stated_seal_code text,
    section_reference text,
    CONSTRAINT "capture_lab_result_sets_position_check" CHECK (("position" >= 0))
);

CREATE TABLE grainledger.capture_lab_results (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    result_set_id uuid NOT NULL,
    "position" integer NOT NULL,
    test_label text NOT NULL,
    substance_id uuid,
    original_result_text text NOT NULL,
    result_kind character varying(32) NOT NULL,
    numeric_value character varying(25),
    unit text,
    lod character varying(25),
    loq character varying(25),
    method text,
    uncertainty text,
    reference text,
    CONSTRAINT "capture_ck_lab_results_kind" CHECK (((result_kind)::text = ANY ((ARRAY['NUMERIC'::character varying, 'BELOW_LIMIT'::character varying, 'NOT_DETECTED'::character varying, 'QUALITATIVE'::character varying, 'OTHER'::character varying])::text[]))),
    CONSTRAINT "capture_ck_lab_results_labels" CHECK (((length(btrim(test_label)) > 0) AND (length(btrim(original_result_text)) > 0))),
    CONSTRAINT "capture_ck_lab_results_lod" CHECK (((lod IS NULL) OR ((lod)::text ~ '^[0-9]{1,12}(\.[0-9]{1,12})?$'::text))),
    CONSTRAINT "capture_ck_lab_results_loq" CHECK (((loq IS NULL) OR ((loq)::text ~ '^[0-9]{1,12}(\.[0-9]{1,12})?$'::text))),
    CONSTRAINT "capture_ck_lab_results_numeric_required" CHECK ((((result_kind)::text <> ALL ((ARRAY['NUMERIC'::character varying, 'BELOW_LIMIT'::character varying])::text[])) OR (numeric_value IS NOT NULL))),
    CONSTRAINT "capture_ck_lab_results_numeric_value" CHECK (((numeric_value IS NULL) OR ((numeric_value)::text ~ '^[0-9]{1,12}(\.[0-9]{1,12})?$'::text))),
    CONSTRAINT "capture_lab_results_position_check" CHECK (("position" >= 0))
);

CREATE TABLE grainledger.capture_offline_operations (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    company_id uuid NOT NULL,
    submitted_by uuid NOT NULL,
    device_id character varying(100) NOT NULL,
    client_operation_id uuid NOT NULL,
    payload_hash character varying(128) NOT NULL,
    payload jsonb NOT NULL,
    captured_at timestamp with time zone NOT NULL,
    capture_geojson jsonb,
    received_at timestamp with time zone NOT NULL,
    status character varying(32) NOT NULL,
    applied_event_id uuid,
    result_payload jsonb,
    conflict_reason text,
    CONSTRAINT "capture_ck_offline_operations_status" CHECK (((status)::text = ANY ((ARRAY['PENDING'::character varying, 'APPLIED'::character varying, 'CONFLICT'::character varying, 'REJECTED'::character varying])::text[])))
);

CREATE TABLE grainledger.capture_plot_seasons (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    company_id uuid NOT NULL,
    plot_id uuid NOT NULL,
    season_id uuid NOT NULL,
    rice_variety text NOT NULL,
    created_by uuid,
    version integer DEFAULT 1 NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);

CREATE TABLE grainledger.capture_plots (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    company_id uuid NOT NULL,
    farmer_id uuid NOT NULL,
    code character varying(40) NOT NULL,
    boundary_geojson jsonb NOT NULL,
    area_ha numeric(24,12) NOT NULL,
    created_by uuid,
    version integer DEFAULT 1 NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    CONSTRAINT "capture_ck_plots_area_ha_positive" CHECK ((area_ha > (0)::numeric))
);

CREATE TABLE grainledger.capture_procurement_items (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    company_id uuid NOT NULL,
    procurement_id uuid NOT NULL,
    line_no integer NOT NULL,
    plot_season_id uuid NOT NULL,
    net_mass_kg numeric(20,6) NOT NULL,
    measured_value numeric(24,12) NOT NULL,
    measured_unit character varying(20) NOT NULL,
    moisture_percent numeric(24,12) NOT NULL,
    CONSTRAINT "capture_ck_procurement_items_line_no" CHECK ((line_no > 0)),
    CONSTRAINT "capture_ck_procurement_items_measured" CHECK ((measured_value > (0)::numeric)),
    CONSTRAINT "capture_ck_procurement_items_moisture" CHECK (((moisture_percent >= (0)::numeric) AND (moisture_percent <= (100)::numeric))),
    CONSTRAINT "capture_ck_procurement_items_net_mass" CHECK ((net_mass_kg > (0)::numeric))
);

CREATE TABLE grainledger.capture_procurements (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    company_id uuid NOT NULL,
    code character varying(40) NOT NULL,
    farmer_id uuid NOT NULL,
    collection_site_id uuid NOT NULL,
    received_by uuid NOT NULL,
    received_at timestamp with time zone NOT NULL,
    status character varying(32) NOT NULL,
    event_id uuid,
    receiving_workflow text DEFAULT 'PHYSICAL_ARRIVAL_V1'::text,
    CONSTRAINT "capture_ck_procurements_status" CHECK (((status)::text = ANY ((ARRAY['DRAFT'::character varying, 'COMMITTED'::character varying])::text[]))),
    CONSTRAINT "capture_procurements_receiving_workflow_check" CHECK (((receiving_workflow IS NULL) OR (receiving_workflow = 'PHYSICAL_ARRIVAL_V1'::text)))
);

CREATE TABLE grainledger.capture_residue_report_selections (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    source_id uuid NOT NULL,
    substance_id uuid NOT NULL,
    effective_from timestamp with time zone NOT NULL,
    effective_to timestamp with time zone,
    published_by uuid NOT NULL,
    published_at timestamp with time zone NOT NULL,
    payload jsonb NOT NULL,
    CONSTRAINT "capture_residue_report_selections_check" CHECK (((effective_to IS NULL) OR (effective_to > effective_from))),
    CONSTRAINT "capture_residue_report_selections_payload_check" CHECK ((jsonb_typeof(payload) = 'object'::text))
);

CREATE TABLE grainledger.capture_residue_source_group_versions (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    code text NOT NULL,
    version_no integer NOT NULL,
    substance_id uuid NOT NULL,
    source_ids uuid[] NOT NULL,
    effective_from timestamp with time zone NOT NULL,
    effective_to timestamp with time zone,
    published_by uuid NOT NULL,
    published_at timestamp with time zone NOT NULL,
    payload jsonb NOT NULL,
    CONSTRAINT "capture_residue_source_group_versions_check" CHECK (((effective_to IS NULL) OR (effective_to > effective_from))),
    CONSTRAINT "capture_residue_source_group_versions_payload_check" CHECK ((jsonb_typeof(payload) = 'object'::text)),
    CONSTRAINT "capture_residue_source_group_versions_version_no_check" CHECK ((version_no > 0))
);

CREATE TABLE grainledger.capture_sample_handovers (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    company_id uuid NOT NULL,
    sample_id uuid NOT NULL,
    recorded_by uuid NOT NULL,
    from_party text NOT NULL,
    to_party text NOT NULL,
    handed_over_at timestamp with time zone NOT NULL,
    seal_condition text NOT NULL,
    evidence_ref text,
    handover_kind character varying(32) DEFAULT 'HANDOVER'::character varying NOT NULL,
    laboratory_name character varying(200),
    laboratory_reference character varying(200),
    laboratory_sample_reference character varying(200),
    CONSTRAINT "capture_ck_sample_handovers_kind" CHECK (((handover_kind)::text = ANY ((ARRAY['HANDOVER'::character varying, 'LAB_DISPATCH'::character varying, 'LAB_ACKNOWLEDGEMENT'::character varying])::text[]))),
    CONSTRAINT "capture_ck_sample_handovers_lab" CHECK ((((handover_kind)::text = 'HANDOVER'::text) OR ((laboratory_name IS NOT NULL) AND (length(btrim((laboratory_name)::text)) > 0))))
);

CREATE TABLE grainledger.capture_samples (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    company_id uuid NOT NULL,
    code character varying(60) NOT NULL,
    source_lot_id uuid NOT NULL,
    seal_code character varying(100) NOT NULL,
    collected_by uuid NOT NULL,
    collected_at timestamp with time zone NOT NULL,
    material_description character varying(200)
);

CREATE TABLE grainledger.capture_source_lots (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    company_id uuid NOT NULL,
    lot_id uuid NOT NULL,
    procurement_item_id uuid NOT NULL
);

CREATE TABLE grainledger.capture_spray_declarations (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    company_id uuid NOT NULL,
    plot_season_id uuid NOT NULL,
    applied_on date NOT NULL,
    recorded_by uuid NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    evidence_ref text
);

CREATE TABLE grainledger.capture_spray_items (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    company_id uuid NOT NULL,
    declaration_id uuid NOT NULL,
    product_name text,
    substance_id uuid,
    rate numeric(24,12),
    rate_unit character varying(40),
    CONSTRAINT "capture_ck_spray_items_product_or_substance" CHECK (((product_name IS NOT NULL) OR (substance_id IS NOT NULL)))
);

CREATE TABLE grainledger.catalog_active_substances (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    code character varying(60) NOT NULL,
    name text NOT NULL
);

CREATE TABLE grainledger.catalog_blend_assessment_policies (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    market_id uuid NOT NULL,
    commodity_id uuid NOT NULL,
    version_no integer NOT NULL,
    supersedes_id uuid,
    effective_from timestamp with time zone NOT NULL,
    effective_to timestamp with time zone,
    required_substance_ids uuid[] NOT NULL,
    margin_fraction numeric(18,12) NOT NULL,
    residue_basis text NOT NULL,
    quality_rules jsonb NOT NULL,
    evidence_reference text NOT NULL,
    reviewed_by text NOT NULL,
    review_authority text NOT NULL,
    reviewed_at timestamp with time zone NOT NULL,
    published_by uuid NOT NULL,
    published_at timestamp with time zone NOT NULL,
    CONSTRAINT "catalog_blend_assessment_policies_version_no_check" CHECK ((version_no > 0)),
    CONSTRAINT "catalog_ck_blend_assessment_basis" CHECK (((length(btrim(residue_basis)) >= 1) AND (length(btrim(residue_basis)) <= 100))),
    CONSTRAINT "catalog_ck_blend_assessment_coverage" CHECK (((cardinality(required_substance_ids) >= 1) AND (cardinality(required_substance_ids) <= 200))),
    CONSTRAINT "catalog_ck_blend_assessment_evidence" CHECK (((length(btrim(evidence_reference)) >= 1) AND (length(btrim(evidence_reference)) <= 1000))),
    CONSTRAINT "catalog_ck_blend_assessment_margin" CHECK (((margin_fraction >= (0)::numeric) AND (margin_fraction < (1)::numeric))),
    CONSTRAINT "catalog_ck_blend_assessment_quality" CHECK ((jsonb_typeof(quality_rules) = 'array'::text)),
    CONSTRAINT "catalog_ck_blend_assessment_window" CHECK (((effective_to IS NULL) OR (effective_to > effective_from)))
);

CREATE TABLE grainledger.catalog_catalog_events (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    actor_id uuid NOT NULL,
    event_type character varying(60) NOT NULL,
    resource_type character varying(60) NOT NULL,
    resource_id uuid NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    correlation_id uuid NOT NULL,
    before_state jsonb,
    after_state jsonb,
    CONSTRAINT "catalog_ck_catalog_events_has_state" CHECK (((before_state IS NOT NULL) OR (after_state IS NOT NULL)))
);

CREATE TABLE grainledger.catalog_commodities (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    code character varying(40) NOT NULL,
    name text NOT NULL,
    created_by uuid,
    is_archived boolean DEFAULT false NOT NULL
);

CREATE TABLE grainledger.catalog_markets (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    code character varying(40) NOT NULL,
    name text NOT NULL
);

CREATE TABLE grainledger.catalog_measurement_units (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    code character varying(30) NOT NULL,
    name text NOT NULL,
    quantity_kind character varying(32) NOT NULL,
    created_by uuid,
    is_deleted boolean DEFAULT false NOT NULL,
    CONSTRAINT "catalog_ck_measurement_units_quantity_kind" CHECK (((quantity_kind)::text = ANY ((ARRAY['MASS'::character varying, 'AREA'::character varying, 'RESIDUE_CONCENTRATION'::character varying, 'APPLICATION_RATE'::character varying, 'FRACTION'::character varying])::text[])))
);

CREATE TABLE grainledger.catalog_mrl_limits (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    mrl_version_id uuid NOT NULL,
    substance_id uuid NOT NULL,
    limit_value numeric(24,12) NOT NULL,
    unit character varying(20) NOT NULL,
    CONSTRAINT "catalog_ck_mrl_limits_nonnegative" CHECK ((limit_value >= (0)::numeric))
);

CREATE TABLE grainledger.catalog_mrl_versions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    market_id uuid NOT NULL,
    commodity_id uuid NOT NULL,
    version_no integer NOT NULL,
    status character varying(32) NOT NULL,
    effective_from timestamp with time zone NOT NULL,
    effective_to timestamp with time zone,
    published_by uuid,
    published_at timestamp with time zone,
    source_ref text NOT NULL,
    CONSTRAINT "catalog_ck_mrl_versions_effective_interval" CHECK (((effective_to IS NULL) OR (effective_to > effective_from))),
    CONSTRAINT "catalog_ck_mrl_versions_publication" CHECK (((((status)::text = 'DRAFT'::text) AND (published_by IS NULL) AND (published_at IS NULL)) OR (((status)::text = 'PUBLISHED'::text) AND (published_by IS NOT NULL) AND (published_at IS NOT NULL))))
);

CREATE TABLE grainledger.catalog_quality_attributes (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    code character varying(60) NOT NULL,
    name text NOT NULL,
    unit character varying(30) NOT NULL
);

CREATE TABLE grainledger.catalog_residue_policy_setting_versions (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    rule_code text NOT NULL,
    version_no integer NOT NULL,
    effective_from timestamp with time zone NOT NULL,
    effective_to timestamp with time zone,
    published_by uuid NOT NULL,
    published_at timestamp with time zone NOT NULL,
    payload jsonb NOT NULL,
    CONSTRAINT "catalog_residue_policy_setting_versions_check" CHECK (((effective_to IS NULL) OR (effective_to > effective_from))),
    CONSTRAINT "catalog_residue_policy_setting_versions_payload_check" CHECK ((jsonb_typeof(payload) = 'object'::text)),
    CONSTRAINT "catalog_residue_policy_setting_versions_version_no_check" CHECK ((version_no > 0))
);

CREATE TABLE grainledger.catalog_residue_rule_versions (
    id uuid NOT NULL,
    code text NOT NULL,
    version_no integer NOT NULL,
    substance_id uuid NOT NULL,
    commodity_id uuid NOT NULL,
    effective_from timestamp with time zone NOT NULL,
    effective_to timestamp with time zone,
    published_by uuid NOT NULL,
    published_at timestamp with time zone NOT NULL,
    payload jsonb NOT NULL,
    company_id uuid,
    CONSTRAINT "catalog_residue_rule_versions_check" CHECK (((effective_to IS NULL) OR (effective_to > effective_from))),
    CONSTRAINT "catalog_residue_rule_versions_payload_check" CHECK ((jsonb_typeof(payload) = 'object'::text)),
    CONSTRAINT "catalog_residue_rule_versions_version_no_check" CHECK ((version_no > 0))
);

CREATE TABLE grainledger.catalog_rice_varieties (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    code character varying(40) NOT NULL,
    name text NOT NULL
);

CREATE TABLE grainledger.exports_blend_plan_drafts (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    order_id uuid NOT NULL,
    container_id uuid NOT NULL,
    specification_id uuid NOT NULL,
    version_no integer NOT NULL,
    version integer NOT NULL,
    status text NOT NULL,
    target_mass_kg numeric(20,6) NOT NULL,
    demand_snapshot jsonb NOT NULL,
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    cancelled_by uuid,
    cancelled_at timestamp with time zone,
    CONSTRAINT "exports_ck_plan_draft_mass" CHECK ((target_mass_kg > (0)::numeric)),
    CONSTRAINT "exports_ck_plan_draft_state" CHECK ((((status = 'DRAFT'::text) AND (cancelled_by IS NULL) AND (cancelled_at IS NULL)) OR ((status = 'CANCELLED'::text) AND (cancelled_by IS NOT NULL) AND (cancelled_at IS NOT NULL)))),
    CONSTRAINT "exports_ck_plan_draft_versions" CHECK (((version_no > 0) AND (version > 0)))
);

CREATE TABLE grainledger.exports_blend_plan_events (
    id uuid NOT NULL,
    plan_id uuid NOT NULL,
    company_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    action text NOT NULL,
    recorded_at timestamp with time zone NOT NULL
);

CREATE TABLE grainledger.exports_blend_plan_previews (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    plan_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    request_json jsonb NOT NULL,
    snapshot_json jsonb NOT NULL,
    fingerprint text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    CONSTRAINT "exports_blend_plan_previews_check" CHECK ((expires_at > created_at)),
    CONSTRAINT "exports_blend_plan_previews_fingerprint_check" CHECK ((length(fingerprint) = 64))
);

CREATE TABLE grainledger.exports_blend_plan_proposals (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    plan_id uuid NOT NULL,
    preview_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    snapshot_json jsonb NOT NULL,
    proposed_at timestamp with time zone NOT NULL
);

CREATE TABLE grainledger.exports_code_counters (
    company_id uuid NOT NULL,
    kind text NOT NULL,
    next_value bigint NOT NULL,
    CONSTRAINT "exports_code_counters_kind_check" CHECK ((kind = ANY (ARRAY['EXP'::text, 'CTN'::text]))),
    CONSTRAINT "exports_code_counters_next_value_check" CHECK ((next_value > 0))
);

CREATE TABLE grainledger.exports_containers (
    id uuid NOT NULL,
    order_id uuid NOT NULL,
    company_id uuid NOT NULL,
    internal_code text NOT NULL,
    target_mass_kg numeric(20,6) NOT NULL,
    original_mass text NOT NULL,
    original_unit text NOT NULL,
    status text DEFAULT 'PLANNED'::text NOT NULL,
    version integer DEFAULT 1 NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "exports_containers_status_check" CHECK ((status = ANY (ARRAY['PLANNED'::text, 'LOADING'::text, 'CLOSED'::text]))),
    CONSTRAINT "exports_containers_target_mass_kg_check" CHECK ((target_mass_kg > (0)::numeric)),
    CONSTRAINT "exports_containers_version_check" CHECK ((version > 0))
);

CREATE TABLE grainledger.exports_events (
    id uuid NOT NULL,
    order_id uuid NOT NULL,
    company_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    action text NOT NULL,
    snapshot jsonb NOT NULL,
    recorded_at timestamp with time zone NOT NULL
);

CREATE TABLE grainledger.exports_export_orders (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    code text NOT NULL,
    market_id uuid NOT NULL,
    commodity_id uuid NOT NULL,
    required_mass_kg numeric(20,6) NOT NULL,
    original_mass text NOT NULL,
    original_unit text NOT NULL,
    shipping_from date NOT NULL,
    shipping_to date NOT NULL,
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    version integer DEFAULT 1 NOT NULL,
    current_specification_version integer DEFAULT 1 NOT NULL,
    CONSTRAINT "exports_export_orders_check" CHECK ((shipping_to >= shipping_from)),
    CONSTRAINT "exports_export_orders_current_specification_version_check" CHECK ((current_specification_version > 0)),
    CONSTRAINT "exports_export_orders_required_mass_kg_check" CHECK ((required_mass_kg > (0)::numeric)),
    CONSTRAINT "exports_export_orders_version_check" CHECK ((version > 0))
);

CREATE TABLE grainledger.exports_idempotent_operations (
    id uuid NOT NULL,
    company_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    request_hash text NOT NULL,
    order_id uuid NOT NULL,
    container_id uuid,
    specification_version integer,
    response_json jsonb NOT NULL,
    response_status integer NOT NULL,
    response_version integer NOT NULL
);

CREATE TABLE grainledger.exports_order_specifications (
    id uuid NOT NULL,
    order_id uuid NOT NULL,
    company_id uuid NOT NULL,
    version_no integer NOT NULL,
    recorded_by uuid NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT "exports_order_specifications_version_no_check" CHECK ((version_no > 0))
);

CREATE TABLE grainledger.exports_specification_limits (
    id uuid NOT NULL,
    specification_id uuid NOT NULL,
    attribute_id uuid NOT NULL,
    attribute_code text NOT NULL,
    attribute_name text NOT NULL,
    unit text NOT NULL,
    min_value numeric(24,12),
    max_value numeric(24,12),
    CONSTRAINT "exports_specification_limits_check" CHECK (((min_value IS NOT NULL) OR (max_value IS NOT NULL))),
    CONSTRAINT "exports_specification_limits_check1" CHECK (((min_value IS NULL) OR (max_value IS NULL) OR (min_value <= max_value)))
);

CREATE TABLE grainledger.identity_audit_events (
    "Id" bigint NOT NULL,
    "ActorUserId" uuid,
    "SubjectUserId" uuid,
    "Action" character varying(100) NOT NULL,
    "DetailsJson" jsonb NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "IpAddress" text
);

ALTER TABLE grainledger.identity_audit_events ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME grainledger."identity_audit_events_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE TABLE grainledger.identity_organizations (
    "Id" uuid NOT NULL,
    "Name" character varying(160) NOT NULL,
    "Kind" character varying(20) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL
);

CREATE TABLE grainledger.identity_password_reset_codes (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "CodeHash" character varying(64) NOT NULL,
    "FailedAttempts" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "LastSentAt" timestamp with time zone NOT NULL,
    "ConsumedAt" timestamp with time zone
);

CREATE TABLE grainledger.identity_registration_attempts (
    "Id" uuid NOT NULL,
    "Email" character varying(320) NOT NULL,
    "NormalizedEmail" character varying(320) NOT NULL,
    "DisplayName" character varying(120) NOT NULL,
    "PasswordHash" character varying(1000) NOT NULL,
    "CodeHash" character varying(64) NOT NULL,
    "FailedAttempts" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "LastSentAt" timestamp with time zone NOT NULL,
    "InvalidatedAt" timestamp with time zone,
    "ConsumedAt" timestamp with time zone
);

CREATE TABLE grainledger.identity_sessions (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "LastSeenAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "RevokedAt" timestamp with time zone,
    "IpAddress" character varying(64),
    "UserAgent" character varying(512),
    "FamilyId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL,
    "RefreshTokenHash" character varying(64),
    "ReplacedBySessionId" uuid,
    "RefreshTokenUsedAt" timestamp with time zone
);

CREATE TABLE grainledger.identity_users (
    "Id" uuid NOT NULL,
    "Provider" character varying(30) NOT NULL,
    "ProviderSubject" character varying(255) NOT NULL,
    "Email" character varying(320) NOT NULL,
    "DisplayName" character varying(120) NOT NULL,
    "PhoneNumber" character varying(32),
    "Role" character varying(40),
    "OrganizationId" uuid,
    "IsActive" boolean NOT NULL,
    "Version" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "FailedLoginCount" integer DEFAULT 0 NOT NULL,
    "LockoutEnd" timestamp with time zone,
    "NormalizedEmail" character varying(320),
    "PasswordHash" character varying(1000)
);

CREATE TABLE grainledger.ledger_arrival_measurement_sets (
    id uuid NOT NULL,
    procurement_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    operator_id uuid NOT NULL,
    captured_at timestamp with time zone NOT NULL,
    recorded_at timestamp with time zone DEFAULT clock_timestamp() NOT NULL,
    observations text NOT NULL,
    expected_version integer NOT NULL,
    threshold_company_id uuid,
    threshold_percentage numeric(8,6),
    threshold_source text,
    threshold_revision uuid,
    CONSTRAINT "ledger_arrival_measurement_sets_observations_check" CHECK ((length(observations) <= 4000)),
    CONSTRAINT "ledger_arrival_threshold_shape" CHECK ((((threshold_company_id IS NULL) AND (threshold_percentage IS NULL) AND (threshold_source IS NULL) AND (threshold_revision IS NULL)) OR ((threshold_company_id IS NOT NULL) AND (threshold_percentage IS NOT NULL) AND (threshold_percentage >= (0)::numeric) AND (threshold_percentage < (100)::numeric) AND (threshold_source IS NOT NULL) AND (threshold_source = ANY (ARRAY['SYSTEM_DEFAULT'::text, 'COMPANY_OVERRIDE'::text])) AND (threshold_revision IS NOT NULL))))
);

CREATE TABLE grainledger.ledger_arrival_measurements (
    measurement_set_id uuid NOT NULL,
    procurement_item_id uuid NOT NULL,
    measured_value numeric(24,12) NOT NULL,
    measured_unit text NOT NULL,
    actual_kg numeric(20,6) NOT NULL,
    CONSTRAINT "ledger_arrival_measurements_actual_kg_check" CHECK ((actual_kg > (0)::numeric)),
    CONSTRAINT "ledger_arrival_measurements_check" CHECK ((actual_kg =
CASE measured_unit
    WHEN 'KG'::text THEN measured_value
    WHEN 'G'::text THEN (measured_value / (1000)::numeric)
    WHEN 'T'::text THEN (measured_value * (1000)::numeric)
    ELSE NULL::numeric
END)),
    CONSTRAINT "ledger_arrival_measurements_measured_unit_check" CHECK ((measured_unit = ANY (ARRAY['KG'::text, 'G'::text, 'T'::text]))),
    CONSTRAINT "ledger_arrival_measurements_measured_value_check" CHECK ((measured_value > (0)::numeric))
);

CREATE TABLE grainledger.ledger_arrival_operations (
    operation_id uuid NOT NULL,
    procurement_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    kind text NOT NULL,
    payload jsonb NOT NULL,
    result jsonb NOT NULL,
    recorded_at timestamp with time zone DEFAULT clock_timestamp() NOT NULL
);

CREATE TABLE grainledger.ledger_arrival_reviews (
    id uuid NOT NULL,
    procurement_id uuid NOT NULL,
    measurement_set_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    expected_version integer NOT NULL,
    action text NOT NULL,
    reason text NOT NULL,
    evidence_id uuid NOT NULL,
    manager_id uuid NOT NULL,
    recorded_at timestamp with time zone DEFAULT clock_timestamp() NOT NULL,
    CONSTRAINT "ledger_arrival_reviews_action_check" CHECK ((action = ANY (ARRAY['APPROVE_SHORTAGE'::text, 'REQUEST_REMEASUREMENT'::text, 'KEEP_BLOCKED'::text]))),
    CONSTRAINT "ledger_arrival_reviews_reason_check" CHECK (((length(TRIM(BOTH FROM reason)) >= 1) AND (length(TRIM(BOTH FROM reason)) <= 4000)))
);

CREATE TABLE grainledger.ledger_assignments (
    user_id uuid NOT NULL,
    facility_id uuid NOT NULL,
    created_by uuid,
    version uuid DEFAULT gen_random_uuid() NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);

CREATE TABLE grainledger.ledger_catalog_commodities (
    catalog_commodity_id uuid NOT NULL,
    ledger_commodity_id uuid NOT NULL
);

CREATE TABLE grainledger.ledger_commodities (
    id uuid NOT NULL,
    name text NOT NULL,
    CONSTRAINT "ledger_commodities_name_check" CHECK ((length(name) > 0))
);

CREATE TABLE grainledger.ledger_compatibilities (
    id uuid NOT NULL,
    facility_id uuid NOT NULL,
    input_products text[] NOT NULL,
    output_commodity_id uuid NOT NULL,
    effective_from timestamp with time zone NOT NULL,
    evidence_id uuid NOT NULL,
    CONSTRAINT "ledger_compatibilities_input_products_check" CHECK ((cardinality(input_products) > 0))
);

CREATE TABLE grainledger.ledger_contributions (
    lot_id uuid NOT NULL,
    source_lot_id uuid NOT NULL,
    quantity_kg numeric(20,6) NOT NULL,
    CONSTRAINT "ledger_contributions_quantity_kg_check" CHECK ((quantity_kg >= (0)::numeric))
);

CREATE TABLE grainledger.ledger_corrections (
    id uuid NOT NULL,
    operation_id uuid NOT NULL,
    reason text NOT NULL,
    status text DEFAULT 'REQUESTED'::text NOT NULL,
    evidence_id uuid,
    reconciliation jsonb,
    replacement_operation_id uuid,
    replacement_version uuid,
    replacement_content jsonb,
    CONSTRAINT "ledger_corrections_reason_check" CHECK ((length(reason) > 0)),
    CONSTRAINT "ledger_corrections_status_check" CHECK ((status = ANY (ARRAY['REQUESTED'::text, 'RECONCILED'::text, 'CANCELLED'::text, 'COMMITTED'::text])))
);

CREATE TABLE grainledger.ledger_custody_allocations (
    event_id uuid NOT NULL,
    input_lot_id uuid NOT NULL,
    output_lot_id uuid NOT NULL,
    source_lot_id uuid NOT NULL,
    quantity_kg numeric(20,6) NOT NULL,
    CONSTRAINT "ledger_custody_allocations_quantity_kg_check" CHECK ((quantity_kg > (0)::numeric))
);

CREATE TABLE grainledger.ledger_custody_edges (
    event_id uuid NOT NULL,
    input_lot_id uuid NOT NULL,
    output_lot_id uuid NOT NULL,
    quantity_kg numeric(20,6) NOT NULL,
    CONSTRAINT "ledger_custody_edges_quantity_kg_check" CHECK ((quantity_kg > (0)::numeric))
);

CREATE TABLE grainledger.ledger_custody_events (
    event_id uuid NOT NULL,
    command_id uuid NOT NULL,
    command_kind text NOT NULL,
    action text NOT NULL,
    actor_id uuid NOT NULL,
    location_id uuid NOT NULL,
    input_lot_id uuid,
    output_lot_id uuid NOT NULL,
    policy_id uuid,
    revision bigint NOT NULL,
    quantity_kg numeric(20,6) NOT NULL,
    request jsonb NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "ledger_custody_events_action_check" CHECK ((action = ANY (ARRAY['INGRESS'::text, 'WITHDRAW'::text]))),
    CONSTRAINT "ledger_custody_events_check" CHECK (((action <> 'INGRESS'::text) OR (policy_id IS NOT NULL))),
    CONSTRAINT "ledger_custody_events_quantity_kg_check" CHECK ((quantity_kg > (0)::numeric)),
    CONSTRAINT "ledger_custody_events_request_check" CHECK ((jsonb_typeof(request) = 'object'::text)),
    CONSTRAINT "ledger_custody_events_revision_check" CHECK ((revision > 0))
);

CREATE TABLE grainledger.ledger_custody_processing_allocations (
    event_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    input_lot_id uuid NOT NULL,
    output_lot_id uuid NOT NULL,
    source_lot_id uuid NOT NULL,
    quantity_kg numeric(20,6) NOT NULL,
    CONSTRAINT "ledger_custody_processing_allocations_quantity_kg_check" CHECK ((quantity_kg > (0)::numeric))
);

CREATE TABLE grainledger.ledger_equipment (
    id uuid NOT NULL,
    facility_id uuid NOT NULL,
    name text NOT NULL,
    created_by uuid,
    version uuid DEFAULT gen_random_uuid() NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    CONSTRAINT "ledger_equipment_name_check" CHECK (((length(name) >= 1) AND (length(name) <= 160)))
);

CREATE TABLE grainledger.ledger_events (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    kind text NOT NULL,
    actor_id uuid NOT NULL,
    operation_id uuid,
    lot_id uuid,
    details jsonb NOT NULL,
    occurred_at timestamp with time zone DEFAULT now() NOT NULL
);

CREATE TABLE grainledger.ledger_evidence (
    id uuid NOT NULL,
    owner_id uuid NOT NULL,
    facility_id uuid NOT NULL,
    file_name text NOT NULL,
    content_type text NOT NULL,
    content bytea NOT NULL,
    sha256 text NOT NULL,
    actor_id uuid NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "ledger_evidence_content_check" CHECK (((octet_length(content) >= 1) AND (octet_length(content) <= 10485760)))
);

CREATE TABLE grainledger.ledger_facilities (
    id uuid NOT NULL,
    name text NOT NULL,
    created_by uuid,
    version uuid DEFAULT gen_random_uuid() NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    CONSTRAINT "ledger_facilities_name_check" CHECK (((length(name) >= 1) AND (length(name) <= 160)))
);

CREATE TABLE grainledger.ledger_holds (
    id uuid NOT NULL,
    lot_id uuid NOT NULL,
    reason text NOT NULL,
    status text DEFAULT 'ACTIVE'::text NOT NULL,
    evidence_id uuid,
    response text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "ledger_holds_reason_check" CHECK ((length(reason) > 0))
);

CREATE TABLE grainledger.ledger_locations (
    id uuid NOT NULL,
    facility_id uuid NOT NULL,
    name text NOT NULL,
    kind text NOT NULL,
    owner_id uuid,
    cleared boolean DEFAULT true NOT NULL,
    created_by uuid,
    version uuid DEFAULT gen_random_uuid() NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    mixed_bulk boolean DEFAULT false NOT NULL,
    CONSTRAINT "ledger_locations_kind_check" CHECK ((kind = ANY (ARRAY['STORE'::text, 'SILO'::text, 'COMPARTMENT'::text, 'ISOLATION'::text]))),
    CONSTRAINT "ledger_locations_name_check" CHECK (((length(name) >= 1) AND (length(name) <= 160)))
);

CREATE TABLE grainledger.ledger_lot_code_reservations (
    lot_id uuid NOT NULL,
    code text NOT NULL,
    owner_id uuid NOT NULL,
    context_kind text NOT NULL,
    context_id uuid NOT NULL,
    stage text NOT NULL,
    product_identity text NOT NULL,
    issued_at timestamp with time zone DEFAULT clock_timestamp() NOT NULL,
    CONSTRAINT "ledger_lot_code_reservations_context_kind_check" CHECK ((context_kind = ANY (ARRAY['SOURCE'::text, 'OPERATION'::text, 'CUSTODY'::text])))
);

CREATE SEQUENCE grainledger.ledger_lot_code_serial
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

CREATE TABLE grainledger.ledger_lot_codes (
    code text NOT NULL,
    origin_lot_id uuid NOT NULL
);

CREATE TABLE grainledger.ledger_lots (
    id uuid NOT NULL,
    code text NOT NULL,
    code_origin_id uuid NOT NULL,
    owner_id uuid NOT NULL,
    commodity_id uuid NOT NULL,
    stage text NOT NULL,
    product_identity text NOT NULL,
    receipt_id uuid,
    receipt_line_id uuid,
    operation_id uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "ledger_lots_stage_check" CHECK ((stage = ANY (ARRAY['RAW_PADDY'::text, 'DRIED_PADDY'::text, 'HEAD_RICE'::text, 'BROKEN_RICE'::text, 'BRAN'::text, 'HUSK'::text, 'BLENDED_RICE'::text, 'BAGGED_RICE'::text])))
);

CREATE TABLE grainledger.ledger_mixed_bulk_command_results (
    action text NOT NULL,
    command_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    request jsonb NOT NULL,
    result jsonb NOT NULL
);

CREATE TABLE grainledger.ledger_mixed_bulk_policies (
    id uuid NOT NULL,
    location_id uuid NOT NULL,
    owner_id uuid NOT NULL,
    commodity_id uuid NOT NULL,
    stage text NOT NULL,
    product_identity text NOT NULL,
    location_kind text NOT NULL,
    effective_from timestamp with time zone NOT NULL,
    evidence_id uuid NOT NULL,
    approved_by uuid NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "ledger_mixed_bulk_policies_location_kind_check" CHECK ((length(TRIM(BOTH FROM location_kind)) > 0)),
    CONSTRAINT "ledger_mixed_bulk_policies_product_identity_check" CHECK ((length(TRIM(BOTH FROM product_identity)) > 0)),
    CONSTRAINT "ledger_mixed_bulk_policies_stage_check" CHECK ((stage = ANY (ARRAY['RAW_PADDY'::text, 'DRIED_PADDY'::text, 'HEAD_RICE'::text, 'BROKEN_RICE'::text, 'BRAN'::text, 'HUSK'::text, 'BLENDED_RICE'::text, 'BAGGED_RICE'::text])))
);

CREATE TABLE grainledger.ledger_mixed_bulk_pools (
    location_id uuid NOT NULL,
    lot_id uuid,
    revision bigint DEFAULT 0 NOT NULL,
    owner_id uuid,
    commodity_id uuid,
    stage text,
    product_identity text,
    CONSTRAINT "ledger_mixed_bulk_pools_check" CHECK (((lot_id IS NULL) OR (revision > 0))),
    CONSTRAINT "ledger_mixed_bulk_pools_check1" CHECK ((((owner_id IS NULL) AND (commodity_id IS NULL) AND (stage IS NULL) AND (product_identity IS NULL)) OR ((owner_id IS NOT NULL) AND (commodity_id IS NOT NULL) AND (stage IS NOT NULL) AND (product_identity IS NOT NULL)))),
    CONSTRAINT "ledger_mixed_bulk_pools_revision_check" CHECK ((revision >= 0))
);

CREATE TABLE grainledger.ledger_notifications (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    hold_id uuid NOT NULL,
    lot_id uuid NOT NULL,
    message text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    read_at timestamp with time zone
);

CREATE TABLE grainledger.ledger_operations (
    id uuid NOT NULL,
    owner_id uuid NOT NULL,
    facility_id uuid NOT NULL,
    kind text NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    equipment_id uuid NOT NULL,
    content jsonb NOT NULL,
    status text DEFAULT 'DRAFT'::text NOT NULL,
    version uuid NOT NULL,
    confirmed_version uuid,
    confirmed_by uuid,
    confirmation_evidence_id uuid,
    variance_kg numeric(20,6),
    allowed_variance_kg numeric(20,6),
    correction_id uuid,
    created_by uuid,
    is_deleted boolean DEFAULT false NOT NULL,
    lot_code_scheme text DEFAULT 'LEGACY'::text NOT NULL,
    CONSTRAINT "ledger_operations_kind_check" CHECK ((kind = ANY (ARRAY['DRYING'::text, 'MILLING'::text, 'MERGE'::text, 'BLENDING'::text, 'BAGGING'::text]))),
    CONSTRAINT "ledger_operations_lot_code_scheme_check" CHECK ((lot_code_scheme = ANY (ARRAY['LEGACY'::text, 'GENERATED'::text]))),
    CONSTRAINT "ledger_operations_status_check" CHECK ((status = ANY (ARRAY['DRAFT'::text, 'COMMITTED'::text, 'CORRECTED'::text])))
);

CREATE TABLE grainledger.ledger_planning_quality_observations (
    id uuid NOT NULL,
    owner_id uuid NOT NULL,
    facility_id uuid NOT NULL,
    lot_id uuid NOT NULL,
    attribute_id uuid NOT NULL,
    version_no integer NOT NULL,
    measured_value numeric(24,12) NOT NULL,
    unit text NOT NULL,
    measured_at timestamp with time zone NOT NULL,
    evidence_id uuid NOT NULL,
    report_reference text NOT NULL,
    reviewed_by text NOT NULL,
    review_authority text NOT NULL,
    reviewed_at timestamp with time zone NOT NULL,
    confirmed_by uuid NOT NULL,
    confirmed_at timestamp with time zone NOT NULL,
    CONSTRAINT "ledger_planning_quality_observations_measured_value_check" CHECK ((measured_value >= (0)::numeric)),
    CONSTRAINT "ledger_planning_quality_observations_report_reference_check" CHECK ((length(btrim(report_reference)) > 0)),
    CONSTRAINT "ledger_planning_quality_observations_review_authority_check" CHECK ((length(btrim(review_authority)) > 0)),
    CONSTRAINT "ledger_planning_quality_observations_reviewed_by_check" CHECK ((length(btrim(reviewed_by)) > 0)),
    CONSTRAINT "ledger_planning_quality_observations_unit_check" CHECK ((length(btrim(unit)) > 0)),
    CONSTRAINT "ledger_planning_quality_observations_version_no_check" CHECK ((version_no > 0))
);

CREATE TABLE grainledger.ledger_planning_yield_rules (
    id uuid NOT NULL,
    facility_id uuid NOT NULL,
    compatibility_id uuid NOT NULL,
    version_no integer NOT NULL,
    output_fraction numeric(18,12) NOT NULL,
    effective_from timestamp with time zone NOT NULL,
    effective_to timestamp with time zone,
    evidence_id uuid NOT NULL,
    reviewed_by text NOT NULL,
    review_authority text NOT NULL,
    reviewed_at timestamp with time zone NOT NULL,
    published_by uuid NOT NULL,
    published_at timestamp with time zone NOT NULL,
    CONSTRAINT "ledger_ck_planning_yield_window" CHECK (((effective_to IS NULL) OR (effective_to > effective_from))),
    CONSTRAINT "ledger_planning_yield_rules_output_fraction_check" CHECK (((output_fraction > (0)::numeric) AND (output_fraction <= (1)::numeric))),
    CONSTRAINT "ledger_planning_yield_rules_review_authority_check" CHECK ((length(btrim(review_authority)) > 0)),
    CONSTRAINT "ledger_planning_yield_rules_reviewed_by_check" CHECK ((length(btrim(reviewed_by)) > 0)),
    CONSTRAINT "ledger_planning_yield_rules_version_no_check" CHECK ((version_no > 0))
);

CREATE TABLE grainledger.ledger_policies (
    id uuid NOT NULL,
    facility_id uuid NOT NULL,
    kind text NOT NULL,
    tolerance_percent numeric(20,6) NOT NULL,
    effective_from timestamp with time zone NOT NULL,
    evidence_id uuid NOT NULL,
    CONSTRAINT "ledger_policies_kind_check" CHECK ((kind = ANY (ARRAY['DRYING'::text, 'MILLING'::text, 'MERGE'::text, 'BLENDING'::text, 'BAGGING'::text]))),
    CONSTRAINT "ledger_policies_tolerance_percent_check" CHECK (((tolerance_percent >= (0)::numeric) AND (tolerance_percent <= (100)::numeric)))
);

CREATE TABLE grainledger.ledger_positions (
    lot_id uuid NOT NULL,
    location_id uuid NOT NULL,
    quantity_kg numeric(20,6) NOT NULL,
    CONSTRAINT "ledger_positions_quantity_kg_check" CHECK ((quantity_kg >= (0)::numeric))
);

CREATE TABLE grainledger.ledger_postings (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    event_id uuid NOT NULL,
    lot_id uuid NOT NULL,
    location_id uuid NOT NULL,
    quantity_kg numeric(20,6) NOT NULL,
    reverses_posting_id uuid,
    CONSTRAINT "ledger_postings_quantity_kg_check" CHECK ((quantity_kg <> (0)::numeric))
);

CREATE TABLE grainledger.ledger_receipt_correction_blocks (
    correction_id uuid NOT NULL,
    receipt_id uuid NOT NULL,
    owner_id uuid NOT NULL,
    committed boolean DEFAULT false NOT NULL
);

CREATE TABLE grainledger.ledger_receipt_reversals (
    receipt_id uuid NOT NULL,
    correction_id uuid NOT NULL,
    owner_id uuid NOT NULL,
    evidence_id uuid NOT NULL
);

CREATE TABLE grainledger.ledger_receiving (
    procurement_id uuid NOT NULL,
    status text NOT NULL,
    version integer DEFAULT 0 NOT NULL,
    latest_measurement_set_id uuid,
    accepted_measurement_set_id uuid,
    accepted_review_id uuid,
    CONSTRAINT "ledger_receiving_check" CHECK (((status = ANY (ARRAY['RECEIVED_MATCHED'::text, 'RECEIVED_WITHIN_TOLERANCE'::text, 'RECEIVED_SHORTAGE_APPROVED'::text])) = (accepted_measurement_set_id IS NOT NULL))),
    CONSTRAINT "ledger_receiving_check1" CHECK (((status = 'RECEIVED_SHORTAGE_APPROVED'::text) = (accepted_review_id IS NOT NULL))),
    CONSTRAINT "ledger_receiving_status_check" CHECK ((status = ANY (ARRAY['PENDING_ARRIVAL'::text, 'AWAITING_MANAGER_REVIEW'::text, 'REMEASUREMENT_REQUESTED'::text, 'RECEIVED_MATCHED'::text, 'RECEIVED_WITHIN_TOLERANCE'::text, 'RECEIVED_SHORTAGE_APPROVED'::text, 'BLOCKED_UNRESOLVED'::text]))),
    CONSTRAINT "ledger_receiving_version_check" CHECK ((version >= 0))
);

CREATE TABLE grainledger.ledger_receiving_blocks (
    discrepancy_id uuid NOT NULL,
    lot_id uuid NOT NULL,
    blocked boolean NOT NULL,
    evidence_id uuid NOT NULL,
    reason text NOT NULL,
    real_difference boolean DEFAULT false NOT NULL
);

CREATE TABLE grainledger.ledger_receiving_threshold_default (
    singleton boolean DEFAULT true NOT NULL,
    percentage numeric(8,6) NOT NULL,
    revision uuid NOT NULL,
    CONSTRAINT "ledger_receiving_threshold_default_percentage_check" CHECK (((percentage >= (0)::numeric) AND (percentage < (100)::numeric))),
    CONSTRAINT "ledger_receiving_threshold_default_singleton_check" CHECK (singleton)
);

CREATE TABLE grainledger.ledger_receiving_threshold_history (
    revision uuid NOT NULL,
    company_id uuid,
    scope text NOT NULL,
    action text NOT NULL,
    old_percentage numeric(8,6),
    new_percentage numeric(8,6),
    actor_id uuid,
    recorded_at timestamp with time zone DEFAULT clock_timestamp() NOT NULL,
    reason text NOT NULL,
    migration_id text,
    CONSTRAINT "ledger_receiving_threshold_history_action_check" CHECK ((action = ANY (ARRAY['INITIALIZE'::text, 'CREATE'::text, 'UPDATE'::text, 'DELETE'::text]))),
    CONSTRAINT "ledger_receiving_threshold_history_check" CHECK (((scope = 'SYSTEM_DEFAULT'::text) = (company_id IS NULL))),
    CONSTRAINT "ledger_receiving_threshold_history_check1" CHECK ((((action = 'INITIALIZE'::text) AND (scope = 'SYSTEM_DEFAULT'::text) AND (actor_id IS NULL) AND (migration_id IS NOT NULL) AND (old_percentage IS NULL) AND (new_percentage IS NOT NULL)) OR ((action <> 'INITIALIZE'::text) AND (actor_id IS NOT NULL) AND (migration_id IS NULL) AND (((action = 'CREATE'::text) AND (scope = 'COMPANY_OVERRIDE'::text) AND (old_percentage IS NULL) AND (new_percentage IS NOT NULL)) OR ((action = 'UPDATE'::text) AND (old_percentage IS NOT NULL) AND (new_percentage IS NOT NULL)) OR ((action = 'DELETE'::text) AND (scope = 'COMPANY_OVERRIDE'::text) AND (old_percentage IS NOT NULL) AND (new_percentage IS NULL)))))),
    CONSTRAINT "ledger_receiving_threshold_history_new_percentage_check" CHECK (((new_percentage >= (0)::numeric) AND (new_percentage < (100)::numeric))),
    CONSTRAINT "ledger_receiving_threshold_history_old_percentage_check" CHECK (((old_percentage >= (0)::numeric) AND (old_percentage < (100)::numeric))),
    CONSTRAINT "ledger_receiving_threshold_history_reason_check" CHECK (((length(TRIM(BOTH FROM reason)) >= 1) AND (length(TRIM(BOTH FROM reason)) <= 4000))),
    CONSTRAINT "ledger_receiving_threshold_history_scope_check" CHECK ((scope = ANY (ARRAY['SYSTEM_DEFAULT'::text, 'COMPANY_OVERRIDE'::text])))
);

CREATE TABLE grainledger.ledger_receiving_threshold_overrides (
    company_id uuid NOT NULL,
    percentage numeric(8,6) NOT NULL,
    revision uuid NOT NULL,
    CONSTRAINT "ledger_receiving_threshold_overrides_percentage_check" CHECK (((percentage >= (0)::numeric) AND (percentage < (100)::numeric)))
);

CREATE TABLE grainledger.ledger_residue_evaluations (
    id uuid NOT NULL,
    lot_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    request jsonb NOT NULL,
    snapshot jsonb NOT NULL,
    persisted_at timestamp with time zone DEFAULT clock_timestamp() NOT NULL
);
