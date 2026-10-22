using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Capture.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("20261022000000_ReviewedResidueEvidence")]
public sealed class ReviewedResidueEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE capture.residue_source_group_versions (
            id uuid PRIMARY KEY, company_id uuid NOT NULL REFERENCES identity.organizations("Id"),
            code text NOT NULL, version_no integer NOT NULL CHECK(version_no>0),
            substance_id uuid NOT NULL REFERENCES catalog.active_substances(id), source_ids uuid[] NOT NULL,
            effective_from timestamptz NOT NULL, effective_to timestamptz,
            published_by uuid NOT NULL REFERENCES identity.users("Id"), published_at timestamptz NOT NULL,
            payload jsonb NOT NULL CHECK(jsonb_typeof(payload)='object'),
            CHECK(effective_to IS NULL OR effective_to>effective_from), UNIQUE(company_id,code,version_no)
        );
        CREATE INDEX residue_groups_company ON capture.residue_source_group_versions(company_id,substance_id,published_at);
        CREATE INDEX residue_groups_members ON capture.residue_source_group_versions USING gin(source_ids);
        CREATE TABLE capture.residue_report_selections (
            id uuid PRIMARY KEY, company_id uuid NOT NULL REFERENCES identity.organizations("Id"),
            source_id uuid NOT NULL REFERENCES capture.source_lots(id),
            substance_id uuid NOT NULL REFERENCES catalog.active_substances(id),
            effective_from timestamptz NOT NULL, effective_to timestamptz,
            published_by uuid NOT NULL REFERENCES identity.users("Id"), published_at timestamptz NOT NULL,
            payload jsonb NOT NULL CHECK(jsonb_typeof(payload)='object'),
            CHECK(effective_to IS NULL OR effective_to>effective_from)
        );
        CREATE INDEX residue_selection_source ON capture.residue_report_selections(company_id,source_id,substance_id,published_at);
        CREATE FUNCTION capture.residue_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Reviewed residue records are immutable' USING ERRCODE='55000'; END $$;
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON capture.residue_source_group_versions
            FOR EACH ROW EXECUTE FUNCTION capture.residue_immutable();
        CREATE TRIGGER immutable_truncate BEFORE TRUNCATE ON capture.residue_source_group_versions
            FOR EACH STATEMENT EXECUTE FUNCTION capture.residue_immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON capture.residue_report_selections
            FOR EACH ROW EXECUTE FUNCTION capture.residue_immutable();
        CREATE TRIGGER immutable_truncate BEFORE TRUNCATE ON capture.residue_report_selections
            FOR EACH STATEMENT EXECUTE FUNCTION capture.residue_immutable();
        REVOKE ALL ON capture.residue_source_group_versions,capture.residue_report_selections FROM PUBLIC;
        REVOKE ALL ON FUNCTION capture.residue_immutable() FROM PUBLIC;

        CREATE FUNCTION capture.residue_review_gate(a uuid,p jsonb) RETURNS void
        LANGUAGE plpgsql SET search_path=pg_catalog,capture,identity AS $$
        BEGIN
            IF NOT EXISTS(SELECT 1 FROM identity.users u JOIN identity.organizations o ON o."Id"=u."OrganizationId"
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
                OR NOT EXISTS(SELECT 1 FROM catalog.active_substances WHERE id=(p->>'SubstanceId')::uuid)
            THEN RAISE EXCEPTION 'Incomplete reviewed residue evidence' USING ERRCODE='22023'; END IF;
        END $$;
        REVOKE ALL ON FUNCTION capture.residue_review_gate(uuid,jsonb) FROM PUBLIC;

        CREATE FUNCTION capture.residue_confirmed_result(c uuid,s uuid,r uuid,substance uuid) RETURNS boolean
        LANGUAGE sql STABLE SET search_path=pg_catalog,capture AS $$
            SELECT EXISTS(SELECT 1 FROM capture.lab_results result
                JOIN capture.lab_result_sets rs ON rs.id=result.result_set_id AND rs.company_id=result.company_id
                JOIN capture.lab_confirmed_associations association ON association.result_set_id=rs.id AND association.company_id=rs.company_id
                JOIN capture.lab_report_confirmations confirmation ON confirmation.id=association.confirmation_id AND confirmation.company_id=association.company_id
                    AND confirmation.report_id=rs.report_id AND confirmation.identity_checked AND confirmation.transcription_checked
                JOIN capture.samples sample ON sample.id=association.sample_id AND sample.company_id=association.company_id
                WHERE result.id=r AND result.company_id=c AND result.substance_id=substance AND sample.source_lot_id=s)
        $$;
        REVOKE ALL ON FUNCTION capture.residue_confirmed_result(uuid,uuid,uuid,uuid) FROM PUBLIC;

        CREATE FUNCTION capture.publish_residue_group(a uuid,p jsonb) RETURNS void
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,capture,catalog,identity AS $$
        DECLARE members uuid[]; previous jsonb;
        BEGIN
            PERFORM pg_advisory_xact_lock(714928352);
            PERFORM capture.residue_review_gate(a,p);
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
                    SELECT 1 FROM capture.source_lots sl JOIN capture.procurement_items i ON i.id=sl.procurement_item_id AND i.company_id=sl.company_id
                    JOIN capture.procurements procurement ON procurement.id=i.procurement_id AND procurement.company_id=i.company_id
                    WHERE sl.id=member AND sl.company_id=(p->>'CompanyId')::uuid
                        AND procurement.received_at >= (p->>'EffectiveFrom')::timestamptz
                        AND (p->>'EffectiveTo' IS NULL OR procurement.received_at<(p->>'EffectiveTo')::timestamptz)))
                OR EXISTS(SELECT 1 FROM jsonb_array_elements_text(p->'SupportingResultIds') result WHERE NOT EXISTS(
                    SELECT 1 FROM unnest(members) member WHERE capture.residue_confirmed_result((p->>'CompanyId')::uuid,member,result::uuid,(p->>'SubstanceId')::uuid)))
            THEN RAISE EXCEPTION 'Group membership or supporting evidence not applicable' USING ERRCODE='22023'; END IF;
            IF EXISTS(SELECT 1 FROM capture.residue_source_group_versions WHERE company_id=(p->>'CompanyId')::uuid
                AND code=p->>'Code' AND substance_id<>(p->>'SubstanceId')::uuid)
            THEN RAISE EXCEPTION 'Group code must retain substance scope' USING ERRCODE='23505'; END IF;
            IF EXISTS(SELECT 1 FROM capture.residue_source_group_versions WHERE company_id=(p->>'CompanyId')::uuid
                AND code=p->>'Code' AND version_no >= (p->>'VersionNo')::int)
            THEN RAISE EXCEPTION 'Group version must advance' USING ERRCODE='23505'; END IF;
            IF p->>'SupersedesId' IS NOT NULL THEN
                SELECT payload INTO previous FROM capture.residue_source_group_versions WHERE id=(p->>'SupersedesId')::uuid;
                IF previous IS NULL OR previous->>'CompanyId' IS DISTINCT FROM p->>'CompanyId' OR previous->>'Code' IS DISTINCT FROM p->>'Code'
                    OR previous->>'SubstanceId' IS DISTINCT FROM p->>'SubstanceId'
                    OR EXISTS(SELECT 1 FROM capture.residue_source_group_versions WHERE payload->>'SupersedesId'=p->>'SupersedesId')
                THEN RAISE EXCEPTION 'Invalid reviewed group supersession' USING ERRCODE='23505'; END IF;
            END IF;
            INSERT INTO capture.residue_source_group_versions VALUES((p->>'Id')::uuid,(p->>'CompanyId')::uuid,p->>'Code',
                (p->>'VersionNo')::int,(p->>'SubstanceId')::uuid,members,(p->>'EffectiveFrom')::timestamptz,(p->>'EffectiveTo')::timestamptz,
                a,(p->>'PublishedAt')::timestamptz,p);
        END $$;
        REVOKE ALL ON FUNCTION capture.publish_residue_group(uuid,jsonb) FROM PUBLIC;

        CREATE FUNCTION capture.publish_residue_selection(a uuid,p jsonb) RETURNS void
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,capture,identity AS $$
        DECLARE previous jsonb; raw_result capture.lab_results;
        BEGIN
            PERFORM pg_advisory_xact_lock(714928352);
            PERFORM capture.residue_review_gate(a,p);
            IF NOT capture.residue_confirmed_result((p->>'CompanyId')::uuid,(p->>'SourceId')::uuid,
                (p->>'SelectedResultId')::uuid,(p->>'SubstanceId')::uuid)
                OR jsonb_typeof(p->'SupersededResultIds') IS DISTINCT FROM 'array' OR jsonb_array_length(p->'SupersededResultIds')>1000
                OR EXISTS(SELECT 1 FROM jsonb_array_elements_text(p->'SupersededResultIds') r WHERE r=p->>'SelectedResultId'
                    OR NOT capture.residue_confirmed_result((p->>'CompanyId')::uuid,(p->>'SourceId')::uuid,r::uuid,(p->>'SubstanceId')::uuid))
            THEN RAISE EXCEPTION 'Selection evidence not confirmed for same source/substance/company' USING ERRCODE='22023'; END IF;
            SELECT * INTO raw_result FROM capture.lab_results WHERE id=(p->>'SelectedResultId')::uuid;
            IF p->>'AbsoluteUncertainty' IS NOT NULL AND (p->>'AbsoluteUncertainty' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$'
                OR raw_result.result_kind<>'NUMERIC' OR length(btrim(COALESCE(raw_result.uncertainty,'')))=0)
                OR p->>'ReportingThreshold' IS NOT NULL AND (p->>'ReportingThreshold' !~ '^[0-9]{1,24}(\.[0-9]{1,18})?$'
                    OR (p->>'ReportingThreshold')::numeric<=0 OR raw_result.result_kind<>'NOT_DETECTED')
            THEN RAISE EXCEPTION 'Normalized uncertainty/threshold lacks applicable raw evidence' USING ERRCODE='22023'; END IF;
            IF p->>'SupersedesId' IS NOT NULL THEN
                SELECT payload INTO previous FROM capture.residue_report_selections WHERE id=(p->>'SupersedesId')::uuid;
                IF previous IS NULL OR previous->>'CompanyId' IS DISTINCT FROM p->>'CompanyId' OR previous->>'SourceId' IS DISTINCT FROM p->>'SourceId'
                    OR previous->>'SubstanceId' IS DISTINCT FROM p->>'SubstanceId'
                    OR EXISTS(SELECT 1 FROM capture.residue_report_selections WHERE payload->>'SupersedesId'=p->>'SupersedesId')
                THEN RAISE EXCEPTION 'Invalid reviewed selection supersession' USING ERRCODE='23505'; END IF;
            END IF;
            INSERT INTO capture.residue_report_selections VALUES((p->>'Id')::uuid,(p->>'CompanyId')::uuid,
                (p->>'SourceId')::uuid,(p->>'SubstanceId')::uuid,(p->>'EffectiveFrom')::timestamptz,(p->>'EffectiveTo')::timestamptz,
                a,(p->>'PublishedAt')::timestamptz,p);
        END $$;
        REVOKE ALL ON FUNCTION capture.publish_residue_selection(uuid,jsonb) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION capture.publish_residue_selection(uuid,jsonb);
        DROP FUNCTION capture.publish_residue_group(uuid,jsonb);
        DROP FUNCTION capture.residue_confirmed_result(uuid,uuid,uuid,uuid);
        DROP FUNCTION capture.residue_review_gate(uuid,jsonb);
        DROP TABLE capture.residue_report_selections,capture.residue_source_group_versions;
        DROP FUNCTION capture.residue_immutable();
        """);
}
