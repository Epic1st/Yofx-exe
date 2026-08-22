using System.Data.Common;
using Npgsql;
using YO4X.ControlPlane.Application;
using YO4X.Persistence.Postgres;
using YO4X.RuntimeControl.Postgres;

namespace YO4X.ControlPlane.Api;

internal sealed class ControlPlaneReadinessProbe(IServiceScopeFactory scopeFactory)
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    public async ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        if (services.GetService<IControlPlaneApplication>() is not IControlPlaneApplication controlApplication
            || controlApplication is UnavailableControlPlaneApplication
            || services.GetService<IRuntimeControlPlaneApplication>() is not IRuntimeControlPlaneApplication runtimeApplication
            || runtimeApplication is UnavailableRuntimeControlPlaneApplication
            || services.GetService<PostgresDatabase>() is not PostgresDatabase controlDatabase
            || services.GetService<RuntimePostgresDatabase>() is not RuntimePostgresDatabase runtimeDatabase
            || services.GetService<RuntimeEvidencePostgresDatabase>() is not RuntimeEvidencePostgresDatabase evidenceDatabase)
        {
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            bool controlReady = await ProbeControlDatabaseAsync(controlDatabase, timeout.Token).ConfigureAwait(false);
            return controlReady
                && await ProbeRuntimeDatabaseAsync(runtimeDatabase, timeout.Token).ConfigureAwait(false)
                && await evidenceDatabase.IsReadyAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (DbException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static async Task<bool> ProbeControlDatabaseAsync(
        PostgresDatabase database,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            select current_user = 'yo4x_control_api'
               and to_regclass('operations.deployments') is not null
               and to_regclass('operations.broker_accounts') is not null
               and to_regclass('governance.compatibility_test_runs') is not null
               and has_table_privilege(current_user, 'operations.deployments', 'SELECT')
               and has_column_privilege(current_user, 'governance.compatibility_test_runs', 'evidence_sha256', 'SELECT')
            """;
        command.CommandTimeout = (int)ProbeTimeout.TotalSeconds;
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is true;
    }

    private static async Task<bool> ProbeRuntimeDatabaseAsync(
        RuntimePostgresDatabase database,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await database
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            select current_user = 'yo4x_worker'
               and to_regclass('operations.worker_assignments') is not null
               and to_regclass('operations.runtime_component_evidence') is not null
               and to_regclass('operations.runtime_event_cursors') is not null
               and to_regclass('operations.runtime_event_inbox') is not null
               and to_regclass('operations.execution_leases') is not null
               and to_regclass('control.command_targets') is not null
               and has_table_privilege(current_user, 'operations.worker_assignments', 'SELECT,INSERT,UPDATE')
               and has_table_privilege(current_user, 'operations.runtime_component_evidence', 'SELECT,INSERT')
               and has_table_privilege(current_user, 'operations.runtime_event_inbox', 'SELECT,INSERT,UPDATE')
               and has_table_privilege(current_user, 'operations.execution_leases', 'SELECT,INSERT,UPDATE')
               and has_table_privilege(current_user, 'control.command_targets', 'SELECT,UPDATE')
            """;
        command.CommandTimeout = (int)ProbeTimeout.TotalSeconds;
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is true;
    }
}
