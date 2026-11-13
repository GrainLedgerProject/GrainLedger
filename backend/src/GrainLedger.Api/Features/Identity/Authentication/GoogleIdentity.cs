namespace GrainLedger.Api.Features.Identity;

public sealed record GoogleIdentity(string Subject, string Email, string DisplayName, bool EmailVerified);
