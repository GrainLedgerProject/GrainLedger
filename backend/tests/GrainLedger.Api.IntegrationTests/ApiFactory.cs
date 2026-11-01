using System.Collections.Generic;
using System.Net.Sockets;
using System.Security.Claims;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using Npgsql;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Exports;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Testcontainers.PostgreSql;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly HashSet<int> ReservedHostPorts = [];
    private readonly int? _hostPort;
    private readonly IContainer _postgres;

    public FakeEmailSender Email { get; } = new();

    public ApiFactory()
    {
        _hostPort = string.Equals(Environment.GetEnvironmentVariable("GRAINLEDGER_TEST_HOST_NETWORK"), "true", StringComparison.OrdinalIgnoreCase)
            ? ReserveHostPort()
            : null;

        if (_hostPort is int port)
        {
            _postgres = new ContainerBuilder("postgres:17-alpine")
                .WithEnvironment("POSTGRES_DB", "grainledger_tests")
                .WithEnvironment("POSTGRES_USER", "postgres")
                .WithEnvironment("POSTGRES_PASSWORD", "postgres")
                .WithCommand("-c", "listen_addresses=127.0.0.1", "-p", port.ToString())
                .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted(
                    "pg_isready", "-h", "127.0.0.1", "-p", port.ToString(), "-U", "postgres", "-d", "grainledger_tests"))
                .WithCreateParameterModifier(parameters => (parameters.HostConfig ?? throw new InvalidOperationException("Container host configuration unavailable")).NetworkMode = "host")
                .Build();
        }

        else
        {
            _postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("grainledger_tests").WithUsername("postgres").WithPassword("postgres").Build();
        }
    }

    public string RuntimeConnectionString { get; private set; } = "";
    public string OwnerConnectionString
    {
        get
        {
            if (_hostPort is not int port)
                return ((PostgreSqlContainer)_postgres).GetConnectionString();

            var connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1",
                Port = port,
                Database = "grainledger_tests",
                Username = "postgres",
                Password = "postgres"
            };
            return connectionString.ConnectionString;
        }
    }
    public bool BrowserCookies { get; init; }
    public TimeProvider Clock { get; init; } = TimeProvider.System;
    public Action<IServiceCollection>? ConfigureTestServices { get; init; }

    private static int ReserveHostPort()
    {
        while (true)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            lock (ReservedHostPorts)
            {
                if (ReservedHostPorts.Add(port))
                    return port;
            }
        }
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:GrainLedger"] = OwnerConnectionString,
            ["ConnectionStrings:GrainLedgerMigration"] = OwnerConnectionString,
            ["Ledger:RuntimeRole"] = "grainledger_test_runtime",
            ["Authentication:Google:ClientId"] = "tests-client",
            ["Authentication:Google:ClientSecret"] = "tests-secret",
            ["Authentication:Jwt:SigningKey"] = "tests-only-signing-key-at-least-thirty-two-bytes-long",
            ["Authentication:Jwt:Issuer"] = "GrainLedger",
            ["Authentication:Jwt:Audience"] = "GrainLedger.Api",
            ["Mail:Smtp:Host"] = "fake",
            ["Mail:FromAddress"] = "no-reply@example.com",
            ["Bootstrap:AdminEmail"] = "admin@example.com"
        }));
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<GoogleOptions>(GoogleDefaults.AuthenticationScheme, options =>
                options.Backchannel = new HttpClient(new FakeGoogleBackchannelHandler()));
            if (BrowserCookies)
            {
                // Test-only HTTP bridge. Production session validation and CSRF remain enabled.
                services.PostConfigure<CookieAuthenticationOptions>("GrainLedger.Application", options =>
                {
                    options.Cookie.Name = "GrainLedger.Session";
                    options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.None;
                });
                services.PostConfigure<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>(options =>
                {
                    options.Cookie.Name = "GrainLedger.Csrf";
                    options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.None;
                });
            }
            services.RemoveAll<IEmailSender>(); services.AddSingleton<IEmailSender>(Email);
            services.RemoveAll<TimeProvider>(); services.AddSingleton(Clock);
            ConfigureTestServices?.Invoke(services);
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<CaptureDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<ExportDbContext>().Database.MigrateAsync();
        await using var owner = new NpgsqlConnection(OwnerConnectionString);
        await owner.OpenAsync();
        await using (var command = new NpgsqlCommand("CREATE ROLE grainledger_test_runtime LOGIN PASSWORD 'test-runtime' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT", owner))
            await command.ExecuteNonQueryAsync();
        await LedgerDatabase.ConfigureRuntimeRoleAsync(OwnerConnectionString, "grainledger_test_runtime");
        RuntimeConnectionString = new NpgsqlConnectionStringBuilder(OwnerConnectionString) { Username = "grainledger_test_runtime", Password = "test-runtime" }.ConnectionString;
        Services.GetRequiredService<IConfiguration>()["ConnectionStrings:GrainLedger"] = RuntimeConnectionString;
        await LedgerDatabase.ValidateRuntimeAsync(RuntimeConnectionString);
    }

    public new async Task DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            try
            {
                await _postgres.DisposeAsync();
            }
            finally
            {
                if (_hostPort is int port)
                {
                    lock (ReservedHostPorts)
                        ReservedHostPorts.Remove(port);
                }
            }
        }
    }

    public async Task<(User User, LoginSession Session)> SeedUserAsync(string? role, Guid? organizationId = null, bool active = true)
    {
        using var scope = Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = new User { Provider = "google", ProviderSubject = Guid.NewGuid().ToString(), Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Test User", Role = role, OrganizationId = organizationId, IsActive = active };
        var session = new LoginSession { User = user, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15) };
        db.AddRange(user, session); await db.SaveChangesAsync(); return (user, session);
    }

    public async Task<LoginSession> SeedSessionAsync(User user)
    {
        using var scope = Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var session = new LoginSession { UserId = user.Id, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15) };
        db.Sessions.Add(session); await db.SaveChangesAsync(); return session;
    }

    public async Task<Organization> SeedOrganizationAsync(OrganizationKind kind, string? name = null)
    {
        using var scope = Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        if (kind == OrganizationKind.Mill)
        {
            var existing = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.Kind == OrganizationKind.Mill);
            if (existing is not null) return existing;
        }
        var organization = new Organization { Name = name ?? $"{kind}-{Guid.NewGuid():N}", Kind = kind };
        db.Organizations.Add(organization); await db.SaveChangesAsync(); return organization;
    }

    public HttpClient CreateRealCookieClient(User user, LoginSession session)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        var cookie = ProtectSessionCookie(user, session);
        client.DefaultRequestHeaders.Add("Cookie", $"{cookie.Name}={cookie.Value}");
        return client;
    }

    public (string Name, string Value) ProtectSessionCookie(User user, LoginSession session)
    {
        using var scope = Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("GrainLedger.Application");
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("session_id", session.Id.ToString())], "GrainLedger.Application");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties { ExpiresUtc = session.ExpiresAt }, "GrainLedger.Application");
        return (options.Cookie.Name!, options.TicketDataFormat.Protect(ticket));
    }
}

