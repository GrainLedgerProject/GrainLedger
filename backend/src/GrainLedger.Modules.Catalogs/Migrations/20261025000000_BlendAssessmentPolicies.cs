using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Catalogs.Migrations;

[DbContext(typeof(CatalogDbContext))]
[Migration("20261025000000_BlendAssessmentPolicies")]
public sealed class BlendAssessmentPolicies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE catalog.blend_assessment_policies (
            id uuid PRIMARY KEY,
            company_id uuid NOT NULL REFERENCES identity.organizations("Id"),
            market_id uuid NOT NULL REFERENCES catalog.markets(id),
            commodity_id uuid NOT NULL REFERENCES catalog.commodities(id),
            version_no integer NOT NULL CHECK (version_no > 0),
            supersedes_id uuid REFERENCES catalog.blend_assessment_policies(id),
            effective_from timestamptz NOT NULL,
            effective_to timestamptz,
            required_substance_ids uuid[] NOT NULL,
            margin_fraction numeric(18,12) NOT NULL,
            residue_basis text NOT NULL,
            quality_rules jsonb NOT NULL,
            evidence_reference text NOT NULL,
            reviewed_by text NOT NULL,
            review_authority text NOT NULL,
            reviewed_at timestamptz NOT NULL,
            published_by uuid NOT NULL REFERENCES identity.users("Id"),
            published_at timestamptz NOT NULL,
            CONSTRAINT uq_blend_assessment_version UNIQUE(company_id,market_id,commodity_id,version_no),
            CONSTRAINT ck_blend_assessment_window CHECK (effective_to IS NULL OR effective_to > effective_from),
            CONSTRAINT ck_blend_assessment_margin CHECK (margin_fraction >= 0 AND margin_fraction < 1),
            CONSTRAINT ck_blend_assessment_coverage CHECK (cardinality(required_substance_ids) BETWEEN 1 AND 200),
            CONSTRAINT ck_blend_assessment_basis CHECK (length(btrim(residue_basis)) BETWEEN 1 AND 100),
            CONSTRAINT ck_blend_assessment_evidence CHECK (length(btrim(evidence_reference)) BETWEEN 1 AND 1000),
            CONSTRAINT ck_blend_assessment_quality CHECK (jsonb_typeof(quality_rules) = 'array')
        );
        CREATE INDEX ix_blend_assessment_current ON catalog.blend_assessment_policies
            (company_id,market_id,commodity_id,effective_from DESC,version_no DESC);
        CREATE FUNCTION catalog.reject_blend_assessment_change() RETURNS trigger
            LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            RAISE EXCEPTION 'Published blend assessment policies are immutable' USING ERRCODE='23514';
        END $$;
        CREATE TRIGGER immutable_blend_assessment_policy BEFORE UPDATE OR DELETE
            ON catalog.blend_assessment_policies FOR EACH ROW
            EXECUTE FUNCTION catalog.reject_blend_assessment_change();

        CREATE FUNCTION catalog.publish_blend_assessment_policy(a uuid, p jsonb) RETURNS jsonb
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,catalog,identity AS $$
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
            IF NOT EXISTS(SELECT 1 FROM identity.users u JOIN identity.organizations o
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
                (SELECT 1 FROM catalog.active_substances WHERE id=x)) OR
               EXISTS(SELECT 1 FROM jsonb_array_elements(p->'qualityRules') AS x
                 WHERE x->>'calculation'<>'MASS_WEIGHTED_MEAN' OR
                   NOT EXISTS(SELECT 1 FROM catalog.quality_attributes
                     WHERE id=(x->>'attributeId')::uuid AND unit=x->>'unit')) THEN
                RAISE EXCEPTION 'Unknown policy catalog reference' USING ERRCODE='22023';
            END IF;
            SELECT COALESCE(max(b.version_no),0) INTO current_version
              FROM catalog.blend_assessment_policies b
             WHERE b.company_id=company AND b.market_id=market AND b.commodity_id=commodity;
            IF version_no<>current_version+1 THEN
                RAISE EXCEPTION 'Assessment policy version must follow current version' USING ERRCODE='23514';
            END IF;
            INSERT INTO catalog.blend_assessment_policies
                (id,company_id,market_id,commodity_id,version_no,effective_from,effective_to,
                 required_substance_ids,margin_fraction,residue_basis,quality_rules,evidence_reference,
                 reviewed_by,review_authority,reviewed_at,published_by,published_at)
            VALUES(policy_id,company,market,commodity,version_no,from_at,to_at,
                required,margin,p->>'residueBasis',p->'qualityRules',p->>'evidenceReference',
                p->>'reviewedBy',p->>'reviewAuthority',review_at,a,statement_timestamp());
            RETURN jsonb_build_object('id',policy_id,'versionNo',version_no,
                'companyId',company,'publishedBy',a);
        END $$;
        REVOKE ALL ON FUNCTION catalog.publish_blend_assessment_policy(uuid,jsonb) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Published blend assessment policy history cannot be rolled back safely.");
}
