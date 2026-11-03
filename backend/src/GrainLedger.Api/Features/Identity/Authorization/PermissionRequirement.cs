using Microsoft.AspNetCore.Authorization;

namespace GrainLedger.Api.Features.Identity;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;
