using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Api.Data;
using GrainLedger.Api.Data.Migrations;
using GrainLedger.Api.Features.Capture;
using GrainLedger.Api.Features.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class CaptureCodeIssuanceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("/api/farmers")]
    [InlineData("/api/plots")]
    [InlineData("/api/procurements")]
    [InlineData("/api/samples")]
    public async Task Creation_rejects_version_one(string route)
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (user, session) = await factory.SeedUserAsync(Roles.PurchasingStaff, company.Id);
        using var client = factory.CreateRealCookieClient(user, session);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var response = await client.PostAsJsonAsync(route, new { deviceId = "test",
            clientOperationId = Guid.NewGuid(), capturedAt = DateTimeOffset.UtcNow,
            payloadSchemaVersion = 1, payload = new { entityId = Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("errors").TryGetProperty("payloadSchemaVersion", out _));
    }

    [Fact]
    public async Task Migration_seeds_continuous_company_and_kind_counters_without_changing_records()
    {
        using var scenario = await LedgerScenario.CreateAsync(factory, policies: false);
        var receipt = await LedgerScenario.SendAsync(scenario.Purchaser, HttpMethod.Post, "/api/procurements",
            scenario.ReceiptBody(Guid.NewGuid(), ["100"]));
        var receiptId = receipt.GetProperty("id").GetGuid();
        var receiptCode = receipt.GetProperty("code").GetString();
        var sourceLotId = receipt.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid();
        var other = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.OwnerConnectionString).Options;
        await using var db = new AppDbContext(options);
        var sampleId = Guid.NewGuid();
        db.Samples.Add(new Sample { Id = sampleId, CompanyId = scenario.OwnerId, Code = "SMP-20261017-0081",
            SourceLotId = sourceLotId, CollectedBy = scenario.Accounts["purchaser"].User.Id,
            CollectedAt = DateTimeOffset.UtcNow, SealCode = "seal" });
        var oversizedSampleId = Guid.NewGuid();
        const string oversizedCode = "SMP-20261017-999999999999999999999999999999";
        db.Samples.Add(new Sample { Id = oversizedSampleId, CompanyId = scenario.OwnerId, Code = oversizedCode,
            SourceLotId = sourceLotId, CollectedBy = scenario.Accounts["purchaser"].User.Id,
            CollectedAt = DateTimeOffset.UtcNow, SealCode = "oversized" });
        db.CodeCounters.AddRange(
            new CaptureCodeCounter { CompanyId = scenario.OwnerId, Kind = "PRC", Scope = "20261017", NextValue = 40 },
            new CaptureCodeCounter { CompanyId = scenario.OwnerId, Kind = "SMP", Scope = "20261017", NextValue = 30 },
            new CaptureCodeCounter { CompanyId = other.Id, Kind = "PRC", Scope = "20261017", NextValue = 900 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var sql = Assert.IsType<SqlOperation>(Assert.Single(new ContinuousCaptureCodes().UpOperations)).Sql;
        await db.Database.OpenConnectionAsync();
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)");
        var beforeMidnight = new DateTimeOffset(2026, 10, 17, 16, 59, 0, TimeSpan.Zero);
        var afterMidnight = beforeMidnight.AddMinutes(2);
        Assert.Equal("PRC-20261017-0040", await CaptureCodes.DatedAsync(db, scenario.OwnerId, "PRC", beforeMidnight, default));
        Assert.Equal("PRC-20261018-0041", await CaptureCodes.DatedAsync(db, scenario.OwnerId, "PRC", afterMidnight, default));
        Assert.Equal("SMP-20261018-0082", await CaptureCodes.DatedAsync(db, scenario.OwnerId, "SMP", afterMidnight, default));
        Assert.Equal("PRC-20261018-0900", await CaptureCodes.DatedAsync(db, other.Id, "PRC", afterMidnight, default));
        Assert.Equal("SMP-20261017-0001", await CaptureCodes.DatedAsync(db, other.Id, "SMP", beforeMidnight, default));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(receiptCode, (await db.Procurements.SingleAsync(x => x.Id == receiptId)).Code);
        Assert.Equal("SMP-20261017-0081", (await db.Samples.SingleAsync(x => x.Id == sampleId)).Code);
        Assert.Equal(oversizedCode, (await db.Samples.SingleAsync(x => x.Id == oversizedSampleId)).Code);
    }

    [Fact]
    public async Task Allocation_expands_serial_and_rollback_does_not_consume_it()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.OwnerConnectionString).Options;
        var date = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var db = new AppDbContext(options))
        {
            db.CodeCounters.Add(new CaptureCodeCounter { CompanyId = company.Id, Kind = "PRC", Scope = "", NextValue = 10000 });
            await db.SaveChangesAsync();
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)");
            Assert.Equal("PRC-20260101-10000", await CaptureCodes.DatedAsync(db, company.Id, "PRC", date, default));
            await db.SaveChangesAsync();
            await tx.RollbackAsync();
        }
        await using var nextDb = new AppDbContext(options);
        await using var nextTx = await nextDb.Database.BeginTransactionAsync();
        await nextDb.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)");
        Assert.Equal("PRC-20260101-10000", await CaptureCodes.DatedAsync(nextDb, company.Id, "PRC", date, default));
    }
}
