using System.Security.Cryptography;
using System.Text;

namespace YO4X.StrategyGovernance;

/// <summary>
/// Verifies isolated-runner attestations against an explicit P-256 public-key trust store.
/// Runner private keys and host process launching are intentionally outside this component.
/// </summary>
public sealed class EcdsaP256Mql5RunnerAttestationVerifier : IMql5RunnerAttestationVerifier, IDisposable
{
    private readonly Dictionary<string, ECDsa> keys;
    private int disposed;

    public EcdsaP256Mql5RunnerAttestationVerifier(IReadOnlyDictionary<string, byte[]> subjectPublicKeys)
    {
        ArgumentNullException.ThrowIfNull(subjectPublicKeys);
        if (subjectPublicKeys.Count is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(subjectPublicKeys),
                "Between one and 32 trusted isolated-runner keys are required.");
        }

        keys = new Dictionary<string, ECDsa>(StringComparer.Ordinal);
        try
        {
            foreach ((string keyId, byte[] encodedKey) in subjectPublicKeys)
            {
                if (!Mql5CompileValidation.IsSafeToken(keyId)
                    || encodedKey is null
                    || encodedKey.Length is < 64 or > 1024)
                {
                    throw new ArgumentException("An isolated-runner trust key is invalid.", nameof(subjectPublicKeys));
                }

                ECDsa key = ECDsa.Create();
                try
                {
                    key.ImportSubjectPublicKeyInfo(encodedKey, out int bytesRead);
                    ECParameters parameters = key.ExportParameters(false);
                    if (bytesRead != encodedKey.Length
                        || key.KeySize != 256
                        || !string.Equals(
                            parameters.Curve.Oid.Value,
                            "1.2.840.10045.3.1.7",
                            StringComparison.Ordinal))
                    {
                        throw new CryptographicException("Only exact P-256 runner public keys are accepted.");
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
        ReadOnlySpan<byte> signature,
        string canonicalPayload)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!string.Equals(algorithm, Mql5CompileValidation.SignatureAlgorithm, StringComparison.Ordinal)
            || !Mql5CompileValidation.IsSafeToken(signingKeyId)
            || signature.Length is < 64 or > 256
            || canonicalPayload is null
            || !keys.TryGetValue(signingKeyId, out ECDsa? key))
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
        GC.SuppressFinalize(this);
    }
}
