using System.ComponentModel.DataAnnotations;

namespace GrainLedger.Modules.Identity;

public sealed record RegisterRequest([property: Required, EmailAddress, StringLength(320)] string Email,
    [property: Required, StringLength(128)] string Password,
    [property: Required, StringLength(120, MinimumLength = 1)] string DisplayName);

public sealed record RegistrationPendingDto(Guid RegistrationId, string Email, int ExpiresIn);

public sealed record VerifyEmailRequest([property: Required, EmailAddress] string Email, Guid RegistrationId,
    [property: Required, RegularExpression("^[0-9]{6}$")] string Otp);

public sealed record ResendVerificationRequest([property: Required, EmailAddress] string Email, Guid RegistrationId);

public sealed record LoginRequest([property: Required, EmailAddress] string Email, [property: Required] string Password);

public sealed record RefreshRequest([property: Required] string RefreshToken);

public sealed record ForgotPasswordRequest([property: Required, EmailAddress] string Email);

public sealed record ResetPasswordRequest([property: Required, EmailAddress] string Email,
    [property: Required, RegularExpression("^[0-9]{6}$")] string Otp, [property: Required] string NewPassword);
