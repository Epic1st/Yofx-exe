using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace YO4X.DevelopmentIdentity;

internal sealed class DevelopmentIdentityInitializer(
    IServiceProvider serviceProvider) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();
        DevelopmentIdentityDbContext database =
            scope.ServiceProvider.GetRequiredService<DevelopmentIdentityDbContext>();
        await database.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        IOpenIddictApplicationManager applications =
            scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        object? application = await applications.FindByClientIdAsync(
            LocalIdentityContract.ClientId,
            cancellationToken).ConfigureAwait(false);
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = LocalIdentityContract.ClientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "YO4X local web development client",
            RedirectUris = { new Uri(LocalIdentityContract.RedirectUri) },
            PostLogoutRedirectUris = { new Uri(LocalIdentityContract.PostLogoutRedirectUri) },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Prefixes.Scope + Scopes.OpenId
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange }
        };
        if (application is null)
        {
            await applications.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await applications.UpdateAsync(application, descriptor, cancellationToken)
                .ConfigureAwait(false);
        }

        Microsoft.AspNetCore.Identity.UserManager<DevelopmentUser> userManager =
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<DevelopmentUser>>();
        LocalIdentityProvisioner provisioner =
            scope.ServiceProvider.GetRequiredService<LocalIdentityProvisioner>();

        string[] seedEmails = ["test@test", "test@test.com", "dev@example.com"];
        foreach (string seedEmail in seedEmails)
        {
            DevelopmentUser? existing = await userManager.FindByEmailAsync(seedEmail).ConfigureAwait(false);
            if (existing is null)
            {
                var user = new DevelopmentUser
                {
                    Id = Guid.CreateVersion7(),
                    UserName = seedEmail,
                    Email = seedEmail,
                    EmailConfirmed = true,
                    TenantId = LocalIdentityContract.TenantId,
                    SessionId = Guid.CreateVersion7()
                };
                Microsoft.AspNetCore.Identity.IdentityResult result =
                    await userManager.CreateAsync(user, "Password123!@#").ConfigureAwait(false);
                if (result.Succeeded)
                {
                    try
                    {
                        await provisioner.ProvisionAsync(user, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort Postgres sync on startup
                    }
                }
            }
            else
            {
                try
                {
                    await provisioner.ProvisionAsync(existing, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // best effort
                }
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
