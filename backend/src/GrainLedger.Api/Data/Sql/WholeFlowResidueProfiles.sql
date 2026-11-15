-- Whole-flow residue contracts replace the earlier per-stage publication API.
CREATE OR REPLACE FUNCTION grainledger.ledger_residue_history(a uuid, l uuid, evaluation uuid DEFAULT NULL::uuid) RETURNS jsonb
    LANGUAGE plpgsql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE result jsonb;
BEGIN
    IF NOT grainledger.ledger_residue_authorized(a,l) THEN
        RAISE EXCEPTION 'Residue lot access denied' USING ERRCODE='42501';
    END IF;
    IF evaluation IS NOT NULL THEN
        SELECT snapshot INTO result FROM grainledger.ledger_residue_evaluations
        WHERE id=evaluation AND lot_id=l AND snapshot->>'engineVersion'='residue-flow-as-received-3';
        IF result IS NULL THEN RAISE EXCEPTION 'Residue evaluation not found' USING ERRCODE='P0002'; END IF;
        RETURN result;
    END IF;
    SELECT jsonb_build_object('items',COALESCE(jsonb_agg(snapshot ORDER BY persisted_at DESC,id),'[]'::jsonb)) INTO result
    FROM grainledger.ledger_residue_evaluations WHERE lot_id=l AND snapshot->>'engineVersion'='residue-flow-as-received-3';
    RETURN result;
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
    IF old.actor_id<>a OR old.lot_id<>l THEN
        RAISE EXCEPTION 'Residue evaluation ID reused with different actor or payload' USING ERRCODE='23505';
    END IF;
    IF old.snapshot->>'engineVersion' IS DISTINCT FROM 'residue-flow-as-received-3' THEN
        RAISE EXCEPTION 'Evaluation ID belongs to an unsupported residue engine; use a new ID' USING ERRCODE='23505';
    END IF;
    IF old.request<>p THEN
        RAISE EXCEPTION 'Residue evaluation ID reused with different actor or payload' USING ERRCODE='23505';
    END IF;
    RETURN old.snapshot;
END $$;

CREATE OR REPLACE FUNCTION grainledger.ledger_residue_append(a uuid, l uuid, evaluation uuid, p jsonb, s jsonb) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE old jsonb; persisted timestamptz := clock_timestamp();
BEGIN
    IF current_setting('transaction_isolation')<>'read committed' THEN
        RAISE EXCEPTION 'Residue persistence requires READ COMMITTED isolation' USING ERRCODE='23514';
    END IF;
    PERFORM grainledger.ledger_lock_writes();
    old:=grainledger.ledger_residue_replay(a,l,evaluation,p);
    IF old IS NOT NULL THEN RETURN old; END IF;
    persisted:=clock_timestamp();
    IF s->>'id' IS DISTINCT FROM evaluation::text OR s->>'lotId' IS DISTINCT FROM l::text
       OR s->>'companyId' IS DISTINCT FROM (SELECT owner_id::text FROM grainledger.ledger_lots WHERE id=l) THEN
        RAISE EXCEPTION 'Residue snapshot identity mismatch' USING ERRCODE='23514';
    END IF;
    IF s->>'engineVersion' IS DISTINCT FROM 'residue-flow-as-received-3' THEN
        RAISE EXCEPTION 'Unsupported residue engine' USING ERRCODE='23514';
    END IF;
    s:=s||jsonb_build_object('persistedAt',persisted);
    INSERT INTO grainledger.ledger_residue_evaluations(id,lot_id,actor_id,request,snapshot,persisted_at)
    VALUES(evaluation,l,a,p,s,persisted);
    RETURN s;
END $$;

