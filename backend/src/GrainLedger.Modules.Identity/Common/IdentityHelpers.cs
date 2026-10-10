using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace GrainLedger.Modules.Identity;

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
