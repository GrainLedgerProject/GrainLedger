using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GrainLedger.Modules.Identity;
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
    public Guid CommodityId { get; private set; } = Guid.NewGuid();
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
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/commodities", new { id = s.CommodityId, name = $"Synthetic paddy {s.CommodityId:N}" });
        foreach (var label in new[] { "operator", "manager" })
            await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/assignments", new { userId = s.Accounts[label].User.Id, facilityId = s.FacilityId });
        await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/evidence", new { id = s.EvidenceId, ownerId = s.OwnerId, facilityId = s.FacilityId, fileName = "synthetic-test-policy.txt", contentType = "text/plain", contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("SYNTHETIC TEST ONLY. Not factory-approved policy. Physical verification test evidence.")) });
        if (policies)
            foreach (var kind in new[] { "DRYING", "MILLING", "MERGE", "BLENDING", "BAGGING" }) await s.PolicyAsync(kind, "0");
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/capture-mock/farmers", new { id = s.FarmerId, ownerOrganizationId = s.OwnerId, name = "Synthetic source farmer", phoneNumber = "+84000000000" });
        var plotId = Guid.NewGuid();
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/capture-mock/plots", new { id = plotId, farmerId = s.FarmerId, name = "Synthetic source plot", latitude = "10.123456", longitude = "106.123456" });
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/capture-mock/seasons", new { id = s.SeasonId, ownerOrganizationId = s.OwnerId, name = "Synthetic season", startsOn = "2026-01-01", endsOn = "2026-12-31" });
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/capture-mock/plot-seasons", new { id = s.PlotSeasonId, plotId, seasonId = s.SeasonId });
        return s;
    }

    public async Task PolicyAsync(string kind, string tolerance) => await SendAsync(Admin, HttpMethod.Post, "/api/ledger/policies", new { id = Guid.NewGuid(), facilityId = FacilityId, kind, tolerancePercent = tolerance, effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1), evidenceId = EvidenceId });

    public object ReceiptBody(Guid id, string[] quantities, Guid? ownerId = null, Guid? locationId = null, string unit = "kg") => new
    {
        id, ownerOrganizationId = ownerId ?? OwnerId, farmerId = FarmerId, seasonId = SeasonId,
        locationId = locationId ?? LocationId, occurredAt = DateTimeOffset.UtcNow,
        lines = quantities.Select(quantity => new { id = Guid.NewGuid(), plotSeasonId = PlotSeasonId, commodityId = CommodityId, lotCode = $"SRC-{Guid.NewGuid():N}", quantity, unit }).ToArray()
    };

    public async Task<JsonElement> ReceiptAsync(params string[] quantities)
    {
        var id = Guid.NewGuid();
        await SendAsync(Purchaser, HttpMethod.Post, "/api/capture-mock/receipts", ReceiptBody(id, quantities));
        return await CommitReceiptAsync(id);
    }

    public async Task<JsonElement> ForeignReceiptAsync(string quantity, Guid? locationId = null)
    {
        var client = Accounts["foreignPurchaser"].Client;
        var farmer = Guid.NewGuid(); var plot = Guid.NewGuid(); var season = Guid.NewGuid(); var plotSeason = Guid.NewGuid(); var receiptId = Guid.NewGuid();
        await SendAsync(client, HttpMethod.Post, "/api/capture-mock/farmers", new { id = farmer, ownerOrganizationId = ForeignOwnerId, name = "Synthetic foreign farmer" });
        await SendAsync(client, HttpMethod.Post, "/api/capture-mock/plots", new { id = plot, farmerId = farmer, name = "Synthetic foreign plot", latitude = "10", longitude = "106" });
        await SendAsync(client, HttpMethod.Post, "/api/capture-mock/seasons", new { id = season, ownerOrganizationId = ForeignOwnerId, name = "Synthetic foreign season", startsOn = "2026-01-01", endsOn = "2026-12-31" });
        await SendAsync(client, HttpMethod.Post, "/api/capture-mock/plot-seasons", new { id = plotSeason, plotId = plot, seasonId = season });
        await SendAsync(client, HttpMethod.Post, "/api/capture-mock/receipts", new { id = receiptId, ownerOrganizationId = ForeignOwnerId, farmerId = farmer, seasonId = season, locationId = locationId ?? LocationId, occurredAt = DateTimeOffset.UtcNow, lines = new[] { new { id = Guid.NewGuid(), plotSeasonId = plotSeason, commodityId = CommodityId, lotCode = $"FOREIGN-{Guid.NewGuid():N}", quantity, unit = "kg" } } });
        var receipt = await GetAsync(client, $"/api/capture-mock/receipts/{receiptId}");
        await SendAsync(client, HttpMethod.Post, $"/api/capture-mock/receipts/{receiptId}/commit", new { commandId = Guid.NewGuid(), version = receipt.GetProperty("version").GetString() });
        return await GetAsync(client, $"/api/capture-mock/receipts/{receiptId}");
    }

    public async Task<JsonElement> CommitReceiptAsync(Guid id, Guid? commandId = null)
    {
        var receipt = await GetAsync(Purchaser, $"/api/capture-mock/receipts/{id}");
        await SendAsync(Purchaser, HttpMethod.Post, $"/api/capture-mock/receipts/{id}/commit", new { commandId = commandId ?? Guid.NewGuid(), version = receipt.GetProperty("version").GetString() });
        return await GetAsync(Purchaser, $"/api/capture-mock/receipts/{id}");
    }

    public static Guid[] Lots(JsonElement receipt) => receipt.GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("lotId").GetGuid()).ToArray();
    public static Guid[] Lines(JsonElement receipt) => receipt.GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("id").GetGuid()).ToArray();

    public object OperationBody(Guid id, string kind, (Guid LotId, string Quantity)[] inputs, (string Stage, string Quantity)[] outputs, string loss = "0", Guid? ownerId = null, Guid? inputLocationId = null, Guid? outputLocationId = null, string? productIdentity = null) => new
    {
        id, ownerId = ownerId ?? OwnerId, facilityId = FacilityId, kind, occurredAt = DateTimeOffset.UtcNow, equipmentId = EquipmentId,
        inputs = inputs.Select(input => new { lotId = input.LotId, locationId = inputLocationId ?? LocationId, quantityKg = input.Quantity }).ToArray(),
        outputs = outputs.Select(output => new { id = Guid.NewGuid(), code = $"OUT-{Guid.NewGuid():N}", commodityId = CommodityId, stage = output.Stage, productIdentity = productIdentity ?? output.Stage, locationId = outputLocationId ?? LocationId, quantityKg = output.Quantity }).ToArray(),
        declaredLossKg = loss
    };

    public async Task<JsonElement> DraftAsync(string kind, (Guid LotId, string Quantity)[] inputs, (string Stage, string Quantity)[] outputs, string loss = "0", string? productIdentity = null)
    {
        var id = Guid.NewGuid();
        await SendAsync(Operator, HttpMethod.Post, "/api/ledger/operations", OperationBody(id, kind, inputs, outputs, loss, productIdentity: productIdentity));
        return await GetAsync(Operator, $"/api/ledger/operations/{id}");
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

    public async Task<Guid> ArrivalAsync(JsonElement receipt, string quantity)
    {
        var id = Guid.NewGuid();
        await SendAsync(Operator, HttpMethod.Post, "/api/capture-mock/arrivals", new { id, receiptLineId = Lines(receipt)[0], quantity, unit = "kg", evidenceId = EvidenceId, observedAt = DateTimeOffset.UtcNow });
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
