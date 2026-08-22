namespace YO4X.ControlPlane.Workers.Outbox;

public sealed record OutboxWorkerIdentity
{
    private OutboxWorkerIdentity(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static OutboxWorkerIdentity Create() =>
        new($"control-plane-outbox-{Environment.ProcessId}-{Guid.NewGuid():N}");

    public static OutboxWorkerIdentity Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new OutboxWorkerIdentity(value.Trim());
    }
}
