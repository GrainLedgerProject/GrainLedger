using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GrainLedger.Modules.Identity;

public sealed class GoogleIdentityProvisioner(IdentityDbContext db, IConfiguration configuration, TimeProvider clock)
{
    public async Task<(User User, bool Created)> ProvisionAsync(GoogleIdentity identity, CancellationToken cancellationToken = default)
    {
        if (!identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Subject) || string.IsNullOrWhiteSpace(identity.Email))
            throw new InvalidOperationException("Google returned an unverified or incomplete identity.");
        var email = identity.Email.Trim().ToLowerInvariant();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928351)", cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Provider == "google" && x.ProviderSubject == identity.Subject, cancellationToken);
        var created = user is null;
        if (user is null)
        {
            user = new User { Provider = "google", ProviderSubject = identity.Subject, Email = email, DisplayName = identity.DisplayName.Trim() };
            var bootstrap = configuration["Bootstrap:AdminEmail"]?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(bootstrap) && string.Equals(bootstrap, email, StringComparison.Ordinal) &&
                !await db.Users.AnyAsync(x => x.Role == Roles.Administrator, cancellationToken))
            {
                var mill = await db.Organizations.SingleOrDefaultAsync(x => x.Kind == OrganizationKind.Mill, cancellationToken);
                if (mill is null) { mill = new Organization { Name = "Tiền Giang Mill", Kind = OrganizationKind.Mill }; db.Organizations.Add(mill); }
                user.Role = Roles.Administrator; user.Organization = mill;
            }
            db.Users.Add(user);
        }
        else
        {
            user.Email = email;
            if (string.IsNullOrWhiteSpace(user.DisplayName)) user.DisplayName = identity.DisplayName.Trim();
            user.UpdatedAt = clock.GetUtcNow();
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (user, created);
    }
}
