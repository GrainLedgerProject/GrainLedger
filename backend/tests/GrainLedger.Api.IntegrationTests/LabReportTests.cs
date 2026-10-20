using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Capture;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class LabReportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);
    private static readonly byte[] OriginalBytes = [0x25, 0x50, 0x44, 0x46, 0x2d, 0x31, 0x2e, 0x37, 0x0a, 0x00, 0x80, 0xff];

    [Fact]
    public async Task Lab_reference_confirmation_requires_matching_recorded_dispatch_evidence_not_generic_handover()
    {
        using var s = await CreateAsync(factory, policies: false);
        var sample = await SampleAsync(s);
        var request = ReportRequest([ResultSet(null)]);
        request["payload"]!["resultSets"]![0]!["laboratorySampleReference"] = "LAB-SPECIMEN-42";
        var id = EntityId(request);
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", request, HttpStatusCode.Created);
        var detail = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        var confirmation = ConfirmRequest(detail, sample);
        confirmation["payload"]!["matches"]![0]!["identityBasis"] = "LAB_REFERENCE";
        confirmation["payload"]!["matches"]![0]!["evidenceRef"] = "dispatch-receipt-42";
        await SendAsync(s.Purchaser, HttpMethod.Post, $"/api/samples/{sample.Id}/handovers", Envelope(new
        {
            entityId = Guid.NewGuid(), fromParty = "Purchasing staff", toParty = "Courier", handedOverAt = CapturedAt,
            handoverKind = "HANDOVER", laboratoryName = "External Test Laboratory", laboratorySampleReference = "LAB-SPECIMEN-42",
            evidenceRef = "dispatch-receipt-42", sealCondition = "Intact"
        }));
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", confirmation, HttpStatusCode.Conflict);
        await SendAsync(s.Purchaser, HttpMethod.Post, $"/api/samples/{sample.Id}/handovers", Envelope(new
        {
            entityId = Guid.NewGuid(), fromParty = "Purchasing staff", toParty = "External Test Laboratory", handedOverAt = CapturedAt,
            handoverKind = "LAB_DISPATCH", laboratoryName = "External Test Laboratory", laboratorySampleReference = "LAB-SPECIMEN-42",
            evidenceRef = "dispatch-receipt-42", sealCondition = "Intact"
        }));
        var wrongEvidence = confirmation.DeepClone().AsObject();
        wrongEvidence["payload"]!["matches"]![0]!["evidenceRef"] = "unrelated-document";
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", wrongEvidence, HttpStatusCode.Conflict);
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", confirmation, HttpStatusCode.OK);
        var confirmed = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        Assert.Equal(JsonValueKind.Null, confirmed.GetProperty("resultSets")[0].GetProperty("sampleId").ValueKind);
        Assert.Equal(sample.Id, confirmed.GetProperty("resultSets")[0].GetProperty("confirmedSampleId").GetGuid());
        Assert.Equal(sample.SourceLotId, confirmed.GetProperty("resultSets")[0].GetProperty("sourceLotId").GetGuid());
        AssertNotAssessed(confirmed);
    }

    [Fact]
    public async Task Persistence_guards_reject_original_row_and_confirmation_changes()
    {
        using var s = await CreateAsync(factory, policies: false);
        var sample = await SampleAsync(s);
        var request = ReportRequest([ResultSet(sample)]);
        var id = EntityId(request);
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", request, HttpStatusCode.Created);
        var detail = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", ConfirmRequest(detail, sample), HttpStatusCode.OK);
        var confirmed = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        await AssertImmutableAsync(async db =>
        {
            (await db.Set<LabReport>().SingleAsync(x => x.Id == id)).Content = [1, 2, 3];
        });
        var setId = detail.GetProperty("resultSets")[0].GetProperty("id").GetGuid();
        var rowId = detail.GetProperty("resultSets")[0].GetProperty("results")[0].GetProperty("id").GetGuid();
        await AssertImmutableAsync(async db =>
        {
            (await db.Set<LabResult>().SingleAsync(x => x.Id == rowId)).OriginalResultText = "Overwritten";
        });
        await AssertImmutableAsync(async db =>
        {
            (await db.Set<LabResultSet>().SingleAsync(x => x.Id == setId)).SampleId = null;
        });
        await AssertImmutableAsync(async db =>
        {
            (await db.Set<LabReportConfirmation>().SingleAsync(x => x.ReportId == id)).TranscriptionChecked = false;
        });
        await AssertImmutableAsync(async db =>
        {
            (await db.Set<LabConfirmedAssociation>().SingleAsync(x => x.ResultSetId == setId)).IdentityBasis = "SEAL_CODE";
        });
        await AssertImmutableAsync(async db =>
        {
            db.Remove(await db.Set<LabResult>().SingleAsync(x => x.Id == rowId));
        });
        await AssertFileAsync(s.Customer, id, OriginalBytes);
        Assert.Equal(confirmed.GetRawText(), (await GetAsync(s.Customer, $"/api/lab-reports/{id}")).GetRawText());
    }

    private async Task AssertImmutableAsync(Func<CaptureDbContext, Task> mutate)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        await mutate(db);
        var exception = await Record.ExceptionAsync(() => db.SaveChangesAsync());
        Assert.NotNull(exception);
        var postgres = Assert.IsType<PostgresException>(exception.GetBaseException());
        Assert.Equal("55000", postgres.SqlState);
        Assert.Equal("Laboratory evidence is immutable.", postgres.MessageText);
    }

    [Fact]
    public async Task Wrong_or_missing_identity_stays_unresolved_and_never_becomes_lot_evidence()
    {
        using var s = await CreateAsync(factory, policies: false);
        var sample = await SampleAsync(s);
        foreach (var set in new[] { ResultSet(sample, statedCode: "WRONG-PRINTED-CODE"), ResultSet(null) })
        {
            var request = ReportRequest([set]);
            var id = EntityId(request);
            await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", request, HttpStatusCode.Created);
            var detail = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
            Assert.Equal("UNRESOLVED", detail.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, detail.GetProperty("confirmedBy").ValueKind);
            Assert.Equal(JsonValueKind.Null, detail.GetProperty("resultSets")[0].GetProperty("sourceLotId").ValueKind);
            Assert.Equal(JsonValueKind.Null, detail.GetProperty("resultSets")[0].GetProperty("confirmedSampleId").ValueKind);
            var confirmation = ConfirmRequest(detail, sample);
            await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", confirmation, HttpStatusCode.Conflict);
            var lotReports = await GetAsync(s.Customer, $"/api/lab-reports?sourceLotId={sample.SourceLotId}");
            Assert.Empty(lotReports.GetProperty("items").EnumerateArray());
            AssertNotAssessed(await GetAsync(s.Customer, $"/api/lab-reports/{id}"));
        }
    }

    [Fact]
    public async Task Confirmed_multi_sample_panel_preserves_exact_qualifiers_unknown_labels_and_separate_rows()
    {
        using var s = await CreateAsync(factory, policies: false);
        var first = await SampleAsync(s);
        var second = await SampleAsync(s);
        await AssertNoPrearrivalStockAsync(s, first);
        await AssertNoPrearrivalStockAsync(s, second);
        var panel = Enumerable.Range(0, 300).Select(i => Result($"Unmapped panel substance {i}", "000.023000000001", value: "000.023000000001")).ToArray();
        var qualifiers = new[]
        {
            Result("Threshold only", "<0.010000000001", "BELOW_LIMIT", "0.010000000001"),
            Result("Not detected", "ND", "NOT_DETECTED", null),
            Result("Unknown qualitative test", "Absent in 25 g", "QUALITATIVE", null),
            Result("Unmapped combined residue definition", "See note α", "OTHER", null)
        };
        var request = ReportRequest([ResultSet(first, panel), ResultSet(second, qualifiers)]);
        var id = EntityId(request);
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", request, HttpStatusCode.Created);
        var before = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        Assert.Equal("PENDING_REVIEW", before.GetProperty("status").GetString());
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", ConfirmRequest(before, first, second), HttpStatusCode.OK);
        var confirmed = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        Assert.Equal("CONFIRMED", confirmed.GetProperty("status").GetString());
        var sets = confirmed.GetProperty("resultSets").EnumerateArray().ToArray();
        Assert.Equal(2, sets.Length);
        var firstSet = sets.Single(x => x.GetProperty("confirmedSampleId").GetGuid() == first.Id);
        var secondSet = sets.Single(x => x.GetProperty("confirmedSampleId").GetGuid() == second.Id);
        Assert.Equal(first.SourceLotId, firstSet.GetProperty("sourceLotId").GetGuid());
        Assert.Equal(second.SourceLotId, secondSet.GetProperty("sourceLotId").GetGuid());
        var rows = firstSet.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(300, rows.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            Assert.Equal($"Unmapped panel substance {i}", rows[i].GetProperty("testLabel").GetString());
            Assert.Equal("000.023000000001", rows[i].GetProperty("numericValue").GetString());
            Assert.Equal("000.023000000001", rows[i].GetProperty("originalResultText").GetString());
            Assert.Equal(JsonValueKind.Null, rows[i].GetProperty("substanceId").ValueKind);
            Assert.Equal("0.000000000001", rows[i].GetProperty("lod").GetString());
            Assert.Equal("0.010000000001", rows[i].GetProperty("loq").GetString());
            Assert.Equal("±0.000000000001", rows[i].GetProperty("uncertainty").GetString());
            Assert.Equal("mg/kg", rows[i].GetProperty("unit").GetString());
            Assert.Equal("Laboratory method A", rows[i].GetProperty("method").GetString());
            Assert.Equal("page 2, row 1", rows[i].GetProperty("reference").GetString());
        }
        var secondRows = secondSet.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(new[] { "BELOW_LIMIT", "NOT_DETECTED", "QUALITATIVE", "OTHER" }, secondRows.Select(x => x.GetProperty("resultKind").GetString()));
        Assert.Equal(new[] { "<0.010000000001", "ND", "Absent in 25 g", "See note α" }, secondRows.Select(x => x.GetProperty("originalResultText").GetString()));
        Assert.Equal("0.010000000001", secondRows[0].GetProperty("numericValue").GetString());
        Assert.All(secondRows.Skip(1), x => Assert.Equal(JsonValueKind.Null, x.GetProperty("numericValue").ValueKind));
        foreach (var sample in new[] { first, second })
        {
            var linked = await GetAsync(s.Customer, $"/api/lab-reports?sourceLotId={sample.SourceLotId}");
            Assert.Equal(id, Assert.Single(linked.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }
        AssertNotAssessed(confirmed);
        await AssertNoPrearrivalStockAsync(s, first);
        await AssertNoPrearrivalStockAsync(s, second);
    }

    [Fact]
    public async Task Intake_and_confirmation_replay_are_stable_and_changed_commands_conflict()
    {
        using var s = await CreateAsync(factory, policies: false);
        var sample = await SampleAsync(s);
        var request = ReportRequest([ResultSet(sample)]);
        var id = EntityId(request);
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", request, HttpStatusCode.Created);
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", request, HttpStatusCode.OK);
        var changed = request.DeepClone().AsObject();
        changed["payload"]!["reportNumber"] = "Changed after capture";
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", changed, HttpStatusCode.Conflict);
        var detail = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        var confirmation = ConfirmRequest(detail, sample);
        var uncheckedIdentity = confirmation.DeepClone().AsObject();
        uncheckedIdentity["payload"]!["identityChecked"] = false;
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", uncheckedIdentity, HttpStatusCode.BadRequest);
        var uncheckedTranscription = confirmation.DeepClone().AsObject();
        uncheckedTranscription["payload"]!["transcriptionChecked"] = false;
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", uncheckedTranscription, HttpStatusCode.BadRequest);
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", confirmation, HttpStatusCode.OK);
        var confirmed = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        Assert.Equal(s.Accounts["customer"].User.Id, confirmed.GetProperty("confirmedBy").GetGuid());
        Assert.Equal(JsonValueKind.String, confirmed.GetProperty("confirmedAt").ValueKind);
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", confirmation, HttpStatusCode.OK);
        Assert.Equal(confirmed.GetRawText(), (await GetAsync(s.Customer, $"/api/lab-reports/{id}")).GetRawText());
        var alteredConfirmation = confirmation.DeepClone().AsObject();
        alteredConfirmation["payload"]!["matches"]![0]!["evidenceRef"] = "Changed evidence";
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", alteredConfirmation, HttpStatusCode.Conflict);
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", ConfirmRequest(detail, sample), HttpStatusCode.Conflict);
        var list = await GetAsync(s.Customer, $"/api/lab-reports?sampleId={sample.Id}");
        Assert.Single(list.GetProperty("items").EnumerateArray());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        Assert.Equal(1, await db.Set<LabReport>().CountAsync(x => x.Id == id));
        Assert.Equal(1, await db.Set<LabReportConfirmation>().CountAsync(x => x.ReportId == id));
        Assert.Equal(1, await db.Set<LabConfirmedAssociation>().CountAsync(x => x.ResultSetId == detail.GetProperty("resultSets")[0].GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Malformed_related_report_id_returns_safe_bad_request_without_persisting_report()
    {
        using var s = await CreateAsync(factory, policies: false);
        var sample = await SampleAsync(s);
        var request = ReportRequest([ResultSet(sample)]);
        request["payload"]!["relatedReportId"] = "invalid-guid-secret";
        using var content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await s.Customer.PostAsync("/api/lab-reports", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Invalid request body", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("Check request field types and formats, then try again.",
            problem.RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain("invalid-guid-secret", problem.RootElement.GetRawText());
        var reports = await GetAsync(s.Customer, "/api/lab-reports");
        Assert.Empty(reports.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Corrections_supplements_and_retests_append_with_reason_and_preserve_original()
    {
        using var s = await CreateAsync(factory, policies: false);
        var sample = await SampleAsync(s);
        var original = ReportRequest([ResultSet(sample)]);
        var id = EntityId(original);
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", original, HttpStatusCode.Created);
        var detail = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", ConfirmRequest(detail, sample), HttpStatusCode.OK);
        var before = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        Assert.Equal(Convert.ToHexString(SHA256.HashData(OriginalBytes)).ToLowerInvariant(), before.GetProperty("sha256").GetString()!.ToLowerInvariant());
        Assert.Equal(OriginalBytes.Length, before.GetProperty("fileSize").GetInt32());
        foreach (var kind in new[] { "AMENDMENT", "SUPPLEMENTARY", "RETEST" })
        {
            await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports",
                ReportRequest([ResultSet(sample)], kind, id), HttpStatusCode.BadRequest);
            await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports",
                ReportRequest([ResultSet(sample)], kind, reason: "Missing parent"), HttpStatusCode.BadRequest);
            var replacementBytes = Encoding.UTF8.GetBytes($"Separate {kind} original");
            var linked = ReportRequest([ResultSet(sample, [Result(text: "ND", kind: "NOT_DETECTED", value: null)])],
                kind, id, $"Evidence for {kind}", replacementBytes);
            await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", linked, HttpStatusCode.Created);
            var child = await GetAsync(s.Customer, $"/api/lab-reports/{EntityId(linked)}");
            Assert.Equal(id, child.GetProperty("relatedReportId").GetGuid());
            Assert.Equal($"Evidence for {kind}", child.GetProperty("relationshipReason").GetString());
            Assert.Equal(kind, child.GetProperty("reportKind").GetString());
            Assert.Equal("ND", child.GetProperty("resultSets")[0].GetProperty("results")[0].GetProperty("originalResultText").GetString());
            AssertNotAssessed(child);
            await AssertFileAsync(s.Customer, EntityId(linked), replacementBytes);
        }
        var after = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        Assert.Equal(before.GetProperty("resultSets").GetRawText(), after.GetProperty("resultSets").GetRawText());
        Assert.Equal(before.GetProperty("confirmedAt").GetRawText(), after.GetProperty("confirmedAt").GetRawText());
        Assert.Equal(before.GetProperty("sha256").GetString(), after.GetProperty("sha256").GetString());
        Assert.Equal(3, after.GetProperty("relatedReports").GetArrayLength());
        Assert.Equal(new[] { "AMENDMENT", "RETEST", "SUPPLEMENTARY" },
            after.GetProperty("relatedReports").EnumerateArray().Select(x => x.GetProperty("reportKind").GetString()).OrderBy(x => x));
        await AssertFileAsync(s.Customer, id, OriginalBytes);
    }

    [Fact]
    public async Task Prearrival_sample_and_late_report_keep_original_source_after_processing_without_stock_or_HOLD_changes()
    {
        using var s = await CreateAsync(factory);
        var sample = await SampleAsync(s);
        await AssertNoPrearrivalStockAsync(s, sample);
        await s.ConfirmArrivalAsync(sample.Receipt);
        var drying = await s.DraftAsync("DRYING", [(sample.LotId, "100")], [("DRIED_PADDY", "100")]);
        await s.CommitAsync(drying);
        var outputId = Output(drying);
        await s.HoldAsync(outputId);
        var sourceBefore = await s.LotAsync(sample.LotId, s.Customer);
        var outputBefore = await s.LotAsync(outputId, s.Customer);
        Assert.Equal(0m, Quantity(sourceBefore));
        Assert.Equal(100m, Quantity(outputBefore));
        Assert.Equal(0m, Quantity(outputBefore, "availableKg"));
        var request = ReportRequest([ResultSet(sample)]);
        var id = EntityId(request);
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", request, HttpStatusCode.Created);
        var detail = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", ConfirmRequest(detail, sample), HttpStatusCode.OK);
        foreach (var kind in new[] { "AMENDMENT", "SUPPLEMENTARY", "RETEST" })
        {
            var linked = ReportRequest([ResultSet(sample)], kind, id, "Later evidence does not release HOLD");
            await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", linked, HttpStatusCode.Created);
            var child = await GetAsync(s.Customer, $"/api/lab-reports/{EntityId(linked)}");
            await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{EntityId(linked)}/confirm", ConfirmRequest(child, sample), HttpStatusCode.OK);
        }
        var confirmed = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        Assert.Equal(sample.SourceLotId, confirmed.GetProperty("resultSets")[0].GetProperty("sourceLotId").GetGuid());
        Assert.Equal(sample.SourceLotId, (await GetAsync(s.Customer, $"/api/samples/{sample.Id}")).GetProperty("sourceLotId").GetGuid());
        Assert.Equal(sourceBefore.GetRawText(), (await s.LotAsync(sample.LotId, s.Customer)).GetRawText());
        Assert.Equal(outputBefore.GetRawText(), (await s.LotAsync(outputId, s.Customer)).GetRawText());
        AssertNotAssessed(confirmed);
    }

    [Fact]
    public async Task Rejects_nonexact_decimals_numeric_JSON_and_unknown_catalog_mappings()
    {
        using var s = await CreateAsync(factory, policies: false);
        var sample = await SampleAsync(s);
        foreach (var value in new[] { "0.1234567890123", "1234567890123", "1e-3", "-0.1", " 0.1" })
            await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports",
                ReportRequest([ResultSet(sample, [Result(value: value)])]), HttpStatusCode.BadRequest);
        var numericJson = ReportRequest([ResultSet(sample)]);
        numericJson["payload"]!["resultSets"]![0]!["results"]![0]!["numericValue"] = 0.023m;
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", numericJson, HttpStatusCode.BadRequest);
        var unknownSubstance = ReportRequest([ResultSet(sample)]);
        unknownSubstance["payload"]!["resultSets"]![0]!["results"]![0]!["substanceId"] = Guid.NewGuid();
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", unknownSubstance, HttpStatusCode.Conflict);
    }

    private static JsonObject ConfirmRequest(JsonElement report, params SampleIdentity[] samples) => Envelope(new
    {
        identityChecked = true, transcriptionChecked = true,
        matches = report.GetProperty("resultSets").EnumerateArray().Select((set, index) => new
        {
            resultSetId = set.GetProperty("id").GetGuid(), sampleId = samples[index].Id, identityBasis = "SAMPLE_CODE", evidenceRef = (string?)null
        }).ToArray()
    });

    private static void AssertNotAssessed(JsonElement report)
    {
        Assert.Equal("NOT_ASSESSED", report.GetProperty("assessmentStatus").GetString());
        Assert.Equal("VN", report.GetProperty("suggestedMarketCode").GetString());
        Assert.Equal(new[] { "COVERAGE_POLICY_NOT_APPROVED", "INTERPRETATION_POLICY_NOT_APPROVED", "LEGAL_DATA_NOT_APPROVED" },
            report.GetProperty("assessmentReasons").EnumerateArray().Select(x => x.GetString()).OrderBy(x => x));
    }

    [Fact]
    public async Task Report_and_original_file_reads_are_company_scoped_and_export_staff_read_only()
    {
        using var s = await CreateAsync(factory, policies: false);
        var sample = await SampleAsync(s);
        var request = ReportRequest([ResultSet(sample)]);
        var reportId = EntityId(request);
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", request, HttpStatusCode.Created);
        var (exportUser, exportSession) = await factory.SeedUserAsync(Roles.ExportStaff, s.OwnerId);
        using var export = factory.CreateRealCookieClient(exportUser, exportSession);
        await CsrfAsync(export);
        foreach (var reader in new[] { s.Customer, export })
        {
            var detail = await GetAsync(reader, $"/api/lab-reports/{reportId}");
            Assert.Equal(reportId, detail.GetProperty("id").GetGuid());
            var list = await GetAsync(reader, "/api/lab-reports");
            Assert.Contains(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == reportId);
            Assert.Equal(sample.Id, (await GetAsync(reader, $"/api/samples/{sample.Id}")).GetProperty("id").GetGuid());
            var samples = await GetAsync(reader, $"/api/samples?sourceLotId={sample.SourceLotId}");
            Assert.Equal(sample.Id, Assert.Single(samples.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
            await GetAsync(reader, $"/api/samples/{sample.Id}/handovers");
            await AssertFileAsync(reader, reportId, OriginalBytes);
        }
        await ExpectAsync(export, HttpMethod.Post, "/api/lab-reports", ReportRequest([ResultSet(sample)]), HttpStatusCode.Forbidden);
        var ownerDetail = await GetAsync(s.Customer, $"/api/lab-reports/{reportId}");
        await ExpectAsync(export, HttpMethod.Post, $"/api/lab-reports/{reportId}/confirm", ConfirmRequest(ownerDetail, sample), HttpStatusCode.Forbidden);
        await ExpectAsync(s.ForeignCustomer, HttpMethod.Post, $"/api/lab-reports/{reportId}/confirm", ConfirmRequest(ownerDetail, sample), HttpStatusCode.NotFound);
        foreach (var route in new[] { $"/api/lab-reports/{reportId}", $"/api/lab-reports/{reportId}/file", $"/api/samples/{sample.Id}", $"/api/samples/{sample.Id}/handovers" })
            using (var response = await s.ForeignCustomer.GetAsync(route))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var foreignList = await GetAsync(s.ForeignCustomer, "/api/lab-reports");
        Assert.DoesNotContain(foreignList.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == reportId);
    }

    [Fact]
    public async Task Purchasing_administrator_and_mill_roles_cannot_read_or_register_lab_evidence()
    {
        using var s = await CreateAsync(factory, policies: false);
        var sample = await SampleAsync(s);
        var report = ReportRequest([ResultSet(sample)]);
        var id = EntityId(report);
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", report, HttpStatusCode.Created);
        var detail = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        foreach (var denied in new[] { s.Purchaser, s.Admin, s.Manager, s.Operator })
        {
            foreach (var route in new[] { "/api/lab-reports", $"/api/lab-reports/{id}", $"/api/lab-reports/{id}/file" })
                using (var response = await denied.GetAsync(route))
                    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            await ExpectAsync(denied, HttpMethod.Post, "/api/lab-reports", ReportRequest([ResultSet(sample)]), HttpStatusCode.Forbidden);
            await ExpectAsync(denied, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", ConfirmRequest(detail, sample), HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Cross_company_sample_related_report_and_unattested_original_are_rejected()
    {
        using var s = await CreateAsync(factory, policies: false);
        var sample = await SampleAsync(s);
        var foreignSource = await s.RegisterSourceAsync(s.Accounts["foreignPurchaser"].Client, s.ForeignOwnerId);
        var foreignReceipt = await SendAsync(s.Accounts["foreignPurchaser"].Client, HttpMethod.Post, "/api/procurements",
            s.ReceiptBody(Guid.NewGuid(), ["100"], farmerId: foreignSource.Farmer, plotSeasonId: foreignSource.PlotSeason));
        var foreignSample = await SampleAsync(s, foreignReceipt, s.Accounts["foreignPurchaser"].Client);
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports",
            ReportRequest([ResultSet(sample), ResultSet(foreignSample)]), HttpStatusCode.Conflict);
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports",
            ReportRequest([ResultSet(foreignSample)]), HttpStatusCode.Conflict);
        var foreignReport = ReportRequest([ResultSet(foreignSample)]);
        await ExpectAsync(s.ForeignCustomer, HttpMethod.Post, "/api/lab-reports", foreignReport, HttpStatusCode.Created);
        var related = ReportRequest([ResultSet(sample)], kind: "AMENDMENT", relatedReportId: EntityId(foreignReport), reason: "Correct transcription");
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", related, HttpStatusCode.Conflict);
        foreach (var attestation in new JsonNode?[] { JsonValue.Create(false), null })
        {
            var unattested = ReportRequest([ResultSet(sample)]);
            unattested["payload"]!["companyOnlyConfirmed"] = attestation;
            await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", unattested, HttpStatusCode.BadRequest);
        }
        var ownerList = await GetAsync(s.Customer, "/api/lab-reports");
        Assert.Empty(ownerList.GetProperty("items").EnumerateArray());
        var ownerReport = ReportRequest([ResultSet(sample)]);
        var ownerId = EntityId(ownerReport);
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", ownerReport, HttpStatusCode.Created);
        var ownerDetail = await GetAsync(s.Customer, $"/api/lab-reports/{ownerId}");
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{ownerId}/confirm",
            ConfirmRequest(ownerDetail, foreignSample), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Confirmed_report_assesses_against_published_VN_MRL_limits_and_flags_unregulated_or_insufficient_rows()
    {
        using var s = await CreateAsync(factory, policies: false);
        var sample = await SampleAsync(s);
        var unit = await s.Admin.PostAsJsonAsync("/api/measurement-units", new
        { code = "MG/KG", name = "Milligram per kilogram", quantityKind = "RESIDUE_CONCENTRATION" });
        Assert.True(unit.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
        var marketResp = await s.Admin.PostAsJsonAsync("/api/markets", new { code = "VN", name = "Vietnam" });
        var vnMarketId = (await marketResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var commResp = await s.Admin.PostAsJsonAsync("/api/commodities", new { code = "RICE", name = "Rice" });
        var riceCommodityId = (await commResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var cpfResp = await s.Admin.PostAsJsonAsync("/api/active-substances", new { code = "CPF006", name = "Chlorpyrifos-ethyl" });
        var cpfId = (await cpfResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var tczResp = await s.Admin.PostAsJsonAsync("/api/active-substances", new { code = "TCZ001", name = "Tricyclazole" });
        var tczId = (await tczResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var importResp = await s.Admin.PostAsJsonAsync("/api/mrl-versions/import", new
        {
            marketId = vnMarketId, commodityId = riceCommodityId,
            effectiveFrom = "2017-07-01T00:00:00Z", effectiveTo = (string?)null,
            sourceRef = "Thông tư 50/2016/TT-BYT",
            limits = new[] { new { substanceCode = "CPF006", limitValue = "0.5", unit = "mg/kg" } }
        });
        Assert.Equal(HttpStatusCode.Created, importResp.StatusCode);
        var versionId = (await importResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var publishReq = new HttpRequestMessage(HttpMethod.Post, $"/api/mrl-versions/{versionId}/publish");
        publishReq.Headers.IfMatch.Add(importResp.Headers.ETag!);
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.SendAsync(publishReq)).StatusCode);

        object MappedRow(string subIdOrCode, string label, string text, string kind, string? num, string? loq = "0.01") => new
        {
            testLabel = label, substanceId = subIdOrCode, originalResultText = text, resultKind = kind,
            numericValue = num, unit = "mg/kg", lod = "0.005", loq, method = "LC-MS/MS", uncertainty = (string?)null, reference = "p.1"
        };

        // 1. Within VN limit (using substance code CPF006 + UUID tczId) -> WITHIN_ASSESSED_LIMITS
        var withinReq = ReportRequest([ResultSet(sample, [
            MappedRow("CPF006", "Chlorpyrifos", "0.20", "NUMERIC", "0.20"),
            MappedRow(tczId.ToString(), "Tricyclazole", "0.05", "NUMERIC", "0.05")
        ])]);
        var withinId = EntityId(withinReq);
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", withinReq, HttpStatusCode.Created);
        var withinDraft = await GetAsync(s.Customer, $"/api/lab-reports/{withinId}");
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{withinId}/confirm", ConfirmRequest(withinDraft, sample), HttpStatusCode.OK);
        var withinConfirmed = await GetAsync(s.Customer, $"/api/lab-reports/{withinId}");
        Assert.Equal("WITHIN_ASSESSED_LIMITS", withinConfirmed.GetProperty("assessmentStatus").GetString());
        var withinReasons = withinConfirmed.GetProperty("assessmentReasons").EnumerateArray().Select(x => x.GetString()!).ToArray();
        Assert.Contains(withinReasons, r => r.Contains("CPF006: WITHIN_LIMIT"));
        Assert.Contains(withinReasons, r => r.Contains("TCZ001: NO_MRL_IN_VN"));

        // 2. Exceeds VN limit -> EXCEEDS_LIMIT
        var exceedsReq = ReportRequest([ResultSet(sample, [MappedRow("CPF006", "Chlorpyrifos", "0.80", "NUMERIC", "0.80")])]);
        var exceedsId = EntityId(exceedsReq);
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", exceedsReq, HttpStatusCode.Created);
        var exceedsDraft = await GetAsync(s.Customer, $"/api/lab-reports/{exceedsId}");
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{exceedsId}/confirm", ConfirmRequest(exceedsDraft, sample), HttpStatusCode.OK);
        var exceedsConfirmed = await GetAsync(s.Customer, $"/api/lab-reports/{exceedsId}");
        Assert.Equal("EXCEEDS_LIMIT", exceedsConfirmed.GetProperty("assessmentStatus").GetString());

        // 3. Reporting limit <0.60 > 0.50 -> INSUFFICIENT_EVIDENCE
        var insuffReq = ReportRequest([ResultSet(sample, [MappedRow("CPF006", "Chlorpyrifos", "<0.60", "BELOW_LIMIT", "0.60")])]);
        var insuffId = EntityId(insuffReq);
        await ExpectAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", insuffReq, HttpStatusCode.Created);
        var insuffDraft = await GetAsync(s.Customer, $"/api/lab-reports/{insuffId}");
        await ExpectAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{insuffId}/confirm", ConfirmRequest(insuffDraft, sample), HttpStatusCode.OK);
        var insuffConfirmed = await GetAsync(s.Customer, $"/api/lab-reports/{insuffId}");
        Assert.Equal("INSUFFICIENT_EVIDENCE", insuffConfirmed.GetProperty("assessmentStatus").GetString());
    }

    private static async Task<SampleIdentity> SampleAsync(LedgerScenario s, JsonElement? receipt = null, HttpClient? purchaser = null)
    {
        var procurement = receipt ?? await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), ["100"]));
        var sourceLotId = procurement.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid();
        var id = Guid.NewGuid();
        var code = $"S-{id:N}";
        var seal = $"SEAL-{id:N}";
        await SendAsync(purchaser ?? s.Purchaser, HttpMethod.Post, "/api/samples", Envelope(new
        {
            entityId = id, sourceLotId, code, sealCode = seal, collectedAt = CapturedAt.AddHours(-1), materialDescription = "Procurement paddy"
        }));
        return new SampleIdentity(id, sourceLotId, Lots(procurement)[0], code, seal, procurement);
    }

    private static async Task AssertNoPrearrivalStockAsync(LedgerScenario s, SampleIdentity sample)
    {
        var source = await s.Purchaser.GetFromJsonAsync<SourceLotResponse>($"/api/source-lots/{sample.SourceLotId}");
        Assert.NotNull(source);
        Assert.Equal(sample.SourceLotId, source.Id);
        Assert.Equal(sample.LotId, source.LotId);
        Assert.Equal("PENDING_ARRIVAL", source.ReceivingStatus);
        Assert.Equal("0", source.UsableStockKg);
        Assert.Empty(source.CurrentStock);
        var ledgerLots = await GetAsync(s.Customer, $"/api/ledger/lots?ids={sample.LotId}");
        Assert.Empty(ledgerLots.GetProperty("items").EnumerateArray());
    }

    private static object Result(string label = "Unknown laboratory analyte", string text = "0.023000000001", string kind = "NUMERIC", string? value = "0.023000000001") => new
    {
        testLabel = label, substanceId = (Guid?)null, originalResultText = text, resultKind = kind,
        numericValue = value, unit = "mg/kg", lod = "0.000000000001", loq = "0.010000000001",
        method = "Laboratory method A", uncertainty = "±0.000000000001", reference = "page 2, row 1"
    };

    private static object ResultSet(SampleIdentity? sample, object[]? results = null, string? statedCode = null) => new
    {
        entityId = Guid.NewGuid(), sampleId = sample?.Id, laboratorySampleReference = (string?)null,
        statedSampleCode = statedCode ?? sample?.Code, statedSealCode = sample?.Seal,
        sectionReference = "page 2", results = results ?? [Result()]
    };

    private static JsonObject ReportRequest(object[] sets, string kind = "FINAL", Guid? relatedReportId = null,
        string? reason = null, byte[]? bytes = null) => Envelope(new
    {
        entityId = Guid.NewGuid(), laboratoryName = "External Test Laboratory", reportNumber = $"LAB-{Guid.NewGuid():N}",
        issuedOn = "2026-10-19", receivedAt = CapturedAt, reportKind = kind, relatedReportId,
        relationshipReason = reason, fileName = "original-report.pdf", contentType = "application/pdf",
        contentBase64 = Convert.ToBase64String(bytes ?? OriginalBytes), companyOnlyConfirmed = true,
        laboratoryConclusion = "Laboratory states pass", statedStandard = "Laboratory reference only; not verified legal policy",
        resultSets = sets
    });

    private static JsonObject Envelope(object payload) => JsonSerializer.SerializeToNode(new
    {
        deviceId = "lab-integration-test", clientOperationId = Guid.NewGuid(), capturedAt = CapturedAt,
        payloadSchemaVersion = 1, payload
    })!.AsObject();

    private static Guid EntityId(JsonObject envelope) => envelope["payload"]!["entityId"]!.GetValue<Guid>();

    private static async Task ExpectAsync(HttpClient client, HttpMethod method, string route, object body, HttpStatusCode expected)
    {
        using var response = await RawAsync(client, method, route, body);
        Assert.True(response.StatusCode == expected,
            $"{method} {route}: expected {(int)expected}, got {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    private static async Task AssertFileAsync(HttpClient client, Guid reportId, byte[] expected)
    {
        using var response = await client.GetAsync($"/api/lab-reports/{reportId}/file");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
    }

    private sealed record SampleIdentity(Guid Id, Guid SourceLotId, Guid LotId, string Code, string Seal, JsonElement Receipt);
}
