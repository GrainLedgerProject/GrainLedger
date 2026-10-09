using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Text.Json.Serialization;

namespace GrainLedger.Modules.Identity;

public sealed record SessionUserDto(Guid Id, string Email, string DisplayName, string? PhoneNumber, string? Role,
    Guid? OrganizationId, string? OrganizationName, bool IsActive, string[] Permissions);
public sealed record UpdateProfileRequest([property: Required, StringLength(120, MinimumLength = 1)] string DisplayName,
    [property: StringLength(32)] string? PhoneNumber);
public sealed record CreateOrganizationRequest([property: Required, StringLength(160, MinimumLength = 1)] string Name,
    [property: JsonRequired] OrganizationKind Kind);
public sealed record UpdateAccessRequest([property: JsonRequired] string? Role,
    [property: JsonRequired] Guid? OrganizationId, [property: JsonRequired] bool IsActive);

public static partial class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>();
        var auth = group.MapGroup("/auth").WithTags("Authentication");
        auth.MapLocalAuthEndpoints();
        auth.MapGet("/csrf", (HttpContext http, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(http);
            return Results.Ok(new { token = tokens.RequestToken });
        });
        auth.MapGet("/google", (IConfiguration configuration, string? returnUrl) =>
        {
            if (string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]) || string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]))
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Google authentication is not configured");
            var safeReturnUrl = IsLocalReturnUrl(returnUrl) ? returnUrl! : "/";
            return Results.Challenge(new AuthenticationProperties
            {
                RedirectUri = "/api/auth/google/complete?returnUrl=" + Uri.EscapeDataString(safeReturnUrl)
            }, [AuthSchemes.Google]);
        }).RequireRateLimiting("authentication").Produces(StatusCodes.Status302Found).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        auth.MapGet("/google/complete", CompleteGoogleLoginAsync).Produces(StatusCodes.Status302Found);
        auth.MapGet("/session", GetSessionAsync).RequireAuthorization().Produces<SessionUserDto>().Produces(StatusCodes.Status401Unauthorized);
        auth.MapPost("/logout", LogoutAsync).RequireAuthorization().Produces(StatusCodes.Status204NoContent);
        auth.MapPost("/logout-all", LogoutAllAsync).RequireAuthorization().Produces(StatusCodes.Status204NoContent);

        group.MapGet("/profile", GetSessionAsync).RequireAuthorization().WithTags("Profile").Produces<SessionUserDto>();
        group.MapPut("/profile", UpdateProfileAsync).RequireAuthorization().WithTags("Profile").Produces<SessionUserDto>();

        var admin = group.MapGroup("/admin").RequireAuthorization("identity.manage").WithTags("Administration");
        admin.MapGet("/roles", () => Results.Ok(Roles.All.Values.Select(x => new
        {
            name = x.Name, organizationKind = x.OrganizationKind, permissions = x.Permissions
        })));
        admin.MapGet("/organizations", async (IdentityDbContext db, CancellationToken ct) => Results.Ok(
            await db.Organizations.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.Kind }).ToListAsync(ct)));
        admin.MapPost("/organizations", CreateOrganizationAsync);
        admin.MapGet("/users", ListUsersAsync);
        admin.MapPut("/users/{id:guid}/access", UpdateAccessAsync);
        return endpoints;
    }

    private static async Task<IResult> CompleteGoogleLoginAsync(HttpContext http, IdentityDbContext db, GoogleIdentityProvisioner provisioner, TimeProvider clock, string? returnUrl)
    {
        var external = await http.AuthenticateAsync(AuthSchemes.External);
        if (!external.Succeeded || external.Principal is null) return Results.Redirect(GoogleLoginDiagnostics.FailureRedirect(http, "external_session"));
        var principal = external.Principal;
        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        var verified = string.Equals(principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email) || !verified)
        {
            await http.SignOutAsync(AuthSchemes.External);
            return Results.Redirect(GoogleLoginDiagnostics.FailureRedirect(http,
                string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email) ? "incomplete_identity" : "unverified_email"));
        }

        var now = clock.GetUtcNow();
        var (user, created) = await provisioner.ProvisionAsync(new GoogleIdentity(subject, email,
            principal.FindFirstValue(ClaimTypes.Name)?.Trim() ?? email, verified), http.RequestAborted);
        if (!user.IsActive)
        {
            await http.SignOutAsync(AuthSchemes.External);
            return Results.Redirect(GoogleLoginDiagnostics.FailureRedirect(http, "inactive"));
        }

        var session = new LoginSession
        {
            User = user, ExpiresAt = now.AddMinutes(15), IpAddress = http.Connection.RemoteIpAddress?.ToString(),
            UserAgent = http.Request.Headers.UserAgent.ToString()[..Math.Min(http.Request.Headers.UserAgent.ToString().Length, 512)]
        };
        db.Sessions.Add(session);
        db.AuditEvents.Add(IdentityHelpers.Audit(http, created ? "user.provisioned" : "auth.login", user.Id, user.Id, new { provider = "google" }));
        await db.SaveChangesAsync(http.RequestAborted);

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("session_id", session.Id.ToString()) };
        await http.SignInAsync(AuthSchemes.Application, new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.Application)),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = session.ExpiresAt, AllowRefresh = true });
        await http.SignOutAsync(AuthSchemes.External);
        return Results.Redirect(IsLocalReturnUrl(returnUrl) ? returnUrl! : "/");
    }

    private static async Task<IResult> GetSessionAsync(ClaimsPrincipal principal, IdentityDbContext db, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Include(x => x.Organization).SingleOrDefaultAsync(x => x.Id == principal.RequiredUserId() && x.IsActive, ct);
        return user is null ? Results.Unauthorized() : Results.Ok(ToSessionDto(user));
    }

    private static async Task<IResult> UpdateProfileAsync(UpdateProfileRequest request, ClaimsPrincipal principal, HttpContext http, IdentityDbContext db, CancellationToken ct)
    {
        var name = request.DisplayName?.Trim() ?? "";
        if (name.Length is < 1 or > 120 || request.PhoneNumber?.Length > 32) return ValidationProblem("Invalid profile fields.");
        var user = await db.Users.Include(x => x.Organization).SingleAsync(x => x.Id == principal.RequiredUserId(), ct);
        user.DisplayName = name; user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(); user.UpdatedAt = DateTimeOffset.UtcNow; user.Version = Guid.NewGuid();
        db.AuditEvents.Add(IdentityHelpers.Audit(http, "profile.updated", user.Id, user.Id, new { user.DisplayName, user.PhoneNumber }));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "The profile was changed concurrently."); }
        return Results.Ok(ToSessionDto(user));
    }

    private static async Task<IResult> LogoutAsync(ClaimsPrincipal principal, HttpContext http, IdentityDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var userId = principal.RequiredUserId();
        var account = await db.Users.Where(x => x.Id == userId).Select(x => new { x.Id, x.NormalizedEmail }).SingleAsync(ct);
        var accountKey = account.NormalizedEmail ?? account.Id.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({accountKey}))", ct);
        if (Guid.TryParse(principal.FindFirstValue("session_id"), out var sid))
        {
            var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == sid, ct);
            if (session is not null)
                await db.Sessions.Where(x => x.UserId == session.UserId && x.FamilyId == session.FamilyId && x.RevokedAt == null)
                    .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, DateTimeOffset.UtcNow), ct);
            db.AuditEvents.Add(IdentityHelpers.Audit(http, "auth.logout", principal.RequiredUserId(), principal.RequiredUserId(), new { sessionId = sid }));
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        await http.SignOutAsync(AuthSchemes.Application);
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAllAsync(ClaimsPrincipal principal, HttpContext http, IdentityDbContext db, CancellationToken ct)
    {
        var id = principal.RequiredUserId(); var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var account = await db.Users.Where(x => x.Id == id).Select(x => new { x.Id, x.NormalizedEmail }).SingleAsync(ct);
        var accountKey = account.NormalizedEmail ?? account.Id.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({accountKey}))", ct);
        await db.Sessions.Where(x => x.UserId == id && x.RevokedAt == null).ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, now), ct);
        db.AuditEvents.Add(IdentityHelpers.Audit(http, "auth.logout_all", id, id, new { })); await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await http.SignOutAsync(AuthSchemes.Application);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateOrganizationAsync(CreateOrganizationRequest request, ClaimsPrincipal principal, HttpContext http, IdentityDbContext db, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? ""; if (name.Length is < 1 or > 160) return ValidationProblem("Organization name is invalid.");
        if (!Enum.IsDefined(request.Kind)) return ValidationProblem("Organization kind must be Mill or Customer.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Same lock as bootstrap, which may also create the platform's mill.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928351)", ct);
        if (request.Kind == OrganizationKind.Mill && await db.Organizations.AnyAsync(x => x.Kind == OrganizationKind.Mill, ct))
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The platform can have only one mill organization.");
        if (await db.Organizations.AnyAsync(x => x.Name.ToLower() == name.ToLower(), ct)) return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Organization name already exists.");
        var organization = new Organization { Name = name, Kind = request.Kind }; db.Organizations.Add(organization);
        db.AuditEvents.Add(IdentityHelpers.Audit(http, "organization.created", principal.RequiredUserId(), null, new { organization.Id, organization.Name, organization.Kind }));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The organization conflicts with an existing organization."); }
        await transaction.CommitAsync(ct);
        return Results.Created($"/api/admin/organizations/{organization.Id}", new { organization.Id, organization.Name, organization.Kind });
    }

    private static async Task<IResult> ListUsersAsync(IdentityDbContext db, CancellationToken ct) => Results.Ok(await db.Users.AsNoTracking()
        .Include(x => x.Organization).OrderBy(x => x.Email).Select(x => new
        {
            x.Id, x.Email, x.DisplayName, x.PhoneNumber, x.Role, x.OrganizationId,
            OrganizationName = x.Organization == null ? null : x.Organization.Name, x.IsActive, etag = x.Version.ToString("N")
        }).ToListAsync(ct));

    private static async Task<IResult> UpdateAccessAsync(Guid id, UpdateAccessRequest request, ClaimsPrincipal principal, HttpContext http, IdentityDbContext db, CancellationToken ct)
    {
        if (!TryReadEtag(http.Request.Headers.IfMatch.ToString(), out var expected)) return Results.Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "If-Match is required.");
        if (request.Role is not null && !Roles.All.TryGetValue(request.Role, out _)) return ValidationProblem("Unknown role.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        var target = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct); if (target is null) return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "User not found.");
        var accountKey = target.NormalizedEmail ?? target.Id.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({accountKey}))", ct);
        if (target.Version != expected) return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "The user was changed concurrently.");
        var actorId = principal.RequiredUserId();
        if (id == actorId && (!request.IsActive || request.Role != Roles.Administrator))
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "You cannot disable yourself or remove your own Administrator role.");
        if (target.Role == Roles.Administrator && target.IsActive && (!request.IsActive || request.Role != Roles.Administrator) &&
            await db.Users.CountAsync(x => x.Role == Roles.Administrator && x.IsActive, ct) <= 1)
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The last active Administrator cannot be disabled or demoted.");

        Organization? organization = null;
        if (request.OrganizationId is { } organizationId)
        {
            organization = await db.Organizations.SingleOrDefaultAsync(x => x.Id == organizationId, ct);
            if (organization is null) return ValidationProblem("Organization does not exist.");
        }
        if (request.Role is { } role)
        {
            var roleDefinition = Roles.All[role];
            if (organization is null) return ValidationProblem("This role requires an organization.");
            if (organization is not null && organization.Kind != roleDefinition.OrganizationKind) return ValidationProblem("Role and organization kinds do not match.");
        }
        else if (request.OrganizationId is not null) return ValidationProblem("A pending user cannot be assigned to an organization.");

        var before = new { target.Role, target.OrganizationId, target.IsActive };
        target.Role = request.Role; target.OrganizationId = request.OrganizationId; target.IsActive = request.IsActive;
        target.Version = Guid.NewGuid(); target.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(IdentityHelpers.Audit(http, "user.access_updated", actorId, target.Id, new { before, after = request }));
        await db.Sessions.Where(x => x.UserId == target.Id && x.RevokedAt == null).ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, DateTimeOffset.UtcNow), ct);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "The user was changed concurrently."); }
        await transaction.CommitAsync(ct);
        http.Response.Headers.ETag = $"\"{target.Version:N}\"";
        return Results.Ok(new { target.Id, target.Role, target.OrganizationId, target.IsActive, etag = target.Version.ToString("N") });
    }

    internal static SessionUserDto ToSessionDto(User user) => new(user.Id, user.Email, user.DisplayName, user.PhoneNumber, user.Role,
        user.OrganizationId, user.Organization?.Name, user.IsActive,
        user.Role is not null && Roles.All.TryGetValue(user.Role, out var role) ? role.Permissions : []);

    private static bool IsLocalReturnUrl(string? value) => !string.IsNullOrWhiteSpace(value) && value[0] == '/' &&
        (value.Length == 1 || value[1] != '/' && value[1] != '\\') && !value.Contains('\\') &&
        !value.Any(char.IsControl) && Uri.TryCreate(value, UriKind.Relative, out _);
    private static bool TryReadEtag(string value, out Guid version) => Guid.TryParse(value.Trim().Trim('"'), out version);
    private static IResult ValidationProblem(string message) => Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [message] });
}

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

public sealed class CredentialEndpointMetadata;

public sealed class NoStoreFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
        return await next(context);
    }
}
