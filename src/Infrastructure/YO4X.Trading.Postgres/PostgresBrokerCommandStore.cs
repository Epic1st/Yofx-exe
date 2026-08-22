using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.Persistence.Postgres;
using YO4X.Risk;
using YO4X.Runtime.Contracts;
using YO4X.Tenancy;
using YO4X.Trading.Abstractions;

namespace YO4X.Trading.Postgres;

public sealed class PostgresBrokerCommandStore
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly PostgresDatabase authorizerDatabase;
    private readonly PostgresDatabase gatewayDatabase;
    private readonly IExecutionLeaseTrustVerifier executionLeaseTrustVerifier;

    public PostgresBrokerCommandStore(
        PostgresDatabase authorizerDatabase,
        PostgresDatabase gatewayDatabase,
        IExecutionLeaseTrustVerifier executionLeaseTrustVerifier)
    {
        this.authorizerDatabase = authorizerDatabase
            ?? throw new ArgumentNullException(nameof(authorizerDatabase));
        this.gatewayDatabase = gatewayDatabase
            ?? throw new ArgumentNullException(nameof(gatewayDatabase));
        this.executionLeaseTrustVerifier = executionLeaseTrustVerifier
            ?? throw new ArgumentNullException(nameof(executionLeaseTrustVerifier));
    }

    public async Task<BrokerCommandAuthorizationReceipt> AuthorizeAsync(
        TenantExecutionContext context,
        BrokerCommandAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ValidateAuthorizationRequest(context, request);
        ExecutionLeaseTrustVerification leaseTrust = executionLeaseTrustVerifier
            .Verify(request.ExecutionLease);
        if (!leaseTrust.IsTrusted
            || leaseTrust.SignatureAlgorithm is null
            || leaseTrust.SigningKeyId is null
            || leaseTrust.TrustedVerificationKeySha256 is null)
        {
            throw new UnauthorizedAccessException(
                $"The execution lease is not trusted ({leaseTrust.ReasonCode}).");
        }

        byte[] commandContent = CanonicalBytes(request.Command);
        byte[] exposureContent = CanonicalBytes(request.Exposure);
        byte[] riskInputContent = CanonicalBytes(request.RiskInput);
        byte[] riskDecisionContent = CanonicalBytes(request.RiskDecision);
        byte[] reconciliationContent = CanonicalBytes(request.Reconciliation);
        try
        {
            string exposureSha256 = Sha256(exposureContent);
            string reconciliationSha256 = Sha256(reconciliationContent);
            var exposureAuthorization = new BrokerExposureAuthorization(
                request.Exposure.ContractVersion,
                request.Exposure.SnapshotId,
                exposureSha256,
                request.Exposure.SourceKind,
                request.Exposure.SourceSequence,
                request.Exposure.SourceEvidenceSha256,
                OldestObservedAt(request.Exposure),
                request.RiskInput.EvaluatedAtUtc,
                request.RiskInput.EvaluatedAtUtc.AddSeconds(1));
            var riskAuthorization = new NumericRiskAuthorization(
                request.RiskDecisionId,
                request.ExecutionLease.Claims.Binding.SafetyPolicyVersionId,
                request.RiskDecision.PolicyDigest,
                ToStorage(request.RiskDecision.ActionClass),
                request.RiskDecision.InputDigest,
                request.RiskDecision.DecisionDigest,
                request.RiskInput.EvaluatedAtUtc,
                request.RiskDecision.IsAllowed);
            var leaseAuthorization = new ExecutionLeaseAuthorization(
                request.ExecutionLease,
                ExecutionLeaseEnvelopeDigest.Sha256(request.ExecutionLease),
                request.ExecutionLease.PayloadSha256,
                ExecutionLeaseEnvelopeDigest.SignatureSha256(request.ExecutionLease),
                leaseTrust.TrustedVerificationKeySha256);
            var reconciliation = new BrokerReconciliationCommitment(
                request.Reconciliation.ContractVersion,
                request.Reconciliation.CommandId,
                request.Reconciliation.Method,
                request.Reconciliation.ScopeSha256,
                request.Reconciliation.MustBeginByUtc,
                request.Reconciliation.MustCompleteByUtc,
                reconciliationSha256);
            BrokerCommandAuthorizationDocument authorizationDocument =
                AuthorizedBrokerCommand.CreateDocument(
                    request.Command,
                    request.Provenance,
                    riskAuthorization,
                    exposureAuthorization,
                    request.ExecutionSafety,
                    leaseAuthorization,
                    reconciliation);
            byte[] authorizationContent = CanonicalBytes(authorizationDocument);
            try
            {
                string expectedAuthorizationSha256 = Sha256(authorizationContent);
                await using TenantPostgresTransaction transaction =
                    await authorizerDatabase.BeginTenantTransactionAsync(context, cancellationToken)
                        .ConfigureAwait(false);
                await using NpgsqlCommand command = transaction.CreateCommand(
                    """
                    select *
                    from control.authorize_broker_command(
                        @command_id, @intent_id, @broker_account_id, @deployment_id,
                        @generation, @source_binding_id, @exposure_id, @risk_decision_id,
                        @lease_id, @lease_token_sha256, @lease_payload_sha256,
                        @lease_signature_sha256, @lease_signature_algorithm,
                        @lease_signing_key_id, @lease_trusted_verification_key_sha256,
                        @idempotency_key, @action_class, @execution_safety_overlay_sha256,
                        @execution_safety_policy_version_watermark,
                        @command_content, @exposure_content, @exposure_source_kind,
                        @exposure_source_sequence, @exposure_source_evidence_sha256,
                        @quote_as_of, @account_as_of, @position_as_of, @order_as_of,
                        @symbol_as_of, @conversion_rate_as_of, @risk_day_as_of,
                        @order_rate_as_of, @risk_input_content, @risk_decision_content,
                        @risk_evaluated_at, @reconciliation_content,
                        @reconciliation_scope_sha256, @reconciliation_must_begin_by,
                        @reconciliation_must_complete_by, @authorization_content,
                        @audit_event_id)
                    """);
                AddAuthorizationParameters(
                    command,
                    request,
                    leaseAuthorization,
                    commandContent,
                    exposureContent,
                    riskInputContent,
                    riskDecisionContent,
                    reconciliationContent,
                    authorizationContent);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new UnauthorizedAccessException(
                        "The durable broker-command authorization was not accepted.");
                }

                Guid commandId = reader.GetGuid(0);
                string persistedAuthorizationSha256 = reader.GetString(1);
                var result = new BrokerCommandAuthorizationReceipt(
                    request,
                    persistedAuthorizationSha256,
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5),
                    reader.GetFieldValue<DateTimeOffset>(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetFieldValue<DateTimeOffset>(9),
                    reader.GetInt64(10),
                    reader.GetFieldValue<DateTimeOffset>(11),
                    reader.GetBoolean(12));
                if (commandId != request.Command.CommandId
                    || !FixedTimeEquals(persistedAuthorizationSha256, expectedAuthorizationSha256)
                    || !FixedTimeEquals(result.ExposureSnapshotSha256, exposureSha256)
                    || !FixedTimeEquals(result.RiskInputSha256, request.RiskDecision.InputDigest)
                    || !FixedTimeEquals(result.RiskDecisionSha256, request.RiskDecision.DecisionDigest)
                    || await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        "PostgreSQL returned an inconsistent broker-command authorization receipt.");
                }

                await reader.DisposeAsync().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(authorizationContent);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(commandContent);
            CryptographicOperations.ZeroMemory(exposureContent);
            CryptographicOperations.ZeroMemory(riskInputContent);
            CryptographicOperations.ZeroMemory(riskDecisionContent);
            CryptographicOperations.ZeroMemory(reconciliationContent);
        }
    }

    public Task<BrokerCommandDispatchClaim> ClaimForDispatchAsync(
        TenantExecutionContext context,
        BrokerCommandAuthorizationReceipt authorization,
        Guid claimToken,
        Guid auditEventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return ClaimForDispatchAsync(
            context,
            new BrokerCommandDispatchReference(
                authorization.Request.Command.CommandId,
                authorization.AuthorizationSha256,
                ExecutionLeaseEnvelopeDigest.Sha256(authorization.Request.ExecutionLease)),
            claimToken,
            auditEventId,
            cancellationToken);
    }

    public async Task<BrokerCommandDispatchClaim> ClaimForDispatchAsync(
        TenantExecutionContext context,
        BrokerCommandDispatchReference reference,
        Guid claimToken,
        Guid auditEventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reference);
        RequireNonEmpty(reference.CommandId, nameof(reference.CommandId));
        RequireDigest(reference.AuthorizationSha256, nameof(reference.AuthorizationSha256));
        RequireDigest(reference.ExecutionLeaseTokenSha256, nameof(reference.ExecutionLeaseTokenSha256));
        RequireNonEmpty(claimToken, nameof(claimToken));
        RequireNonEmpty(auditEventId, nameof(auditEventId));

        await using TenantPostgresTransaction transaction =
            await gatewayDatabase.BeginTenantTransactionAsync(context, cancellationToken)
                .ConfigureAwait(false);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select *
            from control.claim_authorized_broker_command(
                @command_id, @authorization_sha256, @lease_token_sha256,
                @claim_token, @audit_event_id)
            """);
        AddUuid(command, "command_id", reference.CommandId);
        command.Parameters.AddWithValue(
            "authorization_sha256",
            NpgsqlDbType.Text,
            reference.AuthorizationSha256);
        command.Parameters.AddWithValue(
            "lease_token_sha256",
            NpgsqlDbType.Text,
            reference.ExecutionLeaseTokenSha256);
        AddUuid(command, "claim_token", claimToken);
        AddUuid(command, "audit_event_id", auditEventId);

        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException(
                "The broker command is not dispatchable under current durable authority.");
        }

        byte[] commandContent = reader.GetFieldValue<byte[]>(1);
        byte[] authorizationContent = reader.GetFieldValue<byte[]>(2);
        byte[] signedLeaseContent = reader.GetFieldValue<byte[]>(3);
        try
        {
            string returnedAuthorizationSha256 = reader.GetString(4);
            DateTimeOffset exposureOldestObservedAt = reader.GetFieldValue<DateTimeOffset>(5);
            DateTimeOffset exposureReceivedAt = reader.GetFieldValue<DateTimeOffset>(6);
            DateTimeOffset exposureValidUntil = reader.GetFieldValue<DateTimeOffset>(7);
            DateTimeOffset riskEvaluatedAt = reader.GetFieldValue<DateTimeOffset>(8);
            DateTimeOffset riskAuthorizationExpiresAt = reader.GetFieldValue<DateTimeOffset>(9);
            DateTimeOffset claimExpiresAt = reader.GetFieldValue<DateTimeOffset>(10);
            long commandVersion = reader.GetInt64(11);
            bool replayed = reader.GetBoolean(12);
            if (reader.GetGuid(0) != reference.CommandId
                || !FixedTimeEquals(returnedAuthorizationSha256, reference.AuthorizationSha256)
                || await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "PostgreSQL returned an inconsistent broker-command dispatch claim.");
            }

            AuthorizedBrokerCommand authorized = HydrateAuthorizedCommand(
                commandContent,
                authorizationContent,
                signedLeaseContent,
                reference.CommandId,
                reference.AuthorizationSha256,
                reference.ExecutionLeaseTokenSha256,
                exposureOldestObservedAt,
                exposureReceivedAt,
                exposureValidUntil,
                riskEvaluatedAt,
                riskAuthorizationExpiresAt);
            await reader.DisposeAsync().ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new BrokerCommandDispatchClaim(
                authorized,
                claimToken,
                claimExpiresAt,
                commandVersion,
                replayed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(commandContent);
            CryptographicOperations.ZeroMemory(authorizationContent);
            CryptographicOperations.ZeroMemory(signedLeaseContent);
        }
    }

    public async Task<BrokerCommandMutationReceipt> RecordSubmissionAsync(
        TenantExecutionContext context,
        BrokerCommandDispatchClaim claim,
        GatewaySendResult result,
        Guid auditEventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);
        RequireNonEmpty(auditEventId, nameof(auditEventId));

        string disposition = ToStorage(result.Disposition);
        var document = new BrokerGatewaySubmissionDocument(
            disposition,
            result.Code,
            result.BrokerRequestId,
            result.OrderId,
            result.DealId,
            result.ObservedAtUtc.ToUniversalTime());
        byte[] content = CanonicalBytes(document);
        try
        {
            await using TenantPostgresTransaction transaction =
                await gatewayDatabase.BeginTenantTransactionAsync(context, cancellationToken)
                    .ConfigureAwait(false);
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select *
                from control.record_broker_command_submission(
                    @command_id, @authorization_sha256, @claim_token, @disposition,
                    @result_code, @broker_request_id, @broker_order_id, @broker_deal_id,
                    @result_content, @observed_at, @audit_event_id)
                """);
            AddUuid(command, "command_id", claim.Command.Command.CommandId);
            command.Parameters.AddWithValue(
                "authorization_sha256",
                NpgsqlDbType.Text,
                claim.Command.AuthorizationSha256);
            AddUuid(command, "claim_token", claim.ClaimToken);
            command.Parameters.AddWithValue("disposition", NpgsqlDbType.Text, disposition);
            command.Parameters.AddWithValue("result_code", NpgsqlDbType.Text, result.Code);
            AddNullableText(command, "broker_request_id", result.BrokerRequestId);
            AddNullableText(command, "broker_order_id", result.OrderId);
            AddNullableText(command, "broker_deal_id", result.DealId);
            command.Parameters.AddWithValue("result_content", NpgsqlDbType.Bytea, content);
            command.Parameters.AddWithValue(
                "observed_at",
                NpgsqlDbType.TimestampTz,
                result.ObservedAtUtc.ToUniversalTime());
            AddUuid(command, "audit_event_id", auditEventId);
            BrokerCommandMutationReceipt receipt = await ReadMutationReceiptAsync(
                    command,
                    cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return receipt;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }

    public async Task<BrokerCommandMutationReceipt?> RecoverExpiredLifecycleAsync(
        TenantExecutionContext context,
        Guid commandId,
        string authorizationSha256,
        Guid auditEventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        RequireNonEmpty(commandId, nameof(commandId));
        RequireDigest(authorizationSha256, nameof(authorizationSha256));
        RequireNonEmpty(auditEventId, nameof(auditEventId));

        await using TenantPostgresTransaction transaction =
            await gatewayDatabase.BeginTenantTransactionAsync(context, cancellationToken)
                .ConfigureAwait(false);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select *
            from control.recover_expired_broker_command_lifecycle(
                @command_id, @authorization_sha256, @audit_event_id)
            """);
        AddUuid(command, "command_id", commandId);
        command.Parameters.AddWithValue(
            "authorization_sha256",
            NpgsqlDbType.Text,
            authorizationSha256);
        AddUuid(command, "audit_event_id", auditEventId);
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        BrokerCommandMutationReceipt? receipt = null;
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            receipt = new BrokerCommandMutationReceipt(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetBoolean(5));
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "The broker-command recovery transition was ambiguous.");
            }
        }

        await reader.DisposeAsync().ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return receipt;
    }

    public async Task<BrokerCommandReconciliationClaim> BeginReconciliationAsync(
        TenantExecutionContext context,
        Guid commandId,
        string authorizationSha256,
        Guid reconciliationClaimToken,
        Guid auditEventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        RequireNonEmpty(commandId, nameof(commandId));
        RequireDigest(authorizationSha256, nameof(authorizationSha256));
        RequireNonEmpty(reconciliationClaimToken, nameof(reconciliationClaimToken));
        RequireNonEmpty(auditEventId, nameof(auditEventId));

        await using TenantPostgresTransaction transaction =
            await gatewayDatabase.BeginTenantTransactionAsync(context, cancellationToken)
                .ConfigureAwait(false);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select *
            from control.begin_broker_command_reconciliation(
                @command_id, @authorization_sha256, @claim_token, @audit_event_id)
            """);
        AddUuid(command, "command_id", commandId);
        command.Parameters.AddWithValue(
            "authorization_sha256",
            NpgsqlDbType.Text,
            authorizationSha256);
        AddUuid(command, "claim_token", reconciliationClaimToken);
        AddUuid(command, "audit_event_id", auditEventId);
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The broker command is not eligible for reconciliation.");
        }

        byte[] commandContent = reader.GetFieldValue<byte[]>(1);
        byte[] authorizationContent = reader.GetFieldValue<byte[]>(2);
        byte[] signedLeaseContent = reader.GetFieldValue<byte[]>(3);
        try
        {
            string returnedAuthorizationSha256 = reader.GetString(4);
            AuthorizedBrokerCommand authorized = HydrateAuthorizedCommand(
                commandContent,
                authorizationContent,
                signedLeaseContent,
                commandId,
                returnedAuthorizationSha256,
                expectedLeaseTokenSha256: null,
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetFieldValue<DateTimeOffset>(9));
            var receipt = new BrokerCommandReconciliationClaim(
                authorized,
                reconciliationClaimToken,
                reader.GetString(10),
                reader.GetFieldValue<DateTimeOffset>(11),
                reader.GetFieldValue<DateTimeOffset>(12),
                reader.GetFieldValue<DateTimeOffset>(13),
                reader.GetInt32(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.GetInt64(20),
                reader.GetFieldValue<DateTimeOffset>(21),
                reader.GetFieldValue<DateTimeOffset>(22),
                reader.GetBoolean(23));
            if (reader.GetGuid(0) != commandId
                || !FixedTimeEquals(returnedAuthorizationSha256, authorizationSha256)
                || !FixedTimeEquals(receipt.ScopeSha256, authorized.Reconciliation.ScopeSha256)
                || receipt.MustBeginByUtc != authorized.Reconciliation.MustBeginByUtc
                || receipt.MustCompleteByUtc != authorized.Reconciliation.MustCompleteByUtc
                || await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The reconciliation claim was inconsistent.");
            }

            await reader.DisposeAsync().ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return receipt;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(commandContent);
            CryptographicOperations.ZeroMemory(authorizationContent);
            CryptographicOperations.ZeroMemory(signedLeaseContent);
        }
    }

    public async Task<BrokerCommandMutationReceipt> CompleteReconciliationAsync(
        TenantExecutionContext context,
        string authorizationSha256,
        Guid reconciliationClaimToken,
        Guid reconciliationId,
        BrokerCommandReconciliationEvidenceDocument evidence,
        Guid auditEventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(evidence);
        RequireNonEmpty(reconciliationClaimToken, nameof(reconciliationClaimToken));
        RequireNonEmpty(reconciliationId, nameof(reconciliationId));
        RequireNonEmpty(auditEventId, nameof(auditEventId));

        byte[] content = CanonicalBytes(evidence);
        try
        {
            await using TenantPostgresTransaction transaction =
                await gatewayDatabase.BeginTenantTransactionAsync(context, cancellationToken)
                    .ConfigureAwait(false);
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select *
                from control.complete_broker_command_reconciliation(
                    @command_id, @authorization_sha256, @claim_token,
                    @reconciliation_id, @match, @reason_code,
                    @source_evidence_sha256, @result_content, @broker_order_id,
                    @broker_deal_id, @observed_at, @audit_event_id)
                """);
            AddUuid(command, "command_id", evidence.CommandId);
            command.Parameters.AddWithValue(
                "authorization_sha256",
                NpgsqlDbType.Text,
                authorizationSha256);
            AddUuid(command, "claim_token", reconciliationClaimToken);
            AddUuid(command, "reconciliation_id", reconciliationId);
            command.Parameters.AddWithValue("match", NpgsqlDbType.Text, evidence.Match);
            command.Parameters.AddWithValue("reason_code", NpgsqlDbType.Text, evidence.ReasonCode);
            command.Parameters.AddWithValue(
                "source_evidence_sha256",
                NpgsqlDbType.Text,
                evidence.SourceEvidenceSha256);
            command.Parameters.AddWithValue("result_content", NpgsqlDbType.Bytea, content);
            AddNullableText(command, "broker_order_id", evidence.OrderId);
            AddNullableText(command, "broker_deal_id", evidence.DealId);
            command.Parameters.AddWithValue(
                "observed_at",
                NpgsqlDbType.TimestampTz,
                evidence.ObservedAtUtc.ToUniversalTime());
            AddUuid(command, "audit_event_id", auditEventId);
            BrokerCommandMutationReceipt receipt = await ReadMutationReceiptAsync(
                    command,
                    cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return receipt;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }

    private AuthorizedBrokerCommand HydrateAuthorizedCommand(
        byte[] commandContent,
        byte[] authorizationContent,
        byte[] signedLeaseContent,
        Guid expectedCommandId,
        string expectedAuthorizationSha256,
        string? expectedLeaseTokenSha256,
        DateTimeOffset exposureOldestObservedAt,
        DateTimeOffset exposureReceivedAt,
        DateTimeOffset exposureValidUntil,
        DateTimeOffset riskEvaluatedAt,
        DateTimeOffset riskAuthorizationExpiresAt)
    {
        NormalizedBrokerCommand normalized = DeserializeCanonical<NormalizedBrokerCommand>(
            commandContent,
            "normalized broker command");
        BrokerCommandAuthorizationDocument document =
            DeserializeCanonical<BrokerCommandAuthorizationDocument>(
                authorizationContent,
                "broker-command authorization");
        SignedExecutionLease signedLease = DeserializeCanonical<SignedExecutionLease>(
            signedLeaseContent,
            "signed execution lease");
        ExecutionLeaseTrustVerification leaseTrust = executionLeaseTrustVerifier.Verify(signedLease);
        string persistedLeaseTokenSha256 = ExecutionLeaseEnvelopeDigest.Sha256(signedLease);
        string requiredLeaseTokenSha256 = expectedLeaseTokenSha256
            ?? document.ExecutionLeaseTokenSha256;

        if (expectedCommandId == Guid.Empty
            || normalized.CommandId != expectedCommandId
            || document.CommandId != expectedCommandId
            || !FixedTimeEquals(Sha256(commandContent), document.CommandSha256)
            || !FixedTimeEquals(Sha256(authorizationContent), expectedAuthorizationSha256)
            || !FixedTimeEquals(persistedLeaseTokenSha256, requiredLeaseTokenSha256)
            || !FixedTimeEquals(
                persistedLeaseTokenSha256,
                document.ExecutionLeaseTokenSha256)
            || !leaseTrust.IsTrusted
            || leaseTrust.SignatureAlgorithm != document.ExecutionLeaseSignatureAlgorithm
            || leaseTrust.SigningKeyId != document.ExecutionLeaseSigningKeyId
            || leaseTrust.TrustedVerificationKeySha256 is null
            || !FixedTimeEquals(
                leaseTrust.TrustedVerificationKeySha256,
                document.ExecutionLeaseTrustedVerificationKeySha256)
            || riskEvaluatedAt.Offset != TimeSpan.Zero
            || riskEvaluatedAt != normalized.CreatedAtUtc
            || riskAuthorizationExpiresAt.Offset != TimeSpan.Zero
            || riskAuthorizationExpiresAt <= riskEvaluatedAt
            || exposureOldestObservedAt.Offset != TimeSpan.Zero
            || exposureReceivedAt.Offset != TimeSpan.Zero
            || exposureValidUntil.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "PostgreSQL returned an inconsistent durable broker-command envelope.");
        }

        var provenance = new BrokerCommandProvenance(
            document.TenantId,
            document.BrokerAccountId,
            document.StrategyId,
            document.StrategyVersionId,
            document.StrategyVersion,
            document.StrategyPackageSha256,
            document.StrategySourceBindingId,
            document.SourceCorpusId,
            document.SourceCorpusSha256,
            document.SourceManifestSha256,
            document.SourceReportSha256,
            document.CompiledArtifactSha256,
            document.CompilerArtifactSha256,
            document.ParseTypecheckProofSha256,
            document.CompileProofSha256,
            document.SemanticConversionProofSha256,
            document.ReferenceParityProofSha256,
            document.DemoRuntimeProofSha256,
            document.StrategyVerificationEvidenceSha256,
            document.StrategyVerificationSignatureSha256,
            document.StrategyVerificationSignatureAlgorithm,
            document.StrategyVerificationSigningKeyId,
            document.StrategyVerifiedByWorkloadId,
            document.StrategyVerifiedAtUtc,
            document.StrategySignatureCryptographicallyVerified,
            document.GatewayArtifactId,
            document.GatewayArtifactSha256);
        var exposure = new BrokerExposureAuthorization(
            BrokerCommandAuthorizationContractVersions.ExposureSnapshotV1,
            document.ExposureSnapshotId,
            document.ExposureSnapshotSha256,
            document.ExposureSourceKind,
            document.ExposureSourceSequence,
            document.ExposureSourceEvidenceSha256,
            exposureOldestObservedAt,
            exposureReceivedAt,
            exposureValidUntil);
        var risk = new NumericRiskAuthorization(
            document.RiskDecisionId,
            document.RiskPolicyVersionId,
            document.RiskPolicySha256,
            document.RiskActionClass,
            document.RiskInputSha256,
            document.RiskDecisionSha256,
            riskEvaluatedAt,
            true);
        var safety = new ExecutionSafetyAuthorization(
            document.ExecutionSafetyOverlaySha256,
            document.ExecutionSafetyPolicyVersionWatermark);
        var reconciliation = new BrokerReconciliationCommitment(
            document.ReconciliationContractVersion,
            document.CommandId,
            document.ReconciliationMethod,
            document.ReconciliationScopeSha256,
            document.ReconciliationMustBeginByUtc,
            document.ReconciliationMustCompleteByUtc,
            document.ReconciliationCommitmentSha256);
        return AuthorizedBrokerCommand.Create(
            normalized,
            provenance,
            risk,
            exposure,
            safety,
            signedLease,
            leaseTrust.TrustedVerificationKeySha256,
            reconciliation,
            expectedAuthorizationSha256);
    }

    private static async Task<BrokerCommandMutationReceipt> ReadMutationReceiptAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The broker-command lifecycle transition was rejected.");
        }

        var receipt = new BrokerCommandMutationReceipt(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetBoolean(5));
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The broker-command lifecycle transition was ambiguous.");
        }

        return receipt;
    }

    private static void AddAuthorizationParameters(
        NpgsqlCommand command,
        BrokerCommandAuthorizationRequest request,
        ExecutionLeaseAuthorization lease,
        byte[] commandContent,
        byte[] exposureContent,
        byte[] riskInputContent,
        byte[] riskDecisionContent,
        byte[] reconciliationContent,
        byte[] authorizationContent)
    {
        AddUuid(command, "command_id", request.Command.CommandId);
        AddUuid(command, "intent_id", request.Command.IntentId);
        AddUuid(command, "broker_account_id", request.Provenance.BrokerAccountId);
        AddUuid(command, "deployment_id", request.Command.DeploymentId);
        command.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, request.Command.Generation);
        AddUuid(command, "source_binding_id", request.Provenance.StrategySourceBindingId);
        AddUuid(command, "exposure_id", request.Exposure.SnapshotId);
        AddUuid(command, "risk_decision_id", request.RiskDecisionId);
        AddUuid(command, "lease_id", request.ExecutionLease.Claims.LeaseId);
        command.Parameters.AddWithValue(
            "lease_token_sha256",
            NpgsqlDbType.Text,
            lease.LeaseTokenSha256);
        command.Parameters.AddWithValue(
            "lease_payload_sha256",
            NpgsqlDbType.Text,
            lease.LeasePayloadSha256);
        command.Parameters.AddWithValue(
            "lease_signature_sha256",
            NpgsqlDbType.Text,
            lease.LeaseSignatureSha256);
        command.Parameters.AddWithValue(
            "lease_signature_algorithm",
            NpgsqlDbType.Text,
            request.ExecutionLease.SignatureAlgorithm);
        command.Parameters.AddWithValue(
            "lease_signing_key_id",
            NpgsqlDbType.Text,
            request.ExecutionLease.SigningKeyId);
        command.Parameters.AddWithValue(
            "lease_trusted_verification_key_sha256",
            NpgsqlDbType.Text,
            lease.TrustedVerificationKeySha256);
        command.Parameters.AddWithValue(
            "idempotency_key",
            NpgsqlDbType.Text,
            request.Command.IdempotencyKey);
        command.Parameters.AddWithValue(
            "action_class",
            NpgsqlDbType.Text,
            ToStorage(request.RiskDecision.ActionClass));
        command.Parameters.AddWithValue(
            "execution_safety_overlay_sha256",
            NpgsqlDbType.Text,
            request.ExecutionSafety.EffectiveOverlaySha256);
        command.Parameters.AddWithValue(
            "execution_safety_policy_version_watermark",
            NpgsqlDbType.Bigint,
            request.ExecutionSafety.PolicyVersionWatermark);
        command.Parameters.AddWithValue("command_content", NpgsqlDbType.Bytea, commandContent);
        command.Parameters.AddWithValue("exposure_content", NpgsqlDbType.Bytea, exposureContent);
        command.Parameters.AddWithValue(
            "exposure_source_kind",
            NpgsqlDbType.Text,
            request.Exposure.SourceKind);
        command.Parameters.AddWithValue(
            "exposure_source_sequence",
            NpgsqlDbType.Bigint,
            request.Exposure.SourceSequence);
        command.Parameters.AddWithValue(
            "exposure_source_evidence_sha256",
            NpgsqlDbType.Text,
            request.Exposure.SourceEvidenceSha256);
        AddTimestamp(command, "quote_as_of", request.Exposure.QuoteAsOfUtc);
        AddTimestamp(command, "account_as_of", request.Exposure.AccountAsOfUtc);
        AddTimestamp(command, "position_as_of", request.Exposure.PositionAsOfUtc);
        AddTimestamp(command, "order_as_of", request.Exposure.OrderAsOfUtc);
        AddTimestamp(command, "symbol_as_of", request.Exposure.SymbolAsOfUtc);
        AddTimestamp(command, "conversion_rate_as_of", request.Exposure.ConversionRateAsOfUtc);
        AddTimestamp(command, "risk_day_as_of", request.Exposure.RiskDayAsOfUtc);
        AddTimestamp(command, "order_rate_as_of", request.Exposure.OrderRateAsOfUtc);
        command.Parameters.AddWithValue("risk_input_content", NpgsqlDbType.Bytea, riskInputContent);
        command.Parameters.AddWithValue(
            "risk_decision_content",
            NpgsqlDbType.Bytea,
            riskDecisionContent);
        AddTimestamp(command, "risk_evaluated_at", request.RiskInput.EvaluatedAtUtc);
        command.Parameters.AddWithValue(
            "reconciliation_content",
            NpgsqlDbType.Bytea,
            reconciliationContent);
        command.Parameters.AddWithValue(
            "reconciliation_scope_sha256",
            NpgsqlDbType.Text,
            request.Reconciliation.ScopeSha256);
        AddTimestamp(
            command,
            "reconciliation_must_begin_by",
            request.Reconciliation.MustBeginByUtc);
        AddTimestamp(
            command,
            "reconciliation_must_complete_by",
            request.Reconciliation.MustCompleteByUtc);
        command.Parameters.AddWithValue(
            "authorization_content",
            NpgsqlDbType.Bytea,
            authorizationContent);
        AddUuid(command, "audit_event_id", request.AuditEventId);
    }

    private static void ValidateAuthorizationRequest(
        TenantExecutionContext context,
        BrokerCommandAuthorizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Command);
        ArgumentNullException.ThrowIfNull(request.Provenance);
        ArgumentNullException.ThrowIfNull(request.Exposure);
        ArgumentNullException.ThrowIfNull(request.RiskInput);
        ArgumentNullException.ThrowIfNull(request.RiskDecision);
        ArgumentNullException.ThrowIfNull(request.ExecutionLease);
        ArgumentNullException.ThrowIfNull(request.ExecutionSafety);
        ArgumentNullException.ThrowIfNull(request.Reconciliation);
        RequireNonEmpty(request.RiskDecisionId, nameof(request.RiskDecisionId));
        RequireNonEmpty(request.AuditEventId, nameof(request.AuditEventId));

        ExecutionLeaseBinding binding = request.ExecutionLease.Claims.Binding;
        string expectedAction = ToStorage(request.RiskDecision.ActionClass);
        DateTimeOffset oldest = OldestObservedAt(request.Exposure);
        if (context.TenantId != request.Provenance.TenantId
            || context.CorrelationId != request.Command.CommandId
            || context.ActorId != binding.StrategyHostWorkloadId
            || request.Command.CommandId != request.Reconciliation.CommandId
            || request.Command.DeploymentId != request.Exposure.DeploymentId
            || request.Command.DeploymentId != binding.DeploymentId
            || request.Command.Generation != request.Exposure.Generation
            || request.Command.Generation != binding.Generation
            || request.Provenance.TenantId != request.Exposure.TenantId
            || request.Provenance.TenantId != binding.TenantId
            || request.Provenance.BrokerAccountId != request.Exposure.BrokerAccountId
            || request.Provenance.BrokerAccountId != binding.BrokerAccountId
            || request.Provenance.GatewayArtifactId != request.Exposure.GatewayArtifactId
            || !FixedTimeEquals(
                request.Provenance.GatewayArtifactSha256,
                request.Exposure.GatewayArtifactSha256)
            || request.Provenance.StrategyId != binding.StrategyId
            || request.Provenance.StrategyVersionId != binding.StrategyVersionId
            || request.Provenance.StrategyVersion != binding.StrategyVersion
            || !FixedTimeEquals(
                request.Provenance.StrategyPackageSha256,
                binding.StrategyPackageSha256)
            || !request.RiskDecision.IsAllowed
            || request.RiskInput.ActionClass != request.RiskDecision.ActionClass
            || request.RiskDecision.InputDigest != CanonicalJson.Sha256(request.RiskInput)
            || !FixedTimeEquals(request.RiskDecision.PolicyDigest, binding.SafetyPolicySha256)
            || request.RiskInput.EvaluatedAtUtc.Offset != TimeSpan.Zero
            || request.Command.CreatedAtUtc.Offset != TimeSpan.Zero
            || request.Command.CreatedAtUtc != request.RiskInput.EvaluatedAtUtc
            || oldest > request.RiskInput.EvaluatedAtUtc
            || request.Exposure.ContractVersion !=
                BrokerCommandAuthorizationContractVersions.ExposureSnapshotV1
            || request.Exposure.SourceKind != "gateway_reconciliation"
            || request.Exposure.SourceSequence <= 0
            || request.ExecutionSafety.PolicyVersionWatermark < 0
            || request.Reconciliation.ContractVersion !=
                BrokerCommandAuthorizationContractVersions.ReconciliationV1
            || request.Reconciliation.Method != "orders_positions_deals"
            || request.Reconciliation.MustBeginByUtc.Offset != TimeSpan.Zero
            || request.Reconciliation.MustCompleteByUtc.Offset != TimeSpan.Zero
            || request.Reconciliation.MustBeginByUtc > request.Reconciliation.MustCompleteByUtc
            || request.Reconciliation.MustCompleteByUtc >
                request.ExecutionLease.Claims.GraceExpiresAtUtc
            || !CommandMatchesRiskAction(request.Command.Action, expectedAction)
            || request.RiskInput.Timestamps is null
            || request.RiskInput.RiskDayState is null
            || request.RiskInput.Exposure is null
            || request.RiskInput.Timestamps.QuoteAsOfUtc != request.Exposure.QuoteAsOfUtc
            || request.RiskInput.Timestamps.AccountAsOfUtc != request.Exposure.AccountAsOfUtc
            || request.RiskInput.Timestamps.PositionAsOfUtc != request.Exposure.PositionAsOfUtc
            || request.RiskInput.Timestamps.OrderAsOfUtc != request.Exposure.OrderAsOfUtc
            || request.RiskInput.Timestamps.SymbolAsOfUtc != request.Exposure.SymbolAsOfUtc
            || request.RiskInput.Timestamps.ConversionRateAsOfUtc !=
                request.Exposure.ConversionRateAsOfUtc
            || request.RiskInput.RiskDayState.AsOfUtc != request.Exposure.RiskDayAsOfUtc
            || request.RiskInput.Exposure.OrderRateSnapshotAsOfUtc !=
                request.Exposure.OrderRateAsOfUtc)
        {
            throw new DomainException(
                "BROKER_COMMAND_AUTHORIZATION_REQUEST_INVALID",
                "The broker-command authorization request is incomplete or inconsistently bound.");
        }

        ValidateUtcExposure(request.Exposure);
    }

    private static void ValidateUtcExposure(BrokerExposureSnapshotDocument exposure)
    {
        DateTimeOffset[] timestamps =
        [
            exposure.QuoteAsOfUtc,
            exposure.AccountAsOfUtc,
            exposure.PositionAsOfUtc,
            exposure.OrderAsOfUtc,
            exposure.SymbolAsOfUtc,
            exposure.ConversionRateAsOfUtc,
            exposure.RiskDayAsOfUtc,
            exposure.OrderRateAsOfUtc
        ];
        if (timestamps.Any(timestamp => timestamp.Offset != TimeSpan.Zero))
        {
            throw new DomainException(
                "BROKER_EXPOSURE_TIMESTAMP_NOT_UTC",
                "Every broker-exposure timestamp must use UTC.");
        }
    }

    private static DateTimeOffset OldestObservedAt(BrokerExposureSnapshotDocument exposure) =>
        new[]
        {
            exposure.QuoteAsOfUtc,
            exposure.AccountAsOfUtc,
            exposure.PositionAsOfUtc,
            exposure.OrderAsOfUtc,
            exposure.SymbolAsOfUtc,
            exposure.ConversionRateAsOfUtc,
            exposure.RiskDayAsOfUtc,
            exposure.OrderRateAsOfUtc
        }.Min();

    private static string ToStorage(RiskActionClass actionClass) => actionClass switch
    {
        RiskActionClass.ExposureIncrease => "exposure_increase",
        RiskActionClass.ExposureReduction => "exposure_reduction",
        RiskActionClass.Protection => "protection",
        RiskActionClass.PendingOrderCancellation => "pending_order_cancellation",
        RiskActionClass.EmergencyClose => "emergency_close",
        _ => throw new ArgumentOutOfRangeException(nameof(actionClass))
    };

    private static bool CommandMatchesRiskAction(BrokerCommandAction command, string risk) =>
        command switch
        {
            BrokerCommandAction.Place => risk == "exposure_increase",
            BrokerCommandAction.ModifyProtection => risk == "protection",
            BrokerCommandAction.Cancel => risk == "pending_order_cancellation",
            BrokerCommandAction.Close => risk is "exposure_reduction" or "emergency_close",
            _ => false
        };

    private static string ToStorage(GatewayCommandDisposition disposition) => disposition switch
    {
        GatewayCommandDisposition.Accepted => "accepted",
        GatewayCommandDisposition.Rejected => "rejected",
        GatewayCommandDisposition.Unknown => "unknown",
        GatewayCommandDisposition.SubmissionDisabled => "submission_disabled",
        _ => throw new ArgumentOutOfRangeException(nameof(disposition))
    };

    private static byte[] CanonicalBytes<T>(T value) =>
        Encoding.UTF8.GetBytes(CanonicalJson.Serialize(value));

    private static T DeserializeCanonical<T>(byte[] content, string evidenceName)
        where T : class
    {
        T value;
        try
        {
            value = JsonSerializer.Deserialize<T>(content, WebJson)
                ?? throw new JsonException("The document was null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"PostgreSQL returned malformed {evidenceName} evidence.",
                exception);
        }

        byte[] canonical = CanonicalBytes(value);
        try
        {
            if (canonical.Length != content.Length
                || !CryptographicOperations.FixedTimeEquals(canonical, content))
            {
                throw new InvalidOperationException(
                    $"PostgreSQL returned non-canonical {evidenceName} evidence.");
            }

            return value;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonical);
        }
    }

    private static string Sha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static bool FixedTimeEquals(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right));
    }

    private static void RequireNonEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty identifier is required.", parameterName);
        }
    }

    private static void RequireDigest(string? value, string parameterName)
    {
        if (value is null
            || value.Length != 64
            || value.Any(character => character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("A lowercase SHA-256 digest is required.", parameterName);
        }
    }

    private static void AddUuid(NpgsqlCommand command, string name, Guid value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Uuid, value);

    private static void AddTimestamp(NpgsqlCommand command, string name, DateTimeOffset value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.TimestampTz, value.ToUniversalTime());

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.AddWithValue(
            name,
            NpgsqlDbType.Text,
            value is null ? DBNull.Value : value);
}
