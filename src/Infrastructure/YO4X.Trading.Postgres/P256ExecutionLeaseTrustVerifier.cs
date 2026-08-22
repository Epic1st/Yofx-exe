using System.Security.Cryptography;
using System.Text;
using YO4X.Runtime.Contracts;
using YO4X.Trading.Abstractions;

namespace YO4X.Trading.Postgres;

public sealed class P256ExecutionLeaseTrustVerifier : IExecutionLeaseTrustVerifier
{
    public const string SignatureAlgorithm = "ECDSA_P256_SHA256_DER";

    private const string P256CurveOid = "1.2.840.10045.3.1.7";
    private readonly Dictionary<string, TrustedKey> trustedKeys;

    public P256ExecutionLeaseTrustVerifier(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> trustedSubjectPublicKeys)
    {
        ArgumentNullException.ThrowIfNull(trustedSubjectPublicKeys);
        if (trustedSubjectPublicKeys.Count is < 1 or > 32)
        {
            throw new ArgumentException(
                "The execution-lease trust set must contain between one and 32 keys.",
                nameof(trustedSubjectPublicKeys));
        }

        var keys = new Dictionary<string, TrustedKey>(StringComparer.Ordinal);
        foreach ((string keyId, ReadOnlyMemory<byte> subjectPublicKeyInfo) in
                 trustedSubjectPublicKeys)
        {
            if (!IsSafeKeyId(keyId)
                || subjectPublicKeyInfo.Length is < 64 or > 1024)
            {
                throw new ArgumentException(
                    "Each execution-lease trust key requires a bounded identifier and SPKI bytes.",
                    nameof(trustedSubjectPublicKeys));
            }

            byte[] keyBytes = subjectPublicKeyInfo.ToArray();
            try
            {
                using ECDsa verifier = ECDsa.Create();
                verifier.ImportSubjectPublicKeyInfo(keyBytes, out int bytesRead);
                ECParameters parameters = verifier.ExportParameters(false);
                if (bytesRead != keyBytes.Length
                    || verifier.KeySize != 256
                    || parameters.Curve.Oid.Value != P256CurveOid)
                {
                    throw new CryptographicException("The trust key is not an exact P-256 SPKI key.");
                }

                keys.Add(
                    keyId,
                    new TrustedKey(
                        keyBytes,
                        Convert.ToHexString(SHA256.HashData(keyBytes)).ToLowerInvariant()));
                keyBytes = [];
            }
            catch (CryptographicException exception)
            {
                throw new ArgumentException(
                    "Each execution-lease trust key must be an exact P-256 SPKI key.",
                    nameof(trustedSubjectPublicKeys),
                    exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keyBytes);
            }
        }

        trustedKeys = keys;
    }

    public ExecutionLeaseTrustVerification Verify(SignedExecutionLease lease)
    {
        if (lease is null
            || lease.Claims is null
            || lease.Claims.Binding is null
            || lease.Claims.ActionPolicy is null
            || !IsLowerSha256(lease.PayloadSha256)
            || lease.SignatureAlgorithm != SignatureAlgorithm
            || !IsSafeKeyId(lease.SigningKeyId)
            || !trustedKeys.TryGetValue(lease.SigningKeyId, out TrustedKey? trustedKey))
        {
            return Rejected("execution_lease_signing_key_untrusted");
        }

        byte[] payload = ExecutionLeaseCanonicalizer.Serialize(lease.Claims);
        byte[] signature = [];
        try
        {
            string payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
            if (!FixedTimeEquals(payloadSha256, lease.PayloadSha256))
            {
                return Rejected("execution_lease_payload_digest_invalid");
            }

            signature = DecodeBase64Url(lease.SignatureBase64Url);
            if (signature.Length is < 64 or > 80)
            {
                return Rejected("execution_lease_signature_encoding_invalid");
            }

            using ECDsa verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(trustedKey.SubjectPublicKeyInfo, out int bytesRead);
            ECParameters parameters = verifier.ExportParameters(false);
            if (bytesRead != trustedKey.SubjectPublicKeyInfo.Length
                || verifier.KeySize != 256
                || parameters.Curve.Oid.Value != P256CurveOid
                || !verifier.VerifyData(
                    payload,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence))
            {
                return Rejected("execution_lease_signature_invalid");
            }

            return new ExecutionLeaseTrustVerification(
                true,
                "execution_lease_signature_trusted",
                SignatureAlgorithm,
                lease.SigningKeyId,
                trustedKey.Sha256);
        }
        catch (FormatException)
        {
            return Rejected("execution_lease_signature_encoding_invalid");
        }
        catch (CryptographicException)
        {
            return Rejected("execution_lease_signature_invalid");
        }
        catch (ArgumentException)
        {
            return Rejected("execution_lease_claims_invalid");
        }
        catch (InvalidOperationException)
        {
            return Rejected("execution_lease_claims_invalid");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length is < 86 or > 108
            || value.Any(character => character is not (>= 'A' and <= 'Z')
                and not (>= 'a' and <= 'z')
                and not (>= '0' and <= '9')
                and not '-'
                and not '_'))
        {
            throw new FormatException("The signature is not strict Base64Url.");
        }

        string padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            0 => padded,
            2 => padded + "==",
            3 => padded + "=",
            _ => throw new FormatException("The signature is not Base64Url encoded.")
        };
        return Convert.FromBase64String(padded);
    }

    private static bool IsSafeKeyId(string? value) =>
        value is { Length: >= 1 and <= 128 }
        && value.All(character => character is (>= 'A' and <= 'Z')
            or (>= 'a' and <= 'z')
            or (>= '0' and <= '9')
            or '.' or '_' or ':' or '/' or '-');

    private static bool IsLowerSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static bool FixedTimeEquals(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right));
    }

    private static ExecutionLeaseTrustVerification Rejected(string reasonCode) =>
        new(false, reasonCode, null, null, null);

    private sealed record TrustedKey(byte[] SubjectPublicKeyInfo, string Sha256);
}
