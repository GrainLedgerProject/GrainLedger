namespace GrainLedger.Api.Features.Identity;

public sealed record SessionUserDto(Guid Id, string Email, string DisplayName, string? PhoneNumber, string? Role,
    Guid? OrganizationId, string? OrganizationName, bool IsActive, string[] Permissions);
