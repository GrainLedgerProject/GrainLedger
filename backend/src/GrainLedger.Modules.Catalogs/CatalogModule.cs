using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GrainLedger.Modules.Catalogs;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddDbContext<CatalogDbContext>((provider, options) =>
        {
            var connection = provider.GetRequiredService<IConfiguration>().GetConnectionString("GrainLedger");
            if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("ConnectionStrings:GrainLedger is required.");
            options.UseNpgsql(connection, n => n.MigrationsAssembly(typeof(CatalogDbContext).Assembly.FullName));
        });
        services.AddScoped<ApplicableMrlRead>();
        return services;
    }

    public static IEndpointRouteBuilder MapCatalogModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapActiveSubstanceEndpoints();
        endpoints.MapMarketEndpoints();
        endpoints.MapCommodityEndpoints();
        endpoints.MapQualityAttributeEndpoints();
        endpoints.MapRiceVarietyEndpoints();
        endpoints.MapMeasurementUnitEndpoints();
        endpoints.MapMrlVersionEndpoints();
        return endpoints;
    }
}