CREATE OR REPLACE FUNCTION grainledger.catalog_publish_residue_rule(a uuid, p jsonb) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE previous grainledger.catalog_residue_rule_versions%ROWTYPE; company uuid; item jsonb;
BEGIN
    PERFORM pg_advisory_xact_lock(714928352);
    IF jsonb_typeof(p) IS DISTINCT FROM 'object' OR COALESCE(jsonb_typeof(p->'CompanyId'),'null') NOT IN ('null','string') THEN
        RAISE EXCEPTION 'Invalid residue policy scope' USING ERRCODE='22023';
    END IF;
    company := (p->>'CompanyId')::uuid;
    IF NOT EXISTS(SELECT 1 FROM grainledger.identity_users u JOIN grainledger.identity_organizations o ON o."Id"=u."OrganizationId"
        WHERE u."Id"=a AND u."IsActive" AND
        (company IS NULL AND u."Role"='Administrator' AND o."Kind"='Mill'
         OR company IS NOT NULL AND u."Role"='CustomerManager' AND o."Kind"='Customer' AND o."Id"=company)) THEN
        RAISE EXCEPTION 'Residue policy publication denied' USING ERRCODE='42501';
    END IF;
    PERFORM grainledger.catalog_validate_residue_policy_review(p);
    IF (p->>'PublishedBy')::uuid IS DISTINCT FROM a OR p->>'Kind' IS DISTINCT FROM 'FLOW'
        OR p->'SchemaVersion' IS DISTINCT FROM '2'::jsonb OR p->'AsReceivedConfirmed' IS DISTINCT FROM 'true'::jsonb
        OR EXISTS(SELECT 1 FROM jsonb_object_keys(p) k WHERE k <> ALL(ARRAY['Id','Code','VersionNo','Kind','SchemaVersion','SubstanceId','CommodityId','ResidueDefinition','Unit','DecimalPlaces','Laboratory','Steps','AsReceivedConfirmed','EffectiveFrom','EffectiveTo','SupersedesId','EvidenceRef','ApplicabilityStatement','ReviewedBy','ReviewAuthority','ReviewedAt','PublishedBy','PublishedAt','CompanyId']))
        OR EXISTS(SELECT 1 FROM unnest(ARRAY['Code','SubstanceId','CommodityId','ResidueDefinition','Unit']) k
            WHERE jsonb_typeof(p->k) IS DISTINCT FROM 'string' OR length(btrim(p->>k))=0)
        OR length(p->>'Code')>100 OR p->>'Unit' IS DISTINCT FROM 'mg/kg'
        OR jsonb_typeof(p->'VersionNo') IS DISTINCT FROM 'number' OR p->>'VersionNo' !~ '^[0-9]+$'
        OR jsonb_typeof(p->'DecimalPlaces') IS DISTINCT FROM 'number' OR p->>'DecimalPlaces' !~ '^[0-9]+$'
        OR jsonb_typeof(p->'Laboratory') IS DISTINCT FROM 'array' OR jsonb_typeof(p->'Steps') IS DISTINCT FROM 'array'
    THEN RAISE EXCEPTION 'Incomplete reviewed residue profile' USING ERRCODE='22023'; END IF;
    IF (p->>'VersionNo')::int<1 OR (p->>'DecimalPlaces')::int NOT BETWEEN 0 AND 18
        OR jsonb_array_length(p->'Laboratory')+jsonb_array_length(p->'Steps')=0
        OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_active_substances WHERE id=(p->>'SubstanceId')::uuid)
        OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_commodities WHERE id=(p->>'CommodityId')::uuid)
    THEN RAISE EXCEPTION 'Invalid residue profile catalog or precision' USING ERRCODE='22023'; END IF;
    FOR item IN SELECT value FROM jsonb_array_elements(p->'Laboratory') LOOP
        IF jsonb_typeof(item) IS DISTINCT FROM 'object'
            OR EXISTS(SELECT 1 FROM jsonb_object_keys(item) k WHERE k <> ALL(ARRAY['AnalyticalMethod','NumericInterpretationKind','RelativeUncertaintyPercent','RelativeUncertaintyAtZero','BelowLimit','NdThresholdKind']))
            OR jsonb_typeof(item->'AnalyticalMethod') IS DISTINCT FROM 'string' OR length(btrim(item->>'AnalyticalMethod'))=0
            OR COALESCE(jsonb_typeof(item->'NumericInterpretationKind'),'null') NOT IN ('null','string')
            OR item->>'NumericInterpretationKind' IS NOT NULL AND item->>'NumericInterpretationKind' NOT IN ('NUMERIC_ABSOLUTE_UNCERTAINTY','NUMERIC_RELATIVE_UNCERTAINTY')
            OR COALESCE(jsonb_typeof(item->'NdThresholdKind'),'null') NOT IN ('null','string')
            OR item->>'NdThresholdKind' IS NOT NULL AND item->>'NdThresholdKind' NOT IN ('REPORTING_THRESHOLD','LOD','LOQ')
            OR jsonb_typeof(item->'BelowLimit') IS DISTINCT FROM 'boolean'
            OR jsonb_typeof(item->'RelativeUncertaintyAtZero') IS DISTINCT FROM 'boolean'
            OR (item->>'NumericInterpretationKind' IS NULL AND NOT (item->>'BelowLimit')::boolean AND item->>'NdThresholdKind' IS NULL)
            OR (item->>'NumericInterpretationKind'='NUMERIC_RELATIVE_UNCERTAINTY' AND
                (jsonb_typeof(item->'RelativeUncertaintyPercent') IS DISTINCT FROM 'string' OR item->>'RelativeUncertaintyPercent' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$'))
            OR (item->>'NumericInterpretationKind' IS DISTINCT FROM 'NUMERIC_RELATIVE_UNCERTAINTY'
                AND (item->>'RelativeUncertaintyPercent' IS NOT NULL OR (item->>'RelativeUncertaintyAtZero')::boolean))
        THEN RAISE EXCEPTION 'Invalid laboratory profile entry' USING ERRCODE='22023'; END IF;
    END LOOP;
    IF EXISTS(SELECT 1 FROM jsonb_array_elements(p->'Laboratory') x GROUP BY x->>'AnalyticalMethod' HAVING count(*)>1) THEN
        RAISE EXCEPTION 'Duplicate laboratory method' USING ERRCODE='22023';
    END IF;
    FOR item IN SELECT value FROM jsonb_array_elements(p->'Steps') LOOP
        IF jsonb_typeof(item) IS DISTINCT FROM 'object'
            OR EXISTS(SELECT 1 FROM jsonb_object_keys(item) k WHERE k <> ALL(ARRAY['Id','Process','InputStage','OutputStage','OutputFraction','Conditions','FactorLower','FactorUpper','FactorSemantics','AnchorStage','HomogeneousMixing']))
            OR COALESCE(item->>'Id','') !~ '^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$'
            OR item->>'Id'='00000000-0000-0000-0000-000000000000'
            OR EXISTS(SELECT 1 FROM unnest(ARRAY['Process','InputStage','OutputStage','OutputFraction','Conditions','FactorLower','FactorUpper','FactorSemantics']) k
                WHERE jsonb_typeof(item->k) IS DISTINCT FROM 'string' OR length(btrim(item->>k))=0)
            OR item->>'FactorLower' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$'
            OR item->>'FactorUpper' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$'
            OR jsonb_typeof(item->'HomogeneousMixing') IS DISTINCT FROM 'boolean'
            OR item->>'FactorSemantics' NOT IN ('STEP_CONCENTRATION','CUMULATIVE_CONCENTRATION')
            OR item->>'FactorSemantics'='STEP_CONCENTRATION' AND item->>'AnchorStage' IS NOT NULL
            OR item->>'FactorSemantics'='CUMULATIVE_CONCENTRATION' AND COALESCE(item->>'AnchorStage','') NOT IN ('RAW_PADDY','DRIED_PADDY','HEAD_RICE','BROKEN_RICE','BRAN','HUSK','BLENDED_RICE','BAGGED_RICE')
        THEN RAISE EXCEPTION 'Invalid residue process entry' USING ERRCODE='22023'; END IF;
        IF (item->>'FactorLower')::numeric>(item->>'FactorUpper')::numeric OR NOT (
            item->>'Process'='DRYING' AND item->>'InputStage'='RAW_PADDY' AND item->>'OutputStage'='DRIED_PADDY' AND item->>'OutputFraction'='DRIED_PADDY'
            OR item->>'Process'='MILLING' AND item->>'InputStage'='DRIED_PADDY' AND item->>'OutputStage'=item->>'OutputFraction' AND item->>'OutputStage' IN ('HEAD_RICE','BROKEN_RICE','BRAN','HUSK')
            OR item->>'Process'='BLENDING' AND item->>'InputStage' IN ('HEAD_RICE','BROKEN_RICE','BLENDED_RICE') AND item->>'OutputStage'='BLENDED_RICE' AND item->>'OutputFraction'='BLENDED_RICE'
            OR item->>'Process'='BAGGING' AND item->>'InputStage' IN ('HEAD_RICE','BROKEN_RICE','BLENDED_RICE') AND item->>'OutputStage'='BAGGED_RICE' AND item->>'OutputFraction'=item->>'InputStage'
            OR item->>'Process' IN ('MERGE','CUSTODY_INGRESS','CUSTODY_WITHDRAW') AND item->>'InputStage'=item->>'OutputStage' AND
                (item->>'InputStage' IN ('RAW_PADDY','DRIED_PADDY','HEAD_RICE','BROKEN_RICE','BRAN','HUSK','BLENDED_RICE') AND item->>'OutputFraction'=item->>'InputStage'
                 OR item->>'InputStage'='BAGGED_RICE' AND item->>'OutputFraction' IN ('HEAD_RICE','BROKEN_RICE','BLENDED_RICE')))
        THEN RAISE EXCEPTION 'Invalid residue process path or interval' USING ERRCODE='22023'; END IF;
        IF item->>'FactorSemantics'='CUMULATIVE_CONCENTRATION' AND NOT (
            item->>'AnchorStage'='RAW_PADDY'
            OR item->>'AnchorStage'='DRIED_PADDY' AND item->>'InputStage'<>'RAW_PADDY'
            OR item->>'AnchorStage' IN ('HEAD_RICE','BROKEN_RICE') AND
                (item->>'AnchorStage'=item->>'InputStage' OR item->>'InputStage' IN ('BLENDED_RICE','BAGGED_RICE'))
            OR item->>'AnchorStage' IN ('BRAN','HUSK','BAGGED_RICE') AND item->>'AnchorStage'=item->>'InputStage'
            OR item->>'AnchorStage'='BLENDED_RICE' AND item->>'InputStage' IN ('BLENDED_RICE','BAGGED_RICE'))
        THEN RAISE EXCEPTION 'Unsupported cumulative anchor' USING ERRCODE='22023'; END IF;
    END LOOP;
    IF EXISTS(SELECT 1 FROM jsonb_array_elements(p->'Steps') x GROUP BY (x->>'Id')::uuid HAVING count(*)>1)
        OR EXISTS(SELECT 1 FROM jsonb_array_elements(p->'Steps') WITH ORDINALITY x(v,n)
            JOIN jsonb_array_elements(p->'Steps') WITH ORDINALITY y(v,n) ON x.n<y.n
            AND x.v->>'Process'=y.v->>'Process' AND x.v->>'InputStage'=y.v->>'InputStage'
            AND x.v->>'OutputStage'=y.v->>'OutputStage' AND x.v->>'OutputFraction'=y.v->>'OutputFraction'
            AND (x.v->>'Conditions'=y.v->>'Conditions' OR x.v->>'Conditions'='UNCONDITIONAL' OR y.v->>'Conditions'='UNCONDITIONAL'))
    THEN RAISE EXCEPTION 'Duplicate or overlapping residue process entry' USING ERRCODE='22023'; END IF;
    IF EXISTS(SELECT 1 FROM grainledger.catalog_residue_rule_versions r WHERE r.code=p->>'Code'
        AND (r.payload->>'Kind' IS DISTINCT FROM 'FLOW' OR r.payload->>'SchemaVersion' IS DISTINCT FROM '2')) THEN
        RAISE EXCEPTION 'Legacy profile code cannot be reused' USING ERRCODE='23505';
    END IF;
    IF EXISTS(SELECT 1 FROM grainledger.catalog_residue_rule_versions r WHERE r.code=p->>'Code'
        AND (r.company_id IS NOT DISTINCT FROM company OR r.company_id IS NULL OR company IS NULL)
        AND (r.substance_id IS DISTINCT FROM (p->>'SubstanceId')::uuid OR r.commodity_id IS DISTINCT FROM (p->>'CommodityId')::uuid
            OR r.payload->>'Kind' IS DISTINCT FROM 'FLOW' OR r.payload->>'SchemaVersion' IS DISTINCT FROM '2')) THEN
        RAISE EXCEPTION 'Profile code must retain FLOW, substance and commodity scope' USING ERRCODE='23505';
    END IF;
    IF EXISTS(SELECT 1 FROM grainledger.catalog_residue_rule_versions WHERE company_id IS NOT DISTINCT FROM company
        AND code=p->>'Code' AND version_no >= (p->>'VersionNo')::int) THEN
        RAISE EXCEPTION 'Residue version must advance in its scope' USING ERRCODE='23505';
    END IF;
    IF p->>'SupersedesId' IS NOT NULL THEN
        SELECT * INTO previous FROM grainledger.catalog_residue_rule_versions WHERE id=(p->>'SupersedesId')::uuid;
        IF previous.id IS NULL OR previous.code IS DISTINCT FROM p->>'Code' OR previous.company_id IS DISTINCT FROM company
            OR previous.substance_id IS DISTINCT FROM (p->>'SubstanceId')::uuid OR previous.commodity_id IS DISTINCT FROM (p->>'CommodityId')::uuid
            OR previous.payload->>'Kind' IS DISTINCT FROM 'FLOW' OR previous.payload->>'SchemaVersion' IS DISTINCT FROM '2'
            OR EXISTS(SELECT 1 FROM grainledger.catalog_residue_rule_versions WHERE (payload->>'SupersedesId')::uuid=(p->>'SupersedesId')::uuid) THEN
            RAISE EXCEPTION 'Invalid reviewed rule supersession' USING ERRCODE='23505';
        END IF;
    ELSE
        SELECT * INTO previous FROM grainledger.catalog_residue_rule_versions
        WHERE company_id IS NOT DISTINCT FROM company AND code=p->>'Code' ORDER BY version_no DESC LIMIT 1;
    END IF;
    IF previous.id IS NOT NULL AND EXISTS(SELECT 1 FROM jsonb_array_elements(previous.payload->'Steps') old
        CROSS JOIN jsonb_array_elements(p->'Steps') new
        WHERE old->>'Process'=new->>'Process' AND old->>'InputStage'=new->>'InputStage'
            AND old->>'OutputStage'=new->>'OutputStage' AND old->>'OutputFraction'=new->>'OutputFraction'
            AND old->>'Conditions'=new->>'Conditions' AND (old->>'Id')::uuid<>(new->>'Id')::uuid)
        OR EXISTS(SELECT 1 FROM grainledger.catalog_residue_rule_versions r
            CROSS JOIN LATERAL jsonb_array_elements(r.payload->'Steps') old
            CROSS JOIN jsonb_array_elements(p->'Steps') new
            WHERE r.code=p->>'Code' AND r.company_id IS NOT DISTINCT FROM company
                AND r.payload->>'Kind'='FLOW' AND r.payload->>'SchemaVersion'='2'
                AND (old->>'Id')::uuid=(new->>'Id')::uuid
                AND (old->>'Process',old->>'InputStage',old->>'OutputStage',old->>'OutputFraction',old->>'Conditions')
                    IS DISTINCT FROM (new->>'Process',new->>'InputStage',new->>'OutputStage',new->>'OutputFraction',new->>'Conditions'))
    THEN
        RAISE EXCEPTION 'Process row ID must match its physical applicability key' USING ERRCODE='23505';
    END IF;
    INSERT INTO grainledger.catalog_residue_rule_versions(id,code,version_no,substance_id,commodity_id,effective_from,effective_to,published_by,published_at,payload,company_id)
    VALUES((p->>'Id')::uuid,p->>'Code',(p->>'VersionNo')::int,(p->>'SubstanceId')::uuid,(p->>'CommodityId')::uuid,
        (p->>'EffectiveFrom')::timestamptz,(p->>'EffectiveTo')::timestamptz,a,(p->>'PublishedAt')::timestamptz,p,company);
END $$;

CREATE OR REPLACE FUNCTION grainledger.catalog_publish_residue_policy_setting(a uuid, p jsonb) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE previous grainledger.catalog_residue_policy_setting_versions%ROWTYPE; company uuid;
BEGIN
    PERFORM pg_advisory_xact_lock(714928352);
    IF jsonb_typeof(p) IS DISTINCT FROM 'object' OR jsonb_typeof(p->'CompanyId') IS DISTINCT FROM 'string' THEN
        RAISE EXCEPTION 'Explicit company setting scope required' USING ERRCODE='22023';
    END IF;
    company := (p->>'CompanyId')::uuid;
    IF NOT EXISTS(SELECT 1 FROM grainledger.identity_users u JOIN grainledger.identity_organizations o ON o."Id"=u."OrganizationId"
        WHERE u."Id"=a AND u."IsActive" AND u."Role"='CustomerManager' AND o."Kind"='Customer' AND o."Id"=company) THEN
        RAISE EXCEPTION 'Company residue setting publication denied' USING ERRCODE='42501';
    END IF;
    PERFORM grainledger.catalog_validate_residue_policy_review(p);
    IF (p->>'PublishedBy')::uuid IS DISTINCT FROM a
        OR jsonb_typeof(p->'RuleCode') IS DISTINCT FROM 'string' OR length(btrim(p->>'RuleCode'))=0 OR length(p->>'RuleCode')>100
        OR jsonb_typeof(p->'VersionNo') IS DISTINCT FROM 'number' OR p->>'VersionNo' !~ '^[0-9]+$'
        OR COALESCE(p->>'Mode','') NOT IN ('SYSTEM_DEFAULT','COMPANY_OVERRIDE') THEN
        RAISE EXCEPTION 'Incomplete reviewed company residue setting' USING ERRCODE='22023';
    END IF;
    IF (p->>'VersionNo')::int<1 OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_residue_rule_versions
        WHERE code=p->>'RuleCode' AND payload->>'Kind'='FLOW' AND payload->>'SchemaVersion'='2'
        AND (p->>'Mode'='SYSTEM_DEFAULT' AND company_id IS NULL OR p->>'Mode'='COMPANY_OVERRIDE' AND company_id=company)) THEN
        RAISE EXCEPTION 'Reviewed setting requires visible profile in selected mode' USING ERRCODE='22023';
    END IF;
    IF EXISTS(SELECT 1 FROM grainledger.catalog_residue_policy_setting_versions
        WHERE company_id=company AND rule_code=p->>'RuleCode' AND version_no >= (p->>'VersionNo')::int) THEN
        RAISE EXCEPTION 'Company setting version must advance' USING ERRCODE='23505';
    END IF;
    IF p->>'SupersedesId' IS NOT NULL THEN
        SELECT * INTO previous FROM grainledger.catalog_residue_policy_setting_versions WHERE id=(p->>'SupersedesId')::uuid;
        IF previous.id IS NULL OR previous.company_id IS DISTINCT FROM company OR previous.rule_code IS DISTINCT FROM p->>'RuleCode'
            OR EXISTS(SELECT 1 FROM grainledger.catalog_residue_policy_setting_versions WHERE (payload->>'SupersedesId')::uuid=(p->>'SupersedesId')::uuid) THEN
            RAISE EXCEPTION 'Invalid reviewed setting supersession' USING ERRCODE='23505';
        END IF;
    END IF;
    INSERT INTO grainledger.catalog_residue_policy_setting_versions(id,company_id,rule_code,version_no,effective_from,effective_to,published_by,published_at,payload)
    VALUES((p->>'Id')::uuid,company,p->>'RuleCode',(p->>'VersionNo')::int,(p->>'EffectiveFrom')::timestamptz,
        (p->>'EffectiveTo')::timestamptz,a,(p->>'PublishedAt')::timestamptz,p);
END $$;

CREATE OR REPLACE FUNCTION grainledger.capture_residue_review_gate(a uuid, p jsonb) RETURNS void
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM grainledger.identity_users u JOIN grainledger.identity_organizations o ON o."Id"=u."OrganizationId"
        WHERE u."Id"=a AND u."IsActive" AND u."Role"='CustomerManager' AND o."Kind"='Customer'
            AND u."OrganizationId"=(p->>'CompanyId')::uuid) THEN
        RAISE EXCEPTION 'Residue evidence publication denied' USING ERRCODE='42501';
    END IF;
    PERFORM grainledger.catalog_validate_residue_policy_review(p);
    IF p->'SchemaVersion' IS DISTINCT FROM '2'::jsonb OR p->'AsReceivedConfirmed' IS DISTINCT FROM 'true'::jsonb
        OR p ?| ARRAY['MaterialBasis','InputBasis','OutputBasis','Basis','materialBasis','inputBasis','outputBasis','basis']
        OR p->>'Id' IS NULL OR (p->>'Id')::uuid='00000000-0000-0000-0000-000000000000'
        OR (p->>'PublishedBy')::uuid IS DISTINCT FROM a OR (p->>'PublishedAt')::timestamptz IS NULL
        OR (p->>'PublishedAt')::timestamptz > clock_timestamp()
        OR (p->>'ReviewedAt')::timestamptz IS NULL OR (p->>'ReviewedAt')::timestamptz > clock_timestamp()
        OR (p->>'EffectiveFrom')::timestamptz IS NULL
        OR (p->>'EffectiveTo')::timestamptz <= (p->>'EffectiveFrom')::timestamptz
        OR EXISTS(SELECT 1 FROM unnest(ARRAY['EvidenceRef','ApplicabilityStatement','ReviewedBy','ReviewAuthority','ResidueDefinition','ProfileCode']) k
            WHERE length(btrim(COALESCE(p->>k,'')))=0)
        OR NOT EXISTS(SELECT 1 FROM grainledger.catalog_active_substances WHERE id=(p->>'SubstanceId')::uuid)
    THEN RAISE EXCEPTION 'Incomplete reviewed residue evidence' USING ERRCODE='22023'; END IF;
