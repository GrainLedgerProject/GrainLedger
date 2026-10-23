using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;
using static GrainLedger.Api.IntegrationTests.LabReportTests;
using static GrainLedger.Api.IntegrationTests.ResidueScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ResidueEvaluationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("NUMERIC", "NUMERIC_ABSOLUTE_UNCERTAINTY", "0.15", "0.05", null, null, "0.1", "0.2")]
    [InlineData("NUMERIC", "NUMERIC_ABSOLUTE_UNCERTAINTY", "0.03", "0.05", null, null, "0", "0.08")]
    [InlineData("BELOW_LIMIT", "BELOW_LIMIT", "0.02", null, null, null, "0", "0.02")]
    [InlineData("NOT_DETECTED", "ND", null, null, "REPORTING_THRESHOLD", "0.03", "0", "0.03")]
    [InlineData("NOT_DETECTED", "ND", null, null, "LOD", null, "0", "0.01")]
    [InlineData("NOT_DETECTED", "ND", null, null, "LOQ", null, "0", "0.02")]
    public async Task Reviewed_direct_qualifier_interpretation_returns_expected_interval_and_exact_provenance(
        string kind, string interpretation, string? value, string? uncertainty, string? ndPolicy, string? threshold, string lower, string upper)
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("100"));
        var (reportId, report) = await f.ReportAsync(sample, [f.Result(value, kind: kind)]);
        var rule = await f.PublishRuleAsync(f.RuleBody(interpretation, ndPolicy));
        var selection = await f.SelectAsync(sample, report, uncertainty, threshold);
        var before = await f.BusinessStateAsync();
        var evaluation = await f.EvaluateAsync(sample.LotId);
        var result = ResultOf(evaluation, f.SubstanceId);
        Complete(result, decimal.Parse(lower, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(upper, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("mg/kg", result.GetProperty("unit").GetString());
        Assert.Equal(Basis, result.GetProperty("basis").GetString());
        var provenance = Assert.Single(result.GetProperty("provenance").EnumerateArray());
        Assert.Equal(reportId, provenance.GetProperty("reportId").GetGuid());
        Assert.Equal(sample.LotId, provenance.GetProperty("sourceLotId").GetGuid());
        Assert.Equal(sample.SourceLotId, provenance.GetProperty("sourceId").GetGuid());
        Assert.Equal(selection, provenance.GetProperty("selectionId").GetGuid());
        Assert.Contains(result.GetProperty("rules").EnumerateArray(), x => x.GetProperty("id").GetGuid() == rule);
        Assert.Equal(before, await f.BusinessStateAsync());
        Assert.False(evaluation.TryGetProperty("pass", out _));
        Assert.True(evaluation.GetProperty("persistedAt").GetDateTimeOffset() >= evaluation.GetProperty("snapshotAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData("pending-identity")]
    [InlineData("missing-policy")]
    [InlineData("missing-selection")]
    [InlineData("missing-uncertainty")]
    [InlineData("unknown-unit")]
    [InlineData("wrong-basis")]
    [InlineData("nd-without-threshold")]
    [InlineData("nd-missing-reviewed-lod")]
    public async Task Unsupported_measurement_never_invents_finite_bound(string missing)
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("100"));
        var nd = missing.StartsWith("nd-", StringComparison.Ordinal);
        var (_, report) = await f.ReportAsync(sample,
            [f.Result(nd ? null : "0.15", uncertainty: "Uninterpreted laboratory prose", kind: nd ? "NOT_DETECTED" : "NUMERIC",
                unit: missing == "unknown-unit" ? "ppm" : "mg/kg", lod: missing == "nd-missing-reviewed-lod" ? null : "0.01")],
            confirm: missing != "pending-identity");
        if (missing != "missing-policy")
            await f.PublishRuleAsync(f.RuleBody(nd ? "ND" : "NUMERIC_ABSOLUTE_UNCERTAINTY",
                nd ? missing == "nd-missing-reviewed-lod" ? "LOD" : "REPORTING_THRESHOLD" : null));
        if (missing is not ("pending-identity" or "missing-selection"))
            await f.SelectAsync(sample, report, missing == "missing-uncertainty" || nd ? null : "0.05",
                basis: missing == "wrong-basis" ? "UNAPPROVED_DRY_MATTER" : Basis);
        Unresolved(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId));
    }

    [Fact]
    public async Task Lot_preview_is_fresh_scoped_and_does_not_append_history()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("100"));
        await f.PublishRuleAsync(); // No report selection: expected result remains explicitly unresolved.
        var route = $"/api/ledger/lots/{sample.LotId}/residue-preview?substanceIds={f.SubstanceId}";

        using var firstResponse = await s.Customer.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(first.TryGetProperty("id", out _));
        Assert.False(first.TryGetProperty("persistedAt", out _));
        Assert.Equal(sample.LotId, first.GetProperty("lotId").GetGuid());
        Assert.Equal("UNRESOLVED", ResultOf(first, f.SubstanceId).GetProperty("status").GetString());

        var firstTime = first.GetProperty("snapshotAt").GetDateTimeOffset();
        var (_, report) = await f.ReportAsync(sample);
        await f.SelectAsync(sample, report);
        var beforeFreshPreview = await f.BusinessStateAsync();
        using var secondResponse = await s.Customer.GetAsync(route);
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(second.GetProperty("snapshotAt").GetDateTimeOffset() >= firstTime);
        Complete(ResultOf(second, f.SubstanceId), 0.1m, 0.2m);
        Assert.Equal(beforeFreshPreview, await f.BusinessStateAsync());
        Assert.Empty((await GetAsync(s.Customer, $"/api/ledger/lots/{sample.LotId}/residue-evaluations"))
            .GetProperty("items").EnumerateArray());

        using var denied = await s.ForeignCustomer.GetAsync(route);
        Assert.Contains(denied.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
        var tooMany = string.Join(',', Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()));
        using var invalid = await s.Customer.GetAsync(
            $"/api/ledger/lots/{sample.LotId}/residue-preview?substanceIds={tooMany}");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Explicit_group_fallback_only_covers_members_and_never_infers_shared_farmer_or_missing_panel()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var tested = await SampleAsync(s, await s.ReceiptAsync("10"));
        var untested = await s.ReceiptAsync("10");
        var unrelated = await s.ReceiptAsync("10"); // Same farmer/plot deliberately not a residue group member.
        var (_, report) = await f.ReportAsync(tested);
        await f.PublishRuleAsync(); await f.SelectAsync(tested, report);
        var untestedLot = Lots(untested).Single();
        Unresolved(ResultOf(await f.EvaluateAsync(untestedLot), f.SubstanceId));
        var support = report.GetProperty("resultSets")[0].GetProperty("results")[0].GetProperty("id").GetGuid();
        var group = f.GroupBody([tested.SourceLotId, untested.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid()], [support]);
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-source-groups", group);
        var evaluation = await f.EvaluateAsync(untestedLot, substances: [f.SubstanceId, f.OtherSubstanceId]);
        var result = ResultOf(evaluation, f.SubstanceId);
        Complete(result, 0.3m, 0.8m);
        Assert.Contains(result.GetProperty("provenance").EnumerateArray(), x => x.GetProperty("groupVersionId").GetGuid() == group["id"]!.GetValue<Guid>());
        Unresolved(ResultOf(evaluation, f.OtherSubstanceId));
        Unresolved(ResultOf(await f.EvaluateAsync(Lots(unrelated).Single()), f.SubstanceId));
        var foreign = f.GroupBody([tested.SourceLotId], [support]); foreign["companyId"] = s.ForeignOwnerId;
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-source-groups", foreign, HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("DIRECT_FIRST", "0.1", "0.2", 1)]
    [InlineData("DIRECT_FIRST", "0.1", "0.2", 2)]
    [InlineData("UNTESTED_ONLY", "0.1", "0.2", 1)]
    [InlineData("UNTESTED_ONLY", "0.1", "0.2", 2)]
    [InlineData("GROUP_FIRST", "0.3", "0.8", 1)]
    public async Task Reviewed_group_precedence_not_tightest_numeric_interval_selects_source_bound(string precedence, string lower, string upper, int groupCount)
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("100"));
        var (_, report) = await f.ReportAsync(sample); await f.PublishRuleAsync(); await f.SelectAsync(sample, report);
        for (var i = 0; i < groupCount; i++)
            await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-source-groups",
                f.GroupBody([sample.SourceLotId], [report.GetProperty("resultSets")[0].GetProperty("results")[0].GetProperty("id").GetGuid()], precedence: precedence));
        Complete(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId),
            decimal.Parse(lower, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(upper, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Real_drying_milling_blend_pool_aliquot_and_remainder_use_committed_parent_paths()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var first = await SampleAsync(s, await s.ReceiptAsync("60"));
        var second = await SampleAsync(s, await s.ReceiptAsync("40"));
        var (_, a) = await f.ReportAsync(first); var (_, b) = await f.ReportAsync(second, [f.Result("0.55", "±0.25")]);
        await f.PublishRuleAsync(); await f.SelectAsync(first, a); await f.SelectAsync(second, b, "0.25");
        await f.ProcessRuleAsync("DRYING", "RAW_PADDY", "DRIED_PADDY");
        await f.ProcessRuleAsync("MILLING", "DRIED_PADDY", "HEAD_RICE", "0.5", "0.5");
        await f.ProcessRuleAsync("BLENDING", "HEAD_RICE", "BLENDED_RICE");
        await f.ProcessRuleAsync("CUSTODY_INGRESS", "BLENDED_RICE", "BLENDED_RICE");
        await f.ProcessRuleAsync("CUSTODY_WITHDRAW", "BLENDED_RICE", "BLENDED_RICE");
        await f.BlendCompatibilityAsync("HEAD_RICE");
        var driedA = Output(await s.CommitAsync(await s.DraftAsync("DRYING", [(first.LotId, "60")], [("DRIED_PADDY", "60")])));
        var driedB = Output(await s.CommitAsync(await s.DraftAsync("DRYING", [(second.LotId, "40")], [("DRIED_PADDY", "40")])));
        var headA = Output(await s.CommitAsync(await s.DraftAsync("MILLING", [(driedA, "60")], [("HEAD_RICE", "60")])));
        var headB = Output(await s.CommitAsync(await s.DraftAsync("MILLING", [(driedB, "40")], [("HEAD_RICE", "40")])));
        var blended = Output(await s.CommitAsync(await s.DraftAsync("BLENDING", [(headA, "60"), (headB, "40")], [("BLENDED_RICE", "100")])));
        Complete(ResultOf(await f.EvaluateAsync(blended), f.SubstanceId), 0.09m, 0.22m);
        var location = await f.MixedLocationAsync(); await s.TransferAsync(blended, "100", to: location);
        var (pool, revision) = await f.PoolAsync(location);
        var aliquot = await f.WithdrawAsync(location, pool, revision, "25");
        var (remainder, _) = await f.PoolAsync(location);
        foreach (var lot in new[] { pool, aliquot, remainder })
        {
            var result = ResultOf(await f.EvaluateAsync(lot), f.SubstanceId);
            Complete(result, 0.09m, 0.22m);
            Assert.Contains(result.GetProperty("dependencies").EnumerateArray(), x => x.GetProperty("lotId").GetGuid() == first.LotId);
            Assert.Contains(result.GetProperty("dependencies").EnumerateArray(), x => x.GetProperty("lotId").GetGuid() == second.LotId);
        }
        Assert.Equal(0m, Quantity(await s.LotAsync(first.LotId)));
        Assert.Equal(0m, Quantity(await s.LotAsync(second.LotId)));
    }

    [Fact]
    public async Task Reconverging_physical_paths_use_each_effective_factor_not_flattened_root_contributions()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("100"));
        var (_, report) = await f.ReportAsync(sample); await f.PublishRuleAsync(); await f.SelectAsync(sample, report);
        var cut = DateTimeOffset.UtcNow.AddHours(-2);
        var firstRule = f.RuleBody("DRYING", kind: "PROCESS", input: "RAW_PADDY", output: "DRIED_PADDY", lower: "0.5", upper: "0.5");
        firstRule["effectiveTo"] = System.Text.Json.Nodes.JsonValue.Create(cut);
        await f.PublishRuleAsync(firstRule);
        var secondRule = f.RuleBody("DRYING", kind: "PROCESS", input: "RAW_PADDY", output: "DRIED_PADDY", lower: "2", upper: "2");
        secondRule["effectiveFrom"] = System.Text.Json.Nodes.JsonValue.Create(cut);
        await f.PublishRuleAsync(secondRule);
        await f.ProcessRuleAsync("MERGE", "DRIED_PADDY", "DRIED_PADDY");
        var paths = new List<(Guid LotId, string Mass)>();
        foreach (var (mass, occurredAt) in new[] { ("60", cut.AddHours(-1)), ("40", cut.AddHours(1)) })
        {
            var operationId = Guid.NewGuid();
            var body = JsonSerializer.SerializeToNode(s.OperationBody(operationId, "DRYING", [(sample.LotId, mass)], [("DRIED_PADDY", mass)]))!;
            body["occurredAt"] = System.Text.Json.Nodes.JsonValue.Create(occurredAt);
            await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", body);
            var draft = await GetAsync(s.Operator, $"/api/ledger/operations/{operationId}");
            paths.Add((Output(await s.CommitAsync(draft)), mass));
        }
        var merged = Output(await s.CommitAsync(await s.DraftAsync("MERGE", paths.ToArray(), [("DRIED_PADDY", "100")])));
        var result = ResultOf(await f.EvaluateAsync(merged), f.SubstanceId);
        Complete(result, 0.11m, 0.22m);
        foreach (var path in paths)
            Assert.Contains(result.GetProperty("dependencies").EnumerateArray(), x => x.GetProperty("parentLotId").ValueKind == JsonValueKind.String &&
                x.GetProperty("parentLotId").GetGuid() == path.LotId);
    }

    [Fact]
    public async Task Tiny_physical_input_with_zero_root_allocation_remains_dependency_until_evidenced()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var receipts = new[] { await s.ReceiptAsync("0.000001"), await s.ReceiptAsync("0.000001"), await s.ReceiptAsync("0.000001") };
        var sources = receipts.Select(r => (Lot: Lots(r).Single(), Source: r.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid()))
            .OrderBy(x => x.Lot.ToString("D"), StringComparer.Ordinal).ToArray();
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-source-groups",
            f.GroupBody(sources.Take(2).Select(x => x.Source).ToArray(), [], "0", "0"));
        await f.ProcessRuleAsync("DRYING", "RAW_PADDY", "DRIED_PADDY");
        var dried = Output(await s.CommitAsync(await s.DraftAsync("DRYING",
            sources.Select(x => (x.Lot, "0.000001")).ToArray(), [("DRIED_PADDY", "0.000001")], "0.000002")));
        await using (var owner = new NpgsqlConnection(factory.OwnerConnectionString))
        {
            await owner.OpenAsync();
            await using var contribution = new NpgsqlCommand(
                "SELECT quantity_kg FROM ledger.contributions WHERE lot_id=$1 AND source_lot_id=$2", owner);
            contribution.Parameters.AddWithValue(dried);
            contribution.Parameters.AddWithValue(sources[2].Lot);
            Assert.Equal(0m, (decimal)(await contribution.ExecuteScalarAsync())!);
        }
        var missing = ResultOf(await f.EvaluateAsync(dried), f.SubstanceId);
        Unresolved(missing);
        Assert.Contains(missing.GetProperty("dependencies").EnumerateArray(), x => x.GetProperty("lotId").GetGuid() == sources[2].Lot);
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-source-groups",
            f.GroupBody([sources[2].Source], [], "1", "1"));
        Complete(ResultOf(await f.EvaluateAsync(dried), f.SubstanceId), 0.333333333333m, 0.333333333334m);
    }

    [Fact]
    public async Task Declared_drying_loss_and_remaining_stock_do_not_authorize_residue_removal_or_bagging_factor()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("100"));
        var (_, report) = await f.ReportAsync(sample); await f.PublishRuleAsync(); await f.SelectAsync(sample, report);
        var dried = Output(await s.CommitAsync(await s.DraftAsync("DRYING", [(sample.LotId, "100")], [("DRIED_PADDY", "80")], "20")));
        Unresolved(ResultOf(await f.EvaluateAsync(dried), f.SubstanceId));
        await f.ProcessRuleAsync("DRYING", "RAW_PADDY", "DRIED_PADDY");
        Complete(ResultOf(await f.EvaluateAsync(dried), f.SubstanceId), 0.1m, 0.2m);
        await f.ProcessRuleAsync("MILLING", "DRIED_PADDY", "HEAD_RICE");
        var head = Output(await s.CommitAsync(await s.DraftAsync("MILLING", [(dried, "80")], [("HEAD_RICE", "80")])));
        var bagged = Output(await s.CommitAsync(await s.DraftAsync("BAGGING", [(head, "40")], [("BAGGED_RICE", "40")], productIdentity: "HEAD_RICE")));
        Unresolved(ResultOf(await f.EvaluateAsync(bagged), f.SubstanceId));
        var rule = f.RuleBody("BAGGING", kind: "PROCESS", input: "HEAD_RICE", output: "BAGGED_RICE");
        rule["outputFraction"] = "HEAD_RICE"; await f.PublishRuleAsync(rule);
        Complete(ResultOf(await f.EvaluateAsync(bagged), f.SubstanceId), 0.1m, 0.2m);
        await s.TransferAsync(head, "20");
        Complete(ResultOf(await f.EvaluateAsync(head), f.SubstanceId), 0.1m, 0.2m);
    }

    [Fact]
    public async Task Late_confirmed_evidence_updates_exhausted_descendant_but_replay_and_history_remain_immutable()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("100"));
        await f.PublishRuleAsync(); await f.ProcessRuleAsync("DRYING", "RAW_PADDY", "DRIED_PADDY");
        var dried = Output(await s.CommitAsync(await s.DraftAsync("DRYING", [(sample.LotId, "100")], [("DRIED_PADDY", "100")])));
        await s.HoldAsync(dried);
        var id = Guid.NewGuid(); var before = await f.EvaluateAsync(dried, id, [f.SubstanceId, f.OtherSubstanceId]);
        Unresolved(ResultOf(before, f.SubstanceId));
        var (_, report) = await f.ReportAsync(sample); await f.SelectAsync(sample, report);
        var stableState = await f.BusinessStateAsync();
        var fresh = await f.EvaluateAsync(dried);
        Complete(ResultOf(fresh, f.SubstanceId), 0.1m, 0.2m);
        var replay = await f.EvaluateAsync(dried, id, [f.OtherSubstanceId, f.SubstanceId]);
        Assert.Equal(before.GetRawText(), replay.GetRawText());
        Assert.Equal(before.GetRawText(), (await GetAsync(s.Customer, $"/api/ledger/lots/{dried}/residue-evaluations/{id}")).GetRawText());
        var history = await GetAsync(s.Customer, $"/api/ledger/lots/{dried}/residue-evaluations");
        Assert.Equal(2, history.GetProperty("items").GetArrayLength());
        await RejectedAsync(s.Customer, HttpMethod.Post, $"/api/ledger/lots/{dried}/residue-evaluations",
            new { id, substanceIds = new[] { f.SubstanceId } }, HttpStatusCode.Conflict);
        Assert.Equal(stableState, await f.BusinessStateAsync());
        Assert.Equal(0m, Quantity(await s.LotAsync(sample.LotId)));
        Assert.Equal(0m, Quantity(await s.LotAsync(dried), "availableKg"));
    }

    [Fact]
    public async Task Retest_and_amendment_do_not_silently_choose_lowest_or_latest_without_reviewed_supersession()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("100"));
        var (originalId, original) = await f.ReportAsync(sample); await f.PublishRuleAsync();
        var selection = await f.SelectAsync(sample, original);
        Complete(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId), 0.1m, 0.2m);
        var originalText = original.GetRawText();
        var (retestId, retest) = await f.ReportAsync(sample, [f.Result("0.05", "±0.02")], kind: "RETEST", relatedReportId: originalId);
        Unresolved(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId));
        var originalResult = original.GetProperty("resultSets")[0].GetProperty("results")[0].GetProperty("id").GetGuid();
        selection = await f.SelectAsync(sample, retest, "0.02", supersededResults: [originalResult], supersedesId: selection);
        Complete(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId), 0.03m, 0.07m);
        var (_, amendment) = await f.ReportAsync(sample, [f.Result("0.4", "±0.1")], kind: "AMENDMENT", relatedReportId: retestId);
        Unresolved(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId));
        var retestResult = retest.GetProperty("resultSets")[0].GetProperty("results")[0].GetProperty("id").GetGuid();
        await f.SelectAsync(sample, amendment, "0.1", supersededResults: [originalResult, retestResult], supersedesId: selection);
        Complete(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId), 0.3m, 0.5m);
        var unchanged = await GetAsync(s.Customer, $"/api/lab-reports/{originalId}");
        Assert.Equal(original.GetProperty("resultSets").GetRawText(), unchanged.GetProperty("resultSets").GetRawText());
        Assert.Equal(original.GetProperty("sha256").GetString(), unchanged.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Corrected_original_and_replacement_keep_distinct_paths_and_downstream_guard_stays_enforced()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var first = await SampleAsync(s, await s.ReceiptAsync("10")); var second = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (_, a) = await f.ReportAsync(first); var (_, b) = await f.ReportAsync(second, [f.Result("0.55", "±0.25")]);
        await f.PublishRuleAsync(); await f.SelectAsync(first, a); await f.SelectAsync(second, b, "0.25");
        await f.ProcessRuleAsync("DRYING", "RAW_PADDY", "DRIED_PADDY");
        var original = await s.CommitAsync(await s.DraftAsync("DRYING", [(first.LotId, "10")], [("DRIED_PADDY", "10")]));
        var originalId = Output(original);
        var history = await f.EvaluateAsync(originalId);
        var replacement = await s.DraftAsync("DRYING", [(second.LotId, "10")], [("DRIED_PADDY", "10")]);
        var correction = await s.CorrectionAsync(original, replacement);
        await SendAsync(s.Operator, HttpMethod.Post, $"/api/ledger/corrections/{correction}/commit",
            new { replacementOperationId = replacement.GetProperty("id").GetGuid() });
        Complete(ResultOf(await f.EvaluateAsync(Output(replacement)), f.SubstanceId), 0.3m, 0.8m);
        Complete(ResultOf(await f.EvaluateAsync(originalId), f.SubstanceId), 0.1m, 0.2m);
        Assert.Equal(history.GetRawText(), (await GetAsync(s.Customer, $"/api/ledger/lots/{originalId}/residue-evaluations/{history.GetProperty("id").GetGuid()}")).GetRawText());
        await f.ProcessRuleAsync("MILLING", "DRIED_PADDY", "HEAD_RICE");
        await s.CommitAsync(await s.DraftAsync("MILLING", [(Output(replacement), "10")], [("HEAD_RICE", "10")]));
        var blockedCorrection = await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/corrections", new
        {
            id = Guid.NewGuid(), operationId = replacement.GetProperty("id").GetGuid(), reason = Review
        });
        var blockedReplacement = await s.DraftAsync("DRYING", [(second.LotId, "10")], [("DRIED_PADDY", "10")]);
        await RejectedAsync(s.Manager, HttpMethod.Post,
            $"/api/ledger/corrections/{blockedCorrection.GetProperty("id").GetGuid()}/reconcile", new
            {
                evidenceId = s.EvidenceId, replacementOperationId = blockedReplacement.GetProperty("id").GetGuid(),
                replacementVersion = blockedReplacement.GetProperty("version").GetString(),
                reconciliation = new[] { new { lotId = Output(replacement), locationId = s.LocationId, quantityKg = "0" } },
                reason = Review
            });
    }

    [Fact]
    public async Task Current_scope_permissions_and_actor_identity_protect_calculation_and_historical_reads()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("100"));
        var (_, report) = await f.ReportAsync(sample); await f.PublishRuleAsync(); await f.SelectAsync(sample, report);
        var id = Guid.NewGuid(); await f.EvaluateAsync(sample.LotId, id);
        foreach (var denied in new[] { s.Admin, s.Purchaser, s.ForeignCustomer })
        {
            await RejectedAsync(denied, HttpMethod.Post, $"/api/ledger/lots/{sample.LotId}/residue-evaluations",
                new { id = Guid.NewGuid(), substanceIds = new[] { f.SubstanceId } },
                HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
            foreach (var route in new[] { $"/api/ledger/lots/{sample.LotId}/residue-evaluations", $"/api/ledger/lots/{sample.LotId}/residue-evaluations/{id}" })
                using (var response = await denied.GetAsync(route)) Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
        }
        Complete(ResultOf(await f.EvaluateAsync(sample.LotId, client: s.Operator), f.SubstanceId), 0.1m, 0.2m);
        using (var original = await s.Operator.GetAsync($"/api/lab-reports/{report.GetProperty("id").GetGuid()}/file"))
            Assert.Equal(HttpStatusCode.Forbidden, original.StatusCode);
        var derived = await f.EvaluateAsync(sample.LotId, client: s.Operator);
        Assert.DoesNotContain("original-report.pdf", derived.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("laboratoryConclusion", derived.GetRawText(), StringComparison.OrdinalIgnoreCase);
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/lots/{sample.LotId}/residue-evaluations",
            new { id, substanceIds = new[] { f.SubstanceId } }, HttpStatusCode.Conflict);
        await RejectedAsync(s.Customer, HttpMethod.Post, $"/api/ledger/lots/{sample.LotId}/residue-evaluations",
            new { id = Guid.NewGuid(), substanceIds = new[] { f.SubstanceId }, actorId = s.Accounts["operator"].User.Id }, HttpStatusCode.BadRequest);
        var (unassigned, session) = await factory.SeedUserAsync(Roles.MillOperator, s.Accounts["operator"].User.OrganizationId);
        using var other = factory.CreateRealCookieClient(unassigned, session); await CsrfAsync(other);
        await RejectedAsync(other, HttpMethod.Post, $"/api/ledger/lots/{sample.LotId}/residue-evaluations",
            new { id = Guid.NewGuid(), substanceIds = new[] { f.SubstanceId } }, HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.SingleAsync(x => x.Id == s.Accounts["customer"].User.Id);
            user.OrganizationId = s.ForeignOwnerId; await db.SaveChangesAsync();
        }
        using (var moved = await s.Customer.GetAsync($"/api/ledger/lots/{sample.LotId}/residue-evaluations/{id}"))
            Assert.Contains(moved.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.SingleAsync(x => x.Id == s.Accounts["operator"].User.Id);
            user.IsActive = false; await db.SaveChangesAsync();
        }
        using var inactive = await s.Operator.GetAsync($"/api/ledger/lots/{sample.LotId}/residue-evaluations/{id}");
        Assert.Contains(inactive.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
    }

    [Fact]
    public async Task Scientific_publication_requires_review_authority_and_own_company_evidence_rights()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("100")); var (_, report) = await f.ReportAsync(sample);
        var rule = f.RuleBody(); rule["reviewAuthority"] = "";
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/residue-rules", rule, HttpStatusCode.BadRequest);
        var reversed = f.RuleBody("DRYING", kind: "PROCESS", input: "RAW_PADDY", output: "DRIED_PADDY",
            lower: "100000000000000000000000.000000000000000002", upper: "100000000000000000000000.000000000000000001");
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/residue-rules", reversed, HttpStatusCode.BadRequest);
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-rules", f.RuleBody(), HttpStatusCode.Forbidden);
        var selection = f.SelectionBody(sample, report);
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/residue-report-selections", selection, HttpStatusCode.Forbidden);
        await RejectedAsync(s.ForeignCustomer, HttpMethod.Post, "/api/residue-report-selections", selection, HttpStatusCode.Forbidden);
        var pending = await f.ReportAsync(sample, [f.Result("0.8")], confirm: false);
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-report-selections", f.SelectionBody(sample, pending.Report), HttpStatusCode.BadRequest);
        Unresolved(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId));
    }

    [Fact]
    public async Task Supplement_adds_missing_substance_coverage_without_replacing_existing_confirmed_result()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("100"));
        var (originalId, original) = await f.ReportAsync(sample);
        await f.PublishRuleAsync(); await f.SelectAsync(sample, original);
        var before = await f.EvaluateAsync(sample.LotId, substances: [f.SubstanceId, f.OtherSubstanceId]);
        Complete(ResultOf(before, f.SubstanceId), 0.1m, 0.2m); Unresolved(ResultOf(before, f.OtherSubstanceId));
        var (_, supplement) = await f.ReportAsync(sample,
            [f.Result("0.5", "±0.1", substanceId: f.OtherSubstanceId)], kind: "SUPPLEMENTARY", relatedReportId: originalId);
        var rule = f.RuleBody(); rule["substanceId"] = f.OtherSubstanceId; await f.PublishRuleAsync(rule);
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-report-selections",
            f.SelectionBody(sample, supplement, "0.1", substanceId: f.OtherSubstanceId));
        var after = await f.EvaluateAsync(sample.LotId, substances: [f.SubstanceId, f.OtherSubstanceId]);
        Complete(ResultOf(after, f.SubstanceId), 0.1m, 0.2m); Complete(ResultOf(after, f.OtherSubstanceId), 0.4m, 0.6m);
        var unchanged = await GetAsync(s.Customer, $"/api/lab-reports/{originalId}");
        Assert.Equal(original.GetProperty("resultSets").GetRawText(), unchanged.GetProperty("resultSets").GetRawText());
        Assert.Equal(before.GetRawText(), (await GetAsync(s.Customer,
            $"/api/ledger/lots/{sample.LotId}/residue-evaluations/{before.GetProperty("id").GetGuid()}")).GetRawText());
    }

    [Fact]
    public async Task Reviewed_versions_and_evaluation_snapshots_reject_database_mutation_and_runtime_bypass()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("100")); var (_, report) = await f.ReportAsync(sample);
        var rule = await f.PublishRuleAsync(); var selection = await f.SelectAsync(sample, report);
        var ruleCode = (await GetAsync(s.Customer, $"/api/residue-rules/{rule}")).GetProperty("code").GetString();
        var setting = (await GetAsync(s.Customer, "/api/residue-policy-settings")).GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("ruleCode").GetString() == ruleCode).GetProperty("id").GetGuid();
        var group = f.GroupBody([sample.SourceLotId], [report.GetProperty("resultSets")[0].GetProperty("results")[0].GetProperty("id").GetGuid()]);
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-source-groups", group);
        var evaluation = await f.EvaluateAsync(sample.LotId); var evaluationId = evaluation.GetProperty("id").GetGuid();
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
        foreach (var (table, id) in new[]
        {
            ("catalog.residue_rule_versions", rule), ("capture.residue_report_selections", selection),
            ("catalog.residue_policy_setting_versions", setting),
            ("capture.residue_source_group_versions", group["id"]!.GetValue<Guid>()),
            ("ledger.residue_evaluations", evaluationId)
        })
        {
            foreach (var sql in new[] { $"UPDATE {table} SET id=id WHERE id=$1", $"DELETE FROM {table} WHERE id=$1" })
            {
                await using var command = new NpgsqlCommand(sql, owner); command.Parameters.AddWithValue(id);
                await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
                await using var bypass = new NpgsqlCommand(sql, runtime); bypass.Parameters.AddWithValue(id);
                var denied = await Assert.ThrowsAsync<PostgresException>(() => bypass.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
            }
            await using var truncate = new NpgsqlCommand($"TRUNCATE {table}", owner);
            await Assert.ThrowsAsync<PostgresException>(() => truncate.ExecuteNonQueryAsync());
            await using var surviving = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE id=$1", owner);
            surviving.Parameters.AddWithValue(id);
            Assert.Equal(1L, (long)(await surviving.ExecuteScalarAsync())!);
        }
        await using (var directRead = new NpgsqlCommand("SELECT snapshot FROM ledger.residue_evaluations WHERE id=$1", runtime))
        {
            directRead.Parameters.AddWithValue(evaluationId);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege,
                (await Assert.ThrowsAsync<PostgresException>(() => directRead.ExecuteScalarAsync())).SqlState);
        }
        Assert.Equal(evaluation.GetRawText(), (await GetAsync(s.Customer,
            $"/api/ledger/lots/{sample.LotId}/residue-evaluations/{evaluationId}")).GetRawText());
    }

    [Fact]
    public async Task Concurrent_policy_and_selection_commits_do_not_create_hybrid_read_snapshot()
    {
        var gate = new SnapshotGate();
        await using var isolated = new ApiFactory
        {
            ConfigureTestServices = services =>
            {
                services.RemoveAll<IResidueEvidenceReader>();
                services.AddSingleton<IResidueEvidenceReader>(new BlockingEvidenceReader(gate));
            }
        };
        await isolated.InitializeAsync();
        using var s = await CreateAsync(isolated);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("100")); var (_, report) = await f.ReportAsync(sample);
        gate.Enabled = true;
        var calculating = f.EvaluateAsync(sample.LotId);
        await gate.Captured.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try { await f.PublishRuleAsync(); await f.SelectAsync(sample, report); }
        finally { gate.Release.TrySetResult(); }
        var captured = await calculating;
        Unresolved(ResultOf(captured, f.SubstanceId));
        gate.Enabled = false;
        Complete(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId), 0.1m, 0.2m);
        Assert.Equal(captured.GetRawText(), (await GetAsync(s.Customer,
            $"/api/ledger/lots/{sample.LotId}/residue-evaluations/{captured.GetProperty("id").GetGuid()}")).GetRawText());
    }

    [Fact]
    public async Task Scope_revoked_after_read_snapshot_prevents_persistence_in_separate_command()
    {
        var gate = new SnapshotGate();
        await using var isolated = new ApiFactory
        {
            ConfigureTestServices = services =>
            {
                services.RemoveAll<IResidueEvidenceReader>();
                services.AddSingleton<IResidueEvidenceReader>(new BlockingEvidenceReader(gate));
            }
        };
        await isolated.InitializeAsync();
        using var s = await CreateAsync(isolated);
        var f = await ResidueScenario.CreateAsync(s); var sample = await SampleAsync(s, await s.ReceiptAsync("100"));
        var id = Guid.NewGuid(); gate.Enabled = true;
        var request = RawAsync(s.Customer, HttpMethod.Post, $"/api/ledger/lots/{sample.LotId}/residue-evaluations",
            new { id, substanceIds = new[] { f.SubstanceId } });
        await gate.Captured.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            using var scope = isolated.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.SingleAsync(x => x.Id == s.Accounts["customer"].User.Id);
            user.OrganizationId = s.ForeignOwnerId; await db.SaveChangesAsync();
        }
        finally { gate.Release.TrySetResult(); }
        using var denied = await request;
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await using var owner = new NpgsqlConnection(isolated.OwnerConnectionString); await owner.OpenAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM ledger.residue_evaluations WHERE id=$1", owner);
        count.Parameters.AddWithValue(id);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    private sealed class SnapshotGate
    {
        public bool Enabled { get; set; }
        public TaskCompletionSource Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BlockingEvidenceReader(SnapshotGate gate) : IResidueEvidenceReader
    {
        private readonly ResidueEvidenceReader _inner = new();
        public async Task<ResidueEvidenceSnapshot> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            Guid companyId, IReadOnlyCollection<Guid> lotIds, IReadOnlyCollection<Guid> substanceIds, DateTimeOffset snapshotAt, CancellationToken ct)
        {
            var snapshot = await _inner.ReadAsync(connection, transaction, companyId, lotIds, substanceIds, snapshotAt, ct);
            if (gate.Enabled)
            {
                await using var command = new NpgsqlCommand("SELECT current_setting('transaction_isolation'), current_setting('transaction_read_only')", connection, transaction);
                await using (var reader = await command.ExecuteReaderAsync(ct))
                {
                    Assert.True(await reader.ReadAsync(ct)); Assert.Equal("repeatable read", reader.GetString(0)); Assert.Equal("on", reader.GetString(1));
                }
                gate.Captured.TrySetResult(); await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            }
            return snapshot;
        }
    }
}
