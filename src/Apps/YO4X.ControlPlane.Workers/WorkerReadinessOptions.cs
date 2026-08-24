namespace YO4X.ControlPlane.Workers;

public sealed class WorkerReadinessOptions
{
    public const string SectionName = "WorkerReadiness";

    public TimeSpan MaximumHealthyAge { get; init; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (MaximumHealthyAge < TimeSpan.FromSeconds(1)
            || MaximumHealthyAge > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException(
                "MaximumHealthyAge must be between one second and five minutes.");
        }
    }
}
