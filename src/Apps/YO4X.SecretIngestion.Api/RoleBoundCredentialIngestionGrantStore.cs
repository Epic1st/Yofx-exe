using Npgsql;
using NpgsqlTypes;
using YO4X.Persistence.Postgres;
using YO4X.SecretCoordination;

namespace YO4X.SecretIngestion.Api;

internal sealed class RoleBoundCredentialIngestionGrantStore(
    PostgresDatabase database,
    PostgresCredentialIngestionGrantStore inner,
    SecretIngestionPostgresOptions options) : ICredentialIngestionGrantStore
{
    private const string ReadinessSql = """
        select
            current_user = @expected_role
            and (not @require_tls or coalesce(
                (select ssl from pg_catalog.pg_stat_ssl where pid = pg_catalog.pg_backend_pid()),
                false))
        """;

    public async ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using NpgsqlCommand command = new(ReadinessSql, connection);
            command.Parameters.AddWithValue("expected_role", NpgsqlDbType.Text, options.ExpectedDatabaseRole);
            command.Parameters.AddWithValue("require_tls", NpgsqlDbType.Boolean, options.RequireTls);
            if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not true)
            {
                return false;
            }

            return await inner.IsReadyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (NpgsqlException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (TimeoutException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public Task<CredentialIngestionReservation> ReserveAsync(
        CredentialIngestionProof proof,
        DateTimeOffset now,
        TimeSpan reservationDuration,
        CancellationToken cancellationToken) =>
        inner.ReserveAsync(proof, now, reservationDuration, cancellationToken);

    public Task ReleaseBeforeWriteAsync(
        CredentialIngestionReservation reservation,
        DateTimeOffset releasedAt,
        CancellationToken cancellationToken) =>
        inner.ReleaseBeforeWriteAsync(reservation, releasedAt, cancellationToken);

    public Task<CredentialIngestionCompletion> CompleteAsync(
        CredentialIngestionReservation reservation,
        SecretWriteReceipt receipt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken) =>
        inner.CompleteAsync(reservation, receipt, completedAt, cancellationToken);
}
