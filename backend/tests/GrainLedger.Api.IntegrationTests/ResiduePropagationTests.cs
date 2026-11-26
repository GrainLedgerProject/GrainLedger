using System.Net;
using System.Text.Json;
using GrainLedger.Api.Data;
using GrainLedger.Api.Features.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LabReportTests;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;
using static GrainLedger.Api.IntegrationTests.ResidueScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ResiduePropagationTests
{
    [Fact]
    public async Task Late_confirmation_recomputes_exhausted_descendants_without_changing_history_or_stock()
    {
        await using var factory = new ApiFactory(); await factory.InitializeAsync();
        using var s = await CreateAsync(factory); var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("100"));
        var rule = f.RuleBody("NUMERIC_RELATIVE_UNCERTAINTY");
        AddStep(rule, "DRYING", "RAW_PADDY", "DRIED_PADDY");
        AddStep(rule, "MILLING", "DRIED_PADDY", "HEAD_RICE");
        await f.PublishRuleAsync(rule);
        var dried = Output(await s.CommitAsync(await s.DraftAsync("DRYING", [(sample.LotId, "100")], [("DRIED_PADDY", "100")])));
        var head = Output(await s.CommitAsync(await s.DraftAsync("MILLING", [(dried, "100")], [("HEAD_RICE", "100")])));
        var original = await f.EvaluateAsync(head);
        Unresolved(ResultOf(original, f.SubstanceId));
        await f.ReportAsync(sample, [f.Result(uncertainty: null)]);
        var job = (Guid)(await Owner(factory, "SELECT id FROM grainledger.ledger_residue_changes WHERE company_id=@c AND cause_kind='REPORT_CONFIRMED'", ("c", s.OwnerId)))!;
        var business = await f.BusinessStateAsync();
        await CompleteJob(factory, job, 1);
        var result = await SystemEvaluation(factory, job, head);
        Complete(ResultOf(result, f.SubstanceId), 0.135m, 0.165m);
        Assert.Equal("SYSTEM", result.GetProperty("createdByKind").GetString());
        Assert.Equal(job, result.GetProperty("changeId").GetGuid());
        Assert.Equal(original.GetRawText(), (await GetAsync(s.Customer, $"/api/ledger/lots/{head}/residue-evaluations/{original.GetProperty("id").GetGuid()}")).GetRawText());
        Assert.Equal(business, await f.BusinessStateAsync());
        Assert.Equal(0m, Quantity(await s.LotAsync(sample.LotId)));
        Assert.Equal(0m, Quantity(await s.LotAsync(dried)));
        await RejectedAsync(s.Customer, HttpMethod.Post, $"/api/ledger/lots/{head}/residue-evaluations",
            new { id = result.GetProperty("id").GetGuid(), substanceIds = new[] { f.SubstanceId } }, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Replacing_group_recomputes_removed_members_and_does_not_expand_into_foreign_company()
    {
        await using var factory = new ApiFactory(); await factory.InitializeAsync();
        using var s = await CreateAsync(factory); var f = await ResidueScenario.CreateAsync(s);
        var a = await s.ReceiptAsync("10"); var b = await s.ReceiptAsync("20");
        var foreign = Lots(await s.ForeignReceiptAsync("10")).Single();
        await f.PublishRuleAsync();
        var group = f.GroupBody([a.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid(), b.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid()], [], "0.1", "0.2");
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-source-groups", group);
        var replacement = f.GroupBody([b.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid()], [], "0.2", "0.3");
        replacement["code"] = group["code"]!.DeepClone(); replacement["versionNo"] = 2;
        replacement["supersedesId"] = group["id"]!.DeepClone();
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-source-groups", replacement);
        var job = (Guid)(await Owner(factory, "SELECT id FROM grainledger.ledger_residue_changes WHERE subject_id=@id AND phase='PUBLICATION'", ("id", replacement["id"]!.GetValue<Guid>())))!;
        await CompleteJob(factory, job, 1);
        Unresolved(ResultOf(await SystemEvaluation(factory, job, Lots(a).Single()), f.SubstanceId));
        Complete(ResultOf(await SystemEvaluation(factory, job, Lots(b).Single()), f.SubstanceId), 0.2m, 0.3m);
        Assert.Equal(0L, await Owner(factory, "SELECT count(*) FROM grainledger.ledger_residue_change_targets WHERE change_id=@id AND target_id=@lot", ("id", job), ("lot", foreign)));
    }

    [Fact]
    public async Task Panels_larger_than_200_are_partitioned_without_dropping_substances_or_marking_unknown_complete()
    {
        await using var factory = new ApiFactory(); await factory.InitializeAsync();
        using var s = await CreateAsync(factory);
        var lot = Lots(await s.ReceiptAsync("10")).Single();
        var substances = Enumerable.Range(0, 205).Select(_ => Guid.NewGuid()).Order().ToArray();
        var job = await SyntheticJob(factory, s, lot, substances);
        await CompleteJob(factory, job, 2);
        Assert.Equal(2L, await Owner(factory, "SELECT count(*) FROM grainledger.ledger_residue_change_targets WHERE change_id=@id", ("id", job)));
        Assert.Equal(205L, await Owner(factory, "SELECT count(DISTINCT x) FROM grainledger.ledger_residue_change_targets t CROSS JOIN LATERAL unnest(t.substance_ids) x WHERE t.change_id=@id", ("id", job)));
        Assert.Equal(205L, await Owner(factory, "SELECT count(*) FROM grainledger.ledger_residue_evaluations e CROSS JOIN LATERAL jsonb_array_elements(e.snapshot->'results') r WHERE e.change_target_id IN (SELECT id FROM grainledger.ledger_residue_change_targets WHERE change_id=@id) AND r->>'status'='UNRESOLVED'", ("id", job)));
        Assert.Equal(0L, await Owner(factory, "SELECT count(*) FROM grainledger.ledger_residue_risk_events r JOIN grainledger.ledger_residue_change_targets t ON t.id=r.target_entry_id WHERE t.change_id=@id AND r.assessment_status<>'UNRESOLVED'", ("id", job)));
    }

    [Fact]
    public async Task Append_is_atomic_and_idempotent_and_expired_worker_cannot_overwrite_replacement_claim()
    {
        await using var factory = new ApiFactory(); await factory.InitializeAsync();
        using var s = await CreateAsync(factory); var f = await ResidueScenario.CreateAsync(s);
        var lot = Lots(await s.ReceiptAsync("10")).Single();
        var job = await SyntheticJob(factory, s, lot, [f.SubstanceId]);
        var first = await Claim(factory, job);
        await Rpc(factory, first, "DISCOVER", payload: new { batchSize = 10 });
        var target = (await Rpc(factory, first, "NEXT")).EnumerateArray().Single().GetGuid();
        using var scope = factory.Services.CreateScope(); var runner = scope.ServiceProvider.GetRequiredService<ResiduePropagationRunner>();
        var old = await runner.PrepareAsync(job, first.Token, first.Generation, target);
        await Owner(factory, "UPDATE grainledger.ledger_residue_changes SET lease_until=clock_timestamp()-interval '1 second' WHERE id=@id RETURNING id", ("id", job));
        var next = await Claim(factory, job);
        Assert.Equal(first.Generation + 1, next.Generation);
        var current = await runner.PrepareAsync(job, next.Token, next.Generation, target);
        var evaluation = await runner.AppendAsync(job, next.Token, next.Generation, target, current);
        Assert.Equal(evaluation, await runner.AppendAsync(job, next.Token, next.Generation, target, current));
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => runner.AppendAsync(job, first.Token, first.Generation, target, old))).SqlState);
        Assert.Equal(1L, await Owner(factory, "SELECT count(*) FROM grainledger.ledger_residue_evaluations WHERE change_target_id=@id", ("id", target)));
        Assert.Equal(1L, await Owner(factory, "SELECT count(*) FROM grainledger.ledger_residue_risk_events WHERE target_entry_id=@id", ("id", target)));
        // Simulate a crash after durable append but before FINISH; a new runner resumes.
        await Owner(factory, "UPDATE grainledger.ledger_residue_changes SET lease_until=clock_timestamp()-interval '1 second' WHERE id=@id RETURNING id", ("id", job));
        await CompleteJob(factory, job, 1);
        Assert.Equal(evaluation, (await SystemEvaluation(factory, job, lot)).GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_basis_or_crossed_temporal_boundary_rejects_stale_snapshot(bool temporal)
    {
        await using var factory = new ApiFactory(); await factory.InitializeAsync();
        using var s = await CreateAsync(factory); var f = await ResidueScenario.CreateAsync(s);
        var lot = Lots(await s.ReceiptAsync("10")).Single();
        var job = await SyntheticJob(factory, s, lot, [f.SubstanceId], temporal);
        var claim = await Claim(factory, job);
        await Rpc(factory, claim, "DISCOVER", payload: new { batchSize = 10 });
        var target = (await Rpc(factory, claim, "NEXT")).EnumerateArray().Single().GetGuid();
        using var scope = factory.Services.CreateScope(); var runner = scope.ServiceProvider.GetRequiredService<ResiduePropagationRunner>();
        var prepared = await runner.PrepareAsync(job, claim.Token, claim.Generation, target);
        if (temporal) await Task.Delay(3300);
        else await f.PublishRuleAsync();
        Assert.Equal("40001", (await Assert.ThrowsAsync<PostgresException>(() => runner.AppendAsync(job, claim.Token, claim.Generation, target, prepared))).SqlState);
        Assert.Equal(0L, await Owner(factory, "SELECT count(*) FROM grainledger.ledger_residue_evaluations WHERE change_target_id=@id", ("id", target)));
        var fresh = await runner.PrepareAsync(job, claim.Token, claim.Generation, target);
        await runner.AppendAsync(job, claim.Token, claim.Generation, target, fresh);
    }

    [Fact]
    public async Task Worker_scope_cannot_read_another_jobs_target_and_private_graph_is_not_callable()
    {
        await using var factory = new ApiFactory(); await factory.InitializeAsync();
        using var s = await CreateAsync(factory); var f = await ResidueScenario.CreateAsync(s);
        var lot = Lots(await s.ReceiptAsync("10")).Single();
        var job = await SyntheticJob(factory, s, lot, [f.SubstanceId]);
        var otherJob = await SyntheticJob(factory, s, lot, [f.SubstanceId]);
        var claim = await Claim(factory, job); var other = await Claim(factory, otherJob);
        await Rpc(factory, other, "DISCOVER");
        var target = (await Rpc(factory, other, "NEXT")).EnumerateArray().Single().GetGuid();
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => Rpc(factory, claim, "READ", target))).SqlState);
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT grainledger.ledger_residue_graph_core(@lot)", runtime);
        command.Parameters.AddWithValue("lot", lot);
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync())).SqlState);
    }

    [Fact]
    public async Task Upgrade_seeds_backlog_and_keeps_existing_manual_evaluation_unchanged()
    {
        await using var factory = new ApiFactory { MigrationTarget = "20261123000000_ResidueChangeFoundation" };
        await factory.InitializeAsync();
        Assert.Equal(true, await Owner(factory, "SELECT to_regprocedure('grainledger.ledger_location_stock(uuid,uuid,jsonb)') IS NULL"));
        // The current capture endpoint needs this later column to create historical test data.
        // Remove it before upgrading so the real variety migration still runs against its original schema.
        await Owner(factory, "ALTER TABLE grainledger.capture_plot_seasons ADD COLUMN rice_variety_id uuid");
        using var s = await CreateAsync(factory); var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        var (_, report) = await f.ReportAsync(sample); await f.PublishRuleAsync(); await f.SelectAsync(sample, report);
        var manual = await f.EvaluateAsync(sample.LotId);
        await Owner(factory, "ALTER TABLE grainledger.capture_plot_seasons DROP COLUMN rice_variety_id");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.OwnerConnectionString,
            p => p.MigrationsHistoryTable("__EFMigrationsHistory", "grainledger")).Options;
        await using (var db = new AppDbContext(options)) await db.Database.MigrateAsync();
        await LedgerDatabase.ConfigureRuntimeRoleAsync(factory.OwnerConnectionString, "grainledger_test_runtime");
        await LedgerDatabase.ValidateRuntimeAsync(factory.RuntimeConnectionString);
        Assert.Equal(true, await Owner(factory, "SELECT has_function_privilege('grainledger_test_runtime','grainledger.ledger_location_stock(uuid,uuid,jsonb)','EXECUTE')"));
        Assert.Equal(false, await Owner(factory, "SELECT EXISTS (SELECT 1 FROM pg_proc p, aclexplode(coalesce(p.proacl, acldefault('f', p.proowner))) acl WHERE p.oid=to_regprocedure('grainledger.ledger_location_stock(uuid,uuid,jsonb)') AND acl.grantee=0 AND acl.privilege_type='EXECUTE')"));
        await Owner(factory, "REVOKE EXECUTE ON FUNCTION grainledger.ledger_location_stock(uuid,uuid,jsonb) FROM grainledger_test_runtime");
        await Assert.ThrowsAsync<InvalidOperationException>(() => LedgerDatabase.ValidateRuntimeAsync(factory.RuntimeConnectionString));
        await LedgerDatabase.ConfigureRuntimeRoleAsync(factory.OwnerConnectionString, "grainledger_test_runtime");
        await LedgerDatabase.ValidateRuntimeAsync(factory.RuntimeConnectionString);
        var job = (Guid)(await Owner(factory, "SELECT id FROM grainledger.ledger_residue_changes WHERE company_id=@c AND origin->>'cutover'='true'", ("c", s.OwnerId)))!;
        await CompleteJob(factory, job, 2);
        Complete(ResultOf(await SystemEvaluation(factory, job, sample.LotId), f.SubstanceId), 0.1m, 0.2m);
        Assert.Equal(manual.GetRawText(), (await GetAsync(s.Customer, $"/api/ledger/lots/{sample.LotId}/residue-evaluations/{manual.GetProperty("id").GetGuid()}")).GetRawText());
    }

    [Fact]
    public async Task Zero_accounting_share_still_propagates_through_physical_edges()
    {
        await using var factory = new ApiFactory(); await factory.InitializeAsync();
        using var s = await CreateAsync(factory); var f = await ResidueScenario.CreateAsync(s);
        var receipts = new[] { await s.ReceiptAsync("0.000001"), await s.ReceiptAsync("0.000001"), await s.ReceiptAsync("0.000001") };
        var sources = receipts.Select(r => (Lot: Lots(r).Single(), Source: r.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid()))
            .OrderBy(x => x.Lot.ToString("D"), StringComparer.Ordinal).ToArray();
        var profile = f.RuleBody(); AddStep(profile, "DRYING", "RAW_PADDY", "DRIED_PADDY"); await f.PublishRuleAsync(profile);
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-source-groups", f.GroupBody(sources.Take(2).Select(x => x.Source).ToArray(), [], "0", "0"));
        var dried = Output(await s.CommitAsync(await s.DraftAsync("DRYING", sources.Select(x => (x.Lot, "0.000001")).ToArray(), [("DRIED_PADDY", "0.000001")], "0.000002")));
        Assert.Equal(0m, await Owner(factory, "SELECT quantity_kg FROM grainledger.ledger_contributions WHERE lot_id=@lot AND source_lot_id=@source", ("lot", dried), ("source", sources[2].Lot)));
        var group = f.GroupBody([sources[2].Source], [], "1", "1");
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-source-groups", group);
        var job = (Guid)(await Owner(factory, "SELECT id FROM grainledger.ledger_residue_changes WHERE subject_id=@id", ("id", group["id"]!.GetValue<Guid>())))!;
        await CompleteJob(factory, job, 1);
        Complete(ResultOf(await SystemEvaluation(factory, job, dried), f.SubstanceId), 0.333333333333m, 0.333333333334m);
    }

    [Fact]
    public async Task Replacement_claim_while_old_append_waits_for_global_lock_is_fenced_after_wait()
    {
        await using var factory = new ApiFactory(); await factory.InitializeAsync();
        using var s = await CreateAsync(factory); var f = await ResidueScenario.CreateAsync(s);
        var lot = Lots(await s.ReceiptAsync("10")).Single(); var job = await SyntheticJob(factory, s, lot, [f.SubstanceId]);
        var old = await Claim(factory, job); await Rpc(factory, old, "DISCOVER");
        var target = (await Rpc(factory, old, "NEXT")).EnumerateArray().Single().GetGuid();
        using var scope = factory.Services.CreateScope(); var runner = scope.ServiceProvider.GetRequiredService<ResiduePropagationRunner>();
        var prepared = await runner.PrepareAsync(job, old.Token, old.Generation, target);
        await using var blocker = new NpgsqlConnection(factory.OwnerConnectionString); await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(714928352)", blocker, transaction)) await gate.ExecuteNonQueryAsync();
        var waiting = runner.AppendAsync(job, old.Token, old.Generation, target, prepared);
        try
        {
            var blocked = false;
            for (var i = 0; i < 200; i++)
            {
                blocked = (long)(await Owner(factory, "SELECT count(*) FROM pg_stat_activity WHERE wait_event='advisory' AND query LIKE '%ledger_residue_job%'"))! > 0;
                if (blocked) break;
                await Task.Delay(10);
            }
            Assert.True(blocked, "Old worker must be waiting on the Ledger lock before lease replacement");
            await Owner(factory, "UPDATE grainledger.ledger_residue_changes SET lease_until=clock_timestamp()-interval '1 second' WHERE id=@id RETURNING id", ("id", job));
            var replacement = await Claim(factory, job);
            Assert.Equal(old.Generation + 1, replacement.Generation);
        }
        finally { await transaction.RollbackAsync(); }
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(async () => await waiting)).SqlState);
        Assert.Equal(0L, await Owner(factory, "SELECT count(*) FROM grainledger.ledger_residue_evaluations WHERE change_target_id=@id", ("id", target)));
    }

    [Fact]
    public async Task Concurrent_ticks_retry_failed_append_and_leave_future_jobs_unclaimed()
    {
        await using var factory = new ApiFactory(); await factory.InitializeAsync();
        using var s = await CreateAsync(factory); var f = await ResidueScenario.CreateAsync(s);
        var lot = Lots(await s.ReceiptAsync("10")).Single(); var job = await SyntheticJob(factory, s, lot, [f.SubstanceId]);
        await Owner(factory, """
            CREATE FUNCTION grainledger.residue_test_fail_system_append() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.actor_id IS NULL THEN RAISE EXCEPTION 'Synthetic transient append failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER residue_test_fail_system_append BEFORE INSERT ON grainledger.ledger_residue_evaluations
             FOR EACH ROW EXECUTE FUNCTION grainledger.residue_test_fail_system_append();
            """);
        try
        {
            for (var i = 0; i < 20; i++)
            {
                using var scope = factory.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ResiduePropagationRunner>().RunOnceAsync(batchSize: 2);
                if ((string?)(await Owner(factory, "SELECT state FROM grainledger.ledger_residue_changes WHERE id=@id", ("id", job))) == "FAILED_RETRYABLE") break;
            }
            Assert.Equal("FAILED_RETRYABLE", await Owner(factory, "SELECT state FROM grainledger.ledger_residue_changes WHERE id=@id", ("id", job)));
            Assert.Equal(0L, await Owner(factory, "SELECT count(*) FROM grainledger.ledger_residue_evaluations e JOIN grainledger.ledger_residue_change_targets t ON t.id=e.change_target_id WHERE t.change_id=@id", ("id", job)));
        }
        finally
        {
            await Owner(factory, "DROP TRIGGER residue_test_fail_system_append ON grainledger.ledger_residue_evaluations; DROP FUNCTION grainledger.residue_test_fail_system_append();");
        }
        await Owner(factory, "UPDATE grainledger.ledger_residue_changes SET due_at=clock_timestamp() WHERE id=@id RETURNING id", ("id", job));
        for (var i = 0; i < 30; i++)
        {
            using var a = factory.Services.CreateScope(); using var b = factory.Services.CreateScope();
            await Task.WhenAll(a.ServiceProvider.GetRequiredService<ResiduePropagationRunner>().RunOnceAsync(batchSize: 2), b.ServiceProvider.GetRequiredService<ResiduePropagationRunner>().RunOnceAsync(batchSize: 2));
            if ((string?)(await Owner(factory, "SELECT state FROM grainledger.ledger_residue_changes WHERE id=@id", ("id", job))) == "COMPLETED") break;
        }
        Assert.Equal("COMPLETED", await Owner(factory, "SELECT state FROM grainledger.ledger_residue_changes WHERE id=@id", ("id", job)));
        Assert.Equal(1L, await Owner(factory, "SELECT count(*) FROM grainledger.ledger_residue_evaluations e JOIN grainledger.ledger_residue_change_targets t ON t.id=e.change_target_id WHERE t.change_id=@id", ("id", job)));
        var future = await SyntheticJob(factory, s, lot, [f.SubstanceId]);
        await Owner(factory, "UPDATE grainledger.ledger_residue_changes SET due_at=clock_timestamp()+interval '1 day' WHERE id=@id RETURNING id", ("id", future));
        using var finalScope = factory.Services.CreateScope();
        Assert.Equal(0, await finalScope.ServiceProvider.GetRequiredService<ResiduePropagationRunner>().RunOnceAsync());
        Assert.Equal("PENDING", await Owner(factory, "SELECT state FROM grainledger.ledger_residue_changes WHERE id=@id", ("id", future)));
    }

    [Fact]
    public async Task Opted_in_hosted_worker_processes_late_evidence_without_manual_runner_calls()
    {
        await using var factory = new ApiFactory { EnableResidueWorker = true }; await factory.InitializeAsync();
        Assert.Single(factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<ResiduePropagationWorker>());
        using var s = await CreateAsync(factory); var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("10"));
        await f.PublishRuleAsync(f.RuleBody("NUMERIC_RELATIVE_UNCERTAINTY"));
        await f.ReportAsync(sample, [f.Result(uncertainty: null)]);
        var observed = false;
        for (var i = 0; i < 100; i++)
        {
            observed = (long)(await Owner(factory, """
                SELECT count(*) FROM grainledger.ledger_residue_evaluations e
                CROSS JOIN LATERAL jsonb_array_elements(e.snapshot->'results') r
                WHERE e.actor_id IS NULL AND e.lot_id=@lot AND r->>'status'='COMPLETE'
                 AND (r->>'substanceId')::uuid=@substance
                """, ("lot", sample.LotId), ("substance", f.SubstanceId)))! > 0;
            if (observed) break;
            await Task.Delay(200);
        }
        Assert.True(observed, "Hosted worker should persist a finite bound after confirmed evidence");
    }

    [Fact]
    public async Task Global_profile_discovery_visits_adopting_company_and_skips_unadopted_company()
    {
        await using var factory = new ApiFactory(); await factory.InitializeAsync();
        using var s = await CreateAsync(factory); var f = await ResidueScenario.CreateAsync(s);
        var own = Lots(await s.ReceiptAsync("10")).Single(); var foreign = Lots(await s.ForeignReceiptAsync("10")).Single();
        var profile = await f.PublishRuleAsync();
        var job = (Guid)(await Owner(factory, "SELECT id FROM grainledger.ledger_residue_changes WHERE subject_id=@id AND cause_kind='PROFILE_PUBLISHED'", ("id", profile)))!;
        await CompleteJob(factory, job, 1);
        Assert.Equal(1L, await Owner(factory, "SELECT count(*) FROM grainledger.ledger_residue_change_targets WHERE change_id=@id AND target_id=@lot", ("id", job), ("lot", own)));
        Assert.Equal(0L, await Owner(factory, "SELECT count(*) FROM grainledger.ledger_residue_change_targets WHERE change_id=@id AND target_id=@lot", ("id", job), ("lot", foreign)));
    }

    private sealed record Lease(Guid Job, Guid Token, int Generation);
    private static async Task<Lease> Claim(ApiFactory factory, Guid job)
    {
        // Publication timestamps can be slightly ahead of the DB clock. A retry
        // also has a durable backoff; neither case is a valid live lease yet.
        for (var attempt = 0; attempt < 150; attempt++)
        {
            var token = Guid.NewGuid();
            await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString);
            await runtime.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT grainledger.ledger_residue_change_claim(@token,100,60)::text", runtime);
            command.Parameters.AddWithValue("token", token);
            using var doc = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
            var match = doc.RootElement.EnumerateArray().SingleOrDefault(x => x.GetProperty("id").GetGuid() == job);
            if (match.ValueKind != JsonValueKind.Undefined)
                return new(job, token, match.GetProperty("generation").GetInt32());
            var data = await Owner(factory, "SELECT jsonb_build_object('token',lease_id,'generation',generation)::text FROM grainledger.ledger_residue_changes WHERE id=@id AND state='PROCESSING' AND lease_until>clock_timestamp()", ("id", job));
            if (data is string json)
            {
                using var existing = JsonDocument.Parse(json);
                return new(job, existing.RootElement.GetProperty("token").GetGuid(), existing.RootElement.GetProperty("generation").GetInt32());
            }
            await Task.Delay(100);
        }
        var state = await Owner(factory, "SELECT jsonb_build_object('state',state,'dueAt',due_at,'error',last_error)::text FROM grainledger.ledger_residue_changes WHERE id=@id", ("id", job));
        throw new InvalidOperationException($"Job did not become claimable within 15 seconds: {state}");
    }
    private static async Task CompleteJob(ApiFactory factory, Guid job, int batch)
    {
        for (var i = 0; i < 500; i++)
        {
            if ((string?)(await Owner(factory, "SELECT state FROM grainledger.ledger_residue_changes WHERE id=@id", ("id", job))) == "COMPLETED") return;
            var claim = await Claim(factory, job);
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ResiduePropagationRunner>().ProcessAsync(job, claim.Token, claim.Generation, batch, default);
        }
        Assert.Fail("Job did not exhaust its bounded discovery frontier");
    }
    private static async Task<JsonElement> SystemEvaluation(ApiFactory factory, Guid job, Guid lot)
    {
        var text = (string)(await Owner(factory, "SELECT e.snapshot::text FROM grainledger.ledger_residue_evaluations e JOIN grainledger.ledger_residue_change_targets t ON t.id=e.change_target_id WHERE t.change_id=@job AND t.target_id=@lot", ("job", job), ("lot", lot)))!;
        using var document = JsonDocument.Parse(text); return document.RootElement.Clone();
    }
    private static async Task<Guid> SyntheticJob(ApiFactory factory, LedgerScenario s, Guid lot, Guid[] substances, bool boundary = false)
    {
        return (Guid)(await Owner(factory, """
            INSERT INTO grainledger.ledger_residue_changes(company_id,cause_kind,subject_id,subject_version,actor_id,origin,root_lot_ids,substance_ids)
            VALUES(@c,'MATERIAL_CHANGED',gen_random_uuid(),'synthetic',@a,
              CASE WHEN @boundary THEN jsonb_build_object('effectiveTo',clock_timestamp()+interval '3 seconds') ELSE '{}'::jsonb END,
              ARRAY[@lot],@substances) RETURNING id
            """, ("c", s.OwnerId), ("a", s.Accounts["customer"].User.Id), ("lot", lot), ("substances", substances), ("boundary", boundary)))!;
    }
    private static async Task<JsonElement> Rpc(ApiFactory factory, Lease lease, string action, Guid? target = null, object? payload = null)
    {
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT grainledger.ledger_residue_job(@job,@token,@generation,@action,@target,@p)::text", runtime);
        command.Parameters.AddWithValue("job", lease.Job); command.Parameters.AddWithValue("token", lease.Token);
        command.Parameters.AddWithValue("generation", lease.Generation); command.Parameters.AddWithValue("action", action);
        command.Parameters.Add(new NpgsqlParameter("target", NpgsqlDbType.Uuid) { Value = (object?)target ?? DBNull.Value });
        command.Parameters.AddWithValue("p", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(payload ?? new { }));
        using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!); return document.RootElement.Clone();
    }
    private static async Task<object?> Owner(ApiFactory factory, string sql, params (string Key, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value);
        return await command.ExecuteScalarAsync();
    }
}
