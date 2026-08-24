using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.SecretCoordination;

namespace YO4X.ControlPlane.Postgres;

public sealed partial class PostgresControlPlaneApplication
{
    public async Task<CredentialIngestionSessionView> CreateCredentialIngestionSessionAsync(
        UserActor actor,
        CreateCredentialIngestionSession request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (metadata.ExpectedVersion is null)
        {
            throw new DomainException("EXPECTED_VERSION_REQUIRED", "An expected resource version is required.");
        }

        CredentialIngestionProofIssuer issuer = proofIssuer
            ?? throw new BackendCapabilityUnavailableException("credential-ingestion-proof-issuer");
        Uri ingestionOrigin = options.SecretIngestionOrigin is { IsAbsoluteUri: true } configuredOrigin
            && configuredOrigin.Scheme == Uri.UriSchemeHttps
            && configuredOrigin.PathAndQuery == "/"
            && string.IsNullOrEmpty(configuredOrigin.UserInfo)
            && string.IsNullOrEmpty(configuredOrigin.Fragment)
                ? configuredOrigin
                : throw new BackendCapabilityUnavailableException("credential-ingestion-origin");
        string allowedOrigin = NormalizeHttpsOrigin(request.ClientOrigin);
        string approvedClientOrigin = NormalizeHttpsOrigin(
            options.ApprovedCredentialClientOrigin
            ?? throw new BackendCapabilityUnavailableException("credential-ingestion-client-origin"));
        if (!string.Equals(allowedOrigin, approvedClientOrigin, StringComparison.Ordinal))
        {
            throw new AuthorizationDeniedException(
                "CREDENTIAL_INGESTION_ORIGIN_NOT_APPROVED",
                "Credential ingestion is not allowed from this client origin.");
        }
        if (options.IngestionGrantLifetime <= TimeSpan.Zero
            || options.IngestionGrantLifetime > TimeSpan.FromMinutes(10))
        {
            throw new BackendCapabilityUnavailableException("credential-ingestion-grant-lifetime");
        }

        (var transaction, AuthorizedUser user) = await BeginMutationAuthorizedAsync(actor, metadata.CorrelationId, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            RequireVerifiedUser(user);
            RequireMultiFactorAssurance(actor);

            MutationLease<StoredCredentialGrant> mutation = await BeginMutationAsync<object, StoredCredentialGrant>(
                transaction,
                "broker-account.credential-ingestion-session.create",
                metadata,
                new
                {
                    Request = request with { ClientOrigin = new Uri(allowedOrigin, UriKind.Absolute) },
                    metadata.ExpectedVersion
                },
                cancellationToken).ConfigureAwait(false);

            if (mutation.Replay is not null)
            {
                IssuedCredentialIngestionProof replayProof = issuer.Issue(
                    actor.TenantId,
                    actor.UserId,
                    request.BrokerAccountId,
                    mutation.Replay.GrantId,
                    request.Operation,
                    allowedOrigin,
                    metadata.IdempotencyKey,
                    mutation.Replay.ProofKeyId);
                CredentialIngestionSessionView replay = ToCredentialSession(
                    ingestionOrigin,
                    mutation.Replay,
                    replayProof);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return replay;
            }

            Guid grantId = Guid.CreateVersion7();
            string proofKeyId = issuer.CurrentKeyId;
            IssuedCredentialIngestionProof proof = issuer.Issue(
                actor.TenantId,
                actor.UserId,
                request.BrokerAccountId,
                grantId,
                request.Operation,
                allowedOrigin,
                metadata.IdempotencyKey,
                proofKeyId);

            string credentialState;
            bool hasReference;
            string accountState;
            string environment;
            long accountVersion;
            await using (NpgsqlCommand account = transaction.CreateCommand(
                """
                select
                    credential_state,
                    credential_state in ('ready', 'disabled', 'rotation_pending', 'deletion_pending'),
                    state,
                    environment,
                    row_version
                from operations.broker_accounts
                where tenant_id = @tenant_id and user_id = @user_id and id = @account_id
                for update
                """))
            {
                AddUuid(account, "tenant_id", actor.TenantId);
                AddUuid(account, "user_id", actor.UserId);
                AddUuid(account, "account_id", request.BrokerAccountId);
                await using NpgsqlDataReader reader = await account.ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new ResourceNotFoundException();
                }

                credentialState = reader.GetString(0);
                hasReference = reader.GetBoolean(1);
                accountState = reader.GetString(2);
                environment = reader.GetString(3);
                accountVersion = reader.GetInt64(4);
            }

            if (accountVersion != metadata.ExpectedVersion.Value)
            {
                throw VersionConflict();
            }

            StaleCredentialGrantStatus staleGrants = await ExpireStaleCredentialGrantsAsync(
                transaction,
                actor.TenantId,
                request.BrokerAccountId,
                cancellationToken).ConfigureAwait(false);
            long accountVersionBeforeRecovery = accountVersion;
            string credentialStateBeforeRecovery = credentialState;
            if (!staleGrants.OpenGrantExists)
            {
                string recoveredCredentialState = credentialState switch
                {
                    "ingestion_pending" when !hasReference => "absent",
                    "rotation_pending" when hasReference => "ready",
                    _ => credentialState
                };
                if (!string.Equals(recoveredCredentialState, credentialState, StringComparison.Ordinal))
                {
                    await using NpgsqlCommand recover = transaction.CreateCommand(
                        """
                        update operations.broker_accounts
                        set credential_state = @recovered_state,
                            updated_at = greatest(updated_at, clock_timestamp()),
                            row_version = row_version + 1
                        where tenant_id = @tenant_id
                          and user_id = @user_id
                          and id = @account_id
                          and credential_state = @pending_state
                          and row_version = @expected_version
                        returning row_version
                        """);
                    recover.Parameters.AddWithValue("recovered_state", NpgsqlDbType.Text, recoveredCredentialState);
                    recover.Parameters.AddWithValue("pending_state", NpgsqlDbType.Text, credentialState);
                    AddUuid(recover, "tenant_id", actor.TenantId);
                    AddUuid(recover, "user_id", actor.UserId);
                    AddUuid(recover, "account_id", request.BrokerAccountId);
                    recover.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, accountVersion);
                    object recoveredVersion = await recover.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                        ?? throw VersionConflict();
                    accountVersion = Convert.ToInt64(recoveredVersion, System.Globalization.CultureInfo.InvariantCulture);
                    credentialState = recoveredCredentialState;
                }
            }

