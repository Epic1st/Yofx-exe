using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Deployments;
using YO4X.Persistence.Postgres;

namespace YO4X.ControlPlane.Postgres;

public sealed partial class PostgresControlPlaneApplication
{
    private const string DeploymentValidationSql = """
        select
            account.server,
            account.environment,
            account.account_mode,
            account.trading_allowed,
            account.broker_hosted_stop_loss,
            account.broker_hosted_take_profit,
            account.supports_position_query,
            account.supports_order_query,
            account.supports_deal_history,
            account.capability_observed_at,
            account.capability_valid_until,
            account.capability_evidence_sha256,
            account.credential_state,
            account.state,
            account.binding_fingerprint,
            account.dedicated_cloud_use,
            account.manual_or_external_trading_detected,
            profile.id,
            profile.state,
            strategy.id,
            strategy.package_sha256,
            strategy.state,
            risk.id,
            risk.policy_digest,
            risk.state,
            risk.effective_at,
            gateway.id,
            gateway.sha256,
            gateway.state,
            gateway.signature_state,
            gateway.licence_evidence <> '{}'::jsonb,
            gateway.network_evidence <> '{}'::jsonb,
            compatibility.evidence_sha256,
            compatibility.completed_at,
            account.broker_id,
            strategy.strategy_id,
            clock_timestamp(),
            source_binding.id,
            source_binding.verification_evidence_sha256,
            source_binding.verification_signature_algorithm,
            source_binding.verification_signature_sha256,
            source_binding.verification_signing_key_id
        from operations.broker_accounts as account
        left join governance.broker_profiles as profile
          on profile.broker_id = account.broker_id and profile.id = account.broker_profile_id
        left join governance.strategy_versions as strategy
          on strategy.tenant_id = account.tenant_id and strategy.id = @strategy_version_id
        left join governance.strategy_version_source_bindings as source_binding
          on source_binding.tenant_id = strategy.tenant_id
         and source_binding.strategy_version_id = strategy.id
         and source_binding.strategy_package_sha256 = strategy.package_sha256
        left join governance.risk_policy_versions as risk
          on risk.tenant_id = account.tenant_id and risk.id = @risk_policy_version_id
        left join governance.gateway_artifacts as gateway
          on gateway.sha256 = @gateway_digest
        left join lateral
        (
            select run.evidence_sha256, run.completed_at
            from governance.compatibility_test_runs as run
            where run.broker_profile_id = profile.id
              and run.gateway_artifact_id = gateway.id
              and run.state = 'passed'
              and run.evidence_sha256 is not null
              and run.completed_at is not null
            order by run.completed_at desc, run.id desc
            limit 1
        ) as compatibility on true
        where account.tenant_id = @tenant_id
          and account.user_id = @user_id
          and account.id = @account_id
        """;

    public async Task<IReadOnlyList<string>> ValidateDeploymentAsync(
        UserActor actor,
        ValidateDeployment request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Configuration);
        (var transaction, _) = await BeginAuthorizedAsync(actor, Guid.CreateVersion7(), cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            DeploymentValidationResult result = await ValidateDeploymentConfigurationAsync(
                transaction,
                actor,
                request.Configuration,
                null,
                false,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result.Findings;
        }
    }

