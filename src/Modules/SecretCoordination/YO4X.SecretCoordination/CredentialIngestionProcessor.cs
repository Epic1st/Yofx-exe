using YO4X.BuildingBlocks;

namespace YO4X.SecretCoordination;

/// <summary>
/// Contains only digests of the short-lived ingestion proof. Raw bearer and
/// nonce values must be hashed at the transport boundary and never logged.
/// </summary>
public sealed class CredentialIngestionProof
{
    public CredentialIngestionProof(
        Guid tenantId,
        Guid grantId,
        string origin,
        string bearerHash,
        string nonceHash)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An ingestion tenant identifier is required.", nameof(tenantId));
        }

        if (grantId == Guid.Empty)
        {
            throw new ArgumentException("An ingestion grant identifier is required.", nameof(grantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        ValidateDigest(bearerHash, nameof(bearerHash));
        ValidateDigest(nonceHash, nameof(nonceHash));
        TenantId = tenantId;
        GrantId = grantId;
        Origin = origin;
        BearerHash = bearerHash.ToLowerInvariant();
        NonceHash = nonceHash.ToLowerInvariant();
    }

    public Guid TenantId { get; }

    public Guid GrantId { get; }

    public string Origin { get; }

    public string BearerHash { get; }

    public string NonceHash { get; }

    public override string ToString() =>
        $"CredentialIngestionProof {{ TenantId = {TenantId}, GrantId = {GrantId}, Proof = [REDACTED] }}";

    private static void ValidateDigest(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 64
            || value.Any(character => character is not (>= '0' and <= '9')
                and not (>= 'A' and <= 'F')
                and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("A hexadecimal SHA-256 digest is required.", parameterName);
        }
    }
}

public sealed record CredentialIngestionCompletion(Guid GrantId, DateTimeOffset CompletedAt);

public enum CredentialIngestionReservationDisposition
{
    Acquired,
    InProgress,
    Completed
}

public sealed record CredentialIngestionReservation(
    Guid GrantId,
    Guid TenantId,
    Guid BrokerAccountId,
    CredentialIngestionOperation Operation,
    Guid AttemptId,
    CredentialIngestionReservationDisposition Disposition,
    DateTimeOffset? CompletedAt,
    long GrantVersion = 0)
{
    public SecretWriteBinding ToWriteBinding() =>
        new(TenantId, BrokerAccountId, Operation, GrantId);
}

/// <summary>
/// Persists grant reservations and completions in short transactions. Proof
/// mismatch, expiry, revocation, and unknown identifiers must all produce the
/// same UnauthorizedAccessException contract.
/// </summary>
public interface ICredentialIngestionGrantStore
{
    ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken);

    Task<CredentialIngestionReservation> ReserveAsync(
        CredentialIngestionProof proof,
        DateTimeOffset now,
        TimeSpan reservationDuration,
        CancellationToken cancellationToken);

    Task ReleaseBeforeWriteAsync(
        CredentialIngestionReservation reservation,
        DateTimeOffset releasedAt,
        CancellationToken cancellationToken);

    Task<CredentialIngestionCompletion> CompleteAsync(
        CredentialIngestionReservation reservation,
        SecretWriteReceipt receipt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);
}

/// <summary>
/// Coordinates a short database reservation, a write-only idempotent vault
/// write keyed by GrantId, and a short completion transaction. Implementations
/// must invoke readMaterial only after the grant proof is valid and the write
/// has been reserved, and must never hold a database transaction open while
/// reading the body or calling the vault.
/// </summary>
public interface ICredentialIngestionProcessor
{
    ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken);

    Task<CredentialIngestionCompletion> ConsumeAsync(
        CredentialIngestionProof proof,
        Func<CancellationToken, ValueTask<SecretMaterial>> readMaterial,
        CancellationToken cancellationToken);
}

public sealed class CredentialIngestionProcessor(
    ICredentialIngestionGrantStore grantStore,
    IWriteOnlySecretBroker secretBroker,
    IClock clock) : ICredentialIngestionProcessor
{
    private static readonly TimeSpan ReservationDuration = TimeSpan.FromSeconds(30);

    public async ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        if (!await grantStore.IsReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        return await secretBroker.IsReadyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CredentialIngestionCompletion> ConsumeAsync(
        CredentialIngestionProof proof,
        Func<CancellationToken, ValueTask<SecretMaterial>> readMaterial,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(readMaterial);

        if (!await IsReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new BackendCapabilityUnavailableException("credential-ingestion");
        }

        CredentialIngestionReservation reservation = await grantStore.ReserveAsync(
            proof,
            clock.UtcNow,
            ReservationDuration,
            cancellationToken).ConfigureAwait(false);

        if (reservation.Disposition == CredentialIngestionReservationDisposition.Completed)
        {
            return new CredentialIngestionCompletion(
                reservation.GrantId,
                reservation.CompletedAt ?? throw new InvalidOperationException("A completed reservation has no timestamp."));
        }

        if (reservation.Disposition == CredentialIngestionReservationDisposition.InProgress)
        {
            throw new ResourceConflictException(
                "INGESTION_IN_PROGRESS",
                "Credential ingestion for this grant is already in progress.");
        }

        SecretMaterial material;
        try
        {
            material = await readMaterial(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await grantStore.ReleaseBeforeWriteAsync(reservation, clock.UtcNow, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }

        SecretWriteReceipt receipt;
        using (material)
        {
            // The broker contract is idempotent for the GrantId in the binding.
            // An uncertain outcome therefore remains safely retryable after the
            // reservation lease expires without ever reading the secret back.
            receipt = await secretBroker.WriteAsync(
                reservation.ToWriteBinding(),
                material,
                cancellationToken).ConfigureAwait(false);
        }

        SecretWriteBinding expectedBinding = reservation.ToWriteBinding();
        if (receipt is null
            || receipt.State != SecretWriteReceiptState.Stored
            || !receipt.IsBoundTo(expectedBinding)
            || !await secretBroker.VerifyReceiptAsync(receipt, cancellationToken).ConfigureAwait(false))
        {
            throw new BackendCapabilityUnavailableException("credential-ingestion-receipt-verification");
        }

        return await grantStore.CompleteAsync(
            reservation,
            receipt,
            clock.UtcNow,
            cancellationToken).ConfigureAwait(false);
    }
}

public sealed class UnavailableCredentialIngestionProcessor : ICredentialIngestionProcessor
{
    public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public Task<CredentialIngestionCompletion> ConsumeAsync(
        CredentialIngestionProof proof,
        Func<CancellationToken, ValueTask<SecretMaterial>> readMaterial,
        CancellationToken cancellationToken) =>
        Task.FromException<CredentialIngestionCompletion>(
            new BackendCapabilityUnavailableException("credential-ingestion"));
}
