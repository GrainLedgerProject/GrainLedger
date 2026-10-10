using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.RateLimiting;

namespace GrainLedger.Modules.Identity;

internal static class AuthSchemes
{
    public const string Application = "GrainLedger.Application";
    public const string External = "GrainLedger.External";
    public const string Google = GoogleDefaults.AuthenticationScheme;
}

public static class IdentityModuleRegistration
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddDbContext<IdentityDbContext>((provider, options) =>
        {
            var connection = provider.GetRequiredService<IConfiguration>().GetConnectionString("GrainLedger");
            if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("ConnectionStrings:GrainLedger is required.");
            options.UseNpgsql(connection, n => n.MigrationsAssembly(typeof(IdentityDbContext).Assembly.FullName));
        });
        services.AddHttpContextAccessor();
        var keyPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keyPath)) services.AddDataProtection().SetApplicationName("GrainLedger").PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICurrentAccess, CurrentAccess>();
        services.AddScoped<GoogleIdentityProvisioner>();
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
        services.AddScoped<SessionCookieEvents>();
        services.AddScoped<PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler>(sp => sp.GetRequiredService<PermissionAuthorizationHandler>());
        services.AddAuthorization(o =>
        {
            foreach (var permission in Roles.All.Values.SelectMany(role => role.Permissions).Distinct())
                o.AddPolicy(permission, p => p.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
        });
        services.AddAntiforgery(o =>
        {
            o.HeaderName = "X-CSRF-TOKEN";
            o.Cookie.Name = environment.IsDevelopment() ? "GrainLedger.Csrf" : "__Host-GrainLedger.Csrf";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy("authentication", http =>
            RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));
        });

        services.AddAuthentication(o =>
        {
            o.DefaultAuthenticateScheme = AuthSchemes.Application;
            o.DefaultSignInScheme = AuthSchemes.Application;
            o.DefaultChallengeScheme = AuthSchemes.Application;
        })
        .AddCookie(AuthSchemes.Application, o =>
        {
            o.Cookie.Name = environment.IsDevelopment() ? "GrainLedger.Session" : "__Host-GrainLedger.Session";
            o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax; o.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(15); o.SlidingExpiration = true;
            o.EventsType = typeof(SessionCookieEvents);
        })
        .AddCookie(AuthSchemes.External, o =>
        {
            o.Cookie.Name = environment.IsDevelopment() ? "GrainLedger.External" : "__Host-GrainLedger.External";
            o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax; o.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        })
        .AddGoogle(AuthSchemes.Google, o =>
        {
            o.SignInScheme = AuthSchemes.External;
            o.ClientId = string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]) ? "not-configured" : configuration["Authentication:Google:ClientId"]!;
            o.ClientSecret = string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]) ? "not-configured" : configuration["Authentication:Google:ClientSecret"]!;
            o.CallbackPath = "/signin-google";
            o.UsePkce = true;
            o.ClaimActions.MapJsonKey("email_verified", "email_verified");
            o.SaveTokens = false;
            o.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();
                context.Response.Redirect(GoogleLoginDiagnostics.FailureRedirect(context.HttpContext,
                    GoogleLoginDiagnostics.Classify(context.Failure), context.Failure));
                return Task.CompletedTask;
            };
        });
        return services;
    }
}

public sealed record GoogleIdentity(string Subject, string Email, string DisplayName, bool EmailVerified);

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
                !await db.Users.AnyAsync(x => x.Role == Roles.Administrator && x.Provider == "google", cancellationToken))
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

public interface ICurrentAccess
{
    Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default);
    Task<bool> CanAccessOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
}

public sealed class CurrentAccess(IHttpContextAccessor accessor, IdentityDbContext db) : ICurrentAccess
{
    public async Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default)
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return false;
        var role = await db.Users.AsNoTracking().Where(x => x.Id == id && x.IsActive && x.Provider == "google").Select(x => x.Role).SingleOrDefaultAsync(cancellationToken);
        return role is not null && Roles.All.TryGetValue(role, out var definition) && definition.Permissions.Contains(permission);
    }

    public async Task<bool> CanAccessOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return false;
        var access = await db.Users.AsNoTracking().Where(x => x.Id == id && x.IsActive && x.Provider == "google").Select(x => new { x.Role, x.OrganizationId }).SingleOrDefaultAsync(cancellationToken);
        return access?.Role is not null && Roles.All.ContainsKey(access.Role) && access.OrganizationId == organizationId;
    }
}

public sealed class SessionCookieEvents(IdentityDbContext db, TimeProvider clock) : CookieAuthenticationEvents
{
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (!Guid.TryParse(context.Principal?.FindFirstValue("session_id"), out var sessionId))
        {
            context.RejectPrincipal(); return;
        }
        var now = clock.GetUtcNow();
        var session = await db.Sessions.Include(x => x.User).SingleOrDefaultAsync(x => x.Id == sessionId, context.HttpContext.RequestAborted);
        var claimedUserId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (session is null || session.UserId.ToString() != claimedUserId || session.RevokedAt is not null || session.ExpiresAt <= now || !session.User.IsActive ||
            session.User.Provider != "google")
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(AuthSchemes.Application);
            return;
        }
        // Renew both clocks together. A conditional update cannot undo concurrent logout.
        var renewed = await db.Sessions.Where(x => x.Id == sessionId && x.RevokedAt == null && x.ExpiresAt > now && x.User.IsActive &&
                x.User.Provider == "google")
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.LastSeenAt, now).SetProperty(x => x.ExpiresAt, now.AddMinutes(15)), context.HttpContext.RequestAborted);
        if (renewed == 0) { context.RejectPrincipal(); return; }
        context.ShouldRenew = true;
    }
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionAuthorizationHandler(IdentityDbContext db) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return;
        var access = await db.Users.AsNoTracking().Where(x => x.Id == id && x.IsActive && x.Provider == "google").Select(x => new { x.Role }).SingleOrDefaultAsync();
        if (access?.Role is { } role && Roles.All.TryGetValue(role, out var definition) && definition.Permissions.Contains(requirement.Permission))
            context.Succeed(requirement);
    }
}

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled API exception");
        var problem = new ProblemDetails { Status = 500, Title = "An unexpected error occurred", Type = "https://httpstatuses.com/500" };
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}

internal static class IdentityHelpers
{
    public static Guid RequiredUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new UnauthorizedAccessException();

    public static IdentityAuditEvent Audit(HttpContext http, string action, Guid? actor, Guid? subject, object details) => new()
    {
        ActorUserId = actor, SubjectUserId = subject, Action = action,
        DetailsJson = JsonSerializer.Serialize(details), IpAddress = http.Connection.RemoteIpAddress?.ToString()
    };
}
