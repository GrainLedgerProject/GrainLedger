using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class OpenApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Scalar_and_OpenApi_expose_Bearer_auth_for_protected_operations()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var scalar = await client.GetAsync("/scalar/v1");
        Assert.Equal(HttpStatusCode.OK, scalar.StatusCode);
        Assert.Contains("GrainLedger API", await scalar.Content.ReadAsStringAsync());

        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        var paths = document.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/profile").GetProperty("put").GetProperty("security")[0].TryGetProperty("Bearer", out _));
        var google = paths.GetProperty("/api/auth/google").GetProperty("get");
        Assert.False(google.TryGetProperty("security", out var security) && security.GetArrayLength() > 0);
        foreach (var route in new[] { "register", "verify-email", "resend-verification", "login", "refresh", "forgot-password", "reset-password" })
        {
            var operation = paths.GetProperty($"/api/auth/{route}").GetProperty("post");
            Assert.False(operation.TryGetProperty("security", out var requirements) && requirements.GetArrayLength() > 0);
            Assert.True(operation.TryGetProperty("requestBody", out _));
        }
        var loginSchema = paths.GetProperty("/api/auth/login").GetProperty("post").GetProperty("responses")
            .GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.True(loginSchema.TryGetProperty("$ref", out _) || loginSchema.TryGetProperty("properties", out _));
        var parameters = paths.GetProperty("/api/admin/users/{id}/access").GetProperty("put").GetProperty("parameters");
        Assert.Contains(parameters.EnumerateArray(), parameter => parameter.GetProperty("name").GetString() == "If-Match");
    }
}
