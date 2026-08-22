namespace YO4X.ControlPlane.Workers;

public sealed record WorkerHealthSnapshot(
    string ContractVersion,
    string Role,
    bool Healthy,
    string State,
    string Code);
