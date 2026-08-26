using Npgsql;

namespace YO4X.Postgres.IntegrationTests;

/// <summary>
/// The database contract for a background backtest queue runner:
/// 008_backtest_queue_worker_access.sql gives yo4x_worker the queue access it
/// needs and nothing else, and least_privilege_roles.sql restores exactly those
/// grants after its subtractive sweep. Both files run against every database the
/// fixture creates, so a grant that survives only in the migration and is then
/// stripped by the sweep fails here.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class BacktestQueueWorkerAccessPostgresTests(PostgresContainerFixture fixture)
{
    [PostgresFact]
    public async Task WorkerHoldsExactlyTheQueueGrantsAndTheClaimIndexExists()
    {
        await using PostgresTestDatabase database = await fixture.CreateDatabaseAsync();
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync(TestContext.Current.CancellationToken);

        await using (var index = new NpgsqlCommand(
            """
            select indexdef
            from pg_catalog.pg_indexes
            where schemaname = 'simulation' and indexname = 'backtests_queued_claim_idx'
            """,
            administrator))
        {
            object? indexDefinition =
                await index.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            Assert.Equal(
                "CREATE INDEX backtests_queued_claim_idx ON simulation.backtests "
                + "USING btree (created_at, id) WHERE (status = 'QUEUED'::text)",
                Assert.IsType<string>(indexDefinition));
        }

        // Every grant yo4x_worker holds anywhere in the simulation schema, schema
        // and relation level, enumerated from the catalog rather than asserted one
        // capability at a time: a grant nobody asked for shows up as an extra row.
        var granted = new List<string>();
        await using (var acl = new NpgsqlCommand(
            """
            select 'schema:' || namespace.nspname || ':' || privilege.privilege_type
                || case when privilege.is_grantable then ':GRANTABLE' else '' end as entry
            from pg_catalog.pg_namespace as namespace
            cross join lateral pg_catalog.aclexplode(namespace.nspacl) as privilege
            join pg_catalog.pg_roles as role on role.oid = privilege.grantee
            where namespace.nspname = 'simulation' and role.rolname = 'yo4x_worker'

            union all

            select 'relation:' || namespace.nspname || '.' || relation.relname || ':'
                || privilege.privilege_type
                || case when privilege.is_grantable then ':GRANTABLE' else '' end
            from pg_catalog.pg_class as relation
            join pg_catalog.pg_namespace as namespace
              on namespace.oid = relation.relnamespace
            cross join lateral pg_catalog.aclexplode(relation.relacl) as privilege
            join pg_catalog.pg_roles as role on role.oid = privilege.grantee
            where namespace.nspname = 'simulation' and role.rolname = 'yo4x_worker'

            union all

            select 'column:' || namespace.nspname || '.' || relation.relname || '.'
                || attribute.attname || ':' || privilege.privilege_type
            from pg_catalog.pg_attribute as attribute
            join pg_catalog.pg_class as relation on relation.oid = attribute.attrelid
            join pg_catalog.pg_namespace as namespace
              on namespace.oid = relation.relnamespace
            cross join lateral pg_catalog.aclexplode(attribute.attacl) as privilege
            join pg_catalog.pg_roles as role on role.oid = privilege.grantee
            where namespace.nspname = 'simulation' and role.rolname = 'yo4x_worker'

            order by entry
            """,
            administrator))
        await using (NpgsqlDataReader reader =
            await acl.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                granted.Add(reader.GetString(0));
            }
        }

        Assert.Equal(
            [
                "relation:simulation.backtest_inputs:SELECT",
                "relation:simulation.backtests:SELECT",
                "relation:simulation.backtests:UPDATE",
                "schema:simulation:USAGE"
            ],
            granted);

        await using (var effective = new NpgsqlCommand(
            """
            select has_schema_privilege('yo4x_worker', 'simulation', 'USAGE'),
                   has_schema_privilege('yo4x_worker', 'simulation', 'CREATE'),
                   has_table_privilege('yo4x_worker', 'simulation.backtests', 'SELECT'),
                   has_table_privilege('yo4x_worker', 'simulation.backtests', 'UPDATE'),
                   has_table_privilege('yo4x_worker', 'simulation.backtests', 'INSERT'),
                   has_table_privilege('yo4x_worker', 'simulation.backtests', 'DELETE'),
                   has_table_privilege('yo4x_worker', 'simulation.backtests', 'TRUNCATE'),
                   has_table_privilege('yo4x_worker', 'simulation.backtest_inputs', 'SELECT'),
                   has_table_privilege('yo4x_worker', 'simulation.backtest_inputs', 'INSERT'),
                   has_table_privilege('yo4x_worker', 'simulation.backtest_inputs', 'UPDATE'),
                   has_table_privilege('yo4x_worker', 'simulation.backtest_inputs', 'DELETE')
            """,
            administrator))
        await using (NpgsqlDataReader reader =
            await effective.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.True(reader.GetBoolean(0));
            Assert.False(reader.GetBoolean(1));
            Assert.True(reader.GetBoolean(2));
            Assert.True(reader.GetBoolean(3));
            Assert.False(reader.GetBoolean(4));
            Assert.False(reader.GetBoolean(5));
            Assert.False(reader.GetBoolean(6));
            Assert.True(reader.GetBoolean(7));
            Assert.False(reader.GetBoolean(8));
            Assert.False(reader.GetBoolean(9));
            Assert.False(reader.GetBoolean(10));
        }
    }

    [PostgresFact]
    public async Task WorkerCanClaimQueuedRequestsButCannotCreateOrRemoveThem()
    {
        await using PostgresTestDatabase database = await fixture.CreateDatabaseAsync();
        await using var worker = new NpgsqlConnection(database.WorkerConnectionString);
        await worker.OpenAsync(TestContext.Current.CancellationToken);

        await using (var identity = new NpgsqlCommand("select current_user", worker))
        {
            Assert.Equal(
                "yo4x_worker",
                Assert.IsType<string>(
                    await identity.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
        }

        // The claim exactly as a runner issues it. The database is empty, so it
        // claims nothing; what is under test is that the statement is authorized
        // rather than refused, which PostgreSQL decides before any row is matched.
        await using (var claim = new NpgsqlCommand(
            """
            with claimable as (
                select backtest.id
                from simulation.backtests as backtest
                where backtest.status = 'QUEUED'
                order by backtest.created_at, backtest.id
                for update skip locked
                limit 1
            )
            update simulation.backtests as backtest
            set status = 'RUNNING'
            from claimable
            where backtest.id = claimable.id
            returning backtest.id
            """,
            worker))
        {
            Assert.Equal(0, await claim.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }

        await using (var outcome = new NpgsqlCommand(
            """
            update simulation.backtests
            set status = 'COMPLETE', net_profit_amount = 1.00, completed_at = clock_timestamp()
            where false
            """,
            worker))
        {
            Assert.Equal(0, await outcome.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }

        await using (var inputs = new NpgsqlCommand(
            "select count(*) from simulation.backtest_inputs",
            worker))
        {
            Assert.Equal(
                0L,
                Assert.IsType<long>(
                    await inputs.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
        }

        // The plan the claim index exists for. Sequential access is cheaper on an
        // empty table, so the choice is forced: what is asserted is that the index
        // can serve the predicate and the ordering, not which plan the planner
        // prefers at this size.
        await using (NpgsqlTransaction planning =
            await worker.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await using (var forceIndex = new NpgsqlCommand(
                "set local enable_seqscan = off",
                worker,
                planning))
            {
                await forceIndex.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            await using var explain = new NpgsqlCommand(
                """
                explain (costs off)
                select id from simulation.backtests
                where status = 'QUEUED'
                order by created_at, id
                limit 1
                """,
                worker,
                planning);
            var plan = new List<string>();
            await using (NpgsqlDataReader reader =
                await explain.ExecuteReaderAsync(TestContext.Current.CancellationToken))
            {
                while (await reader.ReadAsync(TestContext.Current.CancellationToken))
                {
                    plan.Add(reader.GetString(0));
                }
            }

            Assert.Contains(plan, line => line.Contains("backtests_queued_claim_idx", StringComparison.Ordinal));
            Assert.DoesNotContain(plan, line => line.Contains("Sort Key", StringComparison.Ordinal));
            await planning.RollbackAsync(TestContext.Current.CancellationToken);
        }

        await AssertRefusedAsync(
            worker,
            """
            insert into simulation.backtests
                (id, tenant_id, user_id, strategy_id, period_start, period_end)
            select gen_random_uuid(), gen_random_uuid(), gen_random_uuid(),
                   gen_random_uuid(), current_date, current_date
            where false
            """);
        await AssertRefusedAsync(worker, "delete from simulation.backtests where false");
        await AssertRefusedAsync(worker, "truncate simulation.backtests");
        await AssertRefusedAsync(
            worker,
            """
            insert into simulation.backtest_inputs (id, tenant_id, backtest_id, name, value)
            select gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), 'Period', '14'
            where false
            """);
        await AssertRefusedAsync(
            worker,
            "update simulation.backtest_inputs set value = '99' where false");
        await AssertRefusedAsync(worker, "delete from simulation.backtest_inputs where false");
        await AssertRefusedAsync(worker, "create table simulation.worker_probe (id integer)");
    }

    private static async Task AssertRefusedAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal("42501", refusal.SqlState);
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);
    }
}
