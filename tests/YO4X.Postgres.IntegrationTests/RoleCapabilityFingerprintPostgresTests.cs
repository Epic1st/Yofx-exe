using Npgsql;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Api;
using YO4X.Persistence.Postgres;
using YO4X.RuntimeControl.Postgres;

namespace YO4X.Postgres.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class RoleCapabilityFingerprintPostgresTests(PostgresContainerFixture postgres)
{
    private readonly PostgresContainerFixture postgres = postgres;

    [PostgresFact]
    public async Task LiveCatalogMatchesTheExternallyPinnedSemanticBaseline()
    {
        postgres.RequireAvailable();
        string independentlyProvisioned;
        await using (PostgresTestDatabase firstDatabase = await postgres.CreateDatabaseAsync())
        {
            await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(firstDatabase);
            await using NpgsqlConnection firstAdministrator =
                await firstDatabase.Administrator.OpenConnectionAsync();
            independentlyProvisioned =
                await PostgresCatalogSemanticFingerprint.ComputeSha256Async(
                    firstAdministrator,
                    cancellationToken: CancellationToken.None);
        }

        // Runtime roles and their passwords are cluster-global, and the
        // dedicated-cluster role script intentionally withdraws their access
        // from every non-target database. Provision and attest the second
        // target only after closing all handles to the first target.
        await using PostgresTestDatabase secondDatabase = await postgres.CreateDatabaseAsync();
        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(secondDatabase);
        await using NpgsqlConnection secondAdministrator =
            await secondDatabase.Administrator.OpenConnectionAsync();
        string actual =
            await PostgresCatalogSemanticFingerprint.ComputeSha256Async(
                secondAdministrator,
                cancellationToken: CancellationToken.None);
        await using NpgsqlConnection runtimeWorker =
            await OpenNonPooledAsync(secondDatabase.WorkerConnectionString);
        string runtimeComputed =
            await PostgresCatalogSemanticFingerprint.ComputeSha256Async(
                runtimeWorker,
                cancellationToken: CancellationToken.None);

        Assert.Equal(actual, independentlyProvisioned);
        Assert.True(
            string.Equals(actual, runtimeComputed, StringComparison.Ordinal),
            $"Administrator {actual}; runtime {runtimeComputed}.");

        Assert.True(
            string.Equals(
                PostgresCatalogSemanticFingerprint.ExpectedSha256,
                actual,
                StringComparison.Ordinal),
            $"Expected {PostgresCatalogSemanticFingerprint.ExpectedSha256}; actual {actual}.");
    }

