using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Identity;

public static partial class IdentityEndpoints
{
    private static void MapProfileEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/profile", GetSessionAsync).RequireAuthorization().WithTags("Profile").Produces<SessionUserDto>();
        group.MapPut("/profile", UpdateProfileAsync).RequireAuthorization().WithTags("Profile").Produces<SessionUserDto>();
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
}
