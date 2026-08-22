using System.Text.RegularExpressions;
using YO4X.BuildingBlocks;

namespace YO4X.BrokerAccounts;

public enum BrokerAccountMode
{
    Hedging,
    Netting
}

public enum BrokerAccountEnvironment
{
    Demo,
    Live
}

public enum CloudCredentialState
{
    Absent,
    IngestionPending,
    Ready,
    Disabled,
    RotationPending,
    DeletionPending,
    Deleted
}

public sealed record BrokerCapabilities(
    BrokerAccountMode AccountMode,
    bool TradingAllowed,
    bool BrokerHostedStopLoss,
    bool BrokerHostedTakeProfit,
    bool SupportsPositionQuery,
    bool SupportsOrderQuery,
    bool SupportsDealHistory,
    DateTimeOffset ObservedAt,
    string EvidenceHash);

public sealed partial class BrokerAccount : VersionedAggregate
{
    private BrokerAccount(
        Guid id,
        Guid tenantId,
        Guid userId,
        Guid brokerId,
        string server,
        string maskedLogin,
        string bindingFingerprint,
        BrokerAccountEnvironment environment,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        TenantId = tenantId;
        UserId = userId;
        BrokerId = brokerId;
        Server = server;
        MaskedLogin = maskedLogin;
        BindingFingerprint = bindingFingerprint;
        Environment = environment;
        CredentialState = CloudCredentialState.Absent;
    }

    public Guid TenantId { get; }

    public Guid UserId { get; }

    public Guid BrokerId { get; }

    public string Server { get; }

    public string MaskedLogin { get; }

    public string BindingFingerprint { get; }

    public BrokerAccountEnvironment Environment { get; }

    public CloudCredentialState CredentialState { get; private set; }

    public BrokerCapabilities? Capabilities { get; private set; }

    internal string? CredentialReference { get; private set; }

    public static BrokerAccount CreateDraft(
        Guid tenantId,
        Guid userId,
        Guid brokerId,
        string server,
        string maskedLogin,
        string bindingFingerprint,
        BrokerAccountEnvironment environment,
        IClock clock)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty || brokerId == Guid.Empty)
        {
            throw new ArgumentException("Tenant, user, and broker identifiers are required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(maskedLogin);
        if (!Sha256Pattern().IsMatch(bindingFingerprint))
        {
            throw new ArgumentException("The account binding fingerprint must be a SHA-256 digest.", nameof(bindingFingerprint));
        }

        return new BrokerAccount(
            Identifiers.NewId(),
            tenantId,
            userId,
            brokerId,
            server.Trim(),
            maskedLogin.Trim(),
            bindingFingerprint.ToLowerInvariant(),
            environment,
            clock.UtcNow);
    }

    public void BeginCredentialIngestion(DateTimeOffset occurredAt)
    {
        if (CredentialState is CloudCredentialState.DeletionPending or CloudCredentialState.Deleted)
        {
            throw new DomainException("CREDENTIAL_DELETION_IN_PROGRESS", "Credential ingestion is unavailable after deletion begins.");
        }

        CredentialState = CloudCredentialState.IngestionPending;
        RecordChange(occurredAt);
    }

    public void CompleteCredentialIngestion(string opaqueReference, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(opaqueReference);
        if (CredentialState != CloudCredentialState.IngestionPending)
        {
            throw new DomainException("CREDENTIAL_INGESTION_NOT_PENDING", "No credential ingestion is pending.");
        }

        CredentialReference = opaqueReference;
        CredentialState = CloudCredentialState.Ready;
        RecordChange(occurredAt);
    }

    public void RecordCapabilities(BrokerCapabilities capabilities, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        Capabilities = capabilities;
        RecordChange(occurredAt);
    }

    public void DisableCloudUse(DateTimeOffset occurredAt)
    {
        if (CredentialState is CloudCredentialState.Absent or CloudCredentialState.Deleted)
        {
            return;
        }

        CredentialState = CloudCredentialState.Disabled;
        RecordChange(occurredAt);
    }

    public void RequestCredentialDeletion(DateTimeOffset occurredAt)
    {
        if (CredentialState == CloudCredentialState.Deleted)
        {
            return;
        }

        CredentialState = CloudCredentialState.DeletionPending;
        RecordChange(occurredAt);
    }

    public void ConfirmCredentialDeletion(DateTimeOffset occurredAt)
    {
        if (CredentialState != CloudCredentialState.DeletionPending)
        {
            throw new DomainException("CREDENTIAL_DELETION_NOT_PENDING", "Credential deletion was not requested.");
        }

        CredentialReference = null;
        CredentialState = CloudCredentialState.Deleted;
        RecordChange(occurredAt);
    }

    public IReadOnlyList<string> ValidateU0Eligibility(string allowlistedServer, DateTimeOffset now, TimeSpan freshness)
    {
        var failures = new List<string>();
        if (Environment != BrokerAccountEnvironment.Demo)
        {
            failures.Add("LIVE_ACCOUNT_NOT_ALLOWED");
        }

        if (!string.Equals(Server, allowlistedServer, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("BROKER_SERVER_NOT_ALLOWLISTED");
        }

        if (CredentialState != CloudCredentialState.Ready)
        {
            failures.Add("CREDENTIAL_NOT_READY");
        }

        if (Capabilities is null || now - Capabilities.ObservedAt > freshness)
        {
            failures.Add("BROKER_CAPABILITIES_STALE");
        }
        else
        {
            if (Capabilities.AccountMode != BrokerAccountMode.Hedging)
            {
                failures.Add("HEDGING_ACCOUNT_REQUIRED");
            }

            if (!Capabilities.TradingAllowed)
            {
                failures.Add("BROKER_TRADING_NOT_ALLOWED");
            }

            if (!Capabilities.BrokerHostedStopLoss || !Capabilities.BrokerHostedTakeProfit)
            {
                failures.Add("BROKER_HOSTED_PROTECTION_REQUIRED");
            }
        }

        return failures;
    }

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}

public sealed record CredentialStateView(
    bool Exists,
    CloudCredentialState State,
    DateTimeOffset? LastAuthorizedWorkerUse,
    string MaskedAccountBinding);

public interface IBrokerAccountRepository
{
    Task<BrokerAccount?> FindAsync(Guid tenantId, Guid accountId, CancellationToken cancellationToken);

    Task SaveAsync(BrokerAccount account, long expectedVersion, CancellationToken cancellationToken);
}
