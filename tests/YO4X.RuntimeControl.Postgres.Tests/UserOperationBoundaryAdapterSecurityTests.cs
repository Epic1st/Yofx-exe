using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Runtime.Contracts;

namespace YO4X.RuntimeControl.Postgres.Tests;

public sealed class UserOperationBoundaryAdapterSecurityTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ProviderCommandDescriptorRejectsDuplicateMissingAndTypeDriftedFields()
    {
        string descriptor = DescriptorJson();
        string commandSha256 = DescriptorSha256();
        string duplicateOperationId = descriptor.Replace(
            "\"operationId\":",
            $"\"operationId\":\"{Id(20):D}\",\"operationId\":",
            StringComparison.Ordinal);
        string missingRequestedState = descriptor.Replace(
            "\"requestedTargetState\":\"running\",",
            string.Empty,
            StringComparison.Ordinal);
        string stringResourceVersion = descriptor.Replace(
            "\"submittedResourceVersion\":17",
            "\"submittedResourceVersion\":\"17\"",
            StringComparison.Ordinal);
        string nonCanonicalOperationId = descriptor.Replace(
            Id(20).ToString("D"),
            Id(20).ToString("D").ToUpperInvariant(),
            StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => ParseDescriptor(
            duplicateOperationId,
            commandSha256));
        Assert.Throws<InvalidOperationException>(() => ParseDescriptor(
            missingRequestedState,
            commandSha256));
        Assert.Throws<InvalidOperationException>(() => ParseDescriptor(
            stringResourceVersion,
            commandSha256));
        Assert.Throws<InvalidOperationException>(() => ParseDescriptor(
            nonCanonicalOperationId,
            commandSha256));
    }

    [Fact]
    public void ProviderCommandDescriptorRejectsEveryMismatchedImmutableBinding()
    {
        string descriptor = DescriptorJson();
        string commandSha256 = DescriptorSha256();

        Assert.Throws<InvalidOperationException>(() => UserOperationProviderCommandDescriptor.Parse(
            descriptor,
            Id(99),
            Id(20),
            "deployment.start",
            "deployment",
            Id(30),
            Id(40),
            commandSha256,
            Now.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => UserOperationProviderCommandDescriptor.Parse(
            descriptor,
            Id(10),
            Id(99),
            "deployment.start",
            "deployment",
            Id(30),
            Id(40),
            commandSha256,
            Now.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => UserOperationProviderCommandDescriptor.Parse(
            descriptor,
            Id(10),
            Id(20),
            "deployment.close_only",
            "deployment",
            Id(30),
            Id(40),
            commandSha256,
            Now.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => UserOperationProviderCommandDescriptor.Parse(
            descriptor,
            Id(10),
            Id(20),
            "deployment.start",
            "broker_account",
            Id(30),
            Id(40),
            commandSha256,
            Now.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => UserOperationProviderCommandDescriptor.Parse(
            descriptor,
            Id(10),
            Id(20),
            "deployment.start",
            "deployment",
            Id(99),
            Id(40),
            commandSha256,
            Now.AddMinutes(1)));
    }

    [Fact]
    public void GatewayResultSubmissionBindsEveryFieldAndHashesExactCanonicalRequest()
    {
        UserOperationGatewayResultV5 request = GatewayResult();
        object submission = CreateSubmission("FromGateway", request);

        Assert.Equal(request.ResultId, Required<Guid>(submission, "ResultId"));
        Assert.Equal(request.AttemptId, Required<Guid>(submission, "AttemptId"));
        Assert.Equal(request.InvocationId, Required<Guid>(submission, "InvocationId"));
        Assert.Equal(request.OperationId, Required<Guid>(submission, "OperationId"));
        Assert.Equal(request.DispatchMessageId, Required<Guid>(submission, "DispatchMessageId"));
        Assert.Equal(
            request.GatewayStartReceiptId,
            Required<Guid>(submission, "GatewayStartReceiptId"));
        Assert.Equal(
            request.ProviderCallAuthorizationReceiptId,
            Required<Guid>(submission, "ProviderCallAuthorizationReceiptId"));
        Assert.Equal(
            request.GatewayObservationReceiptId,
            Required<Guid>(submission, "GatewayObservationReceiptId"));
        Assert.Equal(request.GatewayReceiptSha256, Required<string>(submission, "GatewayReceiptSha256"));
        Assert.Null(Property(submission, "ChallengeConsumptionId"));
        Assert.Null(Property(submission, "ChallengeId"));
        Assert.Null(Property(submission, "ChallengeMessageId"));
        Assert.Same(request.ResultCapability, Required<UserOperationBearer>(submission, "ResultCapability"));
        AssertResultEvidenceBindings(submission, request);
        Assert.Equal(
            Sha256Utf8(request.ToCanonicalJson()),
            Required<string>(submission, "RequestSha256"));
    }

    [Fact]
    public void ReconciliationResultSubmissionBindsChallengeAndHashesExactCanonicalRequest()
    {
        UserOperationReconciliationResultV5 request = ReconciliationResult();
        object submission = CreateSubmission("FromReconciliation", request);

        Assert.Equal(request.ResultId, Required<Guid>(submission, "ResultId"));
        Assert.Equal(request.AttemptId, Required<Guid>(submission, "AttemptId"));
        Assert.Null(Property(submission, "InvocationId"));
        Assert.Equal(request.OperationId, Required<Guid>(submission, "OperationId"));
        Assert.Equal(
            request.OriginalDispatchMessageId,
            Required<Guid>(submission, "DispatchMessageId"));
        Assert.Equal(
            request.GatewayStartReceiptId,
            Required<Guid>(submission, "GatewayStartReceiptId"));
        Assert.Equal(
            request.ProviderCallAuthorizationReceiptId,
            Required<Guid>(submission, "ProviderCallAuthorizationReceiptId"));
        Assert.Null(Property(submission, "GatewayObservationReceiptId"));
        Assert.Null(Property(submission, "GatewayReceiptSha256"));
        Assert.Equal(
            request.ChallengeConsumptionId,
            Required<Guid>(submission, "ChallengeConsumptionId"));
        Assert.Equal(request.ChallengeId, Required<Guid>(submission, "ChallengeId"));
        Assert.Equal(
            request.ChallengeMessageId,
            Required<Guid>(submission, "ChallengeMessageId"));
        Assert.Same(
            request.ChallengeResultCapability,
            Required<UserOperationBearer>(submission, "ResultCapability"));
        AssertResultEvidenceBindings(submission, request);
        Assert.Equal(
            Sha256Utf8(request.ToCanonicalJson()),
            Required<string>(submission, "RequestSha256"));
    }

    [Fact]
    public void CredentialBoundarySourceCommitsAndLeavesAuthorizationScopeBeforeProviderCall()
    {
        string source = RepositoryFile(
            "src",
            "Infrastructure",
            "YO4X.RuntimeControl.Postgres",
            "PostgresUserOperationCredentialBoundaryApplication.cs");
        string execution = Slice(
            source,
            "public async Task<UserOperationProviderCallExecutionReceipt> ExecuteProviderCallOnceAsync(",
            "private async Task<ProviderAuthorization> AuthorizeAndCommitAsync(");
        string authorization = Slice(
            source,
            "private async Task<ProviderAuthorization> AuthorizeAndCommitAsync(",
            "private async Task<UserOperationProviderCallAmbiguousReceipt> RecordAmbiguityAsync(");

        int authorizationCall = execution.IndexOf(
            "ProviderAuthorization authority = await AuthorizeAndCommitAsync(",
            StringComparison.Ordinal);
        int providerCall = execution.IndexOf(
            "await providerInvoker.InvokeOnceAsync(",
            StringComparison.Ordinal);
        int transactionScope = authorization.IndexOf(
            "await using (TenantPostgresTransaction transaction",
            StringComparison.Ordinal);
        int commit = authorization.IndexOf(
            "await transaction.CommitAsync(cancellationToken)",
            StringComparison.Ordinal);
        int returnAuthority = authorization.LastIndexOf(
            "return authority;",
            StringComparison.Ordinal);

        Assert.True(authorizationCall >= 0 && providerCall > authorizationCall);
        Assert.True(transactionScope >= 0 && commit > transactionScope);
        Assert.True(returnAuthority > commit);
        Assert.DoesNotContain("providerInvoker", authorization, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderBoundaryUncertaintyExceptionsAreRedactedAndNonRetryable()
    {
        var authorization = new UserOperationProviderAuthorizationCommitUncertainException();
        var completion = new UserOperationProviderCallCompletionUncertainException();

        AssertBoundaryException(
            authorization,
            authorization.Code,
            authorization.Retryable,
            "USER_OPERATION_PROVIDER_AUTHORIZATION_COMMIT_UNCERTAIN");
        AssertBoundaryException(
            completion,
            completion.Code,
            completion.Retryable,
            "USER_OPERATION_PROVIDER_CALL_COMPLETION_UNCERTAIN");
    }

    [Fact]
    public void ProtocolPoolsAndAdaptersRejectWrongRolesBeforeDatabaseOrProviderUse()
    {
        const string wrongRuntimeEvidenceLogin =
            "Host=127.0.0.1;Port=1;Database=yo4x;Username=yo4x_gateway_runtime;Password=x;SSL Mode=Disable";

        Assert.Throws<ArgumentException>(() => new RuntimeEvidencePostgresDatabase(
            wrongRuntimeEvidenceLogin,
            allowInsecureLoopbackForDevelopment: true));
    }

    [Fact]
    public async Task CredentialAndResultAdaptersRejectWrongComponentsWithoutExternalCalls()
    {
        const string credentialConnection =
            "Host=127.0.0.1;Port=1;Database=yo4x;Username=yo4x_credential_runtime;Password=x;SSL Mode=Disable";
        const string resultConnection =
            "Host=127.0.0.1;Port=1;Database=yo4x;Username=yo4x_runtime_evidence;Password=x;SSL Mode=Disable";
        await using var credentialDatabase = new CredentialUserOperationPostgresDatabase(
            credentialConnection,
            allowInsecureLoopbackForDevelopment: true);
        await using var resultDatabase = new RuntimeEvidencePostgresDatabase(
            resultConnection,
            allowInsecureLoopbackForDevelopment: true);
        var invoker = new RecordingProviderInvoker();
        var credentialAdapter = new PostgresUserOperationCredentialBoundaryApplication(
            credentialDatabase,
            invoker,
            new UserOperationInvocationPostgresOptions());
        var resultAdapter = new PostgresUserOperationResultV5Application(resultDatabase);

        AuthorizationDeniedException credentialFailure =
            await Assert.ThrowsAsync<AuthorizationDeniedException>(() =>
                credentialAdapter.ExecuteProviderCallOnceAsync(
                    Actor("supervisor"),
                    UserOperationProviderCallExecutionRequest.Create(
                        Id(70),
                        Id(71),
                        Id(72),
                        Bearer(7)),
                    Metadata(),
                    CancellationToken.None));
        AuthorizationDeniedException gatewayResultFailure =
            await Assert.ThrowsAsync<AuthorizationDeniedException>(async () =>
                await resultAdapter.RecordGatewayResultAsync(
                    Actor("gateway_host"),
                    GatewayResult(),
                    Metadata(),
                    CancellationToken.None));
        AuthorizationDeniedException reconciliationResultFailure =
            await Assert.ThrowsAsync<AuthorizationDeniedException>(async () =>
                await resultAdapter.RecordReconciliationResultAsync(
                    Actor("gateway_host"),
                    null!,
                    Metadata(),
                    CancellationToken.None));

        Assert.Equal("USER_OPERATION_WORKLOAD_ROLE_REQUIRED", credentialFailure.Code);
        Assert.Equal("USER_OPERATION_WORKLOAD_ROLE_REQUIRED", gatewayResultFailure.Code);
        Assert.Equal("USER_OPERATION_WORKLOAD_ROLE_REQUIRED", reconciliationResultFailure.Code);
        Assert.Equal(0, invoker.CallCount);
    }

    private static void AssertResultEvidenceBindings(
        object submission,
        UserOperationGatewayResultV5 request)
    {
        Assert.Equal(request.TargetType, Required<string>(submission, "TargetType"));
        Assert.Equal(request.TargetId, Required<Guid>(submission, "TargetId"));
        Assert.Same(
            request.TargetObservation,
            Assignable<UserOperationTargetObservation>(submission, "TargetObservation"));
        Assert.Equal(
            request.SubmittedResourceVersion,
            Required<long>(submission, "SubmittedResourceVersion"));
        Assert.Equal(
            request.RequestedTargetState,
            Required<string>(submission, "RequestedTargetState"));
        Assert.Equal(
            request.DispatchTargetBindingSha256,
            Required<string>(submission, "DispatchTargetBindingSha256"));
        Assert.Equal(
            request.DispatchPolicySnapshotSha256,
            Required<string>(submission, "DispatchPolicySnapshotSha256"));
        Assert.Equal(request.Outcome, Required<UserOperationObservationOutcome>(submission, "Outcome"));
        Assert.Equal(request.ObservationSha256, Required<string>(submission, "ObservationSha256"));
        Assert.Equal(request.ObservedAtUtc, Required<DateTimeOffset>(submission, "ObservedAtUtc"));
    }

    private static void AssertResultEvidenceBindings(
        object submission,
        UserOperationReconciliationResultV5 request)
    {
        Assert.Equal(request.TargetType, Required<string>(submission, "TargetType"));
        Assert.Equal(request.TargetId, Required<Guid>(submission, "TargetId"));
        Assert.Same(
            request.TargetObservation,
            Assignable<UserOperationTargetObservation>(submission, "TargetObservation"));
        Assert.Equal(
            request.SubmittedResourceVersion,
            Required<long>(submission, "SubmittedResourceVersion"));
        Assert.Equal(
            request.RequestedTargetState,
            Required<string>(submission, "RequestedTargetState"));
        Assert.Equal(
            request.DispatchTargetBindingSha256,
            Required<string>(submission, "DispatchTargetBindingSha256"));
        Assert.Equal(
            request.DispatchPolicySnapshotSha256,
            Required<string>(submission, "DispatchPolicySnapshotSha256"));
        Assert.Equal(request.Outcome, Required<UserOperationObservationOutcome>(submission, "Outcome"));
        Assert.Equal(request.ObservationSha256, Required<string>(submission, "ObservationSha256"));
        Assert.Equal(request.ObservedAtUtc, Required<DateTimeOffset>(submission, "ObservedAtUtc"));
    }

    private static void AssertBoundaryException(
        Exception exception,
        string code,
        bool retryable,
        string expectedCode)
    {
        Assert.Equal(expectedCode, code);
        Assert.False(retryable);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("secret", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nonce", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("capability", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.All(
            exception.GetType().GetConstructors(),
            constructor => Assert.Empty(constructor.GetParameters()));
    }

    private static UserOperationProviderCommand ParseDescriptor(
        string descriptor,
        string commandSha256) =>
        UserOperationProviderCommandDescriptor.Parse(
            descriptor,
            Id(10),
            Id(20),
            "deployment.start",
            "deployment",
            Id(30),
            Id(40),
            commandSha256,
            Now.AddMinutes(1));

    private static string DescriptorJson() => CanonicalJson.Serialize(new
    {
        operationId = Id(20).ToString("D"),
        operationType = "deployment.start",
        requestedTargetState = "running",
        submittedResourceVersion = 17L,
        targetBindingSha256 = Digest('a'),
        targetId = Id(30).ToString("D"),
        targetType = "deployment",
        tenantId = Id(10).ToString("D")
    });

    private static string DescriptorSha256() => CanonicalJson.Sha256(new
    {
        operationId = Id(20).ToString("D"),
        operationType = "deployment.start",
        requestedTargetState = "running",
        submittedResourceVersion = 17L,
        targetBindingSha256 = Digest('a'),
        targetId = Id(30).ToString("D"),
        targetType = "deployment",
        tenantId = Id(10).ToString("D")
    });

    private static object CreateSubmission(string factoryName, object request)
    {
        Type submissionType = typeof(PostgresUserOperationResultV5Application).GetNestedType(
            "ResultSubmission",
            BindingFlags.NonPublic)!;
        MethodInfo factory = submissionType.GetMethod(
            factoryName,
            BindingFlags.Public | BindingFlags.Static)!;
        return factory.Invoke(null, [request])!;
    }

    private static object? Property(object instance, string propertyName) =>
        instance.GetType().GetProperty(propertyName)!.GetValue(instance);

    private static T Required<T>(object instance, string propertyName) =>
        Assert.IsType<T>(Property(instance, propertyName));

    private static T Assignable<T>(object instance, string propertyName) =>
        Assert.IsAssignableFrom<T>(Property(instance, propertyName));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string RepositoryFile(
        string firstSegment,
        params string[] remainingSegments)
    {
        string path = Path.Combine(
            [RepositoryRoot(), firstSegment, .. remainingSegments]);
        return File.ReadAllText(path);
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));

    private static string Sha256Utf8(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static WorkloadActor Actor(string component) => new(
        Id(1),
        Id(2),
        Id(3),
        Id(4),
        Id(5),
        7,
        "test-region",
        component);

    private static RequestMetadata Metadata() => new(
        "user-operation-boundary-adapter-security",
        Id(6),
        null,
        "contract-test");

    private static UserOperationGatewayResultV5 GatewayResult()
    {
        UserOperationTargetObservation observation = DeploymentObservation();
        return UserOperationGatewayResultV5.Create(
            Id(100),
            Id(101),
            Id(102),
            Id(103),
            Id(104),
            Id(105),
            Id(106),
            Id(107),
            Digest('d'),
            "deployment",
            Id(108),
            observation,
            11,
            "running",
            Digest('b'),
            Digest('c'),
            Bearer(4),
            UserOperationObservationOutcome.Succeeded,
            observation.ComputeCanonicalSha256(),
            Now.AddMinutes(10));
    }

    private static UserOperationReconciliationResultV5 ReconciliationResult()
    {
        UserOperationTargetObservation observation =
            UserOperationBrokerTargetObservation.Create("active", "ready", true);
        return UserOperationReconciliationResultV5.Create(
            Id(200),
            Id(201),
            Id(202),
            Id(203),
            Id(204),
            Id(205),
            Id(206),
            Id(207),
            Id(208),
            "broker_account",
            Id(209),
            observation,
            12,
            "disabled:ready",
            Digest('e'),
            Digest('f'),
            Bearer(5),
            UserOperationObservationOutcome.Diverged,
            observation.ComputeCanonicalSha256(),
            Now.AddMinutes(11));
    }

    private static UserOperationDeploymentTargetObservation DeploymentObservation() =>
        UserOperationDeploymentTargetObservation.Create(
            "running",
            Digest('b'),
            Digest('7'),
            true,
            Digest('8'),
            "running",
            "open");

    private static UserOperationBearer Bearer(byte value)
    {
        string encoded = Convert.ToBase64String(Enumerable.Repeat(value, 32).ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return UserOperationBearer.Create(encoded);
    }

    private static Guid Id(int suffix) =>
        Guid.Parse($"a1000000-0000-0000-0000-{suffix:D12}");

    private static string Digest(char character) => new(character, 64);

    private sealed class RecordingProviderInvoker : IUserOperationProviderCallInvoker
    {
        public int CallCount { get; private set; }

        public Task<UserOperationProviderInvocationObservation> InvokeOnceAsync(
            UserOperationProviderCommand command,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("The provider must not be called by this test.");
        }
    }
}
