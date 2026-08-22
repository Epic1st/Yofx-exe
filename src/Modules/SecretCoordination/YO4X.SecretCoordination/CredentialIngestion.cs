using System.Security.Cryptography;
using System.Text.RegularExpressions;
using YO4X.BuildingBlocks;

namespace YO4X.SecretCoordination;

public enum CredentialIngestionOperation
{
    Create,
    Rotate
}

public enum IngestionGrantState
{
    Active,
    Reserved,
    Consumed,
    Expired,
    Revoked
}

public sealed partial class CredentialIngestionGrant : VersionedAggregate
{
    private CredentialIngestionGrant(
        Guid id,
        Guid tenantId,
        Guid brokerAccountId,
        CredentialIngestionOperation operation,
        string origin,
        string bearerHash,
        string nonceHash,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        TenantId = tenantId;
        BrokerAccountId = brokerAccountId;
        Operation = operation;
        Origin = origin;
        BearerHash = bearerHash;
        NonceHash = nonceHash;
        ExpiresAt = expiresAt.ToUniversalTime();
        State = IngestionGrantState.Active;
    }

    public Guid TenantId { get; }

    public Guid BrokerAccountId { get; }

    public CredentialIngestionOperation Operation { get; }

    public string Origin { get; }

    public string BearerHash { get; }

    public string NonceHash { get; }

    public DateTimeOffset ExpiresAt { get; }

    public IngestionGrantState State { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public string? CompletionDigest { get; private set; }

    public Guid? ReservationId { get; private set; }

    public DateTimeOffset? ReservedAt { get; private set; }

    public DateTimeOffset? ReservationExpiresAt { get; private set; }

    public static CredentialIngestionGrant Issue(
        Guid tenantId,
        Guid brokerAccountId,
        CredentialIngestionOperation operation,
        Uri allowedOrigin,
        string bearerHash,
        string nonceHash,
        DateTimeOffset expiresAt,
        IClock clock)
    {
        if (tenantId == Guid.Empty || brokerAccountId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and broker account identifiers are required.");
        }

        ArgumentNullException.ThrowIfNull(allowedOrigin);
        if (!allowedOrigin.IsAbsoluteUri || allowedOrigin.Scheme != Uri.UriSchemeHttps || allowedOrigin.PathAndQuery != "/")
        {
            throw new ArgumentException("The ingestion origin must be an HTTPS origin without a path.", nameof(allowedOrigin));
        }

        ValidateDigest(bearerHash, nameof(bearerHash));
        ValidateDigest(nonceHash, nameof(nonceHash));
        if (expiresAt <= clock.UtcNow || expiresAt - clock.UtcNow > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "The ingestion grant lifetime must not exceed ten minutes.");
        }

        return new CredentialIngestionGrant(
            Identifiers.NewId(),
            tenantId,
            brokerAccountId,
            operation,
            allowedOrigin.GetLeftPart(UriPartial.Authority),
            bearerHash.ToLowerInvariant(),
            nonceHash.ToLowerInvariant(),
            expiresAt,
            clock.UtcNow);
    }

