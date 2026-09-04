using System.Security.Cryptography;
using YO4X.ControlPlane.Application;

namespace YO4X.RuntimeControl.Postgres;

public sealed class P256ExecutionLeaseSigningProvider : IExecutionLeaseSigningProvider, IDisposable
{
    public const string Algorithm = "ECDSA_P256_SHA256_DER";
    private const string P256CurveOid = "1.2.840.10045.3.1.7";
    private byte[] privateKeyPkcs8;
    private readonly string keyId;
    private bool disposed;

    public P256ExecutionLeaseSigningProvider(
        string keyId,
        ReadOnlySpan<byte> privateKeyPkcs8)
    {
        if (!IsSafeKeyId(keyId))
            throw new ArgumentException("The execution-lease signing key identifier is invalid.", nameof(keyId));
        if (privateKeyPkcs8.Length is < 100 or > 2048)
            throw new ArgumentException("The execution-lease private key has an invalid size.", nameof(privateKeyPkcs8));

        byte[] keyBytes = privateKeyPkcs8.ToArray();
        try
        {
            using ECDsa signer = ECDsa.Create();
            signer.ImportPkcs8PrivateKey(keyBytes, out int bytesRead);
            ECParameters parameters = signer.ExportParameters(includePrivateParameters: false);
            if (bytesRead != keyBytes.Length
                || signer.KeySize != 256
                || parameters.Curve.Oid.Value != P256CurveOid)
            {
                throw new CryptographicException("The execution-lease key is not an exact P-256 private key.");
            }

            this.keyId = keyId;
            this.privateKeyPkcs8 = keyBytes;
            keyBytes = [];
        }
        catch (CryptographicException exception)
        {
            throw new ArgumentException(
                "The execution-lease key must be an exact P-256 PKCS#8 private key.",
                nameof(privateKeyPkcs8),
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    public ValueTask<ExecutionLeaseSignature> SignAsync(
        ReadOnlyMemory<byte> canonicalLeasePayload,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (canonicalLeasePayload.Length is < 64 or > 64 * 1024)
            throw new ArgumentException("The canonical execution-lease payload size is invalid.");

        byte[] signature;
        using (ECDsa signer = ECDsa.Create())
        {
            signer.ImportPkcs8PrivateKey(privateKeyPkcs8, out int bytesRead);
            if (bytesRead != privateKeyPkcs8.Length)
                throw new CryptographicException("The execution-lease private key was not consumed exactly.");
            signature = signer.SignData(
                canonicalLeasePayload.Span,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
        }

        try
        {
            string encoded = Convert.ToBase64String(signature)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            return ValueTask.FromResult(new ExecutionLeaseSignature(Algorithm, keyId, encoded));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        CryptographicOperations.ZeroMemory(privateKeyPkcs8);
        privateKeyPkcs8 = [];
    }

    private static bool IsSafeKeyId(string? value) =>
        value is { Length: >= 1 and <= 128 }
        && value.All(character => character is (>= 'A' and <= 'Z')
            or (>= 'a' and <= 'z') or (>= '0' and <= '9')
            or '.' or '_' or ':' or '/' or '-');
}
