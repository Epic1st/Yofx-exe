using System.Security.Cryptography;
using System.Text;
using YO4X.ControlPlane.Workers.Outbox;
using YO4X.Outbox;

namespace YO4X.Worker.Tests;

public sealed class OutboxContractTests
{
    [Fact]
    public void IdempotencyIdentityRemainsStableAcrossAttempts()
    {
        Guid messageId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        ClaimedOutboxItem firstAttempt = CreateItem(messageId, 1);
        ClaimedOutboxItem laterAttempt = CreateItem(messageId, 7);

        OutboxDeliveryEnvelope first = OutboxDeliveryEnvelope.Create(firstAttempt);
        OutboxDeliveryEnvelope later = OutboxDeliveryEnvelope.Create(laterAttempt);

        Assert.Equal(first.StableMessageId, later.StableMessageId);
        Assert.Equal(first.IdempotencyKey, later.IdempotencyKey);
        Assert.Equal(1, first.Attempt);
        Assert.Equal(7, later.Attempt);
        Assert.EndsWith(messageId.ToString("N"), first.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public void IdentitySeparatesTenantsForSameMessageIdentifier()
    {
        Guid messageId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        ClaimedOutboxItem first = CreateItem(messageId, 1);
        ClaimedOutboxItem second = CreateItem(
            messageId,
            1,
            Guid.Parse("20000000-0000-0000-0000-000000000001"));

        Assert.NotEqual(
            OutboxDeliveryEnvelope.Create(first).IdempotencyKey,
            OutboxDeliveryEnvelope.Create(second).IdempotencyKey);
    }

    [Fact]
    public void RetryScheduleIsDeterministicExponentialAndCapped()
    {
        var options = new OutboxDispatchOptions
        {
            BaseRetryDelay = TimeSpan.FromSeconds(1),
            MaximumRetryDelay = TimeSpan.FromSeconds(4),
            MaximumRetryJitter = TimeSpan.FromMilliseconds(100)
        };
        var schedule = new RetrySchedule(options);
        Guid messageId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        TimeSpan first = schedule.GetDelay(messageId, 1);
        TimeSpan second = schedule.GetDelay(messageId, 2);
        TimeSpan capped = schedule.GetDelay(messageId, 99);

        Assert.Equal(first, schedule.GetDelay(messageId, 1));
        Assert.InRange(first, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.1));
        Assert.InRange(second, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2.1));
        Assert.Equal(TimeSpan.FromSeconds(4), capped);
    }

    [Fact]
    public void OptionsRejectUnboundedBatch()
    {
        var options = new OutboxDispatchOptions { BatchSize = 1_001 };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("batch size", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MessageSchemaVersionIsDerivedFromCanonicalMessageType()
    {
        OutboxMessage produced = OutboxMessage.Create(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            "yo4x.deployment.start.requested.v3",
            "user_operation",
            "10000000-0000-0000-0000-000000000002",
            new { schemaVersion = 3 },
            Guid.Parse("10000000-0000-0000-0000-000000000003"),
            null,
            DateTimeOffset.UtcNow);

        Assert.Equal(3, produced.SchemaVersion);
        Assert.Equal(
            4,
            OutboxSchemaVersion.ValidateStored(
                "yo4x.deployment.start.requested.v4",
                4,
                "{\"schemaVersion\":4}"));
        Assert.Equal(
            2,
            OutboxSchemaVersion.ValidateStored(
                "yo4x.user-operation.reconciliation-requested.v2",
                2,
                "{\"contractVersion\":2}"));
        Assert.Equal(
            1,
            OutboxSchemaVersion.ValidateStored(
                "broker_account.credential_ready",
                1,
                "{\"value\":1}"));
        Assert.Equal(
            1,
            OutboxSchemaVersion.ValidateStored(
                "broker_account.vault_ready",
                1,
                "{\"value\":1}"));
        Assert.Equal(
            1,
            OutboxSchemaVersion.ValidateStored(
                "broker_account.credential_ready",
                1,
                "{\"value\":1}"));
        Assert.Equal(
            4,
            OutboxSchemaVersion.ValidateStored(
                "yo4x.deployment.start.requested.v4",
                4,
                "{\"schemaVersion\":4}"));
    }

    [Theory]
    [InlineData("yo4x.deployment.start.requested.v4", "{\"schemaVersion\":1}")]
    [InlineData("yo4x.deployment.start.requested.v4", "{}")]
    [InlineData("yo4x.deployment.start.requested.v4", "{\"schemaVersion\":\"4\"}")]
    [InlineData("yo4x.user-operation.reconciliation-requested.v2", "{\"contractVersion\":3}")]
    [InlineData("yo4x.deployment.start.requested.v04", "{\"schemaVersion\":4}")]
    [InlineData("yo4x.deployment.start.requested.v4", "{\"schemaVersion\":4,\"schemaVersion\":4}")]
    public void MessageSchemaVersionRejectsTypePayloadDrift(
        string messageType,
        string payload)
    {
        Assert.Throws<InvalidDataException>(
            () => OutboxSchemaVersion.ValidateStored(
                messageType,
                messageType.EndsWith(".v2", StringComparison.Ordinal) ? 2 : 4,
                payload));
    }

    [Fact]
    public void MessageSchemaVersionRejectsStoredColumnDrift()
    {
        Assert.Throws<InvalidDataException>(
            () => OutboxSchemaVersion.ValidateStored(
                "yo4x.deployment.start.requested.v4",
                1,
                "{\"schemaVersion\":4}"));
    }

    private static ClaimedOutboxItem CreateItem(
        Guid messageId,
        int attempt,
        Guid? tenantId = null)
    {
        const string payload = "{\"value\":1}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return new ClaimedOutboxItem(
            messageId,
            tenantId ?? Guid.Parse("10000000-0000-0000-0000-000000000001"),
            "test.message",
            1,
            payload,
            hash,
            DateTimeOffset.UtcNow,
            attempt);
    }
}
