using System.Buffers.Binary;
using System.Security.Cryptography;

namespace YO4X.ControlPlane.Workers.Outbox;

public sealed class RetrySchedule
{
    private readonly OutboxDispatchOptions _options;

    public RetrySchedule(OutboxDispatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    public TimeSpan GetDelay(Guid messageId, int attempt)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("A message identifier is required.", nameof(messageId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        long maximumTicks = _options.MaximumRetryDelay.Ticks;
        long delayTicks = _options.BaseRetryDelay.Ticks;
        for (int currentAttempt = 1; currentAttempt < attempt && delayTicks < maximumTicks; currentAttempt++)
        {
            delayTicks = delayTicks > maximumTicks / 2
                ? maximumTicks
                : Math.Min(delayTicks * 2, maximumTicks);
        }

        long availableJitterTicks = Math.Min(
            _options.MaximumRetryJitter.Ticks,
            maximumTicks - delayTicks);
        if (availableJitterTicks == 0)
        {
            return TimeSpan.FromTicks(delayTicks);
        }

        Span<byte> input = stackalloc byte[20];
        _ = messageId.TryWriteBytes(input[..16]);
        BinaryPrimitives.WriteInt32BigEndian(input[16..], attempt);
        Span<byte> hash = stackalloc byte[32];
        _ = SHA256.TryHashData(input, hash, out _);
        ulong sample = BinaryPrimitives.ReadUInt64BigEndian(hash);
        long jitterTicks = (long)(sample % ((ulong)availableJitterTicks + 1UL));
        return TimeSpan.FromTicks(delayTicks + jitterTicks);
    }
}