    public static CredentialIngestionGrant Rehydrate(
        Guid id,
        Guid tenantId,
        Guid brokerAccountId,
        CredentialIngestionOperation operation,
        string origin,
        string bearerHash,
        string nonceHash,
        DateTimeOffset expiresAt,
        IngestionGrantState state,
        Guid? reservationId,
        DateTimeOffset? reservedAt,
        DateTimeOffset? reservationExpiresAt,
        DateTimeOffset? consumedAt,
        string? completionDigest,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        ValidateDigest(bearerHash, nameof(bearerHash));
        ValidateDigest(nonceHash, nameof(nonceHash));

        bool anyReservation = reservationId is not null
            || reservedAt is not null
            || reservationExpiresAt is not null;
        bool hasReservation = reservationId is not null
            && reservedAt is not null
            && reservationExpiresAt is not null;
        if (anyReservation != hasReservation)
        {
            throw new ArgumentException("Reservation metadata must be complete.", nameof(reservationId));
        }

        if (state is IngestionGrantState.Reserved or IngestionGrantState.Consumed && !hasReservation)
        {
            throw new ArgumentException("Reserved and consumed grants require reservation metadata.", nameof(state));
        }

        if (state is not (IngestionGrantState.Reserved or IngestionGrantState.Consumed) && hasReservation)
        {
            throw new ArgumentException("Inactive grants cannot contain reservation metadata.", nameof(state));
        }

        if (hasReservation && (reservationId == Guid.Empty || reservationExpiresAt <= reservedAt))
        {
            throw new ArgumentException("Reservation metadata is invalid.", nameof(reservationId));
        }

        if (state == IngestionGrantState.Consumed)
        {
            if (consumedAt is null || completionDigest is null)
            {
                throw new ArgumentException("Consumed grants require completion metadata.", nameof(state));
            }

            ValidateDigest(completionDigest, nameof(completionDigest));
        }
        else if (consumedAt is not null || completionDigest is not null)
        {
            throw new ArgumentException("Only consumed grants may contain completion metadata.", nameof(state));
        }

        var grant = new CredentialIngestionGrant(
            id,
            tenantId,
            brokerAccountId,
            operation,
            origin,
            bearerHash.ToLowerInvariant(),
            nonceHash.ToLowerInvariant(),
            expiresAt,
            createdAt)
        {
            State = state,
            ReservationId = reservationId,
            ReservedAt = reservedAt?.ToUniversalTime(),
            ReservationExpiresAt = reservationExpiresAt?.ToUniversalTime(),
            ConsumedAt = consumedAt?.ToUniversalTime(),
            CompletionDigest = completionDigest?.ToLowerInvariant()
        };
        grant.RestorePersistenceState(version, updatedAt);
        return grant;
    }

    public bool Authorize(
        Guid tenantId,
        Guid brokerAccountId,
        CredentialIngestionOperation operation,
        string origin,
        string bearerHash,
        string nonceHash,
        DateTimeOffset now)
    {
        if (State != IngestionGrantState.Active)
        {
            return false;
        }

        if (now >= ExpiresAt)
        {
            State = IngestionGrantState.Expired;
            RecordChange(now);
            return false;
        }

        return MatchesProof(tenantId, brokerAccountId, operation, origin, bearerHash, nonceHash);
    }

    public bool MatchesProof(
        Guid tenantId,
        Guid brokerAccountId,
        CredentialIngestionOperation operation,
        string origin,
        string bearerHash,
        string nonceHash) =>
        tenantId == TenantId
        && brokerAccountId == BrokerAccountId
        && operation == Operation
        && string.Equals(origin, Origin, StringComparison.Ordinal)
        && FixedTimeEquals(bearerHash, BearerHash)
        && FixedTimeEquals(nonceHash, NonceHash);

    public bool TryReserve(
        Guid reservationId,
        DateTimeOffset occurredAt,
        TimeSpan reservationDuration)
    {
        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException("A reservation identifier is required.", nameof(reservationId));
        }

