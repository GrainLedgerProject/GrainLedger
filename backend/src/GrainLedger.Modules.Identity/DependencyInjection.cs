using Microsoft.AspNetCore.Builder;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace GrainLedger.Modules.Identity;

public static class IdentityModuleRegistration
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddDbContext<IdentityDbContext>((provider, options) =>
        {
            var connection = provider.GetRequiredService<IConfiguration>().GetConnectionString("GrainLedger");
            if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("ConnectionStrings:GrainLedger is required.");
            options.UseNpgsql(connection, n => n.MigrationsAssembly(typeof(IdentityDbContext).Assembly.FullName));
        });
        services.AddHttpContextAccessor();
        var keyPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keyPath)) services.AddDataProtection().SetApplicationName("GrainLedger").PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICurrentAccess, CurrentAccess>();
        services.AddScoped<GoogleIdentityProvisioner>();
        services.AddScoped<LocalAuthService>();
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<JwtConfiguration>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
        services.AddScoped<SessionCookieEvents>();
        services.AddScoped<PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler>(sp => sp.GetRequiredService<PermissionAuthorizationHandler>());
        services.AddAuthorization(o =>
        {
            foreach (var permission in Roles.All.Values.SelectMany(role => role.Permissions).Distinct())
                o.AddPolicy(permission, p => p.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
        });
        services.AddAntiforgery(o =>
        {
            o.HeaderName = "X-CSRF-TOKEN";
            o.Cookie.Name = environment.IsDevelopment() ? "GrainLedger.Csrf" : "__Host-GrainLedger.Csrf";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy("authentication", http =>
            RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));
        });

        services.AddAuthentication(o =>
        {
            o.DefaultAuthenticateScheme = AuthSchemes.Smart;
            o.DefaultSignInScheme = AuthSchemes.Application;
            o.DefaultChallengeScheme = AuthSchemes.Smart;
        })
        .AddPolicyScheme(AuthSchemes.Smart, AuthSchemes.Smart, o => o.ForwardDefaultSelector = http =>
            http.Request.Headers.ContainsKey("Authorization") ? AuthSchemes.Bearer : AuthSchemes.Application)
        .AddCookie(AuthSchemes.Application, o =>
        {
            o.Cookie.Name = environment.IsDevelopment() ? "GrainLedger.Session" : "__Host-GrainLedger.Session";
            o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax; o.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(15); o.SlidingExpiration = true;
            o.EventsType = typeof(SessionCookieEvents);
        })
        .AddCookie(AuthSchemes.External, o =>
        {
            o.Cookie.Name = environment.IsDevelopment() ? "GrainLedger.External" : "__Host-GrainLedger.External";
            o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax; o.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        })
        .AddJwtBearer(AuthSchemes.Bearer, o =>
        {
            var jwt = new JwtConfiguration(configuration);
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = jwt.SigningKey,
                ValidateIssuer = true, ValidIssuer = jwt.Issuer, ValidateAudience = true, ValidAudience = jwt.Audience,
                ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30), NameClaimType = "sub",
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
            };
            o.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    if (!jwt.IsConfigured || !Guid.TryParse(context.Principal?.FindFirstValue("sub"), out var userId) ||
                        !Guid.TryParse(context.Principal?.FindFirstValue("sid"), out var sessionId))
                    { context.Fail("Invalid bearer session."); return; }
                    var db = context.HttpContext.RequestServices.GetRequiredService<IdentityDbContext>();
                    var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
                    var valid = await db.Sessions.AsNoTracking().AnyAsync(x => x.Id == sessionId && x.UserId == userId &&
                        x.RevokedAt == null && x.ExpiresAt > now && x.User.IsActive, context.HttpContext.RequestAborted);
                    if (!valid) context.Fail("Bearer session is no longer active.");
                }
            };
        })
        .AddGoogle(AuthSchemes.Google, o =>
        {
            o.SignInScheme = AuthSchemes.External;
            o.ClientId = string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]) ? "not-configured" : configuration["Authentication:Google:ClientId"]!;
            o.ClientSecret = string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]) ? "not-configured" : configuration["Authentication:Google:ClientSecret"]!;
            o.CallbackPath = "/signin-google";
            o.UsePkce = true;
            o.ClaimActions.MapJsonKey("email_verified", "email_verified");
            o.SaveTokens = false;
            o.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();
                context.Response.Redirect(GoogleLoginDiagnostics.FailureRedirect(context.HttpContext,
                    GoogleLoginDiagnostics.Classify(context.Failure), context.Failure));
                return Task.CompletedTask;
            };
        });
        return services;
    }
}
