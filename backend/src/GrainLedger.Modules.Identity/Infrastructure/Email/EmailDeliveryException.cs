using MailKit.Net.Smtp;

namespace GrainLedger.Modules.Identity;

public sealed class EmailDeliveryException : Exception
{
    public string Stage { get; }
    public string FailureType { get; }
    public int? SmtpStatus { get; }

    public EmailDeliveryException() : this("unknown", null) { }

    public EmailDeliveryException(string stage, Exception? cause) : base("Email delivery is temporarily unavailable.")
    {
        Stage = stage;
        FailureType = cause?.GetType().Name ?? "Unknown";
        SmtpStatus = cause is SmtpCommandException smtp ? (int)smtp.StatusCode : null;
    }
}
