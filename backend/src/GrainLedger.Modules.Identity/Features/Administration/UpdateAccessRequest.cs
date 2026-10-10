using System.Text.Json.Serialization;

namespace GrainLedger.Modules.Identity;

public sealed record UpdateAccessRequest([property: JsonRequired] string? Role,
    [property: JsonRequired] Guid? OrganizationId, [property: JsonRequired] bool IsActive);
