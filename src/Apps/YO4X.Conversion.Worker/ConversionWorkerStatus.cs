namespace YO4X.Conversion.Worker;

public sealed record ConversionHealthSnapshot(
    string ContractVersion,
    string Role,
    bool Healthy,
    string State,
    string Code);

public sealed class ConversionWorkerStatus
{
    public ConversionWorkerStatus()
    {
        Enabled = false;
        Live = new ConversionHealthSnapshot(
            "worker-health.v1",
            "conversion-worker",
            true,
            "live",
            "process_live");
        Startup = new ConversionHealthSnapshot(
            "worker-health.v1",
            "conversion-worker",
            true,
            "started-disabled",
            "conversion_worker_disabled");
        Ready = new ConversionHealthSnapshot(
            "worker-health.v1",
            "conversion-worker",
            false,
            "not-ready",
            "conversion_prerequisites_missing");
    }

    public bool Enabled { get; }

    public ConversionHealthSnapshot Live { get; }

    public ConversionHealthSnapshot Startup { get; }

    public ConversionHealthSnapshot Ready { get; }
}
