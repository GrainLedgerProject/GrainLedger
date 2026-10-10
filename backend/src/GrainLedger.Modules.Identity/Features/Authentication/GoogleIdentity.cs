namespace GrainLedger.Modules.Identity;

public sealed record GoogleIdentity(string Subject, string Email, string DisplayName, bool EmailVerified);
