using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Capture;

internal static class LabReportMappings
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LabReport>(b =>
        {
            b.ToTable("lab_reports", t =>
            {
                t.HasCheckConstraint("ck_lab_reports_kind", "report_kind IN ('PARTIAL','FINAL','SUPPLEMENTARY','AMENDMENT','RETEST')");
                t.HasCheckConstraint("ck_lab_reports_company_only", "company_only_confirmed");
                t.HasCheckConstraint("ck_lab_reports_file", "file_size > 0 AND file_size <= 10485760 AND octet_length(content) = file_size");
                t.HasCheckConstraint("ck_lab_reports_related", "(related_report_id IS NULL AND report_kind NOT IN ('SUPPLEMENTARY','AMENDMENT','RETEST')) OR (related_report_id IS NOT NULL AND related_report_id <> id AND relationship_reason IS NOT NULL AND length(btrim(relationship_reason)) > 0)");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.LaboratoryName).HasColumnName("laboratory_name").HasMaxLength(200);
            b.Property(x => x.ReportNumber).HasColumnName("report_number").HasMaxLength(200);
            b.Property(x => x.IssuedOn).HasColumnName("issued_on");
            b.Property(x => x.ReceivedAt).HasColumnName("received_at");
            b.Property(x => x.ReportKind).HasColumnName("report_kind").HasMaxLength(32);
            b.Property(x => x.RelatedReportId).HasColumnName("related_report_id");
            b.Property(x => x.RelationshipReason).HasColumnName("relationship_reason");
            b.Property(x => x.LaboratoryConclusion).HasColumnName("laboratory_conclusion");
            b.Property(x => x.StatedStandard).HasColumnName("stated_standard");
            b.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(255);
            b.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(200);
            b.Property(x => x.FileSize).HasColumnName("file_size");
            b.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64);
            b.Property(x => x.Content).HasColumnName("content");
            b.Property(x => x.CompanyOnlyConfirmed).HasColumnName("company_only_confirmed");
            b.Property(x => x.RecordedBy).HasColumnName("recorded_by");
            b.Property(x => x.CapturedAt).HasColumnName("captured_at");
            b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
            b.Property<ulong>("CreationTransactionId").HasColumnName("creation_transaction_id").HasColumnType("xid8").HasDefaultValueSql("pg_current_xact_id()");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("ux_lab_reports_company_id");
            b.HasIndex(x => new { x.CompanyId, x.RecordedAt, x.Id });
            b.HasIndex(x => x.RelatedReportId);
        });
        modelBuilder.Entity<LabReportRetirement>(b =>
        {
            b.ToTable("lab_report_retirements", t => t.HasCheckConstraint("ck_lab_retirement_status",
                "(status = 'DELETED' AND replacement_report_id IS NULL) OR (status = 'SUPERSEDED' AND replacement_report_id IS NOT NULL AND replacement_report_id <> report_id)"));
            b.HasKey(x => x.ReportId);
            b.Property(x => x.ReportId).HasColumnName("report_id");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.ReplacementReportId).HasColumnName("replacement_report_id");
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
            b.Property(x => x.RecordedBy).HasColumnName("recorded_by");
            b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
            b.HasIndex(x => new { x.CompanyId, x.ReportId }).IsUnique();
            b.HasIndex(x => x.ReplacementReportId).IsUnique();
            b.HasOne<LabReport>().WithMany().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<LabReport>().WithMany().HasForeignKey(x => x.ReplacementReportId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<LabResultSet>(b =>
        {
            b.ToTable("lab_result_sets");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.ReportId).HasColumnName("report_id");
            b.Property(x => x.Position).HasColumnName("position");
            b.Property(x => x.SampleId).HasColumnName("sample_id");
            b.Property(x => x.LaboratorySampleReference).HasColumnName("laboratory_sample_reference");
            b.Property(x => x.StatedSampleCode).HasColumnName("stated_sample_code");
            b.Property(x => x.StatedSealCode).HasColumnName("stated_seal_code");
            b.Property(x => x.SectionReference).HasColumnName("section_reference");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("ux_lab_result_sets_company_id");
            b.HasIndex(x => new { x.ReportId, x.Position }).IsUnique();
            b.HasIndex(x => x.SampleId);
            b.HasOne<LabReport>().WithMany().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<LabResult>(b =>
        {
            b.ToTable("lab_results", t =>
            {
                t.HasCheckConstraint("ck_lab_results_kind", "result_kind IN ('NUMERIC','BELOW_LIMIT','NOT_DETECTED','QUALITATIVE','OTHER')");
                t.HasCheckConstraint("ck_lab_results_numeric_required", "result_kind NOT IN ('NUMERIC','BELOW_LIMIT') OR numeric_value IS NOT NULL");
                foreach (var column in new[] { "numeric_value", "lod", "loq" })
                    t.HasCheckConstraint("ck_lab_results_" + column, column + " IS NULL OR " + column + " ~ '^[0-9]{1,12}(\\.[0-9]{1,12})?$'");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.ResultSetId).HasColumnName("result_set_id");
            b.Property(x => x.Position).HasColumnName("position");
            b.Property(x => x.TestLabel).HasColumnName("test_label");
            b.Property(x => x.SubstanceId).HasColumnName("substance_id");
            b.Property(x => x.OriginalResultText).HasColumnName("original_result_text");
            b.Property(x => x.ResultKind).HasColumnName("result_kind").HasMaxLength(32);
            b.Property(x => x.NumericValue).HasColumnName("numeric_value").HasMaxLength(25);
            b.Property(x => x.Unit).HasColumnName("unit");
            b.Property(x => x.Lod).HasColumnName("lod").HasMaxLength(25);
            b.Property(x => x.Loq).HasColumnName("loq").HasMaxLength(25);
            b.Property(x => x.Method).HasColumnName("method");
            b.Property(x => x.Uncertainty).HasColumnName("uncertainty");
            b.Property(x => x.Reference).HasColumnName("reference");
            b.HasIndex(x => new { x.ResultSetId, x.Position }).IsUnique();
            b.HasIndex(x => x.SubstanceId);
            b.HasOne<LabResultSet>().WithMany().HasForeignKey(x => x.ResultSetId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<LabReportConfirmation>(b =>
        {
            b.ToTable("lab_report_confirmations", t => t.HasCheckConstraint("ck_lab_confirmation_checked", "identity_checked AND transcription_checked"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.ReportId).HasColumnName("report_id");
            b.Property(x => x.ConfirmedBy).HasColumnName("confirmed_by");
            b.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at");
            b.Property(x => x.IdentityChecked).HasColumnName("identity_checked");
            b.Property(x => x.TranscriptionChecked).HasColumnName("transcription_checked");
            b.HasIndex(x => x.ReportId).IsUnique();
            b.Property<ulong>("CreationTransactionId").HasColumnName("creation_transaction_id").HasColumnType("xid8").HasDefaultValueSql("pg_current_xact_id()");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("ux_lab_confirmations_company_id");
        });
        modelBuilder.Entity<LabConfirmedAssociation>(b =>
        {
            b.ToTable("lab_confirmed_associations", t => t.HasCheckConstraint("ck_lab_association_basis", "identity_basis IN ('SAMPLE_CODE','SEAL_CODE','LAB_REFERENCE')"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.ConfirmationId).HasColumnName("confirmation_id");
            b.Property(x => x.ResultSetId).HasColumnName("result_set_id");
            b.Property(x => x.SampleId).HasColumnName("sample_id");
            b.Property(x => x.IdentityBasis).HasColumnName("identity_basis").HasMaxLength(32);
            b.Property(x => x.EvidenceRef).HasColumnName("evidence_ref");
            b.HasIndex(x => x.ResultSetId).IsUnique();
            b.HasIndex(x => x.SampleId);
            b.HasOne<LabReportConfirmation>().WithMany().HasForeignKey(x => x.ConfirmationId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
