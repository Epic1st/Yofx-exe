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
        if (await applications.FindByClientIdAsync(
                LocalIdentityContract.ClientId,
                cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        await applications.CreateAsync(
            new OpenIddictApplicationDescriptor
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
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
