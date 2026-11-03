using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GrainLedger.Api.Features.Identity;

public sealed record CreateOrganizationRequest([property: Required, StringLength(160, MinimumLength = 1)] string Name,
    [property: JsonRequired] OrganizationKind Kind);
