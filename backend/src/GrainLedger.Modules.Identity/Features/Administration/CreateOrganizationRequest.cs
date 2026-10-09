using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GrainLedger.Modules.Identity;

public sealed record CreateOrganizationRequest([property: Required, StringLength(160, MinimumLength = 1)] string Name,
    [property: JsonRequired] OrganizationKind Kind);
