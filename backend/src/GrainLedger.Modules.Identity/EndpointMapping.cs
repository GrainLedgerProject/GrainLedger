using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GrainLedger.Modules.Identity;

public static partial class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>();
        MapAuthenticationEndpoints(group);
        MapProfileEndpoints(group);
        MapAdministrationEndpoints(group);
        return endpoints;
    }
}
