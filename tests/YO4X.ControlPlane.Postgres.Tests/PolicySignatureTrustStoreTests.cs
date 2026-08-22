using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using YO4X.ControlPlane.Postgres;

namespace YO4X.ControlPlane.Postgres.Tests;

public sealed class PolicySignatureTrustStoreTests
{
    private const string TrustedKeyId = "policy-root-2026-01";

    [Fact]
    public void AcceptsAValidP256DerSignatureFromTheConfiguredKey()
    {
        using SignatureFixture fixture = new();
        const string payload = "{\"contract\":\"yo4x.test.v1\",\"value\":1}";
        byte[] signature = fixture.SignTrusted(payload);

        Assert.True(fixture.Store.Verify(
            TrustedKeyId,
            PolicySignatureTrustStore.EcdsaP256Sha256Der,
            signature,
            Sha256(signature),
            payload));
    }

    [Fact]
    public async Task AcceptsValidSignaturesAcrossParallelVerificationCalls()
    {
        using SignatureFixture fixture = new();
        string payload = $"{{\"contract\":\"yo4x.parallel.v1\",\"value\":\"{new string('p', 4096)}\"}}";
        byte[] signature = fixture.SignTrusted(payload);
        string signatureHash = Sha256(signature);

        Task<bool>[] verificationTasks = Enumerable.Range(0, 128)
            .Select(_ => Task.Run(() => fixture.Store.Verify(
                TrustedKeyId,
                PolicySignatureTrustStore.EcdsaP256Sha256Der,
                signature,
                signatureHash,
                payload)))
            .ToArray();

        bool[] results = await Task.WhenAll(verificationTasks);

        Assert.All(results, Assert.True);
    }

