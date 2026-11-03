using System.Text.Json.Serialization;

namespace GrainLedger.Api.Features.Identity;

public sealed record UpdateAccessRequest([property: JsonRequired] string? Role,
    [property: JsonRequired] Guid? OrganizationId, [property: JsonRequired] bool IsActive);
