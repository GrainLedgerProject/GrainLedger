using System.Net;
using System.Net.Http.Json;
using GrainLedger.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GrainLedger.Api.Features.Capture;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class CaptureSourceTraceabilityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Procurement_rejects_unassigned_legacy_variety()
    {
        using var scenario = await CreateAsync(factory, policies: false);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var plotSeason = await db.PlotSeasons.SingleAsync(x => x.Id == scenario.PlotSeasonId);
            plotSeason.RiceVarietyId = null;
            await db.SaveChangesAsync();
        }
        var response = await scenario.Purchaser.PostAsJsonAsync("/api/procurements",
            scenario.ReceiptBody(Guid.NewGuid(), ["100"]));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("RICE_VARIETY_UNRESOLVED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task IT01_spray_intake_source_lot_and_sample_preserve_the_same_origin()
    {
        using var scenario = await CreateAsync(factory, policies: false);
        var purchaser = scenario.Purchaser;
        var plotSeason = await purchaser.GetFromJsonAsync<PlotSeasonResponse>(
            $"/api/plot-seasons/{scenario.PlotSeasonId}");
        Assert.NotNull(plotSeason);
        Assert.Equal(scenario.PlotSeasonId, plotSeason.Id);
        Assert.Equal(scenario.SeasonId, plotSeason.SeasonId);

        var declarationId = Guid.NewGuid();
        await SendAsync(purchaser, HttpMethod.Post, "/api/spray-declarations", CaptureCommand(new
        {
            entityId = declarationId,
            plotSeasonId = plotSeason.Id,
            appliedOn = new DateOnly(2026, 10, 12),
            informationSource = "Synthetic field notebook",
            items = new[] { new { productName = "Synthetic test product" } }
        }, 2));

        var receiptId = Guid.NewGuid();
        var registered = await SendAsync(purchaser, HttpMethod.Post, "/api/procurements",
            scenario.ReceiptBody(receiptId, ["100"]));
        var sourceLotId = registered.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var variety = await db.RiceVarieties.SingleAsync(x => x.Id == scenario.RiceVarietyId);
            variety.Name = "ST25 renamed";
            await db.SaveChangesAsync();
        }
        var currentPlotSeason = await purchaser.GetFromJsonAsync<PlotSeasonReferenceResponse>($"/api/plot-seasons/{scenario.PlotSeasonId}");
        Assert.Equal("ST25 renamed", currentPlotSeason!.RiceVariety);

        var sampleId = Guid.NewGuid();
        await SendAsync(purchaser, HttpMethod.Post, "/api/samples", CaptureCommand(new
        {
            entityId = sampleId,
            sourceLotId,
            sealCode = "IT01-SYNTHETIC-SEAL",
            collectedAt = DateTimeOffset.UtcNow
        }, 2));

        var declaration = await purchaser.GetFromJsonAsync<SprayDeclarationResponse>(
            $"/api/spray-declarations/{declarationId}");
        var receipt = await purchaser.GetFromJsonAsync<ProcurementResponse>($"/api/procurements/{receiptId}");
        var source = await purchaser.GetFromJsonAsync<SourceLotResponse>($"/api/source-lots/{sourceLotId}");
        var sample = await purchaser.GetFromJsonAsync<SampleResponse>($"/api/samples/{sampleId}");

        Assert.NotNull(declaration);
        Assert.NotNull(receipt);
        Assert.NotNull(source);
        Assert.NotNull(sample);
        var line = Assert.Single(receipt.Items);
        Assert.Equal(scenario.FarmerId, receipt.FarmerId);
        Assert.Equal(plotSeason.Id, declaration.PlotSeasonId);
        Assert.Equal(declaration.PlotSeasonId, line.PlotSeasonId);
        Assert.Equal(plotSeason.PlotId, line.PlotId);
        Assert.Equal(plotSeason.SeasonId, line.SeasonId);
        Assert.Equal(scenario.RiceVarietyId, line.RiceVarietyId);
        Assert.Equal("ST25", line.RiceVariety);
        Assert.Equal(sourceLotId, line.SourceLotId);
        Assert.Equal(receiptId, source.ProcurementId);
        Assert.Equal(line.Id, source.ProcurementItemId);
        Assert.Equal(receipt.FarmerId, source.FarmerId);
        Assert.Equal(line.PlotSeasonId, source.PlotSeasonId);
        Assert.Equal(line.PlotId, source.PlotId);
        Assert.Equal(line.SeasonId, source.SeasonId);
        Assert.Equal(line.RiceVarietyId, source.RiceVarietyId);
        Assert.Equal(line.RiceVariety, source.RiceVariety);
        Assert.Equal(sourceLotId, sample.SourceLotId);
        Assert.Equal(sampleId, Assert.Single(source.SampleIds));

        foreach (var path in new[] { $"/api/spray-declarations/{declarationId}",
                     $"/api/procurements/{receiptId}", $"/api/source-lots/{sourceLotId}", $"/api/samples/{sampleId}" })
        {
            using var foreignRead = await scenario.Accounts["foreignPurchaser"].Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        }
    }

    [Fact]
    public async Task ST06_intake_mass_moisture_and_sample_seal_survive_readback()
    {
        using var scenario = await CreateAsync(factory, policies: false);
        var purchaser = scenario.Purchaser;
        var receiptId = Guid.NewGuid();
        var created = await SendAsync(purchaser, HttpMethod.Post, "/api/procurements",
            scenario.ReceiptBody(receiptId, ["600"]));
        var lotId = created.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid();

        var sampleId = Guid.NewGuid();
        const string seal = "ST06-SYNTHETIC-SEAL";
        await SendAsync(purchaser, HttpMethod.Post, "/api/samples", CaptureCommand(new
        {
            entityId = sampleId,
            sourceLotId = lotId,
            sealCode = seal,
            materialDescription = "Paddy grain",
            collectedAt = DateTimeOffset.UtcNow
        }, 2));

        var receipt = await purchaser.GetFromJsonAsync<ProcurementResponse>($"/api/procurements/{receiptId}");
        var source = await purchaser.GetFromJsonAsync<SourceLotResponse>($"/api/source-lots/{lotId}");
        var sample = await purchaser.GetFromJsonAsync<SampleResponse>($"/api/samples/{sampleId}");
        Assert.NotNull(receipt);
        Assert.NotNull(source);
        Assert.NotNull(sample);

        var line = Assert.Single(receipt.Items);
        Assert.Equal("600", line.MeasuredValue);
        Assert.Equal("KG", line.MeasuredUnit);
        Assert.Equal("600", receipt.TotalNetMassKg);
        Assert.Equal("600", source.InitialNetMassKg);
        Assert.Equal("22.5", line.MoisturePercent);
        Assert.Equal("22.5", source.MoisturePercent);
        Assert.Equal(line.SourceLotId, sample.SourceLotId);
        Assert.Equal(sampleId, Assert.Single(source.SampleIds));
        Assert.Equal(seal, sample.SealCode);
        Assert.Equal("Paddy grain", sample.MaterialDescription);
    }
}
