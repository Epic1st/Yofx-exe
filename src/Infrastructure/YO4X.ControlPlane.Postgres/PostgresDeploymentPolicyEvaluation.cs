using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Deployments;
using YO4X.Persistence.Postgres;
using YO4X.Policy;

namespace YO4X.ControlPlane.Postgres;

public sealed partial class PostgresControlPlaneApplication
{
    private async Task<DeploymentPolicyEvaluation> EvaluateDeploymentPoliciesAsync(
        TenantPostgresTransaction transaction,
        UserActor actor,
        DeploymentConfiguration configuration,
        DeploymentBinding binding,
        Guid? deploymentId,
        CancellationToken cancellationToken)
    {
        DeploymentBaselinePolicy baseline = await LoadBaselinePolicyAsync(
            transaction,
            actor.TenantId,
            binding,
            cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                id,
                policy_version,
                scope_type,
                scope_id,
                allow_new_deployment,
                allow_strategy_signals,
                allow_exposure_increase,
                allow_exposure_reduction,
                allow_protection,
                allow_pending_order_cancellation,
                allow_emergency_close,
                lease_mode,
                worker_actions,
                credential_mode,
                package_eligibility,
                policy_digest,
                reason,
                incident_id,
                owner_id,
                authority_expires_at,
                review_deadline,
                signature_algorithm,
                signature_bytes,
                signature_sha256,
                signing_key_id
            from control.execution_safety_policies
            where tenant_id = @tenant_id
              and state in
              (
                  'active',
                  'expiry_review_required',
                  'safe_to_release',
                  'deactivating',
                  'reconciling',
                  'partial'
              )
              and
              (
                  (scope_type = 'global' and scope_id is null)
                  or (scope_type = 'environment' and lower(scope_id) = lower(@environment))
                  or (scope_type = 'region' and lower(scope_id) = lower(@region))
                  or (scope_type = 'broker' and lower(scope_id) = lower(@broker_id))
                  or (scope_type = 'gateway' and lower(scope_id) = lower(@gateway_id))
                  or (scope_type = 'runtime' and lower(scope_id) = lower(@runtime_id))
                  or (scope_type = 'strategy' and lower(scope_id) = lower(@strategy_id))
                  or (scope_type = 'strategy_version' and lower(scope_id) = lower(@strategy_version_id))
                  or (scope_type = 'user' and lower(scope_id) = lower(@user_id))
                  or (scope_type = 'account' and lower(scope_id) = lower(@account_id))
                  or (scope_type = 'deployment' and @deployment_id is not null
                      and lower(scope_id) = lower(@deployment_id))
              )
            order by scope_type, scope_id nulls first, policy_version, id
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        command.Parameters.AddWithValue("environment", NpgsqlDbType.Text, "demo");
        command.Parameters.AddWithValue("region", NpgsqlDbType.Text, configuration.Region);
        command.Parameters.AddWithValue("broker_id", NpgsqlDbType.Text, binding.BrokerId.ToString("D"));
        command.Parameters.AddWithValue("gateway_id", NpgsqlDbType.Text, binding.GatewayArtifactId.ToString("D"));
        command.Parameters.AddWithValue("runtime_id", NpgsqlDbType.Text, binding.RuntimeImageDigest);
        command.Parameters.AddWithValue("strategy_id", NpgsqlDbType.Text, binding.StrategyId.ToString("D"));
        command.Parameters.AddWithValue("strategy_version_id", NpgsqlDbType.Text, binding.StrategyVersionId.ToString("D"));
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Text, actor.UserId.ToString("D"));
        command.Parameters.AddWithValue("account_id", NpgsqlDbType.Text, configuration.BrokerAccountId.ToString("D"));
        command.Parameters.AddWithValue(
            "deployment_id",
            NpgsqlDbType.Text,
            deploymentId is null ? DBNull.Value : deploymentId.Value.ToString("D"));

        var policies = new List<DeploymentApplicablePolicy>();
        string? finding = baseline.Finding;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var vector = new ExecutionSafetyPolicyVector(
                    reader.GetBoolean(4),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6),
                    reader.GetBoolean(7),
                    reader.GetBoolean(8),
                    reader.GetBoolean(9),
                    reader.GetBoolean(10),
                    ParseLeaseMode(reader.GetString(11)),
                    ParseWorkerActions(reader.GetFieldValue<string[]>(12)),
                    ParseCredentialMode(reader.GetString(13)),
                    ParsePackageEligibility(reader.GetString(14)));
                string persistedDigest = reader.GetString(15);
                bool digestValid = string.Equals(
                    vector.ComputeDigest(),
                    persistedDigest,
                    StringComparison.Ordinal);
                string signatureAlgorithm = reader.GetString(21);
                byte[] signature = reader.GetFieldValue<byte[]>(22);
                string signatureSha256 = reader.GetString(23);
                string signingKeyId = reader.GetString(24);
                bool signatureValid = policyTrustStore.Verify(
                    signingKeyId,
                    signatureAlgorithm,
                    signature,
                    signatureSha256,
                    CreateExecutionSafetyPolicySignaturePayload(
                        actor.TenantId,
                        reader.GetGuid(0),
                        reader.GetInt64(1),
                        reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        persistedDigest,
                        reader.GetString(16),
                        reader.IsDBNull(17) ? null : reader.GetGuid(17),
                        reader.GetGuid(18),
                        reader.IsDBNull(19) ? null : reader.GetFieldValue<DateTimeOffset>(19),
                        reader.GetFieldValue<DateTimeOffset>(20)));
                if (!digestValid || !signatureValid)
                {
                    vector = FullyRestrictedPolicy;
                    finding ??= digestValid
                        ? "EXECUTION_SAFETY_POLICY_SIGNATURE_INVALID"
                        : "EXECUTION_SAFETY_POLICY_DIGEST_INVALID";
                }

