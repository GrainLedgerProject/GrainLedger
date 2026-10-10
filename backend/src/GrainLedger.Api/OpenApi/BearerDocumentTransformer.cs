using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GrainLedger.Api.OpenApi;

public sealed class BearerDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "GrainLedger API";
        document.Info.Version = "v1";
        document.Info.Description = "Register and verify your email, then login to obtain an accessToken. Paste the accessToken into Scalar's Bearer authentication field. Authenticated Bearer requests do not require CSRF tokens. Google browser sessions still use cookie authentication and CSRF protection.";
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste accessToken from POST /api/auth/login. Do not paste the refreshToken."
        };
        return Task.CompletedTask;
    }
}

public sealed class BearerOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security = [new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
            }];
            operation.Responses ??= new();
            operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing, invalid, expired or revoked access token." });
            operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Your account does not have permission for this operation." });
        }
        if (context.Description.RelativePath?.StartsWith("api/admin/users/", StringComparison.Ordinal) == true &&
            context.Description.RelativePath.EndsWith("/access", StringComparison.Ordinal))
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "If-Match", In = ParameterLocation.Header, Required = true,
                Description = "The target user's etag from GET /api/admin/users. Reload it after a 412 response.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
        }
        return Task.CompletedTask;
    }
}
