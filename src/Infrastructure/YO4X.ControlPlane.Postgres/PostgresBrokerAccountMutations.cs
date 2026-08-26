using System.Text;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using YO4X.BrokerAccounts;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;

namespace YO4X.ControlPlane.Postgres;

public sealed partial class PostgresControlPlaneApplication
{
    public async Task<BrokerAccountView> CreateBrokerAccountAsync(
        UserActor actor,
        CreateBrokerAccount request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        (var transaction, AuthorizedUser user) = await BeginMutationAuthorizedAsync(
                actor,
                metadata.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            RequireVerifiedUser(user);

            CreateBrokerAccount normalized = NormalizeBrokerAccountRegistration(request);

            // Two ways to be linkable, and no third: the deployment-pinned
            // profile exactly as before, or a directory server this tenant
            // explicitly approved. The pin itself is still mandatory, so a
            // deployment that never configured one stays fail-closed rather than
            // silently falling through to the directory. Which of the two applies
            // is decided by PostgreSQL below, not here.
            bool pinnedProfileConfigured = options.ApprovedBrokerProfileId is Guid approvedProfileId
                && approvedProfileId != Guid.Empty
                && !string.IsNullOrWhiteSpace(options.ApprovedBrokerServer);
            if (!pinnedProfileConfigured)
            {
                throw BrokerProfileNotApproved();
            }

            MutationLease<BrokerAccountView> mutation =
                await BeginMutationAsync<CreateBrokerAccount, BrokerAccountView>(
                    transaction,
                    "broker-account.create",
                    metadata,
                    normalized,
                    cancellationToken).ConfigureAwait(false);
            if (mutation.Replay is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return mutation.Replay;
            }

            Guid brokerId;
            await using (NpgsqlCommand profile = transaction.CreateCommand(
                """
                select profile.broker_id
                from governance.broker_profiles as profile
                where profile.id = @broker_profile_id
                  and profile.state = 'approved'
                  and profile.server_name = @server
                  and @environment = any(profile.environment_support)
                  and
                  (
                      (profile.id = @pinned_profile_id and profile.server_name = @pinned_server)
                      or exists
                      (
                          select 1
                          from brokerdirectory.tenant_demo_approvals as approval
                          where approval.broker_profile_id = profile.id
                            and approval.tenant_id = @tenant_id
                      )
                  )
                """))
            {
                AddUuid(profile, "broker_profile_id", normalized.BrokerProfileId);
                AddUuid(profile, "pinned_profile_id", options.ApprovedBrokerProfileId ?? Guid.Empty);
                AddUuid(profile, "tenant_id", actor.TenantId);
                profile.Parameters.AddWithValue(
                    "pinned_server",
                    NpgsqlDbType.Text,
                    NormalizeBrokerServer(options.ApprovedBrokerServer));
                profile.Parameters.AddWithValue("server", NpgsqlDbType.Text, normalized.Server);
                profile.Parameters.AddWithValue("environment", NpgsqlDbType.Text, "demo");
                object? resolvedBrokerId = await profile.ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (resolvedBrokerId is not Guid value || value == Guid.Empty)
                {
                    throw BrokerProfileNotApproved();
                }

                brokerId = value;
            }

            Guid brokerAccountId = Guid.CreateVersion7();
            BrokerAccountView view;
            await using (NpgsqlCommand insert = transaction.CreateCommand(
                """
                insert into operations.broker_accounts
                (
                    id, tenant_id, user_id, broker_id, broker_profile_id,
                    server, masked_login, binding_fingerprint, environment
                )
                values
                (
                    @id, @tenant_id, @user_id, @broker_id, @broker_profile_id,
                    @server, @masked_login, @binding_fingerprint, 'demo'
                )
                returning
                    id, broker_id, server, masked_login, environment,
                    account_mode, row_version, updated_at
                """))
            {
                AddUuid(insert, "id", brokerAccountId);
                AddUuid(insert, "tenant_id", actor.TenantId);
                AddUuid(insert, "user_id", actor.UserId);
                AddUuid(insert, "broker_id", brokerId);
                AddUuid(insert, "broker_profile_id", normalized.BrokerProfileId);
                insert.Parameters.AddWithValue("server", NpgsqlDbType.Text, normalized.Server);
                insert.Parameters.AddWithValue("masked_login", NpgsqlDbType.Text, normalized.MaskedLogin);
                insert.Parameters.AddWithValue(
                    "binding_fingerprint",
                    NpgsqlDbType.Text,
                    normalized.BindingFingerprint);

                try
                {
                    await using NpgsqlDataReader reader = await insert.ExecuteReaderAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("The broker account was not created.");
                    }

                    view = new BrokerAccountView(
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        ParseBrokerEnvironment(reader.GetString(4)),
                        reader.IsDBNull(5) ? null : ParseBrokerMode(reader.GetString(5)),
                        "UNKNOWN",
                        reader.GetInt64(6),
                        reader.GetFieldValue<DateTimeOffset>(7));
                }
                catch (PostgresException exception) when (
                    exception.SqlState == PostgresErrorCodes.UniqueViolation
                    && string.Equals(
                        exception.ConstraintName,
                        "broker_accounts_tenant_id_binding_fingerprint_key",
                        StringComparison.Ordinal))
                {
                    throw new ResourceConflictException(
                        "BROKER_ACCOUNT_ALREADY_REGISTERED",
                        "The broker-account binding is already registered.");
                }
            }

            await AppendMutationEvidenceAsync(
                transaction,
                "broker_account.created",
                "broker_account",
                brokerAccountId,
                metadata.Reason,
                mutation.Id,
                new
                {
                    brokerAccountId,
                    brokerProfileId = normalized.BrokerProfileId,
                    brokerId,
                    normalized.Server,
                    normalized.MaskedLogin,
                    normalized.BindingFingerprint,
                    environment = "demo",
                    state = "pending",
                    credentialState = "absent"
                },
                YO4X.Audit.AuditCategory.Operations,
                YO4X.Audit.AuditOutcome.Succeeded,
                CreateUserAuditContext(
                    actor,
                    user,
                    metadata,
                    resourceVersionAfter: view.Version),
                cancellationToken).ConfigureAwait(false);
            await CompleteMutationAsync(transaction, mutation.Id, 201, view, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return view;
        }
    }

