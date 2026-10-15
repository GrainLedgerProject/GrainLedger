using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Api.Development;

internal static class LedgerDevelopmentSeed
{
    private const string CustomerName = "GrainLedger Demo Customer";
    private const string SeasonName = "GrainLedger Demo 2026 (synthetic)";
    private const string CapturedAt = "2026-01-15T09:00:00Z";

    private static Guid Id(int value) => Guid.Parse($"6c656467-6572-4000-8000-{value:x12}");

    internal static async Task PrepareAccountsAsync(IServiceProvider services, string environment, string? password,
        CancellationToken ct = default)
    {
        if (environment != Environments.Development)
            throw new InvalidOperationException("--seed-ledger is only available in Development.");
        if (!LocalAuthService.IsStrongPassword(password ?? ""))
            throw new InvalidOperationException("DevelopmentSeed__Password must contain 12–128 characters including upper/lower case, a digit and a symbol.");

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        // Fail before creating prerequisites; never reset an existing account to make the seed work.
        foreach (var (alias, role) in new[]
        {
            ("admin", Roles.Administrator), ("mill.manager", Roles.MillManager),
            ("mill.operator", Roles.MillOperator), ("customer.manager", Roles.CustomerManager),
            ("export", Roles.ExportStaff), ("purchasing", Roles.PurchasingStaff)
        })
        {
            var email = alias + "@grainledger.local";
            var normalized = LocalAuthService.NormalizeEmail(email);
            var user = await db.Users.AsNoTracking().Include(x => x.Organization)
                .SingleOrDefaultAsync(x => x.Provider == "local" && x.NormalizedEmail == normalized, ct);
            if (user is null) continue;
            var kind = Roles.All[role].OrganizationKind;
            if (!user.IsActive || user.Role != role || user.Organization?.Kind != kind ||
                kind == OrganizationKind.Customer && user.Organization.Name != CustomerName ||
                user.LockoutEnd > DateTimeOffset.UtcNow || user.PasswordHash is null ||
                hasher.VerifyHashedPassword(user, user.PasswordHash, password!) == PasswordVerificationResult.Failed)
                throw new InvalidOperationException($"Existing demo account {email} is incompatible with --seed-ledger (password, role, organization, active or lockout state). Account left unchanged.");
        }
        await DevelopmentSeed.RunAsync(db, hasher, environment, password, ct);
    }

