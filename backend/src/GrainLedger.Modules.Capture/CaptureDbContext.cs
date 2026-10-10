using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GrainLedger.Modules.Capture;

// Capture SQL owns its constraints and immutable-document history; no module entity crosses this context.
public sealed class CaptureDbContext(DbContextOptions<CaptureDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema("capture");
}

public static class CaptureModuleRegistration
{
    public static IServiceCollection AddCaptureModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CaptureDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("GrainLedger") ?? throw new InvalidOperationException("ConnectionStrings:GrainLedger is required."),
            npgsql => npgsql.MigrationsAssembly(typeof(CaptureDbContext).Assembly.FullName)));
        services.AddScoped<CaptureService>();
        return services;
    }
}
