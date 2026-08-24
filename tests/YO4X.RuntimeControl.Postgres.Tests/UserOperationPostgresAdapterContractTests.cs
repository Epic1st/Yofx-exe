using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Runtime.Contracts;

namespace YO4X.RuntimeControl.Postgres.Tests;

public sealed class UserOperationPostgresAdapterContractTests
{
    [Fact]
    public void ProtocolIdsAreStableAcrossTransportMetadataButBoundToPurposeAndWorkload()
    {
        WorkloadActor actor = Actor("gateway_host");
        RequestMetadata first = Metadata("stable-key", Id(20), "first");
        RequestMetadata transportRetry = Metadata("stable-key", Id(21), "second");
        Guid attemptId = Id(30);
        Guid claimId = Id(31);

        Guid invocation = UserOperationProtocolIdentity.Create(
            UserOperationProtocolIdentityPurpose.Invocation,
            actor,
            first,
            attemptId,
            claimId);
        Guid retry = UserOperationProtocolIdentity.Create(
            UserOperationProtocolIdentityPurpose.Invocation,
            actor,
            transportRetry,
            attemptId,
            claimId);
        Guid startReceipt = UserOperationProtocolIdentity.Create(
            UserOperationProtocolIdentityPurpose.StartReceipt,
            actor,
            first,
            attemptId,
            claimId);
        Guid otherWorkload = UserOperationProtocolIdentity.Create(
            UserOperationProtocolIdentityPurpose.Invocation,
            actor with { WorkloadId = Id(99) },
            first,
            attemptId,
            claimId);

        Assert.Equal(invocation, retry);
        Assert.Equal(8, invocation.Version);
        Assert.NotEqual(invocation, startReceipt);
        Assert.NotEqual(invocation, otherWorkload);
    }

    [Fact]
    public void ProtocolBearersAreIndependentCanonicalThirtyTwoByteSecrets()
    {
        UserOperationBearer first = UserOperationProtocolIdentity.CreateBearer();
        UserOperationBearer second = UserOperationProtocolIdentity.CreateBearer();

        Assert.NotEqual(first.DangerousGetValue(), second.DangerousGetValue());
        Assert.Equal(43, first.DangerousGetValue().Length);
        Assert.Equal(43, second.DangerousGetValue().Length);
    }

    [Fact]
    public async Task ExactConcurrentProtocolTransitionsShareOneExecutionAndResult()
    {
        var singleFlight = new UserOperationProtocolSingleFlight<object>();
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new object();
        int transitionCount = 0;

        Task<object> first = singleFlight.RunAsync(
            Id(12),
            new string('a', 64),
            async transitionCancellationToken =>
            {
                Assert.False(transitionCancellationToken.CanBeCanceled);
                Interlocked.Increment(ref transitionCount);
                entered.SetResult();
                await release.Task.ConfigureAwait(false);
                return expected;
            },
            CancellationToken.None);
        await entered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Task<object> second = singleFlight.RunAsync(
            Id(12),
            new string('a', 64),
            _ => throw new InvalidOperationException(
                "An exact concurrent call must not start a second transition."),
            CancellationToken.None);

        release.SetResult();
        object[] results = await Task.WhenAll(first, second)
            .WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        Assert.Equal(1, transitionCount);
        Assert.Same(expected, results[0]);
        Assert.Same(expected, results[1]);
    }

    [Fact]
    public async Task ConflictingConcurrentRequestCannotJoinStableProtocolAuthority()
    {
        var singleFlight = new UserOperationProtocolSingleFlight<object>();
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int transitionCount = 0;

        Task<object> first = singleFlight.RunAsync(
            Id(13),
            new string('a', 64),
            async _ =>
            {
                Interlocked.Increment(ref transitionCount);
                entered.SetResult();
                await release.Task.ConfigureAwait(false);
                return new object();
            },
            CancellationToken.None);
        await entered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            singleFlight.RunAsync(
                Id(13),
                new string('b', 64),
                _ =>
                {
                    Interlocked.Increment(ref transitionCount);
                    return Task.FromResult(new object());
                },
                CancellationToken.None));

