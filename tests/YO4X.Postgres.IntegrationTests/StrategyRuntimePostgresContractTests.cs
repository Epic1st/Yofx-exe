using System.Data;
using System.Security.Cryptography;
using System.Text;
using YO4X.Runtime.Application;
using YO4X.Runtime.Contracts;
using YO4X.Runtime.Postgres;
using YO4X.Strategy.Abstractions;

namespace YO4X.Postgres.IntegrationTests;

public sealed class StrategyRuntimePostgresContractTests
{
    private static readonly string[] ClaimColumns =
    [
        "claim_disposition",
        "claim_code",
        "authority_now_utc",
        "claim_expires_at_utc",
        "event_content",
        "snapshot_content",
        "prior_state_version",
        "prior_state_content",
        "prior_state_sha256",
        "commit_evidence_content",
        "commit_evidence_sha256",
        "committed_at_utc",
        "replayed"
    ];

    [Fact]
    public void InputCodecDelegatesToApplicationHydratorAndPreservesExactReference()
    {
        StrategyEventInputEvidence expected = CreateInput();
        byte[] eventContent = Encoding.UTF8.GetBytes(expected.EventJson);
        byte[] snapshotContent = Encoding.UTF8.GetBytes(expected.SnapshotJson);
        try
        {
            StrategyEventInputEvidence restored =
                StrategyCanonicalEvidenceCodec.ReadInputEvidence(
                    eventContent,
                    snapshotContent,
                    expected.Reference,
                    "test input");

            Assert.Equal(expected.Reference, restored.Reference);
            Assert.Equal(expected.EventJson, restored.EventJson);
            Assert.Equal(expected.SnapshotJson, restored.SnapshotJson);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(eventContent);
            CryptographicOperations.ZeroMemory(snapshotContent);
        }
    }

    [Fact]
    public void InputCodecRejectsReferenceThatDoesNotMatchRestoredEvidence()
    {
        StrategyEventInputEvidence expected = CreateInput();
        var mismatched = new StrategyEventReference(
            expected.Reference.DeploymentId,
            expected.Reference.WorkerInstanceId,
            expected.Reference.Generation,
            expected.Reference.Sequence + 1,
            expected.Reference.EventId,
            expected.Reference.EventKind,
            expected.Reference.EventContractVersion,
            expected.Reference.EventSha256,
            expected.Reference.SnapshotSequence,
            expected.Reference.SnapshotContractVersion,
            expected.Reference.SnapshotSha256);
        byte[] eventContent = Encoding.UTF8.GetBytes(expected.EventJson);
        byte[] snapshotContent = Encoding.UTF8.GetBytes(expected.SnapshotJson);
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                StrategyCanonicalEvidenceCodec.ReadInputEvidence(
                    eventContent,
                    snapshotContent,
                    mismatched,
                    "test input"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(eventContent);
            CryptographicOperations.ZeroMemory(snapshotContent);
        }
    }

