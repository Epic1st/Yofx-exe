using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Deployments;

namespace YO4X.ControlPlane.Postgres;

public sealed partial class PostgresControlPlaneApplication
{
    public async Task<DeploymentView> CreateDeploymentAsync(
        UserActor actor,
        CreateDeployment request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Configuration);
        request = request with
        {
            Configuration = NormalizeDeploymentConfiguration(request.Configuration)
        };
        (var transaction, AuthorizedUser user) = await BeginMutationAuthorizedAsync(actor, metadata.CorrelationId, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            RequireVerifiedUser(user);

            MutationLease<DeploymentView> mutation = await BeginMutationAsync<CreateDeployment, DeploymentView>(
                transaction,
                "deployment.create",
                metadata,
                request,
                cancellationToken).ConfigureAwait(false);
            if (mutation.Replay is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return mutation.Replay;
            }

            Guid deploymentId = Guid.CreateVersion7();
            DeploymentValidationResult validation = await ValidateDeploymentConfigurationAsync(
                transaction,
                actor,
                request.Configuration,
                deploymentId,
                true,
                cancellationToken).ConfigureAwait(false);
            if (validation.Findings.Count != 0
                || validation.Binding is null
                || validation.PolicyEvaluation is null)
            {
                throw new DomainException(
                    "DEPLOYMENT_VALIDATION_FAILED",
                    string.Join(',', validation.Findings));
            }

            await using NpgsqlCommand existing = transaction.CreateCommand(
                """
                select 1
                from operations.deployments
                where tenant_id = @tenant_id
                  and broker_account_id = @account_id
                  and desired_state not in ('stopped', 'expired', 'revoked')
                limit 1
                """);
            AddUuid(existing, "tenant_id", actor.TenantId);
            AddUuid(existing, "account_id", request.Configuration.BrokerAccountId);
            if (await existing.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                throw new ResourceConflictException(
                    "ACCOUNT_ALREADY_DEPLOYED",
                    "The broker account already has a non-terminal deployment.");
            }

            DeploymentBinding binding = validation.Binding;
            DeploymentPolicyEvaluation policyEvaluation = validation.PolicyEvaluation;
            DateTimeOffset now = await ReadDatabaseStatementTimeAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
            string bindingEvidence = CreateBindingEvidence(binding);
            await using NpgsqlCommand insert = transaction.CreateCommand(
                """
                insert into operations.deployments
                (
                    id, tenant_id, user_id, broker_account_id,
                    strategy_version_id, strategy_source_binding_id,
                    strategy_verification_evidence_sha256,
                    strategy_verification_signature_sha256,
                    strategy_verification_signing_key_id,
                    risk_policy_version_id, risk_policy_digest,
                    gateway_artifact_id, gateway_digest, runtime_digest, strategy_package_digest,
                    region, dedicated_account, hedging_account,
                    broker_hosted_stop_loss, broker_hosted_take_profit,
                    manual_or_external_trading_detected, binding_evidence,
                    binding_evidence_sha256, creation_effective_policy_digest,
                    creation_policy_version_watermark, creation_policy_input_sha256,
                    configuration_sha256, environment, deployment_mode,
                    desired_state, observed_state, fence_generation, row_version,
                    created_at, updated_at
                )
                values
                (
                    @id, @tenant_id, @user_id, @account_id,
                    @strategy_version_id, @strategy_source_binding_id,
                    @strategy_verification_evidence_sha256,
                    @strategy_verification_signature_sha256,
                    @strategy_verification_signing_key_id,
                    @risk_policy_version_id, @risk_policy_digest,
                    @gateway_artifact_id, @gateway_digest, @runtime_digest, @strategy_package_digest,
                    @region, @dedicated_account, @hedging_account,
                    @broker_hosted_stop_loss, @broker_hosted_take_profit,
                    @manual_trading, @binding_evidence,
                    @binding_evidence_sha256, @effective_policy_digest,
                    @policy_version_watermark, @policy_input_sha256,
                    @configuration_sha256, 'demo', 'cloud_demo',
                    'ready', 'unknown', 0, 0,
                    @now, @now
                )
                """);
            AddUuid(insert, "id", deploymentId);
            AddUuid(insert, "tenant_id", actor.TenantId);
            AddUuid(insert, "user_id", actor.UserId);
            AddUuid(insert, "account_id", request.Configuration.BrokerAccountId);
            AddUuid(insert, "strategy_version_id", binding.StrategyVersionId);
            AddUuid(insert, "strategy_source_binding_id", binding.StrategySourceBindingId);
            insert.Parameters.AddWithValue(
                "strategy_verification_evidence_sha256",
                NpgsqlDbType.Text,
                binding.StrategyVerificationEvidenceSha256);
            insert.Parameters.AddWithValue(
                "strategy_verification_signature_sha256",
                NpgsqlDbType.Text,
                binding.StrategyVerificationSignatureSha256);
            insert.Parameters.AddWithValue(
                "strategy_verification_signing_key_id",
                NpgsqlDbType.Text,
                binding.StrategyVerificationSigningKeyId);
            AddUuid(insert, "risk_policy_version_id", binding.RiskPolicyVersionId);
            insert.Parameters.AddWithValue("risk_policy_digest", NpgsqlDbType.Text, binding.RiskPolicyDigest.ToLowerInvariant());
            AddUuid(insert, "gateway_artifact_id", binding.GatewayArtifactId);
            insert.Parameters.AddWithValue("gateway_digest", NpgsqlDbType.Text, binding.GatewayDigest.ToLowerInvariant());
            insert.Parameters.AddWithValue("runtime_digest", NpgsqlDbType.Text, binding.RuntimeImageDigest);
            insert.Parameters.AddWithValue("strategy_package_digest", NpgsqlDbType.Text, binding.StrategyPackageDigest.ToLowerInvariant());
            insert.Parameters.AddWithValue("region", NpgsqlDbType.Text, request.Configuration.Region);
            insert.Parameters.AddWithValue("dedicated_account", NpgsqlDbType.Boolean, request.Configuration.DedicatedAccount);
            insert.Parameters.AddWithValue("hedging_account", NpgsqlDbType.Boolean, request.Configuration.HedgingAccount);
            insert.Parameters.AddWithValue("broker_hosted_stop_loss", NpgsqlDbType.Boolean, request.Configuration.BrokerHostedStopLoss);
            insert.Parameters.AddWithValue("broker_hosted_take_profit", NpgsqlDbType.Boolean, request.Configuration.BrokerHostedTakeProfit);
            insert.Parameters.AddWithValue("manual_trading", NpgsqlDbType.Boolean, request.Configuration.ManualOrExternalTradingDetected);
            insert.Parameters.AddWithValue("binding_evidence", NpgsqlDbType.Jsonb, bindingEvidence);
            insert.Parameters.AddWithValue("binding_evidence_sha256", NpgsqlDbType.Text, Sha256Utf8(bindingEvidence));
            insert.Parameters.AddWithValue("effective_policy_digest", NpgsqlDbType.Text, policyEvaluation.EffectiveDigest);
            insert.Parameters.AddWithValue("policy_version_watermark", NpgsqlDbType.Text, policyEvaluation.VersionWatermark);
            insert.Parameters.AddWithValue("policy_input_sha256", NpgsqlDbType.Text, policyEvaluation.InputSha256);
            insert.Parameters.AddWithValue("configuration_sha256", NpgsqlDbType.Text, request.Configuration.ConfigurationHash);
            insert.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            try
            {
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (
                exception.SqlState == PostgresErrorCodes.UniqueViolation
                && string.Equals(
                    exception.ConstraintName,
                    "deployments_one_nonterminal_per_account_idx",
                    StringComparison.Ordinal))
            {
                throw new ResourceConflictException(
                    "ACCOUNT_ALREADY_DEPLOYED",
                    "The broker account already has a non-terminal deployment.");
            }

            await PersistUserPolicyEvaluationAsync(
                transaction,
                actor,
                mutation.Id,
                "deployment.create",
                deploymentId,
                policyEvaluation,
                now,
                cancellationToken).ConfigureAwait(false);

            var view = new DeploymentView(
                deploymentId,
                DeploymentMode.CloudDemo,
                DeploymentState.Ready,
                "unknown",
                "unknown",
                0,
                0,
                now);
            await AppendMutationEvidenceAsync(
                transaction,
                "deployment.created",
                "deployment",
                deploymentId,
                metadata.Reason,
                mutation.Id,
                new
                {
                    deploymentId,
                    brokerAccountId = request.Configuration.BrokerAccountId,
                    strategyVersionId = binding.StrategyVersionId,
                    riskPolicyVersionId = binding.RiskPolicyVersionId,
                    gatewayArtifactId = binding.GatewayArtifactId,
                    configurationSha256 = request.Configuration.ConfigurationHash,
                    effectivePolicyDigest = policyEvaluation.EffectiveDigest,
                    policyVersionWatermark = policyEvaluation.VersionWatermark,
                    policyInputSha256 = policyEvaluation.InputSha256,
                    desiredState = "ready"
                },
                YO4X.Audit.AuditCategory.Operations,
                YO4X.Audit.AuditOutcome.Succeeded,
                CreateUserAuditContext(
                    actor,
                    user,
                    metadata,
                    resourceVersionAfter: 0,
                    effectivePolicyDigest: policyEvaluation.EffectiveDigest,
                    policyVersionWatermark: policyEvaluation.VersionWatermark,
                    policyInputSha256: policyEvaluation.InputSha256),
                cancellationToken).ConfigureAwait(false);
            await CompleteMutationAsync(transaction, mutation.Id, 201, view, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return view;
        }
    }
}