    private async Task<DeploymentValidationResult> ValidateDeploymentConfigurationAsync(
        TenantPostgresTransaction transaction,
        UserActor actor,
        DeploymentConfiguration configuration,
        Guid? deploymentId,
        bool lockAccount,
        CancellationToken cancellationToken)
    {
        var findings = new List<string>();
        bool gatewayConfigured = IsSha256(options.ApprovedGatewayDigest);
        bool regionConfigured = !string.IsNullOrWhiteSpace(options.ApprovedRegion);
        bool brokerServerConfigured = !string.IsNullOrWhiteSpace(options.ApprovedBrokerServer);
        bool brokerProfileConfigured = options.ApprovedBrokerProfileId is { } approvedProfileId
            && approvedProfileId != Guid.Empty;
        string? runtimeDigest = options.ApprovedRuntimeImageDigest;
        bool runtimeConfigured = runtimeDigest is { Length: 71 }
            && runtimeDigest.StartsWith("sha256:", StringComparison.Ordinal)
            && IsSha256(runtimeDigest[7..]);
        if (!gatewayConfigured)
        {
            findings.Add("U0_GATEWAY_NOT_CONFIGURED");
        }

        if (!regionConfigured)
        {
            findings.Add("U0_REGION_NOT_CONFIGURED");
        }

        if (!brokerServerConfigured)
        {
            findings.Add("U0_BROKER_SERVER_NOT_CONFIGURED");
        }

        if (!brokerProfileConfigured)
        {
            findings.Add("U0_BROKER_PROFILE_NOT_CONFIGURED");
        }

        if (!runtimeConfigured)
        {
            findings.Add("U0_RUNTIME_IMAGE_NOT_CONFIGURED");
        }

        if (options.BrokerCapabilityMaximumAge <= TimeSpan.Zero)
        {
            findings.Add("U0_CAPABILITY_AGE_NOT_CONFIGURED");
        }

        if (gatewayConfigured && regionConfigured)
        {
            findings.AddRange(configuration.ValidateForU0(options.ApprovedGatewayDigest!, options.ApprovedRegion!));
        }
        else
        {
            if (configuration.BrokerAccountId == Guid.Empty
                || configuration.StrategyVersionId == Guid.Empty
                || configuration.RiskPolicyVersionId == Guid.Empty)
            {
                findings.Add("REQUIRED_BINDING_MISSING");
            }

            if (!IsSha256(configuration.GatewayDigest) || !IsSha256(configuration.StrategyPackageDigest))
            {
                findings.Add("INVALID_PACKAGE_DIGEST");
            }

            if (!configuration.DedicatedAccount)
            {
                findings.Add("DEDICATED_ACCOUNT_REQUIRED");
            }

            if (!configuration.HedgingAccount)
            {
                findings.Add("HEDGING_ACCOUNT_REQUIRED");
            }

            if (!configuration.BrokerHostedStopLoss || !configuration.BrokerHostedTakeProfit)
            {
                findings.Add("BROKER_HOSTED_PROTECTION_REQUIRED");
            }

            if (configuration.ManualOrExternalTradingDetected)
            {
                findings.Add("UNEXPECTED_ACCOUNT_ACTIVITY");
            }
        }

        if (configuration.BrokerAccountId == Guid.Empty)
        {
            return new DeploymentValidationResult(Distinct(findings), null, null);
        }

        string gatewayDigest = IsSha256(configuration.GatewayDigest)
            ? configuration.GatewayDigest.ToLowerInvariant()
            : new string('0', 64);
        await using NpgsqlCommand command = transaction.CreateCommand(
            lockAccount ? DeploymentValidationSql + "\nfor update of account" : DeploymentValidationSql);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "user_id", actor.UserId);
        AddUuid(command, "account_id", configuration.BrokerAccountId);
        AddUuid(command, "strategy_version_id", configuration.StrategyVersionId);
        AddUuid(command, "risk_policy_version_id", configuration.RiskPolicyVersionId);
        command.Parameters.AddWithValue("gateway_digest", NpgsqlDbType.Text, gatewayDigest);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            findings.Add("BROKER_ACCOUNT_NOT_FOUND");
            return new DeploymentValidationResult(Distinct(findings), null, null);
        }

        DateTimeOffset? observedAt = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9);
        DateTimeOffset? validUntil = reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10);
        DateTimeOffset? compatibilityCompletedAt = reader.IsDBNull(33)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(33);
        string? capabilityEvidence = reader.IsDBNull(11) ? null : reader.GetString(11);
        bool? dedicatedCloudUse = reader.IsDBNull(15) ? null : reader.GetBoolean(15);
        bool? manualTrading = reader.IsDBNull(16) ? null : reader.GetBoolean(16);
        Guid? brokerProfileId = reader.IsDBNull(17) ? null : reader.GetGuid(17);
        Guid? strategyVersionId = reader.IsDBNull(19) ? null : reader.GetGuid(19);
        string? strategyDigest = reader.IsDBNull(20) ? null : reader.GetString(20);
        Guid? riskPolicyVersionId = reader.IsDBNull(22) ? null : reader.GetGuid(22);
        string? riskDigest = reader.IsDBNull(23) ? null : reader.GetString(23);
        Guid? gatewayArtifactId = reader.IsDBNull(26) ? null : reader.GetGuid(26);
        string? persistedGatewayDigest = reader.IsDBNull(27) ? null : reader.GetString(27);
        DateTimeOffset authorizationNow = reader.GetFieldValue<DateTimeOffset>(36);
        Guid? strategySourceBindingId = reader.IsDBNull(37) ? null : reader.GetGuid(37);
        string? strategyVerificationEvidenceSha256 = reader.IsDBNull(38)
            ? null
            : reader.GetString(38);
        string? strategyVerificationSignatureAlgorithm = reader.IsDBNull(39)
            ? null
            : reader.GetString(39);
        string? strategyVerificationSignatureSha256 = reader.IsDBNull(40)
            ? null
            : reader.GetString(40);
        string? strategyVerificationSigningKeyId = reader.IsDBNull(41)
            ? null
            : reader.GetString(41);

        AddUnless(string.Equals(reader.GetString(1), "demo", StringComparison.Ordinal), findings, "DEMO_ACCOUNT_REQUIRED");
        AddUnless(string.Equals(reader.GetString(13), "active", StringComparison.Ordinal), findings, "BROKER_ACCOUNT_NOT_ACTIVE");
        AddUnless(string.Equals(reader.GetString(12), "ready", StringComparison.Ordinal), findings, "CREDENTIAL_NOT_READY");
        AddUnless(brokerServerConfigured
            && string.Equals(reader.GetString(0), options.ApprovedBrokerServer, StringComparison.Ordinal), findings, "BROKER_SERVER_NOT_APPROVED");
        AddUnless(brokerProfileId is not null
            && brokerProfileConfigured
            && brokerProfileId == options.ApprovedBrokerProfileId
            && string.Equals(reader.GetString(18), "approved", StringComparison.Ordinal), findings, "BROKER_PROFILE_NOT_APPROVED");
        AddUnless(!reader.IsDBNull(2) && string.Equals(reader.GetString(2), "hedging", StringComparison.Ordinal), findings, "HEDGING_ACCOUNT_REQUIRED");
        AddUnless(!reader.IsDBNull(3) && reader.GetBoolean(3), findings, "BROKER_TRADING_NOT_ALLOWED");
        AddUnless(!reader.IsDBNull(4) && reader.GetBoolean(4), findings, "BROKER_HOSTED_STOP_LOSS_REQUIRED");
        AddUnless(!reader.IsDBNull(5) && reader.GetBoolean(5), findings, "BROKER_HOSTED_TAKE_PROFIT_REQUIRED");
        AddUnless(!reader.IsDBNull(6) && reader.GetBoolean(6)
            && !reader.IsDBNull(7) && reader.GetBoolean(7)
            && !reader.IsDBNull(8) && reader.GetBoolean(8), findings, "BROKER_RECONCILIATION_CAPABILITY_REQUIRED");
        AddUnless(dedicatedCloudUse == true, findings, "DEDICATED_ACCOUNT_EVIDENCE_REQUIRED");
        AddUnless(manualTrading == false, findings, "UNEXPECTED_ACCOUNT_ACTIVITY");
        AddUnless(configuration.DedicatedAccount == dedicatedCloudUse, findings, "DEDICATED_ACCOUNT_BINDING_MISMATCH");
        AddUnless(configuration.HedgingAccount
            == (!reader.IsDBNull(2) && string.Equals(reader.GetString(2), "hedging", StringComparison.Ordinal)), findings, "ACCOUNT_MODE_BINDING_MISMATCH");
        AddUnless(configuration.BrokerHostedStopLoss == (!reader.IsDBNull(4) && reader.GetBoolean(4))
            && configuration.BrokerHostedTakeProfit == (!reader.IsDBNull(5) && reader.GetBoolean(5)), findings, "PROTECTION_BINDING_MISMATCH");
        AddUnless(configuration.ManualOrExternalTradingDetected == manualTrading, findings, "ACCOUNT_ACTIVITY_BINDING_MISMATCH");

        AddUnless(observedAt is not null
            && validUntil is not null
            && capabilityEvidence is not null
            && validUntil > authorizationNow
            && options.BrokerCapabilityMaximumAge > TimeSpan.Zero
            && observedAt >= authorizationNow.Subtract(options.BrokerCapabilityMaximumAge)
            && observedAt <= authorizationNow.Add(options.EvidenceFutureClockSkew), findings, "BROKER_CAPABILITY_EVIDENCE_STALE");
        AddUnless(strategyVersionId is not null
            && string.Equals(strategyDigest, configuration.StrategyPackageDigest, StringComparison.OrdinalIgnoreCase), findings, "STRATEGY_PACKAGE_BINDING_INVALID");
        AddUnless(strategyVersionId is not null
            && string.Equals(reader.GetString(21), "demo_approved", StringComparison.Ordinal), findings, "STRATEGY_NOT_DEMO_APPROVED");
        AddUnless(strategySourceBindingId is not null
            && IsSha256(strategyVerificationEvidenceSha256)
            && string.Equals(
                strategyVerificationSignatureAlgorithm,
                "ECDSA_P256_SHA256_DER",
                StringComparison.Ordinal)
            && IsSha256(strategyVerificationSignatureSha256)
            && !string.IsNullOrWhiteSpace(strategyVerificationSigningKeyId),
            findings,
            "STRATEGY_SIGNED_VERIFICATION_NOT_PROVEN");
        AddUnless(riskPolicyVersionId is not null
            && string.Equals(reader.GetString(24), "active", StringComparison.Ordinal)
            && !reader.IsDBNull(25)
            && reader.GetFieldValue<DateTimeOffset>(25) <= authorizationNow, findings, "RISK_POLICY_NOT_ACTIVE");
        AddUnless(gatewayArtifactId is not null
            && gatewayConfigured
            && string.Equals(persistedGatewayDigest, options.ApprovedGatewayDigest, StringComparison.OrdinalIgnoreCase), findings, "GATEWAY_DIGEST_NOT_APPROVED");
        AddUnless(gatewayArtifactId is not null
            && reader.GetString(28) is "demo_canary" or "approved", findings, "GATEWAY_NOT_DEMO_APPROVED");
        AddUnless(gatewayArtifactId is not null
            && string.Equals(reader.GetString(29), "valid", StringComparison.Ordinal), findings, "GATEWAY_SIGNATURE_NOT_VALID");
        AddUnless(gatewayArtifactId is not null && reader.GetBoolean(30) && reader.GetBoolean(31), findings, "GATEWAY_EVIDENCE_INCOMPLETE");
        AddUnless(gatewayArtifactId is not null
            && !reader.IsDBNull(32)
            && compatibilityCompletedAt is not null
            && compatibilityCompletedAt >= authorizationNow.Subtract(options.CompatibilityEvidenceMaximumAge)
            && compatibilityCompletedAt <= authorizationNow.Add(options.EvidenceFutureClockSkew), findings, "GATEWAY_COMPATIBILITY_NOT_PROVEN");

        DeploymentBinding? binding = strategyVersionId is not null
            && riskPolicyVersionId is not null
            && riskDigest is not null
            && gatewayArtifactId is not null
            && persistedGatewayDigest is not null
            && strategySourceBindingId is not null
            && strategyVerificationEvidenceSha256 is not null
            && strategyVerificationSignatureSha256 is not null
            && strategyVerificationSigningKeyId is not null
            && runtimeConfigured
            ? new DeploymentBinding(
                strategyVersionId.Value,
                strategyDigest!,
                strategySourceBindingId.Value,
                strategyVerificationEvidenceSha256,
                strategyVerificationSignatureSha256,
                strategyVerificationSigningKeyId,
                riskPolicyVersionId.Value,
                riskDigest,
                gatewayArtifactId.Value,
                persistedGatewayDigest,
                reader.GetString(14),
                brokerProfileId,
                capabilityEvidence,
                reader.IsDBNull(32) ? null : reader.GetString(32),
                reader.GetGuid(34),
                reader.GetGuid(35),
                runtimeDigest!)
            : null;
        await reader.DisposeAsync().ConfigureAwait(false);
        DeploymentPolicyEvaluation? policyEvaluation = null;
        if (binding is not null)
        {
            policyEvaluation = await EvaluateDeploymentPoliciesAsync(
                transaction,
                actor,
                configuration,
                binding,
                deploymentId,
                cancellationToken).ConfigureAwait(false);
            if (policyEvaluation.Finding is not null)
            {
                findings.Add(policyEvaluation.Finding);
            }

            AddUnless(policyEvaluation.AllowsNewExecution, findings, "EXECUTION_SAFETY_POLICY_BLOCKS_DEPLOYMENT");
        }

        return new DeploymentValidationResult(Distinct(findings), binding, policyEvaluation);
    }

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static DeploymentConfiguration NormalizeDeploymentConfiguration(DeploymentConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!IsSha256(configuration.GatewayDigest)
            || !IsSha256(configuration.StrategyPackageDigest)
            || string.IsNullOrWhiteSpace(configuration.Region)
            || !string.Equals(configuration.Region, configuration.Region.Trim(), StringComparison.Ordinal))
        {
            throw new DomainException(
                "DEPLOYMENT_CONFIGURATION_INVALID",
                "The deployment configuration contains an invalid digest or region.");
        }

        return configuration with
        {
            GatewayDigest = configuration.GatewayDigest.ToLowerInvariant(),
            StrategyPackageDigest = configuration.StrategyPackageDigest.ToLowerInvariant()
        };
    }

    private static void AddUnless(bool condition, List<string> findings, string finding)
    {
        if (!condition)
        {
            findings.Add(finding);
        }
    }

    private static string[] Distinct(IEnumerable<string> findings) => findings
        .Distinct(StringComparer.Ordinal)
        .OrderBy(finding => finding, StringComparer.Ordinal)
        .ToArray();

    private static string CreateBindingEvidence(DeploymentBinding binding) => CanonicalJson.Serialize(new
    {
        binding.AccountBindingFingerprint,
        binding.BrokerProfileId,
        binding.BrokerCapabilityEvidenceSha256,
        binding.GatewayCompatibilityEvidenceSha256,
        binding.RuntimeImageDigest,
        binding.StrategySourceBindingId,
        binding.StrategyVerificationEvidenceSha256,
        binding.StrategyVerificationSignatureSha256,
        binding.StrategyVerificationSigningKeyId,
        validation = "u0-authoritative"
    });

    private sealed record DeploymentValidationResult(
        IReadOnlyList<string> Findings,
        DeploymentBinding? Binding,
        DeploymentPolicyEvaluation? PolicyEvaluation);

    private sealed record DeploymentBinding(
        Guid StrategyVersionId,
        string StrategyPackageDigest,
        Guid StrategySourceBindingId,
        string StrategyVerificationEvidenceSha256,
        string StrategyVerificationSignatureSha256,
        string StrategyVerificationSigningKeyId,
        Guid RiskPolicyVersionId,
        string RiskPolicyDigest,
        Guid GatewayArtifactId,
        string GatewayDigest,
        string AccountBindingFingerprint,
        Guid? BrokerProfileId,
        string? BrokerCapabilityEvidenceSha256,
        string? GatewayCompatibilityEvidenceSha256,
        Guid BrokerId,
        Guid StrategyId,
        string RuntimeImageDigest);
}