    [Theory]
    [InlineData("event", 0)]
    [InlineData("event", 1_048_577)]
    [InlineData("snapshot", 0)]
    [InlineData("snapshot", 4_194_305)]
    [InlineData("state", 0)]
    [InlineData("state", 1_048_577)]
    [InlineData("commit", 1)]
    [InlineData("commit", 8_388_609)]
    public void CodecRejectsRawDatabaseByteLengthsBeforeUtf8Decode(
        string evidenceKind,
        int contentLength)
    {
        StrategyEventInputEvidence valid = CreateInput();
        byte[] eventContent = Encoding.UTF8.GetBytes(valid.EventJson);
        byte[] snapshotContent = Encoding.UTF8.GetBytes(valid.SnapshotJson);
        byte[] malformedContent = new byte[contentLength];
        try
        {
            InvalidOperationException exception;
            switch (evidenceKind)
            {
                case "event":
                    exception = Assert.Throws<InvalidOperationException>(() =>
                        StrategyCanonicalEvidenceCodec.ReadInputEvidence(
                            malformedContent,
                            snapshotContent,
                            valid.Reference,
                            "test input"));
                    break;
                case "snapshot":
                    exception = Assert.Throws<InvalidOperationException>(() =>
                        StrategyCanonicalEvidenceCodec.ReadInputEvidence(
                            eventContent,
                            malformedContent,
                            valid.Reference,
                            "test input"));
                    break;
                case "state":
                    exception = Assert.Throws<InvalidOperationException>(() =>
                        StrategyCanonicalEvidenceCodec.ReadState(
                            0,
                            malformedContent,
                            new string('0', 64),
                            "test state"));
                    break;
                case "commit":
                    exception = Assert.Throws<InvalidOperationException>(() =>
                        StrategyCanonicalEvidenceCodec.ReadCommitEvidence(
                            malformedContent,
                            new string('0', 64),
                            "test commit"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(evidenceKind));
            }

            Assert.Null(exception.InnerException);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(eventContent);
            CryptographicOperations.ZeroMemory(snapshotContent);
            CryptographicOperations.ZeroMemory(malformedContent);
        }
    }

    [Fact]
    public void CodecHasOnlyOnePrivateBoundedByteToTextBoundary()
    {
        System.Reflection.MethodInfo[] byteDecoders = typeof(StrategyCanonicalEvidenceCodec)
            .GetMethods(
                System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic)
            .Where(method => method.ReturnType == typeof(string)
                && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(byte[])))
            .ToArray();

        System.Reflection.MethodInfo decoder = Assert.Single(byteDecoders);
        Assert.Equal("ReadBoundedText", decoder.Name);
        Assert.True(decoder.IsPrivate);
        Assert.Equal(
            [typeof(byte[]), typeof(int), typeof(int), typeof(string)],
            decoder.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void CodecRejectsInvalidUtf8InsideValidByteBounds()
    {
        StrategyEventInputEvidence valid = CreateInput();
        byte[] malformedEventContent = [0xC3, 0x28];
        byte[] snapshotContent = Encoding.UTF8.GetBytes(valid.SnapshotJson);
        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                StrategyCanonicalEvidenceCodec.ReadInputEvidence(
                    malformedEventContent,
                    snapshotContent,
                    valid.Reference,
                    "test input"));

            Assert.IsType<DecoderFallbackException>(exception.InnerException);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(malformedEventContent);
            CryptographicOperations.ZeroMemory(snapshotContent);
        }
    }

    [Fact]
    public void ClaimSchemaRejectsMixedPersistedCommitColumns()
    {
        using DataTableReader reader = CreateSchemaReader(
            [.. ClaimColumns, "persisted_commit_evidence_content"]);

        Assert.Throws<InvalidOperationException>(() =>
            StrategyEventPostgresResultContract.RequireClaimSchema(reader));
    }

    [Fact]
    public void AlreadyCommittedRejectsFalseReplayFlag()
    {
        using DataTableReader reader = CreateClaimReader(
            "already_committed",
            "strategy_event_already_committed",
            replayed: false,
            includeClaimEvidence: false,
            includeCommitEvidence: true);

        Assert.Throws<InvalidOperationException>(() =>
            StrategyEventPostgresResultContract.RequireClaimShape(
                reader,
                "already_committed",
                "strategy_event_already_committed"));
    }

    [Fact]
    public void AlreadyCommittedRejectsMixedClaimAndCommitEvidence()
    {
        using DataTableReader reader = CreateClaimReader(
            "already_committed",
            "strategy_event_already_committed",
            replayed: true,
            includeClaimEvidence: true,
            includeCommitEvidence: true);

        Assert.Throws<InvalidOperationException>(() =>
            StrategyEventPostgresResultContract.RequireClaimShape(
                reader,
                "already_committed",
                "strategy_event_already_committed"));
    }

    [Fact]
    public void NoWorkRejectsUnknownReasonCode()
    {
        using DataTableReader reader = CreateClaimReader(
            "no_work",
            "strategy_event_future_code",
            replayed: false,
            includeClaimEvidence: false,
            includeCommitEvidence: false);

        Assert.Throws<InvalidOperationException>(() =>
            StrategyEventPostgresResultContract.RequireClaimShape(
                reader,
                "no_work",
                "strategy_event_future_code"));
    }

