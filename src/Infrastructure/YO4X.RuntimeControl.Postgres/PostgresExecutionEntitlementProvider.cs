using Npgsql;
using NpgsqlTypes;
using YO4X.ControlPlane.Application;
using YO4X.Persistence.Postgres;
using YO4X.Runtime.Contracts;
using YO4X.Tenancy;

namespace YO4X.RuntimeControl.Postgres;

/// <summary>
/// Resolves one online license and allocates its concurrency slot atomically. The license row
/// is locked before expired slots are reclaimed and the count is evaluated, preventing two
/// simultaneous launches from both taking the final slot.
/// </summary>
public sealed class PostgresExecutionEntitlementProvider(PostgresDatabase database)
    : IExecutionEntitlementProvider
{
    private static readonly TimeSpan ActivationLifetime = TimeSpan.FromMinutes(15);
    private static readonly ExecutionLeaseActionPolicy DemoPolicy = new(
        LeaseActionClass.Increase | LeaseActionClass.Reduce | LeaseActionClass.Protect
            | LeaseActionClass.Cancel | LeaseActionClass.EmergencyClose,
        LeaseActionClass.Reduce | LeaseActionClass.Protect | LeaseActionClass.Cancel
            | LeaseActionClass.EmergencyClose,
        LeaseActionClass.Reduce | LeaseActionClass.Protect | LeaseActionClass.Cancel
            | LeaseActionClass.EmergencyClose,
        LeaseActionClass.Reduce | LeaseActionClass.Protect | LeaseActionClass.Cancel
            | LeaseActionClass.EmergencyClose);

    private readonly PostgresDatabase database = database
        ?? throw new ArgumentNullException(nameof(database));

    public async ValueTask<ExecutionEntitlementGrant?> ResolveAsync(
        ExecutionEntitlementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TenantId == Guid.Empty
            || request.UserId == Guid.Empty
            || request.DeploymentId == Guid.Empty
            || request.BrokerAccountId == Guid.Empty
            || request.StrategyId == Guid.Empty
            || request.StrategyVersionId == Guid.Empty
            || request.StrategyVersion < 1
            || !IsSha256(request.StrategyPackageSha256)
            || request.ExecutionMode != ExecutionMode.CloudDemo)
        {
            return null;
        }

        var context = new TenantExecutionContext(
            request.TenantId,
            request.UserId,
            Guid.CreateVersion7(),
            null);
        await using TenantPostgresTransaction transaction = await database
            .BeginTenantTransactionAsync(context, cancellationToken)
            .ConfigureAwait(false);

        LicenseRow? license = await LockLicenseAsync(transaction, request, cancellationToken)
            .ConfigureAwait(false);
        if (license is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        await using (NpgsqlCommand reclaim = transaction.CreateCommand(
            """
            delete from catalog.strategy_license_activations
            where tenant_id = @tenant_id
              and license_id = @license_id
              and expires_at <= clock_timestamp()
            """))
        {
            AddUuid(reclaim, "tenant_id", request.TenantId);
            AddUuid(reclaim, "license_id", license.Id);
            await reclaim.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        Guid? existingId = await ReadExistingActivationAsync(
            transaction, request, license.Id, cancellationToken).ConfigureAwait(false);
        if (existingId is null)
        {
            await using NpgsqlCommand count = transaction.CreateCommand(
                """
                select count(*)
                from catalog.strategy_license_activations
                where tenant_id = @tenant_id
                  and license_id = @license_id
                  and expires_at > clock_timestamp()
                """);
            AddUuid(count, "tenant_id", request.TenantId);
            AddUuid(count, "license_id", license.Id);
            long active = (long)(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The entitlement slot count was unavailable."));
            if (active >= license.MaxConcurrentBots)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }
        }

        DateTimeOffset requestedExpiry = license.ExpiresAt is { } licenseExpiry
            ? Min(request.RequestedAtUtc.Add(ActivationLifetime), licenseExpiry)
            : request.RequestedAtUtc.Add(ActivationLifetime);
        if (requestedExpiry <= request.RequestedAtUtc)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        Guid activationId = existingId ?? Guid.CreateVersion7();
        await UpsertActivationAsync(
            transaction,
            request,
            license.Id,
            activationId,
            requestedExpiry,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ExecutionEntitlementGrant(
            activationId,
            Max(request.RequestedAtUtc, license.NotBefore ?? license.IssuedAt),
            requestedExpiry,
            DemoPolicy);
    }

    private static async Task<LicenseRow?> LockLicenseAsync(
        TenantPostgresTransaction transaction,
        ExecutionEntitlementRequest request,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select license.id, license.issued_at, license.not_before,
                   license.expires_at, license.max_concurrent_bots
            from catalog.strategy_licenses as license
            where license.tenant_id = @tenant_id
              and license.user_id = @user_id
              and license.strategy_id = @strategy_id
              and license.strategy_version_id = @strategy_version_id
              and license.package_sha256 = @package_sha256
              and @broker_account_id = any(license.bound_broker_account_ids)
              and not license.is_revoked
              and license.issued_at <= @requested_at
              and coalesce(license.not_before, license.issued_at) <= @requested_at
              and (license.expires_at is null or license.expires_at > @requested_at)
            order by license.expires_at nulls last, license.id
            limit 1
            for update
            """);
        AddUuid(command, "tenant_id", request.TenantId);
        AddUuid(command, "user_id", request.UserId);
        AddUuid(command, "strategy_id", request.StrategyId);
        AddUuid(command, "strategy_version_id", request.StrategyVersionId);
        AddUuid(command, "broker_account_id", request.BrokerAccountId);
        command.Parameters.AddWithValue("package_sha256", NpgsqlDbType.Text, request.StrategyPackageSha256);
        command.Parameters.AddWithValue("requested_at", NpgsqlDbType.TimestampTz, request.RequestedAtUtc);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new LicenseRow(
            reader.GetGuid(0),
            reader.GetFieldValue<DateTimeOffset>(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetInt32(4));
    }

    private static async Task<Guid?> ReadExistingActivationAsync(
        TenantPostgresTransaction transaction,
        ExecutionEntitlementRequest request,
        Guid licenseId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select id
            from catalog.strategy_license_activations
            where tenant_id = @tenant_id
              and license_id = @license_id
              and deployment_id = @deployment_id
            """);
        AddUuid(command, "tenant_id", request.TenantId);
        AddUuid(command, "license_id", licenseId);
        AddUuid(command, "deployment_id", request.DeploymentId);
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is Guid id ? id : null;
    }

    private static async Task UpsertActivationAsync(
        TenantPostgresTransaction transaction,
        ExecutionEntitlementRequest request,
        Guid licenseId,
        Guid activationId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into catalog.strategy_license_activations
            (
                id, tenant_id, license_id, user_id, deployment_id, broker_account_id,
                strategy_id, strategy_version_id, package_sha256,
                activated_at, renewed_at, expires_at
            )
            values
            (
                @id, @tenant_id, @license_id, @user_id, @deployment_id, @broker_account_id,
                @strategy_id, @strategy_version_id, @package_sha256,
                @requested_at, @requested_at, @expires_at
            )
            on conflict (tenant_id, license_id, deployment_id) do update
            set renewed_at = excluded.renewed_at,
                expires_at = excluded.expires_at,
                broker_account_id = excluded.broker_account_id,
                strategy_id = excluded.strategy_id,
                strategy_version_id = excluded.strategy_version_id,
                package_sha256 = excluded.package_sha256,
                updated_at = clock_timestamp()
            where strategy_license_activations.user_id = excluded.user_id
            """);
        AddUuid(command, "id", activationId);
        AddUuid(command, "tenant_id", request.TenantId);
        AddUuid(command, "license_id", licenseId);
        AddUuid(command, "user_id", request.UserId);
        AddUuid(command, "deployment_id", request.DeploymentId);
        AddUuid(command, "broker_account_id", request.BrokerAccountId);
        AddUuid(command, "strategy_id", request.StrategyId);
        AddUuid(command, "strategy_version_id", request.StrategyVersionId);
        command.Parameters.AddWithValue("package_sha256", NpgsqlDbType.Text, request.StrategyPackageSha256);
        command.Parameters.AddWithValue("requested_at", NpgsqlDbType.TimestampTz, request.RequestedAtUtc);
        command.Parameters.AddWithValue("expires_at", NpgsqlDbType.TimestampTz, expiresAt);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("The entitlement activation was not durably renewed.");
    }

    private static void AddUuid(NpgsqlCommand command, string name, Guid value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Uuid, value);

    private static bool IsSha256(string value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;
    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    private sealed record LicenseRow(
        Guid Id,
        DateTimeOffset IssuedAt,
        DateTimeOffset? NotBefore,
        DateTimeOffset? ExpiresAt,
        int MaxConcurrentBots);
}