        release.SetResult();
        await first.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, transitionCount);
    }

    [Fact]
    public async Task WaiterCancellationDoesNotCancelOrDuplicateSharedTransition()
    {
        var singleFlight = new UserOperationProtocolSingleFlight<object>();
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var canceledWaiter = new CancellationTokenSource();
        int transitionCount = 0;

        Task<object> owner = singleFlight.RunAsync(
            Id(14),
            new string('c', 64),
            async transitionCancellationToken =>
            {
                Assert.False(transitionCancellationToken.CanBeCanceled);
                Interlocked.Increment(ref transitionCount);
                entered.SetResult();
                await release.Task.ConfigureAwait(false);
                return new object();
            },
            CancellationToken.None);
        await entered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Task<object> waiter = singleFlight.RunAsync(
            Id(14),
            new string('c', 64),
            _ => throw new InvalidOperationException(
                "A canceled waiter must not own the shared transition."),
            canceledWaiter.Token);

        canceledWaiter.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        release.SetResult();
        await owner.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, transitionCount);
    }

    [Fact]
    public async Task CompletedSingleFlightDoesNotSuppressSequentialRetry()
    {
        var singleFlight = new UserOperationProtocolSingleFlight<int>();
        int transitionCount = 0;

        int first = await singleFlight.RunAsync(
            Id(15),
            new string('d', 64),
            _ => Task.FromResult(Interlocked.Increment(ref transitionCount)),
            CancellationToken.None);
        int second = await singleFlight.RunAsync(
            Id(15),
            new string('d', 64),
            _ => Task.FromResult(Interlocked.Increment(ref transitionCount)),
            CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(2, transitionCount);
    }

    [Fact]
    public void GatewayBeginSingleFlightFingerprintBindsClaimGenerationAndBearer()
    {
        string first = UserOperationProtocolIdentity.CreateDeliveryClaimFingerprint(
            Bearer(1),
            1);
        string rotatedGeneration =
            UserOperationProtocolIdentity.CreateDeliveryClaimFingerprint(
                Bearer(1),
                2);
        string rotatedBearer =
            UserOperationProtocolIdentity.CreateDeliveryClaimFingerprint(
                Bearer(2),
                1);

        Assert.True(UserOperationProtocolPostgresCommand.IsSha256(first));
        Assert.NotEqual(first, rotatedGeneration);
        Assert.NotEqual(first, rotatedBearer);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UserOperationProtocolIdentity.CreateDeliveryClaimFingerprint(
                Bearer(1),
                0));
    }

    [Fact]
    public void GatewayObservationReturnRequiresExactCanonicalTargetBinding()
    {
        UserOperationBrokerTargetObservation expected =
            UserOperationBrokerTargetObservation.Create(
                "active",
                "ready",
                brokerConfirmed: true);

        Assert.True(PostgresUserOperationGatewayApplication
            .IsExactReturnedTargetObservation(
                """
                {"credentialState":"ready","brokerConfirmed":true,"accountState":"active"}
                """,
                expected));
        Assert.False(PostgresUserOperationGatewayApplication
            .IsExactReturnedTargetObservation(
                """
                {"accountState":"active","brokerConfirmed":true,"credentialState":"disabled"}
                """,
                expected));
        Assert.False(PostgresUserOperationGatewayApplication
            .IsExactReturnedTargetObservation(
                """
                {"accountState":"active","brokerConfirmed":true,"credentialState":"ready","extra":true}
                """,
                expected));
        Assert.False(PostgresUserOperationGatewayApplication
            .IsExactReturnedTargetObservation("not-json", expected));
        Assert.False(PostgresUserOperationGatewayApplication
            .IsExactReturnedTargetObservation("[]", expected));
    }

    [Fact]
    public void IrreversibleAdaptersUseStableIdentitySingleFlightBeforeBearerMinting()
    {
        string supervisor = File.ReadAllText(FindRepositoryFile(
            "src", "Infrastructure", "YO4X.RuntimeControl.Postgres",
            "PostgresUserOperationSupervisorDeliveryApplication.cs"));
        string gateway = File.ReadAllText(FindRepositoryFile(
            "src", "Infrastructure", "YO4X.RuntimeControl.Postgres",
            "PostgresUserOperationGatewayApplication.cs"));
        string primitive = File.ReadAllText(FindRepositoryFile(
            "src", "Infrastructure", "YO4X.RuntimeControl.Postgres",
            "UserOperationProtocolSingleFlight.cs"));

        Assert.Contains("claimSingleFlight.RunAsync(", supervisor, StringComparison.Ordinal);
        Assert.Contains(
            "CreateBearerFingerprint(",
            supervisor,
            StringComparison.Ordinal);
        Assert.Contains(
            "request.DeliveryCapability",
            supervisor,
            StringComparison.Ordinal);
        Assert.Contains(
            "transitionCancellationToken => ClaimForGatewayCoreAsync(",
            supervisor,
            StringComparison.Ordinal);
        Assert.True(
            supervisor.IndexOf("claimSingleFlight.RunAsync(", StringComparison.Ordinal)
            < supervisor.IndexOf(
                "UserOperationProtocolIdentity.CreateBearer();",
                StringComparison.Ordinal));
        Assert.Contains("beginSingleFlight.RunAsync(", gateway, StringComparison.Ordinal);
        Assert.Contains(
            "CreateDeliveryClaimFingerprint(",
            gateway,
            StringComparison.Ordinal);
        Assert.Contains(
            "request.GatewayCapability",
            gateway,
            StringComparison.Ordinal);
        Assert.Contains(
            "request.DeliveryClaimGeneration",
            gateway,
            StringComparison.Ordinal);
        Assert.Contains(
            "transitionCancellationToken => BeginCoreAsync(",
            gateway,
            StringComparison.Ordinal);
        Assert.True(
            gateway.IndexOf("beginSingleFlight.RunAsync(", StringComparison.Ordinal)
            < gateway.IndexOf(
                "UserOperationBearer redemption =",
                StringComparison.Ordinal));
        Assert.Contains(
            "transition(CancellationToken.None)",
            primitive,
            StringComparison.Ordinal);
        Assert.Contains(
            ".WaitAsync(cancellationToken)",
            primitive,
            StringComparison.Ordinal);
    }

    [Fact]
    public void InvocationAdaptersBindGenerationAndObservationSignaturesByName()
    {
        string supervisor = File.ReadAllText(FindRepositoryFile(
            "src", "Infrastructure", "YO4X.RuntimeControl.Postgres",
            "PostgresUserOperationSupervisorDeliveryApplication.cs"));
        string gateway = File.ReadAllText(FindRepositoryFile(
            "src", "Infrastructure", "YO4X.RuntimeControl.Postgres",
            "PostgresUserOperationGatewayApplication.cs"));
        string migration = File.ReadAllText(FindRepositoryFile(
            "src", "BuildingBlocks", "YO4X.Persistence.Postgres", "Migrations",
            "002_user_operation_invocation_protocol.sql"));

        int claimCall = supervisor.IndexOf(
            "from control.claim_user_operation_delivery(",
            StringComparison.Ordinal);
        Assert.True(claimCall >= 0);
        int namedClaimAttempt = supervisor.IndexOf(
            "p_attempt_id => @attempt_id",
            claimCall,
            StringComparison.Ordinal);
        Assert.InRange(namedClaimAttempt - claimCall, 1, 100);
        Assert.Contains(
            "p_delivery_claim_generation => @delivery_claim_generation",
            supervisor,
            StringComparison.Ordinal);
        Assert.Contains(
            "p_delivery_claim_generation => @delivery_claim_generation",
            gateway,
            StringComparison.Ordinal);
        Assert.Contains(
            "p_target_observation => @target_observation",
            gateway,
            StringComparison.Ordinal);
        Assert.Contains(
            "p_observed_at => @observed_at",
            gateway,
            StringComparison.Ordinal);
        Assert.True(
            gateway.IndexOf(
                "p_target_observation => @target_observation",
                StringComparison.Ordinal)
            < gateway.IndexOf(
                "p_observed_at => @observed_at",
                StringComparison.Ordinal));
        string normalizedGateway = gateway.Replace(
            "\r\n",
            "\n",
            StringComparison.Ordinal);
        Assert.Contains(
            "observation_receipt_sha256,\n                    target_observation,\n                    observed_at,",
            normalizedGateway,
            StringComparison.Ordinal);
        Assert.Contains(
            "string returnedTargetObservationJson = reader.GetString(7);",
            gateway,
            StringComparison.Ordinal);
        Assert.Contains(
            "UserOperationProtocolPostgresCommand.Utc(reader, 8)",
            gateway,
            StringComparison.Ordinal);
        Assert.Contains(
            "UserOperationProtocolPostgresCommand.Utc(reader, 9)",
            gateway,
            StringComparison.Ordinal);
        Assert.Contains("reader.GetInt64(10)", gateway, StringComparison.Ordinal);
        Assert.Contains(
            "IsExactReturnedTargetObservation(",
            gateway,
            StringComparison.Ordinal);

        int beginSignature = migration.IndexOf(
            "create function control.begin_user_operation_gateway_invocation(",
            StringComparison.Ordinal);
        Assert.True(beginSignature >= 0);
        int beginGeneration = migration.IndexOf(
            "p_delivery_claim_generation integer",
            beginSignature,
            StringComparison.Ordinal);
        Assert.InRange(beginGeneration - beginSignature, 1, 300);

        int observationSignature = migration.IndexOf(
            "create function control.record_user_operation_gateway_observation_v5(",
            StringComparison.Ordinal);
        Assert.True(observationSignature >= 0);
        int targetObservation = migration.IndexOf(
            "p_target_observation jsonb,",
            observationSignature,
            StringComparison.Ordinal);
        Assert.InRange(targetObservation - observationSignature, 1, 600);
    }

    [Theory]
    [InlineData(999, 120_000)]
    [InlineData(120_001, 120_000)]
    [InlineData(30_000, 14_999)]
    [InlineData(30_000, 300_001)]
    public void InvocationOptionsRejectDatabaseIncompatibleBounds(
        int claimMilliseconds,
        int receiptMilliseconds)
    {
        var options = new UserOperationInvocationPostgresOptions
        {
            DeliveryClaimLifetime = TimeSpan.FromMilliseconds(claimMilliseconds),
            GatewayReceiptLifetime = TimeSpan.FromMilliseconds(receiptMilliseconds)
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public async Task RoleSpecificPoolsRejectWrongLoginBeforeAnyConnection()
    {
        const string wrong =
            "Host=127.0.0.1;Port=1;Database=yo4x;Username=yo4x_worker;Password=x;SSL Mode=Disable";

        Assert.Throws<ArgumentException>(() =>
            new SupervisorUserOperationPostgresDatabase(wrong));
        Assert.Throws<ArgumentException>(() =>
            new GatewayUserOperationPostgresDatabase(wrong));
        Assert.Throws<ArgumentException>(() =>
            new CredentialUserOperationPostgresDatabase(wrong));

        await Task.CompletedTask;
    }

    [Fact]
    public async Task AdaptersRejectWrongComponentBeforeOpeningDatabase()
    {
        const string connection =
            "Host=127.0.0.1;Port=1;Database=yo4x;Username=yo4x_gateway_runtime;Password=x;SSL Mode=Disable";
        await using var database = new GatewayUserOperationPostgresDatabase(
            connection,
            allowInsecureLoopbackForDevelopment: true);
        var adapter = new PostgresUserOperationGatewayApplication(
            database,
            new UserOperationInvocationPostgresOptions());
        UserOperationGatewayBeginRequest request =
            UserOperationGatewayBeginRequest.Create(
                Id(40),
                Id(41),
                Id(42),
                3,
                Bearer(7));

        AuthorizationDeniedException failure =
            await Assert.ThrowsAsync<AuthorizationDeniedException>(() =>
                adapter.BeginAsync(
                    Actor("supervisor"),
                    request,
                    Metadata("wrong-role", Id(43), null),
                    CancellationToken.None));

        Assert.Equal("USER_OPERATION_WORKLOAD_ROLE_REQUIRED", failure.Code);
    }

    [Fact]
    public async Task ProtocolPoolsRequireVerifyFullByDefault()
    {
        const string plaintextSupervisor =
            "Host=127.0.0.1;Port=1;Database=yo4x;Username=yo4x_supervisor_runtime;Password=x;SSL Mode=Disable";
        const string verifiedGateway =
            "Host=db.internal.example;Port=5432;Database=yo4x;Username=yo4x_gateway_runtime;Password=x;SSL Mode=VerifyFull";
        const string plaintextEvidence =
            "Host=127.0.0.1;Port=1;Database=yo4x;Username=yo4x_runtime_evidence;Password=x;SSL Mode=Disable";

        Assert.Throws<ArgumentException>(() =>
            new SupervisorUserOperationPostgresDatabase(plaintextSupervisor));
        Assert.Throws<ArgumentException>(() =>
            new RuntimeEvidencePostgresDatabase(plaintextEvidence));
        await using var accepted = new GatewayUserOperationPostgresDatabase(verifiedGateway);
        await using var acceptedDevelopmentEvidence = new RuntimeEvidencePostgresDatabase(
            plaintextEvidence,
            allowInsecureLoopbackForDevelopment: true);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("localhost")]
    public async Task DevelopmentPlaintextEscapeAcceptsOnlyExplicitLoopback(string host)
    {
        string connection =
            $"Host={host};Port=1;Database=yo4x;Username=yo4x_credential_runtime;Password=x;SSL Mode=Disable";

        await using var accepted = new CredentialUserOperationPostgresDatabase(
            connection,
            allowInsecureLoopbackForDevelopment: true);
    }

    [Theory]
    [InlineData("db.internal.example")]
    [InlineData("127.0.0.1,db.internal.example")]
    [InlineData("0.0.0.0")]
    public void DevelopmentPlaintextEscapeRejectsNonLoopbackTargets(string host)
    {
        string connection =
            $"Host={host};Port=5432;Database=yo4x;Username=yo4x_gateway_runtime;Password=x;SSL Mode=Disable";

        Assert.Throws<ArgumentException>(() =>
            new GatewayUserOperationPostgresDatabase(
                connection,
                allowInsecureLoopbackForDevelopment: true));
    }

    [Theory]
    [InlineData("Include Error Detail=true")]
    [InlineData("Log Parameters=true")]
    [InlineData("Options=-c statement_timeout=0")]
    [InlineData("No Reset On Close=true")]
    public void DevelopmentEscapeDoesNotBypassSessionOrDiagnosticSafety(string unsafeSetting)
    {
        string connection =
            "Host=127.0.0.1;Port=1;Database=yo4x;Username=yo4x_gateway_runtime;"
            + $"Password=x;SSL Mode=Disable;{unsafeSetting}";

        Assert.Throws<ArgumentException>(() =>
            new GatewayUserOperationPostgresDatabase(
                connection,
                allowInsecureLoopbackForDevelopment: true));
    }

    [Fact]
    public void ProviderCommandDescriptorIsStrictlyBoundToItsCanonicalDigest()
    {
        DateTimeOffset deadline = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        string targetBindingSha256 = new('a', 64);
        var descriptor = new
        {
            operationId = Id(60).ToString("D"),
            operationType = "deployment.start",
            requestedTargetState = "running",
            submittedResourceVersion = 11L,
            targetBindingSha256,
            targetId = Id(4).ToString("D"),
            targetType = "deployment",
            tenantId = Id(1).ToString("D")
        };
        string descriptorJson = CanonicalJson.Serialize(descriptor);
        string commandSha256 = CanonicalJson.Sha256(descriptor);

        UserOperationProviderCommand command = UserOperationProviderCommandDescriptor.Parse(
            descriptorJson,
            Id(1),
            Id(60),
            "deployment.start",
            "deployment",
            Id(4),
            Id(5),
            commandSha256,
            deadline);

        Assert.Equal(Id(60), command.OperationId);
        Assert.Equal(Id(5), command.BrokerAccountId);
        Assert.Equal(11, command.SubmittedResourceVersion);
        Assert.Equal(deadline, command.ExecuteNotAfterUtc);
        Assert.Throws<InvalidOperationException>(() =>
            UserOperationProviderCommandDescriptor.Parse(
                descriptorJson,
                Id(1),
                Id(60),
                "deployment.start",
                "deployment",
                Id(4),
                Id(5),
                new string('b', 64),
                deadline));
    }

    [Fact]
    public void ProviderCommandDescriptorRejectsUnknownOrCrossTargetEvidence()
    {
        DateTimeOffset deadline = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        string targetBindingSha256 = new('c', 64);
        var descriptor = new
        {
            operationId = Id(61).ToString("D"),
            operationType = "deployment.close_only",
            requestedTargetState = "close_only",
            submittedResourceVersion = 12L,
            targetBindingSha256,
            targetId = Id(4).ToString("D"),
            targetType = "deployment",
            tenantId = Id(1).ToString("D")
        };
        string commandSha256 = CanonicalJson.Sha256(descriptor);
        string descriptorWithUnknownProperty =
            CanonicalJson.Serialize(new
            {
                descriptor.operationId,
                descriptor.operationType,
                descriptor.requestedTargetState,
                descriptor.submittedResourceVersion,
                descriptor.targetBindingSha256,
                descriptor.targetId,
                descriptor.targetType,
                descriptor.tenantId,
                unknown = true
            });

        Assert.Throws<InvalidOperationException>(() =>
            UserOperationProviderCommandDescriptor.Parse(
                descriptorWithUnknownProperty,
                Id(1),
                Id(61),
                "deployment.close_only",
                "deployment",
                Id(4),
                Id(5),
                commandSha256,
                deadline));
        Assert.Throws<InvalidOperationException>(() =>
            UserOperationProviderCommandDescriptor.Parse(
                CanonicalJson.Serialize(descriptor),
                Id(1),
                Id(61),
                "deployment.close_only",
                "deployment",
                Id(99),
                Id(5),
                commandSha256,
                deadline));
    }

    private static WorkloadActor Actor(string component) => new(
        Id(1),
        Id(2),
        Id(3),
        Id(4),
        Id(5),
        7,
        "test-region",
        component);

    private static RequestMetadata Metadata(
        string idempotencyKey,
        Guid correlationId,
        string? reason) =>
        new(idempotencyKey, correlationId, null, reason);

    private static Guid Id(int suffix) =>
        Guid.Parse($"a0000000-0000-0000-0000-{suffix:D12}");

    private static UserOperationBearer Bearer(byte value)
    {
        string encoded = Convert.ToBase64String(Enumerable.Repeat(value, 32).ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return UserOperationBearer.Create(encoded);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("The repository file was not found.");
    }
}
