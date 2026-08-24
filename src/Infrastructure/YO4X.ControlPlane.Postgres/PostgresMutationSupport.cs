using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using YO4X.Audit;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Outbox;
using YO4X.Persistence.Postgres;

namespace YO4X.ControlPlane.Postgres;

public sealed partial class PostgresControlPlaneApplication
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan IdempotencyLifetime =
        ControlPlanePostgresOptions.IdempotencyReplayLifetime;

    private static async Task<MutationLease<TResponse>> BeginMutationAsync<TRequest, TResponse>(
        TenantPostgresTransaction transaction,
        string operation,
        RequestMetadata metadata,
        TRequest request,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        ValidateMetadata(metadata);
        string requestSha256 = CanonicalJson.Sha256(request);
        DateTimeOffset now = await ReadDatabaseStatementTimeAsync(transaction, cancellationToken)
            .ConfigureAwait(false);
        IdempotencyLease lease = await PostgresIdempotencyRepository.TryAcquireAsync(
            transaction,
            operation,
            metadata.IdempotencyKey,
            requestSha256,
            now,
            now.Add(IdempotencyLifetime),
            cancellationToken).ConfigureAwait(false);

        if (lease.Acquired)
        {
            return new MutationLease<TResponse>(lease.Id, null);
        }

        if (!FixedTimeEquals(lease.RequestSha256, requestSha256))
        {
            throw new ResourceConflictException(
                "IDEMPOTENCY_KEY_REUSED",
                "The idempotency key was already used for a different request.");
        }

        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select state, response_body::text, response_sha256, response_status
            from control.idempotency_records
            where tenant_id = @tenant_id and id = @id
            """);
        AddUuid(command, "tenant_id", transaction.Context.TenantId);
        AddUuid(command, "id", lease.Id);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The idempotency record could not be loaded.");
        }

        string state = reader.GetString(0);
        string? responseJson = reader.IsDBNull(1) ? null : reader.GetString(1);
        string? responseSha256 = reader.IsDBNull(2) ? null : reader.GetString(2);
        int? responseStatus = reader.IsDBNull(3) ? null : reader.GetInt32(3);
        if (!string.Equals(state, "completed", StringComparison.Ordinal)
            || responseJson is null
            || responseSha256 is null
            || responseStatus is not (>= 200 and <= 299))
        {
            throw new ResourceConflictException(
                "REQUEST_IN_PROGRESS",
                "The request is already in progress.");
        }


        JsonNode? responseNode;
        try
        {
            responseNode = JsonNode.Parse(responseJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The stored idempotent response is invalid.", exception);
        }

        if (responseNode is null
            || !FixedTimeEquals(CanonicalJson.Sha256(responseNode), responseSha256))
        {
            throw new ResourceConflictException(
                "IDEMPOTENT_RESPONSE_INTEGRITY_INVALID",
                "The stored idempotent response failed its integrity check.");
        }

        TResponse? response = JsonSerializer.Deserialize<TResponse>(responseJson, WebJson);
        return response is null
            ? throw new InvalidOperationException("The stored idempotent response is invalid.")
            : new MutationLease<TResponse>(lease.Id, response);
    }

    private static async Task CompleteMutationAsync<TResponse>(
        TenantPostgresTransaction transaction,
        Guid leaseId,
        int statusCode,
        TResponse response,
        CancellationToken cancellationToken)
    {
        string responseJson = CanonicalJson.Serialize(response);
        string responseSha256 = Sha256Utf8(responseJson);
        DateTimeOffset completedAt = await ReadDatabaseStatementTimeAsync(transaction, cancellationToken)
            .ConfigureAwait(false);
        if (!await PostgresIdempotencyRepository.CompleteAsync(
                transaction,
                leaseId,
                statusCode,
                responseJson,
                responseSha256,
                completedAt,
                cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The idempotency record could not be completed.");
        }
    }

    private static async Task AppendMutationEvidenceAsync<TPayload>(
        TenantPostgresTransaction transaction,
        string action,
        string targetType,
        Guid targetId,
        string? reason,
        Guid causationId,
        TPayload redactedPayload,
        AuditCategory category,
        AuditOutcome outcome,
        AuditEvidenceContext evidenceContext,
        CancellationToken cancellationToken,
        DateTimeOffset? authoritativeOccurredAt = null)
    {
        DateTimeOffset occurredAt = authoritativeOccurredAt?.ToUniversalTime()
            ?? await ReadDatabaseStatementTimeAsync(transaction, cancellationToken).ConfigureAwait(false);
        AuditEvent audit = AuditEvent.Create(
            transaction.Context.TenantId,
            transaction.Context.ActorId,
            category,
            action,
            targetType,
            targetId.ToString("D"),
            outcome,
            reason,
            transaction.Context.CorrelationId,
            causationId,
            redactedPayload,
            occurredAt,
            evidenceContext);
        OutboxMessage outbox = OutboxMessage.Create(
            transaction.Context.TenantId,
            action,
            targetType,
            targetId.ToString("D"),
            redactedPayload,
            transaction.Context.CorrelationId,
            causationId,
            occurredAt);
        await PostgresAuditOutboxWriter.AppendAsync(transaction, audit, outbox, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Sha256Utf8(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        byte[] leftBytes = Encoding.ASCII.GetBytes(left);
        byte[] rightBytes = Encoding.ASCII.GetBytes(right);
        try
        {
            return leftBytes.Length == rightBytes.Length
                && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }

    private static void ValidateMetadata(RequestMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (metadata.IdempotencyKey is null
            || (!HexIdempotencyKeyPattern().IsMatch(metadata.IdempotencyKey)
                && !Base64UrlIdempotencyKeyPattern().IsMatch(metadata.IdempotencyKey)))
        {
            throw new DomainException(
                "IDEMPOTENCY_KEY_INVALID",
                "The idempotency key format is invalid.");
        }
        if (metadata.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("A correlation identifier is required.", nameof(metadata));
        }
    }

    private sealed record MutationLease<TResponse>(Guid Id, TResponse? Replay)
        where TResponse : class;

    [GeneratedRegex("^[A-Fa-f0-9]{32,200}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexIdempotencyKeyPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{22,200}$", RegexOptions.CultureInvariant)]
    private static partial Regex Base64UrlIdempotencyKeyPattern();
}