    private static CreateBrokerAccount NormalizeBrokerAccountRegistration(
        CreateBrokerAccount request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BrokerProfileId == Guid.Empty
            || !Enum.IsDefined(request.Environment)
            || request.Environment != BrokerAccountEnvironment.Demo)
        {
            throw InvalidBrokerAccountRegistration();
        }

        string server = NormalizeBrokerServer(request.Server);
        string maskedLogin = NormalizeMaskedLogin(request.MaskedLogin);
        string bindingFingerprint = request.BindingFingerprint?.Trim().ToLowerInvariant()
            ?? string.Empty;
        if (!BindingFingerprintPattern().IsMatch(bindingFingerprint))
        {
            throw InvalidBrokerAccountRegistration();
        }

        return request with
        {
            Server = server,
            MaskedLogin = maskedLogin,
            BindingFingerprint = bindingFingerprint,
            Environment = BrokerAccountEnvironment.Demo
        };
    }

    private static string NormalizeBrokerServer(string? value)
    {
        string normalized = value?.Trim().Normalize(NormalizationForm.FormC) ?? string.Empty;
        if (normalized.Length is < 1 or > 500 || normalized.Any(char.IsControl))
        {
            throw InvalidBrokerAccountRegistration();
        }

        return normalized;
    }

    private static string NormalizeMaskedLogin(string? value)
    {
        string normalized = value?.Trim().Normalize(NormalizationForm.FormC) ?? string.Empty;
        if (!MaskedLoginPattern().IsMatch(normalized))
        {
            throw InvalidBrokerAccountRegistration();
        }

        return normalized;
    }

    private static DomainException InvalidBrokerAccountRegistration() => new(
        "BROKER_ACCOUNT_REGISTRATION_INVALID",
        "The broker-account registration is invalid.");

    private static AuthorizationDeniedException BrokerProfileNotApproved() => new(
        "BROKER_PROFILE_NOT_APPROVED",
        "The broker profile and server are not approved for demo registration.");

    [GeneratedRegex("^[*]{1,96}[0-9]{0,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex MaskedLoginPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex BindingFingerprintPattern();
}
