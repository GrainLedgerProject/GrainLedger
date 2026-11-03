using GrainLedger.Api.Features.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GrainLedger.Api.Features.Catalogs;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddScoped<ApplicableMrlRead>();
        services.AddScoped<BlendAssessmentPolicyRead>();
        services.AddScoped<ResiduePolicyReader>();
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
        endpoints.MapResidueRuleEndpoints();
        endpoints.MapResiduePolicySettingEndpoints();
        endpoints.MapBlendAssessmentPolicyEndpoints();
        return endpoints;
    }
}
