using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace YO4X.Api;

public static class AuthenticationSchemes
{
    public const string User = "yo4x-user";
    public const string Workload = "yo4x-workload";
    public const string Admin = "yo4x-admin-session";
    public const string Emergency = "yo4x-emergency";
}

public static class AuthenticationExtensions
{
    public static IServiceCollection AddYo4xUserAndWorkloadAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = AuthenticationSchemes.User;
            options.DefaultChallengeScheme = AuthenticationSchemes.User;
        })
        .AddJwtBearer(AuthenticationSchemes.User, options => ConfigureJwt(options, configuration, "User"))
        .AddJwtBearer(AuthenticationSchemes.Workload, options => ConfigureJwt(options, configuration, "Workload"));

        services.AddAuthorizationBuilder()
            .AddPolicy("user", policy =>
            {
                policy.AddAuthenticationSchemes(AuthenticationSchemes.User);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("session_id");
                policy.RequireClaim("tenant_id");
            })
            .AddPolicy("workload", policy =>
            {
                policy.AddAuthenticationSchemes(AuthenticationSchemes.Workload);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("tenant_id");
                policy.RequireClaim("workload_id");
                policy.RequireClaim("worker_instance_id");
                policy.RequireClaim("deployment_id");
                policy.RequireClaim("broker_account_id");
                policy.RequireClaim("generation");
                policy.RequireClaim("region");
                policy.RequireClaim("component", "supervisor", "strategy_host", "gateway_host");
                policy.RequireClaim("certificate_sha256");
            });

        return services;
    }

    public static IServiceCollection AddYo4xAdminAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = AuthenticationSchemes.Admin;
            options.DefaultChallengeScheme = AuthenticationSchemes.Admin;
        })
        .AddCookie(AuthenticationSchemes.Admin, options =>
        {
            options.Cookie.Name = "__Host-yo4x-admin";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            options.SlidingExpiration = false;
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                },
                OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorizationBuilder()
            .AddPolicy("admin", policy =>
            {
                policy.AddAuthenticationSchemes(AuthenticationSchemes.Admin);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("sub");
                policy.RequireClaim("tenant_id");
                policy.RequireClaim("admin_session_id");
                policy.RequireClaim("mfa", "hardware_key", "webauthn");
                policy.RequireClaim("managed_device", "true");
                policy.RequireClaim("environment");
                policy.RequireClaim("auth_time");
            });

        return services;
    }

    public static IServiceCollection AddYo4xEmergencyAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = AuthenticationSchemes.Emergency;
            options.DefaultChallengeScheme = AuthenticationSchemes.Emergency;
        })
        .AddJwtBearer(AuthenticationSchemes.Emergency, options => ConfigureJwt(options, configuration, "Emergency"));

        services.AddAuthorizationBuilder()
            .AddPolicy("emergency", policy =>
            {
                policy.AddAuthenticationSchemes(AuthenticationSchemes.Emergency);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("sub");
                policy.RequireClaim("tenant_id");
                policy.RequireClaim("session_id");
                policy.RequireClaim("mfa", "hardware_key", "webauthn");
                policy.RequireClaim("authority", "restrict_only");
                policy.RequireClaim("environment");
                policy.RequireClaim("auth_time");
            });

        return services;
    }

    private static void ConfigureJwt(JwtBearerOptions options, IConfiguration configuration, string sectionName)
    {
        IConfigurationSection section = configuration.GetSection($"Authentication:{sectionName}");
        options.Authority = section["Authority"];
        options.Audience = section["Audience"];
        options.RequireHttpsMetadata = true;
        options.MapInboundClaims = false;
        options.SaveToken = false;
        options.RefreshOnIssuerKeyNotFound = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "permission"
        };
    }
}
