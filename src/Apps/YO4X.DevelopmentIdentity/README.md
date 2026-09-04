# YO4X development identity provider

This is a real, local-only OpenID Connect provider for frontend development. It
uses ASP.NET Core Identity password hashing and lockout, SQLite persistence,
HTTP-only secure authentication and antiforgery cookies, and OpenIddict 7.6.0.
It supports only authorization-code flow with PKCE for one public client at
`http://127.0.0.1:5173`; it has no password grant, client secret, static bearer,
production mode, or non-loopback client.

The app refuses to start unless the environment is `Development` and the
operator explicitly sets `LocalIdentity:Enabled=true`:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:LocalIdentity__Enabled = 'true'
$env:ConnectionStrings__LocalIdentityPostgres = 'Host=127.0.0.1;Database=yo4x;Username=yo4x_local_identity;Password=<local-only-secret>;Include Error Detail=false;Log Parameters=false'
dotnet run --project src/Apps/YO4X.DevelopmentIdentity
```

Discovery is served from
`https://127.0.0.1:7210/.well-known/openid-configuration`. The client identifier
is `yo4x-web-development`, its redirect URI is
`http://127.0.0.1:5173/auth/callback`, and its post-logout redirect is
`http://127.0.0.1:5173/`. A browser client must use PKCE with a fresh verifier,
state, and nonce, and keep access tokens in memory only.

Registration is deliberately local and marks the submitted address confirmed
because this provider has no email delivery boundary. That is a development-only
claim and must never be interpreted as production email-verification evidence.
The provider issues UUID `sub`, `tenant_id`, and `session_id` claims together
with `email` and `email_verified=true`. Access tokens use the
`yo4x-control-plane` audience.

Registration and sign-in call the PostgreSQL execute-only provisioning seam.
The connection must use the dedicated `yo4x_local_identity` login on loopback;
the app refuses to start without it. The database stores only normalized email,
UUID authority, and a session expiry of at most 30 minutes. Identity passwords,
cookies, authorization codes, and access tokens remain in the local provider
boundary and are never written to ControlPlane PostgreSQL.

SQLite state, persisted data-protection keys, and development certificates are
local machine artifacts. Delete the app's `.local` directory to discard
development accounts and cookies. Do not copy its database, data-protection
keys, or certificates into another environment.
