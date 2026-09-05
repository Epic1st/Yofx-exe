using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace YO4X.DevelopmentIdentity;

public static class DevelopmentIdentityRegistration
{
    public static IServiceCollection AddDevelopmentIdentityProvider(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        DevelopmentIdentityStartupGuard.Validate(environment, configuration);
        _ = LocalIdentityPostgresOptions.TryCreate(
            configuration.GetConnectionString("LocalIdentityPostgres"),
            out LocalIdentityPostgresOptions? postgresOptions);
        services.AddSingleton(postgresOptions!);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<LocalIdentityProvisioner>();

        string databasePath = Path.GetFullPath(
            configuration["LocalIdentity:DatabasePath"]
                ?? Path.Combine(environment.ContentRootPath, ".local", "identity.db"));
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        string stateDirectory = Path.GetDirectoryName(databasePath)!;

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(stateDirectory, "data-protection")))
            .SetApplicationName("YO4X.DevelopmentIdentity");
        services.Configure<FormOptions>(options =>
        {
            options.ValueCountLimit = 16;
            options.ValueLengthLimit = 4096;
            options.MultipartBodyLengthLimit = 64 * 1024;
        });

        services.AddDbContext<DevelopmentIdentityDbContext>(options =>
        {
            options.UseSqlite($"Data Source={databasePath}");
            options.UseOpenIddict();
        });

        services.AddIdentity<DevelopmentUser, IdentityRole<Guid>>(options =>
        {
            options.Password.RequiredLength = 12;
            options.Password.RequiredUniqueChars = 4;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.SignIn.RequireConfirmedEmail = true;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<DevelopmentIdentityDbContext>()
        .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "__Host-yo4x-local-identity";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.LoginPath = "/account/sign-in";
        });

        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "__Host-yo4x-local-antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.FormFieldName = "__RequestVerificationToken";
        });

        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore()
                .UseDbContext<DevelopmentIdentityDbContext>())
            .AddServer(options =>
            {
                options.SetIssuer(new Uri(LocalIdentityContract.Issuer));
                options.SetAuthorizationEndpointUris("/connect/authorize");
                options.SetTokenEndpointUris("/connect/token");
                options.AllowAuthorizationCodeFlow();
                options.RequireProofKeyForCodeExchange();
                options.RegisterScopes(
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.Profile);
                options.SetAccessTokenLifetime(TimeSpan.FromHours(8));
                options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(2));
                options.AddDevelopmentEncryptionCertificate();
                options.AddDevelopmentSigningCertificate();
                options.DisableAccessTokenEncryption();
                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();
            });

        services.AddCors(options => options.AddPolicy("development-frontend", policy =>
            policy.WithOrigins([.. LocalIdentityContract.AllowedFrontendOrigins])
                .WithMethods("GET", "POST")
                .WithHeaders("Content-Type")
                .DisallowCredentials()));
        services.AddControllersWithViews();
        services.AddHostedService<DevelopmentIdentityInitializer>();
        return services;
    }
}
