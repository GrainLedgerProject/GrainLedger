using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Catalogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

internal sealed class LedgerScenario : IDisposable
{
    public ApiFactory Factory { get; }
    public Guid OwnerId { get; private set; }
    public Guid ForeignOwnerId { get; private set; }
    public Guid FacilityId { get; private set; } = Guid.NewGuid();
    public Guid ForeignFacilityId { get; private set; } = Guid.NewGuid();
    public Guid LocationId { get; private set; } = Guid.NewGuid();
    public Guid SecondLocationId { get; private set; } = Guid.NewGuid();
    public Guid IsolationLocationId { get; private set; } = Guid.NewGuid();
    public Guid ForeignLocationId { get; private set; } = Guid.NewGuid();
    public Guid EquipmentId { get; private set; } = Guid.NewGuid();
    public Guid CommodityId { get; private set; }
    public Guid CatalogCommodityId { get; private set; }
    public Guid FarmerId { get; private set; } = Guid.NewGuid();
    public Guid SeasonId { get; private set; } = Guid.NewGuid();
    public Guid PlotSeasonId { get; private set; } = Guid.NewGuid();
    public Guid EvidenceId { get; private set; } = Guid.NewGuid();
    public Dictionary<string, (User User, LoginSession Session, HttpClient Client)> Accounts { get; } = [];
    public HttpClient Admin => Accounts["admin"].Client;
    public HttpClient Manager => Accounts["manager"].Client;
    public HttpClient Operator => Accounts["operator"].Client;
    public HttpClient Purchaser => Accounts["purchaser"].Client;
    public HttpClient Customer => Accounts["customer"].Client;
    public HttpClient ForeignCustomer => Accounts["foreignCustomer"].Client;
    private LedgerScenario(ApiFactory factory) => Factory = factory;

    public static async Task<LedgerScenario> CreateAsync(ApiFactory factory, bool policies = true)
    {
        var s = new LedgerScenario(factory);
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        s.OwnerId = (await factory.SeedOrganizationAsync(OrganizationKind.Customer)).Id;
        s.ForeignOwnerId = (await factory.SeedOrganizationAsync(OrganizationKind.Customer)).Id;
        foreach (var (label, role, owner) in new[]
        {
            ("admin", Roles.Administrator, (Guid?)mill.Id), ("manager", Roles.MillManager, (Guid?)mill.Id),
            ("operator", Roles.MillOperator, (Guid?)mill.Id), ("purchaser", Roles.PurchasingStaff, (Guid?)s.OwnerId),
            ("customer", Roles.CustomerManager, (Guid?)s.OwnerId), ("foreignCustomer", Roles.CustomerManager, (Guid?)s.ForeignOwnerId),
            ("foreignPurchaser", Roles.PurchasingStaff, (Guid?)s.ForeignOwnerId), ("pending", (string?)null, (Guid?)null)
        })
        {
            var (user, session) = await factory.SeedUserAsync(role, owner);
            var client = factory.CreateRealCookieClient(user, session);
            await CsrfAsync(client);
            s.Accounts.Add(label, (user, session, client));
        }
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/facilities", new { id = s.FacilityId, name = "Synthetic test facility" });
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/facilities", new { id = s.ForeignFacilityId, name = "Unassigned test facility" });
        foreach (var (id, facilityId, kind) in new[] { (s.LocationId, s.FacilityId, "STORE"), (s.SecondLocationId, s.FacilityId, "STORE"), (s.IsolationLocationId, s.FacilityId, "ISOLATION"), (s.ForeignLocationId, s.ForeignFacilityId, "STORE") })
            await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/locations", new { id, facilityId, name = $"Synthetic {kind} {id:N}", kind });
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/equipment", new { id = s.EquipmentId, facilityId = s.FacilityId, name = "Synthetic equipment" });
        using (var scope = factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var paddy = await catalog.Commodities.SingleOrDefaultAsync(x => x.Code == "PADDY");
            if (paddy is null)
            {
                var created = await SendAsync(s.Admin, HttpMethod.Post, "/api/commodities", new { code = "PADDY", name = "Paddy" });
                s.CatalogCommodityId = created.GetProperty("id").GetGuid();
            }
            else s.CatalogCommodityId = paddy.Id;
            foreach (var unit in new[] { "KG", "G", "T" })
                if (!await catalog.MeasurementUnits.AnyAsync(x => x.Code == unit))
                    catalog.MeasurementUnits.Add(new MeasurementUnit { Id = Guid.NewGuid(), Code = unit, Name = unit, QuantityKind = "MASS" });
            await catalog.SaveChangesAsync();
        }
        s.CommodityId = (await GetAsync(s.Admin, "/api/ledger/setup")).GetProperty("commodities").EnumerateArray()
            .Single(x => x.GetProperty("catalogId").ValueKind == JsonValueKind.String &&
                x.GetProperty("catalogId").GetGuid() == s.CatalogCommodityId).GetProperty("id").GetGuid();
        foreach (var label in new[] { "operator", "manager" })
            await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/assignments", new { userId = s.Accounts[label].User.Id, facilityId = s.FacilityId });
        await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/evidence", new { id = s.EvidenceId, ownerId = s.OwnerId, facilityId = s.FacilityId, fileName = "synthetic-test-policy.txt", contentType = "text/plain", contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("SYNTHETIC TEST ONLY. Not factory-approved policy. Physical verification test evidence.")) });
        if (policies)
            foreach (var kind in new[] { "DRYING", "MILLING", "MERGE", "BLENDING", "BAGGING" }) await s.PolicyAsync(kind, "0");
        var source = await s.RegisterSourceAsync(s.Purchaser, s.OwnerId);
        s.FarmerId = source.Farmer;
        s.SeasonId = source.Season;
        s.PlotSeasonId = source.PlotSeason;
        return s;
    }

