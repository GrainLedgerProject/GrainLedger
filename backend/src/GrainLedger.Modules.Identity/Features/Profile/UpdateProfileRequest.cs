using System.ComponentModel.DataAnnotations;

namespace GrainLedger.Modules.Identity;

public sealed record UpdateProfileRequest([property: Required, StringLength(120, MinimumLength = 1)] string DisplayName,
    [property: StringLength(32)] string? PhoneNumber);
