using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace GrainLedger.Modules.Identity;

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Mail:Smtp:Host"]) &&
        !string.IsNullOrWhiteSpace(configuration["Mail:FromAddress"]);

    public Task SendVerificationCodeAsync(string email, string displayName, string code, CancellationToken cancellationToken) =>
        SendAsync(email, displayName, "Verify your GrainLedger email", $"Your GrainLedger verification code is {code}. It expires in 10 minutes.", cancellationToken);

    public Task SendPasswordResetCodeAsync(string email, string displayName, string code, CancellationToken cancellationToken) =>
        SendAsync(email, displayName, "Reset your GrainLedger password", $"Your GrainLedger password reset code is {code}. It expires in 10 minutes.", cancellationToken);

    private async Task SendAsync(string email, string displayName, string subject, string text, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Email delivery is not configured.");
        var stage = "message";
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(configuration["Mail:FromName"] ?? "GrainLedger", configuration["Mail:FromAddress"]!));
            message.To.Add(new MailboxAddress(displayName, email)); message.Subject = subject; message.Body = new TextPart("plain") { Text = text };
            using var client = new SmtpClient { Timeout = 15000 };
            var port = int.TryParse(configuration["Mail:Smtp:Port"], out var parsed) ? parsed : 587;
            var tls = !bool.TryParse(configuration["Mail:Smtp:UseStartTls"], out var startTls) || startTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
            stage = "connect-tls";
            await client.ConnectAsync(configuration["Mail:Smtp:Host"]!, port, tls, cancellationToken);
            var username = configuration["Mail:Smtp:Username"];
            if (!string.IsNullOrWhiteSpace(username))
            {
                stage = "authenticate";
                await client.AuthenticateAsync(username, configuration["Mail:Smtp:Password"] ?? "", cancellationToken);
            }
            stage = "send";
            await client.SendAsync(message, cancellationToken); await client.DisconnectAsync(true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { throw new EmailDeliveryException(stage, exception); }
    }
}
