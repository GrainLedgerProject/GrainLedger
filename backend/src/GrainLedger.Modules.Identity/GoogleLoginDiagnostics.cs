using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GrainLedger.Modules.Identity;

internal static class GoogleLoginDiagnostics
{
    public static string FailureRedirect(HttpContext http, string reason, Exception? failure = null)
    {
        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GrainLedger.Authentication.Google");
        // Provider exception messages can contain response bodies. Log only classified reasons,
        // exception types and request metadata, never tokens, claims or callback query strings.
        logger.LogWarning("Google sign-in rejected. Reason={Reason}; FailureType={FailureType}; TraceId={TraceId}; Origin={Scheme}://{Host}; Path={Path}",
            reason, failure?.GetType().Name ?? "IdentityValidation", http.TraceIdentifier,
            http.Request.Scheme, http.Request.Host, http.Request.Path);
        return $"/?authError={Uri.EscapeDataString(reason)}&authTrace={Uri.EscapeDataString(http.TraceIdentifier)}";
    }

    public static string Classify(Exception? failure)
    {
        for (var current = failure; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("correlation", StringComparison.OrdinalIgnoreCase)) return "correlation";
            if (current.Message.Contains("state", StringComparison.OrdinalIgnoreCase)) return "invalid_state";
            if (current.Message.Contains("invalid_client", StringComparison.OrdinalIgnoreCase)) return "invalid_client";
            if (current.Message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)) return "invalid_grant";
            if (current.Message.Contains("access_denied", StringComparison.OrdinalIgnoreCase)) return "access_denied";
            if (current is HttpRequestException or TaskCanceledException) return "provider_unavailable";
        }
        return "google";
    }
}
