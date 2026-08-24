using Npgsql;
using YO4X.Admin.Postgres;
using YO4X.BuildingBlocks;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.Postgres.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class AdminReadinessPostgresTests(PostgresContainerFixture postgres)
{
    private readonly PostgresContainerFixture postgres = postgres;

    [PostgresFact]
    public async Task ReadinessRequiresExactAdminBffDatabaseIdentity()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);
        var options = new AdminPostgresOptions();
        var admin = new AdminPostgresApplication(
            database.AdminBff,
            SystemClock.Instance,
            options);
        var genericSafeRuntime = new AdminPostgresApplication(
            database.Application,
            SystemClock.Instance,
            options);

        Assert.True(await admin.IsReadyAsync(CancellationToken.None));

        await using NpgsqlConnection genericConnection =
            await database.Application.OpenConnectionAsync();
        await using var genericRole = new NpgsqlCommand(
            "select current_user, control.assert_safe_runtime_role()",
            genericConnection);
        await using NpgsqlDataReader reader = await genericRole.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("yo4x_emergency", reader.GetString(0));
        Assert.NotEqual("yo4x_admin_bff", reader.GetString(0));
        Assert.False(await reader.ReadAsync());

        Assert.False(await genericSafeRuntime.IsReadyAsync(CancellationToken.None));
    }

    [PostgresFact]
    public async Task ReadinessFailsClosedForStaleAdminSchemaOrPrivileges()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);
        var admin = new AdminPostgresApplication(
            database.AdminBff,
            SystemClock.Instance,
            new AdminPostgresOptions());
        Assert.True(await admin.IsReadyAsync(CancellationToken.None));

        await ExecuteAdministratorAsync(
            database,
            "revoke insert on messaging.outbox_messages from yo4x_admin_bff");
        Assert.False(await admin.IsReadyAsync(CancellationToken.None));
        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        {
            await PostgresContainerFixture.ApplyLeastPrivilegeRoleScriptAsync(administrator);
        }

        Assert.True(await admin.IsReadyAsync(CancellationToken.None));
        await ExecuteAdministratorAsync(
            database,
            "grant select on governance.strategy_source_files to yo4x_admin_bff");
        Assert.False(await admin.IsReadyAsync(CancellationToken.None));
        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        {
            await PostgresContainerFixture.ApplyLeastPrivilegeRoleScriptAsync(administrator);
        }

        Assert.True(await admin.IsReadyAsync(CancellationToken.None));
        await ExecuteAdministratorAsync(
            database,
            "alter table control.approval_requests rename column binding_digest to binding_digest_unavailable");
        Assert.False(await admin.IsReadyAsync(CancellationToken.None));
    }

    [PostgresFact]
    public async Task ReadinessFailsClosedForCatalogAndUnexpectedCapabilityDrift()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);
        var admin = new AdminPostgresApplication(
            database.AdminBff,
            SystemClock.Instance,
            new AdminPostgresOptions());
        Assert.True(await admin.IsReadyAsync(CancellationToken.None));

        await AssertDriftFailsAndRestoreAsync(
            database,
            admin,
            "alter table identity.admin_identities disable row level security",
            "alter table identity.admin_identities enable row level security");
        await AssertDriftFailsAndRestoreAsync(
            database,
            admin,
            "alter table identity.admin_identities no force row level security",
            "alter table identity.admin_identities force row level security");
        await AssertDriftFailsAndRestoreAsync(
            database,
            admin,
            "create policy admin_readiness_drift on identity.admin_identities for select using (true)",
            "drop policy admin_readiness_drift on identity.admin_identities");
        await AssertDriftFailsAndRestoreAsync(
            database,
            admin,
            "alter table control.admin_commands disable trigger admin_commands_immutable_binding",
            "alter table control.admin_commands enable trigger admin_commands_immutable_binding");
        await AssertDriftFailsAndRestoreAsync(
            database,
            admin,
            "grant select on operations.user_operation_results to yo4x_admin_bff",
            "revoke select on operations.user_operation_results from yo4x_admin_bff");
        await AssertDriftFailsAndRestoreAsync(
            database,
            admin,
            "grant execute on function control.refresh_user_operation_backlog_observation() to yo4x_admin_bff",
            "revoke execute on function control.refresh_user_operation_backlog_observation() from yo4x_admin_bff");
    }

    [PostgresFact]
    public async Task CapabilityRolesUseExactNamedLoginIdentities()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select
                bool_and(role.rolcanlogin) filter
                (
                    where role.rolname in
                    (
                        'yo4x_control_api', 'yo4x_admin_bff', 'yo4x_emergency',
                        'yo4x_secret_ingestion', 'yo4x_conversion_worker',
                        'yo4x_strategy_verifier', 'yo4x_runtime_evidence',
                        'yo4x_worker', 'yo4x_supervisor_runtime',
                        'yo4x_trade_authorizer', 'yo4x_gateway_runtime'
                    )
                ),
                bool_and(not role.rolcanlogin) filter
                (
                    where role.rolname = 'yo4x_migrator'
                ),
                count(*) filter
                (
                    where role.rolname in
                    (
                        'yo4x_migrator', 'yo4x_control_api', 'yo4x_admin_bff',
                        'yo4x_emergency', 'yo4x_secret_ingestion',
                        'yo4x_conversion_worker', 'yo4x_strategy_verifier',
                        'yo4x_runtime_evidence', 'yo4x_worker',
                        'yo4x_supervisor_runtime', 'yo4x_trade_authorizer',
                        'yo4x_gateway_runtime'
                    )
                )
            from pg_catalog.pg_roles as role
            """,
            administrator);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.Equal(12L, reader.GetInt64(2));
        Assert.False(await reader.ReadAsync());
    }

    private static async Task AssertDriftFailsAndRestoreAsync(
        PostgresTestDatabase database,
        AdminPostgresApplication admin,
        string driftSql,
        string restoreSql)
    {
        await ExecuteAdministratorAsync(database, driftSql);
        Assert.False(await admin.IsReadyAsync(CancellationToken.None));
        await ExecuteAdministratorAsync(database, restoreSql);
        Assert.True(await admin.IsReadyAsync(CancellationToken.None));
    }

    [PostgresFact]
    public async Task ReapplyingLeastPrivilegeRolesRemovesEveryStaleRuntimeGrant()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();

        await using (var seedStaleGrants = new NpgsqlCommand(
            """
            grant select on governance.strategy_source_files to yo4x_gateway_runtime;
            grant update on control.credential_ingestion_grants to yo4x_conversion_worker;
            grant execute on function control.acquire_u0_authority_lock()
                to yo4x_gateway_runtime;
            """,
            administrator))
        {
            await seedStaleGrants.ExecuteNonQueryAsync();
        }

        (bool staleTableRead, bool staleTableWrite, bool staleFunction) =
            await ReadStaleGrantStateAsync(administrator);
        Assert.True(staleTableRead);
        Assert.True(staleTableWrite);
        Assert.True(staleFunction);

        await PostgresContainerFixture.ApplyLeastPrivilegeRoleScriptAsync(administrator);

        (staleTableRead, staleTableWrite, staleFunction) =
            await ReadStaleGrantStateAsync(administrator);
        Assert.False(staleTableRead);
        Assert.False(staleTableWrite);
        Assert.False(staleFunction);

        await using var requiredGrants = new NpgsqlCommand(
            """
            select
                has_schema_privilege('yo4x_gateway_runtime', 'control', 'USAGE'),
                has_function_privilege(
                    'yo4x_gateway_runtime',
                    'control.begin_broker_command_reconciliation(uuid,text,uuid,uuid)',
                    'EXECUTE'),
                has_function_privilege(
                    'yo4x_conversion_worker',
                    'control.acquire_strategy_import_job(uuid,bytea)',
                    'EXECUTE'),
                has_table_privilege(
                    'yo4x_worker',
                    'operations.deployments',
                    'SELECT')
            """,
            administrator);
        await using NpgsqlDataReader reader = await requiredGrants.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.True(reader.GetBoolean(3));
        Assert.False(await reader.ReadAsync());
    }

    [PostgresFact]
    public async Task WrapperMembershipCannotAssumeRuntimeTenantAuthority()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        string wrapperRole = $"yo4x_wrapper_{Guid.CreateVersion7():N}";
        string wrapperPassword = $"p{Guid.CreateVersion7():N}";
        Guid protectedTenantId = Guid.CreateVersion7();
        bool wrapperRoleRemoved = false;

        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        try
        {
            await using (var provision = new NpgsqlCommand(
                $"""
                create role {wrapperRole} login inherit password '{wrapperPassword}'
                    nosuperuser nocreatedb nocreaterole nobypassrls noreplication;
                grant yo4x_worker to {wrapperRole};
                """,
                administrator))
            {
                await provision.ExecuteNonQueryAsync();
            }

            var seedContext = new TenantExecutionContext(
                protectedTenantId,
                Guid.CreateVersion7(),
                Guid.CreateVersion7());
            await using (TenantPostgresTransaction seed =
                await database.Application.BeginTenantTransactionAsync(seedContext))
            {
                await using NpgsqlCommand insertTenant = seed.CreateCommand(
                    """
                    insert into identity.tenants (id, slug, display_name)
                    values (@tenant_id, @slug, 'Wrapper isolation tenant')
                    """);
                insertTenant.Parameters.AddWithValue("tenant_id", protectedTenantId);
                insertTenant.Parameters.AddWithValue(
                    "slug",
                    $"tenant-{protectedTenantId:N}");
                await insertTenant.ExecuteNonQueryAsync();
                await seed.CommitAsync();
            }

            PostgresException invalidDeployment = await Assert.ThrowsAsync<PostgresException>(
                () => PostgresContainerFixture.ApplyLeastPrivilegeRoleScriptAsync(administrator));
            Assert.Equal(PostgresErrorCodes.RaiseException, invalidDeployment.SqlState);
            Assert.Contains(
                "must not be granted to wrapper or member roles",
                invalidDeployment.MessageText,
                StringComparison.Ordinal);
            await using (var rollback = new NpgsqlCommand("rollback", administrator))
            {
                await rollback.ExecuteNonQueryAsync();
            }

            var wrapperBuilder = new NpgsqlConnectionStringBuilder(
                database.WorkerConnectionString)
            {
                Username = wrapperRole,
                Password = wrapperPassword,
                Pooling = false,
                IncludeErrorDetail = false,
                LogParameters = false
            };
            await using (var wrapper = new NpgsqlConnection(wrapperBuilder.ConnectionString))
            {
                await wrapper.OpenAsync();
                await AssertWrapperCannotReadTenantAsync(
                    wrapper,
                    protectedTenantId,
                    setWorkerRole: false);
                await AssertWrapperCannotReadTenantAsync(
                    wrapper,
                    protectedTenantId,
                    setWorkerRole: true);
            }

            // The fixed runtime login must itself fail closed for as long as
            // any inbound wrapper membership exists. Remove the invalid
            // deployment relationship before proving the repaired direct-role
            // path remains usable.
            await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                var rejectedContext = new TenantExecutionContext(
                    protectedTenantId,
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7());
                await using TenantPostgresTransaction _ =
                    await database.Worker.BeginTenantTransactionAsync(rejectedContext);
            });
            await using (var removeWrapper = new NpgsqlCommand(
                $"drop role if exists {wrapperRole}",
                administrator))
            {
                await removeWrapper.ExecuteNonQueryAsync();
            }
            wrapperRoleRemoved = true;
            await PostgresContainerFixture.ApplyLeastPrivilegeRoleScriptAsync(administrator);

            var workerContext = new TenantExecutionContext(
                protectedTenantId,
                Guid.CreateVersion7(),
                Guid.CreateVersion7());
            await using TenantPostgresTransaction worker =
                await database.Worker.BeginTenantTransactionAsync(workerContext);
            await using NpgsqlCommand visibleToDirectRole = worker.CreateCommand(
                "select count(id) from identity.tenants where id = @tenant_id");
            visibleToDirectRole.Parameters.AddWithValue("tenant_id", protectedTenantId);
            Assert.Equal(1L, await visibleToDirectRole.ExecuteScalarAsync());
            await worker.RollbackAsync();
        }
        finally
        {
            if (!wrapperRoleRemoved)
            {
                await using var cleanup = new NpgsqlCommand(
                    $"drop role if exists {wrapperRole}",
                    administrator);
                await cleanup.ExecuteNonQueryAsync();
            }

            await PostgresContainerFixture.ApplyLeastPrivilegeRoleScriptAsync(administrator);
        }
    }

    [PostgresFact]
    public async Task ReapplyingRolesRestoresEverySecurityDefinerAndTriggerOwner()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();

        await using (var drift = new NpgsqlCommand(
            """
            alter function control.reserve_credential_ingestion_grant(
                uuid, uuid, text, text, text, integer, uuid, uuid)
                owner to postgres
            """,
            administrator))
        {
            await drift.ExecuteNonQueryAsync();
        }

        (bool allOwned, long functionCount, long superuserOwned) =
            await ReadDefinerOwnershipAsync(administrator);
        Assert.False(allOwned);
        Assert.True(functionCount > 0);
        Assert.True(superuserOwned > 0);

        await PostgresContainerFixture.ApplyLeastPrivilegeRoleScriptAsync(administrator);
        await PostgresContainerFixture.ApplyLeastPrivilegeRoleScriptAsync(administrator);

        (allOwned, functionCount, superuserOwned) =
            await ReadDefinerOwnershipAsync(administrator);
        Assert.True(allOwned);
        Assert.True(functionCount > 0);
        Assert.Equal(0L, superuserOwned);

        await using var migrator = new NpgsqlCommand(
            """
            select not rolcanlogin and not rolsuper and not rolbypassrls
            from pg_catalog.pg_roles
            where rolname = 'yo4x_migrator'
            """,
            administrator);
        Assert.Equal(true, await migrator.ExecuteScalarAsync());

        await using var contextBoundary = new NpgsqlCommand(
            """
            with authority as
            (
                select oid from pg_catalog.pg_roles
                where rolname = 'yo4x_context_authority'
            ),
            context_table as
            (
                select relation.oid, relation.reltoastrelid
                from pg_catalog.pg_class as relation
                join pg_catalog.pg_namespace as namespace
                  on namespace.oid = relation.relnamespace
                where namespace.nspname = 'control'
                  and relation.relname = 'tenant_context_capabilities'
            ),
            authority_functions as
            (
                select function.oid,
                    function.oid::regprocedure::text as signature
                from pg_catalog.pg_proc as function
                where function.proowner = (select oid from authority)
            )
            select
                (select array_agg(signature order by signature)
                 from authority_functions) = array[
                    'control.activate_credential_runtime_tenant_context(bytea,uuid,uuid,uuid,uuid)',
                    'control.activate_tenant_context(bytea,uuid,uuid,uuid,uuid)',
                    'control.bind_verified_strategy_import_tenant_context(bytea,uuid,uuid,uuid,uuid)',
                    'control.cleanup_tenant_context_capabilities(integer)',
                    'control.current_actor_id()',
                    'control.current_correlation_id()',
                    'control.current_session_id()',
                    'control.current_tenant_id()',
                    'control.issue_credential_runtime_tenant_context_capability(bytea,text,integer,text,uuid,uuid,uuid,uuid)',
                    'control.issue_tenant_context_capability(bytea,text,text,integer,text,uuid,uuid,uuid,uuid)',
                    'control.reject_tenant_context_capability_rewrite()'],
                not exists
                (
                    select 1
                    from pg_catalog.pg_class as relation
                    where relation.relowner = (select oid from authority)
                      and relation.oid <> (select oid from context_table)
                      and relation.oid <> (select reltoastrelid from context_table)
                      and not exists
                      (
                          select 1
                          from pg_catalog.pg_index as index_record
                          where index_record.indexrelid = relation.oid
                            and index_record.indrelid in
                                ((select oid from context_table),
                                 (select reltoastrelid from context_table))
                      )
                ),
                has_table_privilege(
                    'yo4x_context_authority',
                    'control.tenant_context_capabilities',
                    'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER'),
                not has_table_privilege(
                    'yo4x_migrator',
                    'control.tenant_context_capabilities',
                    'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER'),
                not has_any_column_privilege(
                    'yo4x_migrator',
                    'control.tenant_context_capabilities',
                    'SELECT,INSERT,UPDATE,REFERENCES'),
                (select array_agg(
                    function.oid::regprocedure::text
                    order by function.oid::regprocedure::text)
                 from pg_catalog.pg_proc as function
                 cross join lateral pg_catalog.aclexplode(function.proacl) as privilege
                 join pg_catalog.pg_roles as grantee
                   on grantee.oid = privilege.grantee
                 where grantee.rolname = 'yo4x_context_issuer'
                   and privilege.privilege_type = 'EXECUTE') = array[
                    'control.assert_safe_runtime_role()',
                    'control.cleanup_tenant_context_capabilities(integer)',
                    'control.issue_credential_runtime_tenant_context_capability(bytea,text,integer,text,uuid,uuid,uuid,uuid)',
                    'control.issue_tenant_context_capability(bytea,text,text,integer,text,uuid,uuid,uuid,uuid)'],
                (select array_agg(signature order by signature)
                 from authority_functions
                 where has_function_privilege(
                    'yo4x_migrator', oid, 'EXECUTE')) = array[
                    'control.bind_verified_strategy_import_tenant_context(bytea,uuid,uuid,uuid,uuid)',
                    'control.current_actor_id()',
                    'control.current_correlation_id()',
                    'control.current_session_id()',
                    'control.current_tenant_id()']
            """,
            administrator);
        await using NpgsqlDataReader boundaryReader =
            await contextBoundary.ExecuteReaderAsync();
        Assert.True(await boundaryReader.ReadAsync());
        Assert.True(boundaryReader.GetBoolean(0));
        Assert.True(boundaryReader.GetBoolean(1));
        Assert.True(boundaryReader.GetBoolean(2));
        Assert.True(boundaryReader.GetBoolean(3));
        Assert.True(boundaryReader.GetBoolean(4));
        Assert.True(boundaryReader.GetBoolean(5));
        Assert.True(boundaryReader.GetBoolean(6));
        Assert.False(await boundaryReader.ReadAsync());
        await boundaryReader.DisposeAsync();

        await using var cursorBoundary = new NpgsqlCommand(
            """
            with worker as
            (
                select oid from pg_catalog.pg_roles where rolname = 'yo4x_worker'
            ),
            global_update_columns as
            (
                select pg_catalog.array_agg(
                    attribute.attname::text order by attribute.attname)
                    as names
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid =
                        'control.worker_tenant_scan_cursors'::regclass
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and pg_catalog.has_column_privilege(
                        'yo4x_worker', attribute.attrelid, attribute.attnum,
                        'UPDATE')
            ),
            deployment_select_columns as
            (
                select pg_catalog.array_agg(
                    attribute.attname::text order by attribute.attname)
                    as names
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid =
                        'control.deployment_scan_cursors'::regclass
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and pg_catalog.has_column_privilege(
                        'yo4x_worker', attribute.attrelid, attribute.attnum,
                        'SELECT')
            ),
            deployment_insert_columns as
            (
                select pg_catalog.array_agg(
                    attribute.attname::text order by attribute.attname)
                    as names
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid =
                        'control.deployment_scan_cursors'::regclass
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and pg_catalog.has_column_privilege(
                        'yo4x_worker', attribute.attrelid, attribute.attnum,
                        'INSERT')
            ),
            deployment_update_columns as
            (
                select pg_catalog.array_agg(
                    attribute.attname::text order by attribute.attname)
                    as names
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid =
                        'control.deployment_scan_cursors'::regclass
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and pg_catalog.has_column_privilege(
                        'yo4x_worker', attribute.attrelid, attribute.attnum,
                        'UPDATE')
            )
            select
                has_table_privilege(
                    'yo4x_worker', 'control.worker_tenant_scan_cursors', 'SELECT'),
                not has_table_privilege(
                    'yo4x_worker', 'control.worker_tenant_scan_cursors', 'INSERT')
                and not has_table_privilege(
                    'yo4x_worker', 'control.worker_tenant_scan_cursors', 'UPDATE')
                and not has_table_privilege(
                    'yo4x_worker', 'control.worker_tenant_scan_cursors', 'DELETE')
                and not has_table_privilege(
                    'yo4x_worker', 'control.worker_tenant_scan_cursors', 'TRUNCATE'),
                (select names from global_update_columns) = array['last_tenant_id'],
                (select names from deployment_select_columns) = array[
                    'last_advanced_at', 'last_deployment_id',
                    'last_rotation_completed_at', 'last_scan_at',
                    'rotation_count', 'row_version', 'tenant_id'],
                (select names from deployment_insert_columns) = array['tenant_id'],
                (select names from deployment_update_columns) =
                    array['last_deployment_id'],
                not has_table_privilege(
                    'yo4x_worker', 'control.deployment_scan_cursors', 'SELECT')
                and not has_table_privilege(
                    'yo4x_worker', 'control.deployment_scan_cursors', 'INSERT')
                and not has_table_privilege(
                    'yo4x_worker', 'control.deployment_scan_cursors', 'UPDATE')
                and not has_table_privilege(
                    'yo4x_worker', 'control.deployment_scan_cursors', 'DELETE')
                and not has_table_privilege(
                    'yo4x_worker', 'control.deployment_scan_cursors', 'TRUNCATE'),
                (select pg_catalog.array_agg(consumer order by consumer)
                 from control.worker_tenant_scan_cursors) = array[
                    'credential_grant_expiry', 'deployment_projection',
                    'outbox', 'user_operations']
            """,
            administrator);
        await using NpgsqlDataReader cursorReader =
            await cursorBoundary.ExecuteReaderAsync();
        Assert.True(await cursorReader.ReadAsync());
        for (int index = 0; index < cursorReader.FieldCount; index++)
        {
            Assert.True(cursorReader.GetBoolean(index));
        }

        Assert.False(await cursorReader.ReadAsync());
    }

    private static async Task ExecuteAdministratorAsync(
        PostgresTestDatabase database,
        string sql)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, administrator);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(bool TableRead, bool TableWrite, bool FunctionExecute)>
        ReadStaleGrantStateAsync(NpgsqlConnection administrator)
    {
        await using var command = new NpgsqlCommand(
            """
            select
                has_table_privilege(
                    'yo4x_gateway_runtime',
                    'governance.strategy_source_files',
                    'SELECT'),
                has_table_privilege(
                    'yo4x_conversion_worker',
                    'control.credential_ingestion_grants',
                    'UPDATE'),
                has_function_privilege(
                    'yo4x_gateway_runtime',
                    'control.acquire_u0_authority_lock()',
                    'EXECUTE')
            """,
            administrator);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = (
            reader.GetBoolean(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task AssertWrapperCannotReadTenantAsync(
        NpgsqlConnection wrapper,
        Guid protectedTenantId,
        bool setWorkerRole)
    {
        await using NpgsqlTransaction transaction = await wrapper.BeginTransactionAsync();
        if (setWorkerRole)
        {
            await using var assumeWorker = new NpgsqlCommand(
                "set local role yo4x_worker",
                wrapper,
                transaction);
            await assumeWorker.ExecuteNonQueryAsync();
        }

        await using (var setTenant = new NpgsqlCommand(
            "select set_config('yo4x.tenant_id', @tenant_id, true)",
            wrapper,
            transaction))
        {
            setTenant.Parameters.AddWithValue("tenant_id", protectedTenantId.ToString("D"));
            _ = await setTenant.ExecuteScalarAsync();
        }

        await using var read = new NpgsqlCommand(
            """
            select control.current_tenant_id() is null,
                (select count(id) from identity.tenants where id = @tenant_id)
            """,
            wrapper,
            transaction);
        read.Parameters.AddWithValue("tenant_id", protectedTenantId);
        await using (NpgsqlDataReader reader = await read.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetBoolean(0));
            Assert.Equal(0L, reader.GetInt64(1));
            Assert.False(await reader.ReadAsync());
        }

        await using var guard = new NpgsqlCommand(
            "select control.assert_safe_runtime_role()",
            wrapper,
            transaction);
        PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
            () => guard.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
        await transaction.RollbackAsync();
    }

    private static async Task<(bool AllOwned, long FunctionCount, long SuperuserOwned)>
        ReadDefinerOwnershipAsync(NpgsqlConnection administrator)
    {
        await using var command = new NpgsqlCommand(
            """
            select
                bool_and(owner.rolname = case
                    when namespace.nspname = 'control'
                     and function.oid::regprocedure::text in
                     (
                         'control.reject_tenant_context_capability_rewrite()',
                         'control.current_tenant_id()',
                         'control.current_actor_id()',
                         'control.current_correlation_id()',
                         'control.current_session_id()',
                         'control.issue_tenant_context_capability(bytea,text,text,integer,text,uuid,uuid,uuid,uuid)',
                         'control.activate_tenant_context(bytea,uuid,uuid,uuid,uuid)',
                         'control.issue_credential_runtime_tenant_context_capability(bytea,text,integer,text,uuid,uuid,uuid,uuid)',
                         'control.activate_credential_runtime_tenant_context(bytea,uuid,uuid,uuid,uuid)',
                         'control.cleanup_tenant_context_capabilities(integer)',
                         'control.bind_verified_strategy_import_tenant_context(bytea,uuid,uuid,uuid,uuid)'
                     ) then 'yo4x_context_authority'
                    else 'yo4x_migrator'
                end),
                count(*),
                count(*) filter (where owner.rolsuper)
            from pg_catalog.pg_proc as function
            join pg_catalog.pg_namespace as namespace
              on namespace.oid = function.pronamespace
            join pg_catalog.pg_roles as owner on owner.oid = function.proowner
            where namespace.nspname in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
              and (function.prosecdef or function.prorettype = 'trigger'::regtype)
            """,
            administrator);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = (reader.GetBoolean(0), reader.GetInt64(1), reader.GetInt64(2));
        Assert.False(await reader.ReadAsync());
        return result;
    }
}
