using System.Net;
using System.Text.Json;
using GrainLedger.Api.Features.Identity;
using GrainLedger.Api.Features.Ledger;
using Microsoft.Extensions.DependencyInjection;
using static GrainLedger.Api.IntegrationTests.LabReportTests;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ResidueRiskApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Secret = "SYNTHETIC-PRIVATE-FARMER-AND-RAW-QC";
    private static readonly DateTimeOffset At = new(2026, 11, 23, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Four_gets_return_scoped_public_fields_and_export_omits_source_provenance()
    {
        using var s = await CreateAsync(factory); var d = await Seed(s);
        var (_, export) = await Reader(s.OwnerId, Roles.ExportStaff); using var exportClient = export;
        var change = await GetAsync(s.Customer, $"/api/residue-changes/{d.Change}");
        Assert.Equal(d.Change, change.GetProperty("id").GetGuid());
        Assert.Equal(s.OwnerId, change.GetProperty("companyId").GetGuid());
        Assert.Equal(3, change.GetProperty("progress").GetProperty("totalTargets").GetInt32());
        Assert.False(change.TryGetProperty("lease_id", out _));
        Assert.False(change.TryGetProperty("discovery_cursor", out _));
        Assert.DoesNotContain(Secret, change.GetRawText());
        var targets = await GetAsync(export, $"/api/residue-changes/{d.Change}/targets");
        Assert.Equal(3, targets.GetProperty("total").GetInt32());
        Assert.DoesNotContain(Secret, targets.GetRawText());
        var cm = await GetAsync(s.Customer, $"/api/residue-risk-events/{d.Risks[0]}");
        var staff = await GetAsync(export, $"/api/residue-risk-events/{d.Risks[0]}");
        var measured = cm.GetProperty("assessment").GetProperty("results")[0];
        var redacted = staff.GetProperty("assessment").GetProperty("results")[0];
        Assert.Equal("0.2", measured.GetProperty("upper").GetString());
        Assert.Equal(measured.GetProperty("upper").GetString(), redacted.GetProperty("upper").GetString());
        Assert.NotEmpty(measured.GetProperty("provenance").EnumerateArray());
        Assert.False(redacted.TryGetProperty("provenance", out _));
        Assert.DoesNotContain(Secret, cm.GetRawText()); Assert.DoesNotContain(Secret, staff.GetRawText());
        var unresolved = await GetAsync(export, $"/api/residue-risk-events/{d.Risks[1]}");
        Assert.Equal(JsonValueKind.Null, unresolved.GetProperty("assessment").GetProperty("results")[0].GetProperty("upper").ValueKind);
        using var response = await export.GetAsync($"/api/residue-risk-events?targetId={d.Lots[0]}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Target_and_risk_pages_are_stable_have_totals_and_apply_filters()
    {
        using var s = await CreateAsync(factory); var d = await Seed(s);
        var target1 = await GetAsync(s.Customer, $"/api/residue-changes/{d.Change}/targets?pageSize=2");
        var target2 = await GetAsync(s.Customer, $"/api/residue-changes/{d.Change}/targets?pageSize=2&page=2");
        Assert.True(target1.GetProperty("hasNext").GetBoolean()); Assert.False(target2.GetProperty("hasNext").GetBoolean());
        Assert.Equal(d.Targets.OrderBy(x => x.ToString("D"), StringComparer.Ordinal), Ids(target1).Concat(Ids(target2)));
        var filtered = await GetAsync(s.Customer, $"/api/residue-changes/{d.Change}/targets?state=COMPLETED&targetKind=LOT&targetId={d.Lots[0]}");
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        Assert.Equal(d.Targets[0], Assert.Single(Ids(filtered)));
        var first = await GetAsync(s.Customer, "/api/residue-risk-events?pageSize=2");
        var second = await GetAsync(s.Customer, "/api/residue-risk-events?pageSize=2&page=2");
        Assert.Equal(4, first.GetProperty("total").GetInt32());
        Assert.True(first.GetProperty("hasNext").GetBoolean()); Assert.False(second.GetProperty("hasNext").GetBoolean());
        Assert.Equal(4, Ids(first).Concat(Ids(second)).Distinct().Count());
        Assert.Equal(Ids(first), Ids(await GetAsync(s.Customer, "/api/residue-risk-events?pageSize=2")));
        var empty = await GetAsync(s.Customer, "/api/residue-risk-events?page=99");
        Assert.Empty(Ids(empty)); Assert.Equal(4, empty.GetProperty("total").GetInt32());
        var interval = await GetAsync(s.Customer, "/api/residue-risk-events?from=2026-11-23T00:00:00Z&to=2026-11-23T00:00:01Z&assessmentStatus=UNRESOLVED&targetKind=LOT");
        Assert.Equal(d.Risks[1], Assert.Single(Ids(interval)));
        var offset = await GetAsync(s.Customer, "/api/residue-risk-events?from=2026-11-23T07:00:00%2B07:00&to=2026-11-23T07:00:01%2B07:00&assessmentStatus=UNRESOLVED");
        Assert.Equal(Ids(interval), Ids(offset));
        var exact = await GetAsync(s.Customer, $"/api/residue-risk-events?targetId={d.Lots[0]}&assessmentStatus=COMPLETE");
        Assert.Equal(2, exact.GetProperty("total").GetInt32());
        Assert.Empty(Ids(await GetAsync(s.Customer, "/api/residue-risk-events?targetKind=PLAN")));
    }

    [Fact]
    public async Task Foreign_ids_are_not_found_and_wrong_roles_and_anonymous_requests_are_denied()
    {
        using var s = await CreateAsync(factory); var d = await Seed(s);
        using var anonymous = factory.CreateClient();
        foreach (var route in Routes(d))
        {
            using var foreign = await s.ForeignCustomer.GetAsync(route);
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            foreach (var client in new[] { s.Admin, s.Operator, s.Manager, s.Purchaser })
            {
                using var denied = await client.GetAsync(route);
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            }
            using var deniedAnonymous = await anonymous.GetAsync(route);
            Assert.Equal(HttpStatusCode.Unauthorized, deniedAnonymous.StatusCode);
        }
        var list = await GetAsync(s.ForeignCustomer, "/api/residue-risk-events"); Assert.Empty(Ids(list));
        using var unknown = await s.Customer.GetAsync($"/api/residue-risk-events/{Guid.NewGuid()}"); Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Current_role_company_and_active_status_are_checked_again_for_existing_cookie()
    {
        using var s = await CreateAsync(factory); var d = await Seed(s);
        var (user, client) = await Reader(s.OwnerId, Roles.ExportStaff); using var reader = client;
        Assert.Equal(d.Change, (await GetAsync(client, $"/api/residue-changes/{d.Change}")).GetProperty("id").GetGuid());
        await Sql("UPDATE grainledger.identity_users SET \"Role\"='PurchasingStaff' WHERE \"Id\"=@id", ("id", user));
        using (var denied = await client.GetAsync($"/api/residue-changes/{d.Change}")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await Sql("UPDATE grainledger.identity_users SET \"Role\"='ExportStaff',\"OrganizationId\"=@company WHERE \"Id\"=@id", ("id", user), ("company", s.ForeignOwnerId));
        using (var denied = await client.GetAsync($"/api/residue-changes/{d.Change}")) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        await Sql("UPDATE grainledger.identity_users SET \"IsActive\"=false WHERE \"Id\"=@id", ("id", user));
        using (var denied = await client.GetAsync("/api/residue-risk-events")) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Global_change_projection_only_counts_owned_targets_and_hides_other_company_failures()
    {
        using var s = await CreateAsync(factory); var d = await Seed(s);
        var foreignLot = Lots(await s.ForeignReceiptAsync("10")).Single();
        var global = Guid.NewGuid();
        await Sql("""
            INSERT INTO grainledger.ledger_residue_changes(id,cause_kind,subject_id,subject_version,actor_id,origin,
              state,discovery_complete,last_error)
            VALUES(@id,'PROFILE_PUBLISHED',gen_random_uuid(),'1',@actor,'{"code":"GLOBAL-SYNTHETIC"}',
              'FAILED_RETRYABLE',true,@secret)
            """, ("id", global), ("actor", s.Accounts["admin"].User.Id), ("secret", Secret));
        await Sql("""
            INSERT INTO grainledger.ledger_residue_change_targets(change_id,company_id,target_kind,target_id,relationship_basis,state)
            VALUES(@id,@own,'LOT',@lot,'MATERIAL','COMPLETED'),(@id,@foreign,'LOT',@foreignLot,'MATERIAL','PENDING')
            """, ("id", global), ("own", s.OwnerId), ("lot", d.Lots[0]), ("foreign", s.ForeignOwnerId), ("foreignLot", foreignLot));
        var own = await GetAsync(s.Customer, $"/api/residue-changes/{global}");
        Assert.True(own.GetProperty("globalCause").GetBoolean());
        Assert.Equal("COMPLETED", own.GetProperty("state").GetString());
        Assert.Equal(1, own.GetProperty("progress").GetProperty("totalTargets").GetInt32());
        Assert.Equal(JsonValueKind.Null, own.GetProperty("errorCode").ValueKind);
        Assert.DoesNotContain(Secret, own.GetRawText());
        var foreign = await GetAsync(s.ForeignCustomer, $"/api/residue-changes/{global}/targets");
        Assert.Equal(foreignLot, Assert.Single(foreign.GetProperty("items").EnumerateArray()).GetProperty("targetId").GetGuid());
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (_, unrelated) = await Reader(company.Id, Roles.CustomerManager); using var unadopted = unrelated;
        using var missing = await unrelated.GetAsync($"/api/residue-changes/{global}"); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=1000001")]
    [InlineData("pageSize=101")]
    [InlineData("page=1.2")]
    [InlineData("page=1&page=2")]
    [InlineData("page=")]
    [InlineData("page=9999999999999999999999")]
    [InlineData("targetId=bad-id")]
    [InlineData("targetId=00000000-0000-0000-0000-000000000000")]
    [InlineData("targetKind=PADDY")]
    [InlineData("assessmentStatus=OK")]
    [InlineData("from=2026-11-23")]
    [InlineData("from=2026-11-23T00:00:00")]
    [InlineData("from=2026-99-23T00:00:00Z")]
    [InlineData("from=2026-11-24T00:00:00Z&to=2026-11-23T00:00:00Z")]
    [InlineData("from=2026-11-23T00:00:00Z&to=2026-11-23T00:00:00Z")]
    [InlineData("companyId=00000000-0000-0000-0000-000000000001")]
    [InlineData("actorId=00000000-0000-0000-0000-000000000001")]
    [InlineData("state=COMPLETED")]
    public async Task Invalid_or_unsupported_risk_filters_return_400(string query)
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (_, client) = await Reader(company.Id, Roles.CustomerManager); using var reader = client;
        using var response = await client.GetAsync($"/api/residue-risk-events?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_requests_do_not_change_business_journal_evaluation_reservation_or_audit_data()
    {
        using var s = await CreateAsync(factory); var d = await Seed(s);
        var before = await Snapshot();
        foreach (var route in Routes(d).Append("/api/residue-risk-events")) await GetAsync(s.Customer, route);
        using var invalid = await s.Customer.GetAsync($"/api/residue-changes/{d.Change}/targets?assessmentStatus=PASS");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var detailFilter = await s.Customer.GetAsync($"/api/residue-changes/{d.Change}?page=1");
        Assert.Equal(HttpStatusCode.BadRequest, detailFilter.StatusCode);
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task Older_schema_fails_closed_instead_of_returning_foundation_private_rows()
    {
        await using var old = new ApiFactory { MigrationTarget = "20261123020000_ResiduePropagation" }; await old.InitializeAsync();
        var company = await old.SeedOrganizationAsync(OrganizationKind.Customer);
        var (user, session) = await old.SeedUserAsync(Roles.CustomerManager, company.Id);
        using var client = old.CreateRealCookieClient(user, session);
        using var response = await client.GetAsync("/api/residue-risk-events");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Confirmed_evidence_runs_through_worker_and_is_read_via_all_four_routes_without_new_writes()
    {
        await using var isolated = new ApiFactory(); await isolated.InitializeAsync();
        var api = new ResidueRiskApiTests(isolated);
        using var s = await CreateAsync(isolated); var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        await f.PublishRuleAsync(f.RuleBody("NUMERIC_RELATIVE_UNCERTAINTY"));
        await f.ReportAsync(sample, [f.Result(uncertainty: null)]);
        object? id = null;
        for (var i = 0; i < 100; i++)
        {
            using var scope = isolated.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ResiduePropagationRunner>().RunOnceAsync(batchSize: 2);
            id = await api.Sql("""
                SELECT r.id FROM grainledger.ledger_residue_risk_events r
                JOIN grainledger.ledger_residue_change_targets t ON t.id=r.target_entry_id
                WHERE t.target_id=@lot AND r.assessment_status='COMPLETE' ORDER BY r.evaluated_at DESC,r.id LIMIT 1
                """, ("lot", sample.LotId));
            if (id is Guid) break;
            await Task.Delay(10);
        }
        var riskId = Assert.IsType<Guid>(id);
        var before = await api.Snapshot();
        var detail = await GetAsync(s.Customer, $"/api/residue-risk-events/{riskId}");
        Assert.Equal(0.165m, Decimal(detail.GetProperty("assessment").GetProperty("results")[0].GetProperty("upper")));
        var change = detail.GetProperty("changeId").GetGuid();
        await GetAsync(s.Customer, $"/api/residue-changes/{change}");
        var targets = await GetAsync(s.Customer, $"/api/residue-changes/{change}/targets?targetId={sample.LotId}");
        Assert.Equal(riskId, Assert.Single(targets.GetProperty("items").EnumerateArray()).GetProperty("latestRiskEventId").GetGuid());
        Assert.Contains(riskId, Ids(await GetAsync(s.Customer, $"/api/residue-risk-events?targetId={sample.LotId}")));
        Assert.Equal(before, await api.Snapshot());
        await using var runtime = new NpgsqlConnection(isolated.RuntimeConnectionString); await runtime.OpenAsync();
        await using var bypass = new NpgsqlCommand("SELECT grainledger.ledger_residue_public_risk(@id,true)", runtime);
        bypass.Parameters.AddWithValue("id", riskId);
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => bypass.ExecuteScalarAsync())).SqlState);
    }

    private sealed record Data(Guid Change, Guid[] Lots, Guid[] Targets, Guid[] Risks);
    private async Task<Data> Seed(LedgerScenario s)
    {
        var lots = Lots(await s.ReceiptAsync("10", "20", "30"));
        var change = Guid.NewGuid(); var targets = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var risks = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        await Sql("""
            INSERT INTO grainledger.ledger_residue_changes(id,company_id,cause_kind,subject_id,subject_version,actor_id,origin,last_error)
            VALUES(@id,@c,'MATERIAL_CHANGED',gen_random_uuid(),'synthetic',@actor,jsonb_build_object('privateOrigin',@secret,'code',jsonb_build_object('privatePhone',@secret)),@secret)
            """, ("id", change), ("c", s.OwnerId), ("actor", s.Accounts["customer"].User.Id), ("secret", Secret));
        for (var i = 0; i < 3; i++)
        {
            await Sql("""
                INSERT INTO grainledger.ledger_residue_change_targets(id,change_id,company_id,target_kind,target_id,relationship_basis,state,last_error)
                VALUES(@id,@change,@c,'LOT',@lot,'MATERIAL',@state,@secret)
                """, ("id", targets[i]), ("change", change), ("c", s.OwnerId), ("lot", lots[i]),
                ("state", i == 0 ? "COMPLETED" : i == 1 ? "PENDING" : "FAILED_RETRYABLE"), ("secret", Secret));
        }
        for (var i = 0; i < 4; i++)
        {
            var unknown = i == 1;
            var snapshot = JsonSerializer.Serialize(new
            {
                engineVersion = "residue-flow-as-received-3", privateFarmer = Secret, rawLaboratoryPayload = Secret,
                findings = new[] { new { code = "F12_BOUND", status = "COMPLETE", unit = "mg/kg", actual = new { privatePhone = Secret }, privatePhone = Secret, reference = Secret } },
                results = new[] { new { substanceId = Guid.NewGuid(), status = unknown ? "UNRESOLVED" : "COMPLETE",
                    lower = unknown ? null : "0.1", upper = unknown ? null : "0.2", unit = "mg/kg", privateMeasurement = Secret,
                    reasons = unknown ? new[] { "No applicable adopted profile" } : [],
                    rules = new[] { new { id = Guid.NewGuid(), version = 1, kind = "FLOW", componentKey = new { privatePhone = Secret }, evidenceReference = Secret } },
                    provenance = new[] { new { kind = "DIRECT", sourceLotId = lots[i % 3], reportId = Guid.NewGuid(), originalNumericValue = Secret, farmerPhone = Secret } } } }
            });
            await Sql("""
                INSERT INTO grainledger.ledger_residue_risk_events(id,target_entry_id,dependency_revision,assessment_status,snapshot_at,evaluated_at,snapshot)
                VALUES(@id,@target,@revision,@status,@at,@evaluated,CAST(@snapshot AS jsonb))
                """, ("id", risks[i]), ("target", targets[i % 3]), ("revision", "synthetic-" + i),
                ("status", unknown ? "UNRESOLVED" : "COMPLETE"), ("at", At), ("evaluated", i == 3 ? At.AddSeconds(1) : At), ("snapshot", snapshot));
        }
        return new(change, lots, targets, risks);
    }
    private async Task<(Guid Id, HttpClient Client)> Reader(Guid company, string role)
    {
        var (user, session) = await factory.SeedUserAsync(role, company);
        return (user.Id, factory.CreateRealCookieClient(user, session));
    }
    private static IEnumerable<string> Routes(Data data) => [$"/api/residue-changes/{data.Change}", $"/api/residue-changes/{data.Change}/targets", $"/api/residue-risk-events/{data.Risks[0]}"];
    private static Guid[] Ids(JsonElement value) => value.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
    private async Task<string> Snapshot() => (string)(await Sql("""
        SELECT jsonb_build_object(
          'changes',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM grainledger.ledger_residue_changes t),
          'targets',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM grainledger.ledger_residue_change_targets t),
          'risks',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM grainledger.ledger_residue_risk_events t),
          'evaluations',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM grainledger.ledger_residue_evaluations t),
          'frontier',(SELECT jsonb_agg(to_jsonb(t) ORDER BY change_id,lot_id) FROM grainledger.ledger_residue_frontier t),
          'current',(SELECT jsonb_agg(to_jsonb(t) ORDER BY lot_id,panel) FROM grainledger.ledger_residue_current t),
          'positions',(SELECT jsonb_agg(to_jsonb(t) ORDER BY lot_id,location_id) FROM grainledger.ledger_positions t),
          'postings',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM grainledger.ledger_postings t),
          'reservations',(SELECT jsonb_agg(to_jsonb(t) ORDER BY proposal_id,lot_id,location_id) FROM grainledger.ledger_blend_reservations t),
          'reservationEvents',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM grainledger.ledger_blend_reservation_events t),
          'audit',(SELECT jsonb_agg(to_jsonb(t) ORDER BY "Id") FROM grainledger.identity_audit_events t)
        )::text
        """))!;
    private async Task<object?> Sql(string sql, params (string Key, object Value)[] parameters)
    {
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var command = new NpgsqlCommand(sql, owner);
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value);
        return await command.ExecuteScalarAsync();
    }
}
