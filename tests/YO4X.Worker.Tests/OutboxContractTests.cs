using System.Security.Cryptography;
using System.Text;
using YO4X.ControlPlane.Workers.Outbox;

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
