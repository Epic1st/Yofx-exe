using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Deployments;
using YO4X.Persistence.Postgres;

namespace YO4X.ControlPlane.Postgres;

public sealed partial class PostgresControlPlaneApplication
{
    public async Task<AcceptedOperation> RequestBrokerAccountActionAsync(
        UserActor actor,
        Guid brokerAccountId,
        BrokerAccountAction action,
        DeploymentAction request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        request = NormalizeBrokerActionRequest(action, request);
        if (brokerAccountId == Guid.Empty)
        {
            throw new ResourceNotFoundException();
        }

        const bool versionRequired = true;
        if (versionRequired && metadata.ExpectedVersion is null)
        {
            throw new DomainException("EXPECTED_VERSION_REQUIRED", "An expected resource version is required.");
        }

        (var transaction, AuthorizedUser user) = await BeginMutationAuthorizedAsync(actor, metadata.CorrelationId, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            RequireVerifiedUser(user);

            if (action == BrokerAccountAction.RequestCredentialDeletion)
            {
                RequireMultiFactorAssurance(actor);
            }

            string operationType = action switch
            {
                BrokerAccountAction.TestCloudConnection => "broker_account.connection_test",
                BrokerAccountAction.DisableCloudUse => "broker_account.disable",
                BrokerAccountAction.RequestCredentialDeletion => "broker_account.delete",
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown broker account action.")
            };
            MutationLease<AcceptedOperation> mutation = await BeginMutationAsync<object, AcceptedOperation>(
                transaction,
                operationType,
                metadata,
                new { brokerAccountId, action, request, metadata.ExpectedVersion },
                cancellationToken).ConfigureAwait(false);
            if (mutation.Replay is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return mutation.Replay;
            }

            await using NpgsqlCommand read = transaction.CreateCommand(
                """
                select
                    state,
                    credential_state,
                    credential_state in ('ready', 'disabled', 'rotation_pending', 'deletion_pending'),
                    environment,
                    row_version
                from operations.broker_accounts
                where tenant_id = @tenant_id and user_id = @user_id and id = @account_id
                for update
                """);
            AddUuid(read, "tenant_id", actor.TenantId);
            AddUuid(read, "user_id", actor.UserId);
            AddUuid(read, "account_id", brokerAccountId);
            await using NpgsqlDataReader reader = await read.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new ResourceNotFoundException();
            }

            string accountState = reader.GetString(0);
            string credentialState = reader.GetString(1);
            bool hasCredentialReference = reader.GetBoolean(2);
            string environment = reader.GetString(3);
            long version = reader.GetInt64(4);
            await reader.DisposeAsync().ConfigureAwait(false);
            if (metadata.ExpectedVersion is long expectedVersion && expectedVersion != version)
            {
                throw VersionConflict();
            }

            if (accountState == "deleted" || environment != "demo")
            {
                throw new ResourceConflictException(
                    "BROKER_ACCOUNT_ACTION_NOT_ALLOWED",
                    "The broker account does not allow this operation.");
            }

            DateTimeOffset now = await ReadDatabaseStatementTimeAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
            long submittedVersion = version;
            string requestedTargetState;
            if (action == BrokerAccountAction.TestCloudConnection)
            {
                if (accountState != "active" || credentialState != "ready" || !hasCredentialReference)
                {
                    throw new ResourceConflictException(
                        "CREDENTIAL_NOT_READY",
                        "A connection test requires an active account with a ready cloud credential.");
                }

                requestedTargetState = "active:ready";
            }
            else
            {
                if (action == BrokerAccountAction.RequestCredentialDeletion && !hasCredentialReference)
                {
                    throw new ResourceConflictException(
                        "CREDENTIAL_NOT_PRESENT",
                        "No cloud credential reference exists for deletion.");
                }

                // Disabling/deleting is an immediate control-plane denial, not
                // a claim that a vault operation has completed. Revoking any
                // open ingestion grant also prevents a concurrent completion
                // from restoring the credential to ready.
                await RevokeOpenCredentialGrantsAsync(
                    transaction,
                    actor.TenantId,
                    brokerAccountId,
                    cancellationToken).ConfigureAwait(false);
                const string nextAccountState = "disabled";
                string nextCredentialState = action switch
                {
                    BrokerAccountAction.DisableCloudUse when credentialState == "deletion_pending" => "deletion_pending",
                    BrokerAccountAction.DisableCloudUse when credentialState == "deleted" => "deleted",
                    BrokerAccountAction.DisableCloudUse when hasCredentialReference => "disabled",
                    BrokerAccountAction.DisableCloudUse => "absent",
                    BrokerAccountAction.RequestCredentialDeletion => "deletion_pending",
                    _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown broker account action.")
                };
                requestedTargetState = action == BrokerAccountAction.RequestCredentialDeletion
                    ? "disabled:deleted"
                    : $"{nextAccountState}:{nextCredentialState}";
                await using NpgsqlCommand update = transaction.CreateCommand(
                    """
                    update operations.broker_accounts
                    set state = @account_state,
                        credential_state = @credential_state,
                        updated_at = @now,
                        row_version = row_version + 1
                    where tenant_id = @tenant_id and user_id = @user_id and id = @account_id and row_version = @version
                    returning row_version
                    """);
                update.Parameters.AddWithValue("account_state", NpgsqlDbType.Text, nextAccountState);
                update.Parameters.AddWithValue("credential_state", NpgsqlDbType.Text, nextCredentialState);
                update.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
                AddUuid(update, "tenant_id", actor.TenantId);
                AddUuid(update, "user_id", actor.UserId);
                AddUuid(update, "account_id", brokerAccountId);
                update.Parameters.AddWithValue("version", NpgsqlDbType.Bigint, version);
                submittedVersion = (long)(await update.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw VersionConflict());
            }

            Guid operationId = Guid.CreateVersion7();
            await InsertUserOperationAsync(
                transaction,
                actor,
                operationId,
                operationType,
                "broker_account",
                brokerAccountId,
                mutation.Id,
                metadata.ExpectedVersion,
                submittedVersion,
                requestedTargetState,
                request.ReasonCode,
                now,
                null,
                cancellationToken).ConfigureAwait(false);
            var accepted = Accepted(operationId, submittedVersion, metadata.CorrelationId);
            await AppendMutationEvidenceAsync(
                transaction,
                $"{operationType}.requested",
                "broker_account",
                brokerAccountId,
                request.ReasonCode,
                mutation.Id,
                new
                {
                    operationId,
                    brokerAccountId,
                    operationType,
                    reasonCode = request.ReasonCode,
                    submittedVersion
                },
                YO4X.Audit.AuditCategory.Operations,
                YO4X.Audit.AuditOutcome.Accepted,
                CreateUserAuditContext(actor, user, metadata, version, submittedVersion),
                cancellationToken).ConfigureAwait(false);
            await CompleteMutationAsync(transaction, mutation.Id, 202, accepted, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return accepted;
        }
    }

