using System.Security.Cryptography;
using System.Text;
using YO4X.LocalSecrets.Windows;
using YO4X.Trading.Abstractions;
using YO4X.Trading.ProcessIsolation;

namespace YO4X.Mt5.ConnectionProbe.Windows;

/// <summary>
/// The deliberately narrow result returned by an approved MT5 connection transport.
/// It cannot represent orders, positions, balances, equity, or other trading state.
/// </summary>
public sealed record Mt5ConnectionOnlyObservation(
    string BrokerCompany,
    string ServerName,
    BrokerAccountMode AccountMode,
    BrokerEnvironment Environment,
    BrokerTradingAccess TradingAccess,
    string Currency,
    bool DisconnectConfirmed,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// A vendor-specific implementation may authenticate, read the bounded identity fields
/// above, and disconnect. This interface intentionally has no mutation operation.
/// </summary>
public interface IMt5ConnectionOnlyTransport
{
    Task<Mt5ConnectionOnlyObservation> ConnectAndDisconnectAsync(
        LocalMt5Credential credential,
        CancellationToken cancellationToken);
}

/// <summary>
/// Immutable exact-id/exact-digest approval set. An empty set denies every artifact.
/// </summary>
public sealed class ApprovedMt5ProbeArtifacts
{
    private readonly Dictionary<Guid, string> artifacts;

    public ApprovedMt5ProbeArtifacts(IReadOnlyDictionary<Guid, string> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        var snapshot = new Dictionary<Guid, string>();
        foreach ((Guid id, string digest) in artifacts)
        {
            if (id == Guid.Empty || !IsSha256(digest))
            {
                throw new ArgumentException("Approved artifacts require a non-empty id and lowercase SHA-256.", nameof(artifacts));
            }

            snapshot.Add(id, digest);
        }

        this.artifacts = snapshot;
    }

    public bool Contains(Guid artifactId, string artifactSha256) =>
        artifacts.TryGetValue(artifactId, out string? approved)
        && FixedTimeEquals(approved, artifactSha256);

    private static bool IsSha256(string value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool FixedTimeEquals(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        byte[] leftBytes = Encoding.ASCII.GetBytes(left);
        byte[] rightBytes = Encoding.ASCII.GetBytes(right);
        try
        {
            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }
}

/// <summary>
/// Opens one opaque DPAPI-vault credential only after artifact and vault bindings pass,
/// delegates to a connection-only transport, then disposes the plaintext credential.
/// No exception or credential-derived value is included in a failure result.
/// </summary>
public sealed class VaultBackedBrokerConnectionProbeExecutor(
    ILocalMt5CredentialVault credentialVault,
    ApprovedMt5ProbeArtifacts approvedArtifacts,
    IMt5ConnectionOnlyTransport transport)
    : IBrokerConnectionProbeExecutor
{
    public async Task<GatewayOperationResult<BrokerConnectionProbeObservation>> ConnectProbeAsync(
        BrokerWorkerConnectProbeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.ExpectedEnvironment != BrokerEnvironment.Demo
            || !approvedArtifacts.Contains(
                request.GatewayArtifactId,
                request.GatewayArtifactSha256))
        {
            return Rejected();
        }

        try
        {
            string vaultIdentity = await credentialVault
                .GetEvidenceBindingAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!ApprovedMt5ProbeArtifacts.FixedTimeEquals(
                    vaultIdentity,
                    request.CredentialVaultIdentitySha256))
            {
                return Rejected();
            }

            using LocalMt5Credential? credential = await credentialVault
                .OpenAsync(request.CredentialKey, cancellationToken)
                .ConfigureAwait(false);
            if (credential is null
                || !string.Equals(
                    credential.CredentialKey,
                    request.CredentialKey,
                    StringComparison.Ordinal)
                || !string.Equals(
                    credential.Server,
                    request.Server.ServerName,
                    StringComparison.Ordinal))
            {
                return Rejected();
            }

            LocalMt5CredentialDescriptor descriptor = credential.Describe();
            Mt5ConnectionOnlyObservation connected = await transport
                .ConnectAndDisconnectAsync(credential, cancellationToken)
                .ConfigureAwait(false);

            if (!connected.DisconnectConfirmed
                || connected.Environment != BrokerEnvironment.Demo
                || connected.ObservedAtUtc < request.ProbeNotBeforeUtc
                || !string.Equals(
                    connected.BrokerCompany,
                    request.Server.BrokerCompany,
                    StringComparison.Ordinal)
                || !string.Equals(
                    connected.ServerName,
                    request.Server.ServerName,
                    StringComparison.Ordinal))
            {
                return Failed();
            }

            var observation = new BrokerConnectionProbeObservation(
                BrokerWorkerProtocolContract.ConnectProbeObservationVersion,
                request.BrokerAccountId,
                request.GatewayArtifactId,
                request.GatewayArtifactSha256,
                descriptor.MaskedLogin,
                connected.BrokerCompany,
                connected.ServerName,
                connected.AccountMode,
                connected.Environment,
                connected.TradingAccess,
                connected.Currency,
                true,
                connected.ObservedAtUtc);
            return new GatewayOperationResult<BrokerConnectionProbeObservation>(
                true,
                BrokerWorkerProtocolContract.ConnectProbeSucceededCode,
                observation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Failed();
        }
    }

    private static GatewayOperationResult<BrokerConnectionProbeObservation> Rejected() =>
        new(false, BrokerWorkerProtocolContract.ConnectProbeRejectedCode, null);

    private static GatewayOperationResult<BrokerConnectionProbeObservation> Failed() =>
        new(false, BrokerWorkerProtocolContract.ConnectProbeFailedCode, null);
}
