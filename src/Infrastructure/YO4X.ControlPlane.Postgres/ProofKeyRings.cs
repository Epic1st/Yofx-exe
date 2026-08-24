using System.Security.Cryptography;

namespace YO4X.ControlPlane.Postgres;

internal sealed class ProofKeyRing : IDisposable
{
    private readonly ReaderWriterLockSlim lifecycleLock =
        new(LockRecursionPolicy.NoRecursion);
    private readonly TimeProvider timeProvider;
    private KeyEntry[]? keys;

    public ProofKeyRing(
        byte[] currentKeyBytes,
        byte[]? previousKeyBytes,
        DateTimeOffset? previousRetainUntil,
        TimeProvider? timeProvider = null)
    {
        ValidateKey(currentKeyBytes, nameof(currentKeyBytes));
        if ((previousKeyBytes is null) != (previousRetainUntil is null))
        {
            throw new ArgumentException(
                "A previous proof key and its retention deadline must be configured together.");
        }

        if (previousKeyBytes is not null)
        {
            ValidateKey(previousKeyBytes, nameof(previousKeyBytes));
            if (CryptographicOperations.FixedTimeEquals(currentKeyBytes, previousKeyBytes))
            {
                throw new ArgumentException(
                    "The current and previous proof keys must be different.",
                    nameof(previousKeyBytes));
            }
        }

        this.timeProvider = timeProvider ?? TimeProvider.System;
        var entries = new List<KeyEntry>(2);
        try
        {
            entries.Add(CreateEntry(currentKeyBytes, retainUntil: null));
            if (previousKeyBytes is not null)
            {
                entries.Add(CreateEntry(
                    previousKeyBytes,
                    previousRetainUntil!.Value.ToUniversalTime()));
            }

            keys = entries.ToArray();
        }
        catch
        {
            foreach (KeyEntry entry in entries)
            {
                CryptographicOperations.ZeroMemory(entry.Material);
            }

            throw;
        }
    }

    public string CurrentKeyId
    {
        get
        {
            lifecycleLock.EnterReadLock();
            try
            {
                return GetActiveKeys()[0].Id;
            }
            finally
            {
                lifecycleLock.ExitReadLock();
            }
        }
    }

    public bool IsReady
    {
        get
        {
            lifecycleLock.EnterReadLock();
            try
            {
                KeyEntry[] configuredKeys = GetActiveKeys();
                DateTimeOffset now = timeProvider.GetUtcNow();
                return configuredKeys.All(entry =>
                    entry.RetainUntil is not { } retainUntil
                    || now < retainUntil);
            }
            finally
            {
                lifecycleLock.ExitReadLock();
            }
        }
    }

    public bool TryComputeSha256(
        string? keyId,
        ReadOnlySpan<byte> input,
        Span<byte> destination)
    {
        lifecycleLock.EnterReadLock();
        try
        {
            KeyEntry[] activeKeys = GetActiveKeys();
            foreach (KeyEntry entry in activeKeys)
            {
                if (!string.Equals(entry.Id, keyId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.RetainUntil is { } retainUntil
                    && timeProvider.GetUtcNow() >= retainUntil)
                {
                    return false;
                }

                HMACSHA256.HashData(entry.Material, input, destination);
                return true;
            }

            return false;
        }
        finally
        {
            lifecycleLock.ExitReadLock();
        }
    }

    public void Dispose()
    {
        lifecycleLock.EnterWriteLock();
        try
        {
            KeyEntry[]? owned = Interlocked.Exchange(ref keys, null);
            if (owned is not null)
            {
                foreach (KeyEntry entry in owned)
                {
                    CryptographicOperations.ZeroMemory(entry.Material);
                }
            }
        }
        finally
        {
            lifecycleLock.ExitWriteLock();
        }

        GC.SuppressFinalize(this);
    }

    private KeyEntry[] GetActiveKeys() =>
        keys ?? throw new ObjectDisposedException(nameof(ProofKeyRing));

    private static KeyEntry CreateEntry(byte[] material, DateTimeOffset? retainUntil)
    {
        byte[] ownedMaterial = material.ToArray();
        byte[]? idDigest = null;
        try
        {
            idDigest = SHA256.HashData(ownedMaterial);
            return new KeyEntry(
                Convert.ToHexString(idDigest).ToLowerInvariant(),
                ownedMaterial,
                retainUntil);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(ownedMaterial);
            throw;
        }
        finally
        {
            if (idDigest is not null)
            {
                CryptographicOperations.ZeroMemory(idDigest);
            }
        }
    }

    private static void ValidateKey(byte[]? keyBytes, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(keyBytes, parameterName);
        if (keyBytes.Length != 32 || keyBytes.All(static value => value == 0))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "A proof key must contain exactly 256 bits and cannot be all-zero.");
        }
    }

    private sealed record KeyEntry(
        string Id,
        byte[] Material,
        DateTimeOffset? RetainUntil);
}

public sealed class CredentialProofKeyRing : IDisposable
{
    private readonly ProofKeyRing ring;

    public CredentialProofKeyRing(
        byte[] currentKeyBytes,
        byte[]? previousKeyBytes = null,
        DateTimeOffset? previousRetainUntil = null,
        TimeProvider? timeProvider = null)
    {
        ring = new ProofKeyRing(
            currentKeyBytes,
            previousKeyBytes,
            previousRetainUntil,
            timeProvider);
    }

    public string CurrentKeyId => ring.CurrentKeyId;

    /// <summary>
    /// Indicates whether every configured key slot remains usable. This
    /// exposes no key material and becomes false at the previous slot's
    /// exclusive retirement boundary.
    /// </summary>
    public bool IsReady => ring.IsReady;

    internal bool TryComputeSha256(
        string? keyId,
        ReadOnlySpan<byte> input,
        Span<byte> destination) =>
        ring.TryComputeSha256(keyId, input, destination);

    public void Dispose() => ring.Dispose();

    public override string ToString() => "[REDACTED CREDENTIAL PROOF KEY RING]";
}

public sealed class StrategyImportProofKeyRing : IDisposable
{
    private readonly ProofKeyRing ring;

    public StrategyImportProofKeyRing(
        byte[] currentKeyBytes,
        byte[]? previousKeyBytes = null,
        DateTimeOffset? previousRetainUntil = null,
        TimeProvider? timeProvider = null)
    {
        ring = new ProofKeyRing(
            currentKeyBytes,
            previousKeyBytes,
            previousRetainUntil,
            timeProvider);
    }

    public string CurrentKeyId => ring.CurrentKeyId;

    /// <summary>
    /// Indicates whether every configured key slot remains usable. This
    /// exposes no key material and becomes false at the previous slot's
    /// exclusive retirement boundary.
    /// </summary>
    public bool IsReady => ring.IsReady;

    internal bool TryComputeSha256(
        string? keyId,
        ReadOnlySpan<byte> input,
        Span<byte> destination) =>
        ring.TryComputeSha256(keyId, input, destination);

    public void Dispose() => ring.Dispose();

    public override string ToString() => "[REDACTED STRATEGY IMPORT PROOF KEY RING]";
}
