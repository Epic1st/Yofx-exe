# PostgreSQL role boundary

Runtime processes must connect through separate `NOSUPERUSER NOBYPASSRLS
NOCREATEDB NOCREATEROLE` login roles that inherit exactly one of these
deployment-provisioned capability roles:

- `yo4x_control_api`
- `yo4x_admin_bff`
- `yo4x_emergency`
- `yo4x_secret_ingestion`
- `yo4x_worker`

`yo4x_migrator` is the only DDL/owner role. It must be a separate deployment
credential, own the YO4X schemas and relations, and never be granted to a
runtime login. A migration login using a group owner should `SET ROLE
yo4x_migrator` before applying migrations so newly created objects retain the
same owner.

Construct the migration data source with `PostgresDatabaseUsage.Migrator`.
`MigrateAsync` rejects the default runtime usage, while runtime raw connections
and tenant transactions execute the role-safety assertion before use.

Provision role/login membership outside application deployment (passwords,
certificates, and managed-identity bindings do not belong in source control),
run the embedded migrations as the migrator, then apply
`least_privilege_roles.sql` as a role administrator. Reapply that grant script
when a migration adds a relation.

Every runtime transaction invokes `control.assert_safe_runtime_role()` before
setting tenant context. It rejects superusers, `BYPASSRLS`, database/schema/table
owners (including owner-role members), and roles with database `CREATE`. Runtime
roles receive no `DELETE`, schema `CREATE`, secret-material, or audit mutation
privilege. Tenant-owned tables additionally use `FORCE ROW LEVEL SECURITY` and
transaction-local tenant/actor/correlation/session settings.
