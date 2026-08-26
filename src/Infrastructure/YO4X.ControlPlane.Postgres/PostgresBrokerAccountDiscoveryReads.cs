using System.Text;
using Npgsql;
using NpgsqlTypes;
using YO4X.BrokerAccounts;
using YO4X.ControlPlane.Application;
using YO4X.Persistence.Postgres;

namespace YO4X.ControlPlane.Postgres;

public sealed partial class PostgresControlPlaneApplication
{
    private const int BrokerAccountDiscoveryLimit = 100;

    private const int BrokerAccountRegistrationOptionLimit = 100;

    private const int BrokerServerDirectorySearchLimit = 50;

    private const int BrokerServerDirectoryMinimumQueryLength = 2;

    private const int BrokerServerDirectoryMaximumQueryLength = 100;

    public async Task<IReadOnlyList<BrokerAccountView>> GetBrokerAccountsAsync(
        UserActor actor,
        CancellationToken cancellationToken)
    {
        (var transaction, _) = await BeginAuthorizedAsync(
            actor,
            Guid.CreateVersion7(),
            cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                with authority_time as materialized
                (
                    select clock_timestamp() as authority_now
                )
                select
                    account.id,
                    account.broker_id,
                    account.server,
                    account.masked_login,
                    account.environment,
                    account.account_mode,
                    case
                        when account.capability_valid_until is null then 'UNKNOWN'
                        when account.capability_valid_until <= authority_time.authority_now then 'STALE'
                        else 'CURRENT'
                    end,
                    account.row_version,
                    account.updated_at
                from operations.broker_accounts as account
                cross join authority_time
                where account.tenant_id = @tenant_id
                  and account.user_id = @user_id
                  and account.state <> 'deleted'
                order by account.updated_at desc, account.id desc
                limit @limit
                """);
            AddUuid(command, "tenant_id", actor.TenantId);
            AddUuid(command, "user_id", actor.UserId);
            command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, BrokerAccountDiscoveryLimit);

            var accounts = new List<BrokerAccountView>();
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    accounts.Add(ReadBrokerAccountView(reader));
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return accounts;
        }
    }

    public async Task<IReadOnlyList<BrokerAccountRegistrationOption>> GetBrokerAccountRegistrationOptionsAsync(
        UserActor actor,
        string? query,
        CancellationToken cancellationToken)
    {
        string? searchTerm = NormalizeDirectoryQuery(query);

        // Establish the authenticated tenant/user/session boundary before inspecting
        // configuration or governance data so callers cannot use this endpoint as an oracle.
        (var transaction, _) = await BeginAuthorizedAsync(
            actor,
            Guid.CreateVersion7(),
            cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            IReadOnlyList<BrokerAccountRegistrationOption> result = searchTerm is null
                ? await ReadApprovedRegistrationOptionsAsync(transaction, actor, cancellationToken)
                    .ConfigureAwait(false)
                : await SearchBrokerServerDirectoryAsync(transaction, actor, searchTerm, cancellationToken)
                    .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>
    /// A blank search means "what may I link right now", so this returns the
    /// deployment-pinned profile plus every directory server this tenant has
    /// explicitly approved, and nothing else.
    /// </summary>
    private async Task<IReadOnlyList<BrokerAccountRegistrationOption>> ReadApprovedRegistrationOptionsAsync(
        TenantPostgresTransaction transaction,
        UserActor actor,
        CancellationToken cancellationToken)
    {
        Guid pinnedProfileId = options.ApprovedBrokerProfileId ?? Guid.Empty;
        string pinnedServer = string.IsNullOrWhiteSpace(options.ApprovedBrokerServer)
            ? string.Empty
            : NormalizeBrokerServer(options.ApprovedBrokerServer);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                profile.id,
                profile.broker_company,
                profile.server_name,
                approval.server_id
            from governance.broker_profiles as profile
            left join brokerdirectory.tenant_demo_approvals as approval
              on approval.broker_profile_id = profile.id
             and approval.tenant_id = @tenant_id
            where profile.state = 'approved'
              and 'demo' = any(profile.environment_support)
              and
              (
                  (profile.id = @pinned_profile_id and profile.server_name = @pinned_server)
                  or approval.id is not null
              )
            order by (profile.id = @pinned_profile_id) desc, profile.server_name, profile.id
            limit @limit
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "pinned_profile_id", pinnedProfileId);
        command.Parameters.AddWithValue("pinned_server", NpgsqlDbType.Text, pinnedServer);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, BrokerAccountRegistrationOptionLimit);

        var approved = new List<BrokerAccountRegistrationOption>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            approved.Add(new BrokerAccountRegistrationOption(
                reader.GetGuid(0),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetString(1),
                reader.GetString(2),
                BrokerAccountEnvironment.Demo,
                Approved: true));
        }

        return approved;
    }

    /// <summary>
    /// Searches the imported directory. Matching happens in PostgreSQL and the
    /// page is small on purpose: the directory holds thousands of servers, and
    /// shipping it whole to a browser would be both slow and unusable.
    /// </summary>
    private static async Task<IReadOnlyList<BrokerAccountRegistrationOption>> SearchBrokerServerDirectoryAsync(
        TenantPostgresTransaction transaction,
        UserActor actor,
        string searchTerm,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                directory_server.id,
                directory_server.broker_company,
                directory_server.server_name,
                approval.broker_profile_id
            from brokerdirectory.servers as directory_server
            left join brokerdirectory.tenant_demo_approvals as approval
              on approval.server_id = directory_server.id
             and approval.tenant_id = @tenant_id
            where strpos(directory_server.search_key, @query) > 0
            order by directory_server.search_key, directory_server.id
            limit @limit
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        command.Parameters.AddWithValue("query", NpgsqlDbType.Text, searchTerm);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, BrokerServerDirectorySearchLimit);

        var matches = new List<BrokerAccountRegistrationOption>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            bool approved = !reader.IsDBNull(3);
            matches.Add(new BrokerAccountRegistrationOption(
                approved ? reader.GetGuid(3) : null,
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                BrokerAccountEnvironment.Demo,
                approved));
        }

        return matches;
    }

    /// <summary>
    /// Returns null when the caller supplied no usable search term, so the
    /// endpoint falls back to the approved list instead of scanning the whole
    /// directory for an empty or one-character string.
    /// </summary>
    private static string? NormalizeDirectoryQuery(string? query)
    {
        string normalized = query?.Trim().Normalize(NormalizationForm.FormC) ?? string.Empty;
        if (normalized.Length is < BrokerServerDirectoryMinimumQueryLength
                or > BrokerServerDirectoryMaximumQueryLength
            || normalized.Any(char.IsControl))
        {
            return null;
        }

        return normalized.ToLowerInvariant();
    }

    private static BrokerAccountView ReadBrokerAccountView(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetString(2),
        reader.GetString(3),
        ParseBrokerEnvironment(reader.GetString(4)),
        reader.IsDBNull(5) ? null : ParseBrokerMode(reader.GetString(5)),
        reader.GetString(6),
        reader.GetInt64(7),
        reader.GetFieldValue<DateTimeOffset>(8));
}