                policies.Add(new DeploymentApplicablePolicy(
                    reader.GetGuid(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    persistedDigest,
                    vector,
                    signatureAlgorithm,
                    signatureSha256,
                    signingKeyId));
            }
        }

        ExecutionSafetyPolicyVector effective = ExecutionSafetyPolicyVector.Meet(
            new[] { baseline.Vector }.Concat(policies.Select(policy => policy.Vector)));
        string versionWatermark = CanonicalJson.Sha256(new
        {
            Baseline = new
            {
                baseline.Id,
                baseline.Version,
                baseline.Digest,
                baseline.CanonicalInputDigest,
                baseline.SignatureAlgorithm,
                baseline.SignatureSha256,
                baseline.SigningKeyId
            },
            Policies = policies.Select(policy => new
            {
                policy.Id,
                policy.Version,
                policy.Digest,
                policy.ScopeType,
                policy.ScopeId,
                policy.SignatureAlgorithm,
                policy.SignatureSha256,
                policy.SigningKeyId
            }).ToArray()
        });
        string effectiveDigest = effective.ComputeDigest();
        string inputJson = CanonicalJson.Serialize(new
        {
            Environment = "demo",
            configuration.Region,
            UserId = actor.UserId,
            AccountId = configuration.BrokerAccountId,
            binding.BrokerId,
            binding.StrategyId,
            StrategyVersionId = binding.StrategyVersionId,
            GatewayArtifactId = binding.GatewayArtifactId,
            RuntimeImageDigest = binding.RuntimeImageDigest,
            DeploymentId = deploymentId,
            VersionWatermark = versionWatermark,
            EffectiveDigest = effectiveDigest
        });
        string inputSha256 = Sha256Utf8(inputJson);
        string applicablePoliciesJson = CanonicalJson.Serialize(new
        {
            Baseline = new
            {
                baseline.Id,
                baseline.Version,
                baseline.Digest,
                baseline.CanonicalInputDigest,
                baseline.SignatureAlgorithm,
                baseline.SignatureSha256,
                baseline.SigningKeyId,
                Vector = CreatePolicyVectorEvidence(baseline.Vector)
            },
            Overlays = policies.Select(policy => new
            {
                policy.Id,
                policy.Version,
                policy.Digest,
                policy.ScopeType,
                policy.ScopeId,
                policy.SignatureAlgorithm,
                policy.SignatureSha256,
                policy.SigningKeyId,
                Vector = CreatePolicyVectorEvidence(policy.Vector)
            }).ToArray()
        });
        string effectiveVectorJson = CanonicalJson.Serialize(CreatePolicyVectorEvidence(effective));
        bool allowsNewExecution = effective.AllowNewDeployment
            && effective.AllowStrategySignals
            && effective.AllowExposureIncrease
            && effective.LeaseMode == LeaseMode.Normal
            && effective.WorkerActions == WorkerAction.None
            && effective.CredentialMode == CredentialMode.Normal
            && effective.PackageEligibility == PackageEligibility.Eligible;
        string ruleResultsJson = CanonicalJson.Serialize(new
        {
            IntegrityValid = finding is null,
            effective.AllowNewDeployment,
            effective.AllowStrategySignals,
            effective.AllowExposureIncrease,
            LeaseModeNormal = effective.LeaseMode == LeaseMode.Normal,
            WorkerActionsClear = effective.WorkerActions == WorkerAction.None,
            CredentialModeNormal = effective.CredentialMode == CredentialMode.Normal,
            PackageEligible = effective.PackageEligibility == PackageEligibility.Eligible,
            AllowsNewExecution = allowsNewExecution && finding is null,
            Finding = finding
        });
        string evidenceSha256 = CanonicalJson.Sha256(new
        {
            InputSnapshot = JsonNode.Parse(inputJson),
            ApplicablePolicies = JsonNode.Parse(applicablePoliciesJson),
            EffectiveVector = JsonNode.Parse(effectiveVectorJson),
            RuleResults = JsonNode.Parse(ruleResultsJson),
            EffectivePolicyDigest = effectiveDigest,
            PolicyVersionWatermark = versionWatermark,
            InputSha256 = inputSha256
        });
        return new DeploymentPolicyEvaluation(
            effectiveDigest,
            versionWatermark,
            inputSha256,
            allowsNewExecution && finding is null,
            finding,
            inputJson,
            applicablePoliciesJson,
            effectiveVectorJson,
            ruleResultsJson,
            evidenceSha256);
    }

    private static async Task PersistUserPolicyEvaluationAsync(
        TenantPostgresTransaction transaction,
        UserActor actor,
        Guid idempotencyRecordId,
        string decisionType,
        Guid deploymentId,
        DeploymentPolicyEvaluation evaluation,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into control.user_policy_evaluations
            (
                id, tenant_id, user_id, idempotency_record_id, decision_type,
                target_type, target_id, input_snapshot, applicable_policies,
                effective_vector, rule_results, decision, effective_policy_digest,
                policy_version_watermark, input_sha256, evidence_sha256, evaluated_at
            )
            values
            (
                @id, @tenant_id, @user_id, @idempotency_record_id, @decision_type,
                'deployment', @target_id, @input_snapshot, @applicable_policies,
                @effective_vector, @rule_results, @decision, @effective_policy_digest,
                @policy_version_watermark, @input_sha256, @evidence_sha256, @evaluated_at
            )
            """);
        AddUuid(command, "id", Guid.CreateVersion7());
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "user_id", actor.UserId);
        AddUuid(command, "idempotency_record_id", idempotencyRecordId);
        command.Parameters.AddWithValue("decision_type", NpgsqlDbType.Text, decisionType);
        AddUuid(command, "target_id", deploymentId);
        command.Parameters.AddWithValue("input_snapshot", NpgsqlDbType.Jsonb, evaluation.InputSnapshotJson);
        command.Parameters.AddWithValue("applicable_policies", NpgsqlDbType.Jsonb, evaluation.ApplicablePoliciesJson);
        command.Parameters.AddWithValue("effective_vector", NpgsqlDbType.Jsonb, evaluation.EffectiveVectorJson);
        command.Parameters.AddWithValue("rule_results", NpgsqlDbType.Jsonb, evaluation.RuleResultsJson);
        command.Parameters.AddWithValue(
            "decision",
            NpgsqlDbType.Text,
            evaluation.AllowsNewExecution ? "allow" : "deny");
        command.Parameters.AddWithValue("effective_policy_digest", NpgsqlDbType.Text, evaluation.EffectiveDigest);
        command.Parameters.AddWithValue("policy_version_watermark", NpgsqlDbType.Text, evaluation.VersionWatermark);
        command.Parameters.AddWithValue("input_sha256", NpgsqlDbType.Text, evaluation.InputSha256);
        command.Parameters.AddWithValue("evidence_sha256", NpgsqlDbType.Text, evaluation.EvidenceSha256);
        command.Parameters.AddWithValue("evaluated_at", NpgsqlDbType.TimestampTz, evaluatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<DeploymentBaselinePolicy> LoadBaselinePolicyAsync(
        TenantPostgresTransaction transaction,
        Guid tenantId,
        DeploymentBinding binding,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                policy_id, version_number, normalized_policy::text, policy_digest,
                signature_algorithm, signature_bytes, signature_sha256, signing_key_id
            from governance.risk_policy_versions
            where tenant_id = @tenant_id and id = @policy_id and state = 'active'
            """);
        AddUuid(command, "tenant_id", tenantId);
        AddUuid(command, "policy_id", binding.RiskPolicyVersionId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return DeploymentBaselinePolicy.Invalid(
                binding.RiskPolicyVersionId,
                binding.RiskPolicyDigest,
                "RISK_POLICY_EXECUTION_VECTOR_INVALID");
        }

        Guid policyId = reader.GetGuid(0);
        int version = reader.GetInt32(1);
        string normalizedPolicy = reader.GetString(2);
        string persistedDigest = reader.GetString(3);
        string signatureAlgorithm = reader.GetString(4);
        byte[] signature = reader.GetFieldValue<byte[]>(5);
        string signatureSha256 = reader.GetString(6);
        string signingKeyId = reader.GetString(7);
        string canonicalInputDigest;
        ExecutionSafetyPolicyVector vector;
        try
        {
            JsonNode? node = JsonNode.Parse(normalizedPolicy);
            if (node is null || !TryParseBaselineVector(normalizedPolicy, out vector))
            {
                return DeploymentBaselinePolicy.Invalid(
                    binding.RiskPolicyVersionId,
                    persistedDigest,
                    "RISK_POLICY_EXECUTION_VECTOR_INVALID",
                    version);
            }

            canonicalInputDigest = CanonicalJson.Sha256(node);
        }
        catch (JsonException)
        {
            return DeploymentBaselinePolicy.Invalid(
                binding.RiskPolicyVersionId,
                persistedDigest,
                "RISK_POLICY_EXECUTION_VECTOR_INVALID",
                version);
        }

        if (!string.Equals(persistedDigest, binding.RiskPolicyDigest, StringComparison.Ordinal)
            || !string.Equals(canonicalInputDigest, persistedDigest, StringComparison.Ordinal))
        {
            return DeploymentBaselinePolicy.Invalid(
                binding.RiskPolicyVersionId,
                persistedDigest,
                "RISK_POLICY_DIGEST_INVALID",
                version,
                canonicalInputDigest);
        }

        if (!policyTrustStore.Verify(
                signingKeyId,
                signatureAlgorithm,
                signature,
                signatureSha256,
                CreateRiskPolicySignaturePayload(
                    tenantId,
                    binding.RiskPolicyVersionId,
                    policyId,
                    version,
                    persistedDigest)))
        {
            return DeploymentBaselinePolicy.Invalid(
                binding.RiskPolicyVersionId,
                persistedDigest,
                "RISK_POLICY_SIGNATURE_INVALID",
                version,
                canonicalInputDigest,
                signatureAlgorithm,
                signatureSha256,
                signingKeyId);
        }

        return new DeploymentBaselinePolicy(
            binding.RiskPolicyVersionId,
            version,
            persistedDigest,
            canonicalInputDigest,
            vector,
            null,
            signatureAlgorithm,
            signatureSha256,
            signingKeyId);
    }

    private static bool TryParseBaselineVector(
        string normalizedPolicy,
        out ExecutionSafetyPolicyVector vector)
    {
        vector = FullyRestrictedPolicy;
        try
        {
            using JsonDocument document = JsonDocument.Parse(normalizedPolicy);
            JsonElement policy = document.RootElement;
            if (TryGetProperty(policy, "executionSafety", out JsonElement nested)
                || TryGetProperty(policy, "executionSafetyPolicy", out nested)
                || TryGetProperty(policy, "execution_safety", out nested))
            {
                policy = nested;
            }

            if (policy.ValueKind != JsonValueKind.Object
                || !TryReadBoolean(policy, "allowNewDeployment", out bool allowNewDeployment)
                || !TryReadBoolean(policy, "allowStrategySignals", out bool allowStrategySignals)
                || !TryReadBoolean(policy, "allowExposureIncrease", out bool allowExposureIncrease)
                || !TryReadBoolean(policy, "allowExposureReduction", out bool allowExposureReduction)
                || !TryReadBoolean(policy, "allowProtection", out bool allowProtection)
                || !TryReadBoolean(policy, "allowPendingOrderCancellation", out bool allowPendingOrderCancellation)
                || !TryReadBoolean(policy, "allowEmergencyClose", out bool allowEmergencyClose)
                || !TryReadString(policy, "leaseMode", out string leaseMode)
                || !TryReadString(policy, "credentialMode", out string credentialMode)
                || !TryReadString(policy, "packageEligibility", out string packageEligibility)
                || !TryReadWorkerActions(policy, out WorkerAction workerActions))
            {
                return false;
            }

            vector = new ExecutionSafetyPolicyVector(
                allowNewDeployment,
                allowStrategySignals,
                allowExposureIncrease,
                allowExposureReduction,
                allowProtection,
                allowPendingOrderCancellation,
                allowEmergencyClose,
                ParseLeaseMode(leaseMode.ToUpperInvariant()),
                workerActions,
                ParseCredentialMode(credentialMode.ToUpperInvariant()),
                ParsePackageEligibility(packageEligibility.ToUpperInvariant()));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryReadBoolean(JsonElement parent, string name, out bool value)
    {
        value = false;
        if (!TryGetProperty(parent, name, out JsonElement property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryReadString(JsonElement parent, string name, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(parent, name, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return value.Length != 0;
    }

    private static bool TryReadWorkerActions(JsonElement parent, out WorkerAction actions)
    {
        actions = WorkerAction.None;
        if (!TryGetProperty(parent, "workerActions", out JsonElement values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            actions |= value.GetString()?.ToUpperInvariant() switch
            {
                "DRAIN" => WorkerAction.Drain,
                "FENCE" => WorkerAction.Fence,
                "REPLACE" => WorkerAction.Replace,
                "STOP_AFTER_FLAT" or "STOPAFTERFLAT" => WorkerAction.StopAfterFlat,
                _ => throw new InvalidOperationException("The baseline contains an unknown worker action.")
            };
        }

        return true;
    }

    private static bool TryGetProperty(JsonElement parent, string name, out JsonElement value)
    {
        if (parent.TryGetProperty(name, out value))
        {
            return true;
        }

        string pascalName = char.ToUpperInvariant(name[0]) + name[1..];
        return parent.TryGetProperty(pascalName, out value);
    }

    private static ExecutionSafetyPolicyVector FullyRestrictedPolicy { get; } = new(
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        LeaseMode.Revoke,
        WorkerAction.Drain | WorkerAction.Fence | WorkerAction.Replace | WorkerAction.StopAfterFlat,
        CredentialMode.RevokeReference,
        PackageEligibility.Quarantined);

    private static LeaseMode ParseLeaseMode(string value) => value switch
    {
        "NORMAL" => LeaseMode.Normal,
        "RENEW_RESTRICTED" => LeaseMode.RenewRestricted,
        "REVOKE" => LeaseMode.Revoke,
        _ => throw new InvalidOperationException("An unknown lease mode is persisted.")
    };

    private static CredentialMode ParseCredentialMode(string value) => value switch
    {
        "NORMAL" => CredentialMode.Normal,
        "DISABLE_NEW_USE" => CredentialMode.DisableNewUse,
        "REVOKE_REFERENCE" => CredentialMode.RevokeReference,
        _ => throw new InvalidOperationException("An unknown credential mode is persisted.")
    };

    private static PackageEligibility ParsePackageEligibility(string value) => value switch
    {
        "ELIGIBLE" => PackageEligibility.Eligible,
        "NO_NEW_ASSIGNMENT" => PackageEligibility.NoNewAssignment,
        "QUARANTINED" => PackageEligibility.Quarantined,
        _ => throw new InvalidOperationException("An unknown package eligibility is persisted.")
    };

    private static WorkerAction ParseWorkerActions(IEnumerable<string> values)
    {
        WorkerAction result = WorkerAction.None;
        foreach (string value in values)
        {
            result |= value switch
            {
                "DRAIN" => WorkerAction.Drain,
                "FENCE" => WorkerAction.Fence,
                "REPLACE" => WorkerAction.Replace,
                "STOP_AFTER_FLAT" => WorkerAction.StopAfterFlat,
                _ => throw new InvalidOperationException("An unknown worker action is persisted.")
            };
        }

        return result;
    }

    private static object CreatePolicyVectorEvidence(ExecutionSafetyPolicyVector vector) => new
    {
        vector.AllowNewDeployment,
        vector.AllowStrategySignals,
        vector.AllowExposureIncrease,
        vector.AllowExposureReduction,
        vector.AllowProtection,
        vector.AllowPendingOrderCancellation,
        vector.AllowEmergencyClose,
        LeaseMode = vector.LeaseMode.ToString(),
        WorkerActions = new[]
        {
            (WorkerAction.Drain, "Drain"),
            (WorkerAction.Fence, "Fence"),
            (WorkerAction.Replace, "Replace"),
            (WorkerAction.StopAfterFlat, "StopAfterFlat")
        }
        .Where(item => vector.WorkerActions.HasFlag(item.Item1))
        .Select(item => item.Item2)
        .ToArray(),
        CredentialMode = vector.CredentialMode.ToString(),
        PackageEligibility = vector.PackageEligibility.ToString()
    };

    private static string CreateRiskPolicySignaturePayload(
        Guid tenantId,
        Guid versionId,
        Guid policyId,
        int version,
        string policyDigest) => CanonicalJson.Serialize(new
        {
            Contract = "yo4x.risk-policy.v1",
            TenantId = tenantId.ToString("D"),
            VersionId = versionId.ToString("D"),
            PolicyId = policyId.ToString("D"),
            Version = version,
            PolicyDigest = policyDigest
        });

    private static string CreateExecutionSafetyPolicySignaturePayload(
        Guid tenantId,
        Guid policyId,
        long version,
        string scopeType,
        string? scopeId,
        string policyDigest,
        string reason,
        Guid? incidentId,
        Guid ownerId,
        DateTimeOffset? authorityExpiresAt,
        DateTimeOffset reviewDeadline) => CanonicalJson.Serialize(new
        {
            Contract = "yo4x.execution-safety-policy.v1",
            TenantId = tenantId.ToString("D"),
            PolicyId = policyId.ToString("D"),
            Version = version,
            ScopeType = scopeType,
            ScopeId = scopeId,
            PolicyDigest = policyDigest,
            Reason = reason,
            IncidentId = incidentId?.ToString("D"),
            OwnerId = ownerId.ToString("D"),
            AuthorityExpiresAt = authorityExpiresAt?.ToUniversalTime().ToString("O"),
            ReviewDeadline = reviewDeadline.ToUniversalTime().ToString("O")
        });

    private sealed record DeploymentApplicablePolicy(
        Guid Id,
        long Version,
        string ScopeType,
        string? ScopeId,
        string Digest,
        ExecutionSafetyPolicyVector Vector,
        string SignatureAlgorithm,
        string SignatureSha256,
        string SigningKeyId);

    private sealed record DeploymentPolicyEvaluation(
        string EffectiveDigest,
        string VersionWatermark,
        string InputSha256,
        bool AllowsNewExecution,
        string? Finding,
        string InputSnapshotJson,
        string ApplicablePoliciesJson,
        string EffectiveVectorJson,
        string RuleResultsJson,
        string EvidenceSha256);

    private sealed record DeploymentBaselinePolicy(
        Guid Id,
        int Version,
        string Digest,
        string CanonicalInputDigest,
        ExecutionSafetyPolicyVector Vector,
        string? Finding,
        string? SignatureAlgorithm,
        string? SignatureSha256,
        string? SigningKeyId)
    {
        public static DeploymentBaselinePolicy Invalid(
            Guid id,
            string digest,
            string finding,
            int version = 0,
            string? canonicalInputDigest = null,
            string? signatureAlgorithm = null,
            string? signatureSha256 = null,
            string? signingKeyId = null) => new(
                id,
                version,
                digest,
                canonicalInputDigest ?? new string('0', 64),
                FullyRestrictedPolicy,
                finding,
                signatureAlgorithm,
                signatureSha256,
                signingKeyId);
    }
}
