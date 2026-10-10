using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using GrainLedger.Modules.Capture;
using GrainLedger.Api.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Configuration.AddLocalEnvironmentFile(builder.Environment.ContentRootPath, builder.Environment.IsDevelopment());
    // Explicit runtime settings retain precedence over the local file.
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(args);
}
var migrating = args.Contains("--migrate", StringComparer.Ordinal);
var migrationConnection = builder.Configuration.GetConnectionString("GrainLedgerMigration");
if (migrating)
{
    if (string.IsNullOrWhiteSpace(migrationConnection)) throw new InvalidOperationException("ConnectionStrings:GrainLedgerMigration is required for explicit owner migrations.");
    builder.Configuration["ConnectionStrings:GrainLedger"] = migrationConnection;
}

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddIdentityModule(builder.Configuration, builder.Environment);
builder.Services.AddLedgerModule(builder.Configuration);
builder.Services.AddCaptureModule(builder.Configuration);

var app = builder.Build();
if (migrating)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<CaptureDbContext>().Database.MigrateAsync();
    await LedgerDatabase.ConfigureRuntimeRoleAsync(migrationConnection!, builder.Configuration["Ledger:RuntimeRole"] ?? throw new InvalidOperationException("Ledger:RuntimeRole is required."));
    return;
}
if (!app.Environment.IsEnvironment("Testing"))
    await LedgerDatabase.ValidateRuntimeAsync(app.Services.GetRequiredService<IConfiguration>().GetConnectionString("GrainLedger")
        ?? throw new InvalidOperationException("ConnectionStrings:GrainLedger is required."));

var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
foreach (var proxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
    if (IPAddress.TryParse(proxy, out var address)) forwardedHeaders.KnownProxies.Add(address);
app.UseForwardedHeaders(forwardedHeaders);
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapIdentityModule();
app.MapLedgerModule();
app.MapCaptureModule();
app.MapFallback("/api/{**path}", () => Results.NotFound());
if (!app.Environment.IsDevelopment()) app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
