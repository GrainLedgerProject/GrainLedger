using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace GrainLedger.Modules.Identity;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is EmailDeliveryException delivery)
        {
            // Never log the raw SMTP exception: it may contain addresses or server replies.
            logger.LogWarning("Email delivery failed at {Stage}: {FailureType}; SMTP status {SmtpStatus}",
                delivery.Stage, delivery.FailureType, delivery.SmtpStatus);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = 503, Title = "Email delivery is temporarily unavailable" }, cancellationToken);
            return true;
        }
        logger.LogError(exception, "Unhandled API exception");
        var problem = new ProblemDetails { Status = 500, Title = "An unexpected error occurred", Type = "https://httpstatuses.com/500" };
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
