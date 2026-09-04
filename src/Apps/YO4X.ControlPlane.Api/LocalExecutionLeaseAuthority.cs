using System.Security.Cryptography;
using YO4X.Runtime.Contracts;
using YO4X.Trading.Abstractions;

namespace YO4X.ControlPlane.Api;

/// <summary>A process-local P-256 authority for development execution leases.</summary>
internal sealed class LocalExecutionLeaseAuthority : IExecutionLeaseTrustVerifier, IDisposable
{
    internal const string KeyId = "local-development-execution-lease-v1";
    internal const string Algorithm = "ECDSA_P256_SHA256_DER";
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly object gate = new();

    internal SignedExecutionLease Issue(
        ExecutionLeaseBinding binding,
        LeaseActionClass actions,
        DateTimeOffset now)
    {
        DateTimeOffset issued = now.ToUniversalTime();
        var claims = new ExecutionLeaseClaims(
            RuntimeContractVersions.ExecutionLeaseV1,
            Guid.CreateVersion7(),
            binding,
            issued,
            issued,
            issued.AddMinutes(10),
            issued.AddMinutes(10),
            new ExecutionLeaseActionPolicy(
                actions,
                LeaseActionClass.None,
                LeaseActionClass.None,
                LeaseActionClass.None));
        byte[] payload = ExecutionLeaseCanonicalizer.Serialize(claims);
        byte[] signature;
        lock (gate)
        {
            signature = key.SignData(
                payload,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
        }
        try
        {
            return new SignedExecutionLease(
                claims,
                Convert.ToHexStringLower(SHA256.HashData(payload)),
                Algorithm,
                KeyId,
                Base64Url(signature));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    public ExecutionLeaseTrustVerification Verify(SignedExecutionLease lease)
    {
        if (lease is null
            || lease.SignatureAlgorithm != Algorithm
            || lease.SigningKeyId != KeyId)
            return Rejected();

        byte[] payload = ExecutionLeaseCanonicalizer.Serialize(lease.Claims);
        byte[] signature = [];
        try
        {
            string digest = Convert.ToHexStringLower(SHA256.HashData(payload));
            if (!FixedTimeEquals(digest, lease.PayloadSha256))
                return Rejected();
            signature = DecodeBase64Url(lease.SignatureBase64Url);
            bool valid;
            lock (gate)
            {
                valid = key.VerifyData(
                    payload,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence);
            }
            return valid
                ? new ExecutionLeaseTrustVerification(
                    true,
                    "execution_lease_signature_trusted",
                    Algorithm,
                    KeyId,
                    Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo())))
                : Rejected();
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
        {
            return Rejected();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    public void Dispose() => key.Dispose();

    private static ExecutionLeaseTrustVerification Rejected() =>
        new(false, "execution_lease_signature_invalid", null, null, null);

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            0 => "",
            2 => "==",
            3 => "=",
            _ => throw new FormatException()
        };
        return Convert.FromBase64String(padded);
    }

    private static bool FixedTimeEquals(string left, string right) =>
        left.Length == right.Length
        && CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(left),
            System.Text.Encoding.ASCII.GetBytes(right));
}