    internal static async Task RunAsync(HttpClient client, string password, CancellationToken ct = default)
    {
        var previousAuthorization = client.DefaultRequestHeaders.Authorization;
        try
        {
            // Authenticate each actor once, before setup mutations. Bearer requests retain normal permission checks.
            var admin = await LoginAsync("admin", Roles.Administrator);
            var manager = await LoginAsync("mill.manager", Roles.MillManager);
            var millOperator = await LoginAsync("mill.operator", Roles.MillOperator);
            var purchaser = await LoginAsync("purchasing", Roles.PurchasingStaff);
            var owner = purchaser.GetProperty("user").GetProperty("organizationId").GetGuid();
            if (purchaser.GetProperty("user").GetProperty("organizationName").GetString() != CustomerName ||
                manager.GetProperty("user").GetProperty("organizationId").GetGuid() != admin.GetProperty("user").GetProperty("organizationId").GetGuid() ||
                millOperator.GetProperty("user").GetProperty("organizationId").GetGuid() != admin.GetProperty("user").GetProperty("organizationId").GetGuid())
                throw new InvalidOperationException("Demo accounts must belong to the seeded mill and demo customer organizations. Accounts left unchanged.");

            Use(admin);
            var setup = await GetAsync("/api/ledger/setup");
            await EnsureSetupAsync("facilities", 1, new { id = Id(1), name = "GrainLedger Demo Mill (synthetic)" });
            foreach (var (id, name, kind) in new[]
            {
                (2, "Demo Receiving Store", "STORE"), (3, "Demo Dry Store", "STORE"),
                (4, "Demo Isolation", "ISOLATION")
            })
                await EnsureSetupAsync("locations", id, new { id = Id(id), facilityId = Id(1), name, kind });
            foreach (var (id, name) in new[] { (5, "Demo Dryer"), (6, "Demo Mill") })
                await EnsureSetupAsync("equipment", id, new { id = Id(id), facilityId = Id(1), name });
            foreach (var actor in new[] { manager, millOperator })
            {
                var userId = actor.GetProperty("user").GetProperty("id").GetGuid();
                if (!setup.GetProperty("assignments").EnumerateArray().Any(x =>
                        x.GetProperty("userId").GetGuid() == userId && x.GetProperty("facilityId").GetGuid() == Id(1)))
                    await PostAsync("/api/ledger/assignments", new { userId, facilityId = Id(1) });
            }

            if (!setup.GetProperty("policies").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == Id(8)))
            {
                await PostAsync("/api/ledger/evidence", new
                {
                    id = Id(7), ownerId = owner, facilityId = Id(1), fileName = "synthetic-demo-policy.txt", contentType = "text/plain",
                    contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                        "SYNTHETIC DEVELOPMENT DEMO ONLY. Not factory-approved. Zero-percent DRYING tolerance demonstrates exact mass balance at the synthetic demo facility only. No business approval or real-facility policy is asserted."))
                });
                await PostAsync("/api/ledger/policies", new
                {
                    id = Id(8), facilityId = Id(1), kind = "DRYING", tolerancePercent = "0",
                    effectiveFrom = "2026-01-01T00:00:00Z", evidenceId = Id(7)
                });
            }

            Use(purchaser);
            var receipt = await GetOptionalAsync($"/api/procurements/{Id(20)}");
            if (receipt is null)
            {
                Use(admin);
                var commodity = (await FindAsync("/api/commodities?q=PADDY", "code", "PADDY")) ??
                    await PostAsync("/api/commodities", new { code = "PADDY", name = "Paddy" });
                var unit = (await FindAsync("/api/measurement-units?q=KG", "code", "KG")) ??
                    await PostAsync("/api/measurement-units", new { code = "KG", name = "Kilogram", quantityKind = "MASS" });
                if (unit.GetProperty("quantityKind").GetString() != "MASS")
                    throw new InvalidOperationException("Existing KG catalog unit is not MASS; left unchanged.");

                Use(purchaser);
                await PostAsync("/api/farmers", Capture(30, new { entityId = Id(10), code = "GL-DEMO-FARMER", name = "Synthetic Demo Farmer" }));
                await PostAsync("/api/plots", Capture(31, new
                {
                    entityId = Id(11), farmerId = Id(10), code = "GL-DEMO-PLOT", areaHa = 1m,
                    geometry = new { type = "Point", coordinates = new[] { 106.123456m, 10.123456m } }
                }));
                var plotSeason = await GetOptionalAsync($"/api/plot-seasons/{Id(12)}");
                Guid seasonId;
                if (plotSeason is { } existingLink)
                    seasonId = existingLink.GetProperty("seasonId").GetGuid();
                else
                {
                    Use(admin);
                    var season = (await FindAsync($"/api/crop-seasons?companyId={owner}&search={Uri.EscapeDataString(SeasonName)}", "name", SeasonName)) ??
                        await PostAsync("/api/crop-seasons", new { companyId = owner, name = SeasonName, startsOn = "2026-01-01", endsOn = "2026-12-31" });
                    seasonId = season.GetProperty("id").GetGuid();
                }
                Use(purchaser);
                await PostAsync("/api/plot-seasons", Capture(32, new { entityId = Id(12), plotId = Id(11), seasonId, riceVariety = "ST25 (synthetic demo)" }));
                receipt = await PostAsync("/api/procurements", Capture(33, new
                {
                    entityId = Id(20), farmerId = Id(10), collectionSiteId = Id(1), receivingLocationId = Id(2),
                    commodityId = commodity.GetProperty("id").GetGuid(), receivedAt = CapturedAt,
                    items = new[] { new { itemId = Id(21), lineNo = 1, plotSeasonId = Id(12), measuredValue = "600",
                        measuredUnit = "KG", moisturePercent = "22.5", lotId = Id(22), sourceLotId = Id(23) } }
                }));
            }

            Use(millOperator);
            var receiving = await GetAsync($"/api/ledger/receiving/{Id(20)}");
            // Never refill consumed/transferred stock or supersede a user's arrival measurement/review.
            if (receiving.GetProperty("status").GetString() == "PENDING_ARRIVAL" &&
                receiving.GetProperty("receivingVersion").GetInt32() == 0)
                await PostAsync($"/api/ledger/receiving/{Id(20)}/measurements", new
                {
                    operationId = Id(34), expectedReceivingVersion = 0, capturedAt = CapturedAt,
                    observations = "Synthetic demo complete exact arrival; not a real delivery.",
                    items = new[] { new { procurementItemId = Id(21), measuredValue = "600", measuredUnit = "KG" } }
                });

            async Task<JsonElement> LoginAsync(string alias, string role)
            {
                client.DefaultRequestHeaders.Authorization = null;
                var result = await PostAsync("/api/auth/login", new { email = alias + "@grainledger.local", password });
                if (result.GetProperty("user").GetProperty("role").GetString() != role ||
                    !result.GetProperty("user").GetProperty("isActive").GetBoolean())
                    throw new InvalidOperationException($"Demo account {alias} has incompatible access; left unchanged.");
                return result;
            }

            void Use(JsonElement actor) => client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", actor.GetProperty("accessToken").GetString());

            async Task EnsureSetupAsync(string resource, int id, object body)
            {
                var existing = setup.GetProperty(resource).EnumerateArray().FirstOrDefault(x => x.GetProperty("id").GetGuid() == Id(id));
                if (existing.ValueKind == JsonValueKind.Undefined)
                    await PostAsync("/api/ledger/" + resource, body);
                else if (resource != "facilities" && existing.GetProperty("facilityId").GetGuid() != Id(1))
                    throw new InvalidOperationException($"Seeded {resource} {Id(id)} moved to another facility; left unchanged.");
            }

            async Task<JsonElement?> FindAsync(string path, string property, string value)
            {
                // Bounded pages keep pre-existing catalogs usable without loading all records at once.
                for (var page = 1; ; page++)
                {
                    var result = await GetAsync(path + $"&page={page}&pageSize=100");
                    var match = result.GetProperty("items").EnumerateArray().FirstOrDefault(x => x.GetProperty(property).GetString() == value);
                    if (match.ValueKind != JsonValueKind.Undefined) return match;
                    if (!result.GetProperty("hasMore").GetBoolean()) return null;
                }
            }

            async Task<JsonElement> GetAsync(string path) => (await SendAsync(HttpMethod.Get, path, null, false))!.Value;
            Task<JsonElement?> GetOptionalAsync(string path) => SendAsync(HttpMethod.Get, path, null, true);
            async Task<JsonElement> PostAsync(string path, object body) => (await SendAsync(HttpMethod.Post, path, body, false))!.Value;

            async Task<JsonElement?> SendAsync(HttpMethod method, string path, object? body, bool optional)
            {
                using var request = new HttpRequestMessage(method, path);
                if (body is not null) request.Content = JsonContent.Create(body);
                using var response = await client.SendAsync(request, ct);
                if (optional && response.StatusCode == HttpStatusCode.NotFound) return null;
                if (!response.IsSuccessStatusCode)
                {
                    var title = response.ReasonPhrase;
                    try
                    {
                        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                        if (problem.TryGetProperty("title", out var message)) title = message.GetString();
                        if (problem.TryGetProperty("errors", out var errors))
                            title += " " + string.Join(" ", errors.EnumerateObject().SelectMany(x =>
                                x.Value.EnumerateArray().Select(error => x.Name + ": " + error.GetString())));
                    }
                    catch (JsonException) { }
                    throw new InvalidOperationException($"Ledger demo seed {method} {path} failed ({(int)response.StatusCode}): {title}. Existing data was not reset; rerun after resolving the prerequisite.");
                }
                return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            }
        }
        finally
        {
            client.DefaultRequestHeaders.Authorization = previousAuthorization;
        }
    }

    private static object Capture(int operation, object payload) => new
    {
        deviceId = "grainledger-development-seed-v1", clientOperationId = Id(operation),
        capturedAt = CapturedAt, payloadSchemaVersion = 1, payload
    };
}
