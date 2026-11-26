using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Api.Features.Exports;
using GrainLedger.Api.Data;
using GrainLedger.Api.Features.Catalogs;
using Microsoft.EntityFrameworkCore;
using GrainLedger.Api.Features.Ledger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;
using static GrainLedger.Api.IntegrationTests.LabReportTests;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ResidueExportPropagationTests
{
    internal static async Task VerifyProposalAsync(ApiFactory factory, LedgerScenario s, ResidueScenario f,
        SampleIdentity sample, JsonElement report, Guid selection, JsonElement proposal, Guid plan,
        HttpClient writer, bool approved)
    {
        var proposalId = proposal.GetProperty("id").GetGuid();
        if (approved)
            await SendAsync(s.Customer, HttpMethod.Post, $"/api/blend-plans/{plan}/approve", new ApproveBlendPlanRequest(Guid.NewGuid(), proposalId, 0));
        var job = (Guid)(await Owner(factory, "SELECT id FROM grainledger.ledger_residue_changes WHERE subject_id=$1 AND cause_kind='REPORT_SELECTION'", selection))!;
        await CompleteJob(factory, job);
        var before = await Risk(factory, job, plan);
        Assert.Equal("PASS", before.GetProperty("assessmentStatus").GetString());
        foreach (var field in proposal.GetProperty("assessment").GetProperty("evidence").EnumerateObject())
            Assert.True(JsonElement.DeepEquals(field.Value, before.GetProperty("evidence").GetProperty(field.Name)), field.Name + " differs. Expected " + field.Value.GetRawText() + "; actual " + before.GetProperty("evidence").GetProperty(field.Name).GetRawText());
        Assert.Equal("CURRENT", before.GetProperty("freshness").GetString());
        Guid? otherPlan = approved ? null : await OtherMarketProposal(factory, s, f, writer, proposal);
        var (_, changedReport) = await f.ReportAsync(sample, [f.Result(value: "4")]);
        var oldResult = report.GetProperty("resultSets")[0].GetProperty("results")[0].GetProperty("id").GetGuid();
        var replacement = await f.SelectAsync(sample, changedReport, supersededResults: [oldResult], supersedesId: selection);
        var changed = (Guid)(await Owner(factory, "SELECT id FROM grainledger.ledger_residue_changes WHERE subject_id=$1 AND cause_kind='REPORT_SELECTION'", replacement))!;
        var business = await f.BusinessStateAsync();
        var authority = await Authority(factory);
        await CompleteJob(factory, changed);
        Assert.Equal(business, await f.BusinessStateAsync());
        Assert.Equal(authority, await Authority(factory));
        var after = await Risk(factory, changed, plan);
        Assert.Equal("FAIL", after.GetProperty("assessmentStatus").GetString());
        Assert.Equal("STALE", after.GetProperty("freshness").GetString());
        if (otherPlan is { } second)
        {
            var other = await Risk(factory, changed, second);
            Assert.Equal("PASS", other.GetProperty("assessmentStatus").GetString());
            Assert.NotEqual(after.GetProperty("marketId").GetGuid(), other.GetProperty("marketId").GetGuid());
            Assert.Equal("9", Assert.Single(other.GetProperty("findings").EnumerateArray(), f => f.GetProperty("code").GetString() == "RESIDUE_LIMIT").GetProperty("limit").GetString());
        }
        Assert.Contains(after.GetProperty("findings").EnumerateArray(), x => x.GetProperty("code").GetString() == "RESIDUE_LIMIT" && x.GetProperty("status").GetString() == "FAIL");
        Assert.Equal("PASS", (await Risk(factory, job, plan)).GetProperty("assessmentStatus").GetString());
        var read = await GetAsync(writer, $"/api/blend-plans/{plan}/proposal");
        Assert.Equal("STALE", read.GetProperty("residueFreshness").GetProperty("freshness").GetString());
        Assert.True(JsonElement.DeepEquals(proposal.GetProperty("assessment"), read.GetProperty("assessment")));
        Assert.Equal(HttpStatusCode.NotFound, (await s.ForeignCustomer.GetAsync($"/api/blend-plans/{plan}/proposal")).StatusCode);
        var risks = await GetAsync(s.Customer, $"/api/residue-risk-events?targetKind=PLAN&targetId={plan}");
        Assert.Equal(2, risks.GetProperty("items").GetArrayLength());
        Assert.DoesNotContain("lease", risks.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Guid> OtherMarketProposal(ApiFactory factory, LedgerScenario s, ResidueScenario f, HttpClient writer, JsonElement original)
    {
        var policy = original.GetProperty("assessment").GetProperty("evidence").GetProperty("assessmentPolicy")
            .Deserialize<PublishedBlendAssessmentPolicy>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var market = Guid.NewGuid(); var version = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Markets.Add(new Market { Id = market, Code = "M" + market.ToString("N"), Name = "Synthetic alternate market" });
            db.MrlVersions.Add(new MrlVersion
            {
                Id = version,
                MarketId = market,
                CommodityId = policy.CommodityId,
                VersionNo = 1,
                Status = "PUBLISHED",
                SourceRef = "SYNTHETIC ONLY",
                PublishedBy = s.Accounts["customer"].User.Id,
                PublishedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
                EffectiveFrom = DateTimeOffset.UtcNow.AddMinutes(-2)
            });
            await db.SaveChangesAsync();
            db.MrlLimits.Add(new MrlLimit { Id = Guid.NewGuid(), MrlVersionId = version, SubstanceId = f.SubstanceId, LimitValue = 10m, Unit = "mg/kg" });
            await db.SaveChangesAsync();
        }
        await SendAsync(s.Customer, HttpMethod.Post, "/api/blend-assessment-policies", new PublishBlendAssessmentPolicyRequest(
            Guid.NewGuid(), market, policy.CommodityId, 1, DateTimeOffset.UtcNow.AddMinutes(-2), null, policy.RequiredSubstanceIds,
            "0.1", "AS_RECEIVED", policy.QualityRules, "SYNTHETIC POLICY", "Synthetic reviewer", "Isolated test", DateTimeOffset.UtcNow.AddMinutes(-3)));
        var order = await SendAsync(writer, HttpMethod.Post, "/api/export-orders", new
        {
            operationId = Guid.NewGuid(),
            marketId = market,
            commodityId = policy.CommodityId,
            requiredMass = "10",
            unit = "KG",
            shippingFrom = "2026-11-10",
            shippingTo = "2026-11-20",
            limits = policy.QualityRules.Select(q => new { q.AttributeId, minValue = (string?)null, maxValue = "14" }).ToArray(),
            containers = new[] { new { targetMass = "10", unit = "KG" } }
        });
        var containers = await GetAsync(writer, $"/api/export-orders/{order.GetProperty("id").GetGuid()}/containers");
        var container = Assert.Single(containers.GetProperty("items").EnumerateArray()); var cid = container.GetProperty("id").GetGuid();
        var draft = await SendAsync(writer, HttpMethod.Post, $"/api/export-containers/{cid}/blend-plans/drafts", new CreateBlendPlanDraftRequest(
            Guid.NewGuid(), order.GetProperty("version").GetInt32(), container.GetProperty("version").GetInt32(), "HEAD_RICE"));
        var plan = draft.GetProperty("id").GetGuid();
        var candidates = await GetAsync(writer, $"/api/export-containers/{cid}/candidates?facilityId={s.FacilityId}&planId={plan}");
        var preview = await SendAsync(writer, HttpMethod.Post, $"/api/blend-plans/{plan}/preview", new PreviewBlendPlanRequest(s.FacilityId, candidates.GetProperty("scopeRevision").GetString()!));
        Assert.Equal("PASS", preview.GetProperty("assessmentStatus").GetString());
        await SendAsync(writer, HttpMethod.Post, $"/api/blend-plans/{plan}/propose", new ProposeBlendPlanRequest(Guid.NewGuid(), preview.GetProperty("previewReference").GetGuid()));
        return plan;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_loaded_and_closed_contexts_reassess_without_rewriting_authority_or_dossier(bool close)
    {
        await using var factory = new ApiFactory(); await factory.InitializeAsync();
        using var f = await ContainerExecutionScenario.CreateAsync(factory);
        var detail = (await f.Detail()).GetProperty("container");
        await f.Post("loads", f.Load(detail.GetProperty("version").GetInt32(), close ? "10" : "4"));
        if (close)
        {
            var current = (await f.Detail()).GetProperty("container");
            var metadata = await f.Post("metadata", new ContainerMetadataRequest(Guid.NewGuid(), current.GetProperty("version").GetInt32(), "SYNTHETIC-F12", "SYNTHETIC-SEAL"));
            await f.Post("close", new ContainerCloseRequest(Guid.NewGuid(), metadata.GetProperty("version").GetInt32()));
        }
        var oldProfile = (Guid)(await Owner(factory, "SELECT id FROM grainledger.catalog_residue_rule_versions WHERE code=$1", f.Residue.ProfileCode!))!;
        var rule = System.Text.Json.Nodes.JsonNode.Parse((string)(await Owner(factory,
            "SELECT payload::text FROM grainledger.catalog_residue_rule_versions WHERE id=$1", oldProfile))!)!.AsObject();
        var profile = Guid.NewGuid();
        rule["Id"] = profile; rule["VersionNo"] = 2; rule["SupersedesId"] = oldProfile;
        foreach (var step in rule["Steps"]!.AsArray().Where(s => s!["Process"]!.GetValue<string>() == "MILLING" && s["OutputStage"]!.GetValue<string>() == "HEAD_RICE"))
        {
            step!["FactorLower"] = "20"; step["FactorUpper"] = "20";
        }
        await SendAsync(f.Stock.Admin, HttpMethod.Post, "/api/residue-rules", rule);
        var job = (Guid)(await Owner(factory, "SELECT id FROM grainledger.ledger_residue_changes WHERE subject_id=$1 AND cause_kind='PROFILE_PUBLISHED'", profile))!;
        var authority = await Authority(factory); var business = await f.Residue.BusinessStateAsync();
        await CompleteJob(factory, job);
        Assert.Equal(authority, await Authority(factory)); Assert.Equal(business, await f.Residue.BusinessStateAsync());
        var risk = await Risk(factory, job, f.ContainerId);
        Assert.Equal("FAIL", risk.GetProperty("assessmentStatus").GetString());
        Assert.Contains(risk.GetProperty("sections").EnumerateArray(), s => s.GetProperty("scope").GetString() == "ACTUAL_LOADING");
        Assert.Equal(close ? 1 : 2, risk.GetProperty("sections").GetArrayLength());
        var visible = await GetAsync(f.Stock.Customer, $"/api/residue-risk-events?targetKind=CONTAINER&targetId={f.ContainerId}");
        Assert.Single(visible.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Export_append_rechecks_basis_fences_expired_leases_is_idempotent_and_hides_private_contexts()
    {
        await using var factory = new ApiFactory(); await factory.InitializeAsync();
        using var f = await ContainerExecutionScenario.CreateAsync(factory);
        var job = (Guid)(await Owner(factory, """
            INSERT INTO grainledger.ledger_residue_changes(company_id,cause_kind,subject_id,subject_version,actor_id,origin,root_lot_ids,substance_ids)
            VALUES($1,'MATERIAL_CHANGED',gen_random_uuid(),'synthetic',$2,'{}',ARRAY[$3],ARRAY[$4]) RETURNING id
            """, f.Stock.OwnerId, f.ActorId, f.LotId, f.Residue.SubstanceId))!;
        var lease = await Claim(factory, job);
        for (var i = 0; i < 100; i++)
        {
            var found = await Rpc(factory, lease, "ledger_residue_job", "DISCOVER");
            if (found.GetProperty("discoveryComplete").GetBoolean()) break;
        }
        await Rpc(factory, lease, "exports_residue_job", "DISCOVER");
        var target = Assert.Single((await Rpc(factory, lease, "exports_residue_job", "NEXT")).EnumerateArray()).GetGuid();
        using var scope = factory.Services.CreateScope(); var worker = scope.ServiceProvider.GetRequiredService<ExportResiduePropagation>();
        var prepared = await worker.PrepareAsync(job, lease.Token, lease.Generation, target);
        await SendAsync(f.Stock.Customer, HttpMethod.Post, "/api/ledger/planning-quality-observations", new
        {
            id = Guid.NewGuid(),
            ownerId = f.Stock.OwnerId,
            facilityId = f.Stock.FacilityId,
            lotId = f.LotId,
            attributeId = f.QualityAttributeId,
            versionNo = 2,
            measuredValue = "13.1",
            unit = "%",
            measuredAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            evidenceId = f.Stock.EvidenceId,
            reportReference = "SYNTHETIC-F12-RACE",
            reviewedBy = "Synthetic reviewer",
            reviewAuthority = "Isolated test",
            reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            subjectConfirmed = true
        });
        Assert.Equal("40001", (await Assert.ThrowsAsync<PostgresException>(() => worker.AppendAsync(job, lease.Token, lease.Generation, target, prepared))).SqlState);
        var fresh = await worker.PrepareAsync(job, lease.Token, lease.Generation, target);
        var saved = await worker.AppendAsync(job, lease.Token, lease.Generation, target, fresh);
        var replay = await worker.AppendAsync(job, lease.Token, lease.Generation, target, fresh);
        Assert.Equal(saved.GetProperty("riskEventId").GetGuid(), replay.GetProperty("riskEventId").GetGuid());
        var reread = await worker.PrepareAsync(job,lease.Token,lease.Generation,target);
        var rereadReplay = await worker.AppendAsync(job,lease.Token,lease.Generation,target,reread);
        Assert.Equal(saved.GetProperty("riskEventId").GetGuid(),rereadReplay.GetProperty("riskEventId").GetGuid());
        Assert.Equal(true,await Owner(factory,"SELECT f.snapshot_at=r.snapshot_at FROM grainledger.exports_residue_freshness f JOIN grainledger.ledger_residue_risk_events r ON r.id=f.risk_event_id WHERE r.id=$1",saved.GetProperty("riskEventId").GetGuid()));
        Assert.Equal("PASS", fresh.Snapshot.GetProperty("assessmentStatus").GetString());
        Assert.Equal("REVIEW_REQUIRED", fresh.Snapshot.GetProperty("freshness").GetString());
        Assert.Equal(1L, await Owner(factory, "SELECT count(*) FROM grainledger.ledger_residue_risk_events WHERE target_entry_id=$1", target));
        var other = (Guid)(await Owner(factory, """
            INSERT INTO grainledger.ledger_residue_changes(company_id,cause_kind,subject_id,subject_version,actor_id,origin)
            VALUES($1,'MATERIAL_CHANGED',gen_random_uuid(),'synthetic',$2,'{}') RETURNING id
            """, f.Stock.OwnerId, f.ActorId))!;
        var foreignLease = await Claim(factory, other);
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => Rpc(factory, foreignLease, "exports_residue_job", "READ", target))).SqlState);
        await using (var runtime = new NpgsqlConnection(factory.RuntimeConnectionString))
        {
            await runtime.OpenAsync();
            foreach (var sql in new[] { "SELECT grainledger.exports_residue_context($1,clock_timestamp())", "SELECT grainledger.ledger_residue_lot_job($1,$1,1,'NEXT',NULL,'{}')", "SELECT * FROM grainledger.exports_residue_freshness WHERE target_id=$1" })
            {
                await using var command = new NpgsqlCommand(sql, runtime); command.Parameters.AddWithValue(target);
                Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync())).SqlState);
            }
        }
        await Owner(factory, "UPDATE grainledger.ledger_residue_changes SET lease_until=clock_timestamp()-interval '1 second' WHERE id=$1", job);
        var next = await Claim(factory, job); Assert.True(next.Generation > lease.Generation);
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => worker.AppendAsync(job, lease.Token, lease.Generation, target, fresh))).SqlState);
    }

    [Fact]
    public async Task Upgrade_backfills_export_contexts_of_completed_lot_jobs_without_rewriting_lot_evaluations()
    {
        await using var factory = new ApiFactory { MigrationTarget = "20261123030000_ResidueRiskReads" }; await factory.InitializeAsync();
        using var f = await ContainerExecutionScenario.CreateAsync(factory);
        var job = (Guid)(await Owner(factory, """
            INSERT INTO grainledger.ledger_residue_changes(company_id,cause_kind,subject_id,subject_version,actor_id,origin,root_lot_ids,substance_ids)
            VALUES($1,'MATERIAL_CHANGED',gen_random_uuid(),'synthetic',$2,'{}',ARRAY[$3],ARRAY[$4]) RETURNING id
            """, f.Stock.OwnerId, f.ActorId, f.LotId, f.Residue.SubstanceId))!;
        var lease = await Claim(factory, job);
        using (var scope = factory.Services.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<ResiduePropagationRunner>();
            for (var i = 0; i < 100; i++)
                if ((await Rpc(factory, lease, "ledger_residue_job", "DISCOVER")).GetProperty("discoveryComplete").GetBoolean()) break;
            foreach (var row in (await Rpc(factory, lease, "ledger_residue_job", "NEXT")).EnumerateArray())
            {
                var target = row.GetGuid(); var prepared = await runner.PrepareAsync(job, lease.Token, lease.Generation, target);
                await runner.AppendAsync(job, lease.Token, lease.Generation, target, prepared);
            }
            Assert.True((await Rpc(factory, lease, "ledger_residue_job", "FINISH")).GetProperty("completed").GetBoolean());
        }
        var original = (string)(await Owner(factory, "SELECT jsonb_agg(to_jsonb(e) ORDER BY id)::text FROM grainledger.ledger_residue_evaluations e"))!;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.OwnerConnectionString,
            x => x.MigrationsHistoryTable("__EFMigrationsHistory", "grainledger")).Options;
        await using (var db = new AppDbContext(options)) await db.Database.MigrateAsync();
        await LedgerDatabase.ConfigureRuntimeRoleAsync(factory.OwnerConnectionString, "grainledger_test_runtime");
        await LedgerDatabase.ValidateRuntimeAsync(factory.RuntimeConnectionString);
        Assert.Equal("PENDING", await Owner(factory, "SELECT state FROM grainledger.ledger_residue_changes WHERE id=$1", job));
        await CompleteJob(factory, job);
        Assert.Equal(original, await Owner(factory, "SELECT jsonb_agg(to_jsonb(e) ORDER BY id)::text FROM grainledger.ledger_residue_evaluations e"));
        Assert.Equal("PASS", (await Risk(factory, job, f.PlanId)).GetProperty("assessmentStatus").GetString());
    }

    internal static async Task VerifyPartialProductionAsync(ApiFactory factory, ContainerExecutionScenario f)
    {
        var job = (Guid)(await Owner(factory, """
            INSERT INTO grainledger.ledger_residue_changes(company_id,cause_kind,subject_id,subject_version,actor_id,origin,root_lot_ids,substance_ids)
            VALUES($1,'MATERIAL_CHANGED',gen_random_uuid(),'synthetic',$2,'{}',ARRAY[$3],ARRAY[$4]) RETURNING id
            """, f.Stock.OwnerId, f.ActorId, f.SourceId, f.Residue.SubstanceId))!;
        var authority = await Authority(factory); var business = await f.Residue.BusinessStateAsync();
        await CompleteJob(factory, job);
        Assert.Equal(authority, await Authority(factory)); Assert.Equal(business, await f.Residue.BusinessStateAsync());
        var risk = await Risk(factory, job, f.ContainerId);
        Assert.Equal("PASS", risk.GetProperty("assessmentStatus").GetString());
        Assert.Equal(new[] { "REMAINING_INPUT", "ACTUAL_LOADING", "ACTUAL_OUTPUT" }, risk.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("scope").GetString()).ToArray());
    }

    private sealed record Lease(Guid Job, Guid Token, int Generation);
    private static async Task<Lease> Claim(ApiFactory factory, Guid job)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            await using var connection = new NpgsqlConnection(factory.RuntimeConnectionString);
            await connection.OpenAsync();
            await using var claim = new NpgsqlCommand("SELECT grainledger.ledger_residue_change_claim($1,100,60)", connection);
            claim.Parameters.AddWithValue(Guid.NewGuid()); await claim.ExecuteScalarAsync();
            var raw = await Owner(factory, "SELECT jsonb_build_object('token',lease_id,'generation',generation)::text FROM grainledger.ledger_residue_changes WHERE id=$1 AND state='PROCESSING' AND lease_until>clock_timestamp()", job);
            if (raw is string json)
            {
                using var doc = JsonDocument.Parse(json);
                return new(job,doc.RootElement.GetProperty("token").GetGuid(),doc.RootElement.GetProperty("generation").GetInt32());
            }
            await Task.Delay(100);
        }
        throw new InvalidOperationException("Export test job did not become claimable within 15 seconds");
    }
    private static async Task<JsonElement> Rpc(ApiFactory factory, Lease lease, string function, string action, Guid? target = null)
    {
        Assert.Contains(function, new[] { "ledger_residue_job", "exports_residue_job" });
        await using var connection = new NpgsqlConnection(factory.RuntimeConnectionString); await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT grainledger.{function}($1,$2,$3,$4,$5,$6)::text", connection);
        command.Parameters.AddWithValue(lease.Job); command.Parameters.AddWithValue(lease.Token); command.Parameters.AddWithValue(lease.Generation);
        command.Parameters.AddWithValue(action); command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)target ?? DBNull.Value });
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, "{\"batchSize\":2}");
        using var doc = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!); return doc.RootElement.Clone();
    }

    internal static async Task CompleteJob(ApiFactory factory, Guid job)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if ((string?)(await Owner(factory, "SELECT state FROM grainledger.ledger_residue_changes WHERE id=$1", job)) == "COMPLETED") return;
            var token = Guid.NewGuid();
            await using var connection = new NpgsqlConnection(factory.RuntimeConnectionString); await connection.OpenAsync();
            await using var claim = new NpgsqlCommand("SELECT grainledger.ledger_residue_change_claim($1,100,60)::text", connection);
            claim.Parameters.AddWithValue(token); await claim.ExecuteScalarAsync();
            var lease = (string)(await Owner(factory, "SELECT jsonb_build_object('token',lease_id,'generation',generation)::text FROM grainledger.ledger_residue_changes WHERE id=$1", job))!;
            using var document = JsonDocument.Parse(lease);
            if (document.RootElement.GetProperty("token").ValueKind == JsonValueKind.Null) { await Task.Delay(100); continue; }
            using var scope = factory.Services.CreateScope();
            var logger = new FailureLogger();
            await ActivatorUtilities.CreateInstance<ResiduePropagationRunner>(scope.ServiceProvider, logger).ProcessAsync(job,
                document.RootElement.GetProperty("token").GetGuid(), document.RootElement.GetProperty("generation").GetInt32(), 2, default);
            var error = await Owner(factory, "SELECT last_error FROM grainledger.ledger_residue_changes WHERE id=$1", job);
            if (logger.Failure is PostgresException { SqlState: "40001" }) { await Task.Delay(100); continue; }
            Assert.True(error is null or DBNull, $"Worker failed at attempt {attempt}: {error}: {logger.Failure}");
        }
        Assert.Fail("Export discovery did not finish");
    }
    private static async Task<JsonElement> Risk(ApiFactory factory, Guid job, Guid target)
    {
        var text = (string)(await Owner(factory, "SELECT r.snapshot::text FROM grainledger.ledger_residue_risk_events r JOIN grainledger.ledger_residue_change_targets t ON t.id=r.target_entry_id WHERE t.change_id=$1 AND t.target_id=$2", job, target))!;
        using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone();
    }
    private static async Task<string> Authority(ApiFactory factory) => (string)(await Owner(factory, """
        SELECT jsonb_build_object('decisions',(SELECT jsonb_agg(to_jsonb(x) ORDER BY proposal_id,version) FROM grainledger.exports_blend_plan_decisions x),
          'active',(SELECT jsonb_agg(to_jsonb(x) ORDER BY proposal_id) FROM grainledger.exports_active_blend_approvals x),
          'reservations',(SELECT jsonb_agg(to_jsonb(x) ORDER BY proposal_id,lot_id,location_id) FROM grainledger.ledger_blend_reservations x),
          'reservationEvents',(SELECT jsonb_agg(to_jsonb(x) ORDER BY id) FROM grainledger.ledger_blend_reservation_events x),
          'closures',(SELECT jsonb_agg(to_jsonb(x) ORDER BY container_id) FROM grainledger.ledger_container_closures x))::text
        """))!;
    private static async Task<object?> Owner(ApiFactory factory, string sql, params object[] args)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString); await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, connection); foreach (var arg in args) cmd.Parameters.AddWithValue(arg);
        return await cmd.ExecuteScalarAsync();
    }

    private sealed class FailureLogger : ILogger<ResiduePropagationRunner>
    {
        public Exception? Failure { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (exception is not null) Failure = exception; }
    }
}
