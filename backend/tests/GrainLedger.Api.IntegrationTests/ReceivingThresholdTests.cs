using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using Npgsql;
using NpgsqlTypes;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ReceivingThresholdTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Settings = "/api/ledger/receiving-thresholds";
    private static string Company(Guid id) => $"{Settings}/companies/{id}";
    private static string Measurements(Guid id) => $"/api/ledger/receiving/{id}/measurements";
    private static Guid Id(JsonElement receipt) => receipt.GetProperty("id").GetGuid();
    private static JsonElement Snapshot(JsonElement detail, int index = 0) => detail.GetProperty("measurementSets")[index].GetProperty("threshold");
    private static Guid Version(JsonElement setting) => setting.GetProperty("version").GetGuid();
    private static JsonElement Override(JsonElement settings, Guid company) => settings.GetProperty("companyOverrides").EnumerateArray().Single(x => x.GetProperty("companyId").GetGuid() == company);
    private static object Review(JsonElement detail, string action, Guid evidence) => new
    {
        operationId = Guid.NewGuid(), expectedReceivingVersion = detail.GetProperty("receivingVersion").GetInt32(),
        measurementSetId = detail.GetProperty("latestMeasurementSetId").GetGuid(), action,
        reason = "Physical arrival reviewed against scoped evidence", evidenceId = evidence
    };

    private async Task<JsonElement> DefaultAsync(LedgerScenario s, string percentage)
    {
        var current = (await GetAsync(s.Admin, Settings)).GetProperty("systemDefault");
        await SendAsync(s.Admin, HttpMethod.Put, Settings + "/default", new { percentage, version = Version(current), reason = "Approved test transport policy" });
        return (await GetAsync(s.Admin, Settings)).GetProperty("systemDefault").Clone();
    }

    private static Task<JsonElement> RegisterAsync(LedgerScenario s, params string[] kg) => SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), kg));
    private static void AssertThreshold(JsonElement threshold, Guid company, decimal percentage, string source, Guid revision)
    {
        Assert.Equal(company, threshold.GetProperty("companyId").GetGuid());
        Assert.Equal(percentage, Decimal(threshold.GetProperty("percentage")));
        Assert.Equal(source, threshold.GetProperty("source").GetString());
        Assert.Equal(revision, threshold.GetProperty("revision").GetGuid());
    }

    [Theory]
    [InlineData("100", "RECEIVED_MATCHED", true)]
    [InlineData("99", "RECEIVED_WITHIN_TOLERANCE", true)]
    [InlineData("98", "RECEIVED_WITHIN_TOLERANCE", true)]
    [InlineData("97.999999", "AWAITING_MANAGER_REVIEW", false)]
    [InlineData("101", "AWAITING_MANAGER_REVIEW", false)]
    public async Task Exact_per_line_boundary_posts_only_actual_mass(string actual, string status, bool accepted)
    {
        using var s = await CreateAsync(factory);
        var policy = await DefaultAsync(s, "2");
        var receipt = await RegisterAsync(s, "100"); var id = Id(receipt);
        var body = MeasurementBody(receipt, actual: [actual]);
        var result = await SendAsync(s.Operator, HttpMethod.Post, Measurements(id), body);
        Assert.Equal(status, result.GetProperty("status").GetString());
        AssertThreshold(Snapshot(result), s.OwnerId, 2m, "SYSTEM_DEFAULT", Version(policy));
        Assert.Equal(accepted ? decimal.Parse(actual, CultureInfo.InvariantCulture) : 0m, Decimal(result.GetProperty("usableStockKg")));
        Assert.Empty(result.GetProperty("reviews").EnumerateArray());
        Assert.Equal(accepted ? 1L : 0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.lots WHERE receipt_id=@id", id));
        if (accepted)
        {
            Assert.Equal(decimal.Parse(actual, CultureInfo.InvariantCulture), Quantity(await s.LotAsync(Lots(receipt)[0])));
            var lotCode = receipt.GetProperty("items")[0].GetProperty("lotCode").GetString();
            Assert.StartsWith("RAW-", lotCode);
            Assert.Equal(lotCode, result.GetProperty("items")[0].GetProperty("lotCode").GetString());
            Assert.Equal(lotCode, (await s.LotAsync(Lots(receipt)[0])).GetProperty("code").GetString());
            Assert.Equal(lotCode, (await GetAsync(s.Operator, $"/api/ledger/receiving/{id}"))
                .GetProperty("items")[0].GetProperty("lotCode").GetString());
            Assert.Equal(decimal.Parse(actual, CultureInfo.InvariantCulture), await ScalarAsync<decimal>("SELECT quantity_kg FROM ledger.contributions WHERE lot_id=@id", Lots(receipt)[0]));
            var source = await s.Purchaser.GetFromJsonAsync<SourceLotResponse>($"/api/source-lots/{receipt.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid()}");
            Assert.Equal("100", source!.InitialNetMassKg);
            Assert.Equal(decimal.Parse(actual, CultureInfo.InvariantCulture), decimal.Parse(source.CurrentStock.Single().OnHandKg, CultureInfo.InvariantCulture));
        }
        var projected = await GetAsync(s.Purchaser, $"/api/procurements/{id}");
        Assert.Equal(status, projected.GetProperty("status").GetString());
        Assert.Equal("COMMITTED", await ScalarAsync<string>("SELECT status FROM capture.procurements WHERE id=@id", id));
        var queue = await GetAsync(s.Operator, $"/api/ledger/receiving-tasks?facilityId={s.FacilityId}&status={status}");
        Assert.Contains(queue.GetProperty("items").EnumerateArray(), x => Id(x) == id);
        await DefaultAsync(s, "3");
        var replay = await SendAsync(s.Operator, HttpMethod.Post, Measurements(id), body);
        Assert.Equal(result.GetRawText(), replay.GetRawText());
        var persisted = await GetAsync(s.Operator, $"/api/ledger/receiving/{id}");
        AssertThreshold(Snapshot(persisted), s.OwnerId, 2m, "SYSTEM_DEFAULT", Version(policy));
        var changed = JsonSerializer.SerializeToNode(body)!.AsObject(); changed["observations"] = "Changed replay";
        await RejectedAsync(s.Operator, HttpMethod.Post, Measurements(id), changed, HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("97", "900", false)]
    [InlineData("101", "899", true)]
    public async Task Delivery_average_and_surplus_cannot_hide_offending_line(string first, string second, bool surplus)
    {
        using var s = await CreateAsync(factory); await DefaultAsync(s, "2");
        var config = await GetAsync(s.Admin, Settings);
        var history = await GetAsync(s.Admin, Settings + "/history?page=1&pageSize=100");
        var receipt = await RegisterAsync(s, "100", "900"); var id = Id(receipt);
        var result = await SendAsync(s.Operator, HttpMethod.Post, Measurements(id), MeasurementBody(receipt, actual: [first, second]));
        Assert.Equal("AWAITING_MANAGER_REVIEW", result.GetProperty("status").GetString());
        Assert.Equal(0m, Decimal(result.GetProperty("usableStockKg")));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.lots WHERE receipt_id=@id", id));
        if (surplus)
            await RejectedAsync(s.Manager, HttpMethod.Post, $"/api/ledger/receiving/{id}/reviews", Review(result, "APPROVE_SHORTAGE", s.EvidenceId), HttpStatusCode.Conflict);
        else
        {
            var approved = await SendAsync(s.Manager, HttpMethod.Post, $"/api/ledger/receiving/{id}/reviews", Review(result, "APPROVE_SHORTAGE", s.EvidenceId));
            Assert.Equal("RECEIVED_SHORTAGE_APPROVED", approved.GetProperty("status").GetString());
            Assert.Equal(997m, Decimal(approved.GetProperty("usableStockKg")));
        }
        Assert.Equal(config.GetRawText(), (await GetAsync(s.Admin, Settings)).GetRawText());
        Assert.Equal(history.GetRawText(), (await GetAsync(s.Admin, Settings + "/history?page=1&pageSize=100")).GetRawText());
    }

    [Fact]
    public async Task Normalized_units_keep_microkg_precision_and_measured_stock()
    {
        using var s = await CreateAsync(factory); await DefaultAsync(s, "2");
        var receipt = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), ["0.1"], unit: "T"));
        var body = JsonSerializer.SerializeToNode(MeasurementBody(receipt, actual: ["98000"]))!.AsObject();
        body["items"]![0]!["measuredUnit"] = "G";
        var result = await SendAsync(s.Operator, HttpMethod.Post, Measurements(Id(receipt)), body);
        Assert.Equal("RECEIVED_WITHIN_TOLERANCE", result.GetProperty("status").GetString());
        Assert.Equal(98m, Quantity(await s.LotAsync(Lots(receipt)[0])));
        Assert.Equal("G", result.GetProperty("measurementSets")[0].GetProperty("items")[0].GetProperty("measuredUnit").GetString());
        Assert.Equal(98000m, Decimal(result.GetProperty("measurementSets")[0].GetProperty("items")[0].GetProperty("measuredValue")));
        var beyond = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), ["100000"], unit: "G"));
        var micro = JsonSerializer.SerializeToNode(MeasurementBody(beyond, actual: ["0.097999999"]))!.AsObject();
        micro["items"]![0]!["measuredUnit"] = "T";
        var blocked = await SendAsync(s.Operator, HttpMethod.Post, Measurements(Id(beyond)), micro);
        Assert.Equal("AWAITING_MANAGER_REVIEW", blocked.GetProperty("status").GetString());
        Assert.Equal(97.999999m, Decimal(blocked.GetProperty("items")[0].GetProperty("actualKg")));
    }

    [Fact]
    public async Task Company_precedence_zero_removal_and_persisted_owner_ignore_client_policy()
    {
        using var s = await CreateAsync(factory); var initial = await DefaultAsync(s, "2");
        await SendAsync(s.Admin, HttpMethod.Post, Company(s.ForeignOwnerId), new { percentage = "3", reason = "Foreign company agreement" });
        var foreign = Override(await GetAsync(s.Admin, Settings), s.ForeignOwnerId);
        var source = await s.RegisterSourceAsync(s.Accounts["foreignPurchaser"].Client, s.ForeignOwnerId);
        var a = await RegisterAsync(s, "100");
        var b = await SendAsync(s.Accounts["foreignPurchaser"].Client, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), ["100"], farmerId: source.Farmer, plotSeasonId: source.PlotSeason));
        var forged = JsonSerializer.SerializeToNode(MeasurementBody(a, actual: ["97.5"]))!.AsObject();
        forged["companyId"] = s.ForeignOwnerId; forged["percentage"] = "99";
        forged["threshold"] = JsonSerializer.SerializeToNode(new { companyId = s.ForeignOwnerId, percentage = "99", source = "COMPANY_OVERRIDE", revision = Version(foreign) });
        var aResult = await SendAsync(s.Operator, HttpMethod.Post, Measurements(Id(a)), forged);
        var bResult = await SendAsync(s.Operator, HttpMethod.Post, Measurements(Id(b)), MeasurementBody(b, actual: ["97.5"]));
        Assert.Equal("AWAITING_MANAGER_REVIEW", aResult.GetProperty("status").GetString());
        Assert.Equal("RECEIVED_WITHIN_TOLERANCE", bResult.GetProperty("status").GetString());
        AssertThreshold(Snapshot(aResult), s.OwnerId, 2m, "SYSTEM_DEFAULT", Version(initial));
        AssertThreshold(Snapshot(bResult), s.ForeignOwnerId, 3m, "COMPANY_OVERRIDE", Version(foreign));
        await SendAsync(s.Admin, HttpMethod.Post, Company(s.OwnerId), new { percentage = "0", reason = "Exact match contract" });
        var zero = Override(await GetAsync(s.Admin, Settings), s.OwnerId);
        foreach (var (actual, status) in new[] { ("100", "RECEIVED_MATCHED"), ("99.999999", "AWAITING_MANAGER_REVIEW") })
        {
            var receipt = await RegisterAsync(s, "100");
            var measured = await SendAsync(s.Operator, HttpMethod.Post, Measurements(Id(receipt)), MeasurementBody(receipt, actual: [actual]));
            Assert.Equal(status, measured.GetProperty("status").GetString());
            AssertThreshold(Snapshot(measured), s.OwnerId, 0m, "COMPANY_OVERRIDE", Version(zero));
        }
        var latest = await DefaultAsync(s, "4");
        AssertThreshold(await GetAsync(s.Customer, Settings + "/own-company"), s.OwnerId, 0m, "COMPANY_OVERRIDE", Version(zero));
        AssertThreshold(await GetAsync(s.ForeignCustomer, Settings + "/own-company"), s.ForeignOwnerId, 3m, "COMPANY_OVERRIDE", Version(foreign));
        var stillOverridden = await SendAsync(s.Accounts["foreignPurchaser"].Client, HttpMethod.Post, "/api/procurements",
            s.ReceiptBody(Guid.NewGuid(), ["100"], farmerId: source.Farmer, plotSeasonId: source.PlotSeason));
        var stillReview = await SendAsync(s.Operator, HttpMethod.Post, Measurements(Id(stillOverridden)), MeasurementBody(stillOverridden, actual: ["96.5"]));
        Assert.Equal("AWAITING_MANAGER_REVIEW", stillReview.GetProperty("status").GetString());
        AssertThreshold(Snapshot(stillReview), s.ForeignOwnerId, 3m, "COMPANY_OVERRIDE", Version(foreign));
        await SendAsync(s.Admin, HttpMethod.Delete, Company(s.ForeignOwnerId), new { version = Version(foreign), reason = "Agreement removed" });
        AssertThreshold(await GetAsync(s.ForeignCustomer, Settings + "/own-company"), s.ForeignOwnerId, 4m, "SYSTEM_DEFAULT", Version(latest));
        var fallback = await SendAsync(s.Accounts["foreignPurchaser"].Client, HttpMethod.Post, "/api/procurements",
            s.ReceiptBody(Guid.NewGuid(), ["100"], farmerId: source.Farmer, plotSeasonId: source.PlotSeason));
        var fallbackResult = await SendAsync(s.Operator, HttpMethod.Post, Measurements(Id(fallback)), MeasurementBody(fallback, actual: ["96.5"]));
        Assert.Equal("RECEIVED_WITHIN_TOLERANCE", fallbackResult.GetProperty("status").GetString());
        AssertThreshold(Snapshot(fallbackResult), s.ForeignOwnerId, 4m, "SYSTEM_DEFAULT", Version(latest));
        var old = await GetAsync(s.Operator, $"/api/ledger/receiving/{Id(b)}");
        AssertThreshold(Snapshot(old), s.ForeignOwnerId, 3m, "COMPANY_OVERRIDE", Version(foreign));
        Assert.DoesNotContain("Foreign company agreement", old.GetRawText());
    }

    [Fact]
    public async Task Strict_percentage_contract_rejects_without_config_or_history_changes()
    {
        using var s = await CreateAsync(factory); await DefaultAsync(s, "0");
        await SendAsync(s.Admin, HttpMethod.Post, Company(s.OwnerId), new { percentage = "2.123456", reason = "Six place agreement" });
        Assert.Equal(2.123456m, Decimal(Override(await GetAsync(s.Admin, Settings), s.OwnerId).GetProperty("percentage")));
        var before = await GetAsync(s.Admin, Settings);
        var history = await GetAsync(s.Admin, Settings + "/history?page=1&pageSize=100");
        object?[] invalid = ["-0.000001", "-1", "100", "100.000001", "NaN", "Infinity", "-Infinity", "1e0", "2E-1", "2.0000001", "99.9999999", "", " ", "abc", "2,5", " 2", "2 ", null, 2, 2.5m, true, new object[0], new { value = "2" }];
        foreach (var percentage in invalid)
        {
            var reason = "Invalid input must leave no history";
            await RejectedAsync(s.Admin, HttpMethod.Put, Settings + "/default", new { percentage, version = Version(before.GetProperty("systemDefault")), reason }, HttpStatusCode.BadRequest);
            await RejectedAsync(s.Admin, HttpMethod.Put, Company(s.OwnerId), new { percentage, version = Version(Override(before, s.OwnerId)), reason }, HttpStatusCode.BadRequest);
            await RejectedAsync(s.Admin, HttpMethod.Post, Company(s.ForeignOwnerId), new { percentage, reason }, HttpStatusCode.BadRequest);
        }
        foreach (var reason in new string?[] { null, "", "   " })
            await RejectedAsync(s.Admin, HttpMethod.Put, Settings + "/default", new { percentage = "2", version = Version(before.GetProperty("systemDefault")), reason }, HttpStatusCode.BadRequest);
        foreach (var version in new object?[] { null, 2, "", "not-a-uuid" })
        {
            await RejectedAsync(s.Admin, HttpMethod.Put, Settings + "/default", new { percentage = "2", version, reason = "Invalid version" }, HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
            await RejectedAsync(s.Admin, HttpMethod.Delete, Company(s.OwnerId), new { version, reason = "Invalid version" }, HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        }
        Assert.Equal(before.GetRawText(), (await GetAsync(s.Admin, Settings)).GetRawText());
        Assert.Equal(history.GetRawText(), (await GetAsync(s.Admin, Settings + "/history?page=1&pageSize=100")).GetRawText());
        var highest = await DefaultAsync(s, "99.999999");
        Assert.Equal(99.999999m, Decimal(highest.GetProperty("percentage")));
    }

    [Fact]
    public async Task Company_reads_are_narrow_and_all_mutations_require_persisted_admin()
    {
        using var s = await CreateAsync(factory); var policy = await DefaultAsync(s, "2");
        var noStock = await factory.SeedOrganizationAsync(OrganizationKind.Customer, "Unreceived threshold customer " + Guid.NewGuid().ToString("N"));
        var choices = await GetAsync(s.Admin, Settings + "/companies?search=" + Uri.EscapeDataString(noStock.Name) + "&page=1&pageSize=1");
        Assert.Equal(noStock.Id, Assert.Single(choices.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(1, choices.GetProperty("totalCount").GetInt32());
        Assert.False(choices.GetProperty("hasMore").GetBoolean());
        await SendAsync(s.Admin, HttpMethod.Post, Company(noStock.Id), new { companyId = s.OwnerId, percentage = "3", reason = "Private agreement note" });
        var before = await GetAsync(s.Admin, Settings);
        var history = await GetAsync(s.Admin, Settings + "/history?page=1&pageSize=100");
        foreach (var client in new[] { s.Customer, s.Purchaser, s.ForeignCustomer, s.Operator, s.Manager })
        {
            foreach (var route in new[] { Settings, Settings + "/history", Settings + "/companies" })
            {
                using var denied = await client.GetAsync(route); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            }
            await RejectedAsync(client, HttpMethod.Put, Settings + "/default", new { percentage = "4", version = Version(policy), reason = "Not administrator" }, HttpStatusCode.Forbidden);
            await RejectedAsync(client, HttpMethod.Post, Company(s.OwnerId), new { percentage = "4", reason = "Not administrator" }, HttpStatusCode.Forbidden);
            await RejectedAsync(client, HttpMethod.Put, Company(noStock.Id), new { percentage = "4", version = Version(Override(await GetAsync(s.Admin, Settings), noStock.Id)), reason = "Not administrator" }, HttpStatusCode.Forbidden);
            await RejectedAsync(client, HttpMethod.Delete, Company(noStock.Id), new { version = Version(Override(await GetAsync(s.Admin, Settings), noStock.Id)), reason = "Not administrator" }, HttpStatusCode.Forbidden);
        }
        Assert.Equal(before.GetRawText(), (await GetAsync(s.Admin, Settings)).GetRawText());
        Assert.Equal(history.GetRawText(), (await GetAsync(s.Admin, Settings + "/history?page=1&pageSize=100")).GetRawText());
        foreach (var client in new[] { s.Customer, s.Purchaser })
        {
            var own = await GetAsync(client, Settings + $"/own-company?companyId={noStock.Id}");
            AssertThreshold(own, s.OwnerId, 2m, "SYSTEM_DEFAULT", Version(policy));
            Assert.DoesNotContain("Private agreement note", own.GetRawText());
            Assert.DoesNotContain(noStock.Id.ToString(), own.GetRawText());
        }
        foreach (var client in new[] { s.Operator, s.Manager, s.Admin, s.Accounts["pending"].Client })
        {
            using var denied = await client.GetAsync(Settings + "/own-company");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        var (exporter, exporterSession) = await factory.SeedUserAsync(Roles.ExportStaff, s.OwnerId);
        using var exportClient = factory.CreateRealCookieClient(exporter, exporterSession);
        AssertThreshold(await GetAsync(exportClient, Settings + "/own-company"), s.OwnerId, 2m, "SYSTEM_DEFAULT", Version(policy));
        var (secondAdmin, session) = await factory.SeedUserAsync(Roles.Administrator, s.Accounts["admin"].User.OrganizationId);
        using var admin = factory.CreateRealCookieClient(secondAdmin, session); await CsrfAsync(admin);
        await SendAsync(admin, HttpMethod.Put, Company(noStock.Id), new { percentage = "4", version = Version(Override(await GetAsync(s.Admin, Settings), noStock.Id)), reason = "Any authorized administrator" });
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        await RejectedAsync(s.Admin, HttpMethod.Post, Company(mill.Id), new { percentage = "2", reason = "Wrong organization type" });
        await RejectedAsync(s.Admin, HttpMethod.Post, Company(Guid.NewGuid()), new { percentage = "2", reason = "Missing organization" });
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483647&pageSize=100")]
    public async Task Configuration_history_and_company_lookup_enforce_bounded_pages(string query)
    {
        using var s = await CreateAsync(factory);
        foreach (var route in new[] { Settings + "/history", Settings + "/companies" })
        {
            using var response = await s.Admin.GetAsync(route + "?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task History_lifecycle_records_actor_server_time_and_nonreused_revisions()
    {
        using var s = await CreateAsync(factory); var policy = await DefaultAsync(s, "2");
        var initialized = await GetAsync(s.Admin, Settings + "/history?scope=SYSTEM_DEFAULT&page=1&pageSize=100");
        var init = Assert.Single(initialized.GetProperty("items").EnumerateArray(), x => x.GetProperty("action").GetString() == "INITIALIZE");
        Assert.Equal(JsonValueKind.Null, init.GetProperty("actorId").ValueKind);
        Assert.Equal(JsonValueKind.Null, init.GetProperty("oldPercentage").ValueKind);
        Assert.Equal(2m, Decimal(init.GetProperty("newPercentage")));
        Assert.Equal("20261019000000_ReceivingLossThreshold", init.GetProperty("migrationId").GetString());
        Assert.NotEqual(default, init.GetProperty("recordedAt").GetDateTimeOffset());
        policy = await DefaultAsync(s, "3");
        var defaults = await GetAsync(s.Admin, Settings + "/history?scope=SYSTEM_DEFAULT&page=1&pageSize=100");
        var changedDefault = Assert.Single(defaults.GetProperty("items").EnumerateArray(), x => x.GetProperty("revision").GetGuid() == Version(policy));
        Assert.Equal("UPDATE", changedDefault.GetProperty("action").GetString());
        Assert.Equal(2m, Decimal(changedDefault.GetProperty("oldPercentage")));
        Assert.Equal(3m, Decimal(changedDefault.GetProperty("newPercentage")));
        Assert.Equal(s.Accounts["admin"].User.Id, changedDefault.GetProperty("actorId").GetGuid());
        Assert.Equal("Approved test transport policy", changedDefault.GetProperty("reason").GetString());
        var start = DateTimeOffset.UtcNow;
        var forged = new { percentage = "3", reason = "Initial agreement", actorId = s.Accounts["purchaser"].User.Id, recordedAt = "2000-01-01T00:00:00Z" };
        await SendAsync(s.Admin, HttpMethod.Post, Company(s.OwnerId), forged);
        var first = Override(await GetAsync(s.Admin, Settings), s.OwnerId);
        await SendAsync(s.Admin, HttpMethod.Put, Company(s.OwnerId), new { percentage = "4", version = Version(first), reason = "Revised agreement" });
        var updated = Override(await GetAsync(s.Admin, Settings), s.OwnerId);
        await SendAsync(s.Admin, HttpMethod.Delete, Company(s.OwnerId), new { version = Version(updated), reason = "Agreement withdrawn" });
        await SendAsync(s.Admin, HttpMethod.Post, Company(s.OwnerId), new { percentage = "3", reason = "New agreement after removal" });
        var recreated = Override(await GetAsync(s.Admin, Settings), s.OwnerId);
        Assert.NotEqual(Version(first), Version(recreated));
        await RejectedAsync(s.Admin, HttpMethod.Put, Company(s.OwnerId), new { percentage = "5", version = Version(first), reason = "Stale ABA edit" }, HttpStatusCode.Conflict);
        await RejectedAsync(s.Admin, HttpMethod.Delete, Company(s.OwnerId), new { version = Version(first), reason = "Stale ABA remove" }, HttpStatusCode.Conflict);
        var page = await GetAsync(s.Admin, Settings + $"/history?companyId={s.OwnerId}&scope=COMPANY_OVERRIDE&page=1&pageSize=2");
        var next = await GetAsync(s.Admin, Settings + $"/history?companyId={s.OwnerId}&scope=COMPANY_OVERRIDE&page=2&pageSize=2");
        Assert.Equal(4, page.GetProperty("totalCount").GetInt32()); Assert.True(page.GetProperty("hasMore").GetBoolean());
        Assert.False(next.GetProperty("hasMore").GetBoolean());
        var history = page.GetProperty("items").EnumerateArray().Concat(next.GetProperty("items").EnumerateArray()).OrderBy(x => x.GetProperty("recordedAt").GetDateTimeOffset()).ToArray();
        Assert.Equal(4, history.Select(x => x.GetProperty("revision").GetGuid()).Distinct().Count());
        Assert.Equal(new[] { "CREATE", "UPDATE", "DELETE", "CREATE" }, history.Select(x => x.GetProperty("action").GetString()));
        decimal?[] oldValues = [null, 3m, 4m, null]; decimal?[] newValues = [3m, 4m, null, 3m];
        string[] reasons = ["Initial agreement", "Revised agreement", "Agreement withdrawn", "New agreement after removal"];
        for (var i = 0; i < history.Length; i++)
        {
            var entry = history[i];
            Assert.Equal(s.OwnerId, entry.GetProperty("companyId").GetGuid());
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("companyName").GetString()));
            Assert.Equal("COMPANY_OVERRIDE", entry.GetProperty("scope").GetString());
            Assert.Equal(oldValues[i], entry.GetProperty("oldPercentage").ValueKind == JsonValueKind.Null ? (decimal?)null : Decimal(entry.GetProperty("oldPercentage")));
            Assert.Equal(newValues[i], entry.GetProperty("newPercentage").ValueKind == JsonValueKind.Null ? (decimal?)null : Decimal(entry.GetProperty("newPercentage")));
            Assert.Equal(s.Accounts["admin"].User.Id, entry.GetProperty("actorId").GetGuid());
            Assert.Equal(s.Accounts["admin"].User.DisplayName, entry.GetProperty("actorName").GetString());
            Assert.InRange(entry.GetProperty("recordedAt").GetDateTimeOffset(), start, DateTimeOffset.UtcNow);
            Assert.Equal(reasons[i], entry.GetProperty("reason").GetString());
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("migrationId").ValueKind);
        }
        Assert.Equal(Version(first), history[0].GetProperty("revision").GetGuid());
        Assert.Equal(Version(updated), history[1].GetProperty("revision").GetGuid());
        Assert.Equal(Version(recreated), history[3].GetProperty("revision").GetGuid());
        Assert.Equal(Version(policy), Version((await GetAsync(s.Admin, Settings)).GetProperty("systemDefault")));
        Assert.Equal(recreated.GetRawText(), Override(await GetAsync(s.Admin, Settings), s.OwnerId).GetRawText());
    }

    [Fact]
    public async Task Concurrent_creates_and_edits_have_one_winner_and_one_history_event()
    {
        using var s = await CreateAsync(factory); await DefaultAsync(s, "2");
        await RaceAsync(
            RawAsync(s.Admin, HttpMethod.Post, Company(s.OwnerId), new { percentage = "3", reason = "Create A" }),
            RawAsync(s.Admin, HttpMethod.Post, Company(s.OwnerId), new { percentage = "4", reason = "Create B" }));
        var first = Override(await GetAsync(s.Admin, Settings), s.OwnerId);
        await RaceAsync(
            RawAsync(s.Admin, HttpMethod.Put, Company(s.OwnerId), new { percentage = "5", version = Version(first), reason = "Edit A" }),
            RawAsync(s.Admin, HttpMethod.Put, Company(s.OwnerId), new { percentage = "6", version = Version(first), reason = "Edit B" }));
        var latest = Override(await GetAsync(s.Admin, Settings), s.OwnerId);
        Assert.NotEqual(Version(first), Version(latest));
        var history = await GetAsync(s.Admin, Settings + $"/history?companyId={s.OwnerId}&page=1&pageSize=10");
        Assert.Equal(2, history.GetProperty("totalCount").GetInt32());
        Assert.Contains(history.GetProperty("items").EnumerateArray(), x => x.GetProperty("revision").GetGuid() == Version(first));
        Assert.Contains(history.GetProperty("items").EnumerateArray(), x => x.GetProperty("revision").GetGuid() == Version(latest));
        var defaultBefore = (await GetAsync(s.Admin, Settings)).GetProperty("systemDefault");
        var defaultHistoryBefore = await GetAsync(s.Admin, Settings + "/history?scope=SYSTEM_DEFAULT&page=1&pageSize=100");
        await RaceAsync(
            RawAsync(s.Admin, HttpMethod.Put, Settings + "/default", new { percentage = "3", version = Version(defaultBefore), reason = "Default A" }),
            RawAsync(s.Admin, HttpMethod.Put, Settings + "/default", new { percentage = "4", version = Version(defaultBefore), reason = "Default B" }));
        var defaultHistoryAfter = await GetAsync(s.Admin, Settings + "/history?scope=SYSTEM_DEFAULT&page=1&pageSize=100");
        Assert.Equal(defaultHistoryBefore.GetProperty("totalCount").GetInt32() + 1, defaultHistoryAfter.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Pending_review_keeps_snapshot_remeasurement_uses_current_policy()
    {
        using var s = await CreateAsync(factory); var oldPolicy = await DefaultAsync(s, "2");
        var receipt = await RegisterAsync(s, "100"); var id = Id(receipt);
        var body = MeasurementBody(receipt, actual: ["97.5"]);
        var first = await SendAsync(s.Operator, HttpMethod.Post, Measurements(id), body);
        var settings = await DefaultAsync(s, "3");
        var pending = await GetAsync(s.Operator, $"/api/ledger/receiving/{id}");
        Assert.Equal("AWAITING_MANAGER_REVIEW", pending.GetProperty("status").GetString());
        Assert.Equal(0m, Decimal(pending.GetProperty("usableStockKg")));
        AssertThreshold(Snapshot(pending), s.OwnerId, 2m, "SYSTEM_DEFAULT", Version(oldPolicy));
        Assert.Equal(first.GetRawText(), (await SendAsync(s.Operator, HttpMethod.Post, Measurements(id), body)).GetRawText());
        var blocked = await SendAsync(s.Manager, HttpMethod.Post, $"/api/ledger/receiving/{id}/reviews", Review(pending, "KEEP_BLOCKED", s.EvidenceId));
        Assert.Equal("BLOCKED_UNRESOLVED", blocked.GetProperty("status").GetString());
        var requested = await SendAsync(s.Manager, HttpMethod.Post, $"/api/ledger/receiving/{id}/reviews", Review(blocked, "REQUEST_REMEASUREMENT", s.EvidenceId));
        var remeasure = JsonSerializer.SerializeToNode(MeasurementBody(receipt, version: requested.GetProperty("receivingVersion").GetInt32(), actual: ["97.5"]))!.AsObject();
        remeasure["capturedAt"] = "2000-01-01T00:00:00Z";
        var accepted = await SendAsync(s.Operator, HttpMethod.Post, Measurements(id), remeasure);
        Assert.Equal("RECEIVED_WITHIN_TOLERANCE", accepted.GetProperty("status").GetString());
        Assert.Equal(97.5m, Decimal(accepted.GetProperty("usableStockKg")));
        AssertThreshold(Snapshot(accepted), s.OwnerId, 2m, "SYSTEM_DEFAULT", Version(oldPolicy));
        AssertThreshold(Snapshot(accepted, 1), s.OwnerId, 3m, "SYSTEM_DEFAULT", Version(settings));
        Assert.Equal(2, accepted.GetProperty("reviews").GetArrayLength());
        Assert.Equal(Version(settings), Version((await GetAsync(s.Admin, Settings)).GetProperty("systemDefault")));
    }

    [Fact]
    public async Task Measurement_and_configuration_serialize_in_both_orders_without_mixed_snapshots()
    {
        using var s = await CreateAsync(factory); var policy = await DefaultAsync(s, "2");
        var first = await RegisterAsync(s, "100");
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await using var takeLock = new NpgsqlCommand("SELECT ledger.lock_writes()", owner, tx); await takeLock.ExecuteNonQueryAsync();
            var measurement = SendAsync(s.Operator, HttpMethod.Post, Measurements(Id(first)), MeasurementBody(first, actual: ["97.5"]));
            await WaitForWriteWaiterAsync("arrival_command");
            await RoutineAsync(owner, tx, "ledger.receiving_threshold_command(@actor,'default',@body)", s.Accounts["admin"].User.Id,
                new { percentage = "3", version = Version(policy), reason = "Configuration wins serialization" });
            await tx.CommitAsync();
            var result = await measurement;
            var current = (await GetAsync(s.Admin, Settings)).GetProperty("systemDefault");
            AssertThreshold(Snapshot(result), s.OwnerId, 3m, "SYSTEM_DEFAULT", Version(current));
            Assert.Equal("RECEIVED_WITHIN_TOLERANCE", result.GetProperty("status").GetString());
        }
        policy = await DefaultAsync(s, "2");
        var second = await RegisterAsync(s, "100");
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await using var takeLock = new NpgsqlCommand("SELECT ledger.lock_writes()", owner, tx); await takeLock.ExecuteNonQueryAsync();
            var edit = SendAsync(s.Admin, HttpMethod.Put, Settings + "/default", new { percentage = "3", version = Version(policy), reason = "Measurement wins serialization" });
            await WaitForWriteWaiterAsync("receiving_threshold_command");
            var measured = await RoutineAsync(owner, tx, "ledger.arrival_command(@actor,@id,'measurements',@body)", s.Accounts["operator"].User.Id,
                MeasurementBody(second, actual: ["97.5"]), Id(second));
            await tx.CommitAsync(); await edit;
            Assert.Equal("AWAITING_MANAGER_REVIEW", measured.GetProperty("status").GetString());
            var result = await GetAsync(s.Operator, $"/api/ledger/receiving/{Id(second)}");
            AssertThreshold(Snapshot(result), s.OwnerId, 2m, "SYSTEM_DEFAULT", Version(policy));
            Assert.Equal(0m, Decimal(result.GetProperty("usableStockKg")));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_invalid_default_fails_closed_but_preserves_saved_replays(bool invalid)
    {
        using var s = await CreateAsync(factory); await DefaultAsync(s, "2");
        var receipt = await RegisterAsync(s, "100"); var id = Id(receipt);
        var savedReceipt = await RegisterAsync(s, "100"); var savedBody = MeasurementBody(savedReceipt);
        var saved = await SendAsync(s.Operator, HttpMethod.Post, Measurements(Id(savedReceipt)), savedBody);
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        // Test-only fault injection; restore same immutable revision rather than manufacturing policy.
        await using var read = new NpgsqlCommand("SELECT percentage,revision FROM ledger.receiving_threshold_default", owner);
        decimal percentage; Guid revision;
        await using (var row = await read.ExecuteReaderAsync()) { Assert.True(await row.ReadAsync()); percentage = row.GetDecimal(0); revision = row.GetGuid(1); }
        await using var fault = new NpgsqlCommand(invalid
            ? "UPDATE ledger.receiving_threshold_default SET percentage=99"
            : "DELETE FROM ledger.receiving_threshold_default", owner);
        await fault.ExecuteNonQueryAsync();
        try
        {
            using var response = await RawAsync(s.Operator, HttpMethod.Post, Measurements(id), MeasurementBody(receipt));
            Assert.False(response.IsSuccessStatusCode);
            Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.arrival_measurement_sets WHERE procurement_id=@id", id));
            Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.lots WHERE receipt_id=@id", id));
            Assert.Equal("PENDING_ARRIVAL", await ScalarAsync<string>("SELECT status FROM ledger.receiving WHERE procurement_id=@id", id));
            Assert.Equal(saved.GetRawText(), (await SendAsync(s.Operator, HttpMethod.Post, Measurements(Id(savedReceipt)), savedBody)).GetRawText());
        }
        finally
        {
            await using var restore = new NpgsqlCommand("INSERT INTO ledger.receiving_threshold_default(singleton,percentage,revision) VALUES(true,@percentage,@revision) ON CONFLICT(singleton) DO UPDATE SET percentage=EXCLUDED.percentage,revision=EXCLUDED.revision", owner);
            restore.Parameters.AddWithValue("percentage", percentage); restore.Parameters.AddWithValue("revision", revision); await restore.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Runtime_direct_writes_and_unauthorized_protected_commands_are_denied()
    {
        using var s = await CreateAsync(factory); var policy = await DefaultAsync(s, "2");
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
        foreach (var sql in new[]
        {
            "SELECT * FROM ledger.receiving_threshold_default",
            "SELECT * FROM ledger.receiving_threshold_overrides",
            "SELECT * FROM ledger.receiving_threshold_history",
            "UPDATE ledger.receiving_threshold_default SET percentage=99",
            "DELETE FROM ledger.receiving_threshold_default",
            "INSERT INTO ledger.receiving_threshold_default(singleton,percentage,revision) VALUES(false,99,gen_random_uuid())",
            "INSERT INTO ledger.receiving_threshold_overrides(company_id,percentage,revision) VALUES(@company,99,gen_random_uuid())",
            "UPDATE ledger.receiving_threshold_overrides SET percentage=99",
            "DELETE FROM ledger.receiving_threshold_overrides",
            "INSERT INTO ledger.receiving_threshold_history(revision,scope,action,new_percentage,reason) VALUES(gen_random_uuid(),'SYSTEM_DEFAULT','UPDATE',99,'forged')",
            "UPDATE ledger.receiving_threshold_history SET reason='forged'",
            "DELETE FROM ledger.receiving_threshold_history"
        })
        {
            await using var command = new NpgsqlCommand(sql, runtime); command.Parameters.AddWithValue("company", s.OwnerId);
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
        var before = await GetAsync(s.Admin, Settings); var history = await GetAsync(s.Admin, Settings + "/history?page=1&pageSize=100");
        foreach (var label in new[] { "purchaser", "customer", "operator", "manager", "pending" })
        {
            await using var command = new NpgsqlCommand("SELECT ledger.receiving_threshold_command(@actor,'default',@body)", runtime);
            command.Parameters.AddWithValue("actor", s.Accounts[label].User.Id);
            command.Parameters.AddWithValue("body", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(new { percentage = "99", version = Version(policy), reason = "Unauthorized DB call" }));
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
        Assert.Equal(before.GetRawText(), (await GetAsync(s.Admin, Settings)).GetRawText());
        Assert.Equal(history.GetRawText(), (await GetAsync(s.Admin, Settings + "/history?page=1&pageSize=100")).GetRawText());
    }

    [Theory]
    [InlineData("receiving_threshold_history", "reason")]
    [InlineData("receiving_threshold_overrides", "percentage")]
    public async Task Runtime_validation_rejects_inherited_column_only_threshold_reads(string table, string column)
    {
        var group = "threshold_column_" + Guid.NewGuid().ToString("N");
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE ROLE {group} NOLOGIN", owner))
            await create.ExecuteNonQueryAsync();
        try
        {
            await using (var grant = new NpgsqlCommand($"GRANT SELECT({column}) ON ledger.{table} TO {group}; GRANT {group} TO grainledger_test_runtime WITH INHERIT TRUE", owner))
                await grant.ExecuteNonQueryAsync();
            await using (var effective = new NpgsqlCommand($"SELECT has_column_privilege('grainledger_test_runtime','ledger.{table}','{column}','SELECT') AND NOT has_table_privilege('grainledger_test_runtime','ledger.{table}','SELECT')", owner))
                Assert.Equal(true, await effective.ExecuteScalarAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => LedgerDatabase.ValidateRuntimeAsync(factory.RuntimeConnectionString));
            await Assert.ThrowsAsync<InvalidOperationException>(() => LedgerDatabase.ConfigureRuntimeRoleAsync(factory.OwnerConnectionString, "grainledger_test_runtime"));
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"REVOKE {group} FROM grainledger_test_runtime; REVOKE SELECT({column}) ON ledger.{table} FROM {group}; DROP ROLE {group}", owner);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static async Task RaceAsync(Task<HttpResponseMessage> first, Task<HttpResponseMessage> second)
    {
        var results = await Task.WhenAll(first, second);
        try
        {
            Assert.Single(results, x => x.IsSuccessStatusCode);
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var result in results) result.Dispose(); }
    }

    private async Task WaitForWriteWaiterAsync(string routine)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync(deadline.Token);
        while (true)
        {
            await using var query = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE usename='grainledger_test_runtime' AND wait_event='advisory' AND query LIKE @routine)", owner);
            query.Parameters.AddWithValue("routine", "%" + routine + "%");
            if ((bool)(await query.ExecuteScalarAsync(deadline.Token))!) return;
            await Task.Delay(10, deadline.Token);
        }
    }

    private static async Task<JsonElement> RoutineAsync(NpgsqlConnection owner, NpgsqlTransaction tx, string expression, Guid actor, object body, Guid? id = null)
    {
        await using var command = new NpgsqlCommand("SELECT " + expression, owner, tx);
        command.Parameters.AddWithValue("actor", actor); command.Parameters.AddWithValue("body", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(body));
        if (id.HasValue) command.Parameters.AddWithValue("id", id.Value);
        return JsonSerializer.Deserialize<JsonElement>((string)(await command.ExecuteScalarAsync())!);
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var command = new NpgsqlCommand(sql, owner); command.Parameters.AddWithValue("id", id);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