    public async Task PolicyAsync(string kind, string tolerance) => await SendAsync(Admin, HttpMethod.Post, "/api/ledger/policies", new { id = Guid.NewGuid(), facilityId = FacilityId, kind, tolerancePercent = tolerance, effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1), evidenceId = EvidenceId });

    public static object CaptureCommand(object payload) => new { deviceId = "integration-test", clientOperationId = Guid.NewGuid(),
        capturedAt = DateTimeOffset.UtcNow, payloadSchemaVersion = 1, payload };

    public async Task<(Guid Farmer, Guid Season, Guid PlotSeason)> RegisterSourceAsync(HttpClient client, Guid company)
    {
        var farmer = Guid.NewGuid(); var plot = Guid.NewGuid(); var plotSeason = Guid.NewGuid();
        await SendAsync(client, HttpMethod.Post, "/api/farmers", CaptureCommand(new { entityId = farmer, name = "Test source farmer", contact = "+84000000000" }));
        await SendAsync(client, HttpMethod.Post, "/api/plots", CaptureCommand(new { entityId = plot, farmerId = farmer,
            areaHa = 1m, geometry = new { type = "Point", coordinates = new[] { 106.123456m, 10.123456m } } }));
        var season = (await SendAsync(Admin, HttpMethod.Post, "/api/crop-seasons", new { companyId = company, name = "Test season", startsOn = "2026-01-01", endsOn = "2026-12-31" })).GetProperty("id").GetGuid();
        await SendAsync(client, HttpMethod.Post, "/api/plot-seasons", CaptureCommand(new { entityId = plotSeason, plotId = plot, seasonId = season, riceVariety = "ST25" }));
        return (farmer, season, plotSeason);
    }

    public object ReceiptBody(Guid id, string[] quantities, Guid? locationId = null, string unit = "KG",
        Guid? farmerId = null, Guid? plotSeasonId = null) => CaptureCommand(new
    {
        entityId = id, farmerId = farmerId ?? FarmerId,
        collectionSiteId = locationId == ForeignLocationId ? ForeignFacilityId : FacilityId,
        receivingLocationId = locationId ?? LocationId, commodityId = CatalogCommodityId, receivedAt = DateTimeOffset.UtcNow,
        items = quantities.Select((quantity, index) => new { itemId = Guid.NewGuid(), lineNo = index + 1,
            plotSeasonId = plotSeasonId ?? PlotSeasonId, measuredValue = quantity, measuredUnit = unit,
            moisturePercent = "22.5", lotId = Guid.NewGuid(), sourceLotId = Guid.NewGuid() }).ToArray()
    });

    public async Task<JsonElement> ReceiptAsync(params string[] quantities)
    {
        var receipt = await SendAsync(Purchaser, HttpMethod.Post, "/api/procurements", ReceiptBody(Guid.NewGuid(), quantities));
        await ConfirmArrivalAsync(receipt);
        CommodityId = (await LotAsync(Lots(receipt)[0], Customer)).GetProperty("commodityId").GetGuid();
        return receipt;
    }

    public async Task<JsonElement> ForeignReceiptAsync(string quantity, Guid? locationId = null)
    {
        var client = Accounts["foreignPurchaser"].Client;
        var source = await RegisterSourceAsync(client, ForeignOwnerId);
        var receipt = await SendAsync(client, HttpMethod.Post, "/api/procurements",
            ReceiptBody(Guid.NewGuid(), [quantity], locationId, farmerId: source.Farmer, plotSeasonId: source.PlotSeason));
        if (locationId == ForeignLocationId)
        {
            var (operatorUser, session) = await Factory.SeedUserAsync(Roles.MillOperator, Accounts["operator"].User.OrganizationId);
            using var foreignOperator = Factory.CreateRealCookieClient(operatorUser, session);
            await CsrfAsync(foreignOperator);
            await SendAsync(Admin, HttpMethod.Post, "/api/ledger/assignments", new { userId = operatorUser.Id, facilityId = ForeignFacilityId });
            await SendAsync(foreignOperator, HttpMethod.Post, $"/api/ledger/receiving/{receipt.GetProperty("id").GetGuid()}/measurements", MeasurementBody(receipt));
        }
        else await ConfirmArrivalAsync(receipt);
        CommodityId = (await LotAsync(Lots(receipt)[0], ForeignCustomer)).GetProperty("commodityId").GetGuid();
        return receipt;
    }

    public static object MeasurementBody(JsonElement receipt, int version = 0, string[]? actual = null, Guid? operationId = null, string observations = "Synthetic measured complete arrival") => new
    {
        operationId = operationId ?? Guid.NewGuid(), expectedReceivingVersion = version,
        capturedAt = DateTimeOffset.UtcNow, observations,
        items = receipt.GetProperty("items").EnumerateArray().Select((line, i) => new
        {
            procurementItemId = line.GetProperty("id").GetGuid(),
            measuredValue = actual is null ? line.GetProperty("measuredValue").GetString()! : actual[i],
            measuredUnit = actual is null ? line.GetProperty("measuredUnit").GetString()! : "KG"
        }).ToArray()
    };

    public async Task<JsonElement> ConfirmArrivalAsync(JsonElement receipt) =>
        await SendAsync(Operator, HttpMethod.Post, $"/api/ledger/receiving/{receipt.GetProperty("id").GetGuid()}/measurements", MeasurementBody(receipt));

    public static Guid[] Lots(JsonElement receipt) => receipt.GetProperty("items").EnumerateArray().Select(line => line.GetProperty("lotId").GetGuid()).ToArray();
    public static Guid[] Lines(JsonElement receipt) => receipt.GetProperty("items").EnumerateArray().Select(line => line.GetProperty("id").GetGuid()).ToArray();

    public object OperationBody(Guid id, string kind, (Guid LotId, string Quantity)[] inputs, (string Stage, string Quantity)[] outputs, string loss = "0", Guid? ownerId = null, Guid? inputLocationId = null, Guid? outputLocationId = null, string? productIdentity = null) => new
    {
        id, ownerId = ownerId ?? OwnerId, facilityId = FacilityId, kind, occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1), equipmentId = EquipmentId,
        inputs = inputs.Select(input => new { lotId = input.LotId, locationId = inputLocationId ?? LocationId, quantityKg = input.Quantity }).ToArray(),
        outputs = outputs.Select(output => new { id = Guid.NewGuid(), code = "", commodityId = CommodityId, stage = output.Stage, productIdentity = productIdentity ?? output.Stage, locationId = outputLocationId ?? LocationId, quantityKg = output.Quantity }).ToArray(),
        declaredLossKg = loss
    };

    public async Task<JsonElement> DraftAsync(string kind, (Guid LotId, string Quantity)[] inputs, (string Stage, string Quantity)[] outputs, string loss = "0", string? productIdentity = null)
    {
        var id = Guid.NewGuid();
        await SendAsync(Operator, HttpMethod.Post, "/api/ledger/operations", OperationBody(id, kind, inputs, outputs, loss, productIdentity: productIdentity));
        return await GetAsync(Operator, $"/api/ledger/operations/{id}");
    }

    public async Task<JsonElement> UpdateDraftAsync(Guid id, object body)
    {
        var current = await GetAsync(Operator, $"/api/ledger/operations/{id}");
        var request = JsonSerializer.SerializeToNode(body)!.AsObject();
        request["version"] = current.GetProperty("version").GetString();
        return await SendAsync(Operator, HttpMethod.Put, $"/api/ledger/operations/{id}", request);
    }

    public async Task<JsonElement> CommitAsync(JsonElement operation) => await SendAsync(Operator, HttpMethod.Post, $"/api/ledger/operations/{operation.GetProperty("id").GetGuid()}/commit", new { });
    public static Guid Output(JsonElement operation, int index = 0) => operation.GetProperty("outputs")[index].GetProperty("id").GetGuid();
    public async Task<JsonElement> LotAsync(Guid id, HttpClient? client = null) => await GetAsync(client ?? Operator, $"/api/ledger/lots/{id}");
    public static decimal Quantity(JsonElement lot, string property = "quantityKg") => lot.GetProperty("positions").EnumerateArray().Sum(p => Decimal(p.GetProperty(property)));
    public static decimal Decimal(JsonElement value) { Assert.Equal(JsonValueKind.String, value.ValueKind); return decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture); }

    public async Task<JsonElement> TransferAsync(Guid lotId, string quantity, Guid? from = null, Guid? to = null, Guid? holdId = null) => await SendAsync(holdId is null ? Operator : Manager, HttpMethod.Post, "/api/ledger/transfers", new { id = Guid.NewGuid(), lotId, fromLocationId = from ?? LocationId, toLocationId = to ?? SecondLocationId, quantityKg = quantity, occurredAt = DateTimeOffset.UtcNow, holdId });
    public async Task<Guid> HoldAsync(Guid lotId)
    {
        var id = Guid.NewGuid();
        await SendAsync(Customer, HttpMethod.Post, "/api/ledger/holds", new { id, lotId, reason = "Synthetic customer HOLD" });
        return id;
    }


    public async Task<Guid> CorrectionAsync(JsonElement operation, JsonElement replacement)
    {
        var id = Guid.NewGuid();
        await SendAsync(Operator, HttpMethod.Post, "/api/ledger/corrections", new { id, operationId = operation.GetProperty("id").GetGuid(), reason = "Synthetic evidence-backed correction" });
        var reconciliation = operation.GetProperty("outputs").EnumerateArray().Select(o => new { lotId = o.GetProperty("id").GetGuid(), locationId = o.GetProperty("locationId").GetGuid(), quantityKg = o.GetProperty("quantityKg").GetString() }).ToArray();
        await SendAsync(Manager, HttpMethod.Post, $"/api/ledger/corrections/{id}/reconcile", new { evidenceId = EvidenceId, reconciliation, replacementOperationId = replacement.GetProperty("id").GetGuid(), replacementVersion = replacement.GetProperty("version").GetString(), reason = "Synthetic physical stock verified" });
        return id;
    }

    public static async Task CsrfAsync(HttpClient client)
    {
        var body = await GetAsync(client, "/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.GetProperty("token").GetString());
    }

    public static async Task<JsonElement> GetAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        Assert.True(response.IsSuccessStatusCode, $"GET {route}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    public static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string route, object body)
    {
        using var response = await RawAsync(client, method, route, body);
        Assert.True(response.IsSuccessStatusCode, $"{method} {route}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        var content = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(content) ? default : JsonSerializer.Deserialize<JsonElement>(content).Clone();
    }

    public static Task<HttpResponseMessage> RawAsync(HttpClient client, HttpMethod method, string route, object body) => client.SendAsync(new HttpRequestMessage(method, route) { Content = JsonContent.Create(body) });
    public static async Task RejectedAsync(HttpClient client, HttpMethod method, string route, object body, params HttpStatusCode[] expected)
    {
        using var response = await RawAsync(client, method, route, body);
        Assert.Contains(response.StatusCode, expected.Length == 0 ? new[] { HttpStatusCode.BadRequest, HttpStatusCode.Forbidden, HttpStatusCode.Conflict } : expected);
    }
    public void Dispose() { foreach (var account in Accounts.Values) account.Client.Dispose(); }
}
