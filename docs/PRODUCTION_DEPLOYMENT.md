# YO4X production deployment guide

This document describes how to move YO4X from the current local-development
setup to a production deployment. It covers configuration, PostgreSQL, the
central backend, background workers, the admin website, and the Windows desktop
application.

> **Important:** the repository is not currently approved for live production
> trading. The database and control-plane foundations are substantial, but live
> broker mutation is deliberately disabled and several production integrations
> are not implemented. See [Production go-live blockers](#production-go-live-blockers)
> before deploying anything that can affect a real account.

## 1. Production architecture

Use separate public endpoints and keep PostgreSQL and workers on private
networks:

```text
Users' Windows PCs
  YO4X Desktop + embedded React UI + local bot runtime
       | outbound HTTPS 443 only
       +---- api.yo4x.com --------> Control Plane API
       +---- auth.yo4x.com -------> production OIDC identity provider
       +---- updates.yo4x.com ----> signed desktop releases and manifest

Administrators
       | HTTPS 443 + MFA
       v
  admin.yo4x.com -> WAF/reverse proxy -> production Admin UI/BFF

Private application network
  Control Plane API
  Control Plane Workers
  Secret Ingestion API
  Conversion Workers on isolated Windows runners
  Emergency Safety API
  Runtime services when production execution is implemented
       |
       +---- managed PostgreSQL 18 (private endpoint, TLS VerifyFull)
       +---- private durable package/object storage
       +---- secret manager / HSM-backed signing service
       +---- centralized logs, metrics and alerts
```

The **central backend** is not a single executable. Its public entry point is
`src/Apps/YO4X.ControlPlane.Api`, supported by private services and workers.
Do not make worker, conversion, secret-ingestion, runtime, or database ports
public.

### Public DNS and ports

| Endpoint | Public port | Purpose |
|---|---:|---|
| `api.yo4x.com` | 443 | User and desktop Control Plane API |
| `auth.yo4x.com` | 443 | Production OIDC provider |
| `admin.yo4x.com` | 443 | Admin UI and Admin BFF |
| `updates.yo4x.com` | 443 | Signed desktop packages and update manifests |

Internal Kestrel ports may be chosen by the platform, but should sit behind a
load balancer or reverse proxy. PostgreSQL uses TCP 5432 on the private network.
Local development ports such as 5184, 7209, 7210, 4173 and 5173 must not be
published to the internet.

## 2. Environments and secret handling

Maintain isolated `development`, `staging`, and `production` environments. Each
must have its own database, credentials, encryption keys, signing keys, OIDC
clients, storage buckets, DNS names, and audit destination. Never copy a
production secret into development.

The checked-in `.env.example` files are documentation templates, not secrets:

- Backend template: `src/Apps/YO4X.ControlPlane.Api/.env.example`
- Desktop template: `src/Apps/YO4X.Desktop/yo4x.desktop.env.example`
- Embedded web build template: `src/Frontend/YO4X.Web/.env.example`
- Local PostgreSQL Compose template: `.env.example`

### Rules

1. Store production secrets in the hosting platform's secret manager. Inject
   them as environment variables or mounted, read-only secret files.
2. Do not commit populated `.env` files, certificates, passwords, private keys,
   publication secrets, MT5 credentials, or connection strings.
3. Give every service an independent database password or managed identity.
   Rotate one service without changing another.
4. Restrict mounted secret files to the service identity. Do not place them in
   the web root, release ZIP, container image, logs, crash dumps, or backups that
   have a wider audience.
5. Values beginning with `VITE_` are compiled into browser JavaScript and are
   public. They may contain URLs and public client IDs only—never credentials.
6. `yo4x.desktop.env` is shipped to customers and is also public. It may contain
   backend and identity URLs only.
7. Rotate secrets on a schedule and immediately after suspected disclosure.
   Keep an auditable, tested dual-key overlap process for signing-key rotation.

ASP.NET configuration uses double underscores to represent nested keys. For
example, `ConnectionStrings__Postgres` maps to
`ConnectionStrings:Postgres`.

### Backend environment file behavior

The Control Plane API loads configuration in this order:

1. Existing process environment variables.
2. A file named by `YO4X_BACKEND_ENV_FILE`.
3. Otherwise, `.env` beside the executable or in its working directory.

Existing process variables take precedence over file values. The loader accepts
only approved prefixes and rejects a file larger than 256 KiB. In production,
prefer direct secret-manager injection. If a mounted file is used, set
`YO4X_BACKEND_ENV_FILE` to its absolute path and mount it read-only.

Example Control Plane configuration (replace every example value):

```dotenv
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=https://0.0.0.0:7209

ConnectionStrings__Postgres=Host=postgres.internal;Port=5432;Database=yo4x;Username=yo4x_control_api;Password=<secret>;SSL Mode=VerifyFull;Root Certificate=C:\secure\postgres-ca.crt
ConnectionStrings__ContextIssuer=Host=postgres.internal;Port=5432;Database=yo4x;Username=yo4x_context_issuer;Password=<different-secret>;SSL Mode=VerifyFull;Root Certificate=C:\secure\postgres-ca.crt
ConnectionStrings__RuntimePostgres=Host=postgres.internal;Port=5432;Database=yo4x;Username=yo4x_worker;Password=<different-secret>;SSL Mode=VerifyFull;Root Certificate=C:\secure\postgres-ca.crt
ConnectionStrings__RuntimeEvidencePostgres=Host=postgres.internal;Port=5432;Database=yo4x;Username=yo4x_runtime_evidence;Password=<different-secret>;SSL Mode=VerifyFull;Root Certificate=C:\secure\postgres-ca.crt

Authentication__User__Authority=https://auth.yo4x.com/
Authentication__User__Audience=yo4x-control-plane
Authentication__Workload__Authority=https://auth.yo4x.com/
Authentication__Workload__Audience=yo4x-runtime

Frontend__AllowedOrigins__0=http://127.0.0.1:4173
Frontend__AllowedOrigins__1=http://127.0.0.1:4174
MarketplacePublication__SharedSecretFile=C:\secure\marketplace-publication.secret
MarketplacePublication__PackageKeyDocumentFile=C:\secure\package-keys.json
MarketplacePublication__ArtifactRoot=D:\yo4x-data\strategy-packages
MarketplacePublication__TenantId=<production-tenant-uuid>
MarketplacePublication__ActorId=<production-admin-actor-uuid>
```

The user UI exists only in the desktop app, so no public `app.yo4x.com` origin
is needed. Permit only the exact loopback origins the production desktop shell
actually uses. Never use `*` with credentialed CORS.

The API currently targets `net10.0-windows10.0.19041.0`; deploy it on a supported
Windows Server VM or Windows container with the required .NET 10 runtime, or
publish it self-contained. Terminate public TLS at a managed load balancer and
also encrypt the load-balancer-to-service hop where the platform permits it.

### Desktop runtime configuration

Place `yo4x.desktop.env` beside `YO4X.exe` before signing and packaging:

```dotenv
YO4X_CONTROL_API_ORIGIN=https://api.yo4x.com/
YO4X_DESKTOP_IDENTITY_URL=https://auth.yo4x.com/
```

Do not ship `YO4X_DESKTOP_IDENTITY_CERTIFICATE_SHA256` in production. That
setting is for a development loopback certificate; production endpoints should
use certificates issued by a publicly trusted CA.

## 3. PostgreSQL production deployment

### Required service

Use a managed PostgreSQL 18 service or a separately operated PostgreSQL 18
cluster with:

- a private network endpoint and firewall allowlist;
- TLS with full hostname and CA verification;
- encryption at rest;
- multi-zone/high-availability failover;
- automated backups and point-in-time recovery;
- connection, CPU, memory, lock, long-transaction, temp-file, WAL and disk
  alerts;
- reserved administrator connections and pool limits below each role's limit;
- a restore drill before launch and at least quarterly afterward.

The repository's `compose.yaml` is a loopback-bound developer database. It is
not the production database deployment.

Create the database from `template0` using UTF-8, libc locale provider, and `C`
collation/ctype, as required by
`docs/backend/POSTGRESQL_BASELINE_POLICY.md`.

### Roles

YO4X intentionally uses exact, non-inheriting roles. Important roles include:

- `yo4x_control_api`
- `yo4x_admin_bff`
- `yo4x_context_issuer`
- `yo4x_secret_ingestion`
- `yo4x_conversion_worker`
- `yo4x_strategy_verifier`
- `yo4x_runtime_evidence`
- `yo4x_worker`
- `yo4x_emergency`
- runtime-specific supervisor, authorizer, gateway and credential roles

Applications must connect directly as the role assigned to them. They must not
connect as `postgres`, a database owner, or `yo4x_migrator`, and roles must not
be nested through membership.

### Migration process

There are currently 23 ordered embedded migrations under
`src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations`. Their IDs and
checksums are enforced by the migration runner. Never edit an applied migration
or manually rewrite `control.schema_migrations`.

Use this release process:

1. Put the application in maintenance/drain mode when the migration's review
   requires it.
2. Take a backup and prove that it can be restored.
3. Run a one-off, offline migration job using a separately protected direct
   PostgreSQL superuser connection and `PostgresDatabaseUsage.Migrator`.
4. Call `PostgresDatabase.MigrateAsync()` from that job.
5. In the same controlled release procedure, execute
   `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql`.
   Reapply this role script after every migration that adds an object.
6. Run catalog/ACL/readiness verification, then destroy the migration job and
   remove its credential from the job environment.
7. Deploy backward-compatible application services and monitor readiness and
   error rates.

`src/Tools/YO4X.DevelopmentBootstrap` is explicitly development-specific and
creates `yo4x_development`; do not run it against production. The repository
does not yet contain a dedicated production migration executable. Build and
review that one-off deployment job before the first production database rollout.
Do not replace it with automatic migration during API startup.

## 4. Strategy package and conversion storage

MQ5 uploads must travel through the authenticated admin boundary to the central
backend. The backend records source metadata and conversion/publication state in
PostgreSQL; the produced `.yo4x` package belongs in private, durable artifact
storage, not inside the database as an unrestricted source blob and not on an
administrator's PC.

The current Control Plane settings use filesystem paths:

- `MarketplacePublication:ArtifactRoot`
- `MarketplacePublication:PackageKeyDocumentFile`
- `MarketplacePublication:SharedSecretFile`

A single production instance can use an encrypted, backed-up shared volume as an
interim solution. Multiple API replicas require shared durable storage with
atomic/versioned writes. The preferred production design is private object
storage with server-side encryption, object versioning, retention rules,
malware/secret scanning, immutable audit references, and short-lived authorized
downloads. Package signing or encryption keys should be protected by a managed
KMS/HSM and should not be readable by the admin browser.

Run conversion on isolated, disposable Windows workers because the MQL5 toolchain
and vendor binaries are Windows-specific. Workers should have no inbound public
port, obtain one job at a time through an authenticated queue, validate includes
and input sizes, enforce CPU/memory/time limits, publish only after verification,
and erase the workspace after completion. A package is visible to users only
after conversion, verification, signing, durable upload, and the database commit
all succeed.

## 5. Deploying the backend

Build immutable Release artifacts in CI from a reviewed commit. Do not build on
the production server.

Example publish commands from the repository root:

```powershell
dotnet restore YO4X.sln
dotnet test YO4X.sln -c Release --no-restore

dotnet publish src/Apps/YO4X.ControlPlane.Api/YO4X.ControlPlane.Api.csproj `
  -c Release -r win-x64 --self-contained true -o artifacts/prod/control-api

dotnet publish src/Apps/YO4X.Admin.Bff/YO4X.Admin.Bff.csproj `
  -c Release -r win-x64 --self-contained true -o artifacts/prod/admin-bff

dotnet publish src/Apps/YO4X.ControlPlane.Workers/YO4X.ControlPlane.Workers.csproj `
  -c Release -r win-x64 --self-contained true -o artifacts/prod/control-workers

dotnet publish src/Apps/YO4X.Conversion.Worker/YO4X.Conversion.Worker.csproj `
  -c Release -r win-x64 --self-contained true -o artifacts/prod/conversion-worker
```

Publish the other private APIs/runtime hosts only when their production
dependencies and trust boundaries are implemented and approved. Give each
process its own OS/container identity and configuration. Do not reuse the
Control Plane connection string for workers or the Admin BFF.

Deploy in this order:

1. Network, private DNS, public DNS, certificates, WAF and secret manager.
2. PostgreSQL, backups, roles and the reviewed one-off migration job.
3. Private artifact storage, KMS/signing configuration and queues.
4. Production OIDC provider and registered clients/audiences.
5. Control Plane API, then private workers and APIs.
6. Admin BFF and production admin UI.
7. A staging desktop release, end-to-end test, then a small production canary.
8. Gradual desktop release through the stable update channel.

All hosts that implement the standard health contract expose:

- `/health/live` for process liveness;
- `/health/startup` for startup completion;
- `/health/ready` for dependency readiness.

Use liveness only to restart a dead process. Remove a service from traffic when
readiness fails. Do not expose detailed health responses, stack traces, or
development exception pages publicly.

## 6. Identity and account creation

Do not deploy `src/Apps/YO4X.DevelopmentIdentity` to production. It is a local
development identity provider.

Select a production OIDC/OAuth 2.0 provider and configure:

- separate native desktop and admin web clients;
- Authorization Code flow with PKCE for the desktop;
- exact registered redirect URIs—no wildcards;
- short access-token lifetime and rotating refresh tokens;
- MFA and stronger conditional access for administrators;
- `yo4x-control-plane` and workload audiences;
- stable subject identifiers mapped to YO4X database profiles;
- email verification, account recovery, revocation and session audit flows.

The present frontend identity implementation is still centered on the fixed
development OIDC contract. Production identity configuration and the complete
desktop callback/session lifecycle must be implemented and tested before release;
changing only `YO4X_DESKTOP_IDENTITY_URL` is not sufficient evidence that
production sign-in will work.

## 7. Deploying the admin website

### Do not publish the current development portal

`src/Apps/YO4X.Admin.Portal/server.mjs` is a local developer tool. It:

- listens on the hard-coded development port 5184;
- accepts only a `127.0.0.1` Control Plane target;
- disables upstream certificate verification;
- stores sessions in process memory;
- authenticates against a local JSON file;
- reads a shared publication secret from a local file.

It must never be exposed to the internet or treated as the production admin
site.

### Production admin design

Use `src/Apps/YO4X.Admin.Bff` as the server-side security boundary and build a
production admin UI that talks only to that BFF. Before launch, connect the BFF
to the selected enterprise OIDC provider and require an explicit admin role and
MFA. Do not let browser JavaScript receive the marketplace publication secret,
database credentials, package keys, or a service token.

Configure at minimum:

```dotenv
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=https://0.0.0.0:7208
ConnectionStrings__AdminPostgres=Host=postgres.internal;Port=5432;Database=yo4x;Username=yo4x_admin_bff;Password=<secret>;SSL Mode=VerifyFull;Root Certificate=C:\secure\postgres-ca.crt
ConnectionStrings__ContextIssuer=Host=postgres.internal;Port=5432;Database=yo4x;Username=yo4x_context_issuer;Password=<different-secret>;SSL Mode=VerifyFull;Root Certificate=C:\secure\postgres-ca.crt
AdminSecurity__AllowedOrigins__0=https://admin.yo4x.com
```

The reverse proxy should terminate public TLS, set the canonical host, reject
oversized requests, add security headers, and forward only to the Admin BFF.
Keep the admin origin exact, cookies `Secure`, `HttpOnly` and `SameSite=Strict`,
retain antiforgery validation, deny framing, and use a restrictive content
security policy. Prefer a VPN or identity-aware proxy/IP allowlist in addition
to MFA. Record every upload, retry, publication, entitlement and administrative
change in immutable audit data.

Replacing the developer portal with this production flow is a required product
change, not only a server configuration change.

## 8. Deploying the Windows desktop app

The user frontend is embedded in `YO4X.exe`; users do not need a separately
hosted frontend website. The desktop app should run the authorized `.yo4x`
strategy locally and communicate with the backend over HTTPS for identity,
catalogue, entitlement, package and heartbeat data. Database credentials and
direct PostgreSQL access must never exist on a user's machine.

Production release procedure:

1. Build `src/Frontend/YO4X.Web` with production mode and development identity
   disabled.
2. Copy its `dist` output into `src/Apps/YO4X.Desktop/wwwroot` through the
   existing release script.
3. Create the public-only `yo4x.desktop.env` shown earlier.
4. Publish `YO4X.Desktop` as self-contained `win-x64` so customers are not
   required to install .NET separately.
5. Include the approved, pinned MT5 bridge dependency.
6. Do **not** copy every `.yo4x` file from `Testing/Mq5` into a production build.
   Ship only deliberately approved starter content, or download entitled signed
   packages after sign-in.
7. Authenticode-sign `YO4X.exe` and the installer with an EV or organization
   code-signing certificate and a trusted timestamp.
8. Generate the release ZIP/installer, SHA-256 digest and signed update manifest.
9. Scan the exact signed artifact, install it on a clean supported Windows VM,
   and test upgrade and rollback from the previous version.
10. Publish immutable versioned packages to the update bucket/CDN, then publish
    the short-cache manifest last.

The repository provides `scripts/Publish-OtaRelease.ps1`, for example:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Publish-OtaRelease.ps1 `
  -Version 1.1.0 `
  -Channel beta `
  -CertThumbprint <signing-certificate-thumbprint> `
  -S3Bucket s3://yo4x-updates
```

For production, signing must be mandatory. The current script only warns when
the certificate or `signtool.exe` is absent, so CI must fail the release if the
Authenticode signature is missing or invalid. It also currently gathers every
test `.yo4x` package; change that to an explicit approved manifest before use.
Protect the signing key in a hardware-backed key store or signing service and
verify the generated manifest/package independently before upload.

The app requires Windows 10 version 2004/build 19041 or later based on its target
framework. Validate the actual OS and WebView2 prerequisites on a clean machine.

## 9. CI/CD and promotion

Use one pipeline artifact and promote the exact same bytes from test to staging
to production. A recommended pipeline is:

1. Restore from the lock file and verify dependency integrity.
2. Run backend unit/integration tests against a fresh PostgreSQL 18 database.
3. Run frontend typecheck, tests, production build and dependency audit.
4. Run secret, SAST, dependency and malware scans.
5. Build immutable backend artifacts and a self-contained desktop artifact.
6. Produce an SBOM, hashes, version metadata and provenance attestation.
7. Deploy database migrations through a separately approved job.
8. Deploy staging, run smoke and end-to-end tests, then require approval.
9. Deploy backend canary, monitor, and expand.
10. Sign and publish desktop beta, then promote the verified digest to stable.

Never deploy directly from a developer's dirty working tree or from the
`artifacts` directory already present on a workstation. Tag the reviewed commit
and retain its build evidence.

## 10. Production verification

Run these checks in staging and again after each production deployment:

- Public endpoints present a valid certificate, force HTTPS and have expected
  security headers.
- `/health/live` and `/health/ready` succeed through the load balancer; private
  health endpoints cannot leak details to the internet.
- Each process connects using its exact PostgreSQL role and fails if given a
  more privileged or wrong role.
- Applied migration IDs/checksums and catalog/ACL fingerprints match the release.
- A new user can register/sign in, restart the app, refresh a token, sign out,
  revoke the session and recover an account without a redirect loop.
- An administrator with MFA can upload a harmless test MQ5 strategy; conversion
  occurs on an isolated runner; the signed `.yo4x` artifact is stored durably;
  and a failed conversion remains failed with a useful sanitized diagnostic.
- A free strategy creates an entitlement without payment. A paid strategy
  requires a verified completed checkout. Both are auditable and idempotent.
- An entitled desktop can download and verify a package; an unentitled user,
  modified package or replayed authorization cannot start it.
- Broker account linking stores only protected credential material through the
  approved secret boundary and never logs the password.
- Symbol resolution uses the connected broker account's imported symbol list
  and fails visibly when no approved mapping exists.
- The local runtime heartbeat is visible only while the desktop and bot are
  actually running; closing/sleeping the PC stops local execution.
- Backups restore into an isolated environment, and rollback procedures work.

Do not use a real-money trade as an ordinary deployment smoke test. Trade on a
dedicated demo account only after the production mutation safety path is
implemented, reviewed, enabled and independently authorized.

## 11. Rollback and recovery

- Keep the previous backend artifact deployable and use a canary/blue-green
  strategy. Roll back application binaries when schema compatibility permits.
- Treat released database migrations as immutable. Prefer an additive
  forward-fix; restore only through the reviewed disaster-recovery procedure.
- Version strategy artifacts immutably. Revoke a bad version and point the
  catalogue to a reviewed replacement; do not overwrite bytes at the same key.
- Keep the previous signed desktop release available. Use the update manifest
  to stop rollout or set a safe minimum version only after compatibility review.
- Define an emergency process to disable new executions, revoke tokens/keys,
  quarantine a conversion worker and disconnect a compromised broker binding.

## 12. Production go-live blockers

The current repository status explicitly leaves the following work unfinished:

- Live trading, raw order endpoints and production broker mutation are disabled.
- Production broker-command authorization and authenticated broker-observation
  provenance are absent.
- GatewayHost is proof-only; the authenticated cross-host transport, production
  outbox destination/consumer and restart coordinator are absent.
- A production write-only broker secret provider and signed receipt verifier
  have not been selected or configured.
- A trusted isolated Windows conversion/runtime runner and approved platform
  snapshot/signing key are not configured.
- Production OIDC identity and the desktop production login lifecycle are not
  complete.
- The local Node admin portal is not production-safe and must be replaced by a
  real Admin UI/BFF identity flow.
- Durable multi-instance object storage/KMS integration must replace or harden
  the current local filesystem publication model.
- A reviewed production database migration executable/job is missing.
- Historical credentials reportedly existed in remote Git history. Every
  affected credential must be revoked/rotated and remote history, caches and
  forks must be handled through a credential-incident procedure.
- Desktop release automation must fail closed on missing signatures and stop
  automatically bundling test strategies.

Until these are closed with staging evidence, YO4X may be deployed only as a
development/demo system and must not be represented as capable of safely placing
real trades.

## 13. Final launch checklist

- [ ] Production domains, certificates, WAF and private networking configured
- [ ] Development ports and database blocked from the internet
- [ ] Production OIDC configured; admin MFA and role checks proven
- [ ] Secret manager populated; no secret exists in frontend/desktop artifacts
- [ ] PostgreSQL 18 HA, TLS VerifyFull, PITR and restore drill proven
- [ ] Migration job and least-privilege role verification passed
- [ ] Durable package storage, KMS/signing and isolated conversion queue proven
- [ ] Backend readiness, logs, metrics, alerts and paging proven
- [ ] Admin developer portal replaced; admin audit and CSRF controls tested
- [ ] Desktop self-contained, Authenticode-signed and clean-VM tested
- [ ] OTA signature/hash/rollback and staged rollout tested
- [ ] Symbol mapping tested against every supported broker/account type
- [ ] All live-trading safety blockers closed and independently reviewed
- [ ] Credential-history incident remediation completed
- [ ] Runbooks, owners, escalation contacts, RTO and RPO approved
