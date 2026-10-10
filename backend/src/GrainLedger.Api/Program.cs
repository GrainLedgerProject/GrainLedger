using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using GrainLedger.Modules.MockCapture;
using GrainLedger.Api.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using GrainLedger.Api.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var migrating = args.Contains("--migrate", StringComparer.Ordinal);
var seedDevelopment = args.Contains("--seed-dev", StringComparer.Ordinal);
var seedMock = args.Contains("--seed-mock", StringComparer.Ordinal);
if ((migrating ? 1 : 0) + (seedDevelopment ? 1 : 0) + (seedMock ? 1 : 0) > 1)
    throw new InvalidOperationException("Run --migrate, --seed-dev and --seed-mock separately; these commands cannot be combined.");
if (seedMock && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("--seed-mock is only available in Development.");
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Configuration.AddLocalEnvironmentFile(builder.Environment.ContentRootPath, builder.Environment.IsDevelopment());
    // Explicit runtime settings retain precedence over the local file.
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(args);
}
var mockCaptureEnabled = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
var migrationConnection = builder.Configuration.GetConnectionString("GrainLedgerMigration");
if (seedMock && string.IsNullOrWhiteSpace(migrationConnection))
    throw new InvalidOperationException("ConnectionStrings:GrainLedgerMigration is required for explicit owner mock seeding.");
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
builder.Services.AddLedgerModule(builder.Configuration);
if (mockCaptureEnabled) builder.Services.AddMockCaptureModule(builder.Configuration);

var app = builder.Build();
if (seedMock)
{
    await MockCaptureSeed.RunAsync(migrationConnection!, app.Environment.EnvironmentName);
    app.Logger.LogInformation("Synthetic mock seed completed; existing seeded records and stock unchanged on replay.");
    return;
}
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
    await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.MigrateAsync();
    if (mockCaptureEnabled)
        await scope.ServiceProvider.GetRequiredService<MockCaptureDbContext>().Database.MigrateAsync();
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
if (mockCaptureEnabled) app.MapMockCaptureModule();
app.MapFallback("/api/{**path}", () => Results.NotFound());
if (!app.Environment.IsDevelopment()) app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
