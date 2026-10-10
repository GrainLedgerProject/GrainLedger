using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GrainLedger.Modules.MockCapture;

// Synthetic development records stay durable; SQL owns constraints and immutable history.
public sealed class MockCaptureDbContext(DbContextOptions<MockCaptureDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema("capture_mock");
}

public static class MockCaptureModuleRegistration
{
    public static IServiceCollection AddMockCaptureModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MockCaptureDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("GrainLedger") ?? throw new InvalidOperationException("ConnectionStrings:GrainLedger is required."),
            npgsql => npgsql.MigrationsAssembly(typeof(MockCaptureDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__EFMigrationsHistory", "capture_mock")));
        services.AddScoped<MockCaptureService>();
        return services;
    }
}
