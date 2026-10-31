using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class PlanningQualityObservationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Reviewed_finished_lot_measurement_is_versioned_and_owner_scoped()
    {
        using var scenario = await LedgerScenario.CreateAsync(factory);
        var receipt = await scenario.ReceiptAsync("30");
        var raw = LedgerScenario.Lots(receipt)[0];
        var drying = await scenario.DraftAsync("DRYING", [(raw, "30")],
            [("DRIED_PADDY", "30")]);
        await scenario.CommitAsync(drying);
        var milling = await scenario.DraftAsync("MILLING",
            [(LedgerScenario.Output(drying), "30")],
            [("HEAD_RICE", "20"), ("BRAN", "10")]);
        await scenario.CommitAsync(milling);
        var lotId = LedgerScenario.Output(milling, 0);
        var attributeId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            db.QualityAttributes.Add(new QualityAttribute
            {
                Id = attributeId, Code = "Q" + attributeId.ToString("N"),
                Name = "Synthetic moisture", Unit = "%"
            });
            await db.SaveChangesAsync();
        }
        object Body(Guid id, int version, string value) => new
        {
            id, ownerId = scenario.OwnerId, facilityId = scenario.FacilityId,
            lotId, attributeId, versionNo = version, measuredValue = value,
            unit = "%", measuredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            evidenceId = scenario.EvidenceId, reportReference = "SYNTHETIC-QC-1",
            reviewedBy = "Synthetic reviewer", reviewAuthority = "Synthetic authority",
            reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-2), subjectConfirmed = true
        };
        var denied = await scenario.ForeignCustomer.PostAsJsonAsync(
            "/api/ledger/planning-quality-observations", Body(Guid.NewGuid(), 1, "12"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var first = await scenario.Customer.PostAsJsonAsync(
            "/api/ledger/planning-quality-observations", Body(Guid.NewGuid(), 1, "12"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await scenario.Customer.PostAsJsonAsync(
            "/api/ledger/planning-quality-observations", Body(Guid.NewGuid(), 2, "13"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var (staff, session) = await factory.SeedUserAsync(Roles.ExportStaff, scenario.OwnerId);
        using var reader = factory.CreateRealCookieClient(staff, session);
        var rows = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/ledger/planning-quality-observations?ownerId={scenario.OwnerId}" +
            $"&facilityId={scenario.FacilityId}&lotIds={lotId}");
        var current = Assert.Single(rows.EnumerateArray().ToArray());
        Assert.Equal(2, current.GetProperty("versionNo").GetInt32());
        Assert.Equal("13", current.GetProperty("measuredValue").GetString());
        var managerRows = await scenario.Customer.GetFromJsonAsync<JsonElement>(
            $"/api/ledger/planning-quality-observations?ownerId={scenario.OwnerId}" +
            $"&facilityId={scenario.FacilityId}&lotIds={lotId}");
        Assert.Equal(2, Assert.Single(managerRows.EnumerateArray().ToArray())
            .GetProperty("versionNo").GetInt32());
        var foreignManagerRead = await scenario.ForeignCustomer.GetAsync(
            $"/api/ledger/planning-quality-observations?ownerId={scenario.OwnerId}" +
            $"&facilityId={scenario.FacilityId}&lotIds={lotId}");
        Assert.Equal(HttpStatusCode.Forbidden, foreignManagerRead.StatusCode);
        var (foreign, foreignSession) = await factory.SeedUserAsync(Roles.ExportStaff,
            scenario.ForeignOwnerId);
        using var foreignReader = factory.CreateRealCookieClient(foreign, foreignSession);
        var foreignRead = await foreignReader.GetAsync(
            $"/api/ledger/planning-quality-observations?ownerId={scenario.OwnerId}" +
            $"&facilityId={scenario.FacilityId}&lotIds={lotId}");
        Assert.Equal(HttpStatusCode.Forbidden, foreignRead.StatusCode);
    }
}
