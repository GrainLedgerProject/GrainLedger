namespace GrainLedger.Api.Features.Identity;

public interface IEmailSender
{
    bool IsConfigured { get; }
    Task SendVerificationCodeAsync(string email, string displayName, string code, CancellationToken cancellationToken);
    Task SendPasswordResetCodeAsync(string email, string displayName, string code, CancellationToken cancellationToken);
}
