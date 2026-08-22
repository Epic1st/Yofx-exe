using System.Security.Cryptography;
using System.Text;

namespace YO4X.ControlPlane.Workers.Operations;

internal sealed class WorkerPolicySignatureTrustStore : IDisposable
{
    private const string SupportedAlgorithm = "ECDSA_P256_SHA256_DER";
    private readonly Dictionary<string, ECDsa> keys;
    private int disposed;

    public WorkerPolicySignatureTrustStore(IReadOnlyDictionary<string, byte[]> subjectPublicKeys)
    {
        ArgumentNullException.ThrowIfNull(subjectPublicKeys);
        if (subjectPublicKeys.Count is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(subjectPublicKeys));
        }

        keys = new Dictionary<string, ECDsa>(StringComparer.Ordinal);
        try
        {
            foreach ((string keyId, byte[] encodedKey) in subjectPublicKeys)
            {
                if (!IsValidKeyId(keyId) || encodedKey.Length is < 64 or > 1024)
                {
                    throw new ArgumentException("A policy trust key is invalid.", nameof(subjectPublicKeys));
                }

                ECDsa key = ECDsa.Create();
                try
                {
                    key.ImportSubjectPublicKeyInfo(encodedKey, out int bytesRead);
                    ECParameters parameters = key.ExportParameters(false);
                    if (bytesRead != encodedKey.Length
                        || key.KeySize != 256
                        || parameters.Curve.Oid.Value != "1.2.840.10045.3.1.7")
                    {
                        throw new CryptographicException("Only exact P-256 public keys are accepted.");
                    }

                    keys.Add(keyId, key);
                    key = null!;
                }
                finally
                {
                    key?.Dispose();
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (algorithm != SupportedAlgorithm
            || !IsValidKeyId(signingKeyId)
            || signature.Length is < 64 or > 256
            || !keys.TryGetValue(signingKeyId, out ECDsa? key))
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

        byte[] actualHash = SHA256.HashData(signature);
        bool hashMatches = expectedHash.Length == 32
            && CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        CryptographicOperations.ZeroMemory(expectedHash);
        CryptographicOperations.ZeroMemory(actualHash);
        if (!hashMatches)
        {
            return false;
        }

        byte[] payload = Encoding.UTF8.GetBytes(canonicalPayload);
        try
        {
            return key.VerifyData(
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        foreach (ECDsa key in keys.Values)
        {
            key.Dispose();
        }

        keys.Clear();
    }

    private static bool IsValidKeyId(string? value) => value is { Length: >= 1 and <= 200 }
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.' or ':' or '/');
}