        if (reservationDuration < TimeSpan.FromSeconds(1)
            || reservationDuration > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(reservationDuration));
        }

        DateTimeOffset now = occurredAt.ToUniversalTime();
        if (State == IngestionGrantState.Consumed)
        {
            return false;
        }

        if (State == IngestionGrantState.Reserved && ReservationExpiresAt > now)
        {
            return false;
        }

        if (State is not (IngestionGrantState.Active or IngestionGrantState.Reserved) || now >= ExpiresAt)
        {
            if (State is IngestionGrantState.Active or IngestionGrantState.Reserved)
            {
                State = IngestionGrantState.Expired;
                ReservationId = null;
                ReservedAt = null;
                ReservationExpiresAt = null;
                RecordChange(now);
            }

            return false;
        }

        ReservationId = reservationId;
        ReservedAt = now;
        ReservationExpiresAt = now.Add(reservationDuration);
        State = IngestionGrantState.Reserved;
        RecordChange(now);
        return true;
    }

    public void ReleaseBeforeWrite(Guid reservationId, DateTimeOffset occurredAt)
    {
        if (State != IngestionGrantState.Reserved || ReservationId != reservationId)
        {
            return;
        }

        DateTimeOffset now = occurredAt.ToUniversalTime();
        ReservationId = null;
        ReservedAt = null;
        ReservationExpiresAt = null;
        State = now >= ExpiresAt ? IngestionGrantState.Expired : IngestionGrantState.Active;
        RecordChange(now);
    }

    public void MarkConsumed(Guid reservationId, string completionDigest, DateTimeOffset occurredAt)
    {
        ValidateDigest(completionDigest, nameof(completionDigest));
        if (State == IngestionGrantState.Consumed)
        {
            if (!FixedTimeEquals(completionDigest, CompletionDigest!))
            {
                throw new DomainException("INGESTION_COMPLETION_CONFLICT", "The ingestion grant already has a different completion.");
            }

            return;
        }

        if (State != IngestionGrantState.Reserved || ReservationId != reservationId)
        {
            throw new DomainException("INGESTION_GRANT_INACTIVE", "The ingestion grant is no longer active.");
        }

        CompletionDigest = completionDigest.ToLowerInvariant();
        ConsumedAt = occurredAt.ToUniversalTime();
        State = IngestionGrantState.Consumed;
        RecordChange(occurredAt);
    }

    private static bool FixedTimeEquals(string first, string second)
    {
        byte[] firstBytes = System.Text.Encoding.UTF8.GetBytes(first);
        byte[] secondBytes = System.Text.Encoding.UTF8.GetBytes(second);
        try
        {
            return firstBytes.Length == secondBytes.Length
                && CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(firstBytes);
            CryptographicOperations.ZeroMemory(secondBytes);
        }
    }

    private static void ValidateDigest(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!DigestPattern().IsMatch(value))
        {
            throw new ArgumentException("A lowercase or uppercase SHA-256 hex digest is required.", parameterName);
        }
    }

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();
}

public sealed class SecretMaterial : IDisposable
{
    private byte[]? _bytes;

    public SecretMaterial(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), "Credential material must be between 1 and 4096 bytes.");
        }

        _bytes = bytes;
    }

    public ReadOnlyMemory<byte> Bytes => _bytes ?? throw new ObjectDisposedException(nameof(SecretMaterial));

    public void Dispose()
    {
        byte[]? bytes = Interlocked.Exchange(ref _bytes, null);
        if (bytes is not null)
        {
            CryptographicOperations.ZeroMemory(bytes);
        }

        GC.SuppressFinalize(this);
    }

    public override string ToString() => "[REDACTED SECRET MATERIAL]";
}

public sealed record SecretWriteBinding(
    Guid TenantId,
    Guid BrokerAccountId,
    CredentialIngestionOperation Operation,
    Guid GrantId);

public enum SecretBrokerProvider
{
    AzureKeyVault,
    AwsSecretsManager,
    GoogleSecretManager,
    HashiCorpVault
}

public enum SecretWriteReceiptState
{
    Stored
}

/// <summary>
/// Provider-signed, non-secret evidence that a write-only credential write was
/// persisted for one exact ingestion binding. The opaque reference is never
/// treated as authorization and its URI scheme is pinned to the provider type.
/// </summary>
public sealed class SecretWriteReceipt
{
    private const int MaximumOpaqueReferenceLength = 2_000;
    private const int MaximumSigningKeyIdLength = 500;
    private const int MaximumSignatureBytes = 1_024;
    private const int MaximumSignatureBase64Length = ((MaximumSignatureBytes + 2) / 3) * 4;

