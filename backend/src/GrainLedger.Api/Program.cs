using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Api.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using GrainLedger.Api.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var migrating = args.Contains("--migrate", StringComparer.Ordinal);
var seedDevelopment = args.Contains("--seed-dev", StringComparer.Ordinal);
if (migrating && seedDevelopment)
    throw new InvalidOperationException("Run --migrate and --seed-dev separately.");
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Configuration.AddLocalEnvironmentFile(builder.Environment.ContentRootPath, builder.Environment.IsDevelopment());
    // Explicit runtime settings retain precedence over the local file.
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(args);
}
var migrationConnection = builder.Configuration.GetConnectionString("GrainLedgerMigration");
if (migrating)
{
    if (string.IsNullOrWhiteSpace(migrationConnection)) throw new InvalidOperationException("ConnectionStrings:GrainLedgerMigration is required for explicit owner migrations.");
    builder.Configuration["ConnectionStrings:GrainLedger"] = migrationConnection;
}

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerDocumentTransformer>();
    options.AddOperationTransformer<BearerOperationTransformer>();
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddIdentityModule(builder.Configuration, builder.Environment);
builder.Services.AddCaptureModule();
builder.Services.AddCatalogModule();
builder.Services.AddLedgerModule(builder.Configuration);

var app = builder.Build();
if (seedDevelopment)
{
    await using var scope = app.Services.CreateAsyncScope();
    var created = await DevelopmentSeed.RunAsync(
        scope.ServiceProvider.GetRequiredService<IdentityDbContext>(),
        scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<User>>(),
        app.Environment.EnvironmentName, builder.Configuration["DevelopmentSeed:Password"]);
    app.Logger.LogInformation("Development seed completed: {Created} accounts created; existing local accounts unchanged.", created);
    return;
}

if (migrating)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<CaptureDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.MigrateAsync();
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
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi();
    app.MapScalarApiReference("/scalar", options => options
        .WithTitle("GrainLedger API")
        .AddPreferredSecuritySchemes("Bearer"));
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("Health");
app.MapIdentityModule();
app.MapLedgerModule();
app.MapCaptureModule();
app.MapCatalogModule();
app.MapFallback("/api/{**path}", () => Results.NotFound());
if (!app.Environment.IsDevelopment()) app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