END $$;

-- Used only by protected publishers; references are checked at procurement receipt time.
CREATE FUNCTION grainledger.capture_residue_profile_binding_valid(p jsonb, source_id uuid, rule_id uuid, setting_id uuid) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT EXISTS(
        SELECT 1 FROM grainledger.capture_source_lots sl
        JOIN grainledger.capture_procurement_items i ON i.id=sl.procurement_item_id AND i.company_id=sl.company_id
        JOIN grainledger.capture_procurements pr ON pr.id=i.procurement_id AND pr.company_id=i.company_id
        JOIN grainledger.ledger_lots l ON l.id=sl.lot_id
        LEFT JOIN grainledger.ledger_catalog_commodities mapping ON mapping.ledger_commodity_id=l.commodity_id
        JOIN grainledger.catalog_residue_rule_versions r ON r.id=rule_id
        JOIN grainledger.catalog_residue_policy_setting_versions s ON s.id=setting_id
        WHERE sl.id=source_id AND sl.company_id=(p->>'CompanyId')::uuid AND l.owner_id=sl.company_id
          AND r.code=p->>'ProfileCode' AND r.substance_id=(p->>'SubstanceId')::uuid
          AND r.commodity_id=COALESCE(mapping.catalog_commodity_id,l.commodity_id)
          AND r.payload->>'ResidueDefinition'=p->>'ResidueDefinition'
          AND r.payload->>'Kind'='FLOW' AND r.payload->>'SchemaVersion'='2'
          AND s.company_id=sl.company_id AND s.rule_code=r.code
          AND (s.payload->>'Mode'='SYSTEM_DEFAULT' AND r.company_id IS NULL
               OR s.payload->>'Mode'='COMPANY_OVERRIDE' AND r.company_id=sl.company_id)
          AND r.published_at <= (p->>'PublishedAt')::timestamptz AND s.published_at <= (p->>'PublishedAt')::timestamptz
          AND r.effective_from<=pr.received_at AND (r.effective_to IS NULL OR pr.received_at<r.effective_to)
          AND s.effective_from<=pr.received_at AND (s.effective_to IS NULL OR pr.received_at<s.effective_to)
          AND (WITH RECURSIVE removed(id) AS (
              SELECT (v.payload->>'SupersedesId')::uuid FROM grainledger.catalog_residue_rule_versions v
              WHERE v.code=r.code AND v.company_id IS NOT DISTINCT FROM r.company_id
                AND v.payload->>'Kind'='FLOW' AND v.payload->>'SchemaVersion'='2'
                AND v.published_at <= (p->>'PublishedAt')::timestamptz
                AND v.effective_from<=pr.received_at AND (v.effective_to IS NULL OR pr.received_at<v.effective_to)
                AND v.payload->>'SupersedesId' IS NOT NULL
              UNION
              SELECT (v.payload->>'SupersedesId')::uuid FROM removed x
              JOIN grainledger.catalog_residue_rule_versions v ON v.id=x.id
              WHERE v.payload->>'SupersedesId' IS NOT NULL
          )
          SELECT count(*)=1 AND bool_or(v.id=r.id) FROM grainledger.catalog_residue_rule_versions v
          WHERE v.code=r.code AND v.company_id IS NOT DISTINCT FROM r.company_id
            AND v.payload->>'Kind'='FLOW' AND v.payload->>'SchemaVersion'='2'
            AND v.published_at <= (p->>'PublishedAt')::timestamptz
            AND v.effective_from<=pr.received_at AND (v.effective_to IS NULL OR pr.received_at<v.effective_to)
            AND NOT EXISTS(SELECT 1 FROM removed WHERE id=v.id))
          AND (WITH RECURSIVE removed(id) AS (
              SELECT (v.payload->>'SupersedesId')::uuid FROM grainledger.catalog_residue_policy_setting_versions v
              WHERE v.company_id=s.company_id AND v.rule_code=s.rule_code
                AND v.published_at <= (p->>'PublishedAt')::timestamptz
                AND v.effective_from<=pr.received_at AND (v.effective_to IS NULL OR pr.received_at<v.effective_to)
                AND v.payload->>'SupersedesId' IS NOT NULL
              UNION
              SELECT (v.payload->>'SupersedesId')::uuid FROM removed x
              JOIN grainledger.catalog_residue_policy_setting_versions v ON v.id=x.id
              WHERE v.payload->>'SupersedesId' IS NOT NULL
          )
          SELECT count(*)=1 AND bool_or(v.id=s.id) FROM grainledger.catalog_residue_policy_setting_versions v
          WHERE v.company_id=s.company_id AND v.rule_code=s.rule_code
            AND v.published_at <= (p->>'PublishedAt')::timestamptz
            AND v.effective_from<=pr.received_at AND (v.effective_to IS NULL OR pr.received_at<v.effective_to)
            AND NOT EXISTS(SELECT 1 FROM removed WHERE id=v.id))
    )