    public SecretWriteReceipt(
        SecretBrokerProvider provider,
        SecretWriteBinding binding,
        string opaqueReference,
        SecretWriteReceiptState state,
        string signatureAlgorithm,
        string signingKeyId,
        string signatureBase64)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.TenantId == Guid.Empty
            || binding.BrokerAccountId == Guid.Empty
            || binding.GrantId == Guid.Empty
            || !Enum.IsDefined(binding.Operation))
        {
            throw new ArgumentException("A complete secret-write binding is required.", nameof(binding));
        }

        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        if (state != SecretWriteReceiptState.Stored)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Only a durable stored receipt is accepted.");
        }

        Provider = provider;
        Binding = binding;
        OpaqueReference = NormalizeReference(provider, opaqueReference);
        State = state;
        SignatureAlgorithm = NormalizeToken(signatureAlgorithm, nameof(signatureAlgorithm), 100);
        SigningKeyId = NormalizeToken(signingKeyId, nameof(signingKeyId), MaximumSigningKeyIdLength);
        SignatureBase64 = NormalizeSignature(signatureBase64);
        CompletionDigest = CanonicalJson.Sha256(CreateSigningPayload());
    }

    public SecretBrokerProvider Provider { get; }

    public SecretWriteBinding Binding { get; }

    public string OpaqueReference { get; }

    public SecretWriteReceiptState State { get; }

    public string SignatureAlgorithm { get; }

    public string SigningKeyId { get; }

    public string SignatureBase64 { get; }

    public string CompletionDigest { get; }

    public string SigningPayloadJson => CanonicalJson.Serialize(CreateSigningPayload());

    public bool IsBoundTo(SecretWriteBinding expected) =>
        expected is not null
        && Binding.TenantId == expected.TenantId
        && Binding.BrokerAccountId == expected.BrokerAccountId
        && Binding.Operation == expected.Operation
        && Binding.GrantId == expected.GrantId;

    public override string ToString() =>
        $"SecretWriteReceipt {{ Provider = {Provider}, GrantId = {Binding.GrantId}, Reference = [REDACTED], Signature = [REDACTED] }}";

    private object CreateSigningPayload() => new
    {
        Provider = Provider.ToString(),
        Binding.TenantId,
        Binding.BrokerAccountId,
        Operation = Binding.Operation.ToString(),
        Binding.GrantId,
        OpaqueReference,
        State = State.ToString(),
        SignatureAlgorithm,
        SigningKeyId
    };

    private static string NormalizeReference(SecretBrokerProvider provider, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaximumOpaqueReferenceLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "The opaque credential reference is too long.");
        }

        string normalized = value.Trim();
        if (normalized.Any(char.IsControl)
            || !Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.Equals(uri.Scheme, RequiredScheme(provider), StringComparison.Ordinal))
        {
            throw new ArgumentException("The opaque credential reference is invalid for the provider.", nameof(value));
        }

        return uri.AbsoluteUri;
    }

    private static string RequiredScheme(SecretBrokerProvider provider) => provider switch
    {
        SecretBrokerProvider.AzureKeyVault => "azure-kv",
        SecretBrokerProvider.AwsSecretsManager => "aws-sm",
        SecretBrokerProvider.GoogleSecretManager => "gcp-sm",
        SecretBrokerProvider.HashiCorpVault => "vault",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private static string NormalizeToken(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The provider receipt token is too long.");
        }

        string normalized = value.Trim().ToLowerInvariant();
        if (normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not ':' and not '/'))
        {
            throw new ArgumentException("The provider receipt token is invalid.", parameterName);
        }

        return normalized;
    }

    private static string NormalizeSignature(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaximumSignatureBase64Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "The provider receipt signature is too long.");
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The provider receipt signature is invalid.", nameof(value), exception);
        }

        try
        {
            if (signature.Length is < 32 or > MaximumSignatureBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The provider receipt signature length is invalid.");
            }

            return Convert.ToBase64String(signature);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }
}

public interface IWriteOnlySecretBroker
{
    ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes without read-back. Implementations must treat Binding.GrantId as
    /// the idempotency key and return the original receipt for a duplicate.
    /// </summary>
    Task<SecretWriteReceipt> WriteAsync(
        SecretWriteBinding binding,
        SecretMaterial material,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifies the provider signature without reading credential material.
    /// A provider must fail closed for an unknown signing key or algorithm.
    /// </summary>
    ValueTask<bool> VerifyReceiptAsync(
        SecretWriteReceipt receipt,
        CancellationToken cancellationToken);
}
