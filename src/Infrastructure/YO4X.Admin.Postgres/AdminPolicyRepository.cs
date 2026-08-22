using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.Persistence.Postgres;
using YO4X.Policy;

namespace YO4X.Admin.Postgres;

internal sealed record ApplicablePolicy(
    Guid Id,
    long Version,
    string ScopeType,
    string? ScopeId,
    string Digest,
    ExecutionSafetyPolicyVector Vector);

internal sealed record AdminPolicyEvaluation(
    ExecutionSafetyPolicyVector EffectiveBeforeRequest,
    ExecutionSafetyPolicyVector EffectiveAfterRequest,
    string EffectiveDigest,
    string VersionWatermark,
    IReadOnlyList<ApplicablePolicy> ApplicablePolicies,
    WorkerActionPlan WorkerPlan,
    string InputSnapshotJson,
    string PolicyVersionsJson,
    string EvidenceSha256);

internal static class AdminPolicyRepository
{
    public static async Task<AdminPolicyEvaluation> EvaluateAsync(
        TenantPostgresTransaction transaction,
        AdminResourceScope resource,
        ExecutionSafetyPolicyVector requestedRestriction,
        bool accountConfirmedFlat,
        bool protectedReductionPathAvailable,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ApplicablePolicy> policies = await LoadApplicableAsync(
            transaction,
            resource,
            cancellationToken).ConfigureAwait(false);
        ExecutionSafetyPolicyVector effectiveBefore = policies.Count == 0
            ? ExecutionSafetyPolicyVector.Unrestricted
            : ExecutionSafetyPolicyVector.Meet(policies.Select(policy => policy.Vector));
        ExecutionSafetyPolicyVector effectiveAfter = effectiveBefore.Meet(requestedRestriction);
        if (!effectiveAfter.IsAtLeastAsRestrictiveAs(effectiveBefore))
        {
            throw new InvalidOperationException("Policy meet unexpectedly weakened an active restriction.");
        }

        WorkerActionPlan plan = WorkerActionPlanner.Plan(
            effectiveAfter,
            new WorkerActionPlanningContext(
                accountConfirmedFlat,
                protectedReductionPathAvailable));
        string versionWatermark = CanonicalJson.Sha256(new
        {
            Policies = policies.Select(policy => new
            {
                policy.Id,
                policy.Version,
                policy.Digest
            }).ToArray()
        });
        string policyVersionsJson = CanonicalJson.Serialize(new
        {
            Items = policies.Select(policy => new
            {
                policy.Id,
                policy.Version,
                policy.Digest,
                policy.ScopeType,
                policy.ScopeId
            }).ToArray(),
            VersionWatermark = versionWatermark
        });
        string inputSnapshotJson = CanonicalJson.Serialize(new
        {
            Resource = new
            {
                resource.Environment,
                resource.Dimensions,
                resource.Version
            },
            EffectiveBefore = effectiveBefore.ToDocument(),
            RequestedRestriction = requestedRestriction.ToDocument(),
            EffectiveAfter = effectiveAfter.ToDocument(),
            EffectiveDigest = effectiveAfter.ComputeDigest(),
            WorkerPlan = new
            {
                Disposition = plan.Disposition.ToString(),
                Steps = plan.Steps.Select(step => step.ToString()).ToArray(),
                Issues = plan.Issues.Select(issue => new { issue.Code, issue.Message }).ToArray()
            }
        });
        return new AdminPolicyEvaluation(
            effectiveBefore,
            effectiveAfter,
            effectiveAfter.ComputeDigest(),
            versionWatermark,
            policies,
            plan,
            inputSnapshotJson,
            policyVersionsJson,
            CanonicalJson.Sha256(new
            {
                Input = System.Text.Json.Nodes.JsonNode.Parse(inputSnapshotJson),
                Versions = System.Text.Json.Nodes.JsonNode.Parse(policyVersionsJson),
                Decision = "approval_required"
            }));
    }

