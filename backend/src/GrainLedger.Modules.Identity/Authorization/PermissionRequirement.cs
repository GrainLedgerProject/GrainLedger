using Microsoft.AspNetCore.Authorization;

namespace GrainLedger.Modules.Identity;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;
