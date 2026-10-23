using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Catalogs.Migrations;

[DbContext(typeof(CatalogDbContext))]
[Migration("20261023000000_CompanyResiduePolicies")]
public sealed class CompanyResiduePolicies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- Existing payloads remain byte-for-byte unchanged; nullable scope means system history.
        ALTER TABLE catalog.residue_rule_versions ADD COLUMN company_id uuid REFERENCES identity.organizations("Id");
        ALTER TABLE catalog.residue_rule_versions DROP CONSTRAINT residue_rule_versions_code_version_no_key;
        CREATE UNIQUE INDEX residue_rule_scope_version ON catalog.residue_rule_versions(company_id,code,version_no) NULLS NOT DISTINCT;

        CREATE TABLE catalog.residue_policy_setting_versions (
            id uuid PRIMARY KEY,
            company_id uuid NOT NULL REFERENCES identity.organizations("Id"),
            rule_code text NOT NULL, version_no integer NOT NULL CHECK(version_no>0),
            effective_from timestamptz NOT NULL, effective_to timestamptz,
            published_by uuid NOT NULL REFERENCES identity.users("Id"), published_at timestamptz NOT NULL,
            payload jsonb NOT NULL CHECK(jsonb_typeof(payload)='object'),
            CHECK(effective_to IS NULL OR effective_to>effective_from), UNIQUE(company_id,rule_code,version_no)
        );
        CREATE INDEX residue_setting_company_snapshot ON catalog.residue_policy_setting_versions(company_id,published_at);
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON catalog.residue_policy_setting_versions
            FOR EACH ROW EXECUTE FUNCTION catalog.residue_immutable();
        CREATE TRIGGER immutable_truncate BEFORE TRUNCATE ON catalog.residue_policy_setting_versions
            FOR EACH STATEMENT EXECUTE FUNCTION catalog.residue_immutable();
        REVOKE ALL ON catalog.residue_policy_setting_versions FROM PUBLIC;

        CREATE FUNCTION catalog.validate_residue_policy_review(p jsonb) RETURNS void
        LANGUAGE plpgsql SET search_path=pg_catalog,catalog AS $$
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
        END $$;
        REVOKE ALL ON FUNCTION catalog.validate_residue_policy_review(jsonb) FROM PUBLIC;

        CREATE OR REPLACE FUNCTION catalog.publish_residue_rule(a uuid,p jsonb) RETURNS void
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,catalog,identity AS $$
        DECLARE previous catalog.residue_rule_versions%ROWTYPE; company uuid;
        BEGIN
            PERFORM pg_advisory_xact_lock(714928352);
            IF jsonb_typeof(p) IS DISTINCT FROM 'object'
                OR COALESCE(jsonb_typeof(p->'CompanyId'),'null') NOT IN ('null','string')
            THEN RAISE EXCEPTION 'Invalid residue policy scope' USING ERRCODE='22023'; END IF;
            company := (p->>'CompanyId')::uuid;
            IF NOT EXISTS(SELECT 1 FROM identity.users u JOIN identity.organizations o ON o."Id"=u."OrganizationId"
                WHERE u."Id"=a AND u."IsActive" AND
                    (company IS NULL AND u."Role"='Administrator' AND o."Kind"='Mill'
                    OR company IS NOT NULL AND u."Role"='CustomerManager' AND o."Kind"='Customer' AND o."Id"=company))
            THEN RAISE EXCEPTION 'Residue policy publication denied' USING ERRCODE='42501'; END IF;
            PERFORM catalog.validate_residue_policy_review(p);
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
                OR NOT EXISTS(SELECT 1 FROM catalog.active_substances WHERE id=(p->>'SubstanceId')::uuid)
                OR NOT EXISTS(SELECT 1 FROM catalog.commodities WHERE id=(p->>'CommodityId')::uuid)
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
            IF EXISTS(SELECT 1 FROM catalog.residue_rule_versions r WHERE r.code=p->>'Code'
                AND (r.company_id IS NOT DISTINCT FROM company OR r.company_id IS NULL OR company IS NULL)
                AND (r.substance_id IS DISTINCT FROM (p->>'SubstanceId')::uuid
                    OR r.payload->>'Kind' IS DISTINCT FROM p->>'Kind' OR r.commodity_id IS DISTINCT FROM (p->>'CommodityId')::uuid))
            THEN RAISE EXCEPTION 'Rule code must retain substance, kind and commodity scope' USING ERRCODE='23505'; END IF;
            IF EXISTS(SELECT 1 FROM catalog.residue_rule_versions WHERE company_id IS NOT DISTINCT FROM company
                AND code=p->>'Code' AND version_no >= (p->>'VersionNo')::int)
            THEN RAISE EXCEPTION 'Residue version must advance in its scope' USING ERRCODE='23505'; END IF;
            IF p->>'SupersedesId' IS NOT NULL THEN
                SELECT * INTO previous FROM catalog.residue_rule_versions WHERE id=(p->>'SupersedesId')::uuid;
                IF previous.id IS NULL OR previous.code IS DISTINCT FROM p->>'Code' OR previous.company_id IS DISTINCT FROM company
                    OR previous.substance_id IS DISTINCT FROM (p->>'SubstanceId')::uuid OR previous.payload->>'Kind' IS DISTINCT FROM p->>'Kind'
                    OR EXISTS(SELECT 1 FROM catalog.residue_rule_versions WHERE (payload->>'SupersedesId')::uuid=(p->>'SupersedesId')::uuid)
                THEN RAISE EXCEPTION 'Invalid reviewed rule supersession' USING ERRCODE='23505'; END IF;
            END IF;
            INSERT INTO catalog.residue_rule_versions(id,code,version_no,substance_id,commodity_id,effective_from,effective_to,published_by,published_at,payload,company_id)
                VALUES((p->>'Id')::uuid,p->>'Code',(p->>'VersionNo')::int,(p->>'SubstanceId')::uuid,(p->>'CommodityId')::uuid,
                    (p->>'EffectiveFrom')::timestamptz,(p->>'EffectiveTo')::timestamptz,a,(p->>'PublishedAt')::timestamptz,p,company);
        END $$;
        REVOKE ALL ON FUNCTION catalog.publish_residue_rule(uuid,jsonb) FROM PUBLIC;

        CREATE FUNCTION catalog.publish_residue_policy_setting(a uuid,p jsonb) RETURNS void
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,catalog,identity AS $$
        DECLARE previous catalog.residue_policy_setting_versions%ROWTYPE; company uuid;
        BEGIN
            PERFORM pg_advisory_xact_lock(714928352);
            IF jsonb_typeof(p) IS DISTINCT FROM 'object' OR jsonb_typeof(p->'CompanyId') IS DISTINCT FROM 'string'
            THEN RAISE EXCEPTION 'Explicit company setting scope required' USING ERRCODE='22023'; END IF;
            company := (p->>'CompanyId')::uuid;
            IF NOT EXISTS(SELECT 1 FROM identity.users u JOIN identity.organizations o ON o."Id"=u."OrganizationId"
                WHERE u."Id"=a AND u."IsActive" AND u."Role"='CustomerManager' AND o."Kind"='Customer' AND o."Id"=company)
            THEN RAISE EXCEPTION 'Company residue setting publication denied' USING ERRCODE='42501'; END IF;
            PERFORM catalog.validate_residue_policy_review(p);
            IF (p->>'PublishedBy')::uuid IS DISTINCT FROM a
                OR jsonb_typeof(p->'RuleCode') IS DISTINCT FROM 'string' OR length(btrim(p->>'RuleCode'))=0 OR length(p->>'RuleCode')>100
                OR jsonb_typeof(p->'VersionNo') IS DISTINCT FROM 'number' OR p->>'VersionNo' !~ '^[0-9]+$'
                OR p->>'Mode' IS NULL OR p->>'Mode' NOT IN ('SYSTEM_DEFAULT','COMPANY_OVERRIDE')
            THEN RAISE EXCEPTION 'Incomplete reviewed company residue setting' USING ERRCODE='22023'; END IF;
            IF (p->>'VersionNo')::int<1 OR NOT EXISTS(SELECT 1 FROM catalog.residue_rule_versions
                WHERE code=p->>'RuleCode' AND (company_id IS NULL OR company_id=company))
            THEN RAISE EXCEPTION 'Reviewed setting requires existing visible profile series' USING ERRCODE='22023'; END IF;
            IF EXISTS(SELECT 1 FROM catalog.residue_policy_setting_versions
                WHERE company_id=company AND rule_code=p->>'RuleCode' AND version_no >= (p->>'VersionNo')::int)
            THEN RAISE EXCEPTION 'Company setting version must advance' USING ERRCODE='23505'; END IF;
            IF p->>'SupersedesId' IS NOT NULL THEN
                SELECT * INTO previous FROM catalog.residue_policy_setting_versions WHERE id=(p->>'SupersedesId')::uuid;
                IF previous.id IS NULL OR previous.company_id IS DISTINCT FROM company OR previous.rule_code IS DISTINCT FROM p->>'RuleCode'
                    OR EXISTS(SELECT 1 FROM catalog.residue_policy_setting_versions WHERE (payload->>'SupersedesId')::uuid=(p->>'SupersedesId')::uuid)
                THEN RAISE EXCEPTION 'Invalid reviewed setting supersession' USING ERRCODE='23505'; END IF;
            END IF;
            INSERT INTO catalog.residue_policy_setting_versions(id,company_id,rule_code,version_no,effective_from,effective_to,published_by,published_at,payload)
                VALUES((p->>'Id')::uuid,company,p->>'RuleCode',(p->>'VersionNo')::int,(p->>'EffectiveFrom')::timestamptz,
                    (p->>'EffectiveTo')::timestamptz,a,(p->>'PublishedAt')::timestamptz,p);
        END $$;
        REVOKE ALL ON FUNCTION catalog.publish_residue_policy_setting(uuid,jsonb) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException(
        "Reviewed company policy history cannot be downgraded into global rule scope. Restore an approved database backup instead.");
}
