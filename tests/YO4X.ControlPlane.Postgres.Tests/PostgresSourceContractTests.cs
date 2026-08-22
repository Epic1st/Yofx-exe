namespace YO4X.ControlPlane.Postgres.Tests;

public sealed class PostgresSourceContractTests
{
    [Fact]
    public void DeploymentInsertPersistsTheRequiredCanonicalBindingEvidenceHash()
    {
        string source = ReadSource("PostgresDeploymentMutations.cs");

        Assert.Contains("binding_evidence_sha256", source, StringComparison.Ordinal);
        Assert.Contains("Sha256Utf8(bindingEvidence)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeploymentStartRevalidatesTheFrozenCanonicalBindingEvidenceHash()
    {
        string source = ReadSource("PostgresUserOperations.cs");

        Assert.Contains("snapshot.Configuration.ConfigurationHash", source, StringComparison.Ordinal);
        Assert.Contains("snapshot.ConfigurationSha256", source, StringComparison.Ordinal);
        Assert.Contains("binding_evidence_sha256", source, StringComparison.Ordinal);
        Assert.Contains("Sha256Utf8(CreateBindingEvidence(validation.Binding))", source, StringComparison.Ordinal);
        Assert.Contains("snapshot.BindingEvidenceSha256", source, StringComparison.Ordinal);
        Assert.Contains("FixedTimeEquals", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CredentialGrantReleaseDelegatesExpiryAndEvidenceToTheAtomicCapability()
    {
        string store = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "PostgresCredentialIngestionGrantStore.cs");
        string migration = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "001_foundation.sql");
        string release = ExtractMethod(store, "ReleaseBeforeWriteAsync");
        string capability = Slice(
            migration,
            "create function control.release_credential_ingestion_grant(",
            "revoke all on function control.release_credential_ingestion_grant(");

        Assert.Contains("control.release_credential_ingestion_grant(", release, StringComparison.Ordinal);
        Assert.Contains("@expected_version", release, StringComparison.Ordinal);
        Assert.DoesNotContain("update control.credential_ingestion_grants", release, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("control.expire_secret_credential_ingestion_grant(", capability, StringComparison.Ordinal);
        Assert.Contains("clock_timestamp()", capability, StringComparison.Ordinal);
        Assert.Contains("target_audit_event_id", capability, StringComparison.Ordinal);
        Assert.Contains("target_outbox_message_id", capability, StringComparison.Ordinal);
    }

    [Fact]
    public void DeploymentStartPersistsItsCurrentPolicyEvaluationEvidence()
    {
        string userOperations = RemoveWhitespace(ReadSource("PostgresUserOperations.cs"));
        string validation = RemoveWhitespace(ReadSource("PostgresDeploymentValidation.cs"));

        Assert.Contains("effective_policy_digest", userOperations, StringComparison.Ordinal);
        Assert.Contains("policy_version_watermark", userOperations, StringComparison.Ordinal);
        Assert.Contains("policy_input_sha256", userOperations, StringComparison.Ordinal);
        Assert.Contains(
            "ValidateDeploymentConfigurationAsync(transaction,actor,snapshot.Configuration,deploymentId,true,cancellationToken)",
            userOperations,
            StringComparison.Ordinal);
        Assert.Contains("currentPolicyEvaluation=validation.PolicyEvaluation", userOperations, StringComparison.Ordinal);
        Assert.Contains("now,currentPolicyEvaluation,cancellationToken", userOperations, StringComparison.Ordinal);
        Assert.Contains(
            "EvaluateDeploymentPoliciesAsync(transaction,actor,configuration,binding,deploymentId,cancellationToken)",
            validation,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DeploymentFreshnessAndPolicyEffectiveTimeUseOneAuthoritativeDatabaseInstant()
    {
        string validation = ReadSource("PostgresDeploymentValidation.cs");
        string normalized = RemoveWhitespace(validation);

        Assert.Contains("strategy.strategy_id,clock_timestamp()", normalized, StringComparison.Ordinal);
        Assert.Contains(
            "DateTimeOffsetauthorizationNow=reader.GetFieldValue<DateTimeOffset>(36)",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains("validUntil>authorizationNow", normalized, StringComparison.Ordinal);
        Assert.Contains(
            "observedAt>=authorizationNow.Subtract(options.BrokerCapabilityMaximumAge)",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "reader.GetFieldValue<DateTimeOffset>(25)<=authorizationNow",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "compatibilityCompletedAt>=authorizationNow.Subtract(options.CompatibilityEvidenceMaximumAge)",
            normalized,
            StringComparison.Ordinal);
        Assert.DoesNotContain("clock.UtcNow", validation, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectReadQueriesEndTheirReaderScopeBeforeCommitting()
    {
        string source = ReadSource("PostgresControlPlaneReads.cs");
        string[] methodNames =
        [
            "GetSessionsAsync",
            "GetBrokerAccountAsync",
            "GetCredentialStateAsync",
            "GetOperationAsync",
            "GetDeploymentActivityAsync"
        ];

        foreach (string methodName in methodNames)
        {
            string body = ExtractMethod(source, methodName);
            int readerDeclaration = body.IndexOf("await using NpgsqlDataReader", StringComparison.Ordinal);
            int commit = readerDeclaration < 0
                ? -1
                : body.IndexOf("await transaction.CommitAsync", readerDeclaration, StringComparison.Ordinal);

            Assert.True(readerDeclaration >= 0, $"{methodName} must own its Npgsql reader.");
            Assert.True(commit > readerDeclaration, $"{methodName} must commit after reading.");
            Assert.True(
                HasClosedLexicalScope(body, readerDeclaration, commit),
                $"{methodName} must dispose its Npgsql reader before committing the transaction.");
        }
    }

    [Fact]
    public void ReadAuthorizationAvoidsU0SerializationWhileMutationsRevalidateUnderTheLock()
    {
        string source = ReadSource("PostgresControlPlaneApplication.cs");
        string authorization = Slice(
            source,
            "private async ValueTask<(TenantPostgresTransaction Transaction, AuthorizedUser User)> BeginAuthorizedAsync",
            "private static void AddUuid");
        int lockGuard = authorization.IndexOf("if (acquireAuthorityLock)", StringComparison.Ordinal);
        int authorityLock = authorization.IndexOf("acquire_u0_authority_lock", StringComparison.Ordinal);
        int authorityRead = authorization.IndexOf("from identity.user_identities", StringComparison.Ordinal);

        Assert.Contains("acquireAuthorityLock: false", authorization, StringComparison.Ordinal);
        Assert.Contains("acquireAuthorityLock: true", authorization, StringComparison.Ordinal);
        Assert.True(lockGuard >= 0, "Only mutation authorization may acquire the U0 authority lock.");
        Assert.True(authorityLock > lockGuard, "The U0 authority lock must be conditional.");
        Assert.True(authorityRead > authorityLock, "The authority lock must precede authoritative identity reads.");
        Assert.Contains("join identity.tenants as tenant", authorization, StringComparison.Ordinal);
        Assert.Contains("tenant.state", authorization, StringComparison.Ordinal);
        Assert.Contains("tenantState, \"active\"", authorization, StringComparison.Ordinal);
        Assert.Contains("session.expires_at > clock_timestamp()", authorization, StringComparison.Ordinal);
        Assert.DoesNotContain("expiresAt <= clock.UtcNow", authorization, StringComparison.Ordinal);

        foreach (string mutationSource in new[]
        {
            "PostgresCredentialMutations.cs",
            "PostgresDeploymentMutations.cs",
            "PostgresSessionMutations.cs",
            "PostgresStrategyImportMutations.cs",
            "PostgresUserOperations.cs"
        })
        {
            string mutation = ReadSource(mutationSource);
            Assert.Contains("BeginMutationAuthorizedAsync(", mutation, StringComparison.Ordinal);
            Assert.DoesNotContain("await BeginAuthorizedAsync(", mutation, StringComparison.Ordinal);
        }

        foreach (string readSource in new[]
        {
            "PostgresControlPlaneReads.cs",
            "PostgresDeploymentValidation.cs"
        })
        {
            string read = ReadSource(readSource);
            Assert.Contains("BeginAuthorizedAsync(", read, StringComparison.Ordinal);
            Assert.DoesNotContain("BeginMutationAuthorizedAsync(", read, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("reserve_credential_ingestion_grant")]
    [InlineData("release_credential_ingestion_grant")]
    [InlineData("complete_credential_ingestion_grant")]
    [InlineData("claim_credential_grant_cleanup")]
    [InlineData("complete_credential_grant_cleanup")]
    public void CredentialCapabilitiesTakeTheAuthorityLockBeforeAccountAndGrantRows(string functionName)
    {
        string migration = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "001_foundation.sql");
        string capability = Slice(
            migration,
            $"create function control.{functionName}(",
            $"revoke all on function control.{functionName}(");
        int authorityLock = capability.IndexOf("perform control.acquire_u0_authority_lock()", StringComparison.Ordinal);
        int accountLock = capability.IndexOf("from operations.broker_accounts as account", authorityLock, StringComparison.Ordinal);
        int grantLock = capability.IndexOf("from control.credential_ingestion_grants as ingestion_grant", accountLock, StringComparison.Ordinal);

        Assert.True(authorityLock >= 0, $"{functionName} must join the authority serialization protocol.");
        Assert.True(accountLock > authorityLock, $"{functionName} must lock the account after U0.");
        Assert.True(grantLock > accountLock, $"{functionName} must lock the grant after its account.");
    }

    [Fact]
    public void RuntimeRolesMatchControlPlaneValidationAndSecretReadBoundaries()
    {
        string roles = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Security",
            "least_privilege_roles.sql");
        string controlSection = Slice(
            roles,
            "-- Tenant control API:",
            "-- Admin BFF:");
        string ingestionSection = Slice(
            roles,
            "-- Secret ingestion:",
            "-- Authenticated broker-result ingress");
        string normalizedIngestion = RemoveWhitespaceAroundLineBreaks(ingestionSection);

        Assert.Contains("on governance.compatibility_test_runs to yo4x_control_api", controlSection, StringComparison.Ordinal);
        Assert.Contains("control.execution_safety_policies", controlSection, StringComparison.Ordinal);
        Assert.Contains("control.reserve_credential_ingestion_grant", normalizedIngestion, StringComparison.Ordinal);
        Assert.Contains("control.release_credential_ingestion_grant", normalizedIngestion, StringComparison.Ordinal);
        Assert.Contains("control.complete_credential_ingestion_grant", normalizedIngestion, StringComparison.Ordinal);
        Assert.Contains("revoke all privileges on control.credential_ingestion_grants", normalizedIngestion, StringComparison.Ordinal);
        Assert.DoesNotContain("grant select", ingestionSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grant update", ingestionSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grant insert on audit.audit_events", ingestionSection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CredentialGrantCreationAndTransitionsAreDatabaseOwnedAndRoleExact()
    {
        string migration = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "001_foundation.sql");
        string roles = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Security",
            "least_privilege_roles.sql");
        string table = RemoveWhitespaceAroundLineBreaks(Slice(
            migration,
            "create table control.credential_ingestion_grants",
            "create table control.idempotency_records"));
        string guard = RemoveWhitespaceAroundLineBreaks(Slice(
            migration,
            "create function control.enforce_credential_ingestion_grant_lifecycle",
            "create function control.lock_u0_tenant_authority_mutation"));
        string mutation = ReadSource("PostgresCredentialMutations.cs");
        string normalizedMutation = RemoveWhitespaceAroundLineBreaks(mutation);

        Assert.Contains("state text not null default 'active'", table, StringComparison.Ordinal);
        Assert.Contains("row_version bigint not null default 0", table, StringComparison.Ordinal);
        Assert.Contains("created_at timestamptz not null default statement_timestamp()", table, StringComparison.Ordinal);
        Assert.Contains("updated_at timestamptz not null default statement_timestamp()", table, StringComparison.Ordinal);
        Assert.Contains("expires_at <= created_at + interval '10 minutes'", table, StringComparison.Ordinal);

        Assert.Contains("security definer set search_path = ''", guard, StringComparison.Ordinal);
        Assert.Contains("session_user <> 'yo4x_control_api'", guard, StringComparison.Ordinal);
        Assert.Contains("new.tenant_id is distinct from control.current_tenant_id()", guard, StringComparison.Ordinal);
        Assert.Contains("new.state <> 'active'", guard, StringComparison.Ordinal);
        Assert.Contains("new.row_version <> 0", guard, StringComparison.Ordinal);
        Assert.Contains("new.created_at is distinct from statement_timestamp()", guard, StringComparison.Ordinal);
        Assert.Contains("new.expires_at <= statement_timestamp()", guard, StringComparison.Ordinal);
        Assert.Contains("new.expires_at > statement_timestamp() + interval '10 minutes'", guard, StringComparison.Ordinal);
        Assert.Contains("perform control.acquire_u0_authority_lock()", guard, StringComparison.Ordinal);
        Assert.Contains("account.user_id = control.current_actor_id()", guard, StringComparison.Ordinal);
        Assert.Contains("account.environment = 'demo'", guard, StringComparison.Ordinal);
        Assert.Contains("account.state in ('pending', 'active')", guard, StringComparison.Ordinal);
        Assert.Contains("identity.security_state = 'active'", guard, StringComparison.Ordinal);
        Assert.Contains("tenant.state = 'active'", guard, StringComparison.Ordinal);
        Assert.Contains("new.operation = 'create' and account.state in ('pending', 'active') and account.credential_state = 'absent' and account.credential_reference is null", guard, StringComparison.Ordinal);
        Assert.Contains("new.operation = 'rotate' and account.state = 'active' and account.credential_state = 'ready' and account.credential_reference is not null", guard, StringComparison.Ordinal);
        Assert.Contains("old.allowed_origin, old.bearer_hash, old.nonce_hash, old.expires_at, old.created_at", guard, StringComparison.Ordinal);
        Assert.Contains("new.row_version <> old.row_version + 1", guard, StringComparison.Ordinal);

        Assert.Contains("session_user = 'yo4x_control_api' and old.state in ('active', 'reserved') and new.state in ('expired', 'revoked')", guard, StringComparison.Ordinal);
        Assert.Contains("new.state <> 'expired' or old.expires_at <= lifecycle_now", guard, StringComparison.Ordinal);
        Assert.Contains("session_user = 'yo4x_secret_ingestion'", guard, StringComparison.Ordinal);
        Assert.Contains("old.state = 'reserved' and new.state = 'consumed'", guard, StringComparison.Ordinal);
        Assert.Contains("new.reservation_id, new.reserved_at, new.reservation_expires_at", guard, StringComparison.Ordinal);
        Assert.Contains("old.reservation_expires_at > lifecycle_now and old.expires_at > lifecycle_now", guard, StringComparison.Ordinal);
        Assert.Contains("new.consumed_at >= lifecycle_now - interval '5 minutes'", guard, StringComparison.Ordinal);
        Assert.Contains("session_user = 'yo4x_worker' and old.state in ('active', 'reserved')", guard, StringComparison.Ordinal);
        Assert.Contains("old.cleanup_claim_token is null or old.cleanup_claim_expires_at <= lifecycle_now", guard, StringComparison.Ordinal);
        Assert.Contains("and not ( old.expires_at <= lifecycle_now or (old.state = 'reserved' and old.reservation_expires_at <= lifecycle_now) )", guard, StringComparison.Ordinal);
        Assert.Contains("old.expires_at <= lifecycle_now and old.cleanup_claim_token is not null and new.state = 'expired'", guard, StringComparison.Ordinal);
        Assert.Contains("old.reservation_expires_at <= lifecycle_now and old.cleanup_claim_token is not null and new.state = 'active'", guard, StringComparison.Ordinal);
        Assert.Contains("before insert or update or delete on control.credential_ingestion_grants", guard, StringComparison.Ordinal);

        string insertGrant = roles.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(RemoveWhitespaceAroundLineBreaks)
            .Single(statement => statement.Contains("grant insert (", StringComparison.OrdinalIgnoreCase)
                && statement.Contains("on control.credential_ingestion_grants to yo4x_control_api", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            "grant insert (id, tenant_id, broker_account_id, operation, allowed_origin, bearer_hash, nonce_hash, expires_at)",
            insertGrant,
            StringComparison.Ordinal);
        foreach (string forbidden in new[]
        {
            "state", "reservation_id", "reserved_at", "cleanup_claim_token",
            "completion_digest", "consumed_at", "row_version", "created_at", "updated_at"
        })
        {
            string insertColumns = Slice(insertGrant, "grant insert (", ") on control.credential_ingestion_grants");
            Assert.DoesNotContain(forbidden, insertColumns, StringComparison.OrdinalIgnoreCase);
        }

        string updateGrant = roles.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(RemoveWhitespaceAroundLineBreaks)
            .Single(statement => statement.Contains("grant update (", StringComparison.OrdinalIgnoreCase)
                && statement.Contains("on control.credential_ingestion_grants to yo4x_control_api", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            "grant update (state, reservation_id, reserved_at, reservation_expires_at, cleanup_claim_token, cleanup_claimed_by, cleanup_claim_expires_at, row_version, updated_at)",
            updateGrant,
            StringComparison.Ordinal);
        Assert.DoesNotContain("completion_digest", updateGrant, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("consumed_at", updateGrant, StringComparison.OrdinalIgnoreCase);
        string[] updateColumns = Slice(
                updateGrant,
                "grant update (",
                ") on control.credential_ingestion_grants")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain("expires_at", updateColumns, StringComparer.OrdinalIgnoreCase);

        Assert.Contains(
            "id, tenant_id, broker_account_id, operation, allowed_origin, bearer_hash, nonce_hash, expires_at ) values",
            normalizedMutation,
            StringComparison.Ordinal);
        Assert.Contains("statement_timestamp() + @grant_lifetime", mutation, StringComparison.Ordinal);
        Assert.Contains("returning expires_at, created_at", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("'active', @expires_at, @now, @now", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("clock.UtcNow", mutation, StringComparison.Ordinal);
        Assert.Contains("expires_at <= clock_timestamp()", mutation, StringComparison.Ordinal);
        Assert.Contains("expires_at > clock_timestamp()", mutation, StringComparison.Ordinal);
        Assert.Contains("updated_at = greatest(updated_at, clock_timestamp())", mutation, StringComparison.Ordinal);

        int recovery = mutation.IndexOf("recoveredCredentialState", StringComparison.Ordinal);
        int insert = mutation.IndexOf("insert into control.credential_ingestion_grants", StringComparison.Ordinal);
        Assert.True(recovery >= 0 && insert > recovery,
            "A stale pending account must be restored to authoritative absent/ready truth before the guarded insert.");
    }

    [Fact]
    public void BrokerAccountRuntimeUpdatesCannotBypassGrantOrBrokerProofAuthority()
    {
        string migration = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "001_foundation.sql");
        string guard = RemoveWhitespaceAroundLineBreaks(Slice(
            migration,
            "create function operations.enforce_broker_account_runtime_transition",
            "create trigger tenants_u0_authority_lock"));
        string indexes = RemoveWhitespaceAroundLineBreaks(Slice(
            migration,
            "create index credential_ingestion_account_idx",
            "create index idempotency_expiry_idx"));

        string declaration = Slice(
            guard,
            "create function operations.enforce_broker_account_runtime_transition",
            "as $$");
        Assert.Contains("security definer", declaration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set row_security = on", declaration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("session_user not in ('yo4x_control_api', 'yo4x_secret_ingestion', 'yo4x_worker')", guard, StringComparison.Ordinal);
        Assert.Contains("if tg_op <> 'UPDATE'", guard, StringComparison.Ordinal);
        Assert.Contains("old.tenant_id is distinct from control.current_tenant_id()", guard, StringComparison.Ordinal);
        Assert.Contains("new.tenant_id is distinct from old.tenant_id", guard, StringComparison.Ordinal);
        Assert.Contains("new.row_version := old.row_version + 1", guard, StringComparison.Ordinal);
        Assert.Contains("new.updated_at := greatest(old.updated_at, statement_timestamp())", guard, StringComparison.Ordinal);
        Assert.Contains("old.binding_fingerprint", guard, StringComparison.Ordinal);
        Assert.Contains("old.capability_evidence_sha256, old.created_at", guard, StringComparison.Ordinal);

        Assert.Contains("control.current_actor_id() is distinct from old.user_id", guard, StringComparison.Ordinal);
        Assert.Contains("ingestion_grant.state in ('active', 'reserved')", guard, StringComparison.Ordinal);
        Assert.Contains("old.credential_state = 'absent' and new.credential_state = 'ingestion_pending'", guard, StringComparison.Ordinal);
        Assert.Contains("old.credential_state = 'ready' and new.credential_state = 'rotation_pending'", guard, StringComparison.Ordinal);
        Assert.Contains("new.state = 'disabled' and not has_open_grant", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("new.state = 'active'", Slice(guard, "if session_user = 'yo4x_control_api'", "elsif session_user = 'yo4x_secret_ingestion'"), StringComparison.Ordinal);

        string ingestion = Slice(
            guard,
            "elsif session_user = 'yo4x_secret_ingestion'",
            "else if control.current_actor_id()");
        Assert.Contains("9fda7b52-620b-4eb9-a34c-632163a6078f", ingestion, StringComparison.Ordinal);
        Assert.Contains("ingestion_grant.state = 'reserved'", ingestion, StringComparison.Ordinal);
        Assert.Contains("ingestion_grant.reservation_expires_at > lifecycle_now", ingestion, StringComparison.Ordinal);
        Assert.Contains("ingestion_grant.expires_at > lifecycle_now", ingestion, StringComparison.Ordinal);
        Assert.Contains("^(azure-kv|aws-sm|gcp-sm|vault)://", ingestion, StringComparison.Ordinal);
        Assert.Contains("new.credential_reference is not null", ingestion, StringComparison.Ordinal);
        Assert.DoesNotContain("new.credential_reference is distinct from old.credential_reference", ingestion, StringComparison.Ordinal);
        Assert.Contains("old.credential_state = 'ingestion_pending'", ingestion, StringComparison.Ordinal);
        Assert.Contains("old.credential_state = 'rotation_pending'", ingestion, StringComparison.Ordinal);

        int workerStart = guard.IndexOf("21e67e5a-daec-46eb-84af-f97244508616", StringComparison.Ordinal);
        int workerEnd = guard.IndexOf("if not (control_transition", workerStart, StringComparison.Ordinal);
        Assert.True(workerStart >= 0 && workerEnd > workerStart);
        string worker = guard[workerStart..workerEnd];
        Assert.Contains("21e67e5a-daec-46eb-84af-f97244508616", worker, StringComparison.Ordinal);
        Assert.Contains("ingestion_grant.cleanup_claim_token is not null", worker, StringComparison.Ordinal);
        Assert.Contains("ingestion_grant.cleanup_claim_expires_at > lifecycle_now", worker, StringComparison.Ordinal);
        Assert.Contains("ingestion_grant.expires_at <= lifecycle_now", worker, StringComparison.Ordinal);
        Assert.Contains("old.state = 'disabled' and new.state = 'disabled'", worker, StringComparison.Ordinal);
        Assert.Contains("old.credential_state = 'deletion_pending' and new.credential_state = 'deleted'", worker, StringComparison.Ordinal);
        Assert.Contains("old.state = 'active' and new.state = 'active'", worker, StringComparison.Ordinal);
        Assert.Contains("old.credential_state = 'rotation_pending' and new.credential_state = 'ready'", worker, StringComparison.Ordinal);

        int u0Trigger = migration.IndexOf("create trigger broker_accounts_u0_authority_lock", StringComparison.Ordinal);
        int runtimeTrigger = migration.IndexOf("create trigger broker_accounts_z_runtime_transition_guard", StringComparison.Ordinal);
        Assert.True(u0Trigger >= 0 && runtimeTrigger > u0Trigger,
            "The broker-account runtime guard must sort after the U0 trigger so authority is serialized first.");
        Assert.Contains("before insert or update or delete on operations.broker_accounts", migration[runtimeTrigger..], StringComparison.Ordinal);
        Assert.Contains(
            "create unique index credential_ingestion_one_open_grant_idx on control.credential_ingestion_grants (tenant_id, broker_account_id) where state in ('active', 'reserved')",
            indexes,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BrokerResultIngressAndProjectionHaveSeparateAuthoritiesAndIdempotentTerminalTruth()
    {
        string migration = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "001_foundation.sql");
        string roles = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Security",
            "least_privilege_roles.sql");
        string resultTable = RemoveWhitespaceAroundLineBreaks(Slice(
            migration,
            "create table operations.user_operation_results",
            "create function operations.reject_user_operation_result_mutation"));
        string projection = RemoveWhitespaceAroundLineBreaks(Slice(
            migration,
            "create function control.apply_confirmed_broker_operation_result",
            "revoke all on function control.apply_confirmed_broker_operation_result"));
        string ingressRole = RemoveWhitespaceAroundLineBreaks(Slice(
            roles,
            "-- Authenticated broker-result ingress",
            "-- Conversion worker:"));
        string workerRole = RemoveWhitespaceAroundLineBreaks(Slice(
            roles,
            "-- Worker:",
            "commit;"));

        Assert.Contains("outcome text not null check (outcome in ('succeeded', 'failed'))", resultTable, StringComparison.Ordinal);
        Assert.Contains("unique (tenant_id, operation_id, dispatch_message_id)", resultTable, StringComparison.Ordinal);
        Assert.Contains("check (outcome <> 'succeeded' or broker_confirmed)", resultTable, StringComparison.Ordinal);
        Assert.Contains(
            "check (outcome <> 'succeeded' or requested_target_state = account_state || ':' || credential_state)",
            resultTable,
            StringComparison.Ordinal);

        Assert.Contains(
            "grant select, insert on operations.user_operation_results to yo4x_runtime_evidence",
            ingressRole,
            StringComparison.Ordinal);
        Assert.DoesNotContain("apply_confirmed_broker_operation_result", ingressRole, StringComparison.Ordinal);
        Assert.DoesNotContain("credential_reference", ingressRole, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grant update", ingressRole, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "grant execute on function control.apply_confirmed_broker_operation_result(uuid, uuid, uuid) to yo4x_worker",
            workerRole,
            StringComparison.Ordinal);
        Assert.Contains("operations.user_operation_results", workerRole, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "insert on operations.user_operation_results",
            workerRole,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains("session_user <> 'yo4x_worker'", projection, StringComparison.Ordinal);
        Assert.Contains("result.outcome = 'succeeded'", projection, StringComparison.Ordinal);
        Assert.Contains("result.broker_confirmed", projection, StringComparison.Ordinal);
        Assert.Contains("result.requested_target_state = result.account_state || ':' || result.credential_state", projection, StringComparison.Ordinal);

        int deletedReplay = projection.IndexOf(
            "account_record.credential_state = 'deleted' and account_record.reference_cleared",
            StringComparison.Ordinal);
        int deletedUpdate = projection.IndexOf("set credential_reference = null", StringComparison.Ordinal);
        Assert.True(deletedReplay >= 0 && deletedUpdate > deletedReplay,
            "An already-confirmed deletion must replay without churning account version.");
        Assert.Contains("and credential_state = 'deletion_pending'", projection, StringComparison.Ordinal);

        int rotationReplay = projection.IndexOf(
            "account_record.credential_state = 'ready'",
            deletedUpdate,
            StringComparison.Ordinal);
        int rotationUpdate = projection.IndexOf("set credential_state = 'ready'", rotationReplay, StringComparison.Ordinal);
        Assert.True(rotationReplay >= 0 && rotationUpdate > rotationReplay,
            "An already-confirmed rotation must replay without churning account version.");
        Assert.Contains("and credential_state = 'rotation_pending'", projection, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversionSourceEvidenceIsTenantBoundContentAddressedAndImmutable()
    {
        string migration = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "001_foundation.sql");
        string corpus = Slice(
            migration,
            "create table governance.strategy_source_corpora",
            "create table governance.strategy_source_files");
        string files = Slice(
            migration,
            "create table governance.strategy_source_files",
            "create function governance.reject_strategy_source_evidence_mutation");
        string immutability = Slice(
            migration,
            "create function governance.reject_strategy_source_evidence_mutation",
            "create table governance.strategy_versions");
        string sourcePolicies = Slice(
            migration,
            "create policy strategy_source_corpus_actor_insert",
            "create policy idempotency_actor_insert");

        Assert.Contains("tenant_id uuid not null references identity.tenants(id)", corpus, StringComparison.Ordinal);
        Assert.Contains("user_id uuid not null", corpus, StringComparison.Ordinal);
        Assert.Contains(
            "foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id)",
            RemoveWhitespaceAroundLineBreaks(corpus),
            StringComparison.Ordinal);
        foreach (string digest in new[] { "corpus_sha256", "manifest_sha256", "report_sha256" })
        {
            Assert.Contains($"{digest} text not null check ({digest} ~ '^[0-9a-f]{{64}}$')", corpus, StringComparison.Ordinal);
        }

        Assert.Contains("manifest_content bytea not null", corpus, StringComparison.Ordinal);
        Assert.Contains("report_content bytea not null", corpus, StringComparison.Ordinal);
        Assert.Contains("manifest_sha256 = encode(pg_catalog.sha256(manifest_content), 'hex')", corpus, StringComparison.Ordinal);
        Assert.Contains("report_sha256 = encode(pg_catalog.sha256(report_content), 'hex')", corpus, StringComparison.Ordinal);
        Assert.Contains("manifest = convert_from(manifest_content, 'UTF8')::jsonb", corpus, StringComparison.Ordinal);
        Assert.Contains("created_at timestamptz not null default statement_timestamp()", corpus, StringComparison.Ordinal);
        Assert.Contains("octet_length(disposition_counts::text) <= 2048", corpus, StringComparison.Ordinal);
        Assert.Contains("manifest_order integer not null check (manifest_order between 0 and 9999)", files, StringComparison.Ordinal);
        Assert.Contains("source_sha256 text not null check (source_sha256 ~ '^[0-9a-f]{64}$')", files, StringComparison.Ordinal);
        Assert.Contains("source_content bytea not null", files, StringComparison.Ordinal);
        Assert.Contains("created_at timestamptz not null default statement_timestamp()", files, StringComparison.Ordinal);
        Assert.Contains("cardinality(entrypoints) <= 64", files, StringComparison.Ordinal);
        Assert.Contains("octet_length(array_to_string(entrypoints, pg_catalog.chr(31))) <= 8192", files, StringComparison.Ordinal);
        Assert.Contains("jsonb_array_length(includes) <= 256", files, StringComparison.Ordinal);
        Assert.Contains("octet_length(includes::text) <= 65536", files, StringComparison.Ordinal);
        Assert.Contains("jsonb_array_length(features) <= 128", files, StringComparison.Ordinal);
        Assert.Contains("octet_length(features::text) <= 65536", files, StringComparison.Ordinal);
        Assert.Contains("jsonb_array_length(findings) <= 256", files, StringComparison.Ordinal);
        Assert.Contains("octet_length(findings::text) <= 131072", files, StringComparison.Ordinal);
        Assert.Contains("check (octet_length(source_content) = byte_length)", files, StringComparison.Ordinal);
        Assert.Contains("check (source_sha256 = encode(pg_catalog.sha256(source_content), 'hex'))", files, StringComparison.Ordinal);
        Assert.Contains("unique (tenant_id, corpus_id, relative_path)", files, StringComparison.Ordinal);
        Assert.Contains("unique (tenant_id, corpus_id, manifest_order)", files, StringComparison.Ordinal);
        Assert.Contains(
            "verification = '{\"demoRuntimeProven\":false,\"metaEditorCompileProven\":false,\"parsedAndTypeChecked\":false,\"referenceParityProven\":false,\"semanticConversionProven\":false,\"staticInventoryCompleted\":true}'::jsonb",
            RemoveWhitespaceAroundLineBreaks(files),
            StringComparison.Ordinal);
        Assert.Contains("before update or delete on governance.strategy_source_corpora", immutability, StringComparison.Ordinal);
        Assert.Contains("before update or delete on governance.strategy_source_files", immutability, StringComparison.Ordinal);
        Assert.Contains(
            "select control.apply_tenant_rls('governance.strategy_source_corpora'::regclass, false)",
            migration,
            StringComparison.Ordinal);
        Assert.Contains(
            "select control.apply_tenant_rls('governance.strategy_source_files'::regclass, false)",
            migration,
            StringComparison.Ordinal);
        Assert.Contains("create policy strategy_source_corpus_actor_insert", sourcePolicies, StringComparison.Ordinal);
        Assert.Contains("create policy strategy_source_corpus_actor_select", sourcePolicies, StringComparison.Ordinal);
        Assert.Contains("create policy strategy_source_file_actor_insert", sourcePolicies, StringComparison.Ordinal);
        Assert.Contains("create policy strategy_source_file_actor_select", sourcePolicies, StringComparison.Ordinal);
        Assert.Contains("with check (user_id = (select control.current_actor_id()))", sourcePolicies, StringComparison.Ordinal);
        Assert.Contains("using (user_id = (select control.current_actor_id()))", sourcePolicies, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversionEvidenceMustExactlyMatchItsBoundManifestAndCommitOnlyWithConsumption()
    {
        string migration = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "001_foundation.sql");
        string corpus = RemoveWhitespaceAroundLineBreaks(Slice(
            migration,
            "create table governance.strategy_source_corpora",
            "create table governance.strategy_source_files"));
        string fileGuard = RemoveWhitespaceAroundLineBreaks(Slice(
            migration,
            "create function governance.authorize_strategy_source_file_insert",
            "create function control.complete_strategy_import_job"));
        string commitGuard = RemoveWhitespaceAroundLineBreaks(Slice(
            migration,
            "create function governance.require_consumed_strategy_source_import",
            "create function control.lock_u0_tenant_authority_mutation"));

        Assert.Contains("(manifest - 'files') = pg_catalog.jsonb_build_object(", corpus, StringComparison.Ordinal);
        foreach (string topLevelBinding in new[]
        {
            "'schemaVersion', schema_version",
            "'analyzerVersion', analyzer_version",
            "'corpusSha256', corpus_sha256",
            "'fileCount', file_count",
            "'totalBytes', total_bytes"
        })
        {
            Assert.Contains(topLevelBinding, corpus, StringComparison.Ordinal);
        }

        Assert.Contains("jsonb_typeof(manifest -> 'files') = 'array'", corpus, StringComparison.Ordinal);
        Assert.Contains("jsonb_array_length(manifest -> 'files') = file_count", corpus, StringComparison.Ordinal);

        Assert.Contains("manifest_file := persisted_corpus.manifest -> 'files' -> new.manifest_order", fileGuard, StringComparison.Ordinal);
        Assert.Contains("new.manifest_order >= persisted_corpus.file_count", fileGuard, StringComparison.Ordinal);
        Assert.Contains("existing_file_count >= persisted_corpus.file_count", fileGuard, StringComparison.Ordinal);
        Assert.Contains("existing_total_bytes + new.byte_length > persisted_corpus.total_bytes", fileGuard, StringComparison.Ordinal);
        Assert.Contains("manifest_file is distinct from pg_catalog.jsonb_build_object(", fileGuard, StringComparison.Ordinal);
        foreach (string fileBinding in new[]
        {
            "'relativePath', new.relative_path",
            "'byteLength', new.byte_length",
            "'sha256', new.source_sha256",
            "'textEncoding', new.text_encoding",
            "'entrypoints', pg_catalog.to_jsonb(new.entrypoints)",
            "'includes', new.includes",
            "'features', new.features",
            "'findings', new.findings",
            "'verification', new.verification"
        })
        {
            Assert.Contains(fileBinding, fileGuard, StringComparison.Ordinal);
        }

        Assert.Contains("when 'expert_or_program' then 'expertOrProgram'", fileGuard, StringComparison.Ordinal);
        Assert.Contains("when 'needs_semantic_validation' then 'needsSemanticValidation'", fileGuard, StringComparison.Ordinal);
        Assert.Contains("when 'needs_source' then 'needsSource'", fileGuard, StringComparison.Ordinal);

        Assert.Contains("session_user <> 'yo4x_conversion_worker'", commitGuard, StringComparison.Ordinal);
        Assert.Contains("control.current_correlation_id() is distinct from completed_job.correlation_id", commitGuard, StringComparison.Ordinal);
        Assert.Contains("completed_job.state <> 'consumed'", commitGuard, StringComparison.Ordinal);
        Assert.Contains("completed_job.reservation_id is distinct from new.reservation_id", commitGuard, StringComparison.Ordinal);
        Assert.Contains("completed_job.corpus_id is distinct from new.id", commitGuard, StringComparison.Ordinal);
        Assert.Contains("create constraint trigger strategy_source_corpus_requires_consumed_job", commitGuard, StringComparison.Ordinal);
        Assert.Contains("deferrable initially deferred", commitGuard, StringComparison.Ordinal);
    }

    [Fact]
    public void StrategyImportCapabilityIsMfaIssuedAndOnlyItsDigestIsPersisted()
    {
        string migration = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "001_foundation.sql");
        string mutation = ReadRepositoryFile(
            "src",
            "Infrastructure",
            "YO4X.ControlPlane.Postgres",
            "PostgresStrategyImportMutations.cs");
        string proofIssuer = ReadRepositoryFile(
            "src",
            "Infrastructure",
            "YO4X.ControlPlane.Postgres",
            "StrategyImportProofIssuer.cs");
        string roles = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Security",
            "least_privilege_roles.sql");
        string jobs = Slice(
            migration,
            "create table control.strategy_import_jobs",
            "create table governance.strategy_source_corpora");
        string jobTransition = Slice(
            migration,
            "create function control.enforce_strategy_import_job_transition",
            "create function governance.reject_strategy_source_evidence_mutation");
        string controlRole = RemoveWhitespaceAroundLineBreaks(Slice(
            roles,
            "-- Tenant control API:",
            "-- Admin BFF:"));

        Assert.Contains("RequireMultiFactorAssurance(actor)", mutation, StringComparison.Ordinal);
        Assert.Contains("RequireVerifiedUser(user)", mutation, StringComparison.Ordinal);
        Assert.Contains("StrategyImportProofIssuer.HashCapability(proof.Capability)", mutation, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.ZeroMemory(capabilitySha256)", mutation, StringComparison.Ordinal);
        string capabilityHash = Slice(
            proofIssuer,
            "public static byte[] HashCapability",
            "private static byte[] DecodeCapability");
        Assert.Contains("byte[] bytes = DecodeCapability(capability)", capabilityHash, StringComparison.Ordinal);
        Assert.Contains("return SHA256.HashData(bytes)", capabilityHash, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.ZeroMemory(bytes)", capabilityHash, StringComparison.Ordinal);
        Assert.Contains("capability_sha256 bytea not null check (octet_length(capability_sha256) = 32)", jobs, StringComparison.Ordinal);
        Assert.DoesNotContain("capability text", jobs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("capability bytea", jobs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "grant insert (id, tenant_id, user_id, correlation_id, source_label, capability_sha256, expires_at) on control.strategy_import_jobs to yo4x_control_api",
            controlRole,
            StringComparison.Ordinal);
        string importInsert = RemoveWhitespaceAroundLineBreaks(Slice(
            mutation,
            "insert into control.strategy_import_jobs",
            "AddUuid(insert, \"id\""));
        Assert.Contains(
            "id, tenant_id, user_id, correlation_id, source_label, capability_sha256, expires_at",
            importInsert,
            StringComparison.Ordinal);
        Assert.DoesNotContain("state", importInsert, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("row_version", importInsert, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "grant select (id, tenant_id, user_id, state, row_version, expires_at, updated_at) on control.strategy_import_jobs to yo4x_control_api",
            controlRole,
            StringComparison.Ordinal);
        string importJobReadGrant = Slice(
            controlRole,
            "grant select (id, tenant_id, user_id, state, row_version, expires_at, updated_at)",
            "on control.strategy_import_jobs to yo4x_control_api");
        Assert.DoesNotContain("capability_sha256", importJobReadGrant, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("source_label", importJobReadGrant, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("corpus_sha256", importJobReadGrant, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correlation_id", importJobReadGrant, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("correlation_id uuid not null check", jobs, StringComparison.Ordinal);
        Assert.Contains("check (expires_at <= created_at + interval '30 minutes')", jobs, StringComparison.Ordinal);
        Assert.Contains(
            "grant update (state, reservation_id, reservation_expires_at, row_version, updated_at) on control.strategy_import_jobs to yo4x_control_api",
            controlRole,
            StringComparison.Ordinal);
        Assert.Contains("session_user = 'yo4x_control_api'", jobTransition, StringComparison.Ordinal);
        Assert.Contains("new.state = 'revoked'", jobTransition, StringComparison.Ordinal);
        Assert.Contains("session_user <> 'yo4x_control_api'", jobTransition, StringComparison.Ordinal);
        Assert.Contains("new.tenant_id is distinct from control.current_tenant_id()", jobTransition, StringComparison.Ordinal);
        Assert.Contains("new.user_id is distinct from control.current_actor_id()", jobTransition, StringComparison.Ordinal);
        Assert.Contains("new.correlation_id is distinct from control.current_correlation_id()", jobTransition, StringComparison.Ordinal);
        Assert.Contains("new.state <> 'active'", jobTransition, StringComparison.Ordinal);
        Assert.Contains("new.row_version <> 0", jobTransition, StringComparison.Ordinal);
        Assert.Contains("new.created_at is distinct from statement_timestamp()", jobTransition, StringComparison.Ordinal);
        Assert.Contains("new.expires_at <= statement_timestamp()", jobTransition, StringComparison.Ordinal);
        Assert.Contains("new.expires_at > statement_timestamp() + interval '30 minutes'", jobTransition, StringComparison.Ordinal);
        Assert.Contains("perform control.acquire_u0_authority_lock()", jobTransition, StringComparison.Ordinal);
        Assert.Contains("identity.security_state = 'active'", jobTransition, StringComparison.Ordinal);
        Assert.Contains("tenant.state = 'active'", jobTransition, StringComparison.Ordinal);
        Assert.Contains("old.user_id is distinct from control.current_actor_id()", jobTransition, StringComparison.Ordinal);
        Assert.Contains("old.correlation_id is distinct from control.current_correlation_id()", jobTransition, StringComparison.Ordinal);
        Assert.Contains("new.row_version := old.row_version + 1", jobTransition, StringComparison.Ordinal);
        Assert.Contains("new.updated_at := greatest(old.updated_at, lifecycle_now)", jobTransition, StringComparison.Ordinal);
        Assert.Contains("old.reservation_expires_at > lifecycle_now", jobTransition, StringComparison.Ordinal);
        Assert.Contains("old.expires_at > lifecycle_now", jobTransition, StringComparison.Ordinal);
        Assert.Contains("before insert or update or delete on control.strategy_import_jobs", jobTransition, StringComparison.Ordinal);
        Assert.Contains(
            "set_config('yo4x.correlation_id', locked_job.correlation_id::text, true)",
            migration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConversionCliCannotSelfAssertTenantUserOrSourceLabelAuthority()
    {
        string command = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.Conversion.Worker",
            "ConversionInventoryCommand.cs");
        string store = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.Conversion.Worker",
            "PostgresMql5CorpusStore.cs");
        string request = Slice(
            store,
            "public sealed class Mql5CorpusPersistenceRequest",
            "public sealed record Mql5CorpusPersistenceResult");

        Assert.Contains("RejectSelfAssertedAuthorityOptions(arguments)", command, StringComparison.Ordinal);
        foreach (string option in new[] { "--tenant-id", "--user-id", "--source-label", "--correlation-id", "--capability" })
        {
            Assert.Contains($"\"{option}\"", command, StringComparison.Ordinal);
        }

        Assert.Contains("Environment.SetEnvironmentVariable(environmentName, null)", command, StringComparison.Ordinal);
        Assert.Contains("decoded = Convert.FromBase64String", command, StringComparison.Ordinal);
        Assert.Contains("return decoded", command, StringComparison.Ordinal);
        Assert.DoesNotContain("SHA256.HashData", command, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantId", request, StringComparison.Ordinal);
        Assert.DoesNotContain("UserId", request, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceLabel", request, StringComparison.Ordinal);
        Assert.Contains("byte[] capability", request, StringComparison.Ordinal);
        Assert.DoesNotContain("capabilitySha256", request, StringComparison.Ordinal);
        Assert.DoesNotContain("string capability", request, StringComparison.OrdinalIgnoreCase);
        string capabilityCopy = Slice(request, "internal byte[] CopyCapability()", "public void Dispose()");
        string capabilityDispose = Slice(request, "public void Dispose()", "public override string ToString()");
        Assert.Contains("lock (lifecycleLock)", capabilityCopy, StringComparison.Ordinal);
        Assert.Contains(".ToArray()", capabilityCopy, StringComparison.Ordinal);
        Assert.Contains("lock (lifecycleLock)", capabilityDispose, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref capability, null)", capabilityDispose, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.ZeroMemory(owned)", capabilityDispose, StringComparison.Ordinal);
        Assert.Contains("command.Parameters.AddWithValue(\"capability\", NpgsqlDbType.Bytea, capability)", store, StringComparison.Ordinal);
        Assert.DoesNotContain("capability_sha256\", NpgsqlDbType.Bytea", store, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reservation.TenantId", store, StringComparison.Ordinal);
        Assert.Contains("reservation.UserId", store, StringComparison.Ordinal);
        Assert.Contains("reservation.CorrelationId", store, StringComparison.Ordinal);
        Assert.Contains("reservation.SourceLabel", store, StringComparison.Ordinal);
        Assert.Contains(
            "consumed && (reservation.ReservationId != importJobId",
            store,
            StringComparison.Ordinal);
        Assert.DoesNotContain("--correlation-id", request, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversionPersistenceRequiresExactSecurityDefinerReservationAndCompletion()
    {
        string migration = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "001_foundation.sql");
        string acquire = Slice(
            migration,
            "create function control.acquire_strategy_import_job",
            "create function control.acquire_strategy_import_persistence_lock");
        string corpusGuard = Slice(
            migration,
            "create function governance.authorize_strategy_source_corpus_insert",
            "create function governance.authorize_strategy_source_file_insert");
        string fileGuard = Slice(
            migration,
            "create function governance.authorize_strategy_source_file_insert",
            "create function control.complete_strategy_import_job");
        string completion = Slice(
            migration,
            "create function control.complete_strategy_import_job",
            "create function control.lock_u0_tenant_authority_mutation");
        string completionPayload = RemoveWhitespaceAroundLineBreaks(Slice(
            completion,
            "safe_payload_canonical :=",
            "insert into audit.audit_events"));

        Assert.Contains("security definer", acquire, StringComparison.Ordinal);
        Assert.Contains("set search_path = ''", acquire, StringComparison.Ordinal);
        Assert.Contains("session_user <> 'yo4x_conversion_worker'", acquire, StringComparison.Ordinal);
        Assert.Contains("octet_length(supplied_capability) <> 32", acquire, StringComparison.Ordinal);
        Assert.Contains("locked_job.capability_sha256 <> pg_catalog.sha256(supplied_capability)", acquire, StringComparison.Ordinal);
        Assert.DoesNotContain("locked_job.capability_sha256 <> supplied_capability", acquire, StringComparison.Ordinal);
        Assert.Contains("perform control.acquire_u0_tenant_authority_lock(target_tenant_id)", acquire, StringComparison.Ordinal);
        Assert.Contains("for update", acquire, StringComparison.Ordinal);
        Assert.Contains("perform set_config('yo4x.tenant_id', locked_job.tenant_id::text, true)", acquire, StringComparison.Ordinal);
        Assert.Contains("perform set_config('yo4x.actor_id', locked_job.user_id::text, true)", acquire, StringComparison.Ordinal);
        Assert.Contains("tenant.state = 'active'", acquire, StringComparison.Ordinal);
        Assert.Contains("identity.security_state = 'active'", acquire, StringComparison.Ordinal);
        Assert.Contains("authorization_now := clock_timestamp()", acquire, StringComparison.Ordinal);
        Assert.Contains("authorization_now + interval '5 minutes'", acquire, StringComparison.Ordinal);
        Assert.Contains("locked_job.reservation_id is distinct from locked_job.id", acquire, StringComparison.Ordinal);
        Assert.Contains("reservation_id = locked_job.id", acquire, StringComparison.Ordinal);
        Assert.Contains("locked_job.correlation_id", acquire, StringComparison.Ordinal);

        foreach (string guard in new[] { corpusGuard, fileGuard, completion })
        {
            Assert.Contains("security definer", guard, StringComparison.Ordinal);
            Assert.Contains("set search_path = ''", guard, StringComparison.Ordinal);
            Assert.Contains("session_user <> 'yo4x_conversion_worker'", guard, StringComparison.Ordinal);
            Assert.Contains("control.current_tenant_id()", guard, StringComparison.Ordinal);
            Assert.Contains("control.current_actor_id()", guard, StringComparison.Ordinal);
            Assert.Contains("locked_job.reservation_id", guard, StringComparison.Ordinal);
            Assert.Contains("authorization_now := clock_timestamp()", guard, StringComparison.Ordinal);
            Assert.Contains("locked_job.reservation_expires_at <= authorization_now", guard, StringComparison.Ordinal);
        }

        Assert.Contains("new.id <> locked_job.id", corpusGuard, StringComparison.Ordinal);
        Assert.Contains("new.user_id <> locked_job.user_id", corpusGuard, StringComparison.Ordinal);
        Assert.Contains("new.source_label <> locked_job.source_label", corpusGuard, StringComparison.Ordinal);
        Assert.Contains("new.corpus_id <> locked_job.id", fileGuard, StringComparison.Ordinal);
        Assert.Contains("target_job_id is null", completion, StringComparison.Ordinal);
        Assert.Contains("control.current_correlation_id() is null", completion, StringComparison.Ordinal);
        Assert.Contains("locked_job.reservation_id is distinct from locked_job.id", completion, StringComparison.Ordinal);
        Assert.Contains("locked_job.correlation_id is distinct from control.current_correlation_id()", completion, StringComparison.Ordinal);
        Assert.Contains("persisted_file_count <> persisted_corpus.file_count", completion, StringComparison.Ordinal);
        Assert.Contains("minimum_manifest_order <> 0", completion, StringComparison.Ordinal);
        Assert.Contains("maximum_manifest_order <> persisted_corpus.file_count - 1", completion, StringComparison.Ordinal);
        Assert.Contains("computed_corpus_sha256 <> persisted_corpus.corpus_sha256", completion, StringComparison.Ordinal);
        Assert.Contains("computed_disposition_counts <> persisted_corpus.disposition_counts", completion, StringComparison.Ordinal);
        Assert.Contains("completed_at := clock_timestamp()", completion, StringComparison.Ordinal);
        Assert.Contains("locked_job.reservation_expires_at <= completed_at", completion, StringComparison.Ordinal);
        Assert.Contains("locked_job.expires_at <= completed_at", completion, StringComparison.Ordinal);
        Assert.True(
            completion.IndexOf("locked_job.reservation_expires_at <= completed_at", StringComparison.Ordinal)
                < completion.IndexOf("update control.strategy_import_jobs", StringComparison.Ordinal),
            "Final capability expiry must be checked immediately before the terminal state transition.");
        Assert.Contains("set state = 'consumed'", completion, StringComparison.Ordinal);
        Assert.Contains(
            "{\"importJobId\":\"' || locked_job.id::text || '\",\"verification\":\"static-inventory-only\"}",
            completionPayload,
            StringComparison.Ordinal);
        Assert.DoesNotContain("source_label", completionPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("source_content", completionPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("manifest_content", completionPayload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("insert into audit.audit_events", completion, StringComparison.Ordinal);
        Assert.Contains("insert into messaging.outbox_messages", completion, StringComparison.Ordinal);
        Assert.Contains("control.current_correlation_id(), locked_job.id", completion, StringComparison.Ordinal);
        Assert.Contains("strategy_source_corpus_capability_guard", completion, StringComparison.Ordinal);
        Assert.Contains("strategy_source_file_capability_guard", completion, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversionWorkerHasWriteOnlyRawSourceAccessAndCannotPromoteOrReachRuntime()
    {
        string roles = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Security",
            "least_privilege_roles.sql");
        string conversionSection = Slice(roles, "-- Conversion worker:", "-- Worker:");
        string normalized = RemoveWhitespaceAroundLineBreaks(conversionSection);
        string normalizedSchemaUsage = RemoveWhitespaceAroundLineBreaks(Slice(
            roles,
            "grant usage on schema identity, \"authorization\", control, operations, governance, audit, messaging, readmodel",
            "grant execute on function control.current_tenant_id()"));
        string store = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.Conversion.Worker",
            "PostgresMql5CorpusStore.cs");

        Assert.Contains(
            "revoke all on function control.acquire_strategy_import_job(uuid, bytea), control.acquire_strategy_import_persistence_lock(uuid), control.complete_strategy_import_job(uuid, uuid, uuid) from public",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "grant execute on function control.acquire_strategy_import_job(uuid, bytea), control.acquire_strategy_import_persistence_lock(uuid), control.complete_strategy_import_job(uuid, uuid, uuid) to yo4x_conversion_worker",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains("grant usage on schema control, governance to yo4x_conversion_worker", normalizedSchemaUsage, StringComparison.Ordinal);
        string corpusGrant = Slice(
            normalized,
            "grant insert (id, tenant_id, user_id, import_job_id, reservation_id",
            "on governance.strategy_source_corpora to yo4x_conversion_worker");
        string fileGrant = Slice(
            normalized,
            "grant insert (id, tenant_id, corpus_id, user_id, import_job_id",
            "on governance.strategy_source_files to yo4x_conversion_worker");
        Assert.DoesNotContain("source_content", corpusGrant, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manifest_content", corpusGrant, StringComparison.Ordinal);
        Assert.Contains("report_content", corpusGrant, StringComparison.Ordinal);
        Assert.Contains("manifest_order", fileGrant, StringComparison.Ordinal);
        Assert.Contains("source_content", fileGrant, StringComparison.Ordinal);
        Assert.DoesNotContain("grant select", conversionSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grant update", conversionSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grant delete", conversionSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audit.audit_events", conversionSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("messaging.outbox_messages", conversionSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("operations.", conversionSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("approval", conversionSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("strategy_versions", conversionSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential_reference", conversionSection, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("insert into governance.strategy_source_corpora", store, StringComparison.Ordinal);
        Assert.Contains("insert into governance.strategy_source_files", store, StringComparison.Ordinal);
        Assert.Contains("Mql5CorpusManifest manifest = ValidateAndRebuildCorpus(corpus)", store, StringComparison.Ordinal);
        string trustedRebuild = Slice(
            store,
            "internal static Mql5CorpusManifest ValidateAndRebuildCorpus",
            "private static string ToStorage(Mql5SourceKind");
        Assert.Contains("new Mql5StaticInventoryAnalyzer().Analyze(corpus.Documents)", trustedRebuild, StringComparison.Ordinal);
        Assert.Contains("Mql5InventoryFormatter.ToJson(corpus.Manifest)", trustedRebuild, StringComparison.Ordinal);
        Assert.Contains("Mql5InventoryFormatter.ToJson(rebuilt)", trustedRebuild, StringComparison.Ordinal);
        Assert.Contains("return rebuilt", trustedRebuild, StringComparison.Ordinal);
        Assert.Contains("manifest_content", store, StringComparison.Ordinal);
        Assert.Contains("report_content", store, StringComparison.Ordinal);
        Assert.Contains("manifest_order", store, StringComparison.Ordinal);
        Assert.Contains("source_content", store, StringComparison.Ordinal);
        Assert.Contains("NpgsqlDbType.Bytea, content", store, StringComparison.Ordinal);
        foreach (string manifestFragment in new[] { "file.Includes", "file.Features", "file.Findings", "file.Verification" })
        {
            Assert.Contains(
                $"Mql5InventoryFormatter.ToJsonFragment({manifestFragment})",
                store,
                StringComparison.Ordinal);
        }
        Assert.Contains(
            "select control.complete_strategy_import_job(@job_id, @audit_id, @outbox_id)",
            store,
            StringComparison.Ordinal);
        Assert.DoesNotContain("PostgresAuditOutboxWriter", store, StringComparison.Ordinal);
        Assert.DoesNotContain("insert into audit.audit_events", store, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("insert into messaging.outbox_messages", store, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("created_at", Slice(
            store,
            "insert into governance.strategy_source_corpora",
            "await insertCorpus.ExecuteNonQueryAsync"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("created_at", Slice(
            store,
            "insert into governance.strategy_source_files",
            "await command.ExecuteNonQueryAsync"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("from governance.strategy_source_corpora", store, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("from governance.strategy_source_files", store, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("governance.strategy_versions", store, StringComparison.OrdinalIgnoreCase);
        foreach (string operationReference in new[]
        {
            "from operations.",
            "join operations.",
            "insert into operations.",
            "update operations.",
            "delete from operations."
        })
        {
            Assert.DoesNotContain(operationReference, store, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void PolicySignatureColumnsAndRuntimeGrantsPreserveTheTrustBoundary()
    {
        string migration = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "001_foundation.sql");
        string roles = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Security",
            "least_privilege_roles.sql");
        string riskPolicies = Slice(
            migration,
            "create table governance.risk_policy_versions",
            "create function governance.reject_risk_policy_content_mutation");
        string safetyPolicies = Slice(
            migration,
            "create table control.execution_safety_policies",
            "create function control.reject_execution_safety_policy_content_mutation");
        string controlSection = Slice(roles, "-- Tenant control API:", "-- Admin BFF:");
        string emergencySection = Slice(roles, "-- Emergency plane:", "-- Secret ingestion:");

        foreach (string table in new[] { riskPolicies, safetyPolicies })
        {
            Assert.Contains(
                "signature_algorithm text not null check (signature_algorithm = 'ECDSA_P256_SHA256_DER')",
                table,
                StringComparison.Ordinal);
            Assert.Contains("signature_bytes bytea not null", table, StringComparison.Ordinal);
            Assert.Contains("signature_sha256 text not null", table, StringComparison.Ordinal);
            Assert.Contains("signing_key_id text not null", table, StringComparison.Ordinal);
        }

        string normalizedControl = RemoveWhitespaceAroundLineBreaks(controlSection);
        Assert.Contains("signature_algorithm", normalizedControl, StringComparison.Ordinal);
        Assert.Contains("signature_bytes", normalizedControl, StringComparison.Ordinal);
        Assert.Contains("signature_sha256", normalizedControl, StringComparison.Ordinal);
        Assert.Contains("signing_key_id", normalizedControl, StringComparison.Ordinal);

        string normalizedEmergency = RemoveWhitespaceAroundLineBreaks(emergencySection);
        Assert.Contains(
            "signature_algorithm, signature_bytes, signature_sha256, signing_key_id",
            normalizedEmergency,
            StringComparison.Ordinal);
        Assert.Contains(
            "grant update (state, row_version, updated_at) on control.execution_safety_policies to yo4x_emergency",
            normalizedEmergency,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "grant insert, update on control.execution_safety_policies",
            normalizedEmergency,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmergencyPolicyRoleCannotReleaseOrEraseARestriction()
    {
        string migration = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "001_foundation.sql");
        string monotonicity = Slice(
            migration,
            "create function control.enforce_emergency_policy_monotonicity()",
            "create trigger execution_safety_policies_emergency_monotonicity");
        string normalized = RemoveWhitespaceAroundLineBreaks(monotonicity);
        string allowedTransitions = Slice(normalized, "if new.state not in", "then");

        Assert.Contains("current_user <> 'yo4x_emergency'", normalized, StringComparison.Ordinal);
        Assert.Contains("new.state <> 'active'", normalized, StringComparison.Ordinal);
        Assert.Contains("Emergency policy writes cannot release a restriction.", normalized, StringComparison.Ordinal);
        Assert.Contains("'active'", allowedTransitions, StringComparison.Ordinal);
        Assert.Contains("'expiry_review_required'", allowedTransitions, StringComparison.Ordinal);
        Assert.Contains("'safe_to_release'", allowedTransitions, StringComparison.Ordinal);
        Assert.Contains("'reconciling'", allowedTransitions, StringComparison.Ordinal);
        Assert.Contains("'partial'", allowedTransitions, StringComparison.Ordinal);
        Assert.DoesNotContain("'inactive'", allowedTransitions, StringComparison.Ordinal);
    }

    [Fact]
    public void UserPolicyEvaluationEvidenceIsReconstructibleAndImmutable()
    {
        string migration = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "001_foundation.sql");
        string evaluationTable = Slice(
            migration,
            "create table control.user_policy_evaluations",
            "create table control.approval_requests");
        string normalizedSchema = RemoveWhitespaceAroundLineBreaks(evaluationTable);
        string evaluator = ReadSource("PostgresDeploymentPolicyEvaluation.cs");

        foreach (string requiredEvidence in new[]
        {
            "input_snapshot jsonb not null",
            "applicable_policies jsonb not null",
            "effective_vector jsonb not null",
            "rule_results jsonb not null",
            "effective_policy_digest text not null",
            "policy_version_watermark text not null",
            "input_sha256 text not null",
            "evidence_sha256 text not null"
        })
        {
            Assert.Contains(requiredEvidence, normalizedSchema, StringComparison.Ordinal);
        }

        Assert.Contains(
            "before update or delete on control.user_policy_evaluations",
            normalizedSchema,
            StringComparison.Ordinal);
        Assert.Contains("insert into control.user_policy_evaluations", evaluator, StringComparison.Ordinal);
        Assert.Contains("ApplicablePolicies = JsonNode.Parse(applicablePoliciesJson)", evaluator, StringComparison.Ordinal);
        Assert.Contains("EffectiveVector = JsonNode.Parse(effectiveVectorJson)", evaluator, StringComparison.Ordinal);
        Assert.Contains("RuleResults = JsonNode.Parse(ruleResultsJson)", evaluator, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("identity.user_identities")]
    [InlineData("identity.user_session_families")]
    [InlineData("operations.deployments")]
    [InlineData("control.idempotency_records")]
    [InlineData("control.user_operations")]
    [InlineData("control.credential_ingestion_grants")]
    public void ControlApiCannotBlanketMutateSecurityOrAsynchronousTruthTables(string relation)
    {
        string roles = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Security",
            "least_privilege_roles.sql");
        string controlSection = Slice(
            roles,
            "-- Tenant control API:",
            "-- Admin BFF:");

        foreach (string statement in controlSection.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string normalized = string.Join(
                ' ',
                statement.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (!normalized.Contains(relation, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int on = normalized.IndexOf(" on ", StringComparison.OrdinalIgnoreCase);
            Assert.True(on >= 0, $"The grant for {relation} is malformed.");
            string privileges = normalized[..on];
            bool grantsMutation = privileges.Contains("insert", StringComparison.OrdinalIgnoreCase)
                || privileges.Contains("update", StringComparison.OrdinalIgnoreCase);
            Assert.False(
                grantsMutation && !privileges.Contains('('),
                $"yo4x_control_api must receive only column-scoped mutation rights on {relation}.");
        }
    }

    [Fact]
    public void ControlPlaneNeverReadsTheOpaqueCredentialReference()
    {
        string credentialMutations = ReadSource("PostgresCredentialMutations.cs");
        string userOperations = ReadSource("PostgresUserOperations.cs");
        string reads = ReadSource("PostgresControlPlaneReads.cs");

        Assert.DoesNotContain("credential_reference is not null", credentialMutations, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential_reference is not null", userOperations, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential_reference is not null", reads, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasClosedLexicalScope(string body, int readerDeclaration, int commit)
    {
        int containingOpenBrace = body.LastIndexOf('{', readerDeclaration);
        if (containingOpenBrace < 0)
        {
            return false;
        }

        int depth = 0;
        for (int index = containingOpenBrace; index < commit; index++)
        {
            depth += body[index] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0
            };

            if (depth == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractMethod(string source, string methodName)
    {
        int method = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(method >= 0, $"The expected method {methodName} was not found.");
        int nextMethod = source.IndexOf("\n    public ", method + methodName.Length, StringComparison.Ordinal);
        return nextMethod < 0 ? source[method..] : source[method..nextMethod];
    }

    private static string ReadSource(string fileName)
        => ReadRepositoryFile(
            "src",
            "Infrastructure",
            "YO4X.ControlPlane.Postgres",
            fileName);

    private static string RemoveWhitespace(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character)));

    private static string RemoveWhitespaceAroundLineBreaks(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string ReadRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        string path = Path.Combine([directory.FullName, .. segments]);
        Assert.True(File.Exists(path), $"The repository contract file {path} was not found.");
        return File.ReadAllText(path);
    }

    private static string Slice(string value, string startMarker, string endMarker)
    {
        int start = value.IndexOf(startMarker, StringComparison.Ordinal);
        int end = value.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Contract section {startMarker} was not found.");
        return value[start..end];
    }
}
