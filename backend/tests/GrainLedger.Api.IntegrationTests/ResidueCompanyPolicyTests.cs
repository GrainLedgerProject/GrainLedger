using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrainLedger.Api.Features.Catalogs;
using Npgsql;
using NpgsqlTypes;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LabReportTests;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;
using static GrainLedger.Api.IntegrationTests.ResidueScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ResidueCompanyPolicyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Concurrent_retirement_has_one_winner_and_immutable_audit_record()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var id = await f.PublishRuleAsync();
        var responses = await Task.WhenAll(
            s.Admin.PostAsJsonAsync($"/api/residue-rules/{id}/retire", new { reason = "First review" }),
            s.Admin.PostAsJsonAsync($"/api/residue-rules/{id}/retire", new { reason = "Second review" }));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
            var winner = JsonDocument.Parse(await responses.Single(x => x.IsSuccessStatusCode).Content.ReadAsStringAsync()).RootElement;
            var profile = await GetAsync(s.Admin, $"/api/residue-rules/{id}");
            Assert.Equal(winner.GetProperty("reason").GetString(), profile.GetProperty("retirement").GetProperty("reason").GetString());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Retirement_blocks_new_evaluations_and_bindings_without_rewriting_history()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (_, report) = await f.ReportAsync(sample, [f.Result(uncertainty: null)]);
        var rule = Relative(f, "10");
        var first = await f.PublishRuleAsync(rule);
        var second = f.RuleBody("NUMERIC_RELATIVE_UNCERTAINTY", supersedesId: first, version: 2, code: f.ProfileCode);
        var secondId = await f.PublishRuleAsync(second);
        await f.SelectAsync(sample, report, uncertainty: null);
        var evaluationId = Guid.NewGuid();
        var original = await f.EvaluateAsync(sample.LotId, evaluationId);
        Complete(ResultOf(original, f.SubstanceId), 0.135m, 0.165m);

        await RejectedAsync(s.Customer, HttpMethod.Post, $"/api/residue-rules/{first}/retire",
            new { reason = "Not authorized" }, HttpStatusCode.Forbidden);
        await RejectedAsync(s.Admin, HttpMethod.Post, $"/api/residue-rules/{first}/retire",
            new { reason = " " }, HttpStatusCode.BadRequest);
        var retirement = await SendAsync(s.Admin, HttpMethod.Post, $"/api/residue-rules/{first}/retire",
            new { reason = "  Review withdrawn  " });
        Assert.Equal("Review withdrawn", retirement.GetProperty("reason").GetString());
        Assert.Equal(first, retirement.GetProperty("profileId").GetGuid());
        var listed = await GetAsync(s.Customer, $"/api/residue-rules/{first}");
        Assert.Equal(retirement.GetProperty("retiredBy").GetGuid(), listed.GetProperty("retirement").GetProperty("retiredBy").GetGuid());
        var later = await GetAsync(s.Customer, $"/api/residue-rules/{secondId}");
        Assert.Equal(first, later.GetProperty("retirement").GetProperty("profileId").GetGuid());
        await RejectedAsync(s.Admin, HttpMethod.Post, $"/api/residue-rules/{first}/retire",
            new { reason = "Again" }, HttpStatusCode.Conflict);
        Unresolved(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId));
        Assert.Equal(original.GetRawText(), (await f.EvaluateAsync(sample.LotId, evaluationId)).GetRawText());
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-report-selections",
            f.SelectionBody(sample, report, uncertainty: null), HttpStatusCode.BadRequest);
        var next = f.RuleBody(supersedesId: secondId, version: 3, code: f.ProfileCode);
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/residue-rules", next, HttpStatusCode.Conflict);
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-policy-settings",
            f.SettingBody(f.ProfileCode!, version: 2), HttpStatusCode.Conflict);
        await using (var runtime = new NpgsqlConnection(factory.RuntimeConnectionString))
        {
            await runtime.OpenAsync();
            await using (var privileges = new NpgsqlCommand("""
                SELECT has_table_privilege(current_user,'grainledger.catalog_residue_rule_retirements','SELECT')
                   AND NOT has_table_privilege(current_user,'grainledger.catalog_residue_rule_retirements','INSERT')
                   AND NOT has_table_privilege(current_user,'grainledger.catalog_residue_rule_retirements','UPDATE')
                   AND NOT has_table_privilege(current_user,'grainledger.catalog_residue_rule_retirements','DELETE')
                """, runtime))
                Assert.Equal(true, await privileges.ExecuteScalarAsync());
            await using var append = new NpgsqlCommand("SELECT grainledger.ledger_residue_append($1,$2,$3,$4,$5)", runtime);
            var staleId = Guid.NewGuid();
            append.Parameters.AddWithValue(s.Accounts["customer"].User.Id);
            append.Parameters.AddWithValue(sample.LotId);
            append.Parameters.AddWithValue(staleId);
            append.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(new { substanceIds = new[] { f.SubstanceId } }));
            append.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(new {
                id = staleId, lotId = sample.LotId, companyId = s.OwnerId, snapshotAt = DateTimeOffset.UtcNow,
                engineVersion = "residue-flow-as-received-3", retiredProfileIds = Array.Empty<Guid>()
            }));
            Assert.Equal(PostgresErrorCodes.SerializationFailure,
                (await Assert.ThrowsAsync<PostgresException>(() => append.ExecuteScalarAsync())).SqlState);
        }
    }

    [Fact]
    public async Task Retirement_is_scoped_to_company_series_and_does_not_fall_back_to_system()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (_, report) = await f.ReportAsync(sample, [f.Result(uncertainty: null)]);
        var system = Relative(f, "10");
        var systemId = await f.PublishRuleAsync(system);
        await f.SelectAsync(sample, report, uncertainty: null);
        var settings = await GetAsync(s.Customer, "/api/residue-policy-settings");
        var adoption = settings.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("ruleCode").GetString() == f.ProfileCode).GetProperty("id").GetGuid();
        var own = Relative(f, "20");
        own["code"] = system["code"]!.DeepClone();
        own["companyId"] = s.OwnerId;
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-rules", own);
        var ownId = own["id"]!.GetValue<Guid>();
        await f.PublishSettingAsync(f.SettingBody(f.ProfileCode!, "COMPANY_OVERRIDE", 2, adoption));
        Complete(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId), 0.12m, 0.18m);
        await RejectedAsync(s.ForeignCustomer, HttpMethod.Post, $"/api/residue-rules/{ownId}/retire",
            new { reason = "Foreign" }, HttpStatusCode.NotFound);
        await SendAsync(s.Customer, HttpMethod.Post, $"/api/residue-rules/{ownId}/retire", new { reason = "Withdrawn" });
        Unresolved(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId));
        var global = await GetAsync(s.Customer, $"/api/residue-rules/{systemId}");
        Assert.False(global.TryGetProperty("retirement", out _));
    }

    [Fact]
    public async Task Reviewed_adoption_override_and_return_preserve_company_scope_history_and_replay()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (_, report) = await f.ReportAsync(sample, [f.Result(uncertainty: null)]);
        var rule = Relative(f, "10");
        var ruleId = await f.PublishRuleAsync(rule, adopt: false);
        var noAdoption = await f.EvaluateAsync(sample.LotId);
        Unresolved(ResultOf(noAdoption, f.SubstanceId));

        var adoption = f.SettingBody(rule["code"]!.GetValue<string>());
        var adoptionId = await f.PublishSettingAsync(adoption);
        await f.SelectAsync(sample, report, uncertainty: null);
        var inheritedId = Guid.NewGuid();
        var inherited = await f.EvaluateAsync(sample.LotId, inheritedId);
        var inheritedResult = ResultOf(inherited, f.SubstanceId);
        Complete(inheritedResult, 0.135m, 0.165m);
        var reference = inheritedResult.GetProperty("rules").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == ruleId);
        Assert.Equal("SYSTEM_DEFAULT", reference.GetProperty("policyOrigin").GetString());
        Assert.Equal(adoptionId, reference.GetProperty("policySettingId").GetGuid());
        Assert.Equal(1, reference.GetProperty("policySettingVersion").GetInt32());
        var uncertainty = Assert.Single(inheritedResult.GetProperty("provenance").EnumerateArray());
        Assert.Equal("10", uncertainty.GetProperty("relativeUncertaintyPercent").GetString());
        Assert.Equal(0.015m, Decimal(uncertainty.GetProperty("absoluteUncertainty")));

        var companyRule = Relative(f, "20");
        companyRule["code"] = rule["code"]!.DeepClone();
        companyRule["companyId"] = s.OwnerId;
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-rules", companyRule);
        var overrideSetting = f.SettingBody(rule["code"]!.GetValue<string>(), "COMPANY_OVERRIDE", 2, adoptionId);
        var overrideId = await f.PublishSettingAsync(overrideSetting);
        var own = ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId);
        Complete(own, 0.12m, 0.18m);
        var ownReference = own.GetProperty("rules").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == companyRule["id"]!.GetValue<Guid>());
        Assert.Equal("COMPANY_OVERRIDE", ownReference.GetProperty("policyOrigin").GetString());
        Assert.Equal(overrideId, ownReference.GetProperty("policySettingId").GetGuid());

        using var other = await CreateAsync(factory);
        var foreign = new ResidueScenario(other);
        var otherSample = await SampleAsync(other, await other.ReceiptAsync("10"));
        var (_, otherReport) = await foreign.ReportAsync(otherSample, [f.Result(uncertainty: null)]);
        await foreign.PublishSettingAsync(foreign.SettingBody(rule["code"]!.GetValue<string>()));
        var foreignSelection = foreign.SelectionBody(otherSample, otherReport, uncertainty: null, substanceId: f.SubstanceId);
        foreignSelection["profileCode"] = rule["code"]!.DeepClone();
        await SendAsync(other.Customer, HttpMethod.Post, "/api/residue-report-selections", foreignSelection);
        Complete(ResultOf(await foreign.EvaluateAsync(otherSample.LotId, substances: [f.SubstanceId]), f.SubstanceId), 0.135m, 0.165m);
        var foreignRules = await GetAsync(other.Customer, "/api/residue-rules");
        Assert.DoesNotContain(foreignRules.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == companyRule["id"]!.GetValue<Guid>());

        var restore = f.SettingBody(rule["code"]!.GetValue<string>(), version: 3, supersedesId: overrideId);
        await f.PublishSettingAsync(restore);
        Complete(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId), 0.135m, 0.165m);
        Assert.Equal(inherited.GetRawText(), (await f.EvaluateAsync(sample.LotId, inheritedId)).GetRawText());
        Assert.Equal(noAdoption.GetRawText(), (await GetAsync(s.Customer,
            $"/api/ledger/lots/{sample.LotId}/residue-evaluations/{noAdoption.GetProperty("id").GetGuid()}")).GetRawText());
    }

    [Theory]
    [InlineData("0.15", null, null, false, "0.135", "0.165")]
    [InlineData("0.15", "Laboratory expanded uncertainty requires normalization", null, false, null, null)]
    [InlineData("0.15", "±0.05", "0.05", false, "0.1", "0.2")]
    [InlineData("0", null, null, false, null, null)]
    [InlineData("0", null, null, true, "0", "0")]
    public async Task Relative_policy_respects_report_uncertainty_and_explicit_zero_boundary(
        string value, string? rawUncertainty, string? absolute, bool atZero, string? lower, string? upper)
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (_, report) = await f.ReportAsync(sample, [f.Result(value, rawUncertainty)]);
        var rule = Relative(f, "10");
        rule["laboratory"]![0]!["relativeUncertaintyAtZero"] = atZero;
        await f.PublishRuleAsync(rule);
        await f.SelectAsync(sample, report, absolute);
        var result = ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId);
        if (lower is null) Unresolved(result);
        else Complete(result, decimal.Parse(lower, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(upper!, System.Globalization.CultureInfo.InvariantCulture));
        if (absolute is not null)
        {
            var provenance = Assert.Single(result.GetProperty("provenance").EnumerateArray());
            Assert.Equal(0.05m, Decimal(provenance.GetProperty("absoluteUncertainty")));
            Assert.NotEqual("NUMERIC_RELATIVE_UNCERTAINTY", provenance.GetProperty("uncertaintyMode").GetString());
        }
    }

    [Fact]
    public async Task Preview_uses_confirmed_proposed_selection_without_persisting_or_changing_business_state()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (_, report) = await f.ReportAsync(sample, [f.Result(uncertainty: null)]);
        await f.PublishRuleAsync(Relative(f, "10"));
        var selection = f.SelectionBody(sample, report, uncertainty: null);
        var before = await f.BusinessStateAsync();
        var selectionHistory = await GetAsync(s.Customer, "/api/residue-report-selections");
        var preview = await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-report-selections/preview", selection);
        Assert.Equal(0.135m, Decimal(preview.GetProperty("lower")));
        Assert.Equal(0.165m, Decimal(preview.GetProperty("upper")));
        Assert.Empty(preview.GetProperty("reasons").EnumerateArray());
        Assert.Equal(selectionHistory.GetRawText(), (await GetAsync(s.Customer, "/api/residue-report-selections")).GetRawText());
        Assert.Empty((await GetAsync(s.Customer, $"/api/ledger/lots/{sample.LotId}/residue-evaluations")).GetProperty("items").EnumerateArray());
        Assert.Equal(before, await f.BusinessStateAsync());
        await RejectedAsync(s.ForeignCustomer, HttpMethod.Post, "/api/residue-report-selections/preview", selection, HttpStatusCode.Forbidden);
        selection["companyId"] = s.ForeignOwnerId;
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-report-selections/preview", selection, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Selection_preview_without_as_received_confirmation_cannot_use_group_first_bound()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (_, report) = await f.ReportAsync(sample, [f.Result(uncertainty: null)]);
        await f.PublishRuleAsync(Relative(f, "10"));
        var resultId = report.GetProperty("resultSets")[0].GetProperty("results")[0].GetProperty("id").GetGuid();
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-source-groups",
            f.GroupBody([sample.SourceLotId], [resultId], precedence: "GROUP_FIRST"));
        var selection = f.SelectionBody(sample, report, uncertainty: null);
        var confirmed = await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-report-selections/preview", selection);
        Assert.Equal(0.3m, Decimal(confirmed.GetProperty("lower")));
        Assert.Equal(0.8m, Decimal(confirmed.GetProperty("upper")));

        var before = await f.BusinessStateAsync();
        var selections = await GetAsync(s.Customer, "/api/residue-report-selections");
        var groups = await GetAsync(s.Customer, "/api/residue-source-groups");
        var evaluations = await GetAsync(s.Customer, $"/api/ledger/lots/{sample.LotId}/residue-evaluations");
        selection["asReceivedConfirmed"] = false;
        var preview = await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-report-selections/preview", selection);
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("lower").ValueKind);
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("upper").ValueKind);
        Assert.Contains(preview.GetProperty("reasons").EnumerateArray(),
            x => x.GetString() == "As-received concentration confirmation required for residue bounds");
        Assert.Equal(selections.GetRawText(), (await GetAsync(s.Customer, "/api/residue-report-selections")).GetRawText());
        Assert.Equal(groups.GetRawText(), (await GetAsync(s.Customer, "/api/residue-source-groups")).GetRawText());
        Assert.Equal(evaluations.GetRawText(), (await GetAsync(s.Customer, $"/api/ledger/lots/{sample.LotId}/residue-evaluations")).GetRawText());
        Assert.Equal(before, await f.BusinessStateAsync());
    }

    [Fact]
    public async Task Company_override_does_not_inherit_missing_step_and_return_restores_system_profile()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (_, report) = await f.ReportAsync(sample, [f.Result(uncertainty: null)]);
        var system = Relative(f, "10");
        AddStep(system, "DRYING", "RAW_PADDY", "DRIED_PADDY");
        await f.PublishRuleAsync(system, adopt: false);
        var adoption = await f.PublishSettingAsync(f.SettingBody(system["code"]!.GetValue<string>()));
        await f.SelectAsync(sample, report, uncertainty: null);
        var dried = Output(await s.CommitAsync(await s.DraftAsync("DRYING", [(sample.LotId, "10")], [("DRIED_PADDY", "10")])));
        Complete(ResultOf(await f.EvaluateAsync(dried), f.SubstanceId), 0.135m, 0.165m);

        var company = Relative(f, "10");
        company["code"] = system["code"]!.DeepClone();
        company["companyId"] = s.OwnerId;
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-rules", company);
        var overridden = await f.PublishSettingAsync(f.SettingBody(system["code"]!.GetValue<string>(), "COMPANY_OVERRIDE", 2, adoption));
        Unresolved(ResultOf(await f.EvaluateAsync(dried), f.SubstanceId));
        await f.PublishSettingAsync(f.SettingBody(system["code"]!.GetValue<string>(), version: 3, supersedesId: overridden));
        Complete(ResultOf(await f.EvaluateAsync(dried), f.SubstanceId), 0.135m, 0.165m);
    }

    [Theory]
    [InlineData("adoption-after-source")]
    [InlineData("expired-adoption")]
    public async Task Adoption_outside_source_window_cannot_bind_report(string missing)
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (_, report) = await f.ReportAsync(sample, [f.Result(uncertainty: null)]);
        var rule = Relative(f, "10");
        await f.PublishRuleAsync(rule, adopt: false);
        var setting = f.SettingBody(rule["code"]!.GetValue<string>());
        if (missing == "adoption-after-source") setting["effectiveFrom"] = DateTimeOffset.UtcNow.AddSeconds(1);
        if (missing == "expired-adoption") setting["effectiveTo"] = DateTimeOffset.UtcNow.AddDays(-1);
        await f.PublishSettingAsync(setting);
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-report-selections",
            f.SelectionBody(sample, report, uncertainty: null), HttpStatusCode.BadRequest);
        Unresolved(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId));
    }

    [Fact]
    public async Task Own_company_policy_permission_does_not_grant_global_foreign_or_unreviewed_publication()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var system = Relative(f, "10");
        await f.PublishRuleAsync(system, adopt: false);
        var own = Relative(f, "20"); own["code"] = system["code"]!.DeepClone(); own["companyId"] = s.OwnerId;
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/residue-rules", own, HttpStatusCode.Forbidden);
        await RejectedAsync(s.ForeignCustomer, HttpMethod.Post, "/api/residue-rules", own, HttpStatusCode.Forbidden);
        var global = Relative(f, "20");
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-rules", global, HttpStatusCode.Forbidden);
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-rules", own);
        var ownId = own["id"]!.GetValue<Guid>();
        Assert.Equal(ownId, (await GetAsync(s.Customer, $"/api/residue-rules/{ownId}")).GetProperty("id").GetGuid());
        foreach (var reader in new[] { s.Admin, s.ForeignCustomer })
        {
            using var hidden = await reader.GetAsync($"/api/residue-rules/{ownId}");
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        }
        var setting = f.SettingBody(system["code"]!.GetValue<string>());
        foreach (var client in new[] { s.Admin, s.ForeignCustomer, s.Purchaser, s.Operator })
            await RejectedAsync(client, HttpMethod.Post, "/api/residue-policy-settings", setting, HttpStatusCode.Forbidden);
        var settingId = await f.PublishSettingAsync(setting);
        using var foreignSetting = await s.ForeignCustomer.GetAsync($"/api/residue-policy-settings/{settingId}");
        Assert.Equal(HttpStatusCode.NotFound, foreignSetting.StatusCode);
        var forgedScope = f.SettingBody(system["code"]!.GetValue<string>());
        forgedScope["companyId"] = s.ForeignOwnerId;
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-policy-settings", forgedScope, HttpStatusCode.Forbidden);
        var unreviewed = Relative(f, "10"); unreviewed["reviewedBy"] = "";
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/residue-rules", unreviewed, HttpStatusCode.BadRequest);
        foreach (var invalid in new[] { "-1", "1e1", "10%" })
        {
            var rule = Relative(f, invalid);
            await RejectedAsync(s.Admin, HttpMethod.Post, "/api/residue-rules", rule, HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task Relative_uncertainty_does_not_supply_missing_process_rules_or_nd_thresholds()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (_, report) = await f.ReportAsync(sample, [f.Result(uncertainty: null)]);
        var profile = Relative(f, "10");
        profile["laboratory"]![0]!["ndThresholdKind"] = "REPORTING_THRESHOLD";
        await f.PublishRuleAsync(profile);
        await f.SelectAsync(sample, report, uncertainty: null);
        var dried = Output(await s.CommitAsync(await s.DraftAsync("DRYING", [(sample.LotId, "10")], [("DRIED_PADDY", "10")])));
        Unresolved(ResultOf(await f.EvaluateAsync(dried), f.SubstanceId));
        var revision = (JsonObject)profile.DeepClone();
        revision["id"] = Guid.NewGuid();
        revision["versionNo"] = 2;
        revision["supersedesId"] = profile["id"]!.DeepClone();
        AddStep(revision, "DRYING", "RAW_PADDY", "DRIED_PADDY");
        var processId = await f.PublishRuleAsync(revision);
        var result = ResultOf(await f.EvaluateAsync(dried), f.SubstanceId);
        Complete(result, 0.135m, 0.165m);
        var stepId = revision["steps"]![0]!["id"]!.GetValue<Guid>();
        var reference = result.GetProperty("rules").EnumerateArray().Single(x =>
            x.GetProperty("id").GetGuid() == processId &&
            x.GetProperty("componentKey").GetString() == stepId.ToString());
        Assert.Equal("SYSTEM_DEFAULT", reference.GetProperty("policyOrigin").GetString());
        Assert.Equal(1, reference.GetProperty("policySettingVersion").GetInt32());

        var ndSample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (_, nd) = await f.ReportAsync(ndSample, [f.Result(null, null, "NOT_DETECTED", lod: null, loq: null)]);
        await f.SelectAsync(ndSample, nd, uncertainty: null);
        Unresolved(ResultOf(await f.EvaluateAsync(ndSample.LotId), f.SubstanceId));
    }

    [Fact]
    public async Task Tiny_relative_uncertainty_stays_exact_until_final_processing_rounding()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (_, report) = await f.ReportAsync(sample, [f.Result(uncertainty: null)]);
        var profile = Relative(f, "0.000000000000000001");
        AddStep(profile, "DRYING", "RAW_PADDY", "DRIED_PADDY", "1000000000000", "1000000000000");
        await f.PublishRuleAsync(profile);
        await f.SelectAsync(sample, report, uncertainty: null);
        var dried = Output(await s.CommitAsync(await s.DraftAsync("DRYING", [(sample.LotId, "10")], [("DRIED_PADDY", "10")])));
        Complete(ResultOf(await f.EvaluateAsync(dried), f.SubstanceId), 149999999999.9999999985m, 150000000000.0000000015m);
    }

    [Fact]
    public async Task Returned_inheritance_ignores_unused_override_applicability_for_another_adopted_profile()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var resultBody = JsonSerializer.SerializeToNode(f.Result(uncertainty: null))!.AsObject();
        resultBody["method"] = "OTHER_REVIEWED_METHOD";
        var (_, report) = await f.ReportAsync(sample, [resultBody]);
        var systemA = Relative(f, "10");
        await f.PublishRuleAsync(systemA, adopt: false);
        var adoptedA = await f.PublishSettingAsync(f.SettingBody(systemA["code"]!.GetValue<string>()));
        var overrideA = Relative(f, "30");
        overrideA["code"] = systemA["code"]!.DeepClone();
        overrideA["companyId"] = s.OwnerId;
        overrideA["laboratory"]![0]!["analyticalMethod"] = "OTHER_REVIEWED_METHOD";
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-rules", overrideA);
        var activeOverride = await f.PublishSettingAsync(f.SettingBody(systemA["code"]!.GetValue<string>(), "COMPANY_OVERRIDE", 2, adoptedA));
        await f.PublishSettingAsync(f.SettingBody(systemA["code"]!.GetValue<string>(), version: 3, supersedesId: activeOverride));
        var systemB = Relative(f, "20");
        systemB["laboratory"]![0]!["analyticalMethod"] = "OTHER_REVIEWED_METHOD";
        var selectedB = await f.PublishRuleAsync(systemB);
        await f.SelectAsync(sample, report, uncertainty: null);
        var result = ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId);
        Complete(result, 0.12m, 0.18m);
        Assert.Contains(result.GetProperty("rules").EnumerateArray(), x => x.GetProperty("id").GetGuid() == selectedB);
        Assert.DoesNotContain(result.GetProperty("rules").EnumerateArray(), x => x.GetProperty("id").GetGuid() == overrideA["id"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task Protected_settings_command_rejects_non_json_timestamps_without_poisoning_reads()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (_, report) = await f.ReportAsync(sample, [f.Result(uncertainty: null)]);
        var rule = Relative(f, "10");
        await f.PublishRuleAsync(rule, adopt: false);
        var setting = f.SettingBody(rule["code"]!.GetValue<string>());
        var payload = StoredSetting(setting);
        payload["ReviewedAt"] = "yesterday";
        var error = await Assert.ThrowsAsync<PostgresException>(() => PublishStoredSetting(s, payload));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, error.SqlState);
        Assert.Empty((await GetAsync(s.Customer, "/api/residue-policy-settings")).GetProperty("items").EnumerateArray());
        await f.PublishSettingAsync(setting);
        await f.SelectAsync(sample, report, uncertainty: null);
        Complete(ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId), 0.135m, 0.165m);
    }

    [Fact]
    public async Task Protected_settings_command_treats_case_variants_as_same_superseded_uuid()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var rule = Relative(f, "10");
        await f.PublishRuleAsync(rule, adopt: false);
        var first = f.SettingBody(rule["code"]!.GetValue<string>());
        first["id"] = Guid.Parse("abcdefab-abcd-4abc-8abc-abcdefabcdef");
        var firstId = await f.PublishSettingAsync(first);
        await f.PublishSettingAsync(f.SettingBody(rule["code"]!.GetValue<string>(), version: 2, supersedesId: firstId));
        var fork = StoredSetting(f.SettingBody(rule["code"]!.GetValue<string>(), version: 3, supersedesId: firstId));
        fork["SupersedesId"] = firstId.ToString().ToUpperInvariant();
        var error = await Assert.ThrowsAsync<PostgresException>(() => PublishStoredSetting(s, fork));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        var history = (await GetAsync(s.Customer, "/api/residue-policy-settings")).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new[] { 1, 2 }, history.Select(x => x.GetProperty("versionNo").GetInt32()));
    }

    private static JsonObject StoredSetting(JsonObject request) => JsonSerializer.SerializeToNode(
        JsonSerializer.Deserialize<ResiduePolicySettingVersion>(request.ToJsonString(), new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.AsObject();

    private static async Task PublishStoredSetting(LedgerScenario scenario, JsonObject payload)
    {
        payload["PublishedBy"] = scenario.Accounts["customer"].User.Id;
        await using var connection = new NpgsqlConnection(scenario.Factory.RuntimeConnectionString);
        await connection.OpenAsync();
        await using (var clock = new NpgsqlCommand("SELECT clock_timestamp()", connection))
            payload["PublishedAt"] = new DateTimeOffset((DateTime)(await clock.ExecuteScalarAsync())!);
        await using var command = new NpgsqlCommand("SELECT grainledger.catalog_publish_residue_policy_setting($1,$2)", connection);
        command.Parameters.AddWithValue(scenario.Accounts["customer"].User.Id);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, payload.ToJsonString());
        await command.ExecuteNonQueryAsync();
    }

    private static JsonObject Relative(ResidueScenario scenario, string percent)
    {
        var rule = scenario.RuleBody("NUMERIC_RELATIVE_UNCERTAINTY");
        rule["laboratory"]![0]!["relativeUncertaintyPercent"] = percent;
        rule["laboratory"]![0]!["relativeUncertaintyAtZero"] = false;
        return rule;
    }
}
