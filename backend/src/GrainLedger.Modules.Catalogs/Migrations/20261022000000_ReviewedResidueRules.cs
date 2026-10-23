using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Catalogs.Migrations;

[DbContext(typeof(CatalogDbContext))]
[Migration("20261022000000_ReviewedResidueRules")]
public sealed class ReviewedResidueRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE catalog.residue_rule_versions (
            id uuid PRIMARY KEY, code text NOT NULL, version_no integer NOT NULL CHECK(version_no>0),
            substance_id uuid NOT NULL REFERENCES catalog.active_substances(id),
            commodity_id uuid NOT NULL REFERENCES catalog.commodities(id),
            effective_from timestamptz NOT NULL, effective_to timestamptz,
            published_by uuid NOT NULL REFERENCES identity.users("Id"), published_at timestamptz NOT NULL,
            payload jsonb NOT NULL CHECK(jsonb_typeof(payload)='object'),
            CHECK(effective_to IS NULL OR effective_to>effective_from), UNIQUE(code,version_no)
        );
        CREATE INDEX residue_rule_substance ON catalog.residue_rule_versions(substance_id,published_at);
        CREATE FUNCTION catalog.residue_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Reviewed residue records are immutable' USING ERRCODE='55000'; END $$;
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON catalog.residue_rule_versions
            FOR EACH ROW EXECUTE FUNCTION catalog.residue_immutable();
        CREATE TRIGGER immutable_truncate BEFORE TRUNCATE ON catalog.residue_rule_versions
            FOR EACH STATEMENT EXECUTE FUNCTION catalog.residue_immutable();
        REVOKE ALL ON catalog.residue_rule_versions FROM PUBLIC;
        REVOKE ALL ON FUNCTION catalog.residue_immutable() FROM PUBLIC;

        CREATE FUNCTION catalog.publish_residue_rule(a uuid,p jsonb) RETURNS void
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,catalog,identity AS $$
        DECLARE previous jsonb; now_at timestamptz := clock_timestamp();
        BEGIN
            PERFORM pg_advisory_xact_lock(714928352);
            IF NOT EXISTS(SELECT 1 FROM identity.users u JOIN identity.organizations o ON o."Id"=u."OrganizationId"
                WHERE u."Id"=a AND u."IsActive" AND u."Role"='Administrator' AND o."Kind"='Mill') THEN
                RAISE EXCEPTION 'Residue policy publication denied' USING ERRCODE='42501';
            END IF;
            IF p->>'Id' IS NULL OR (p->>'Id')::uuid='00000000-0000-0000-0000-000000000000'
                OR (p->>'PublishedBy')::uuid IS DISTINCT FROM a
                OR (p->>'PublishedAt')::timestamptz > now_at
                OR (p->>'ReviewedAt')::timestamptz > now_at OR (p->>'ReviewedAt')::timestamptz IS NULL
                OR (p->>'EffectiveFrom')::timestamptz IS NULL
                OR (p->>'EffectiveTo')::timestamptz <= (p->>'EffectiveFrom')::timestamptz
                OR COALESCE((p->>'VersionNo')::int,0)<1
                OR COALESCE((p->>'DecimalPlaces')::int,-1) NOT BETWEEN 0 AND 18
                OR jsonb_typeof(p->'HomogeneousMixing') IS DISTINCT FROM 'boolean'
                OR p->>'Unit' IS DISTINCT FROM 'mg/kg'
                OR EXISTS(SELECT 1 FROM unnest(ARRAY['Code','ResidueDefinition','InputStage','OutputStage','InputBasis','OutputBasis',
                    'EvidenceRef','ApplicabilityStatement','ReviewedBy','ReviewAuthority']) k WHERE length(btrim(COALESCE(p->>k,'')))=0)
                OR NOT EXISTS(SELECT 1 FROM catalog.active_substances WHERE id=(p->>'SubstanceId')::uuid)
                OR NOT EXISTS(SELECT 1 FROM catalog.commodities WHERE id=(p->>'CommodityId')::uuid)
            THEN RAISE EXCEPTION 'Incomplete reviewed residue policy' USING ERRCODE='22023'; END IF;
            IF p->>'Kind'='INTERPRETATION' THEN
                IF p->>'InterpretationKind' IS NULL OR p->>'InterpretationKind' NOT IN ('NUMERIC_ABSOLUTE_UNCERTAINTY','BELOW_LIMIT','ND')
                    OR length(btrim(COALESCE(p->>'AnalyticalMethod','')))=0
                    OR p->>'InputBasis' IS DISTINCT FROM p->>'OutputBasis' OR p->>'InputStage' IS DISTINCT FROM p->>'OutputStage'
                    OR p->>'FactorLower' IS NOT NULL OR p->>'FactorUpper' IS NOT NULL OR p->>'FactorSemantics' IS NOT NULL
                    OR COALESCE((p->>'HomogeneousMixing')::boolean,false)
                    OR p->>'InterpretationKind'='ND' AND (p->>'NdThresholdKind' IS NULL OR p->>'NdThresholdKind' NOT IN ('REPORTING_THRESHOLD','LOD','LOQ'))
                    OR p->>'InterpretationKind'<>'ND' AND p->>'NdThresholdKind' IS NOT NULL
                THEN RAISE EXCEPTION 'Invalid residue interpretation' USING ERRCODE='22023'; END IF;
            ELSIF p->>'Kind'='PROCESS' THEN
                IF p->>'FactorLower' IS NULL OR p->>'FactorUpper' IS NULL
                    OR p->>'FactorLower' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$' OR p->>'FactorUpper' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$'
                    OR (p->>'FactorLower')::numeric > (p->>'FactorUpper')::numeric
                    OR p->>'FactorSemantics' IS NULL OR p->>'FactorSemantics' NOT IN ('STEP_CONCENTRATION','CUMULATIVE_CONCENTRATION')
                    OR EXISTS(SELECT 1 FROM unnest(ARRAY['Process','OutputFraction','Conditions']) k WHERE length(btrim(COALESCE(p->>k,'')))=0)
                    OR p->>'InterpretationKind' IS NOT NULL OR p->>'NdThresholdKind' IS NOT NULL
                THEN RAISE EXCEPTION 'Invalid residue process factor' USING ERRCODE='22023'; END IF;
            ELSE RAISE EXCEPTION 'Unknown residue policy kind' USING ERRCODE='22023'; END IF;
            IF EXISTS(SELECT 1 FROM catalog.residue_rule_versions WHERE code=p->>'Code' AND
                (payload->>'SubstanceId' IS DISTINCT FROM p->>'SubstanceId' OR payload->>'Kind' IS DISTINCT FROM p->>'Kind'
                    OR payload->>'CommodityId' IS DISTINCT FROM p->>'CommodityId'))
            THEN RAISE EXCEPTION 'Rule code must retain substance, kind and commodity scope' USING ERRCODE='23505'; END IF;
            IF EXISTS(SELECT 1 FROM catalog.residue_rule_versions WHERE code=p->>'Code' AND version_no >= (p->>'VersionNo')::int)
                THEN RAISE EXCEPTION 'Residue version must advance' USING ERRCODE='23505'; END IF;
            IF p->>'SupersedesId' IS NOT NULL THEN
                SELECT payload INTO previous FROM catalog.residue_rule_versions WHERE id=(p->>'SupersedesId')::uuid;
                IF previous IS NULL OR previous->>'Code' IS DISTINCT FROM p->>'Code'
                    OR previous->>'SubstanceId' IS DISTINCT FROM p->>'SubstanceId' OR previous->>'Kind' IS DISTINCT FROM p->>'Kind'
                    OR EXISTS(SELECT 1 FROM catalog.residue_rule_versions WHERE payload->>'SupersedesId'=p->>'SupersedesId')
                THEN RAISE EXCEPTION 'Invalid reviewed rule supersession' USING ERRCODE='23505'; END IF;
            END IF;
            INSERT INTO catalog.residue_rule_versions VALUES((p->>'Id')::uuid,p->>'Code',(p->>'VersionNo')::int,
                (p->>'SubstanceId')::uuid,(p->>'CommodityId')::uuid,(p->>'EffectiveFrom')::timestamptz,(p->>'EffectiveTo')::timestamptz,
                a,(p->>'PublishedAt')::timestamptz,p);
        END $$;
        REVOKE ALL ON FUNCTION catalog.publish_residue_rule(uuid,jsonb) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION catalog.publish_residue_rule(uuid,jsonb);
        DROP TABLE catalog.residue_rule_versions;
        DROP FUNCTION catalog.residue_immutable();
        """);
}
