using Npgsql;
using YO4X.BrokerAccounts;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;

namespace YO4X.ControlPlane.Postgres;

public sealed partial class PostgresControlPlaneApplication
{
    /// <summary>
    /// Promotes one imported MetaTrader 5 directory server to a demo-linkable
    /// server for the caller's own tenant.
    /// </summary>
    /// <remarks>
    /// Minting the governance profile behind an approval is a GLOBAL authority
    /// mutation, and `control.lock_u0_global_authority_mutation` requires every
    /// global write to precede the tenant authority lock in the same
    /// transaction. This path therefore does not take the tenant authority lock
    /// up front the way other user mutations do: the capability takes it itself,
    /// after the governance write, so the mandated ordering holds. The Control
    /// API role has no write grant on `governance.broker_profiles` and the
    /// capability re-validates the tenant, identity and session independently of
    /// anything this process asserts.
    /// </remarks>
    public async Task<BrokerAccountRegistrationOption> ApproveBrokerServerAsync(
        UserActor actor,
        ApproveBrokerServer request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DirectoryServerId == Guid.Empty)
        {
            throw InvalidBrokerServerApproval();
        }

        (var transaction, AuthorizedUser user) = await BeginAuthorizedAsync(
                actor,
                metadata.CorrelationId,
                acquireAuthorityLock: false,
                cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            RequireVerifiedUser(user);

            MutationLease<BrokerAccountRegistrationOption> mutation =
                await BeginMutationAsync<ApproveBrokerServer, BrokerAccountRegistrationOption>(
                    transaction,
                    "broker-server.approve",
                    metadata,
                    request,
                    cancellationToken).ConfigureAwait(false);
            if (mutation.Replay is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return mutation.Replay;
            }

            BrokerAccountRegistrationOption option;
            await using (NpgsqlCommand approve = transaction.CreateCommand(
                """
                select approved_broker_profile_id, approved_broker_company, approved_server_name
                from brokerdirectory.approve_demo_server(@directory_server_id)
                """))
            {
                AddUuid(approve, "directory_server_id", request.DirectoryServerId);
                try
                {
                    await using NpgsqlDataReader reader = await approve
                        .ExecuteReaderAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("The broker server was not approved.");
                    }

                    option = new BrokerAccountRegistrationOption(
                        reader.GetGuid(0),
                        request.DirectoryServerId,
                        reader.GetString(1),
                        reader.GetString(2),
                        BrokerAccountEnvironment.Demo,
                        Approved: true);
                }
                catch (PostgresException exception)
                    when (string.Equals(exception.SqlState, "42704", StringComparison.Ordinal))
                {
                    throw new ResourceNotFoundException();
                }
                catch (PostgresException exception)
                    when (string.Equals(exception.SqlState, "42501", StringComparison.Ordinal))
                {
                    throw BrokerServerApprovalDenied();
                }
            }

            await AppendMutationEvidenceAsync(
                transaction,
                "broker_server.approved",
                "broker_profile",
                option.BrokerProfileId ?? Guid.Empty,
                metadata.Reason,
                mutation.Id,
                new
                {
                    directoryServerId = request.DirectoryServerId,
                    brokerProfileId = option.BrokerProfileId,
                    option.BrokerCompany,
                    option.Server,
                    environment = "demo",
                    scope = "tenant"
                },
                YO4X.Audit.AuditCategory.Governance,
                YO4X.Audit.AuditOutcome.Succeeded,
                CreateUserAuditContext(actor, user, metadata),
                cancellationToken).ConfigureAwait(false);
            await CompleteMutationAsync(transaction, mutation.Id, 201, option, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return option;
        }
    }

    private static DomainException InvalidBrokerServerApproval() => new(
        "BROKER_SERVER_APPROVAL_INVALID",
        "The broker-server approval request is invalid.");

    private static AuthorizationDeniedException BrokerServerApprovalDenied() => new(
        "BROKER_SERVER_APPROVAL_DENIED",
        "The broker server could not be approved for demo registration.");
}
