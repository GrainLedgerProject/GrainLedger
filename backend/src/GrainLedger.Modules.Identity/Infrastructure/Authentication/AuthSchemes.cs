using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace GrainLedger.Modules.Identity;

internal static class AuthSchemes
{
    public const string Application = "GrainLedger.Application";
    public const string External = "GrainLedger.External";
    public const string Google = GoogleDefaults.AuthenticationScheme;
    public const string Bearer = JwtBearerDefaults.AuthenticationScheme;
    public const string Smart = "GrainLedger.Smart";
}
