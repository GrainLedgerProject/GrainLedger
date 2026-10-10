using System.Net;
using System.Net.Http.Json;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class DevelopmentSeedTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Seed_creates_six_login_accounts_and_rerun_preserves_existing_passwords_and_roles()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        const string password = "SeedOnly!Password123";
        Assert.Equal(6, await DevelopmentSeed.RunAsync(db, hasher, "Development", password));
        var users = await db.Users.Include(x => x.Organization).ToListAsync();
        Assert.Equal(6, users.Count);
        Assert.Equal(Roles.All.Keys.Order(), users.Select(x => x.Role!).Order());
        Assert.All(users, u => Assert.Equal(Roles.All[u.Role!].OrganizationKind, u.Organization!.Kind));
        Assert.Single(await db.Organizations.Where(x => x.Kind == OrganizationKind.Customer).ToListAsync());
        using var client = factory.CreateClient();
        foreach (var user in users)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { user.Email, password })).StatusCode);
        var admin = users.Single(x => x.Role == Roles.Administrator);
        var hash = admin.PasswordHash;
        admin.DisplayName = "Keep my changes";
        admin.Role = Roles.MillManager;
        await db.SaveChangesAsync();
        Assert.Equal(0, await DevelopmentSeed.RunAsync(db, hasher, "Development", "DifferentSeed!Password123"));
        await db.Entry(admin).ReloadAsync();
        Assert.Equal(hash, admin.PasswordHash);
        Assert.Equal("Keep my changes", admin.DisplayName);
        Assert.Equal(Roles.MillManager, admin.Role);
        Assert.Equal(6, await db.Users.CountAsync());
        Assert.Equal(2, await db.Organizations.CountAsync());
    }

    [Fact]
    public async Task Seed_rejects_production_and_weak_password_before_writing()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => DevelopmentSeed.RunAsync(db, hasher, "Production", "SeedOnly!Password123"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => DevelopmentSeed.RunAsync(db, hasher, "Development", "weak"));
    }
}
