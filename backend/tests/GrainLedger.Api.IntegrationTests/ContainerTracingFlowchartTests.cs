using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using GrainLedger.Api.Features.Exports;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

// These tests use ApiFactory's isolated Testcontainer only. Never point them at the shared database.
public sealed class ContainerTracingFlowchartTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Container_graph_starts_at_actual_manifest_and_keeps_partial_load_sources_separate_from_historical_mass()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory, preBlended: true);
        var empty = await SendAsync(f.Stock.Customer, HttpMethod.Post, f.Route + "/trace", new { });
        Assert.Empty(empty.GetProperty("graph").GetProperty("lots").EnumerateArray());
        Assert.Empty(empty.GetProperty("graph").GetProperty("loadingEdges").EnumerateArray());
        var planned = await SendAsync(f.Stock.Customer, HttpMethod.Post, "/api/ledger/trace", new { lotId = f.SourceId, direction = "forward" });
        Assert.Empty(planned.GetProperty("actualContainers").EnumerateArray());
        var plan = Assert.Single(planned.GetProperty("affectedPlans").EnumerateArray());
        Assert.Equal(f.ProposalId, plan.GetProperty("proposalId").GetGuid());
        Assert.True(plan.GetProperty("activeApproval").GetBoolean());
        await f.Post("loads", f.Load(1, "4.000001"));
        var partial = await SendAsync(f.Stock.Customer, HttpMethod.Post, f.Route + "/trace", new { });
        Assert.Equal("4.000001", partial.GetProperty("loadedKg").GetString());
        Assert.Equal(4.000001m, partial.GetProperty("sourceContributions").EnumerateArray().Sum(x => decimal.Parse(x.GetProperty("quantityKg").GetString()!, CultureInfo.InvariantCulture)));
        await f.Post("loads", f.Load(2, "5.999999"));
        var trace = await SendAsync(f.Stock.Customer, HttpMethod.Post, f.Route + "/trace", new { });
        var graph = trace.GetProperty("graph");
        Assert.Equal("container", graph.GetProperty("root").GetProperty("type").GetString());
        Assert.Equal(f.ContainerId, graph.GetProperty("root").GetProperty("id").GetGuid());
        Assert.False(graph.TryGetProperty("targetLotId", out _));
        Assert.Single(graph.GetProperty("actualContainers").EnumerateArray());
        var edge = Assert.Single(graph.GetProperty("loadingEdges").EnumerateArray());
        Assert.Equal(f.LotId, edge.GetProperty("lotId").GetGuid());
        Assert.Equal("10.000000", edge.GetProperty("quantityKg").GetString());
        Assert.Equal(2, edge.GetProperty("lines").GetArrayLength());
        Assert.All(trace.GetProperty("sourceContributions").EnumerateArray(), x => Assert.Equal("5.000000", x.GetProperty("quantityKg").GetString()));
        var plot = Assert.Single(trace.GetProperty("plotContributions").EnumerateArray());
        Assert.Equal(2, plot.GetProperty("sourceCount").GetInt32());
        Assert.Equal("10.000000", plot.GetProperty("quantityKg").GetString());
        Assert.Equal("100", plot.GetProperty("percentage").GetString());
        Assert.False(string.IsNullOrWhiteSpace(plot.GetProperty("plotCode").GetString()));
        var forward = await SendAsync(f.Stock.Customer, HttpMethod.Post, "/api/ledger/trace", new { lotId = f.SourceId, direction = "forward" });
        var container = Assert.Single(forward.GetProperty("actualContainers").EnumerateArray());
        Assert.Equal("10.000000", container.GetProperty("linkedLoadedKg").GetString());
        Assert.Equal("5.000000", container.GetProperty("sourceLoadedKg").GetString());
        var historical = graph.GetProperty("lots").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == f.LotId);
        Assert.Equal("20.000000", historical.GetProperty("producedKg").GetString());
        foreach (var line in edge.GetProperty("lines").EnumerateArray())
            Assert.Equal(decimal.Parse(line.GetProperty("quantityKg").GetString()!, CultureInfo.InvariantCulture),
                line.GetProperty("sourceContributions").EnumerateArray().Sum(x => decimal.Parse(x.GetProperty("quantityKg").GetString()!, CultureInfo.InvariantCulture)));
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var audits = new NpgsqlCommand("SELECT count(*) FROM grainledger.ledger_events WHERE kind='TRACEBACK' AND details->>'containerId'=$1", owner);
        audits.Parameters.AddWithValue(f.ContainerId.ToString());
        Assert.Equal(3L, await audits.ExecuteScalarAsync()); // empty, partial, full; no per-lot audits
        var traceId = graph.GetProperty("traceId").GetGuid();
        Assert.Equal(trace.GetProperty("traceId").GetGuid(), traceId);
        var history = await GetAsync(f.Stock.Customer, $"/api/ledger/business-history?category=TRACE&search={traceId}");
        Assert.Equal(traceId.ToString(), Assert.Single(history.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        Assert.Empty((await GetAsync(f.Stock.ForeignCustomer, $"/api/ledger/business-history?category=TRACE&search={traceId}")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Forward_edges_keep_two_actual_containers_distinct_and_export_cannot_read_backward_or_private_graph()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory, preBlended: true);
        var assessment = (await f.Detail()).GetProperty("readiness");
        // A second retained manifest is fixture-only; production writes are covered by the scenario above.
        var made = await ContainerRetainedTraceFixture.SeedAsync(factory, f, f.LotId, 4m, 2, assessment);
        var forward = await SendAsync(f.Export, HttpMethod.Post, "/api/ledger/trace", new { lotId = f.SourceId, direction = "forward" });
        Assert.Equal(2, forward.GetProperty("actualContainers").GetArrayLength());
        var edges = forward.GetProperty("loadingEdges").EnumerateArray().ToArray();
        Assert.Equal(2, edges.Length);
        Assert.Equal(made.Order(), edges.Select(x => x.GetProperty("containerId").GetGuid()).Order());
        Assert.DoesNotContain("sourceContributions", forward.GetProperty("loadingEdges").GetRawText());
        Assert.All(forward.GetProperty("actualContainers").EnumerateArray(), target =>
            Assert.Equal(System.Text.Json.JsonValueKind.Null, target.GetProperty("sourceLoadedKg").ValueKind));
        var backward = await SendAsync(f.Stock.Customer, HttpMethod.Post, f.Route + "/trace", new { });
        Assert.Equal(f.ContainerId, Assert.Single(backward.GetProperty("graph").GetProperty("actualContainers").EnumerateArray()).GetProperty("containerId").GetGuid());
        var lotBackward = await SendAsync(f.Stock.Customer, HttpMethod.Post, "/api/ledger/trace", new { lotId = f.LotId, direction = "backward" });
        Assert.False(lotBackward.TryGetProperty("actualContainers", out _));
        foreach (var client in new[] { f.Export, f.Stock.ForeignCustomer, f.Stock.Operator })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(f.Route + "/trace", new { })).StatusCode);
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
        await using var denied = new NpgsqlCommand("SELECT grainledger.ledger_container_trace_graph($1,$2)", runtime);
        denied.Parameters.AddWithValue(f.ActorId); denied.Parameters.AddWithValue(f.ContainerId);
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteScalarAsync())).SqlState);
        await using var privateCall = new NpgsqlCommand("SELECT grainledger.ledger_trace_graph($1,$2,'backward','current')", runtime);
        privateCall.Parameters.AddWithValue(f.Stock.Accounts["customer"].User.Id); privateCall.Parameters.AddWithValue(new[] { f.LotId });
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => privateCall.ExecuteScalarAsync())).SqlState);
    }
}
