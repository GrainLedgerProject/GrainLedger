using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrainLedger.Modules.Identity;

public static partial class IdentityEndpoints
{
    private static void MapAdministrationEndpoints(RouteGroupBuilder group)
    {
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

    private static bool TryReadEtag(string value, out Guid version) => Guid.TryParse(value.Trim().Trim('"'), out version);
}
