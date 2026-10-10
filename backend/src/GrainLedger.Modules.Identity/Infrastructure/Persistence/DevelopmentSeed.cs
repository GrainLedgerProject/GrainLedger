using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace GrainLedger.Modules.Identity;

public static class DevelopmentSeed
{
    public static async Task<int> RunAsync(IdentityDbContext db, IPasswordHasher<User> hasher,
        string environment, string? password, CancellationToken ct = default)
    {
        if (environment != Environments.Development)
            throw new InvalidOperationException("--seed-dev is only available in Development.");
        if (!LocalAuthService.IsStrongPassword(password ?? ""))
            throw new InvalidOperationException("Set DevelopmentSeed__Password in .env: 12–128 characters including upper/lower case, a digit and a symbol.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize with organization creation and administrator bootstrap.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928351)", ct);
        var mill = await db.Organizations.SingleOrDefaultAsync(x => x.Kind == OrganizationKind.Mill, ct);
        if (mill is null)
        {
            mill = new Organization { Name = "GrainLedger Mill", Kind = OrganizationKind.Mill };
            db.Organizations.Add(mill);
        }
        const string customerName = "GrainLedger Demo Customer";
        var customer = await db.Organizations.SingleOrDefaultAsync(x => x.Name == customerName, ct);
        if (customer is not null && customer.Kind != OrganizationKind.Customer)
            throw new InvalidOperationException("The demo customer name is already used by a non-customer organization.");
        if (customer is null)
        {
            customer = new Organization { Name = customerName, Kind = OrganizationKind.Customer };
            db.Organizations.Add(customer);
        }
        (string Alias, string Role)[] accounts =
        [
            ("admin", Roles.Administrator), ("mill.manager", Roles.MillManager),
            ("mill.operator", Roles.MillOperator), ("customer.manager", Roles.CustomerManager),
            ("export", Roles.ExportStaff), ("purchasing", Roles.PurchasingStaff)
        ];
        var created = 0;
        foreach (var (alias, role) in accounts)
        {
            var email = alias + "@grainledger.local";
            var normalized = LocalAuthService.NormalizeEmail(email);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({normalized}))", ct);
            if (await db.Users.AnyAsync(x => x.Provider == "local" && x.NormalizedEmail == normalized, ct)) continue;
            var user = new User
            {
                Provider = "local", ProviderSubject = Guid.NewGuid().ToString("N"),
                Email = email, NormalizedEmail = normalized, DisplayName = "Demo " + role, Role = role,
                Organization = Roles.All[role].OrganizationKind == OrganizationKind.Mill ? mill : customer
            };
            user.PasswordHash = hasher.HashPassword(user, password!);
            db.Users.Add(user);
            created++;
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return created;
    }
}
