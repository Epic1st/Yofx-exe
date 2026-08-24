# PostgreSQL role boundary

Tenant-data processes connect directly as these exact deployment-provisioned
`LOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE
NOREPLICATION` roles:

- `yo4x_control_api`
- `yo4x_admin_bff`
- `yo4x_emergency`
- `yo4x_secret_ingestion`
- `yo4x_conversion_worker`
- `yo4x_strategy_verifier`
- `yo4x_runtime_evidence`
- `yo4x_worker`
- `yo4x_supervisor_runtime`
- `yo4x_trade_authorizer`
- `yo4x_gateway_runtime`

Do not put a wrapper login in one of these roles and do not grant these roles
membership in another role. Application registration, runtime readiness, and
`control.assert_safe_runtime_role()` deliberately require the exact
`current_user` and reject role membership. Passwords, certificates, and managed
identity bindings remain external deployment secrets even though the database
role names are fixed.

Tenant authority is not taken from a custom PostgreSQL setting. A runtime
transaction first obtains its exact database, direct-role, backend-PID, and full
`xid8` binding. A separately credentialed `yo4x_context_issuer` connection
stores only a SHA-256 digest for a fresh one-use 256-bit capability, bound to
that transaction and its exact tenant, actor, correlation, and optional session
identity. The runtime presents the raw capability once. RLS reads only the
activated protected row; caller-written GUCs, a pooled session, another role,
backend, transaction, database, or expired capability cannot establish tenant
authority.

`yo4x_context_authority` is an isolated `NOLOGIN NOINHERIT` owner of only the
tenant-context capability relation and its security-definer functions. It has
no memberships. `yo4x_context_issuer` is a direct, non-member login with
execute-only issuance and cleanup rights plus the minimum readiness metadata.
The issuer connection is a separate deployment secret and production
registration requires the exact role and PostgreSQL `VerifyFull` TLS.

The current in-process provider protects against theft or arbitrary SQL use of
a runtime database credential; it does not preserve this separation after
compromise of a process that also holds the issuer configuration. A deployment
requiring that stronger process-compromise boundary must move issuance behind a
separately authenticated authority that validates the caller's workload and
scope. GatewayHost does not receive the general issuer credential, and the
conversion worker derives its transaction binding only from a verified,
one-time strategy-import capability.

`yo4x_runtime_evidence` is owned only by the authenticated user-operation
result ingress. It can invoke execute-only capabilities to append one terminal,
redacted broker or deployment result for an exact frozen dispatch, but cannot
write either proof table directly or mutate accounts, deployments,
assignments, operations, or projection state. `yo4x_worker` can read immutable
results and project confirmed state, but cannot insert or alter result evidence.
The control-plane API binds this pool from
`ConnectionStrings:RuntimeEvidencePostgres`; production registration requires
the exact role name and PostgreSQL `VerifyFull` TLS.

`yo4x_migrator` is the isolated `NOLOGIN NOINHERIT` DDL/owner role. It owns the
YO4X database, schemas, and ordinary objects, has no memberships, and is never
reachable through a runtime login. It is not a credential and operators must
not attempt to `SET ROLE` into it.

Construct the migration data source with `PostgresDatabaseUsage.Migrator` and
an offline, separately protected PostgreSQL superuser credential. This is the
only deployment executor proven by the integration harness: membership into
the no-login owners is forbidden, while the role script must transfer object
and database ownership and repair cluster-global ACLs. The script preflights
that `session_user = current_user` and that the direct executor is a true
PostgreSQL superuser before its first mutation. A managed-provider equivalent
is unsupported until that provider's exact ownership/ACL procedure is tested
separately. `MigrateAsync` rejects default runtime usage. The offline superuser
applies the embedded migration and then `least_privilege_roles.sql`; reapply
the role script whenever a migration adds an object. This credential is never
configured in an application process.

Provision the exact runtime login roles and their credential bindings outside
application deployment; credentials do not belong in source control. The role
contract is dedicated-cluster scoped: runtime and PUBLIC access is removed from
non-target databases and from unapproved schema, object, parameter, large-object,
and advisory-lock capability surfaces.

This SQL boundary protects tenant confidentiality and integrity from a stolen
runtime database credential; it does not contain arbitrary-SQL availability.
PostgreSQL `USERSET` timeouts and memory settings can be changed by the login,
and finite role connection slots can still consume shared CPU, memory, temp
storage, locks, or LISTEN/NOTIFY capacity. Production must expose runtime
credentials only through an authenticated workload/network boundary or an
allowlisting SQL proxy, keep application pools below the pinned role limits,
reserve administrator connections, enforce database/host resource and temp-file
quotas, terminate long transactions/sessions with an independent watchdog, and
monitor connection, lock, temp-file, WAL, and notification-queue pressure.

Worker fairness is process-independent. PostgreSQL stores one migration-seeded
cursor for each fixed global workstream and one tenant-private deployment cursor.
The worker can update only the relevant cursor identifier; trigger guards derive
scan timestamps, completed rotations, counters, and row versions from the exact
next catalog UUID. Readiness requires a complete rotation within the configured
SLA. This proves cursor liveness, not that a compromised worker performed the
business work: a holder of the worker credential can still invoke legitimate
next-cursor transitions without processing the selected item. Independent work
outcome, queue-age, error-rate, and business-state monitoring therefore remains
required.

Every runtime transaction invokes `control.assert_safe_runtime_role()` before
activating tenant context. It rejects superusers, `BYPASSRLS`, role membership,
database/schema/table owners, unsafe session posture, prepared transactions, and
roles with database `CREATE`. Runtime roles receive no schema `CREATE`, raw
secret-material, `DELETE`, or audit-rewrite privilege; any audit access is an
exact append-only grant or a guarded function edge. Tenant-owned tables use
`FORCE ROW LEVEL SECURITY`; the protected capability expires by statement time
and is bound to one full transaction identifier.
