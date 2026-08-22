using System.Security.Cryptography;
using YO4X.BuildingBlocks;
using YO4X.Risk;

namespace YO4X.Domain.Tests;

public sealed class NumericRiskPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidP256SignedPolicyIsAccepted()
    {
        NumericRiskPolicyDescriptor descriptor = CreateDescriptor();

        VerifiedNumericRiskPolicy verified = Verify(descriptor);

        Assert.Equal(descriptor.PolicyId, verified.Descriptor.PolicyId);
        Assert.Equal(CanonicalJson.Sha256(descriptor), verified.PayloadDigest);
    }

    [Fact]
    public void MissingNumericPolicyValuesAreRejectedInsteadOfDefaulted()
    {
        NumericRiskPolicyContent baseline = CreateContent();
        NumericRiskPolicyContent[] invalid =
        [
            baseline with { MaxPerOrderVolume = null },
            baseline with { MaxAccountPositionVolume = null },
            baseline with { MaxAccountGrossNotional = null },
            baseline with { MaxOpenPositions = null },
            baseline with { MaxOpenOrders = null },
            baseline with { MaxOrdersPerWindow = null },
            baseline with { OrderRateWindowMilliseconds = null },
            baseline with { MaxDailyLoss = null },
            baseline with { MaxDrawdown = null },
            baseline with { MaxSpreadPoints = null },
            baseline with { MaxSlippagePoints = null },
            baseline with { MaxStopLossDistancePoints = null },
            baseline with { MinTakeProfitDistancePoints = null },
            baseline with { IncreaseFreshness = null },
            baseline with { ReduceProtectFreshness = null },
            baseline with { DemoOnly = null },
            baseline with { HedgingOnly = null },
            baseline with { RequireBrokerHostedStopLoss = null },
            baseline with { RequireBrokerHostedTakeProfit = null },
            baseline with { BlockExposureIncreaseOnExternalActivity = null }
        ];

        foreach (NumericRiskPolicyContent content in invalid)
        {
            Assert.Throws<DomainException>(() => Verify(CreateDescriptor(content: content)));
        }
    }

    [Fact]
    public void U0SafetyBooleansCannotBeDisabledByPolicy()
    {
        NumericRiskPolicyContent baseline = CreateContent();
        NumericRiskPolicyContent[] invalid =
        [
            baseline with { DemoOnly = false },
            baseline with { HedgingOnly = false },
            baseline with { RequireBrokerHostedStopLoss = false },
            baseline with { RequireBrokerHostedTakeProfit = false },
            baseline with { BlockExposureIncreaseOnExternalActivity = false }
        ];

        foreach (NumericRiskPolicyContent content in invalid)
        {
            Assert.Throws<DomainException>(() => Verify(CreateDescriptor(content: content)));
        }
    }

    [Fact]
    public void NegativeLimitsAndNonPositiveProtectionOrRateWindowsAreRejected()
    {
        NumericRiskPolicyContent baseline = CreateContent();
        NumericRiskPolicyContent[] invalid =
        [
            baseline with { MaxPerOrderVolume = -0.01m },
            baseline with { MaxDailyLoss = -0.01m },
            baseline with { MaxSpreadPoints = -0.01m },
            baseline with { MaxOpenOrders = -1 },
            baseline with { OrderRateWindowMilliseconds = 0 },
            baseline with { MaxStopLossDistancePoints = 0m },
            baseline with { MinTakeProfitDistancePoints = 0m }
        ];

        foreach (NumericRiskPolicyContent content in invalid)
        {
            Assert.Throws<DomainException>(() => Verify(CreateDescriptor(content: content)));
        }
    }

    [Fact]
    public void PayloadDigestMismatchIsRejectedBeforeSignatureTrust()
    {
        NumericRiskPolicyDescriptor descriptor = CreateDescriptor();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] signature = key.SignData(
            System.Text.Encoding.UTF8.GetBytes(VerifiedNumericRiskPolicy.CanonicalPayload(descriptor)),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        var envelope = new RiskPolicySignature(
            EcdsaP256RiskPolicySignatureVerifier.Algorithm,
            "risk-owner-1",
            signature,
            Convert.ToHexString(SHA256.HashData(signature)).ToLowerInvariant(),
            new string('0', 64));
        using var verifier = new EcdsaP256RiskPolicySignatureVerifier(
            new Dictionary<string, byte[]> { ["risk-owner-1"] = key.ExportSubjectPublicKeyInfo() });

        DomainException exception = Assert.Throws<DomainException>(() =>
            VerifiedNumericRiskPolicy.Verify(new SignedNumericRiskPolicy(descriptor, envelope), verifier));

        Assert.Equal("RISK_POLICY_PAYLOAD_DIGEST_MISMATCH", exception.Code);
    }

    [Fact]
    public void WrongSigningKeyAndWrongCurveAreRejected()
    {
        NumericRiskPolicyDescriptor descriptor = CreateDescriptor();
        using var trusted = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var untrusted = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SignedNumericRiskPolicy signed = Sign(descriptor, untrusted);
        using var verifier = new EcdsaP256RiskPolicySignatureVerifier(
            new Dictionary<string, byte[]> { ["risk-owner-1"] = trusted.ExportSubjectPublicKeyInfo() });

        DomainException exception = Assert.Throws<DomainException>(() =>
            VerifiedNumericRiskPolicy.Verify(signed, verifier));
        Assert.Equal("RISK_POLICY_SIGNATURE_UNTRUSTED", exception.Code);

        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.ThrowsAny<CryptographicException>(() =>
            new EcdsaP256RiskPolicySignatureVerifier(
                new Dictionary<string, byte[]> { ["risk-owner-1"] = p384.ExportSubjectPublicKeyInfo() }));
    }

    [Fact]
    public void RestrictiveMeetIsScopeOrderIndependentAndMonotonic()
    {
        VerifiedNumericRiskPolicy baseline = Verify(CreateDescriptor());
        NumericRiskPolicyContent restriction = CreateContent() with
        {
            MaxPerOrderVolume = 1m,
            MaxAccountPositionVolume = 8m,
            MaxAccountGrossNotional = 80_000m,
            MaxDailyLoss = 300m,
            MaxDrawdown = 700m,
            MaxSpreadPoints = 20m,
            MaxSlippagePoints = 7m,
            MaxStopLossDistancePoints = 400m,
            MinTakeProfitDistancePoints = 150m,
            IncreaseFreshness = Freshness(500)
        };
        VerifiedNumericRiskPolicy overlay = Verify(CreateDescriptor(
            policyId: Guid.Parse("58e632e2-aec2-4c3a-8e8c-171c1b16dca1"),
            scope: "deployment:restricted",
            content: restriction));

        EffectiveNumericRiskPolicy first = EffectiveNumericRiskPolicy.Meet([baseline, overlay], Now);
        EffectiveNumericRiskPolicy second = EffectiveNumericRiskPolicy.Meet([overlay, baseline], Now);
        EffectiveNumericRiskPolicy onlyBaseline = EffectiveNumericRiskPolicy.Meet([baseline], Now);
        EffectiveNumericRiskPolicy duplicateBaseline = EffectiveNumericRiskPolicy.Meet(
            [baseline, baseline],
            Now);

        Assert.Equal(first.Digest, second.Digest);
        Assert.Equal(1m, first.MaxPerOrderVolume);
        Assert.Equal(150m, first.MinTakeProfitDistancePoints);
        Assert.True(first.IsAtLeastAsRestrictiveAs(onlyBaseline));
        Assert.Equal(onlyBaseline.Digest, duplicateBaseline.Digest);
    }

    [Fact]
    public void MeetRejectsIncompatibleRiskDayOrRateWindowSemantics()
    {
        VerifiedNumericRiskPolicy baseline = Verify(CreateDescriptor());
        VerifiedNumericRiskPolicy differentDay = Verify(CreateDescriptor(
            policyId: Guid.NewGuid(),
            content: CreateContent() with
            {
                RiskDay = new RiskDayDefinition("UTC", "tzdb-test-v1", new TimeOnly(17, 0))
            }));
        VerifiedNumericRiskPolicy differentWindow = Verify(CreateDescriptor(
            policyId: Guid.NewGuid(),
            content: CreateContent() with { OrderRateWindowMilliseconds = 30_000 }));

        Assert.Equal(
            "RISK_POLICY_RISK_DAY_INCOMPATIBLE",
            Assert.Throws<DomainException>(() =>
                EffectiveNumericRiskPolicy.Meet([baseline, differentDay], Now)).Code);
        Assert.Equal(
            "RISK_POLICY_ORDER_WINDOW_INCOMPATIBLE",
            Assert.Throws<DomainException>(() =>
                EffectiveNumericRiskPolicy.Meet([baseline, differentWindow], Now)).Code);
    }

    [Fact]
    public void EmptyExpiredAndConflictingPolicySetsFailClosed()
    {
        Assert.Equal(
            "RISK_POLICY_SET_EMPTY",
            Assert.Throws<DomainException>(() =>
                EffectiveNumericRiskPolicy.Meet([], Now)).Code);

        VerifiedNumericRiskPolicy expired = Verify(CreateDescriptor(
            effectiveFromUtc: Now.AddDays(-2),
            expiresAtUtc: Now.AddDays(-1)));
        Assert.Equal(
            "RISK_POLICY_NOT_CURRENT",
            Assert.Throws<DomainException>(() =>
                EffectiveNumericRiskPolicy.Meet([expired], Now)).Code);

        Guid sameId = Guid.NewGuid();
        VerifiedNumericRiskPolicy first = Verify(CreateDescriptor(policyId: sameId));
        VerifiedNumericRiskPolicy conflict = Verify(CreateDescriptor(
            policyId: sameId,
            content: CreateContent() with { MaxDailyLoss = 1m }));
        Assert.Equal(
            "RISK_POLICY_VERSION_DIGEST_CONFLICT",
            Assert.Throws<DomainException>(() =>
                EffectiveNumericRiskPolicy.Meet([first, conflict], Now)).Code);

        VerifiedNumericRiskPolicy differentlySigned = Verify(CreateDescriptor(policyId: sameId));
        Assert.Equal(
            "RISK_POLICY_VERSION_SIGNATURE_CONFLICT",
            Assert.Throws<DomainException>(() =>
                EffectiveNumericRiskPolicy.Meet([first, differentlySigned], Now)).Code);
    }

    internal static NumericRiskPolicyContent CreateContent() => new(
        new RiskDayDefinition("UTC", "tzdb-test-v1", TimeOnly.MinValue),
        MaxPerOrderVolume: 2m,
        MaxAccountPositionVolume: 10m,
        MaxAccountGrossNotional: 100_000m,
        MaxOpenPositions: 5,
        MaxOpenOrders: 5,
        MaxOrdersPerWindow: 10,
        OrderRateWindowMilliseconds: 60_000,
        MaxDailyLoss: 500m,
        MaxDrawdown: 1_000m,
        MaxSpreadPoints: 25m,
        MaxSlippagePoints: 10m,
        MaxStopLossDistancePoints: 500m,
        MinTakeProfitDistancePoints: 100m,
        IncreaseFreshness: Freshness(1_000),
        ReduceProtectFreshness: Freshness(5_000),
        DemoOnly: true,
        HedgingOnly: true,
        RequireBrokerHostedStopLoss: true,
        RequireBrokerHostedTakeProfit: true,
        BlockExposureIncreaseOnExternalActivity: true);

    internal static NumericRiskPolicyDescriptor CreateDescriptor(
        Guid? policyId = null,
        string scope = "environment:u0",
        DateTimeOffset? effectiveFromUtc = null,
        DateTimeOffset? expiresAtUtc = null,
        NumericRiskPolicyContent? content = null) => new(
            policyId ?? Guid.Parse("6be95b2a-e68f-4e07-9946-7dcc49f3dd83"),
            Version: 1,
            scope,
            effectiveFromUtc ?? Now.AddDays(-1),
            expiresAtUtc ?? Now.AddDays(1),
            content ?? CreateContent());

    internal static VerifiedNumericRiskPolicy Verify(NumericRiskPolicyDescriptor descriptor)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SignedNumericRiskPolicy signed = Sign(descriptor, key);
        using var verifier = new EcdsaP256RiskPolicySignatureVerifier(
            new Dictionary<string, byte[]> { ["risk-owner-1"] = key.ExportSubjectPublicKeyInfo() });
        return VerifiedNumericRiskPolicy.Verify(signed, verifier);
    }

    private static SignedNumericRiskPolicy Sign(
        NumericRiskPolicyDescriptor descriptor,
        ECDsa key)
    {
        byte[] payload = System.Text.Encoding.UTF8.GetBytes(
            VerifiedNumericRiskPolicy.CanonicalPayload(descriptor));
        byte[] signature = key.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        return new SignedNumericRiskPolicy(
            descriptor,
            new RiskPolicySignature(
                EcdsaP256RiskPolicySignatureVerifier.Algorithm,
                "risk-owner-1",
                signature,
                Convert.ToHexString(SHA256.HashData(signature)).ToLowerInvariant(),
                CanonicalJson.Sha256(descriptor)));
    }

    private static RiskFreshnessLimits Freshness(long milliseconds) => new(
        milliseconds,
        milliseconds,
        milliseconds,
        milliseconds,
        milliseconds,
        milliseconds);
}