    public static async Task InsertEvaluationAsync(
        TenantPostgresTransaction transaction,
        Guid commandId,
        AdminPolicyEvaluation evaluation,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into control.policy_evaluations
            (
                id,
                tenant_id,
                command_id,
                actor_id,
                input_snapshot,
                policy_versions,
                decision,
                evidence_sha256,
                evaluated_at
            )
            values
            (
                @id,
                @tenant_id,
                @command_id,
                @actor_id,
                @input_snapshot,
                @policy_versions,
                'approval_required',
                @evidence_sha256,
                @evaluated_at
            )
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, Identifiers.NewId());
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("command_id", NpgsqlDbType.Uuid, commandId);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Uuid, transaction.Context.ActorId);
        command.Parameters.AddWithValue("input_snapshot", NpgsqlDbType.Jsonb, evaluation.InputSnapshotJson);
        command.Parameters.AddWithValue("policy_versions", NpgsqlDbType.Jsonb, evaluation.PolicyVersionsJson);
        command.Parameters.AddWithValue("evidence_sha256", NpgsqlDbType.Text, evaluation.EvidenceSha256);
        command.Parameters.AddWithValue("evaluated_at", NpgsqlDbType.TimestampTz, evaluatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ApplicablePolicy>> LoadApplicableAsync(
        TenantPostgresTransaction transaction,
        AdminResourceScope resource,
        CancellationToken cancellationToken)
    {
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
                policy_digest
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
            order by id
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var policies = new List<ApplicablePolicy>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string scopeType = reader.GetString(2);
            string? scopeId = reader.IsDBNull(3) ? null : reader.GetString(3);
            if (!IsApplicable(scopeType, scopeId, resource))
            {
                continue;
            }

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
            if (!string.Equals(vector.ComputeDigest(), persistedDigest, StringComparison.Ordinal))
            {
                throw new ResourceConflictException(
                    "SAFETY_POLICY_DIGEST_INVALID",
                    "An applicable safety policy no longer matches its immutable digest.");
            }

            policies.Add(new ApplicablePolicy(
                reader.GetGuid(0),
                reader.GetInt64(1),
                scopeType,
                scopeId,
                persistedDigest,
                vector));
        }

        return policies.AsReadOnly();
    }

    private static bool IsApplicable(
        string scopeType,
        string? scopeId,
        AdminResourceScope resource)
    {
        if (scopeType == "global")
        {
            return scopeId is null;
        }

        return scopeId is not null
            && resource.Dimensions.TryGetValue(scopeType, out string? resourceScopeId)
            && string.Equals(scopeId, resourceScopeId, StringComparison.OrdinalIgnoreCase);
    }

    private static LeaseMode ParseLeaseMode(string value) => value switch
    {
        "NORMAL" => LeaseMode.Normal,
        "RENEW_RESTRICTED" => LeaseMode.RenewRestricted,
        "REVOKE" => LeaseMode.Revoke,
        _ => throw new InvalidOperationException($"Unknown persisted lease mode: '{value}'.")
    };

    private static CredentialMode ParseCredentialMode(string value) => value switch
    {
        "NORMAL" => CredentialMode.Normal,
        "DISABLE_NEW_USE" => CredentialMode.DisableNewUse,
        "REVOKE_REFERENCE" => CredentialMode.RevokeReference,
        _ => throw new InvalidOperationException($"Unknown persisted credential mode: '{value}'.")
    };

    private static PackageEligibility ParsePackageEligibility(string value) => value switch
    {
        "ELIGIBLE" => PackageEligibility.Eligible,
        "NO_NEW_ASSIGNMENT" => PackageEligibility.NoNewAssignment,
        "QUARANTINED" => PackageEligibility.Quarantined,
        _ => throw new InvalidOperationException($"Unknown persisted package eligibility: '{value}'.")
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
                _ => throw new InvalidOperationException($"Unknown persisted worker action: '{value}'.")
            };
        }

        return result;
    }
}
