using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LabReportTests;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;
using static GrainLedger.Api.IntegrationTests.ResidueScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ResidueReportReviewTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Confirmed_multi_sample_report_reuses_scientific_review_and_appends_atomic_replayable_bindings()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var samples = new[] { await SampleAsync(s, await s.ReceiptAsync("10")), await SampleAsync(s, await s.ReceiptAsync("10")) };
        var body = ReportRequest(samples.Select(sample => ResultSet(sample, [f.Result(uncertainty: null)])).ToArray());
        var reportId = EntityId(body);
        await SendAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", body);
        var report = await GetAsync(s.Customer, $"/api/lab-reports/{reportId}");
        await SendAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{reportId}/confirm", ConfirmRequest(report, samples));
        var rule = Relative(f); var ruleId = await f.PublishRuleAsync(rule);
        var original = await f.EvaluateAsync(samples[0].LotId);
        Unresolved(ResultOf(original, f.SubstanceId));
        var before = await GetAsync(s.Customer, "/api/residue-report-selections");
        var preview = await Preview(s, reportId);
        Assert.Equal(before.GetRawText(), (await GetAsync(s.Customer, "/api/residue-report-selections")).GetRawText());
        Assert.Equal(2, preview.GetProperty("rows").GetArrayLength());
        foreach (var row in preview.GetProperty("rows").EnumerateArray())
        {
            Assert.Empty(row.GetProperty("reasons").EnumerateArray());
            Assert.Equal(ruleId, row.GetProperty("profile").GetProperty("id").GetGuid());
            Assert.Equal("SYNTHETIC_AS_RECEIVED", row.GetProperty("assessment").GetProperty("basis").GetString());
            Assert.Equal(0.135m, Decimal(row.GetProperty("assessment").GetProperty("lower")));
        }
        var confirmation = Confirmation(s, preview);
        await SendAsync(s.Customer, HttpMethod.Post, Route(reportId, "confirm"), confirmation);
        var published = await GetAsync(s.Customer, "/api/residue-report-selections");
        Assert.Equal(2, published.GetProperty("items").GetArrayLength());
        foreach (var selection in published.GetProperty("items").EnumerateArray())
        {
            Assert.Equal("Synthetic reviewer", selection.GetProperty("reviewedBy").GetString());
            Assert.Equal(s.Accounts["customer"].User.Id, selection.GetProperty("publishedBy").GetGuid());
            Assert.Equal(reportId, selection.GetProperty("bindingReportId").GetGuid());
            Assert.Equal(ruleId, selection.GetProperty("profileRuleId").GetGuid());
        }
        await SendAsync(s.Customer, HttpMethod.Post, Route(reportId, "confirm"), confirmation);
        Assert.Equal(published.GetRawText(), (await GetAsync(s.Customer, "/api/residue-report-selections")).GetRawText());
        foreach (var sample in samples) Complete(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId), 0.135m, 0.165m);
        Assert.Equal(original.GetRawText(), (await GetAsync(s.Customer,
            $"/api/ledger/lots/{samples[0].LotId}/residue-evaluations/{original.GetProperty("id").GetGuid()}")).GetRawText());
        confirmation["rows"]![0]!["note"] = "Different request cannot reuse immutable row UUID";
        await RejectedAsync(s.Customer, HttpMethod.Post, Route(reportId, "confirm"), confirmation, HttpStatusCode.Conflict);
        var boundSummary = await Preview(s, reportId);
        Assert.All(boundSummary.GetProperty("rows").EnumerateArray(), row =>
        {
            Assert.True(row.GetProperty("published").GetBoolean());
            Assert.Empty(row.GetProperty("reasons").EnumerateArray());
        });
    }

    [Theory]
    [InlineData("missing-adoption")]
    [InlineData("wrong-method")]
    [InlineData("wrong-unit")]
    [InlineData("adoption-after-source")]
    [InlineData("missing-override")]
    [InlineData("ambiguous-basis")]
    [InlineData("reported-uncertainty")]
    [InlineData("pending-report")]
    public async Task Authoritative_matching_and_uncertainty_fail_closed(string mismatch)
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (reportId, _) = await f.ReportAsync(sample,
            [f.Result(uncertainty: mismatch == "reported-uncertainty" ? "±0.05 laboratory expanded uncertainty" : null,
                unit: mismatch == "wrong-unit" ? "ppb" : "mg/kg")], confirm: mismatch != "pending-report");
        var rule = Relative(f);
        if (mismatch == "wrong-method") rule["analyticalMethod"] = "OTHER_METHOD";
        await f.PublishRuleAsync(rule, adopt: false);
        if (mismatch != "missing-adoption")
        {
            var setting = f.SettingBody(rule["code"]!.GetValue<string>());
            if (mismatch == "missing-override") setting["mode"] = "COMPANY_OVERRIDE";
            if (mismatch == "adoption-after-source") setting["effectiveFrom"] = DateTimeOffset.UtcNow.AddSeconds(1);
            await f.PublishSettingAsync(setting);
        }
        if (mismatch == "ambiguous-basis")
        {
            var other = Relative(f); other["inputBasis"] = "OTHER_REVIEWED_BASIS"; other["outputBasis"] = "OTHER_REVIEWED_BASIS";
            await f.PublishRuleAsync(other);
        }
        var preview = await Preview(s, reportId);
        Assert.NotEmpty(preview.GetProperty("rows")[0].GetProperty("reasons").EnumerateArray());
        if (mismatch != "pending-report")
            await RejectedAsync(s.Customer, HttpMethod.Post, Route(reportId, "confirm"), Confirmation(s, preview), HttpStatusCode.Conflict);
        Assert.Empty((await GetAsync(s.Customer, "/api/residue-report-selections")).GetProperty("items").EnumerateArray());
        if (mismatch == "reported-uncertainty")
        {
            var normalized = Confirmation(s, preview);
            normalized["rows"]![0]!["absoluteUncertainty"] = "0.05";
            normalized["rows"]![0]!["note"] = Review;
            var reviewed = await SendAsync(s.Customer, HttpMethod.Post, Route(reportId, "preview"), normalized);
            Assert.Empty(reviewed.GetProperty("rows")[0].GetProperty("reasons").EnumerateArray());
            Assert.Equal(0.10m, Decimal(reviewed.GetProperty("rows")[0].GetProperty("assessment").GetProperty("lower")));
            await SendAsync(s.Customer, HttpMethod.Post, Route(reportId, "confirm"), Confirmation(s, reviewed));
            Complete(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId), 0.10m, 0.20m);
        }
    }

    [Theory]
    [InlineData("invalid-row")]
    [InlineData("foreign-row")]
    [InlineData("pending-row")]
    [InlineData("stale-preview")]
    [InlineData("null-row")]
    [InlineData("null-rows")]
    public async Task Bad_second_row_cannot_partially_publish_valid_first_row(string invalid)
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var first = await SampleAsync(s, await s.ReceiptAsync("10"));
        var second = await SampleAsync(s, await s.ReceiptAsync("10"));
        var body = ReportRequest([ResultSet(first, [f.Result(uncertainty: null)]), ResultSet(second, [f.Result(uncertainty: null)])]);
        var id = EntityId(body);
        await SendAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", body);
        var report = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        await SendAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", ConfirmRequest(report, first, second));
        var rule = Relative(f); await f.PublishRuleAsync(rule);
        var preview = await Preview(s, id);
        var confirmation = Confirmation(s, preview);
        var secondInput = confirmation["rows"]![1]!;
        var expected = HttpStatusCode.BadRequest;
        if (invalid == "invalid-row") secondInput["absoluteUncertainty"] = "not-decimal";
        if (invalid == "foreign-row")
        {
            var foreignSample = await SampleAsync(s, await s.ForeignReceiptAsync("10"), s.Accounts["foreignPurchaser"].Client);
            secondInput["sourceId"] = foreignSample.SourceLotId;
        }
        if (invalid == "pending-row")
        {
            var (_, pending) = await f.ReportAsync(second, [f.Result(uncertainty: null)], confirm: false);
            secondInput["resultId"] = pending.GetProperty("resultSets")[0].GetProperty("results")[0].GetProperty("id").GetGuid();
        }
        if (invalid == "stale-preview")
        {
            await f.PublishRuleAsync(Relative(f)); expected = HttpStatusCode.Conflict;
        }
        if (invalid == "null-row") confirmation["rows"]![1] = null;
        if (invalid == "null-rows") confirmation["rows"] = null;
        if (invalid is "null-row" or "null-rows")
            await RejectedAsync(s.Customer, HttpMethod.Post, Route(id, "preview"), confirmation, HttpStatusCode.BadRequest);
        await RejectedAsync(s.Customer, HttpMethod.Post, Route(id, "confirm"), confirmation, expected);
        Assert.Empty((await GetAsync(s.Customer, "/api/residue-report-selections")).GetProperty("items").EnumerateArray());
        await RejectedAsync(s.ForeignCustomer, HttpMethod.Post, Route(id, "preview"), new { companyId = s.OwnerId, effectiveFrom = (DateTimeOffset?)null, rows = Array.Empty<object>() }, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Conflicting_report_and_existing_binding_require_named_explicit_supersession()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (_, first) = await f.ReportAsync(sample, [f.Result(uncertainty: null)]);
        await f.PublishRuleAsync(Relative(f));
        var priorId = await f.SelectAsync(sample, first, uncertainty: null);
        var (reportId, _) = await f.ReportAsync(sample, [f.Result("0.2", uncertainty: null)]);
        var preview = await Preview(s, reportId);
        var row = Assert.Single(preview.GetProperty("rows").EnumerateArray());
        Assert.NotEmpty(row.GetProperty("reasons").EnumerateArray());
        var conflict = Assert.Single(row.GetProperty("conflictingResults").EnumerateArray());
        Assert.Contains(first.GetProperty("reportNumber").GetString()!, conflict.GetProperty("label").GetString());
        Assert.Equal(priorId, Assert.Single(row.GetProperty("existingSelections").EnumerateArray()).GetProperty("selectionId").GetGuid());
        var request = Confirmation(s, preview);
        await RejectedAsync(s.Customer, HttpMethod.Post, Route(reportId, "confirm"), request, HttpStatusCode.Conflict);
        request["rows"]![0]!["supersedesId"] = priorId;
        request["rows"]![0]!["supersededResultIds"] = new JsonArray(JsonValue.Create(conflict.GetProperty("resultId").GetGuid()));
        request["rows"]![0]!["note"] = Review;
        var reviewed = await SendAsync(s.Customer, HttpMethod.Post, Route(reportId, "preview"), request);
        Assert.Empty(reviewed.GetProperty("rows")[0].GetProperty("reasons").EnumerateArray());
        await SendAsync(s.Customer, HttpMethod.Post, Route(reportId, "confirm"), Confirmation(s, reviewed));
        Complete(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId), 0.18m, 0.22m);
        Assert.Equal(2, (await GetAsync(s.Customer, "/api/residue-report-selections")).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Not_detected_reporting_threshold_requires_documented_exact_review()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (id, _) = await f.ReportAsync(sample, [f.Result(null, null, "NOT_DETECTED", lod: null, loq: null)]);
        await f.PublishRuleAsync(f.RuleBody("ND", "REPORTING_THRESHOLD"));
        var preview = await Preview(s, id);
        Assert.NotEmpty(preview.GetProperty("rows")[0].GetProperty("reasons").EnumerateArray());
        var request = Confirmation(s, preview);
        request["rows"]![0]!["reportingThreshold"] = "0.02";
        request["rows"]![0]!["note"] = Review;
        var reviewed = await SendAsync(s.Customer, HttpMethod.Post, Route(id, "preview"), request);
        Assert.Empty(reviewed.GetProperty("rows")[0].GetProperty("reasons").EnumerateArray());
        await SendAsync(s.Customer, HttpMethod.Post, Route(id, "confirm"), Confirmation(s, reviewed));
        Complete(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId), 0m, 0.02m);
    }

    [Fact]
    public async Task Confirmed_prearrival_report_section_stays_unresolved_without_blocking_arrived_section_preview()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var arrived = await SampleAsync(s, await s.ReceiptAsync("10"));
        var prearrival = await SampleAsync(s);
        var body = ReportRequest([ResultSet(arrived, [f.Result(uncertainty: null)]),
            ResultSet(prearrival, [f.Result(uncertainty: null)])]);
        var id = EntityId(body);
        await SendAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", body);
        var report = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        await SendAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", ConfirmRequest(report, arrived, prearrival));
        await f.PublishRuleAsync(Relative(f));
        var preview = await Preview(s, id);
        Assert.Equal(2, preview.GetProperty("rows").GetArrayLength());
        var pending = preview.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("input").GetProperty("sourceId").GetGuid() == prearrival.SourceLotId);
        Assert.Contains("Original owned source lot required", pending.GetProperty("reasons").EnumerateArray().Select(x => x.GetString()));
        Assert.Null(pending.GetProperty("assessment").Deserialize<object>());
        var ready = preview.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("reasons").GetArrayLength() == 0);
        await RejectedAsync(s.Customer, HttpMethod.Post, Route(id, "confirm"), Confirmation(s, preview), HttpStatusCode.Conflict);
        Assert.Empty((await GetAsync(s.Customer, "/api/residue-report-selections")).GetProperty("items").EnumerateArray());
        var confirmation = Confirmation(s, preview);
        confirmation["rows"] = new JsonArray(JsonNode.Parse(ready.GetProperty("input").GetRawText()));
        await SendAsync(s.Customer, HttpMethod.Post, Route(id, "confirm"), confirmation);
        Assert.Single((await GetAsync(s.Customer, "/api/residue-report-selections")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Large_real_lab_panel_can_prepare_all_rows_and_confirm_only_compatible_mapped_results()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var results = Enumerable.Range(1, 1001).Select(index =>
        {
            var result = JsonSerializer.SerializeToNode(f.Result(uncertainty: null))!.AsObject();
            result["substanceId"] = null; result["testLabel"] = $"Unmapped laboratory panel analyte {index}";
            return (object)result;
        }).Append(f.Result(uncertainty: null)).ToArray();
        var (id, _) = await f.ReportAsync(sample, results);
        await f.PublishRuleAsync(Relative(f));
        var initial = await Preview(s, id);
        Assert.Equal(1002, initial.GetProperty("rows").GetArrayLength());
        var request = Confirmation(s, initial);
        var prepared = await SendAsync(s.Customer, HttpMethod.Post, Route(id, "preview"), request);
        Assert.Equal(1002, prepared.GetProperty("rows").GetArrayLength());
        await RejectedAsync(s.Customer, HttpMethod.Post, Route(id, "confirm"), Confirmation(s, prepared), HttpStatusCode.Conflict);
        Assert.Empty((await GetAsync(s.Customer, "/api/residue-report-selections")).GetProperty("items").EnumerateArray());
        var ready = Assert.Single(prepared.GetProperty("rows").EnumerateArray(), row => row.GetProperty("reasons").GetArrayLength() == 0);
        var confirmation = Confirmation(s, prepared);
        confirmation["rows"] = new JsonArray(JsonNode.Parse(ready.GetProperty("input").GetRawText()));
        await SendAsync(s.Customer, HttpMethod.Post, Route(id, "confirm"), confirmation);
        Complete(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId), 0.135m, 0.165m);
        Assert.Single((await GetAsync(s.Customer, "/api/residue-report-selections")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Single_confirmation_binds_compatible_micrograms_and_returns_policy_and_mapping_exceptions_with_stable_replay()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var first = await SampleAsync(s, await s.ReceiptAsync("10"));
        var second = await SampleAsync(s, await s.ReceiptAsync("10"));
        var third = await SampleAsync(s, await s.ReceiptAsync("10"));
        await f.PublishRuleAsync(Relative(f));
        var unmapped = JsonSerializer.SerializeToNode(f.Result(uncertainty: null))!.AsObject();
        unmapped["substanceId"] = null;
        var body = ReportRequest([
            ResultSet(first, [f.Result("150", uncertainty: null, unit: "µg/kg", lod: "10", loq: "20"),
                f.Result(uncertainty: null, substanceId: f.OtherSubstanceId), unmapped]),
            ResultSet(second, [f.Result(uncertainty: "±0.05 raw laboratory uncertainty")]),
            ResultSet(third, [f.Result(uncertainty: null), f.Result("0.2", uncertainty: null)])
        ]);
        var id = EntityId(body);
        await SendAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", body);
        var draft = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        var request = ConfirmRequest(draft, first, second, third);
        var confirmed = await SendAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", request);
        Assert.Equal("CONFIRMED", confirmed.GetProperty("status").GetString());
        var rows = confirmed.GetProperty("residueBindings").EnumerateArray().ToArray();
        Assert.Equal(6, rows.Length);
        Assert.Single(rows, row => row.GetProperty("published").GetBoolean());
        Assert.All(rows.Where(row => !row.GetProperty("published").GetBoolean()),
            row => Assert.NotEmpty(row.GetProperty("reasons").EnumerateArray()));
        Assert.Single((await GetAsync(s.Customer, "/api/residue-report-selections")).GetProperty("items").EnumerateArray());
        Complete(ResultOf(await f.EvaluateAsync(first.LotId), f.SubstanceId), 0.135m, 0.165m);
        var result = ResultOf(await f.EvaluateAsync(first.LotId), f.SubstanceId);
        Assert.Equal("150", result.GetProperty("provenance")[0].GetProperty("originalNumericValue").GetString());
        Assert.Equal("µg/kg", result.GetProperty("provenance")[0].GetProperty("originalUnit").GetString());
        var raw = (await GetAsync(s.Customer, $"/api/lab-reports/{id}")).GetProperty("resultSets")[0].GetProperty("results")[0];
        Assert.Equal("150", raw.GetProperty("numericValue").GetString());
        Assert.Equal("10", raw.GetProperty("lod").GetString());
        Assert.Equal("20", raw.GetProperty("loq").GetString());
        var replay = await SendAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", request);
        Assert.Equal(confirmed.GetRawText(), replay.GetRawText());
        Assert.Single((await GetAsync(s.Customer, "/api/residue-report-selections")).GetProperty("items").EnumerateArray());
        await RejectedAsync(s.ForeignCustomer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", request, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Concurrent_retests_confirm_identity_without_replacing_existing_selection()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        await f.PublishRuleAsync(Relative(f));
        var (originalId, _) = await f.ReportAsync(sample, [f.Result(uncertainty: null)]);
        var selectedBefore = await GetAsync(s.Customer, "/api/residue-report-selections");
        Assert.Single(selectedBefore.GetProperty("items").EnumerateArray());
        var drafts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
            await f.ReportAsync(sample, [f.Result(i == 0 ? "0.2" : "0.3", uncertainty: null)],
                confirm: false, kind: "RETEST", relatedReportId: originalId)));
        var results = await Task.WhenAll(drafts.Select(draft => SendAsync(s.Customer, HttpMethod.Post,
            $"/api/lab-reports/{draft.ReportId}/confirm", ConfirmRequest(draft.Report, sample))));
        Assert.All(results, result =>
        {
            Assert.Equal("CONFIRMED", result.GetProperty("status").GetString());
            var row = Assert.Single(result.GetProperty("residueBindings").EnumerateArray());
            Assert.False(row.GetProperty("published").GetBoolean());
            Assert.Contains(row.GetProperty("reasons").EnumerateArray(),
                reason => reason.GetString()!.Contains("explicit reviewed supersession", StringComparison.Ordinal));
        });
        Assert.Equal(selectedBefore.GetRawText(), (await GetAsync(s.Customer, "/api/residue-report-selections")).GetRawText());
        Unresolved(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId));
    }

    [Theory]
    [InlineData("NUMERIC_ABSOLUTE_UNCERTAINTY", null)]
    [InlineData("ND", "REPORTING_THRESHOLD")]
    public async Task Exception_review_and_single_selection_preview_use_original_unit_exactly_once(string kind, string? threshold)
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        await f.PublishRuleAsync(f.RuleBody(kind, threshold));
        var (id, report) = await f.ReportAsync(sample,
            [f.Result(kind == "ND" ? null : "150", kind == "ND" ? null : "±50 µg/kg raw", kind == "ND" ? "NOT_DETECTED" : "NUMERIC",
                unit: "μg/kg", lod: "10", loq: "20")]);
        var initial = await Preview(s, id);
        Assert.NotEmpty(initial.GetProperty("rows")[0].GetProperty("reasons").EnumerateArray());
        var request = Confirmation(s, initial);
        request["rows"]![0]![kind == "ND" ? "reportingThreshold" : "absoluteUncertainty"] = kind == "ND" ? "30" : "50";
        request["rows"]![0]!["note"] = Review;
        var prepared = await SendAsync(s.Customer, HttpMethod.Post, Route(id, "preview"), request);
        var bound = prepared.GetProperty("rows")[0].GetProperty("assessment");
        Assert.Empty(bound.GetProperty("reasons").EnumerateArray());
        var expert = f.SelectionBody(sample, report, uncertainty: kind == "ND" ? null : "50", threshold: kind == "ND" ? "30" : null);
        var single = await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-report-selections/preview", expert);
        Assert.Equal(bound.GetProperty("lower").GetString(), single.GetProperty("lower").GetString());
        Assert.Equal(bound.GetProperty("upper").GetString(), single.GetProperty("upper").GetString());
        await SendAsync(s.Customer, HttpMethod.Post, Route(id, "confirm"), Confirmation(s, prepared));
        Complete(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId), kind == "ND" ? 0m : 0.1m, kind == "ND" ? 0.03m : 0.2m);
    }

    [Fact]
    public async Task Binding_failure_rolls_back_capture_confirmation_and_selections_then_same_envelope_can_retry()
    {
        var gate = new BindingFailureGate();
        await using var isolated = new ApiFactory
        {
            ConfigureTestServices = services =>
            {
                services.RemoveAll<IConfirmedLabReportResidueBinder>();
                services.AddScoped<IConfirmedLabReportResidueBinder>(sp =>
                    new FailAfterBinding(sp.GetRequiredService<ResidueEvaluationService>(), gate));
            }
        };
        await isolated.InitializeAsync();
        using var s = await CreateAsync(isolated);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        await f.PublishRuleAsync(Relative(f));
        var (id, report) = await f.ReportAsync(sample, [f.Result(uncertainty: null)], confirm: false);
        var request = ConfirmRequest(report, sample);
        await RejectedAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", request, HttpStatusCode.InternalServerError);
        var pending = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        Assert.Equal("PENDING_REVIEW", pending.GetProperty("status").GetString());
        Assert.Empty((await GetAsync(s.Customer, "/api/residue-report-selections")).GetProperty("items").EnumerateArray());
        gate.Enabled = false;
        var confirmed = await SendAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", request);
        Assert.True(Assert.Single(confirmed.GetProperty("residueBindings").EnumerateArray()).GetProperty("published").GetBoolean());
        using var scope = isolated.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await identity.Users.SingleAsync(x => x.Id == s.Accounts["customer"].User.Id);
        user.IsActive = false;
        await identity.SaveChangesAsync();
        await RejectedAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", request, HttpStatusCode.Unauthorized);
    }

    private sealed class BindingFailureGate
    {
        public bool Enabled { get; set; } = true;
    }

    private sealed class FailAfterBinding(ResidueEvaluationService inner, BindingFailureGate gate) : IConfirmedLabReportResidueBinder
    {
        public async Task<IReadOnlyList<LabReportResidueBinding>> BindConfirmedReportAsync(NpgsqlConnection connection,
            NpgsqlTransaction transaction, Guid actorId, Guid companyId, Guid reportId, DateTimeOffset confirmedAt,
            CancellationToken ct)
        {
            Assert.Same(connection, transaction.Connection);
            var rows = await inner.BindConfirmedReportAsync(connection, transaction, actorId, companyId, reportId, confirmedAt, ct);
            Assert.True(Assert.Single(rows).Published);
            if (gate.Enabled) throw new InvalidOperationException("Synthetic failure after binding, before caller commit");
            return rows;
        }
    }

    private static JsonObject Relative(ResidueScenario f)
    {
        var rule = f.RuleBody("NUMERIC_RELATIVE_UNCERTAINTY");
        rule["relativeUncertaintyPercent"] = "10"; rule["relativeUncertaintyAtZero"] = false;
        return rule;
    }
    private static string Route(Guid id, string action) => $"/api/lab-reports/{id}/residue-review/{action}";
    private static Task<JsonElement> Preview(LedgerScenario s, Guid id) => SendAsync(s.Customer, HttpMethod.Post, Route(id, "preview"),
        new { companyId = s.OwnerId, effectiveFrom = (DateTimeOffset?)null, rows = Array.Empty<object>() });
    private static JsonObject Confirmation(LedgerScenario s, JsonElement preview) => new()
    {
        ["companyId"] = s.OwnerId, ["effectiveFrom"] = JsonNode.Parse(preview.GetProperty("effectiveFrom").GetRawText()),
        ["rows"] = new JsonArray(preview.GetProperty("rows").EnumerateArray().Select(row => JsonNode.Parse(row.GetProperty("input").GetRawText())).ToArray())
    };
}
