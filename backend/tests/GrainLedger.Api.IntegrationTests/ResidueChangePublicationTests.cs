using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LabReportTests;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ResidueChangePublicationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Automatic_multi_sample_confirmation_and_journal_rollback_together_then_replay_once()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var samples = new[] { await SampleAsync(s, await s.ReceiptAsync("10")), await SampleAsync(s, await s.ReceiptAsync("20")) };
        await f.PublishRuleAsync(f.RuleBody("NUMERIC_RELATIVE_UNCERTAINTY"));
        var body = ReportRequest(samples.Select(sample => ResultSet(sample, [f.Result(uncertainty: null)])).ToArray());
        var id = EntityId(body);
        await SendAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", body);
        var report = await GetAsync(s.Customer, $"/api/lab-reports/{id}");
        var confirm = ConfirmRequest(report, samples);
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
        await owner.OpenAsync();
        // Fail after the second journal row: both evidence rows and the first
        // journal row have already been inserted in this same transaction.
        await Sql(owner, """
            CREATE FUNCTION grainledger.residue_test_fail_second_journal() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
             IF NEW.cause_kind='REPORT_SELECTION' AND
              (SELECT count(*) FROM grainledger.ledger_residue_changes WHERE company_id=NEW.company_id AND cause_kind='REPORT_SELECTION')=2
             THEN RAISE EXCEPTION 'Synthetic journal failure'; END IF;
             RETURN NEW;
            END $$;
            CREATE TRIGGER z_residue_test_fail_second_journal AFTER INSERT ON grainledger.ledger_residue_changes
             FOR EACH ROW EXECUTE FUNCTION grainledger.residue_test_fail_second_journal();
            """);
        try
        {
            await RejectedAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", confirm, HttpStatusCode.InternalServerError);
        }
        finally
        {
            await Sql(owner, """
                DROP TRIGGER z_residue_test_fail_second_journal ON grainledger.ledger_residue_changes;
                DROP FUNCTION grainledger.residue_test_fail_second_journal();
                """);
        }
        Assert.Equal("PENDING_REVIEW", (await GetAsync(s.Customer, $"/api/lab-reports/{id}")).GetProperty("status").GetString());
        Assert.Empty((await GetAsync(s.Customer, "/api/residue-report-selections")).GetProperty("items").EnumerateArray());
        Assert.DoesNotContain(await CompanyChanges(s.OwnerId), x => Kind(x) == "REPORT_SELECTION");
        var confirmed = await SendAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", confirm);
        Assert.All(confirmed.GetProperty("residueBindings").EnumerateArray(), x => Assert.True(x.GetProperty("published").GetBoolean()));
        var changes = (await CompanyChanges(s.OwnerId)).Where(x => Kind(x) == "REPORT_SELECTION").ToArray();
        Assert.Equal(2, changes.Length);
        Assert.Equal(samples.Select(x => x.LotId).Order(), changes.SelectMany(Roots).Order());
        Assert.All(changes, x => Assert.Equal(s.Accounts["customer"].User.Id, x.GetProperty("actor_id").GetGuid()));
        var replay = await SendAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/confirm", confirm);
        Assert.Equal(confirmed.GetRawText(), replay.GetRawText());
        Assert.Equal(changes.Select(Id), (await CompanyChanges(s.OwnerId)).Where(x => Kind(x) == "REPORT_SELECTION").Select(Id));
    }

    [Fact]
    public async Task Manual_review_is_read_only_until_confirm_and_changed_replay_cannot_append_jobs()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (id, _) = await f.ReportAsync(sample, [f.Result(uncertainty: null)]);
        await f.PublishRuleAsync(f.RuleBody("NUMERIC_RELATIVE_UNCERTAINTY"));
        var before = (await CompanyChanges(s.OwnerId)).Select(Id).ToArray();
        var preview = await SendAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/residue-review/preview",
            new { companyId = s.OwnerId, effectiveFrom = (DateTimeOffset?)null, rows = Array.Empty<object>(), asReceivedConfirmed = true });
        Assert.Equal(before, (await CompanyChanges(s.OwnerId)).Select(Id));
        var confirm = new JsonObject
        {
            ["companyId"] = s.OwnerId, ["effectiveFrom"] = JsonNode.Parse(preview.GetProperty("effectiveFrom").GetRawText()),
            ["asReceivedConfirmed"] = true,
            ["rows"] = new JsonArray(preview.GetProperty("rows").EnumerateArray().Select(x => JsonNode.Parse(x.GetProperty("input").GetRawText())).ToArray())
        };
        var response = await SendAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/residue-review/confirm", confirm);
        var change = Assert.Single(await CompanyChanges(s.OwnerId), x => Kind(x) == "REPORT_SELECTION");
        Assert.Equal(sample.LotId, Assert.Single(Roots(change)));
        var replay = await SendAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/residue-review/confirm", confirm);
        Assert.True(replay.GetProperty("rows")[0].GetProperty("published").GetBoolean());
        Assert.Equal(response.GetProperty("rows")[0].GetProperty("input").GetProperty("id").GetGuid(),
            replay.GetProperty("rows")[0].GetProperty("input").GetProperty("id").GetGuid());
        confirm["rows"]![0]!["note"] = "Different reviewed request";
        await RejectedAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{id}/residue-review/confirm", confirm, HttpStatusCode.Conflict);
        Assert.Equal(Id(change), Id(Assert.Single(await CompanyChanges(s.OwnerId), x => Kind(x) == "REPORT_SELECTION")));
    }

    [Fact]
    public async Task Direct_selection_group_and_supersession_record_exact_sources_and_effectivity()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (_, report) = await f.ReportAsync(sample);
        await f.PublishRuleAsync();
        var selection = await f.SelectAsync(sample, report);
        var first = Assert.Single(await SubjectChanges(selection));
        Assert.Equal("REPORT_SELECTION", Kind(first));
        Assert.Equal(sample.LotId, Assert.Single(Roots(first)));
        Assert.Equal(f.SubstanceId, Assert.Single(first.GetProperty("substance_ids").EnumerateArray()).GetGuid());
        var next = await f.SelectAsync(sample, report, supersedesId: selection);
        Assert.Single(await SubjectChanges(next));
        Assert.Equal(Id(first), Id(Assert.Single(await SubjectChanges(selection))));
        var group = f.GroupBody([sample.SourceLotId], []);
        group["effectiveTo"] = DateTimeOffset.UtcNow.AddDays(3);
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-source-groups", group);
        var changes = await SubjectChanges(group["id"]!.GetValue<Guid>());
        Assert.Equal(new[] { "PUBLICATION", "EFFECTIVE_TO" }.Order(), changes.Select(x => x.GetProperty("phase").GetString()).Order());
        Assert.All(changes, x => Assert.Equal(sample.LotId, Assert.Single(Roots(x))));
        var expiry = changes.Single(x => x.GetProperty("phase").GetString() == "EFFECTIVE_TO");
        Assert.True(expiry.GetProperty("due_at").GetDateTimeOffset() > expiry.GetProperty("known_at").GetDateTimeOffset());
        Assert.All(changes, x => Assert.Equal(s.OwnerId, x.GetProperty("company_id").GetGuid()));
        await RejectedAsync(s.ForeignCustomer, HttpMethod.Post, "/api/residue-source-groups", group, HttpStatusCode.Forbidden);
        Assert.Equal(changes.Select(Id), (await SubjectChanges(group["id"]!.GetValue<Guid>())).Select(Id));
    }

    [Fact]
    public async Task Global_profile_future_adoption_and_retirement_preserve_scope_and_schedule()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var rule = f.RuleBody();
        rule["effectiveFrom"] = DateTimeOffset.UtcNow.AddDays(1);
        rule["effectiveTo"] = DateTimeOffset.UtcNow.AddDays(4);
        var id = await f.PublishRuleAsync(rule, adopt: false);
        var published = await SubjectChanges(id);
        Assert.Equal(3, published.Length);
        Assert.All(published, x => Assert.Equal(JsonValueKind.Null, x.GetProperty("company_id").ValueKind));
        Assert.All(published, x => Assert.Equal("PROFILE_PUBLISHED", Kind(x)));
        Assert.Equal(new[] { "EFFECTIVE_FROM", "EFFECTIVE_TO", "PUBLICATION" }, published.Select(x => x.GetProperty("phase").GetString()).Order());
        var setting = f.SettingBody(rule["code"]!.GetValue<string>());
        setting["effectiveFrom"] = rule["effectiveFrom"]!.DeepClone();
        setting["effectiveTo"] = rule["effectiveTo"]!.DeepClone();
        await f.PublishSettingAsync(setting);
        var adopted = await SubjectChanges(setting["id"]!.GetValue<Guid>());
        Assert.Equal(3, adopted.Length);
        Assert.All(adopted, x => Assert.Equal(s.OwnerId, x.GetProperty("company_id").GetGuid()));
        await SendAsync(s.Admin, HttpMethod.Post, $"/api/residue-rules/{id}/retire", new { reason = ResidueScenario.Review });
        var retired = Assert.Single(await SubjectChanges(id), x => Kind(x) == "PROFILE_RETIRED");
        Assert.Equal(JsonValueKind.Null, retired.GetProperty("company_id").ValueKind);
        Assert.Equal(s.Accounts["admin"].User.Id, retired.GetProperty("actor_id").GetGuid());
        Assert.Equal(4, (await SubjectChanges(id)).Length);
    }

    [Fact]
    public async Task Invalid_later_adoption_rolls_back_earlier_journal_and_valid_batch_replays_once()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var firstRule = f.RuleBody(); var secondRule = f.RuleBody();
        await f.PublishRuleAsync(firstRule, adopt: false); await f.PublishRuleAsync(secondRule, adopt: false);
        var first = f.SettingBody(firstRule["code"]!.GetValue<string>());
        var second = f.SettingBody(secondRule["code"]!.GetValue<string>(), "COMPANY_OVERRIDE");
        var body = new JsonObject { ["items"] = new JsonArray(first, second) };
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-policy-settings/batch", body, HttpStatusCode.BadRequest);
        Assert.Empty(await SubjectChanges(first["id"]!.GetValue<Guid>()));
        Assert.Empty(await SubjectChanges(second["id"]!.GetValue<Guid>()));
        second["mode"] = "SYSTEM_DEFAULT";
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-policy-settings/batch", body);
        var changes = (await CompanyChanges(s.OwnerId)).Where(x => Kind(x) == "POLICY_ADOPTION").Select(Id).ToArray();
        Assert.Equal(2, changes.Length);
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-policy-settings/batch", body);
        Assert.Equal(changes, (await CompanyChanges(s.OwnerId)).Where(x => Kind(x) == "POLICY_ADOPTION").Select(Id));
    }

    [Fact]
    public async Task Committed_topology_captures_pool_aliquot_remainder_and_processing_but_drafts_do_not()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var lot = Lots(await s.ReceiptAsync("10")).Single();
        var location = await f.MixedLocationAsync("RAW_PADDY");
        await s.TransferAsync(lot, "10", to: location);
        var (pool, revision) = await f.PoolAsync(location);
        var aliquot = await f.WithdrawAsync(location, pool, revision, "4");
        var (remainder, _) = await f.PoolAsync(location);
        var changes = await CompanyChanges(s.OwnerId);
        var withdraw = Assert.Single(changes, x => x.GetProperty("origin").TryGetProperty("eventKind", out var kind) && kind.GetString() == "POOL_WITHDRAW" && Roots(x).Contains(pool));
        Assert.Contains(pool, Roots(withdraw)); Assert.Contains(aliquot, Roots(withdraw)); Assert.Contains(remainder, Roots(withdraw));
        var before = changes.Select(Id).ToArray();
        var operationId = Guid.NewGuid();
        await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations",
            s.OperationBody(operationId, "DRYING", [(aliquot, "4")], [("DRIED_PADDY", "4")], inputLocationId: s.SecondLocationId));
        var draft = await GetAsync(s.Operator, $"/api/ledger/operations/{operationId}");
        await GetAsync(s.Customer, $"/api/ledger/lots/{pool}");
        Assert.Equal(before, (await CompanyChanges(s.OwnerId)).Select(Id));
        var committed = await s.CommitAsync(draft);
        var processing = Assert.Single(await CompanyChanges(s.OwnerId), x => Kind(x) == "PROCESSING_COMMITTED");
        Assert.Contains(aliquot, Roots(processing));
        Assert.Contains(Output(committed), Roots(processing));
        Assert.Equal(s.Accounts["operator"].User.Id, processing.GetProperty("actor_id").GetGuid());
        var after = (await CompanyChanges(s.OwnerId)).Select(Id).ToArray();
        await s.CommitAsync(draft);
        Assert.Equal(after, (await CompanyChanges(s.OwnerId)).Select(Id));
    }

    [Fact]
    public async Task Deferred_topology_journal_failure_rolls_back_processing_and_stock()
    {
        using var s = await CreateAsync(factory);
        var lot = Lots(await s.ReceiptAsync("10")).Single();
        var draft = await s.DraftAsync("DRYING", [(lot, "10")], [("DRIED_PADDY", "10")]);
        var before = (await CompanyChanges(s.OwnerId)).Select(Id).ToArray();
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
        await owner.OpenAsync();
        await Sql(owner, """
            CREATE FUNCTION grainledger.residue_test_fail_material_journal() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
             IF NEW.cause_kind='PROCESSING_COMMITTED' THEN RAISE EXCEPTION 'Synthetic deferred journal failure'; END IF;
             RETURN NEW;
            END $$;
            CREATE TRIGGER z_residue_test_fail_material_journal AFTER INSERT ON grainledger.ledger_residue_changes
             FOR EACH ROW EXECUTE FUNCTION grainledger.residue_test_fail_material_journal();
            """);
        try
        {
            await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{draft.GetProperty("id").GetGuid()}/commit",
                new { }, HttpStatusCode.InternalServerError);
        }
        finally
        {
            await Sql(owner, """
                DROP TRIGGER z_residue_test_fail_material_journal ON grainledger.ledger_residue_changes;
                DROP FUNCTION grainledger.residue_test_fail_material_journal();
                """);
        }
        Assert.Equal(before, (await CompanyChanges(s.OwnerId)).Select(Id));
        Assert.Equal(10m, Quantity(await s.LotAsync(lot)));
        Assert.Equal("DRAFT", (await GetAsync(s.Operator, $"/api/ledger/operations/{draft.GetProperty("id").GetGuid()}")).GetProperty("status").GetString());
        await s.CommitAsync(draft);
        Assert.Single(await CompanyChanges(s.OwnerId), x => Kind(x) == "PROCESSING_COMMITTED");
    }

    [Fact]
    public async Task Company_profile_and_retirement_cannot_publish_a_global_or_foreign_cause()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var rule = f.RuleBody();
        rule["companyId"] = s.OwnerId;
        var id = rule["id"]!.GetValue<Guid>();
        await RejectedAsync(s.ForeignCustomer, HttpMethod.Post, "/api/residue-rules", rule, HttpStatusCode.Forbidden);
        Assert.Empty(await SubjectChanges(id));
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-rules", rule);
        await SendAsync(s.Customer, HttpMethod.Post, $"/api/residue-rules/{id}/retire", new { reason = ResidueScenario.Review });
        var changes = await SubjectChanges(id);
        Assert.Equal(2, changes.Length);
        Assert.All(changes, x =>
        {
            Assert.Equal(s.OwnerId, x.GetProperty("company_id").GetGuid());
            Assert.Equal(s.Accounts["customer"].User.Id, x.GetProperty("actor_id").GetGuid());
        });
    }

    [Fact]
    public async Task Runtime_cannot_invoke_enqueue_hooks_or_insert_arbitrary_causes()
    {
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString);
        await runtime.OpenAsync();
        Assert.True((bool)(await Sql(runtime, """
            SELECT NOT has_function_privilege(current_user,'grainledger.ledger_residue_publication_journal()','EXECUTE')
             AND NOT has_function_privilege(current_user,'grainledger.ledger_residue_topology_journal()','EXECUTE')
             AND NOT has_table_privilege(current_user,'grainledger.ledger_residue_changes','INSERT,UPDATE,DELETE')
            """))!);
    }

    private Task<JsonElement[]> CompanyChanges(Guid company) => Read("company_id", company);
    private Task<JsonElement[]> SubjectChanges(Guid subject) => Read("subject_id", subject);
    private async Task<JsonElement[]> Read(string column, Guid value)
    {
        Assert.Contains(column, new[] { "company_id", "subject_id" });
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
        await owner.OpenAsync();
        using var document = JsonDocument.Parse((string)(await Sql(owner,
            $"SELECT coalesce(jsonb_agg(to_jsonb(c) ORDER BY sequence_no),'[]')::text FROM grainledger.ledger_residue_changes c WHERE {column}=@value", ("value", value)))!);
        return document.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray();
    }
    private static Guid Id(JsonElement value) => value.GetProperty("id").GetGuid();
    private static string Kind(JsonElement value) => value.GetProperty("cause_kind").GetString()!;
    private static Guid[] Roots(JsonElement value) => value.GetProperty("root_lot_ids").EnumerateArray().Select(x => x.GetGuid()).ToArray();
    private static async Task<object?> Sql(NpgsqlConnection connection, string sql, params (string Key, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value);
        return await command.ExecuteScalarAsync();
    }
}