    [Fact]
    public async Task DisposeCanRaceParallelVerificationWithoutReturningCorruptResults()
    {
        using SignatureFixture fixture = new();
        string payload = $"{{\"contract\":\"yo4x.dispose-race.v1\",\"value\":\"{new string('r', 4096)}\"}}";
        byte[] signature = fixture.SignTrusted(payload);
        string signatureHash = Sha256(signature);
        int successfulVerifications = 0;
        using ManualResetEventSlim start = new(false);
        int workerCount = Math.Clamp(Environment.ProcessorCount * 2, 4, 16);

        Task[] verificationTasks = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                for (int attempt = 0; attempt < 128; attempt++)
                {
                    try
                    {
                        if (!fixture.Store.Verify(
                                TrustedKeyId,
                                PolicySignatureTrustStore.EcdsaP256Sha256Der,
                                signature,
                                signatureHash,
                                payload))
                        {
                            throw new InvalidOperationException(
                                "A valid signature was rejected before disposal completed.");
                        }

                        Interlocked.Increment(ref successfulVerifications);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                }
            }))
            .ToArray();

        Task disposeTask = Task.Factory.StartNew(
            () =>
            {
                start.Wait();
                if (!SpinWait.SpinUntil(
                        () => Volatile.Read(ref successfulVerifications) > 0,
                        TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("No verification completed before the disposal race.");
                }

                fixture.Store.Dispose();
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        start.Set();
        await Task.WhenAll(verificationTasks.Append(disposeTask));

        Assert.True(Volatile.Read(ref successfulVerifications) > 0);
        Assert.Throws<ObjectDisposedException>(() => fixture.Store.Verify(
            TrustedKeyId,
            PolicySignatureTrustStore.EcdsaP256Sha256Der,
            signature,
            signatureHash,
            payload));

        // Disposal is deliberately idempotent for nested service lifetimes.
        fixture.Store.Dispose();
    }

    [Fact]
    public void OwnsAndZeroesValidatedPublicKeyEncodingOnDispose()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] suppliedKey = signer.ExportSubjectPublicKeyInfo();
        using PolicySignatureTrustStore store = new(
            new Dictionary<string, byte[]> { [TrustedKeyId] = suppliedKey });
        byte[] ownedKey = GetOwnedKeyEncoding(store);
        const string payload = "{\"contract\":\"yo4x.key-lifecycle.v1\"}";
        byte[] signature = signer.SignData(
            Encoding.UTF8.GetBytes(payload),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);

        Assert.NotSame(suppliedKey, ownedKey);
        suppliedKey.AsSpan().Fill(0xA5);
        Assert.True(store.Verify(
            TrustedKeyId,
            PolicySignatureTrustStore.EcdsaP256Sha256Der,
            signature,
            Sha256(signature),
            payload));

        store.Dispose();

        Assert.All(ownedKey, value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => store.Verify(
            TrustedKeyId,
            PolicySignatureTrustStore.EcdsaP256Sha256Der,
            signature,
            Sha256(signature),
            payload));
    }

    [Fact]
    public void RejectsASignatureProducedByAnUntrustedKey()
    {
        using SignatureFixture fixture = new();
        const string payload = "{\"contract\":\"yo4x.test.v1\",\"value\":1}";
        byte[] signature = fixture.SignUntrusted(payload);

        Assert.False(fixture.Store.Verify(
            TrustedKeyId,
            PolicySignatureTrustStore.EcdsaP256Sha256Der,
            signature,
            Sha256(signature),
            payload));
    }

    [Fact]
    public void RejectsAValidSignatureReplayedAgainstAnotherPayload()
    {
        using SignatureFixture fixture = new();
        const string signedPayload = "{\"contract\":\"yo4x.test.v1\",\"value\":1}";
        byte[] signature = fixture.SignTrusted(signedPayload);

        Assert.False(fixture.Store.Verify(
            TrustedKeyId,
            PolicySignatureTrustStore.EcdsaP256Sha256Der,
            signature,
            Sha256(signature),
            "{\"contract\":\"yo4x.test.v1\",\"value\":2}"));
    }

    [Fact]
    public void RejectsASignatureWhosePersistedHashDoesNotMatch()
    {
        using SignatureFixture fixture = new();
        const string payload = "{\"contract\":\"yo4x.test.v1\",\"value\":1}";
        byte[] signature = fixture.SignTrusted(payload);

        Assert.False(fixture.Store.Verify(
            TrustedKeyId,
            PolicySignatureTrustStore.EcdsaP256Sha256Der,
            signature,
            new string('0', 64),
            payload));
    }

    [Theory]
    [InlineData("ecdsa_p256_sha256_der")]
    [InlineData("ECDSA_P256_SHA256_P1363")]
    [InlineData("RSA_PSS_SHA256")]
    public void RejectsEveryNonAllowlistedSignatureAlgorithm(string algorithm)
    {
        using SignatureFixture fixture = new();
        const string payload = "{\"contract\":\"yo4x.test.v1\",\"value\":1}";
        byte[] signature = fixture.SignTrusted(payload);

        Assert.False(fixture.Store.Verify(
            TrustedKeyId,
            algorithm,
            signature,
            Sha256(signature),
            payload));
    }

    [Fact]
    public void RejectsA256BitCurveThatIsNotNistP256()
    {
        // A valid secp256k1 SubjectPublicKeyInfo containing the standard
        // generator point. Platforms that cannot import this curve reject it
        // at import; platforms that can must still reject it by curve identity.
        byte[] secp256k1SubjectPublicKeyInfo = Convert.FromHexString(
            "3056301006072a8648ce3d020106052b8104000a03420004" +
            "79be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798" +
            "483ada7726a3c4655da4fbfc0e1108a8fd17b448a68554199c47d08ffb10d4b8");

        Exception? exception = Record.Exception(() => new PolicySignatureTrustStore(
            new Dictionary<string, byte[]> { [TrustedKeyId] = secp256k1SubjectPublicKeyInfo }));
        Assert.NotNull(exception);
        Assert.True(
            exception is CryptographicException or NotSupportedException,
            $"Unexpected non-P256 rejection type: {exception.GetType().FullName}");
    }

    [Fact]
    public void RiskPolicySignaturePayloadBindsEveryAuthoritativeDimension()
    {
        Guid tenantId = Guid.Parse("019c7e8f-8eb8-76b8-be93-40025f2072d5");
        Guid versionId = Guid.Parse("019c7e8f-9e1a-7631-8872-e03b2648dfcb");
        Guid policyId = Guid.Parse("019c7e8f-ab53-7151-ab2c-99a24d5a1a4e");
        const int version = 7;
        string digest = new('a', 64);
        string baseline = CreateRiskPayload(tenantId, versionId, policyId, version, digest);

        using SignatureFixture fixture = new();
        byte[] signature = fixture.SignTrusted(baseline);
        string signatureHash = Sha256(signature);
        string[] mutations =
        [
            CreateRiskPayload(Guid.NewGuid(), versionId, policyId, version, digest),
            CreateRiskPayload(tenantId, Guid.NewGuid(), policyId, version, digest),
            CreateRiskPayload(tenantId, versionId, Guid.NewGuid(), version, digest),
            CreateRiskPayload(tenantId, versionId, policyId, version + 1, digest),
            CreateRiskPayload(tenantId, versionId, policyId, version, new string('b', 64))
        ];

        Assert.All(mutations, changedPayload =>
        {
            Assert.NotEqual(baseline, changedPayload);
            Assert.False(fixture.Store.Verify(
                TrustedKeyId,
                PolicySignatureTrustStore.EcdsaP256Sha256Der,
                signature,
                signatureHash,
                changedPayload));
        });
    }

    [Fact]
    public void ExecutionPolicySignaturePayloadBindsEveryAuthoritativeDimension()
    {
        Guid tenantId = Guid.Parse("019c7e8f-ba28-7f3e-8952-0274625bb68a");
        Guid policyId = Guid.Parse("019c7e8f-c97b-7af5-8691-ce69c456a89f");
        Guid incidentId = Guid.Parse("019c7e8f-df14-7d09-95f0-7f45f1a67949");
        Guid ownerId = Guid.Parse("019c7e8f-eda5-7772-a4bc-fe3a6d75295b");
        DateTimeOffset authorityExpiresAt = new(2026, 8, 22, 13, 0, 0, TimeSpan.Zero);
        DateTimeOffset reviewDeadline = new(2026, 8, 22, 14, 0, 0, TimeSpan.Zero);
        string digest = new('c', 64);
        object?[] values =
        [
            tenantId, policyId, 11L, "deployment", "deployment-1", digest,
            "incident containment", incidentId, ownerId, authorityExpiresAt, reviewDeadline
        ];
        string baseline = CreateExecutionPayload(values);

        using SignatureFixture fixture = new();
        byte[] signature = fixture.SignTrusted(baseline);
        string signatureHash = Sha256(signature);
        for (int index = 0; index < values.Length; index++)
        {
            object?[] changed = values.ToArray();
            changed[index] = index switch
            {
                0 or 1 or 7 or 8 => Guid.NewGuid(),
                2 => 12L,
                3 => "account",
                4 => "deployment-2",
                5 => new string('d', 64),
                6 => "different reason",
                9 => authorityExpiresAt.AddMinutes(1),
                10 => reviewDeadline.AddMinutes(1),
                _ => throw new InvalidOperationException()
            };
            string changedPayload = CreateExecutionPayload(changed);

            Assert.NotEqual(baseline, changedPayload);
            Assert.False(fixture.Store.Verify(
                TrustedKeyId,
                PolicySignatureTrustStore.EcdsaP256Sha256Der,
                signature,
                signatureHash,
                changedPayload));
        }
    }

    private static string CreateRiskPayload(
        Guid tenantId,
        Guid versionId,
        Guid policyId,
        int version,
        string digest) => InvokePayloadFactory(
            "CreateRiskPolicySignaturePayload",
            tenantId,
            versionId,
            policyId,
            version,
            digest);

    private static string CreateExecutionPayload(object?[] arguments) => InvokePayloadFactory(
        "CreateExecutionSafetyPolicySignaturePayload",
        arguments);

    private static string InvokePayloadFactory(string methodName, params object?[] arguments)
    {
        MethodInfo? method = typeof(PostgresControlPlaneApplication).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, arguments));
    }

    private static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static byte[] GetOwnedKeyEncoding(PolicySignatureTrustStore store)
    {
        FieldInfo? keyField = typeof(PolicySignatureTrustStore).GetField(
            "keys",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(keyField);
        Dictionary<string, byte[]> keys = Assert.IsType<Dictionary<string, byte[]>>(keyField.GetValue(store));
        return keys[TrustedKeyId];
    }

    private sealed class SignatureFixture : IDisposable
    {
        private readonly ECDsa trustedSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa untrustedSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public SignatureFixture()
        {
            Store = new PolicySignatureTrustStore(
                new Dictionary<string, byte[]>
                {
                    [TrustedKeyId] = trustedSigner.ExportSubjectPublicKeyInfo()
                });
        }

        public PolicySignatureTrustStore Store { get; }

        public byte[] SignTrusted(string payload) => Sign(trustedSigner, payload);

        public byte[] SignUntrusted(string payload) => Sign(untrustedSigner, payload);

        public void Dispose()
        {
            Store.Dispose();
            trustedSigner.Dispose();
            untrustedSigner.Dispose();
        }

        private static byte[] Sign(ECDsa signer, string payload) => signer.SignData(
            Encoding.UTF8.GetBytes(payload),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
    }
}
