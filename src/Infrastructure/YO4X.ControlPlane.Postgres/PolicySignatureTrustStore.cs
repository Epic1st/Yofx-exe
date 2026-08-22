using System.Security.Cryptography;
using System.Text;

namespace YO4X.ControlPlane.Postgres;

/// <summary>
/// Runtime-only public-key trust store for immutable policy signatures. Private
/// signing material is deliberately outside the control plane process.
/// </summary>
public sealed class PolicySignatureTrustStore : IDisposable
{
    public const string EcdsaP256Sha256Der = "ECDSA_P256_SHA256_DER";

    private readonly Dictionary<string, byte[]> keys;
    private readonly ReaderWriterLockSlim lifecycleLock = new(LockRecursionPolicy.NoRecursion);
    private int disposed;

    public PolicySignatureTrustStore(IReadOnlyDictionary<string, byte[]> subjectPublicKeys)
    {
        ArgumentNullException.ThrowIfNull(subjectPublicKeys);
        if (subjectPublicKeys.Count is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(subjectPublicKeys),
                "Between one and 32 trusted policy keys are required.");
        }

        keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            foreach ((string keyId, byte[] encodedKey) in subjectPublicKeys)
            {
                if (!IsValidKeyId(keyId) || encodedKey is null || encodedKey.Length is < 64 or > 1024)
                {
                    throw new ArgumentException("A policy trust key is invalid.", nameof(subjectPublicKeys));
                }

                byte[]? ownedKey = encodedKey.ToArray();
                try
                {
                    ValidatePublicKey(ownedKey);
                    keys.Add(keyId, ownedKey);
                    ownedKey = null;
                }
                finally
                {
                    if (ownedKey is not null)
                    {
                        CryptographicOperations.ZeroMemory(ownedKey);
                    }
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool Verify(
        string signingKeyId,
        string algorithm,
        byte[] signature,
        string signatureSha256,
        string canonicalPayload)
    {
        lifecycleLock.EnterReadLock();
        try
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            if (!string.Equals(algorithm, EcdsaP256Sha256Der, StringComparison.Ordinal)
                || !IsValidKeyId(signingKeyId)
                || signature is null
                || signature.Length is < 64 or > 256
                || signatureSha256 is not { Length: 64 }
                || canonicalPayload is null
                || !keys.TryGetValue(signingKeyId, out byte[]? encodedKey))
            {
                return false;
            }

            byte[] expectedHash;
            try
            {
                expectedHash = Convert.FromHexString(signatureSha256);
            }
            catch (FormatException)
            {
                return false;
            }

            if (expectedHash.Length != 32)
            {
                CryptographicOperations.ZeroMemory(expectedHash);
                return false;
            }

            byte[] actualHash = SHA256.HashData(signature);
            bool hashMatches = CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
            CryptographicOperations.ZeroMemory(expectedHash);
            CryptographicOperations.ZeroMemory(actualHash);
            if (!hashMatches)
            {
                return false;
            }

            byte[] payload = Encoding.UTF8.GetBytes(canonicalPayload);
            try
            {
                using ECDsa verifier = ECDsa.Create();
                verifier.ImportSubjectPublicKeyInfo(encodedKey, out int bytesRead);
                return bytesRead == encodedKey.Length
                    && verifier.VerifyData(
                        payload,
                        signature,
                        HashAlgorithmName.SHA256,
                        DSASignatureFormat.Rfc3279DerSequence);
            }
            catch (CryptographicException)
            {
                return false;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
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
            if (disposed == 0)
            {
                disposed = 1;
                foreach (byte[] encodedKey in keys.Values)
                {
                    CryptographicOperations.ZeroMemory(encodedKey);
                }

                keys.Clear();
            }
        }
        finally
        {
            lifecycleLock.ExitWriteLock();
        }

        GC.SuppressFinalize(this);
    }

    private static void ValidatePublicKey(byte[] encodedKey)
    {
        using ECDsa key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(encodedKey, out int bytesRead);
        ECParameters parameters = key.ExportParameters(false);
        if (bytesRead != encodedKey.Length
            || key.KeySize != 256
            || !string.Equals(
                parameters.Curve.Oid.Value,
                "1.2.840.10045.3.1.7",
                StringComparison.Ordinal))
        {
            throw new CryptographicException("Only exact P-256 public keys are accepted.");
        }
    }

    private static bool IsValidKeyId(string? value) => value is { Length: >= 1 and <= 200 }
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.' or ':' or '/');

    public override string ToString() => "[POLICY PUBLIC-KEY TRUST STORE]";
}