    public async Task<AcceptedOperation> RequestDeploymentActionAsync(
        UserActor actor,
        Guid deploymentId,
        DeploymentState requestedState,
        DeploymentAction request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        request = NormalizeDeploymentActionRequest(requestedState, request);
        if (deploymentId == Guid.Empty)
        {
            throw new ResourceNotFoundException();
        }

        if (metadata.ExpectedVersion is null)
        {
            throw new DomainException("EXPECTED_VERSION_REQUIRED", "An expected resource version is required.");
        }

        string operationType = requestedState switch
        {
            DeploymentState.Starting => "deployment.start",
            DeploymentState.CloseOnly => "deployment.close_only",
            DeploymentState.StopAfterFlat => "deployment.stop_after_flat",
            _ => throw new DomainException("DEPLOYMENT_ACTION_NOT_ALLOWED", "The requested deployment action is not supported.")
        };
        (var transaction, AuthorizedUser user) = await BeginMutationAuthorizedAsync(actor, metadata.CorrelationId, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            RequireVerifiedUser(user);

            MutationLease<AcceptedOperation> mutation = await BeginMutationAsync<object, AcceptedOperation>(
                transaction,
                operationType,
                metadata,
                new { deploymentId, requestedState, request, metadata.ExpectedVersion },
                cancellationToken).ConfigureAwait(false);
            if (mutation.Replay is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return mutation.Replay;
            }

            DeploymentMutationSnapshot snapshot = await ReadDeploymentMutationSnapshotAsync(
                transaction,
                actor,
                deploymentId,
                requestedState != DeploymentState.Starting,
                cancellationToken).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException();
            if (snapshot.Version != metadata.ExpectedVersion.Value)
            {
                throw VersionConflict();
            }

            DeploymentPolicyEvaluation? currentPolicyEvaluation = null;
            if (requestedState == DeploymentState.Starting)
            {
                if (snapshot.State != "ready")
                {
                    throw InvalidDeploymentTransition();
                }

                DeploymentValidationResult validation = await ValidateDeploymentConfigurationAsync(
                    transaction,
                    actor,
                    snapshot.Configuration,
                    deploymentId,
                    true,
                    cancellationToken).ConfigureAwait(false);
                if (validation.Findings.Count != 0 || validation.Binding is null
                    || validation.PolicyEvaluation is null
                    || validation.Binding.RiskPolicyDigest != snapshot.RiskPolicyDigest
                    || validation.Binding.GatewayArtifactId != snapshot.GatewayArtifactId
                    || !string.Equals(
                        validation.Binding.RuntimeImageDigest,
                        snapshot.RuntimeImageDigest,
                        StringComparison.Ordinal)
                    || validation.Binding.StrategyPackageDigest != snapshot.Configuration.StrategyPackageDigest
                    || !FixedTimeEquals(
                        snapshot.Configuration.ConfigurationHash,
                        snapshot.ConfigurationSha256)
                    || !FixedTimeEquals(
                        Sha256Utf8(CreateBindingEvidence(validation.Binding)),
                        snapshot.BindingEvidenceSha256))
                {
                    throw new DomainException(
                        "DEPLOYMENT_REVALIDATION_FAILED",
                        string.Join(',', validation.Findings.Append("FROZEN_BINDING_REVALIDATION_FAILED").Distinct()));
                }

                currentPolicyEvaluation = validation.PolicyEvaluation;
            }
            else if (snapshot.State is "stopped" or "revoked" or "expired")
            {
                throw InvalidDeploymentTransition();
            }

            string persistedState = requestedState switch
            {
                DeploymentState.Starting => "starting",
                DeploymentState.CloseOnly => "close_only",
                DeploymentState.StopAfterFlat => "stop_after_flat",
                _ => throw new InvalidOperationException("Unsupported deployment action.")
            };
            DateTimeOffset now = await ReadDatabaseStatementTimeAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
            long submittedVersion = snapshot.Version;
            if (!string.Equals(snapshot.State, persistedState, StringComparison.Ordinal))
            {
                await using NpgsqlCommand update = transaction.CreateCommand(
                    """
                    update operations.deployments
                    set desired_state = @desired_state,
                        fence_generation = fence_generation + @generation_increment,
                        updated_at = @now,
                        row_version = row_version + 1
                    where tenant_id = @tenant_id and user_id = @user_id and id = @deployment_id and row_version = @version
                    returning row_version
                    """);
                update.Parameters.AddWithValue("desired_state", NpgsqlDbType.Text, persistedState);
                update.Parameters.AddWithValue(
                    "generation_increment",
                    NpgsqlDbType.Bigint,
                    requestedState == DeploymentState.Starting ? 1L : 0L);
                update.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
                AddUuid(update, "tenant_id", actor.TenantId);
                AddUuid(update, "user_id", actor.UserId);
                AddUuid(update, "deployment_id", deploymentId);
                update.Parameters.AddWithValue("version", NpgsqlDbType.Bigint, snapshot.Version);
                submittedVersion = (long)(await update.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw VersionConflict());
            }

            Guid operationId = Guid.CreateVersion7();
            await InsertUserOperationAsync(
                transaction,
                actor,
                operationId,
                operationType,
                "deployment",
                deploymentId,
                mutation.Id,
                metadata.ExpectedVersion,
                submittedVersion,
                requestedState switch
                {
                    DeploymentState.Starting => "running",
                    DeploymentState.CloseOnly => "close_only",
                    DeploymentState.StopAfterFlat => "stopped",
                    _ => throw new InvalidOperationException("Unsupported deployment action.")
                },
                request.ReasonCode,
                now,
                currentPolicyEvaluation,
                cancellationToken).ConfigureAwait(false);
            if (currentPolicyEvaluation is not null)
            {
                await PersistUserPolicyEvaluationAsync(
                    transaction,
                    actor,
                    mutation.Id,
                    "deployment.start",
                    deploymentId,
                    currentPolicyEvaluation,
                    now,
                    cancellationToken).ConfigureAwait(false);
            }

            var accepted = Accepted(operationId, submittedVersion, metadata.CorrelationId);
            await AppendMutationEvidenceAsync(
                transaction,
                $"{operationType}.requested",
                "deployment",
                deploymentId,
                request.ReasonCode,
                mutation.Id,
                new
                {
                    operationId,
                    deploymentId,
                    operationType,
                    reasonCode = request.ReasonCode,
                    submittedVersion,
                    effectivePolicyDigest = currentPolicyEvaluation?.EffectiveDigest,
                    policyVersionWatermark = currentPolicyEvaluation?.VersionWatermark,
                    policyInputSha256 = currentPolicyEvaluation?.InputSha256
                },
                YO4X.Audit.AuditCategory.Operations,
                YO4X.Audit.AuditOutcome.Accepted,
                CreateUserAuditContext(
                    actor,
                    user,
                    metadata,
                    snapshot.Version,
                    submittedVersion,
                    currentPolicyEvaluation?.EffectiveDigest,
                    currentPolicyEvaluation?.VersionWatermark,
                    currentPolicyEvaluation?.InputSha256),
                cancellationToken).ConfigureAwait(false);
            await CompleteMutationAsync(transaction, mutation.Id, 202, accepted, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return accepted;
        }
    }

    private static async Task InsertUserOperationAsync(
        TenantPostgresTransaction transaction,
        UserActor actor,
        Guid operationId,
        string operationType,
        string targetType,
        Guid targetId,
        Guid idempotencyRecordId,
        long? expectedVersion,
        long submittedResourceVersion,
        string requestedTargetState,
        string reason,
        DateTimeOffset now,
        DeploymentPolicyEvaluation? policyEvaluation,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into control.user_operations
            (
                id, tenant_id, user_id, session_family_id, operation_type,
                target_type, target_id, state, idempotency_record_id,
                expected_resource_version, submitted_resource_version,
                requested_target_state, reason, correlation_id,
                effective_policy_digest, policy_version_watermark, policy_input_sha256,
                row_version, created_at, updated_at
            )
            values
            (
                @id, @tenant_id, @user_id, @session_id, @operation_type,
                @target_type, @target_id, 'accepted', @idempotency_id,
                @expected_version, @submitted_resource_version,
                @requested_target_state, @reason, @correlation_id,
                @effective_policy_digest, @policy_version_watermark, @policy_input_sha256,
                0, @now, @now
            )
            """);
        AddUuid(command, "id", operationId);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "user_id", actor.UserId);
        AddUuid(command, "session_id", actor.SessionId);
        command.Parameters.AddWithValue("operation_type", NpgsqlDbType.Text, operationType);
        command.Parameters.AddWithValue("target_type", NpgsqlDbType.Text, targetType);
        AddUuid(command, "target_id", targetId);
        AddUuid(command, "idempotency_id", idempotencyRecordId);
        AddNullableLong(command, "expected_version", expectedVersion);
        command.Parameters.AddWithValue(
            "submitted_resource_version",
            NpgsqlDbType.Bigint,
            submittedResourceVersion);
        command.Parameters.AddWithValue(
            "requested_target_state",
            NpgsqlDbType.Text,
            requestedTargetState);
        command.Parameters.AddWithValue("reason", NpgsqlDbType.Text, reason.Trim());
        AddUuid(command, "correlation_id", transaction.Context.CorrelationId);
        command.Parameters.AddWithValue(
            "effective_policy_digest",
            NpgsqlDbType.Text,
            policyEvaluation is null ? DBNull.Value : policyEvaluation.EffectiveDigest);
        command.Parameters.AddWithValue(
            "policy_version_watermark",
            NpgsqlDbType.Text,
            policyEvaluation is null ? DBNull.Value : policyEvaluation.VersionWatermark);
        command.Parameters.AddWithValue(
            "policy_input_sha256",
            NpgsqlDbType.Text,
            policyEvaluation is null ? DBNull.Value : policyEvaluation.InputSha256);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RevokeOpenCredentialGrantsAsync(
        TenantPostgresTransaction transaction,
        Guid tenantId,
        Guid brokerAccountId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.credential_ingestion_grants
            set state = 'revoked',
                reservation_id = null,
                reserved_at = null,
                reservation_expires_at = null,
                cleanup_claim_token = null,
                cleanup_claimed_by = null,
                cleanup_claim_expires_at = null,
                updated_at = greatest(updated_at, clock_timestamp()),
                row_version = row_version + 1
            where tenant_id = @tenant_id
              and broker_account_id = @account_id
              and state in ('active', 'reserved')
            """);
        AddUuid(command, "tenant_id", tenantId);
        AddUuid(command, "account_id", brokerAccountId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DeploymentMutationSnapshot?> ReadDeploymentMutationSnapshotAsync(
        TenantPostgresTransaction transaction,
        UserActor actor,
        Guid deploymentId,
        bool lockDeployment,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                desired_state, row_version, broker_account_id, strategy_version_id,
                risk_policy_version_id, gateway_digest, strategy_package_digest,
                runtime_digest,
                region, dedicated_account, hedging_account,
                broker_hosted_stop_loss, broker_hosted_take_profit,
                manual_or_external_trading_detected, risk_policy_digest,
                gateway_artifact_id, binding_evidence_sha256, configuration_sha256
            from operations.deployments
            where tenant_id = @tenant_id and user_id = @user_id and id = @deployment_id
            """;
        await using NpgsqlCommand command = transaction.CreateCommand(lockDeployment ? sql + "\nfor update" : sql);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "user_id", actor.UserId);
        AddUuid(command, "deployment_id", deploymentId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new DeploymentMutationSnapshot(
            reader.GetString(0),
            reader.GetInt64(1),
            new DeploymentConfiguration(
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(8),
                reader.GetBoolean(9),
                reader.GetBoolean(10),
                reader.GetBoolean(11),
                reader.GetBoolean(12),
                reader.GetBoolean(13)),
            reader.GetString(14),
            reader.GetGuid(15),
            reader.GetString(16),
            reader.GetString(17),
            reader.GetString(7));
    }

    private static AcceptedOperation Accepted(Guid operationId, long version, Guid correlationId) => new(
        operationId,
        new Uri($"/v1/operations/{operationId:D}", UriKind.Relative),
        version,
        correlationId);

    private static DeploymentAction NormalizeBrokerActionRequest(
        BrokerAccountAction action,
        DeploymentAction request)
    {
        DeploymentAction normalized = NormalizeActionRequest(request);
        bool allowed = action switch
        {
            BrokerAccountAction.TestCloudConnection => normalized.ReasonCode is "user_connection_test",
            BrokerAccountAction.DisableCloudUse => normalized.ReasonCode is
                "user_disabled_cloud_use" or "security_concern" or "account_retired",
            BrokerAccountAction.RequestCredentialDeletion => normalized.ReasonCode is
                "user_requested_credential_deletion" or "security_concern" or "account_retired",
            _ => false
        };
        if (!allowed)
        {
            throw new DomainException("REASON_CODE_NOT_ALLOWED", "The reason code is not allowed for this operation.");
        }

        return normalized;
    }

    private static DeploymentAction NormalizeDeploymentActionRequest(
        DeploymentState requestedState,
        DeploymentAction request)
    {
        DeploymentAction normalized = NormalizeActionRequest(request);
        bool allowed = requestedState switch
        {
            DeploymentState.Starting => normalized.ReasonCode is
                "user_started_deployment" or "validation_complete",
            DeploymentState.CloseOnly => normalized.ReasonCode is
                "user_requested_close_only" or "risk_reduction" or "security_concern",
            DeploymentState.StopAfterFlat => normalized.ReasonCode is
                "user_requested_stop_after_flat" or "maintenance" or "strategy_retired",
            _ => false
        };
        if (!allowed)
        {
            throw new DomainException("REASON_CODE_NOT_ALLOWED", "The reason code is not allowed for this operation.");
        }

        return normalized;
    }

    private static DeploymentAction NormalizeActionRequest(DeploymentAction request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ReasonCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WrittenReason);
        string reasonCode = request.ReasonCode.Trim().ToLowerInvariant();
        if (reasonCode.Length is < 1 or > 64
            || reasonCode.Any(character => character is not (>= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_' or '-' or '.'))
            || request.WrittenReason.Trim().Length > 2000)
        {
            throw new DomainException("REASON_INVALID", "The reason is invalid.");
        }

        // Free-form text is deliberately not persisted or hashed. User notes
        // can accidentally contain broker credentials or tokens; the bounded,
        // operation-specific reason code is the durable audit explanation.
        return new DeploymentAction(reasonCode, "[redacted-user-note]");
    }

    private static ResourceConflictException InvalidDeploymentTransition() => new(
        "DEPLOYMENT_TRANSITION_INVALID",
        "The deployment state does not allow this transition.");

    private sealed record DeploymentMutationSnapshot(
        string State,
        long Version,
        DeploymentConfiguration Configuration,
        string RiskPolicyDigest,
        Guid GatewayArtifactId,
        string BindingEvidenceSha256,
        string ConfigurationSha256,
        string RuntimeImageDigest);
}
