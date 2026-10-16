using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class PermissionContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(Roles.Administrator, "identity.manage,platform.configure,trace.read.all")]
    [InlineData(Roles.MillManager, "platform.read,lots.read.all,conversions.read,conversions.confirm,corrections.reconcile,receiving.reconcile,holds.process,reconciliation.read,history.read")]
    [InlineData(Roles.MillOperator, "platform.read,lots.read.facility,lots.receive,conversions.write,containers.operate,history.read")]
    [InlineData(Roles.CustomerManager, "farms.read,lots.read.tenant,lab.write,blend.approve,holds.manage,trace.read.tenant,history.read")]
    [InlineData(Roles.ExportStaff, "mrl.read,lots.read.tenant,lab.read,blend.propose,containers.write,trace.read.tenant,history.read")]
    [InlineData(Roles.PurchasingStaff, "farms.write,purchases.write,history.read")]
    public async Task Session_and_policy_enforcement_preserve_existing_role_contract(string role, string expected)
    {
        var (user, session) = await factory.SeedUserAsync(role);
        using var client = factory.CreateRealCookieClient(user, session);
        var profile = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal(expected.Split(',').Order(), profile.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()).Order());
        Assert.Equal(role == Roles.Administrator ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/roles")).StatusCode);
        var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        using var scope = factory.Services.CreateScope();
        var scopedAuthorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var identity = new System.Security.Claims.ClaimsIdentity([new(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString())], "test");
        foreach (var permission in Permissions.All)
        {
            Assert.NotNull(await provider.GetPolicyAsync(permission));
            var result = await scopedAuthorization.AuthorizeAsync(new System.Security.Claims.ClaimsPrincipal(identity), null, permission);
            Assert.Equal(expected.Split(',').Contains(permission), result.Succeeded);
        }
    }
}