    [PostgresFact]
    public async Task RuntimeReadinessRequiresTheExactDeclarativeRoleContracts()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);

        bool controlRoleSatisfied = await IsSatisfiedAsync(
            database.ControlApi,
            Yo4xPostgresRoleContracts.ControlApi);
        Assert.True(
            controlRoleSatisfied,
            controlRoleSatisfied
                ? string.Empty
                : await DescribePrivilegeMismatchAsync(
                    database.ControlApi,
                    Yo4xPostgresRoleContracts.ControlApi));
        Assert.True(await ControlPlaneReadinessProbe.ProbeControlDatabaseAsync(
            database.ControlApi,
            SystemClock.Instance,
            CancellationToken.None));
        bool workerRoleSatisfied = await IsSatisfiedAsync(
            database.Worker,
            Yo4xPostgresRoleContracts.Worker);
        Assert.True(
            workerRoleSatisfied,
            workerRoleSatisfied
                ? string.Empty
                : await DescribePrivilegeMismatchAsync(
                    database.Worker,
                    Yo4xPostgresRoleContracts.Worker));
        await using var evidence = new RuntimeEvidencePostgresDatabase(
            database.RuntimeEvidenceConnectionString,
            database.TenantContextCapabilityProvider,
            allowInsecureLoopbackForDevelopment: true);
        Assert.True(await evidence.IsReadyAsync());
        await using var supervisor = new SupervisorUserOperationPostgresDatabase(
            database.SupervisorRuntimeConnectionString,
            database.TenantContextCapabilityProvider,
            allowInsecureLoopbackForDevelopment: true);
        await using var gateway = new GatewayUserOperationPostgresDatabase(
            database.GatewayRuntimeConnectionString,
            database.TenantContextCapabilityProvider,
            allowInsecureLoopbackForDevelopment: true);
        await using var credential = new CredentialUserOperationPostgresDatabase(
            database.CredentialRuntimeConnectionString,
            database.TenantContextCapabilityProvider,
            allowInsecureLoopbackForDevelopment: true);
        Assert.True(await supervisor.IsReadyAsync());
        Assert.True(await gateway.IsReadyAsync());
        Assert.True(await credential.IsReadyAsync());
        var secret = new PostgresCredentialIngestionGrantStore(database.SecretIngestion);
        Assert.True(await secret.IsReadyAsync(CancellationToken.None));

        await AssertDriftRejectedAndRestoreAsync(
            database,
            "revoke insert (id) on operations.deployments from yo4x_control_api",
            () => ControlPlaneReadinessProbe.ProbeControlDatabaseAsync(
                database.ControlApi,
                SystemClock.Instance,
                CancellationToken.None));

        await AssertDriftRejectedAndRestoreAsync(
            database,
            "revoke select on governance.strategy_version_source_bindings from yo4x_worker",
            () => IsSatisfiedAsync(database.Worker, Yo4xPostgresRoleContracts.Worker));

        await AssertDriftRejectedAndRestoreAsync(
            database,
            "grant select (credential_reference) on operations.broker_accounts to yo4x_worker",
            () => IsSatisfiedAsync(database.Worker, Yo4xPostgresRoleContracts.Worker));

        await AssertDriftRejectedAndRestoreAsync(
            database,
            "grant select on governance.strategy_versions to yo4x_worker with grant option",
            () => IsSatisfiedAsync(database.Worker, Yo4xPostgresRoleContracts.Worker));

        await AssertDriftRejectedAndRestoreAsync(
            database,
            """
            do $$
            begin
                execute format(
                    'grant temporary on database %I to public',
                    current_database());
            end
            $$
            """,
            () => IsSatisfiedAsync(database.Worker, Yo4xPostgresRoleContracts.Worker));

        await AssertDriftRejectedAndRestoreAsync(
            database,
            "alter table governance.strategy_versions owner to yo4x_worker",
            () => IsSatisfiedAsync(database.Worker, Yo4xPostgresRoleContracts.Worker),
            "alter table governance.strategy_versions owner to yo4x_migrator");

        await AssertDriftRejectedAndRestoreAsync(
            database,
            """
            do $$
            begin
                execute format(
                    'alter database %I owner to yo4x_worker',
                    current_database());
            end
            $$
            """,
            () => IsSatisfiedAsync(database.Worker, Yo4xPostgresRoleContracts.Worker),
            """
            do $$
            begin
                execute format(
                    'alter database %I owner to yo4x_migrator',
                    current_database());
            end
            $$
            """);

        await AssertDriftRejectedAndRestoreAsync(
            database,
            "revoke select (sha256) on control.schema_migrations from yo4x_runtime_evidence",
            async () => await evidence.IsReadyAsync());

        await AssertDriftRejectedAndRestoreAsync(
            database,
            "revoke select (sha256) on control.schema_migrations from yo4x_secret_ingestion",
            () => secret.IsReadyAsync(CancellationToken.None).AsTask());
    }

    [PostgresFact]
    public async Task UserOperationProtocolRolesMatchTheirExactDeclarativeContracts()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);
        await using var evidence = new PostgresDatabase(
            database.RuntimeEvidenceConnectionString,
            PostgresDatabaseUsage.Runtime,
            database.TenantContextCapabilityProvider,
            allowInsecureLoopbackForDevelopment: true);

        await AssertExactAsync(database.SupervisorRuntime, Yo4xPostgresRoleContracts.SupervisorRuntime);
        await AssertExactAsync(database.GatewayRuntime, Yo4xPostgresRoleContracts.GatewayRuntime);
        await AssertExactAsync(database.CredentialRuntime, Yo4xPostgresRoleContracts.CredentialRuntime);
        await AssertExactAsync(evidence, Yo4xPostgresRoleContracts.RuntimeEvidence);

        async Task AssertExactAsync(
            PostgresDatabase roleDatabase,
            PostgresRoleCapabilityContract contract)
        {
            bool satisfied = await IsSatisfiedAsync(roleDatabase, contract);
            Assert.True(
                satisfied,
                satisfied
                    ? string.Empty
                    : await DescribePrivilegeMismatchAsync(roleDatabase, contract));
        }
    }

    [PostgresFact]
    public async Task RuntimeReadinessRejectsRoleDatabaseAndEffectiveSettingDrift()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using NpgsqlConnection existingWorker =
            await OpenNonPooledAsync(database.WorkerConnectionString);

        Assert.True(
            await IsSatisfiedAsync(
                existingWorker,
                Yo4xPostgresRoleContracts.Worker),
            await ReadRuntimePostureAsync(existingWorker));

        await AssertExistingSessionDriftAsync(
            administrator,
            existingWorker,
            "alter role yo4x_worker bypassrls",
            "alter role yo4x_worker nobypassrls");
        await AssertExistingSessionDriftAsync(
            administrator,
            existingWorker,
            "alter role yo4x_worker connection limit 0",
            "alter role yo4x_worker connection limit 32");
        await AssertExistingSessionDriftAsync(
            administrator,
            existingWorker,
            "alter role yo4x_worker valid until '2000-01-01T00:00:00Z'",
            "alter role yo4x_worker valid until 'infinity'");
        await AssertExistingSessionDriftAsync(
            administrator,
            existingWorker,
            """
            do $$
            begin
                execute format(
                    'alter database %I connection limit 0',
                    current_database());
            end
            $$
            """,
            """
            do $$
            begin
                execute format(
                    'alter database %I connection limit -1',
                    current_database());
            end
            $$
            """);

        await AssertReconnectDriftAndRoleRepairAsync(
            database,
            administrator,
            "alter role yo4x_worker set row_security = off");
        await AssertReconnectDriftAndRoleRepairAsync(
            database,
            administrator,
            """
            do $$
            begin
                execute format(
                    'alter database %I set default_transaction_read_only = on',
                    current_database());
            end
            $$
            """);
    }

    [PostgresFact]
    public async Task SemanticAttestationRejectsEveryProtectedCatalogDriftClass()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();

        string[] driftStatements =
        [
            "alter table identity.tenants disable row level security",
            """
            do $$
            declare target record;
            begin
                select namespace.nspname, relation.relname, policy.polname
                into strict target
                from pg_catalog.pg_policy as policy
                join pg_catalog.pg_class as relation on relation.oid = policy.polrelid
                join pg_catalog.pg_namespace as namespace
                  on namespace.oid = relation.relnamespace
                where namespace.nspname in
                    ('identity', 'authorization', 'control', 'operations',
                     'governance', 'audit', 'messaging', 'readmodel')
                order by namespace.nspname, relation.relname, policy.polname
                limit 1;
                execute format(
                    'drop policy %I on %I.%I',
                    target.polname, target.nspname, target.relname);
            end
            $$
            """,
            """
            do $$
            declare target record;
            begin
                select namespace.nspname, relation.relname
                into strict target
                from pg_catalog.pg_trigger as trigger
                join pg_catalog.pg_class as relation on relation.oid = trigger.tgrelid
                join pg_catalog.pg_namespace as namespace
                  on namespace.oid = relation.relnamespace
                where trigger.tgisinternal
                  and namespace.nspname in
                    ('identity', 'authorization', 'control', 'operations',
                     'governance', 'audit', 'messaging', 'readmodel')
                order by namespace.nspname, relation.relname
                limit 1;
                execute format(
                    'alter table %I.%I disable trigger all',
                    target.nspname, target.relname);
            end
            $$
            """,
            """
            do $$
            declare target record;
            begin
                select namespace.nspname, relation.relname, trigger.tgname
                into strict target
                from pg_catalog.pg_trigger as trigger
                join pg_catalog.pg_class as relation on relation.oid = trigger.tgrelid
                join pg_catalog.pg_namespace as namespace
                  on namespace.oid = relation.relnamespace
                where not trigger.tgisinternal
                  and namespace.nspname in
                    ('identity', 'authorization', 'control', 'operations',
                     'governance', 'audit', 'messaging', 'readmodel')
                order by namespace.nspname, relation.relname, trigger.tgname
                limit 1;
                execute format(
                    'alter table %I.%I disable trigger %I',
                    target.nspname, target.relname, target.tgname);
            end
            $$
            """,
            """
            create or replace function control.current_tenant_id()
            returns uuid
            language sql
            stable
            parallel safe
            set search_path = ''
            as $function$ select null::uuid $function$
            """,
            "alter function control.current_tenant_id() owner to postgres",
            """
            do $$
            declare target record;
            begin
                select namespace.nspname, relation.relname, attribute.attname
                into strict target
                from pg_catalog.pg_attrdef as default_value
                join pg_catalog.pg_class as relation
                  on relation.oid = default_value.adrelid
                join pg_catalog.pg_namespace as namespace
                  on namespace.oid = relation.relnamespace
                join pg_catalog.pg_attribute as attribute
                  on attribute.attrelid = relation.oid
                 and attribute.attnum = default_value.adnum
                where namespace.nspname in
                    ('identity', 'authorization', 'control', 'operations',
                     'governance', 'audit', 'messaging', 'readmodel')
                order by namespace.nspname, relation.relname, attribute.attnum
                limit 1;
                execute format(
                    'alter table %I.%I alter column %I drop default',
                    target.nspname, target.relname, target.attname);
            end
            $$
            """,
            """
            do $$
            declare target record;
            begin
                select namespace.nspname, relation.relname, constraint_record.conname
                into strict target
                from pg_catalog.pg_constraint as constraint_record
                join pg_catalog.pg_class as relation
                  on relation.oid = constraint_record.conrelid
                join pg_catalog.pg_namespace as namespace
                  on namespace.oid = relation.relnamespace
                where constraint_record.contype = 'c'
                  and namespace.nspname in
                    ('identity', 'authorization', 'control', 'operations',
                     'governance', 'audit', 'messaging', 'readmodel')
                order by namespace.nspname, relation.relname,
                    constraint_record.conname
                limit 1;
                execute format(
                    'alter table %I.%I drop constraint %I',
                    target.nspname, target.relname, target.conname);
            end
            $$
            """,
            """
            do $$
            declare target record;
            begin
                select namespace.nspname, index_relation.relname
                into strict target
                from pg_catalog.pg_index as index_record
                join pg_catalog.pg_class as index_relation
                  on index_relation.oid = index_record.indexrelid
                join pg_catalog.pg_class as table_relation
                  on table_relation.oid = index_record.indrelid
                join pg_catalog.pg_namespace as namespace
                  on namespace.oid = table_relation.relnamespace
                where namespace.nspname in
                    ('identity', 'authorization', 'control', 'operations',
                     'governance', 'audit', 'messaging', 'readmodel')
                  and not exists
                  (
                      select 1
                      from pg_catalog.pg_constraint as constraint_record
                      where constraint_record.conindid = index_relation.oid
                  )
                order by namespace.nspname, index_relation.relname
                limit 1;
                execute format(
                    'drop index %I.%I',
                    target.nspname, target.relname);
            end
            $$
            """,
            """
            create rule yo4x_fingerprint_drift as
            on delete to identity.invalidated_session_tokens
            do instead nothing
            """,
            """
            create procedure control.yo4x_fingerprint_drift()
            language sql
            as $procedure$ select 1 $procedure$
            """,
            "create sequence control.yo4x_fingerprint_drift_sequence",
            """
            create schema yo4x_fingerprint_drift;
            create table yo4x_fingerprint_drift.tenant_child ()
                inherits (identity.tenants)
            """,
            """
            create role yo4x_fingerprint_rogue noinherit;
            grant select on identity.tenants to yo4x_fingerprint_rogue
            """,
            """
            alter default privileges for role yo4x_migrator
                grant execute on functions to public;
            set local role yo4x_migrator;
            create function control.yo4x_fingerprint_public_canary()
            returns integer
            language sql
            as $function$ select 1 $function$;
            reset role;
            do $$
            begin
                if not exists
                (
                    select 1
                    from pg_catalog.pg_proc as function
                    cross join lateral pg_catalog.aclexplode(
                        coalesce(
                            function.proacl,
                            pg_catalog.acldefault('f', function.proowner)))
                        as privilege
                    where function.oid =
                        'control.yo4x_fingerprint_public_canary()'::regprocedure
                      and privilege.grantee = 0
                      and privilege.privilege_type = 'EXECUTE'
                ) then
                    raise exception 'PUBLIC canary capability was not inherited';
                end if;
            end
            $$
            """
        ];

        string baseline = await PostgresCatalogSemanticFingerprint.ComputeSha256Async(
            administrator,
            cancellationToken: CancellationToken.None);
        foreach (string driftSql in driftStatements)
        {
            await AssertSemanticDriftAsync(administrator, baseline, driftSql);
        }
    }

    [PostgresFact]
    public async Task RoleReapplicationClosesGlobalPublicAndRuntimeCapabilityDrift()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        string baseline = await PostgresCatalogSemanticFingerprint.ComputeSha256Async(
            administrator,
            cancellationToken: CancellationToken.None);
        bool logicalEmitAvailable;
        await using (var logicalEmitProbe = new NpgsqlCommand(
            "select pg_catalog.to_regprocedure(" +
            "'pg_catalog.pg_logical_emit_message(boolean,text,text)') is not null",
            administrator))
        {
            logicalEmitAvailable = (bool)(await logicalEmitProbe.ExecuteScalarAsync())!;
        }

        await using (var drift = new NpgsqlCommand(
            """
            grant usage, create on schema public to public;
            create function public.yo4x_public_definer_canary()
            returns name
            language sql
            security definer
            set search_path = ''
            as $function$ select current_user $function$;
            grant select on table pg_catalog.pg_authid to public, yo4x_worker;
            grant select (capability_sha256)
                on control.tenant_context_capabilities to yo4x_migrator;
            grant set on parameter session_replication_role to yo4x_worker;
            grant execute on function pg_catalog.lo_create(oid) to public;
            grant execute on function pg_catalog.pg_advisory_lock(bigint) to public;
            grant usage on schema pg_toast to public;
            do $drift$
            declare
                context_toast regclass;
            begin
                select relation.reltoastrelid::regclass
                into strict context_toast
                from pg_catalog.pg_class as relation
                where relation.oid =
                    'control.tenant_context_capabilities'::regclass;
                execute pg_catalog.format(
                    'grant select on table %s to public',
                    context_toast);
            end
            $drift$;
            alter default privileges for role yo4x_context_authority
                grant execute on functions to public;
            create schema yo4x_runtime_owner_canary authorization yo4x_worker;
            """,
            administrator))
        {
            await drift.ExecuteNonQueryAsync();
        }
        if (logicalEmitAvailable)
        {
            await using var logicalEmitDrift = new NpgsqlCommand(
                "grant execute on function " +
                "pg_catalog.pg_logical_emit_message(boolean,text,text) to public",
                administrator);
            await logicalEmitDrift.ExecuteNonQueryAsync();
        }

        string drifted = await PostgresCatalogSemanticFingerprint.ComputeSha256Async(
            administrator,
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(baseline, drifted);
        await using (NpgsqlConnection driftedWorker =
            await OpenNonPooledAsync(database.WorkerConnectionString))
        {
            await using var enableReplica = new NpgsqlCommand(
                "set session_replication_role = replica",
                driftedWorker);
            await enableReplica.ExecuteNonQueryAsync();
        }

        await PostgresContainerFixture.ApplyLeastPrivilegeRoleScriptAsync(administrator);
        await PostgresContainerFixture.ApplyLeastPrivilegeRoleScriptAsync(administrator);

        string repaired = await PostgresCatalogSemanticFingerprint.ComputeSha256Async(
            administrator,
            cancellationToken: CancellationToken.None);
        // Subtractive role repair removes every capability but deliberately does
        // not destroy unexpected operator-owned objects. The semantic manifest
        // therefore remains fail closed until those canaries are explicitly
        // removed by the protected deployment administrator.
        Assert.NotEqual(baseline, repaired);
        await using (var repairedPrivileges = new NpgsqlCommand(
            """
            select
                not has_schema_privilege('yo4x_worker', 'public', 'CREATE'),
                not has_table_privilege(
                    'yo4x_worker', 'pg_catalog.pg_authid', 'SELECT'),
                not exists
                (
                    select 1
                    from pg_catalog.pg_class as relation
                    cross join lateral pg_catalog.aclexplode(
                        coalesce(
                            relation.relacl,
                            pg_catalog.acldefault('r', relation.relowner)))
                        as privilege
                    where relation.oid = 'pg_catalog.pg_authid'::regclass
                      and privilege.grantee = 0
                      and privilege.privilege_type = 'SELECT'
                ),
                not has_parameter_privilege(
                    'yo4x_worker', 'session_replication_role', 'SET'),
                not has_column_privilege(
                    'yo4x_migrator',
                    'control.tenant_context_capabilities',
                    'capability_sha256',
                    'SELECT'),
                not exists
                (
                    select 1
                    from pg_catalog.pg_namespace as namespace
                    cross join lateral pg_catalog.aclexplode(namespace.nspacl)
                        as privilege
                    where namespace.nspname = 'pg_toast'
                      and privilege.grantee = 0
                      and privilege.privilege_type = 'USAGE'
                ),
                not exists
                (
                    select 1
                    from pg_catalog.pg_class as context_relation
                    join pg_catalog.pg_class as toast_relation
                      on toast_relation.oid = context_relation.reltoastrelid
                    cross join lateral pg_catalog.aclexplode(toast_relation.relacl)
                        as privilege
                    where context_relation.oid =
                        'control.tenant_context_capabilities'::regclass
                      and privilege.grantee = 0
                      and privilege.privilege_type = 'SELECT'
                ),
                not exists
                (
                    select 1
                    from pg_catalog.pg_default_acl as default_acl
                    join pg_catalog.pg_roles as owner
                      on owner.oid = default_acl.defaclrole
                    cross join lateral pg_catalog.aclexplode(default_acl.defaclacl)
                        as privilege
                    where owner.rolname = 'yo4x_context_authority'
                      and privilege.grantee = 0
                ),
                not has_function_privilege(
                    'yo4x_worker', 'pg_catalog.lo_create(oid)', 'EXECUTE'),
                coalesce(
                    not has_function_privilege(
                        'yo4x_worker',
                        pg_catalog.to_regprocedure(
                            'pg_catalog.pg_logical_emit_message(boolean,text,text)'),
                        'EXECUTE'),
                    true),
                not has_function_privilege(
                    'yo4x_worker', 'pg_catalog.pg_advisory_lock(bigint)', 'EXECUTE'),
                not has_function_privilege(
                    'yo4x_worker',
                    'public.yo4x_public_definer_canary()',
                    'EXECUTE'),
                (select owner.rolname
                 from pg_catalog.pg_namespace as namespace
                 join pg_catalog.pg_roles as owner
                   on owner.oid = namespace.nspowner
                 where namespace.nspname = 'yo4x_runtime_owner_canary')
                    = 'yo4x_migrator'
            """,
            administrator))
        await using (NpgsqlDataReader reader =
            await repairedPrivileges.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            for (int index = 0; index < reader.FieldCount; index++)
            {
                Assert.True(
                    reader.GetBoolean(index),
                    $"Reapplication privilege assertion {index} was false.");
            }

            Assert.False(await reader.ReadAsync());
        }

        await using NpgsqlConnection repairedWorker =
            await OpenNonPooledAsync(database.WorkerConnectionString);
        var deniedStatements = new List<string>
        {
            "set session_replication_role = replica",
            "select pg_catalog.lo_create(0::oid)",
            "select pg_catalog.pg_advisory_lock(1::bigint)",
            $"select control.acquire_u0_tenant_authority_lock('{Guid.CreateVersion7():D}'::uuid)"
        };
        if (logicalEmitAvailable)
        {
            deniedStatements.Add(
                "select pg_catalog.pg_logical_emit_message(false, 'yo4x', 'canary')");
        }

        foreach (string deniedSql in deniedStatements)
        {
            await using var denied = new NpgsqlCommand(deniedSql, repairedWorker);
            PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                () => denied.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
        }

        await using NpgsqlConnection conversionWorker =
            await OpenNonPooledAsync(database.ConversionWorkerConnectionString);
        await using var privateTenantLock = new NpgsqlCommand(
            $"select control.acquire_u0_tenant_authority_lock('{Guid.CreateVersion7():D}'::uuid)",
            conversionWorker);
        PostgresException privateLockRejected =
            await Assert.ThrowsAsync<PostgresException>(
                () => privateTenantLock.ExecuteNonQueryAsync());
        Assert.Equal(
            PostgresErrorCodes.InsufficientPrivilege,
            privateLockRejected.SqlState);

        await using (var removeCanaries = new NpgsqlCommand(
            """
            drop function public.yo4x_public_definer_canary();
            drop schema yo4x_runtime_owner_canary;
            """,
            administrator))
        {
            await removeCanaries.ExecuteNonQueryAsync();
        }

        string fullyRestored = await PostgresCatalogSemanticFingerprint.ComputeSha256Async(
            administrator,
            cancellationToken: CancellationToken.None);
        Assert.Equal(baseline, fullyRestored);
    }

    [PostgresFact]
    public async Task InaccessiblePeerDatabaseDoesNotChangeLocalSemanticIdentity()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        string peerName = $"yo4x_peer_{Guid.CreateVersion7():N}";
        string quotedPeer = $"\"{peerName}\"";
        string baseline = await PostgresCatalogSemanticFingerprint.ComputeSha256Async(
            administrator,
            cancellationToken: CancellationToken.None);

        try
        {
            await using (var createPeer = new NpgsqlCommand(
                $"create database {quotedPeer} owner yo4x_migrator",
                administrator))
            {
                await createPeer.ExecuteNonQueryAsync();
            }

            await using (var closePeer = new NpgsqlCommand(
                $"revoke all privileges on database {quotedPeer} from public, "
                + "yo4x_context_authority, yo4x_context_issuer, yo4x_control_api, "
                + "yo4x_admin_bff, yo4x_emergency, yo4x_secret_ingestion, "
                + "yo4x_conversion_worker, yo4x_strategy_verifier, "
                + "yo4x_runtime_evidence, yo4x_worker, yo4x_supervisor_runtime, "
                + "yo4x_trade_authorizer, yo4x_gateway_runtime",
                administrator))
            {
                await closePeer.ExecuteNonQueryAsync();
            }

            string withInaccessiblePeer =
                await PostgresCatalogSemanticFingerprint.ComputeSha256Async(
                    administrator,
                    cancellationToken: CancellationToken.None);
            Assert.Equal(baseline, withInaccessiblePeer);

            await using (var driftPeer = new NpgsqlCommand(
                $"grant connect on database {quotedPeer} to yo4x_worker",
                administrator))
            {
                await driftPeer.ExecuteNonQueryAsync();
            }

            string drifted = await PostgresCatalogSemanticFingerprint.ComputeSha256Async(
                administrator,
                cancellationToken: CancellationToken.None);
            Assert.NotEqual(baseline, drifted);

            await using (var repairPeer = new NpgsqlCommand(
                $"revoke all privileges on database {quotedPeer} from yo4x_worker",
                administrator))
            {
                await repairPeer.ExecuteNonQueryAsync();
            }

            string repaired = await PostgresCatalogSemanticFingerprint.ComputeSha256Async(
                administrator,
                cancellationToken: CancellationToken.None);
            Assert.Equal(baseline, repaired);
        }
        finally
        {
            await using var dropPeer = new NpgsqlCommand(
                $"drop database if exists {quotedPeer} with (force)",
                administrator);
            await dropPeer.ExecuteNonQueryAsync();
        }
    }

    private static async Task<bool> IsSatisfiedAsync(
        PostgresDatabase database,
        PostgresRoleCapabilityContract contract)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        return await IsSatisfiedAsync(connection, contract);
    }

    private static async Task<string> DescribePrivilegeMismatchAsync(
        PostgresDatabase database,
        PostgresRoleCapabilityContract contract)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            with role_identity as
            (
                select oid from pg_catalog.pg_roles where rolname = current_user
            ),
            actual_column_rows as
            (
                select (case when privilege.grantee = 0 then 'PUBLIC|' else '' end)
                    || namespace.nspname || '.' || relation.relname || '|'
                    || privilege.privilege_type || '|' || string_agg(
                        attribute.attname, ',' order by attribute.attname)
                    || case when privilege.is_grantable
                        then '|WITH_GRANT_OPTION' else '' end as value
                from pg_catalog.pg_attribute as attribute
                join pg_catalog.pg_class as relation
                  on relation.oid = attribute.attrelid
                join pg_catalog.pg_namespace as namespace
                  on namespace.oid = relation.relnamespace
                cross join lateral pg_catalog.aclexplode(attribute.attacl) as privilege
                where namespace.nspname in
                    ('identity', 'authorization', 'control', 'operations',
                     'governance', 'audit', 'messaging', 'readmodel')
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and privilege.grantee in (0, (select oid from role_identity))
                group by privilege.grantee, namespace.nspname, relation.relname,
                    privilege.privilege_type, privilege.is_grantable
            )
            select category, value
            from
            (
                select 'database'::text as category,
                    (case when privilege.grantee = 0 then 'PUBLIC|' else '' end)
                    || privilege.privilege_type
                    || case when privilege.is_grantable
                        then '|WITH_GRANT_OPTION' else '' end as value
                from pg_catalog.pg_database as database
                cross join lateral pg_catalog.aclexplode(coalesce(
                    database.datacl,
                    pg_catalog.acldefault('d', database.datdba))) as privilege
                where database.datname = current_database()
                  and privilege.grantee in (0, (select oid from role_identity))
                union all
                select 'schema',
                    (case when privilege.grantee = 0 then 'PUBLIC|' else '' end)
                    || namespace.nspname || '|' || privilege.privilege_type
                    || case when privilege.is_grantable
                        then '|WITH_GRANT_OPTION' else '' end
                from pg_catalog.pg_namespace as namespace
                cross join lateral pg_catalog.aclexplode(coalesce(
                    namespace.nspacl,
                    pg_catalog.acldefault('n', namespace.nspowner))) as privilege
                where namespace.nspname in
                    ('identity', 'authorization', 'control', 'operations',
                     'governance', 'audit', 'messaging', 'readmodel')
                  and privilege.grantee in (0, (select oid from role_identity))
                union all
                select 'table',
                    (case when privilege.grantee = 0 then 'PUBLIC|' else '' end)
                    || namespace.nspname || '.' || relation.relname || '|'
                    || privilege.privilege_type
                    || case when privilege.is_grantable
                        then '|WITH_GRANT_OPTION' else '' end
                from pg_catalog.pg_class as relation
                join pg_catalog.pg_namespace as namespace
                  on namespace.oid = relation.relnamespace
                cross join lateral pg_catalog.aclexplode(coalesce(
                    relation.relacl,
                    pg_catalog.acldefault(
                        (case when relation.relkind = 'S' then 'S' else 'r' end)::"char",
                        relation.relowner))) as privilege
                where namespace.nspname in
                    ('identity', 'authorization', 'control', 'operations',
                     'governance', 'audit', 'messaging', 'readmodel')
                  and relation.relkind in ('r', 'p', 'v', 'm', 'f', 'S')
                  and privilege.grantee in (0, (select oid from role_identity))
                union all
                select 'column', value from actual_column_rows
                union all
                select 'function',
                    (case when privilege.grantee = 0 then 'PUBLIC|' else '' end)
                    || function.oid::regprocedure::text
                    || case when privilege.is_grantable
                        then '|WITH_GRANT_OPTION' else '' end
                from pg_catalog.pg_proc as function
                join pg_catalog.pg_namespace as namespace
                  on namespace.oid = function.pronamespace
                cross join lateral pg_catalog.aclexplode(coalesce(
                    function.proacl,
                    pg_catalog.acldefault('f', function.proowner))) as privilege
                where namespace.nspname in
                    ('identity', 'authorization', 'control', 'operations',
                     'governance', 'audit', 'messaging', 'readmodel')
                  and privilege.privilege_type = 'EXECUTE'
                  and privilege.grantee in (0, (select oid from role_identity))
            ) as privilege_manifest
            order by category, value
            """,
            connection);
        var actual = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string category = reader.GetString(0);
            if (!actual.TryGetValue(category, out List<string>? values))
            {
                values = [];
                actual.Add(category, values);
            }

            values.Add(reader.GetString(1));
        }
        await reader.CloseAsync();

        await using var identity = new NpgsqlCommand(
            """
            select role.rolcanlogin, role.rolinherit, role.rolsuper,
                role.rolbypassrls, role.rolcreatedb, role.rolcreaterole,
                role.rolreplication, role.rolconnlimit,
                coalesce(array_to_string(role.rolconfig, ';'), '<null>'),
                current_setting('session_replication_role'),
                current_setting('row_security'),
                current_setting('transaction_read_only'),
                current_setting('default_transaction_read_only'),
                current_setting('default_transaction_isolation'),
                current_setting('transaction_timeout'),
                current_setting('max_prepared_transactions'),
                current_setting('search_path'),
                (select count(*)
                 from pg_catalog.pg_auth_members as membership
                 where membership.member = role.oid or membership.roleid = role.oid),
                (select count(*)
                 from pg_catalog.pg_db_role_setting as setting
                 join pg_catalog.pg_database as database
                   on database.datname = current_database()
                 where setting.setrole in (0, role.oid)
                   and setting.setdatabase in (0, database.oid))
            from pg_catalog.pg_roles as role
            where role.rolname = current_user
            """,
            connection);
        string identityDescription;
        await using (NpgsqlDataReader identityReader = await identity.ExecuteReaderAsync())
        {
            Assert.True(await identityReader.ReadAsync());
            identityDescription = string.Join(
                '|',
                Enumerable.Range(0, identityReader.FieldCount)
                    .Select(index => $"{identityReader.GetName(index)}={identityReader.GetValue(index)}"));
        }

        await using var structural = new NpgsqlCommand(
            """
            select
                session_user as session_role,
                current_user as current_role,
                (select coalesce(role.rolvaliduntil::text, '<null>')
                 from pg_catalog.pg_roles as role
                 where role.rolname = current_user) as credential_valid_until,
                (select role.rolcanlogin::text || '|' || role.rolinherit::text || '|'
                    || role.rolsuper::text || '|' || role.rolbypassrls::text || '|'
                    || role.rolcreatedb::text || '|' || role.rolcreaterole::text || '|'
                    || role.rolreplication::text || '|' || role.rolconnlimit::text || '|'
                    || coalesce(array_to_string(role.rolconfig, ';'), '<null>')
                 from pg_catalog.pg_roles as role
                 where role.rolname = 'yo4x_migrator') as migrator_identity,
                (select count(*)
                 from pg_catalog.pg_auth_members as membership
                 join pg_catalog.pg_roles as role
                   on role.oid in (membership.member, membership.roleid)
                 where role.rolname = 'yo4x_migrator') as migrator_memberships,
                (select role.rolcanlogin::text || '|' || role.rolinherit::text || '|'
                    || role.rolsuper::text || '|' || role.rolbypassrls::text || '|'
                    || role.rolcreatedb::text || '|' || role.rolcreaterole::text || '|'
                    || role.rolreplication::text || '|' || role.rolconnlimit::text || '|'
                    || coalesce(array_to_string(role.rolconfig, ';'), '<null>')
                 from pg_catalog.pg_roles as role
                 where role.rolname = 'yo4x_context_authority') as context_authority_identity,
                (select count(*)
                 from pg_catalog.pg_auth_members as membership
                 join pg_catalog.pg_roles as role
                   on role.oid in (membership.member, membership.roleid)
                 where role.rolname = 'yo4x_context_authority') as context_authority_memberships,
                coalesce((
                    select string_agg(coalesce(array_to_string(setting.setconfig, ';'), '<null>')
                        || '[' || coalesce(pg_catalog.cardinality(setting.setconfig), 0)::text || ']', ',')
                    from pg_catalog.pg_db_role_setting as setting
                    join pg_catalog.pg_roles as role on role.oid = setting.setrole
                    join pg_catalog.pg_database as database
                      on database.datname = current_database()
                    where role.rolname = current_user
                      and setting.setdatabase in (0, database.oid)), '<none>') as database_settings,
                (select database_owner.rolname
                 from pg_catalog.pg_database as database
                 join pg_catalog.pg_roles as database_owner
                   on database_owner.oid = database.datdba
                 where database.datname = current_database()) as database_owner,
                coalesce((
                    select string_agg(namespace.nspname || '=' || owner.rolname, ','
                        order by namespace.nspname)
                    from pg_catalog.pg_namespace as namespace
                    join pg_catalog.pg_roles as owner on owner.oid = namespace.nspowner
                    where namespace.nspname in
                        ('identity', 'authorization', 'control', 'operations',
                         'governance', 'audit', 'messaging', 'readmodel')
                      and owner.rolname <> 'yo4x_migrator'), '<none>') as namespace_owners,
                coalesce((
                    select string_agg(namespace.nspname || '.' || relation.relname
                        || '=' || owner.rolname, ',' order by namespace.nspname, relation.relname)
                    from pg_catalog.pg_class as relation
                    join pg_catalog.pg_namespace as namespace
                      on namespace.oid = relation.relnamespace
                    join pg_catalog.pg_roles as owner on owner.oid = relation.relowner
                    where namespace.nspname in
                        ('identity', 'authorization', 'control', 'operations',
                         'governance', 'audit', 'messaging', 'readmodel')
                      and relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f', 'i', 'I')
                      and owner.rolname <> 'yo4x_migrator'), '<none>') as relation_owners,
                coalesce((
                    select string_agg(function.oid::regprocedure::text || '=' || owner.rolname,
                        ',' order by function.oid::regprocedure::text)
                    from pg_catalog.pg_proc as function
                    join pg_catalog.pg_namespace as namespace
                      on namespace.oid = function.pronamespace
                    join pg_catalog.pg_roles as owner on owner.oid = function.proowner
                    where namespace.nspname in
                        ('identity', 'authorization', 'control', 'operations',
                         'governance', 'audit', 'messaging', 'readmodel')
                      and owner.rolname <> 'yo4x_migrator'), '<none>') as function_owners
            """,
            connection);
        string structuralDescription;
        await using (NpgsqlDataReader structuralReader = await structural.ExecuteReaderAsync())
        {
            Assert.True(await structuralReader.ReadAsync());
            structuralDescription = string.Join(
                '|',
                Enumerable.Range(0, structuralReader.FieldCount)
                    .Select(index => $"{structuralReader.GetName(index)}={structuralReader.GetValue(index)}"));
        }

        string semanticSha256 = await PostgresCatalogSemanticFingerprint.ComputeSha256Async(
            connection,
            transaction: null,
            CancellationToken.None);

        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["database"] = Manifest("DatabasePrivileges"),
            ["schema"] = Manifest("SchemaPrivileges"),
            ["table"] = Manifest("TablePrivileges"),
            ["column"] = Manifest("ColumnPrivileges"),
            ["function"] = Manifest("FunctionPrivileges")
        };
        string privileges = string.Join(
            Environment.NewLine,
            expected.SelectMany(pair =>
            {
                string[] found = actual.TryGetValue(pair.Key, out List<string>? values)
                    ? values.ToArray()
                    : [];
                return new[]
                {
                    $"{pair.Key} missing: {string.Join(", ", pair.Value.Except(found, StringComparer.Ordinal))}",
                    $"{pair.Key} extra: {string.Join(", ", found.Except(pair.Value, StringComparer.Ordinal))}",
                    $"{pair.Key} sequence equal: {pair.Value.SequenceEqual(found, StringComparer.Ordinal)}"
                };
            }));
        return privileges
            + Environment.NewLine
            + "identity: "
            + identityDescription
            + Environment.NewLine
            + "structure: "
            + structuralDescription
            + Environment.NewLine
            + "semantic: "
            + semanticSha256
            + Environment.NewLine
            + "expected configuration: "
            + string.Join(';', Manifest("RoleConfiguration"));

        string[] Manifest(string propertyName) =>
            (string[])(typeof(PostgresRoleCapabilityContract)
                .GetProperty(
                    propertyName,
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(contract)!);
    }

    private static async Task<bool> IsSatisfiedAsync(
        NpgsqlConnection connection,
        PostgresRoleCapabilityContract contract)
    {
        return await PostgresRoleCapabilityFingerprint.IsSatisfiedAsync(
            connection,
            transaction: null,
            contract,
            CancellationToken.None);
    }

    private static async Task<NpgsqlConnection> OpenNonPooledAsync(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<string> ReadRuntimePostureAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            select pg_catalog.jsonb_pretty(pg_catalog.jsonb_build_object(
                'current_user', current_user,
                'session_user', session_user,
                'role_config', role.rolconfig,
                'can_login', role.rolcanlogin,
                'inherit', role.rolinherit,
                'superuser', role.rolsuper,
                'bypass_rls', role.rolbypassrls,
                'create_db', role.rolcreatedb,
                'create_role', role.rolcreaterole,
                'replication', role.rolreplication,
                'connection_limit', role.rolconnlimit,
                'valid_until', role.rolvaliduntil,
                'search_path', current_setting('search_path'),
                'row_security', current_setting('row_security'),
                'session_replication_role', current_setting('session_replication_role'),
                'transaction_read_only', current_setting('transaction_read_only'),
                'default_transaction_read_only',
                    current_setting('default_transaction_read_only'),
                'default_transaction_isolation',
                    current_setting('default_transaction_isolation'),
                'semantic_sha256', @semantic_sha256))
            from pg_catalog.pg_roles as role
            where role.rolname = current_user
            """,
            connection);
        command.Parameters.AddWithValue(
            "semantic_sha256",
            await PostgresCatalogSemanticFingerprint.ComputeSha256Async(
                connection,
                cancellationToken: CancellationToken.None));
        return (string?)await command.ExecuteScalarAsync()
            ?? "PostgreSQL runtime posture was unavailable.";
    }

    private static async Task AssertExistingSessionDriftAsync(
        NpgsqlConnection administrator,
        NpgsqlConnection existingWorker,
        string driftSql,
        string restoreSql)
    {
        try
        {
            await using (var drift = new NpgsqlCommand(driftSql, administrator))
            {
                await drift.ExecuteNonQueryAsync();
            }

            Assert.False(await IsSatisfiedAsync(
                existingWorker,
                Yo4xPostgresRoleContracts.Worker));
        }
        finally
        {
            await using var restore = new NpgsqlCommand(restoreSql, administrator);
            await restore.ExecuteNonQueryAsync();
        }

        Assert.True(await IsSatisfiedAsync(
            existingWorker,
            Yo4xPostgresRoleContracts.Worker));
    }

    private static async Task AssertReconnectDriftAndRoleRepairAsync(
        PostgresTestDatabase database,
        NpgsqlConnection administrator,
        string driftSql)
    {
        await using (var drift = new NpgsqlCommand(driftSql, administrator))
        {
            await drift.ExecuteNonQueryAsync();
        }

        try
        {
            await using NpgsqlConnection drifted =
                await OpenNonPooledAsync(database.WorkerConnectionString);
            Assert.False(await IsSatisfiedAsync(
                drifted,
                Yo4xPostgresRoleContracts.Worker));
        }
        finally
        {
            await PostgresContainerFixture.ApplyLeastPrivilegeRoleScriptAsync(administrator);
        }

        await using NpgsqlConnection restored =
            await OpenNonPooledAsync(database.WorkerConnectionString);
        Assert.True(await IsSatisfiedAsync(
            restored,
            Yo4xPostgresRoleContracts.Worker));
    }

    private static async Task AssertSemanticDriftAsync(
        NpgsqlConnection administrator,
        string baseline,
        string driftSql)
    {
        await using NpgsqlTransaction transaction =
            await administrator.BeginTransactionAsync();
        try
        {
            await using (var drift = new NpgsqlCommand(
                driftSql,
                administrator,
                transaction))
            {
                await drift.ExecuteNonQueryAsync();
            }

            string drifted = await PostgresCatalogSemanticFingerprint.ComputeSha256Async(
                administrator,
                transaction,
                CancellationToken.None);
            Assert.NotEqual(baseline, drifted);
        }
        finally
        {
            await transaction.RollbackAsync();
        }

        string restored = await PostgresCatalogSemanticFingerprint.ComputeSha256Async(
            administrator,
            cancellationToken: CancellationToken.None);
        Assert.Equal(baseline, restored);
    }

    private static async Task AssertDriftRejectedAndRestoreAsync(
        PostgresTestDatabase database,
        string driftSql,
        Func<Task<bool>> readiness,
        string? restoreBeforeRoleReapplySql = null)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using (var drift = new NpgsqlCommand(driftSql, administrator))
        {
            await drift.ExecuteNonQueryAsync();
        }

        bool rejected;
        try
        {
            rejected = !await readiness();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            rejected = true;
        }

        Assert.True(rejected);
        if (restoreBeforeRoleReapplySql is not null)
        {
            await using var restoreOwnership = new NpgsqlCommand(
                restoreBeforeRoleReapplySql,
                administrator);
            await restoreOwnership.ExecuteNonQueryAsync();
        }

        await PostgresContainerFixture.ApplyLeastPrivilegeRoleScriptAsync(administrator);
        Assert.True(await readiness());
    }
}
