using System.Net;
using System.Text.Json;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ByproductVarietyPolicyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Path = "/api/ledger/byproduct-variety-policy";

    [Fact]
    public async Task Overrides_are_independent_scoped_and_concurrency_checked()
    {
        using var s = await CreateAsync(factory);
        var initial = await GetAsync(s.Customer, Path);
        Assert.Equal(s.OwnerId, initial.GetProperty("companyId").GetGuid());
        var items = initial.GetProperty("items");
        Assert.Equal(new[] { "BRAN", "HUSK" }, items.EnumerateArray().Select(x => x.GetProperty("stage").GetString()).ToArray());
        Assert.All(items.EnumerateArray(), item => { Assert.True(item.GetProperty("allowCrossVariety").GetBoolean()); Assert.Equal(JsonValueKind.Null, item.GetProperty("revision").ValueKind); });
        var bran = await SendAsync(s.Customer, HttpMethod.Put, Path + "/BRAN", new { allowCrossVariety = false, expectedRevision = (Guid?)null, reason = "Separate bran" });
        var revision = bran.GetProperty("revision").GetGuid();
        Assert.False(bran.GetProperty("allowCrossVariety").GetBoolean());
        await RejectedAsync(s.Customer, HttpMethod.Put, Path + "/BRAN", new { allowCrossVariety = true, expectedRevision = (Guid?)null, reason = "Stale" }, HttpStatusCode.Conflict);
        var husk = await SendAsync(s.Customer, HttpMethod.Put, Path + "/HUSK", new { allowCrossVariety = false, expectedRevision = (Guid?)null, reason = "Separate husk" });
        var reset = await SendAsync(s.Customer, HttpMethod.Put, Path + "/BRAN", new { allowCrossVariety = (bool?)null, expectedRevision = revision, reason = "Reset bran" });
        Assert.True(reset.GetProperty("allowCrossVariety").GetBoolean());
        Assert.Equal("SYSTEM_DEFAULT", reset.GetProperty("source").GetString());
        Assert.NotEqual(revision, reset.GetProperty("revision").GetGuid());
        var after = (await GetAsync(s.Customer, Path)).GetProperty("items");
        Assert.Equal(husk.GetProperty("revision").GetGuid(), after[1].GetProperty("revision").GetGuid());
        Assert.False(after[1].GetProperty("allowCrossVariety").GetBoolean());
        var foreign = (await GetAsync(s.ForeignCustomer, Path)).GetProperty("items");
        Assert.All(foreign.EnumerateArray(), item => Assert.True(item.GetProperty("allowCrossVariety").GetBoolean()));
        foreach (var client in new[] { s.Admin, s.Operator, s.Manager, s.Purchaser })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Path)).StatusCode);
            await RejectedAsync(client, HttpMethod.Put, Path + "/BRAN", new { allowCrossVariety = false, expectedRevision = (Guid?)null, reason = "Denied" }, HttpStatusCode.Forbidden);
        }
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path)).StatusCode);
        await RejectedAsync(s.Customer, HttpMethod.Put, Path + "/bran", new { allowCrossVariety = false, expectedRevision = (Guid?)null, reason = "Invalid" }, HttpStatusCode.BadRequest);
        await RejectedAsync(s.Customer, HttpMethod.Put, Path + "/BRAN", new { allowCrossVariety = false, expectedRevision = (Guid?)null, reason = "Invalid", companyId = s.ForeignOwnerId }, HttpStatusCode.BadRequest);
        await RejectedAsync(s.Customer, HttpMethod.Put, Path + "/BRAN", new { allowCrossVariety = "false", expectedRevision = (Guid?)null, reason = "Invalid" }, HttpStatusCode.BadRequest);
        await RejectedAsync(s.Customer, HttpMethod.Put, Path + "/BRAN", new { allowCrossVariety = false, reason = "Missing revision" }, HttpStatusCode.BadRequest);
        await RejectedAsync(s.Customer, HttpMethod.Put, Path + "/BRAN", new { allowCrossVariety = false, expectedRevision = Guid.Empty, reason = "Invalid revision" }, HttpStatusCode.BadRequest);
    }
}