    [Theory]
    [InlineData("strategy_event_claimed", false)]
    [InlineData("strategy_event_expired_claim_recovered", false)]
    [InlineData("strategy_event_claim_replayed", true)]
    public void ClaimedAcceptsOnlyTheProtocolCodeAndReplayCombinations(
        string code,
        bool replayed)
    {
        using DataTableReader reader = CreateClaimReader(
            "claimed",
            code,
            replayed,
            includeClaimEvidence: true,
            includeCommitEvidence: false);

        StrategyEventPostgresResultContract.RequireClaimShape(reader, "claimed", code);
    }

    private static StrategyEventInputEvidence CreateInput()
    {
        DateTimeOffset now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        var envelope = new RuntimeEnvelope<StrategyEvent>(
            RuntimeContractVersions.EnvelopeV1,
            Guid.Parse("91000000-0000-0000-0000-000000000001"),
            Guid.Parse("91000000-0000-0000-0000-000000000002"),
            3,
            7,
            Guid.Parse("91000000-0000-0000-0000-000000000003"),
            now,
            now,
            new NewTickEvent(now, "EURUSD", 1.10m, 1.11m, 12));
        StrategySnapshot snapshot = StrategySnapshot.Create(
            8,
            now,
            now,
            new StrategyAccountSnapshot(4, 10_000m, 10_100m, 9_500m, "USD"),
            [new StrategyQuoteSnapshot(12, "EURUSD", 1.10m, 1.11m, now)]);
        return StrategyEventInputEvidence.Create(envelope, snapshot);
    }

    private static DataTableReader CreateSchemaReader(IEnumerable<string> columns)
    {
        var table = new DataTable();
        foreach (string column in columns)
        {
            table.Columns.Add(column, typeof(object));
        }

        return table.CreateDataReader();
    }

    private static DataTableReader CreateClaimReader(
        string disposition,
        string code,
        bool replayed,
        bool includeClaimEvidence,
        bool includeCommitEvidence)
    {
        var table = new DataTable();
        table.Columns.Add("claim_disposition", typeof(string));
        table.Columns.Add("claim_code", typeof(string));
        table.Columns.Add("authority_now_utc", typeof(DateTimeOffset));
        table.Columns.Add("claim_expires_at_utc", typeof(DateTimeOffset));
        table.Columns.Add("event_content", typeof(byte[]));
        table.Columns.Add("snapshot_content", typeof(byte[]));
        table.Columns.Add("prior_state_version", typeof(long));
        table.Columns.Add("prior_state_content", typeof(byte[]));
        table.Columns.Add("prior_state_sha256", typeof(string));
        table.Columns.Add("commit_evidence_content", typeof(byte[]));
        table.Columns.Add("commit_evidence_sha256", typeof(string));
        table.Columns.Add("committed_at_utc", typeof(DateTimeOffset));
        table.Columns.Add("replayed", typeof(bool));

        DataRow row = table.NewRow();
        row["claim_disposition"] = disposition;
        row["claim_code"] = code;
        row["replayed"] = replayed;
        if (includeClaimEvidence)
        {
            row["authority_now_utc"] = new DateTimeOffset(
                2026,
                8,
                22,
                12,
                0,
                0,
                TimeSpan.Zero);
            row["claim_expires_at_utc"] = new DateTimeOffset(
                2026,
                8,
                22,
                12,
                0,
                30,
                TimeSpan.Zero);
            row["event_content"] = new byte[] { 1 };
            row["snapshot_content"] = new byte[] { 2 };
            row["prior_state_version"] = 0L;
            row["prior_state_content"] = new byte[] { 3 };
            row["prior_state_sha256"] = new string('a', 64);
        }

        if (includeCommitEvidence)
        {
            row["commit_evidence_content"] = new byte[] { 4 };
            row["commit_evidence_sha256"] = new string('b', 64);
            row["committed_at_utc"] = new DateTimeOffset(
                2026,
                8,
                22,
                12,
                0,
                1,
                TimeSpan.Zero);
        }

        table.Rows.Add(row);
        DataTableReader reader = table.CreateDataReader();
        Assert.True(reader.Read());
        return reader;
    }
}
