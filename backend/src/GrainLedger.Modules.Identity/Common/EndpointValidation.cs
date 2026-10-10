using Microsoft.AspNetCore.Http;

namespace GrainLedger.Modules.Identity;

public static partial class IdentityEndpoints
{
    private static IResult ValidationProblem(string message) => Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [message] });
}
