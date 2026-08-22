namespace YO4X.BuildingBlocks;

public sealed class BackendCapabilityUnavailableException : Exception
{
    public BackendCapabilityUnavailableException(string capability)
        : base("The required backend capability is not configured or is not safely available.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        Capability = capability;
    }

    public string Capability { get; }
}