$$;
REVOKE ALL ON FUNCTION grainledger.capture_residue_profile_binding_valid(jsonb,uuid,uuid,uuid) FROM PUBLIC;

CREATE OR REPLACE FUNCTION grainledger.capture_publish_residue_selection(a uuid, p jsonb) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE previous jsonb; raw_result grainledger.capture_lab_results; profile jsonb;
BEGIN
    PERFORM pg_advisory_xact_lock(714928352);
    PERFORM grainledger.capture_residue_review_gate(a,p);
    IF p->>'SchemaVersion' IS DISTINCT FROM '2' OR jsonb_typeof(p) IS DISTINCT FROM 'object'
        OR EXISTS(SELECT 1 FROM jsonb_object_keys(p) k WHERE k <> ALL(ARRAY[
            'Id','CompanyId','SourceId','SubstanceId','SelectedResultId','SupersededResultIds','ResidueDefinition',
            'AbsoluteUncertainty','ReportingThreshold','EffectiveFrom','EffectiveTo','SupersedesId','EvidenceRef',
            'ApplicabilityStatement','ReviewedBy','ReviewAuthority','ReviewedAt','PublishedBy','PublishedAt',
            'BindingReportId','ProfileRuleId','ProfileSettingId','BindingRequest','BindingFingerprint','BindingNote',
            'SchemaVersion','AsReceivedConfirmed','ProfileCode','LaboratoryMethod']))
        OR EXISTS(SELECT 1 FROM unnest(ARRAY['Id','CompanyId','SourceId','SubstanceId','SelectedResultId',
            'ResidueDefinition','EffectiveFrom','EvidenceRef','ApplicabilityStatement','ReviewedBy','ReviewAuthority',
            'ReviewedAt','PublishedBy','PublishedAt','ProfileRuleId','ProfileSettingId','ProfileCode','LaboratoryMethod']) k
            WHERE jsonb_typeof(p->k) IS DISTINCT FROM 'string')
        OR EXISTS(SELECT 1 FROM unnest(ARRAY['AbsoluteUncertainty','ReportingThreshold','EffectiveTo','SupersedesId',
            'BindingReportId','BindingRequest','BindingFingerprint','BindingNote']) k
            WHERE p ? k AND jsonb_typeof(p->k) NOT IN ('string','null'))
        OR jsonb_typeof(p->'SupersededResultIds') IS DISTINCT FROM 'array'
    THEN RAISE EXCEPTION 'Invalid selection evidence payload' USING ERRCODE='22023'; END IF;
    IF EXISTS(SELECT 1 FROM jsonb_array_elements(p->'SupersededResultIds') item WHERE jsonb_typeof(item) IS DISTINCT FROM 'string')
        OR EXISTS(SELECT 1 FROM unnest(ARRAY['EffectiveFrom','EffectiveTo','ReviewedAt','PublishedAt']) k
            WHERE p->>k IS NOT NULL AND p->>k !~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-](0[0-9]|1[0-3]):[0-5][0-9]|[+-]14:00)$')
    THEN RAISE EXCEPTION 'Invalid selection evidence payload' USING ERRCODE='22023'; END IF;
    IF EXISTS(SELECT 1 FROM unnest(ARRAY['Id','CompanyId','SourceId','SubstanceId','SelectedResultId','SupersedesId',
        'PublishedBy','BindingReportId','ProfileRuleId','ProfileSettingId']) k
        WHERE p->>k IS NOT NULL AND p->>k !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')
        OR EXISTS(SELECT 1 FROM jsonb_array_elements_text(p->'SupersededResultIds') item
            WHERE item !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')
    THEN RAISE EXCEPTION 'Invalid selection evidence identifier' USING ERRCODE='22023'; END IF;
    p := jsonb_set(p,'{PublishedAt}',to_jsonb(clock_timestamp()));
    IF NOT grainledger.capture_residue_confirmed_result((p->>'CompanyId')::uuid,(p->>'SourceId')::uuid,
        (p->>'SelectedResultId')::uuid,(p->>'SubstanceId')::uuid)
        OR jsonb_typeof(p->'SupersededResultIds') IS DISTINCT FROM 'array' OR jsonb_array_length(p->'SupersededResultIds')>1000
        OR EXISTS(SELECT 1 FROM jsonb_array_elements_text(p->'SupersededResultIds') r WHERE r::uuid=(p->>'SelectedResultId')::uuid
            OR NOT grainledger.capture_residue_confirmed_result((p->>'CompanyId')::uuid,(p->>'SourceId')::uuid,r::uuid,(p->>'SubstanceId')::uuid))
        OR NOT grainledger.capture_residue_profile_binding_valid(p,(p->>'SourceId')::uuid,(p->>'ProfileRuleId')::uuid,(p->>'ProfileSettingId')::uuid)
    THEN RAISE EXCEPTION 'Selection evidence or profile binding not applicable' USING ERRCODE='22023'; END IF;
    SELECT * INTO raw_result FROM grainledger.capture_lab_results WHERE id=(p->>'SelectedResultId')::uuid;
    SELECT payload INTO profile FROM grainledger.catalog_residue_rule_versions WHERE id=(p->>'ProfileRuleId')::uuid;
    IF raw_result.method IS DISTINCT FROM p->>'LaboratoryMethod' OR NOT EXISTS(
        SELECT 1 FROM jsonb_array_elements(profile->'Laboratory') lab WHERE lab->>'AnalyticalMethod'=raw_result.method
        AND (raw_result.result_kind='NUMERIC' AND lab->>'NumericInterpretationKind' IS NOT NULL
             OR raw_result.result_kind='BELOW_LIMIT' AND lab->'BelowLimit'='true'::jsonb
             OR raw_result.result_kind='NOT_DETECTED' AND lab->>'NdThresholdKind' IS NOT NULL))
    THEN RAISE EXCEPTION 'Laboratory profile does not support result' USING ERRCODE='22023'; END IF;
    IF raw_result.unit NOT IN ('mg/kg','µg/kg','μg/kg','ug/kg') OR raw_result.unit IS NULL
        OR (SELECT count(DISTINCT r.code)
            FROM grainledger.catalog_residue_rule_versions r
            JOIN grainledger.catalog_residue_policy_setting_versions s
                ON s.rule_code=r.code AND s.company_id=(p->>'CompanyId')::uuid
            WHERE r.substance_id=(p->>'SubstanceId')::uuid
                AND r.payload->>'Kind'='FLOW' AND r.payload->>'SchemaVersion'='2'
                AND r.payload->>'Unit'='mg/kg'
                AND EXISTS(SELECT 1 FROM jsonb_array_elements(
                    CASE WHEN jsonb_typeof(r.payload->'Laboratory')='array' THEN r.payload->'Laboratory' ELSE '[]'::jsonb END) lab
                    WHERE lab->>'AnalyticalMethod'=raw_result.method
                        AND (raw_result.result_kind='NUMERIC' AND lab->>'NumericInterpretationKind' IN
                                ('NUMERIC_ABSOLUTE_UNCERTAINTY','NUMERIC_RELATIVE_UNCERTAINTY')
                            OR raw_result.result_kind='BELOW_LIMIT' AND lab->'BelowLimit'='true'::jsonb
                            OR raw_result.result_kind='NOT_DETECTED' AND lab->>'NdThresholdKind' IS NOT NULL))
                AND grainledger.capture_residue_profile_binding_valid(
                    jsonb_set(jsonb_set(p,'{ProfileCode}',to_jsonb(r.code)),'{ResidueDefinition}',r.payload->'ResidueDefinition'),
                    (p->>'SourceId')::uuid,r.id,s.id)) <> 1
    THEN RAISE EXCEPTION 'Selection requires exactly one compatible adopted profile' USING ERRCODE='22023'; END IF;
    IF p->>'AbsoluteUncertainty' IS NOT NULL AND (p->>'AbsoluteUncertainty' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$'
        OR raw_result.result_kind<>'NUMERIC' OR length(btrim(COALESCE(raw_result.uncertainty,'')))=0)
        OR p->>'ReportingThreshold' IS NOT NULL AND (p->>'ReportingThreshold' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$'
            OR (p->>'ReportingThreshold')::numeric<=0 OR raw_result.result_kind<>'NOT_DETECTED')
    THEN RAISE EXCEPTION 'Normalized uncertainty/threshold lacks applicable raw evidence' USING ERRCODE='22023'; END IF;
    IF p->>'SupersedesId' IS NOT NULL THEN
        SELECT payload INTO previous FROM grainledger.capture_residue_report_selections WHERE id=(p->>'SupersedesId')::uuid;
        IF previous IS NULL OR previous->>'SchemaVersion' IS DISTINCT FROM '2'
            OR (previous->>'CompanyId')::uuid IS DISTINCT FROM (p->>'CompanyId')::uuid
            OR (previous->>'SourceId')::uuid IS DISTINCT FROM (p->>'SourceId')::uuid
            OR (previous->>'SubstanceId')::uuid IS DISTINCT FROM (p->>'SubstanceId')::uuid
            OR EXISTS(SELECT 1 FROM grainledger.capture_residue_report_selections WHERE (payload->>'SupersedesId')::uuid=(p->>'SupersedesId')::uuid)
        THEN RAISE EXCEPTION 'Invalid reviewed selection supersession' USING ERRCODE='23505'; END IF;
    END IF;
    INSERT INTO grainledger.capture_residue_report_selections VALUES((p->>'Id')::uuid,(p->>'CompanyId')::uuid,
        (p->>'SourceId')::uuid,(p->>'SubstanceId')::uuid,(p->>'EffectiveFrom')::timestamptz,(p->>'EffectiveTo')::timestamptz,
        a,(p->>'PublishedAt')::timestamptz,p);
END $$;

CREATE OR REPLACE FUNCTION grainledger.capture_publish_residue_group(a uuid, p jsonb) RETURNS void
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE members uuid[]; previous jsonb;
BEGIN
    PERFORM pg_advisory_xact_lock(714928352);
    PERFORM grainledger.capture_residue_review_gate(a,p);
    IF p->>'SchemaVersion' IS DISTINCT FROM '2' OR jsonb_typeof(p) IS DISTINCT FROM 'object'
        OR EXISTS(SELECT 1 FROM jsonb_object_keys(p) k WHERE k <> ALL(ARRAY[
            'Id','CompanyId','Code','VersionNo','SubstanceId','SourceIds','ProductStage','ProductIdentity',
            'ResidueDefinition','Lower','Upper','Unit','Precedence','SupportingResultIds','EffectiveFrom','EffectiveTo',
            'SupersedesId','EvidenceRef','ApplicabilityStatement','ReviewedBy','ReviewAuthority','ReviewedAt',
            'PublishedBy','PublishedAt','DecimalPlaces','SchemaVersion','AsReceivedConfirmed','ProfileCode','ProfileBindings']))
        OR EXISTS(SELECT 1 FROM unnest(ARRAY['Id','CompanyId','Code','SubstanceId','ProductStage','ProductIdentity',
            'ResidueDefinition','Lower','Upper','Unit','Precedence','EffectiveFrom','EvidenceRef','ApplicabilityStatement',
            'ReviewedBy','ReviewAuthority','ReviewedAt','PublishedBy','PublishedAt','ProfileCode']) k
            WHERE jsonb_typeof(p->k) IS DISTINCT FROM 'string')
        OR EXISTS(SELECT 1 FROM unnest(ARRAY['EffectiveTo','SupersedesId']) k
            WHERE p ? k AND jsonb_typeof(p->k) NOT IN ('string','null'))
        OR EXISTS(SELECT 1 FROM unnest(ARRAY['VersionNo','DecimalPlaces']) k
            WHERE jsonb_typeof(p->k) IS DISTINCT FROM 'number' OR p->>k !~ '^(0|[1-9][0-9]*)$')
        OR EXISTS(SELECT 1 FROM unnest(ARRAY['SourceIds','SupportingResultIds','ProfileBindings']) k
            WHERE jsonb_typeof(p->k) IS DISTINCT FROM 'array')
    THEN RAISE EXCEPTION 'Invalid group evidence payload' USING ERRCODE='22023'; END IF;
    IF EXISTS(SELECT 1 FROM jsonb_array_elements(p->'SourceIds') item WHERE jsonb_typeof(item) IS DISTINCT FROM 'string')
        OR EXISTS(SELECT 1 FROM jsonb_array_elements(p->'SupportingResultIds') item WHERE jsonb_typeof(item) IS DISTINCT FROM 'string')
        OR EXISTS(SELECT 1 FROM jsonb_array_elements(p->'ProfileBindings') item WHERE jsonb_typeof(item) IS DISTINCT FROM 'object')
        OR EXISTS(SELECT 1 FROM unnest(ARRAY['EffectiveFrom','EffectiveTo','ReviewedAt','PublishedAt']) k
            WHERE p->>k IS NOT NULL AND p->>k !~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-](0[0-9]|1[0-3]):[0-5][0-9]|[+-]14:00)$')
    THEN RAISE EXCEPTION 'Invalid group evidence payload' USING ERRCODE='22023'; END IF;
    IF EXISTS(SELECT 1 FROM jsonb_array_elements(p->'ProfileBindings') item
        WHERE EXISTS(SELECT 1 FROM jsonb_object_keys(item) k WHERE k <> ALL(ARRAY['SourceId','ProfileRuleId','ProfileSettingId']))
            OR EXISTS(SELECT 1 FROM unnest(ARRAY['SourceId','ProfileRuleId','ProfileSettingId']) k
                WHERE jsonb_typeof(item->k) IS DISTINCT FROM 'string'))
    THEN RAISE EXCEPTION 'Invalid group profile binding payload' USING ERRCODE='22023'; END IF;
    IF EXISTS(SELECT 1 FROM unnest(ARRAY['Id','CompanyId','SubstanceId','SupersedesId','PublishedBy']) k
        WHERE p->>k IS NOT NULL AND p->>k !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')
        OR EXISTS(SELECT 1 FROM jsonb_array_elements_text(p->'SourceIds') item
            WHERE item !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')
        OR EXISTS(SELECT 1 FROM jsonb_array_elements_text(p->'SupportingResultIds') item
            WHERE item !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')
        OR EXISTS(SELECT 1 FROM jsonb_array_elements(p->'ProfileBindings') item,
            unnest(ARRAY['SourceId','ProfileRuleId','ProfileSettingId']) k
            WHERE item->>k !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')
    THEN RAISE EXCEPTION 'Invalid group evidence identifier' USING ERRCODE='22023'; END IF;
    p := jsonb_set(p,'{PublishedAt}',to_jsonb(clock_timestamp()));
    IF jsonb_typeof(p->'SourceIds') IS DISTINCT FROM 'array' OR jsonb_array_length(p->'SourceIds') NOT BETWEEN 1 AND 1000
        OR jsonb_typeof(p->'SupportingResultIds') IS DISTINCT FROM 'array' OR jsonb_array_length(p->'SupportingResultIds')>1000
        OR jsonb_typeof(p->'ProfileBindings') IS DISTINCT FROM 'array'
        OR COALESCE((p->>'VersionNo')::int,0)<1 OR length(btrim(COALESCE(p->>'Code','')))=0
        OR p->>'ProductStage' IS DISTINCT FROM 'RAW_PADDY' OR p->>'ProductIdentity' IS DISTINCT FROM 'RAW_PADDY'
        OR COALESCE((p->>'DecimalPlaces')::int,-1) NOT BETWEEN 0 AND 18
        OR p->>'Unit' IS DISTINCT FROM 'mg/kg' OR COALESCE(p->>'Precedence','') NOT IN ('DIRECT_FIRST','GROUP_FIRST','UNTESTED_ONLY')
        OR p->>'Lower' IS NULL OR p->>'Upper' IS NULL OR p->>'Lower' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$'
        OR p->>'Upper' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$' OR (p->>'Lower')::numeric>(p->>'Upper')::numeric
    THEN RAISE EXCEPTION 'Invalid reviewed residue group' USING ERRCODE='22023'; END IF;
    SELECT array_agg(value::uuid) INTO members FROM jsonb_array_elements_text(p->'SourceIds');
    IF cardinality(members)<>(SELECT count(DISTINCT x) FROM unnest(members) x)
        OR jsonb_array_length(p->'ProfileBindings')<>cardinality(members)
        OR EXISTS(SELECT 1 FROM unnest(members) member WHERE
            (SELECT count(*) FROM jsonb_array_elements(p->'ProfileBindings') b WHERE (b->>'SourceId')::uuid=member)<>1
            OR NOT EXISTS(SELECT 1 FROM grainledger.capture_source_lots sl
                JOIN grainledger.capture_procurement_items i ON i.id=sl.procurement_item_id AND i.company_id=sl.company_id
                JOIN grainledger.capture_procurements procurement ON procurement.id=i.procurement_id AND procurement.company_id=i.company_id
                WHERE sl.id=member AND sl.company_id=(p->>'CompanyId')::uuid
                    AND procurement.received_at >= (p->>'EffectiveFrom')::timestamptz
                    AND (p->>'EffectiveTo' IS NULL OR procurement.received_at<(p->>'EffectiveTo')::timestamptz)))
        OR EXISTS(SELECT 1 FROM jsonb_array_elements(p->'ProfileBindings') b
            WHERE jsonb_typeof(b) IS DISTINCT FROM 'object'
                OR EXISTS(SELECT 1 FROM jsonb_object_keys(b) k WHERE k <> ALL(ARRAY['SourceId','ProfileRuleId','ProfileSettingId']))
                OR NOT grainledger.capture_residue_profile_binding_valid(p,(b->>'SourceId')::uuid,(b->>'ProfileRuleId')::uuid,(b->>'ProfileSettingId')::uuid))
        OR EXISTS(SELECT 1 FROM jsonb_array_elements_text(p->'SupportingResultIds') result WHERE NOT EXISTS(
            SELECT 1 FROM unnest(members) member WHERE grainledger.capture_residue_confirmed_result((p->>'CompanyId')::uuid,member,result::uuid,(p->>'SubstanceId')::uuid)))
    THEN RAISE EXCEPTION 'Group membership, profile binding or supporting evidence not applicable' USING ERRCODE='22023'; END IF;
    IF EXISTS(SELECT 1 FROM grainledger.capture_residue_source_group_versions WHERE company_id=(p->>'CompanyId')::uuid
        AND code=p->>'Code' AND (substance_id<>(p->>'SubstanceId')::uuid OR payload->>'SchemaVersion' IS DISTINCT FROM '2')) THEN
        RAISE EXCEPTION 'Group code must retain schema and substance scope' USING ERRCODE='23505';
    END IF;
    IF EXISTS(SELECT 1 FROM grainledger.capture_residue_source_group_versions WHERE company_id=(p->>'CompanyId')::uuid
        AND code=p->>'Code' AND version_no >= (p->>'VersionNo')::int) THEN
        RAISE EXCEPTION 'Group version must advance' USING ERRCODE='23505';
    END IF;
    IF p->>'SupersedesId' IS NOT NULL THEN
        SELECT payload INTO previous FROM grainledger.capture_residue_source_group_versions WHERE id=(p->>'SupersedesId')::uuid;
        IF previous IS NULL OR previous->>'SchemaVersion' IS DISTINCT FROM '2'
            OR (previous->>'CompanyId')::uuid IS DISTINCT FROM (p->>'CompanyId')::uuid OR previous->>'Code' IS DISTINCT FROM p->>'Code'
            OR (previous->>'SubstanceId')::uuid IS DISTINCT FROM (p->>'SubstanceId')::uuid
            OR EXISTS(SELECT 1 FROM grainledger.capture_residue_source_group_versions WHERE (payload->>'SupersedesId')::uuid=(p->>'SupersedesId')::uuid)
        THEN RAISE EXCEPTION 'Invalid reviewed group supersession' USING ERRCODE='23505'; END IF;
    END IF;
    INSERT INTO grainledger.capture_residue_source_group_versions VALUES((p->>'Id')::uuid,(p->>'CompanyId')::uuid,p->>'Code',
        (p->>'VersionNo')::int,(p->>'SubstanceId')::uuid,members,(p->>'EffectiveFrom')::timestamptz,(p->>'EffectiveTo')::timestamptz,
        a,(p->>'PublishedAt')::timestamptz,p);
END $$;
