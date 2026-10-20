using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class SampleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(Guid CompanyId, Guid SourceLotId, HttpClient Client, LedgerScenario Scenario)> SeedSourceAsync(bool foreign = false)
    {
        var scenario = await LedgerScenario.CreateAsync(factory, policies: false);
        var companyId = foreign ? scenario.ForeignOwnerId : scenario.OwnerId;
        var client = foreign ? scenario.Accounts["foreignPurchaser"].Client : scenario.Purchaser;
        var source = await scenario.RegisterSourceAsync(client, companyId);
        var receipt = await SendAsync(client, HttpMethod.Post, "/api/procurements",
            scenario.ReceiptBody(Guid.NewGuid(), ["100"], farmerId: source.Farmer, plotSeasonId: source.PlotSeason));
        var sourceLotId = receipt.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid();
        return (companyId, sourceLotId, client, scenario);
    }

    private static object SampleRequest(Guid sourceLotId, Guid entityId, Guid operationId, string code,
        string? materialDescription = null) => new
    {
        deviceId = "sample-test", clientOperationId = operationId,
        capturedAt = new DateTimeOffset(2026, 10, 14, 3, 5, 0, TimeSpan.Zero), payloadSchemaVersion = 1,
        payload = new { entityId, sourceLotId, code, sealCode = "SEAL-001",
            collectedAt = new DateTimeOffset(2026, 10, 14, 3, 0, 0, TimeSpan.Zero), materialDescription }
    };

    private static object HandoverRequest(Guid entityId, Guid operationId,
        string? handoverKind = null, string? laboratoryName = null,
        string? laboratoryReference = null, string? laboratorySampleReference = null) => new
    {
        deviceId = "sample-test", clientOperationId = operationId,
        capturedAt = new DateTimeOffset(2026, 10, 14, 7, 5, 0, TimeSpan.Zero), payloadSchemaVersion = 1,
        payload = new { entityId, fromParty = "Collection agent", toParty = "Laboratory",
            handedOverAt = new DateTimeOffset(2026, 10, 14, 7, 0, 0, TimeSpan.Zero),
            sealCondition = "Intact", evidenceRef = (string?)"handover-proof",
            handoverKind, laboratoryName, laboratoryReference, laboratorySampleReference }
    };

    [Fact]
    public async Task Collects_sample_then_records_handover_with_replay_and_readback()
    {
        var source = await SeedSourceAsync();
        using var scenario = source.Scenario;
        using var client = source.Client;
        var sampleId = Guid.NewGuid(); var sampleOperation = Guid.NewGuid();
        var request = SampleRequest(source.SourceLotId, sampleId, sampleOperation, $"S-{sampleId:N}");
        var created = await client.PostAsJsonAsync("/api/samples", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var sample = (await created.Content.ReadFromJsonAsync<SampleResponse>())!;
        Assert.Null(sample.MaterialDescription);
        Assert.Equal(source.SourceLotId, sample.SourceLotId);
        Assert.Equal(0, sample.HandoverCount);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/samples", request)).StatusCode);

        var handoverId = Guid.NewGuid(); var handoverOperation = Guid.NewGuid();
        var handover = HandoverRequest(handoverId, handoverOperation);
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync($"/api/samples/{sampleId}/handovers", handover)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync($"/api/samples/{sampleId}/handovers", handover)).StatusCode);
        var detail = await client.GetFromJsonAsync<SampleResponse>($"/api/samples/{sampleId}");
        Assert.Equal(1, detail!.HandoverCount);
        var list = await client.GetFromJsonAsync<SampleListResponse>($"/api/samples?sourceLotId={source.SourceLotId}");
        Assert.Contains(list!.Items, x => x.Id == sampleId && x.HandoverCount == 1);
        var history = await client.GetFromJsonAsync<SampleHandoverListResponse>($"/api/samples/{sampleId}/handovers");
        Assert.Single(history!.Items);
        Assert.Equal("HANDOVER", history.Items[0].HandoverKind);
        Assert.Equal("handover-proof", history.Items[0].EvidenceRef);
        Assert.Equal(handoverId, history.Items[0].Id);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        Assert.Equal(1, await db.Samples.CountAsync(x => x.Id == sampleId));
        Assert.Equal(1, await db.SampleHandovers.CountAsync(x => x.SampleId == sampleId));
        Assert.Equal(1, await db.BusinessEvents.CountAsync(x => x.CorrelationId == sampleOperation));
        Assert.Equal(1, await db.BusinessEvents.CountAsync(x => x.CorrelationId == handoverOperation));
    }

    [Fact]
    public async Task Records_material_and_distinct_lab_handover_kinds_with_content_bound_replay()
    {
        var source = await SeedSourceAsync();
        using var scenario = source.Scenario;
        using var client = source.Client;
        var sampleId = Guid.NewGuid();
        var sample = await client.PostAsJsonAsync("/api/samples",
            SampleRequest(source.SourceLotId, sampleId, Guid.NewGuid(), $"S-{sampleId:N}", "Paddy grain"));
        Assert.Equal(HttpStatusCode.Created, sample.StatusCode);
        Assert.Equal("Paddy grain", (await sample.Content.ReadFromJsonAsync<SampleResponse>())!.MaterialDescription);

        var dispatchId = Guid.NewGuid();
        var dispatchOperation = Guid.NewGuid();
        var dispatch = HandoverRequest(dispatchId, dispatchOperation, "LAB_DISPATCH",
            "North Lab", "REF-1", "N-100");
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync($"/api/samples/{sampleId}/handovers", dispatch)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync($"/api/samples/{sampleId}/handovers", dispatch)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync($"/api/samples/{sampleId}/handovers",
                HandoverRequest(dispatchId, dispatchOperation, "LAB_DISPATCH",
                    "North Lab", "REF-2", "N-100"))).StatusCode);

        var acknowledgementId = Guid.NewGuid();
        var acknowledgement = await client.PostAsJsonAsync($"/api/samples/{sampleId}/handovers",
            HandoverRequest(acknowledgementId, Guid.NewGuid(), "LAB_ACKNOWLEDGEMENT", "North Lab", "REF-1", "N-100"));
        Assert.Equal(HttpStatusCode.Created, acknowledgement.StatusCode);
        var handovers = await client.GetFromJsonAsync<SampleHandoverListResponse>(
            $"/api/samples/{sampleId}/handovers");
        Assert.Contains(handovers!.Items, x => x.HandoverKind == "LAB_DISPATCH" && x.LaboratoryName == "North Lab");
        Assert.Contains(handovers.Items, x => x.HandoverKind == "LAB_ACKNOWLEDGEMENT");
    }

    [Fact]
    public async Task Requires_laboratory_name_for_lab_handover_kind()
    {
        var source = await SeedSourceAsync();
        using var scenario = source.Scenario;
        using var client = source.Client;
        var sampleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/samples",
            SampleRequest(source.SourceLotId, sampleId, Guid.NewGuid(), $"S-{sampleId:N}"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            $"/api/samples/{sampleId}/handovers",
            HandoverRequest(Guid.NewGuid(), Guid.NewGuid(), "LAB_DISPATCH"))).StatusCode);
    }

    [Fact]
    public async Task Rejects_unaccepted_source_duplicate_code_and_changed_operation()
    {
        var source = await SeedSourceAsync();
        using var scenario = source.Scenario;
        using var client = source.Client;
        var sampleId = Guid.NewGuid(); var operationId = Guid.NewGuid(); var code = $"S-{sampleId:N}";
        var request = SampleRequest(source.SourceLotId, sampleId, operationId, code);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/samples", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/samples",
            SampleRequest(source.SourceLotId, sampleId, operationId, code, "Changed material"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/samples",
            SampleRequest(source.SourceLotId, sampleId, operationId, "changed"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/samples",
            SampleRequest(source.SourceLotId, Guid.NewGuid(), Guid.NewGuid(), code))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/samples",
            SampleRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "other"))).StatusCode);
    }

    [Fact]
    public async Task Hides_other_company_sample_and_does_not_create_handover()
    {
        var source = await SeedSourceAsync();
        using var scenario = source.Scenario;
        using var owner = source.Client;
        var sampleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/samples",
            SampleRequest(source.SourceLotId, sampleId, Guid.NewGuid(), $"S-{sampleId:N}"))).StatusCode);
        var other = await SeedSourceAsync(foreign: true);
        using var otherScenario = other.Scenario;
        using var outsider = other.Client;
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/samples/{sampleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await outsider.PostAsJsonAsync($"/api/samples/{sampleId}/handovers",
            HandoverRequest(Guid.NewGuid(), Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await outsider.PostAsJsonAsync("/api/samples",
            SampleRequest(source.SourceLotId, Guid.NewGuid(), Guid.NewGuid(), "other-company"))).StatusCode);
    }
}
