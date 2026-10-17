using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class SampleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(Guid CompanyId, Guid SourceLotId, HttpClient Client)> SeedSourceAsync()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (user, session) = await factory.SeedUserAsync(Roles.PurchasingStaff, company.Id);
        var client = factory.CreateRealCookieClient(user, session);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var farmerId = Guid.NewGuid(); var plotId = Guid.NewGuid();
        var seasonId = Guid.NewGuid(); var plotSeasonId = Guid.NewGuid();
        var receiptId = Guid.NewGuid(); var lineId = Guid.NewGuid(); var sourceLotId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        db.Farmers.Add(new Farmer { Id = farmerId, CompanyId = company.Id,
            Code = Guid.NewGuid().ToString("N"), Name = "Farmer", CreatedAt = DateTimeOffset.UtcNow });
        db.Plots.Add(new Plot { Id = plotId, CompanyId = company.Id, FarmerId = farmerId,
            Code = Guid.NewGuid().ToString("N"), AreaHa = 1m,
            BoundaryGeojson = "{\"type\":\"Point\",\"coordinates\":[106.35,10.4]}" });
        db.CropSeasons.Add(new CropSeason { Id = seasonId, CompanyId = company.Id,
            Name = "Test season", StartsOn = new DateOnly(2026, 1, 1), EndsOn = new DateOnly(2026, 12, 31) });
        var ev = new CaptureBusinessEvent { CompanyId = company.Id, ActorId = user.Id,
            EventType = "PROCUREMENT_COMMITTED", OccurredAt = DateTimeOffset.UtcNow,
            RecordedAt = DateTimeOffset.UtcNow, CorrelationId = Guid.NewGuid(), Payload = "{}" };
        db.BusinessEvents.Add(ev);
        await db.SaveChangesAsync();
        db.PlotSeasons.Add(new PlotSeason { Id = plotSeasonId, CompanyId = company.Id,
            PlotId = plotId, SeasonId = seasonId, RiceVariety = "ST25" });
        await db.SaveChangesAsync();
        db.Procurements.Add(new Procurement { Id = receiptId, CompanyId = company.Id,
            Code = $"TM-{receiptId:N}", FarmerId = farmerId, CollectionSiteId = Guid.NewGuid(),
            ReceivedBy = user.Id, ReceivedAt = DateTimeOffset.UtcNow, Status = "COMMITTED", EventId = ev.Id });
        await db.SaveChangesAsync();
        db.ProcurementItems.Add(new ProcurementItem { Id = lineId, CompanyId = company.Id,
            ProcurementId = receiptId, LineNo = 1, PlotSeasonId = plotSeasonId,
            NetMassKg = 100m, MeasuredValue = 100m, MeasuredUnit = "KG", MoisturePercent = 20m });
        await db.SaveChangesAsync();
        db.SourceLots.Add(new SourceLot { Id = sourceLotId, CompanyId = company.Id,
            LotId = Guid.NewGuid(), ProcurementItemId = lineId });
        await db.SaveChangesAsync();
        return (company.Id, sourceLotId, client);
    }

    private static object SampleRequest(Guid sourceLotId, Guid entityId, Guid operationId, string code) => new
    {
        deviceId = "sample-test", clientOperationId = operationId,
        capturedAt = new DateTimeOffset(2026, 10, 14, 3, 5, 0, TimeSpan.Zero), payloadSchemaVersion = 1,
        payload = new { entityId, sourceLotId, code, sealCode = "SEAL-001",
            collectedAt = new DateTimeOffset(2026, 10, 14, 3, 0, 0, TimeSpan.Zero) }
    };

    private static object HandoverRequest(Guid entityId, Guid operationId) => new
    {
        deviceId = "sample-test", clientOperationId = operationId,
        capturedAt = new DateTimeOffset(2026, 10, 14, 7, 5, 0, TimeSpan.Zero), payloadSchemaVersion = 1,
        payload = new { entityId, fromParty = "Collection agent", toParty = "Laboratory",
            handedOverAt = new DateTimeOffset(2026, 10, 14, 7, 0, 0, TimeSpan.Zero),
            sealCondition = "Intact", evidenceRef = (string?)null }
    };

    [Fact]
    public async Task Collects_sample_then_records_handover_with_replay_and_readback()
    {
        var source = await SeedSourceAsync();
        using var client = source.Client;
        var sampleId = Guid.NewGuid(); var sampleOperation = Guid.NewGuid();
        var request = SampleRequest(source.SourceLotId, sampleId, sampleOperation, $"S-{sampleId:N}");
        var created = await client.PostAsJsonAsync("/api/samples", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var sample = (await created.Content.ReadFromJsonAsync<SampleResponse>())!;
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
        Assert.Equal(handoverId, history.Items[0].Id);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        Assert.Equal(1, await db.Samples.CountAsync(x => x.Id == sampleId));
        Assert.Equal(1, await db.SampleHandovers.CountAsync(x => x.SampleId == sampleId));
        Assert.Equal(1, await db.BusinessEvents.CountAsync(x => x.CorrelationId == sampleOperation));
        Assert.Equal(1, await db.BusinessEvents.CountAsync(x => x.CorrelationId == handoverOperation));
    }

    [Fact]
    public async Task Rejects_unaccepted_source_duplicate_code_and_changed_operation()
    {
        var source = await SeedSourceAsync();
        using var client = source.Client;
        var sampleId = Guid.NewGuid(); var operationId = Guid.NewGuid(); var code = $"S-{sampleId:N}";
        var request = SampleRequest(source.SourceLotId, sampleId, operationId, code);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/samples", request)).StatusCode);
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
        using var owner = source.Client;
        var sampleId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/samples",
            SampleRequest(source.SourceLotId, sampleId, Guid.NewGuid(), $"S-{sampleId:N}"))).StatusCode);
        var other = await SeedSourceAsync();
        using var outsider = other.Client;
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/samples/{sampleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await outsider.PostAsJsonAsync($"/api/samples/{sampleId}/handovers",
            HandoverRequest(Guid.NewGuid(), Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await outsider.PostAsJsonAsync("/api/samples",
            SampleRequest(source.SourceLotId, Guid.NewGuid(), Guid.NewGuid(), "other-company"))).StatusCode);
    }
}
