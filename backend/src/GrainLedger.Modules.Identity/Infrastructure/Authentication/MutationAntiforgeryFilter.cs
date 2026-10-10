using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace GrainLedger.Modules.Identity;

public sealed class MutationAntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method) && !HttpMethods.IsHead(context.HttpContext.Request.Method) && !HttpMethods.IsOptions(context.HttpContext.Request.Method))
        {
            if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<CredentialEndpointMetadata>() is not null)
                return await next(context);
            var bearer = await context.HttpContext.AuthenticateAsync(AuthSchemes.Bearer);
            if (bearer.Succeeded && bearer.Principal?.Identity?.IsAuthenticated == true)
                return await next(context);
            try { await antiforgery.ValidateRequestAsync(context.HttpContext); }
            catch (AntiforgeryValidationException)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid CSRF token");
            }
        }
        return await next(context);
    }
}
