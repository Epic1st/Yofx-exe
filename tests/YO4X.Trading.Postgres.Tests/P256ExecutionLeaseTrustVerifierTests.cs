using System.Security.Cryptography;
using System.Globalization;
using YO4X.Runtime.Contracts;
using YO4X.Trading.Abstractions;
using YO4X.Trading.Postgres;

namespace YO4X.Trading.Postgres.Tests;

public sealed class P256ExecutionLeaseTrustVerifierTests
{
    [Fact]
    public void ValidDerSignatureIsTrustedAndBindsExactKey()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SignedExecutionLease lease = SignLease(key, "lease-key-1");
        byte[] spki = key.ExportSubjectPublicKeyInfo();
        var verifier = CreateVerifier("lease-key-1", spki);

        ExecutionLeaseTrustVerification result = verifier.Verify(lease);

        Assert.True(result.IsTrusted);
        Assert.Equal(P256ExecutionLeaseTrustVerifier.SignatureAlgorithm, result.SignatureAlgorithm);
        Assert.Equal("lease-key-1", result.SigningKeyId);
        Assert.Equal(Sha256(spki), result.TrustedVerificationKeySha256);
    }

    [Fact]
    public void TamperedClaimsAreRejected()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SignedExecutionLease lease = SignLease(key, "lease-key-1");
        var verifier = CreateVerifier("lease-key-1", key.ExportSubjectPublicKeyInfo());
        SignedExecutionLease tampered = lease with
        {
            Claims = lease.Claims with
            {
                Binding = lease.Claims.Binding with { Generation = 2 }
            }
        };

        ExecutionLeaseTrustVerification result = verifier.Verify(tampered);

        Assert.False(result.IsTrusted);
        Assert.Equal("execution_lease_payload_digest_invalid", result.ReasonCode);
    }

    [Fact]
    public void TamperedDerSignatureIsRejected()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SignedExecutionLease lease = SignLease(key, "lease-key-1");
        var verifier = CreateVerifier("lease-key-1", key.ExportSubjectPublicKeyInfo());
        byte[] signature = DecodeBase64Url(lease.SignatureBase64Url);
        signature[^1] ^= 0x01;
        SignedExecutionLease tampered = lease with { SignatureBase64Url = Base64Url(signature) };

        ExecutionLeaseTrustVerification result = verifier.Verify(tampered);

        Assert.False(result.IsTrusted);
        Assert.Equal("execution_lease_signature_invalid", result.ReasonCode);
    }

    [Fact]
    public void WrongKeyAndWrongAlgorithmAreRejected()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SignedExecutionLease lease = SignLease(key, "lease-key-1");
        var verifier = CreateVerifier("lease-key-1", key.ExportSubjectPublicKeyInfo());

        Assert.False(verifier.Verify(lease with { SigningKeyId = "lease-key-2" }).IsTrusted);
        Assert.False(verifier.Verify(lease with { SignatureAlgorithm = "ES256" }).IsTrusted);
    }

    [Fact]
    public void P1363SignatureIsRejected()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ExecutionLeaseClaims claims = Claims();
        byte[] payload = ExecutionLeaseCanonicalizer.Serialize(claims);
        byte[] p1363 = key.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var lease = new SignedExecutionLease(
            claims,
            Sha256(payload),
            P256ExecutionLeaseTrustVerifier.SignatureAlgorithm,
            "lease-key-1",
            Base64Url(p1363));
        var verifier = CreateVerifier("lease-key-1", key.ExportSubjectPublicKeyInfo());

        ExecutionLeaseTrustVerification result = verifier.Verify(lease);

        Assert.False(result.IsTrusted);
        Assert.Equal("execution_lease_signature_invalid", result.ReasonCode);
    }

    [Fact]
    public void StandardOrPaddedBase64IsRejected()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SignedExecutionLease lease = SignLease(key, "lease-key-1");
        var verifier = CreateVerifier("lease-key-1", key.ExportSubjectPublicKeyInfo());
        string padded = Convert.ToBase64String(DecodeBase64Url(lease.SignatureBase64Url)) + "=";

        ExecutionLeaseTrustVerification result = verifier.Verify(
            lease with { SignatureBase64Url = padded });

        Assert.False(result.IsTrusted);
        Assert.Equal("execution_lease_signature_encoding_invalid", result.ReasonCode);
    }

    [Fact]
    public void MalformedLeaseReturnsRejectedInsteadOfEscaping()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = CreateVerifier("lease-key-1", key.ExportSubjectPublicKeyInfo());
        var malformed = new SignedExecutionLease(
            null!,
            new string('a', 64),
            P256ExecutionLeaseTrustVerifier.SignatureAlgorithm,
            "lease-key-1",
            new string('A', 86));

        ExecutionLeaseTrustVerification result = verifier.Verify(malformed);

        Assert.False(result.IsTrusted);
    }

    [Fact]
    public void P384AndMalformedSpkiTrustKeysAreRejected()
    {
        using ECDsa p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Throws<ArgumentException>(() =>
            CreateVerifier("lease-key-1", p384.ExportSubjectPublicKeyInfo()));
        Assert.Throws<ArgumentException>(() =>
            CreateVerifier("lease-key-1", new byte[64]));
    }

    [Fact]
    public void TrustSetAndKeyIdentifiersAreStrictlyBounded()
    {
        Assert.Throws<ArgumentException>(() =>
            new P256ExecutionLeaseTrustVerifier(
                new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)));

        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] spki = key.ExportSubjectPublicKeyInfo();
        Assert.Throws<ArgumentException>(() => CreateVerifier(" key", spki));
        Assert.Throws<ArgumentException>(() => CreateVerifier(new string('k', 129), spki));

        var tooMany = Enumerable.Range(0, 33).ToDictionary(
            index => $"key-{index}",
            _ => (ReadOnlyMemory<byte>)spki,
            StringComparer.Ordinal);
        Assert.Throws<ArgumentException>(() => new P256ExecutionLeaseTrustVerifier(tooMany));
    }

    [Fact]
    public void P256OidAndSpkiDigestAreRevalidatedOnEveryVerification()
    {
        using ECDsa trustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SignedExecutionLease otherLease = SignLease(otherKey, "lease-key-1");
        var verifier = CreateVerifier("lease-key-1", trustedKey.ExportSubjectPublicKeyInfo());

        ExecutionLeaseTrustVerification result = verifier.Verify(otherLease);

        Assert.False(result.IsTrusted);
        Assert.Equal("execution_lease_signature_invalid", result.ReasonCode);
    }

    private static P256ExecutionLeaseTrustVerifier CreateVerifier(string keyId, byte[] spki) =>
        new(new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
        {
            [keyId] = spki
        });

    private static SignedExecutionLease SignLease(ECDsa key, string keyId)
    {
        ExecutionLeaseClaims claims = Claims();
        byte[] payload = ExecutionLeaseCanonicalizer.Serialize(claims);
        byte[] signature = key.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        return new SignedExecutionLease(
            claims,
            Sha256(payload),
            P256ExecutionLeaseTrustVerifier.SignatureAlgorithm,
            keyId,
            Base64Url(signature));
    }

    private static ExecutionLeaseClaims Claims()
    {
        DateTimeOffset now = DateTimeOffset.Parse(
            "2026-08-22T12:00:00Z",
            CultureInfo.InvariantCulture);
        return new ExecutionLeaseClaims(
            RuntimeContractVersions.ExecutionLeaseV1,
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            new ExecutionLeaseBinding(
                Guid.Parse("20000000-0000-0000-0000-000000000001"),
                Guid.Parse("30000000-0000-0000-0000-000000000001"),
                Guid.Parse("40000000-0000-0000-0000-000000000001"),
                Guid.Parse("50000000-0000-0000-0000-000000000001"),
                Guid.Parse("60000000-0000-0000-0000-000000000001"),
                new string('a', 64),
                Guid.Parse("70000000-0000-0000-0000-000000000001"),
                Guid.Parse("80000000-0000-0000-0000-000000000001"),
                1,
                new string('b', 64),
                ExecutionMode.CloudDemo,
                Guid.Parse("90000000-0000-0000-0000-000000000001"),
                new string('c', 64),
                Guid.Parse("a0000000-0000-0000-0000-000000000001"),
                Guid.Parse("b0000000-0000-0000-0000-000000000001"),
                Guid.Parse("c0000000-0000-0000-0000-000000000001"),
                Guid.Parse("d0000000-0000-0000-0000-000000000001"),
                Guid.Parse("e0000000-0000-0000-0000-000000000001"),
                1,
                "region-1"),
            now,
            now,
            now.AddMinutes(5),
            now.AddMinutes(10),
            new ExecutionLeaseActionPolicy(
                LeaseActionClass.Increase | LeaseActionClass.Reduce,
                LeaseActionClass.Reduce,
                LeaseActionClass.None,
                LeaseActionClass.None));
    }

    private static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