            if (staleGrants.ExpiredGrant is { } expiredGrant)
            {
                await AppendMutationEvidenceAsync(
                    transaction,
                    "broker_account.credential_ingestion_session_expired",
                    "credential_ingestion_grant",
                    expiredGrant.Id,
                    "Expired credential-ingestion authority was recovered before issuing new authority.",
                    expiredGrant.Id,
                    new
                    {
                        grantId = expiredGrant.Id,
                        brokerAccountId = request.BrokerAccountId,
                        operation = expiredGrant.Operation,
                        state = "expired",
                        credentialStateBefore = credentialStateBeforeRecovery,
                        credentialStateAfter = credentialState,
                        accountVersionBefore = accountVersionBeforeRecovery,
                        accountVersionAfter = accountVersion
                    },
                    YO4X.Audit.AuditCategory.Operations,
                    YO4X.Audit.AuditOutcome.Succeeded,
                    CreateUserAuditContext(
                        actor,
                        user,
                        metadata,
                        expiredGrant.PreviousVersion,
                        expiredGrant.CurrentVersion),
                    cancellationToken,
                    expiredGrant.ExpiredAt).ConfigureAwait(false);
            }

            bool accountStateEligible = request.Operation switch
            {
                CredentialIngestionOperation.Create => accountState is "pending" or "active",
                CredentialIngestionOperation.Rotate => accountState == "active",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.Operation,
                    "Unknown ingestion operation.")
            };
            if (!string.Equals(environment, "demo", StringComparison.Ordinal)
                || !accountStateEligible)
            {
                throw new ResourceConflictException(
                    "CREDENTIAL_INGESTION_NOT_ALLOWED",
                    "Credential ingestion is not allowed for this broker account.");
            }

            string nextCredentialState;
            switch (request.Operation)
            {
                case CredentialIngestionOperation.Create when credentialState == "absent" && !hasReference:
                    nextCredentialState = "ingestion_pending";
                    break;
                case CredentialIngestionOperation.Rotate when credentialState == "ready" && hasReference:
                    nextCredentialState = "rotation_pending";
                    break;
                case CredentialIngestionOperation.Create:
                case CredentialIngestionOperation.Rotate:
                    throw new ResourceConflictException(
                        "CREDENTIAL_STATE_CONFLICT",
                        "The broker account credential state does not allow this operation.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(request), request.Operation, "Unknown ingestion operation.");
            }

            DateTimeOffset expiresAt;
            DateTimeOffset now;
            await using NpgsqlCommand insert = transaction.CreateCommand(
                """
                insert into control.credential_ingestion_grants
                (
                    id, tenant_id, broker_account_id, operation, allowed_origin,
                    bearer_hash, nonce_hash, proof_key_id, expires_at
                )
                values
                (
                    @id, @tenant_id, @account_id, @operation, @allowed_origin,
                    @bearer_hash, @nonce_hash, @proof_key_id,
                    statement_timestamp() + @grant_lifetime
                )
                returning expires_at, created_at
                """);
            AddUuid(insert, "id", grantId);
            AddUuid(insert, "tenant_id", actor.TenantId);
            AddUuid(insert, "account_id", request.BrokerAccountId);
            insert.Parameters.AddWithValue(
                "operation",
                NpgsqlDbType.Text,
                request.Operation == CredentialIngestionOperation.Create ? "create" : "rotate");
            insert.Parameters.AddWithValue("allowed_origin", NpgsqlDbType.Text, allowedOrigin);
            insert.Parameters.AddWithValue("bearer_hash", NpgsqlDbType.Text, CredentialIngestionProofIssuer.HashProof(proof.Bearer));
            insert.Parameters.AddWithValue("nonce_hash", NpgsqlDbType.Text, CredentialIngestionProofIssuer.HashProof(proof.Nonce));
            insert.Parameters.AddWithValue("proof_key_id", NpgsqlDbType.Text, proofKeyId);
            insert.Parameters.AddWithValue("grant_lifetime", NpgsqlDbType.Interval, options.IngestionGrantLifetime);
            await using (NpgsqlDataReader grantReader = await insert.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (!await grantReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("The database did not return the created credential grant.");
                }

                expiresAt = grantReader.GetFieldValue<DateTimeOffset>(0).ToUniversalTime();
                now = grantReader.GetFieldValue<DateTimeOffset>(1).ToUniversalTime();
            }

            await using NpgsqlCommand update = transaction.CreateCommand(
                """
                update operations.broker_accounts
                set credential_state = @credential_state, updated_at = @now, row_version = row_version + 1
                where tenant_id = @tenant_id and user_id = @user_id and id = @account_id
                  and row_version = @expected_version
                returning row_version
                """);
            update.Parameters.AddWithValue("credential_state", NpgsqlDbType.Text, nextCredentialState);
            update.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            AddUuid(update, "tenant_id", actor.TenantId);
            AddUuid(update, "user_id", actor.UserId);
            AddUuid(update, "account_id", request.BrokerAccountId);
            update.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, accountVersion);
            object updatedVersion = await update.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                ?? throw VersionConflict();
            long finalAccountVersion = Convert.ToInt64(
                updatedVersion,
                System.Globalization.CultureInfo.InvariantCulture);

            var stored = new StoredCredentialGrant(
                grantId,
                actor.TenantId,
                expiresAt,
                proofKeyId);
            await AppendMutationEvidenceAsync(
                transaction,
                "broker_account.credential_ingestion_session_created",
                "broker_account",
                request.BrokerAccountId,
                metadata.Reason,
                mutation.Id,
                new
                {
                    grantId,
                    brokerAccountId = request.BrokerAccountId,
                    operation = request.Operation.ToString(),
                    expiresAt
                },
                YO4X.Audit.AuditCategory.Operations,
                YO4X.Audit.AuditOutcome.Succeeded,
                CreateUserAuditContext(
                    actor,
                    user,
                    metadata,
                    metadata.ExpectedVersion.Value,
                    finalAccountVersion),
                cancellationToken).ConfigureAwait(false);
            // Only public authority metadata, including the non-secret key id,
            // is replayed. Bearer and nonce proofs are deterministically
            // re-derived and never persisted.
            await CompleteMutationAsync(transaction, mutation.Id, 201, stored, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ToCredentialSession(ingestionOrigin, stored, proof);
        }
    }

    private static CredentialIngestionSessionView ToCredentialSession(
        Uri ingestionOrigin,
        StoredCredentialGrant stored,
        IssuedCredentialIngestionProof proof) => new(
            stored.GrantId,
            new Uri(
                ingestionOrigin,
                $"/v1/tenants/{stored.TenantId:D}/credential-ingestion-grants/{stored.GrantId:D}/consume"),
            proof.Bearer,
            proof.Nonce,
            stored.ExpiresAt);

    private static async Task<StaleCredentialGrantStatus> ExpireStaleCredentialGrantsAsync(
        YO4X.Persistence.Postgres.TenantPostgresTransaction transaction,
        Guid tenantId,
        Guid brokerAccountId,
        CancellationToken cancellationToken)
    {
        ExpiredCredentialGrant? expiredGrant = null;
        await using (NpgsqlCommand expire = transaction.CreateCommand(
            """
            update control.credential_ingestion_grants
            set state = 'expired',
                reservation_id = null,
                reserved_at = null,
                reservation_expires_at = null,
                cleanup_claim_token = null,
                cleanup_claimed_by = null,
                cleanup_claim_expires_at = null,
                row_version = row_version + 1,
                updated_at = greatest(updated_at, clock_timestamp())
            where tenant_id = @tenant_id
              and broker_account_id = @account_id
              and state in ('active', 'reserved')
              and expires_at <= clock_timestamp()
            returning id, operation, row_version - 1, row_version, updated_at
            """))
        {
            AddUuid(expire, "tenant_id", tenantId);
            AddUuid(expire, "account_id", brokerAccountId);
            await using NpgsqlDataReader reader = await expire.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                expiredGrant = new ExpiredCredentialGrant(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetFieldValue<DateTimeOffset>(4));
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        "More than one open credential-ingestion grant existed for a broker account.");
                }
            }
        }

        await using NpgsqlCommand active = transaction.CreateCommand(
            """
            select exists
            (
                select 1
                from control.credential_ingestion_grants
                where tenant_id = @tenant_id
                  and broker_account_id = @account_id
                  and state in ('active', 'reserved')
                  and expires_at > clock_timestamp()
            )
            """);
        AddUuid(active, "tenant_id", tenantId);
        AddUuid(active, "account_id", brokerAccountId);
        bool openGrantExists = await active.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
        return new StaleCredentialGrantStatus(openGrantExists, expiredGrant);
    }

    private static string NormalizeHttpsOrigin(Uri origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!origin.IsAbsoluteUri
            || origin.Scheme != Uri.UriSchemeHttps
            || origin.PathAndQuery != "/"
            || !string.IsNullOrEmpty(origin.UserInfo)
            || !string.IsNullOrEmpty(origin.Fragment))
        {
            throw new DomainException(
                "CLIENT_ORIGIN_INVALID",
                "The client origin must be an exact HTTPS origin.");
        }

        return origin.GetLeftPart(UriPartial.Authority);
    }

    private sealed record StoredCredentialGrant(
        Guid GrantId,
        Guid TenantId,
        DateTimeOffset ExpiresAt,
        string ProofKeyId);

    private sealed record StaleCredentialGrantStatus(
        bool OpenGrantExists,
        ExpiredCredentialGrant? ExpiredGrant);

    private sealed record ExpiredCredentialGrant(
        Guid Id,
        string Operation,
        long PreviousVersion,
        long CurrentVersion,
        DateTimeOffset ExpiredAt);
}