public sealed class FakeEmailSender : IEmailSender
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _verification = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _reset = new(StringComparer.OrdinalIgnoreCase);
    public bool IsConfigured => true;
    public bool FailDelivery { get; set; }
    public Task SendVerificationCodeAsync(string email, string displayName, string code, CancellationToken cancellationToken)
    { if (FailDelivery) throw new EmailDeliveryException(); _verification[email] = code; return Task.CompletedTask; }
    public Task SendPasswordResetCodeAsync(string email, string displayName, string code, CancellationToken cancellationToken)
    { if (FailDelivery) throw new EmailDeliveryException(); _reset[email] = code; return Task.CompletedTask; }
    public string VerificationCode(string email) => _verification[email];
    public string ResetCode(string email) => _reset[email];
}

internal sealed class FakeGoogleBackchannelHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsoluteUri.Contains("/token", StringComparison.Ordinal) == true)
        {
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            var code = Uri.UnescapeDataString(form.Split('&').Select(x => x.Split('=', 2)).Single(x => x[0] == "code")[1].Replace('+', ' '));
            return Json(new { access_token = code, token_type = "Bearer", expires_in = 3600 });
        }

        if (request.RequestUri?.AbsoluteUri.Contains("userinfo", StringComparison.Ordinal) == true)
        {
            var token = request.Headers.Authorization?.Parameter ?? "missing";
            // Match GoogleDefaults.UserInformationEndpoint (/oauth2/v3/userinfo): sub and email_verified.
            if (token.StartsWith("missing-verification-", StringComparison.Ordinal))
                return Json(new { sub = $"subject-{token}", email = $"{token}@example.com", name = "OAuth Test User" });
            var verified = !token.StartsWith("unverified-", StringComparison.Ordinal);
            return Json(new { sub = $"subject-{token}", email = $"{token}@example.com", email_verified = verified, name = "OAuth Test User" });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };
}
