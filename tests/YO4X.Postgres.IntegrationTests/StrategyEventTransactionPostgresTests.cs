using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.Persistence.Postgres;
using YO4X.Runtime.Application;
using YO4X.Runtime.Contracts;
using YO4X.Runtime.Postgres;
using YO4X.Strategy.Abstractions;
using YO4X.Tenancy;

namespace YO4X.Postgres.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class StrategyEventTransactionPostgresTests(PostgresContainerFixture postgres)
{
    private const string RiskEvaluationTopic =
        "strategy.action.risk-evaluation-requested.v1";
    private readonly PostgresContainerFixture postgres = postgres;

    [PostgresFact]
    public async Task AtomicCommitPersistsFiveArtifactsAndExactReplayWithoutDuplicates()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        AuthoritySeed authority = await SeedApprovedAuthorityAsync(database);
        var context = new TenantExecutionContext(
            authority.TenantId,
            authority.SupervisorWorkloadId,
            Guid.CreateVersion7());
        var store = new PostgresStrategyEventTransactionStore(database.SupervisorRuntime);
        Guid reusedActionId = Guid.CreateVersion7();
        const string reusedIdempotencyKey = "strategy-action-proof-1";

        StrategyEventInputEvidence first = CreateInput(authority, 1, "first");
        StrategyEventIntakeReceipt intake = await store.PersistAsync(
            context,
            first,
            CancellationToken.None);
        Assert.False(intake.Replayed);

        ArtifactCounts beforeCommit = await ReadArtifactCountsAsync(database, authority);
        Assert.Equal(new ArtifactCounts(1, 1, 0, 0, 0, 0), beforeCommit);

        StrategyEventInputEvidence drift = CreateInput(
            authority,
            1,
            "content-drift",
            first.Reference.EventId,
            first.Envelope.ReceivedAtUtc);
        PostgresException driftRejected = await Assert.ThrowsAsync<PostgresException>(
            () => store.PersistAsync(context, drift, CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, driftRejected.SqlState);

        var lostAcknowledgementStore = new LostAcknowledgementOnceStore(store);
        StrategyEventProcessingCoordinator coordinator = CreateCoordinator(
            lostAcknowledgementStore,
            new DeterministicStrategyHost(reusedActionId, reusedIdempotencyKey));
        StrategyEventProcessingResult committed = await coordinator.ProcessAsync(
            context,
            first.Reference);
        Assert.Equal(StrategyEventProcessingOutcome.AlreadyCommitted, committed.Outcome);
        Assert.NotNull(committed.Receipt);
        Assert.True(committed.Receipt.Replayed);
        Assert.NotNull(lostAcknowledgementStore.FirstReceipt);
        Assert.NotNull(lostAcknowledgementStore.FirstRequest);
        Assert.False(lostAcknowledgementStore.FirstReceipt.Replayed);
        Assert.Equal(
            lostAcknowledgementStore.FirstReceipt.Evidence.CanonicalJson,
            committed.Receipt.Evidence.CanonicalJson);

        ArtifactCounts afterCommit = await ReadArtifactCountsAsync(database, authority);
        Assert.Equal(new ArtifactCounts(2, 1, 1, 1, 1, 1), afterCommit);

        JsonObject forgedDocument = JsonNode.Parse(
            lostAcknowledgementStore.FirstRequest.Evidence.CanonicalJson)!.AsObject();
        forgedDocument["combinedActionBytes"] =
            lostAcknowledgementStore.FirstRequest.Evidence.Document.CombinedActionBytes + 1;
        string forgedJson = CanonicalJson.Serialize(forgedDocument);
        PostgresException forgedCountRejected = await Assert.ThrowsAsync<PostgresException>(
            () => CommitRawAsync(
                database,
                context,
                first.Reference,
                lostAcknowledgementStore.FirstRequest.Claim.ClaimToken,
                forgedJson,
                Sha256(forgedJson)));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, forgedCountRejected.SqlState);
        Assert.Equal(afterCommit, await ReadArtifactCountsAsync(database, authority));

        JsonObject extraCommitField = JsonNode.Parse(
            lostAcknowledgementStore.FirstRequest.Evidence.CanonicalJson)!.AsObject();
        extraCommitField["unexpected"] = true;
        string extraCommitJson = CanonicalJson.Serialize(extraCommitField);
        PostgresException extraCommitRejected = await Assert.ThrowsAsync<PostgresException>(
            () => CommitRawAsync(
                database,
                context,
                first.Reference,
                lostAcknowledgementStore.FirstRequest.Claim.ClaimToken,
                extraCommitJson,
                Sha256(extraCommitJson)));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, extraCommitRejected.SqlState);
        Assert.Equal(afterCommit, await ReadArtifactCountsAsync(database, authority));

        JsonObject extraActionWrapper = JsonNode.Parse(
            lostAcknowledgementStore.FirstRequest.Evidence.CanonicalJson)!.AsObject();
        extraActionWrapper["actions"]!.AsArray()[0]!.AsObject()["unexpected"] = true;

        JsonObject extraResultState = JsonNode.Parse(
            lostAcknowledgementStore.FirstRequest.Evidence.CanonicalJson)!.AsObject();
        JsonObject resultDocument = JsonNode.Parse(
            extraResultState["resultJson"]!.GetValue<string>())!.AsObject();
        resultDocument["state"]!.AsObject()["unexpected"] = true;
        string resultJson = CanonicalJson.Serialize(resultDocument);
        extraResultState["resultJson"] = resultJson;
        extraResultState["resultSha256"] = Sha256(resultJson);

        JsonObject extraOutboxPayload = JsonNode.Parse(
            lostAcknowledgementStore.FirstRequest.Evidence.CanonicalJson)!.AsObject();
        JsonObject actionWrapper = extraOutboxPayload["actions"]!.AsArray()[0]!.AsObject();
        JsonObject outboxPayload = JsonNode.Parse(
            actionWrapper["outboxPayloadJson"]!.GetValue<string>())!.AsObject();
        outboxPayload["unexpected"] = true;
        string outboxPayloadJson = CanonicalJson.Serialize(outboxPayload);
        actionWrapper["outboxPayloadJson"] = outboxPayloadJson;
        actionWrapper["outboxPayloadSha256"] = Sha256(outboxPayloadJson);

        foreach (JsonObject malformedDocument in new[]
        {
            extraActionWrapper,
            extraResultState,
            extraOutboxPayload
        })
        {
            string malformedJson = CanonicalJson.Serialize(malformedDocument);
            PostgresException malformedRejected = await Assert.ThrowsAsync<PostgresException>(
                () => CommitRawAsync(
                    database,
                    context,
                    first.Reference,
                    lostAcknowledgementStore.FirstRequest.Claim.ClaimToken,
                    malformedJson,
                    Sha256(malformedJson)));
            Assert.Equal(PostgresErrorCodes.InvalidParameterValue, malformedRejected.SqlState);
            Assert.Equal(afterCommit, await ReadArtifactCountsAsync(database, authority));
        }

        foreach ((string field, JsonNode value) in new[]
        {
            ("tenantId", JsonValue.Create(Guid.CreateVersion7().ToString())!),
            ("eventId", JsonValue.Create(Guid.CreateVersion7().ToString())!)
        })
        {
            JsonObject conflictingDocument = JsonNode.Parse(
                lostAcknowledgementStore.FirstRequest.Evidence.CanonicalJson)!.AsObject();
            conflictingDocument[field] = value;
            string conflictingJson = CanonicalJson.Serialize(conflictingDocument);
            PostgresException conflictingRejected = await Assert.ThrowsAsync<PostgresException>(
                () => CommitRawAsync(
                    database,
                    context,
                    first.Reference,
                    lostAcknowledgementStore.FirstRequest.Claim.ClaimToken,
                    conflictingJson,
                    Sha256(conflictingJson)));
            Assert.Equal(PostgresErrorCodes.InvalidParameterValue, conflictingRejected.SqlState);
            Assert.Equal(afterCommit, await ReadArtifactCountsAsync(database, authority));
        }

        StrategyEventProcessingResult replay = await coordinator.ProcessAsync(
            context,
            first.Reference);
        Assert.Equal(StrategyEventProcessingOutcome.AlreadyCommitted, replay.Outcome);
        Assert.NotNull(replay.Receipt);
        Assert.True(replay.Receipt.Replayed);
        Assert.Equal(committed.Receipt.Evidence.Sha256, replay.Receipt.Evidence.Sha256);
        Assert.Equal(
            committed.Receipt.Evidence.CanonicalJson,
            replay.Receipt.Evidence.CanonicalJson);
        Assert.Equal(afterCommit, await ReadArtifactCountsAsync(database, authority));

        StrategyEventInputEvidence second = CreateInput(authority, 2, "second");
        await store.PersistAsync(context, second, CancellationToken.None);
        StrategyEventProcessingResult constrained = await coordinator.ProcessAsync(
            context,
            second.Reference);
        Assert.Equal(
            StrategyEventProcessingOutcome.CommitRecoveryRequired,
            constrained.Outcome);

        ArtifactCounts afterConstraintFailure =
            await ReadArtifactCountsAsync(database, authority);
        Assert.Equal(new ArtifactCounts(2, 2, 1, 1, 1, 1), afterConstraintFailure);
        Assert.Equal(
            "claimed",
            await ReadEventStateAsync(database, authority, second.Reference.EventId));

        await AssertSupervisorRawAccessDeniedAsync(database, context);
        await AssertImmutableDeleteRejectedAsync(
            database,
            authority,
            first.Reference.EventId,
            deleteHead: false);
        await AssertImmutableDeleteRejectedAsync(
            database,
            authority,
            first.Reference.EventId,
            deleteHead: true);
    }

    [PostgresFact]
    public async Task ClaimsAreExclusiveRecoverableAndAuthorityFailuresAreClosed()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        AuthoritySeed authority = await SeedApprovedAuthorityAsync(database);
        var context = new TenantExecutionContext(
            authority.TenantId,
            authority.SupervisorWorkloadId,
            Guid.CreateVersion7());
        var store = new PostgresStrategyEventTransactionStore(
            database.SupervisorRuntime,
            TimeSpan.FromSeconds(1));

        StrategyEventInputEvidence malformedSource = CreateInput(authority, 1, "malformed");
        string canonicalEvent = malformedSource.EventJson;
        string[] nonCanonicalIntakeDocuments =
        [
            canonicalEvent.Insert(1, " "),
            ReverseRootPropertyOrder(canonicalEvent),
            canonicalEvent.Insert(1, "\"contractVersion\":999,"),
            InsertAfter(
                canonicalEvent,
                "\"payload\":{",
                "\"contractVersion\":999,")
        ];
        foreach (string nonCanonicalEvent in nonCanonicalIntakeDocuments)
        {
            PostgresException canonicalRejected = await Assert.ThrowsAsync<PostgresException>(
                () => PersistRawAsync(
                    database,
                    context,
                    malformedSource.Reference,
                    nonCanonicalEvent,
                    Sha256(nonCanonicalEvent),
                    malformedSource.SnapshotJson));
            Assert.Equal(PostgresErrorCodes.InvalidParameterValue, canonicalRejected.SqlState);
            Assert.Equal(
                new ArtifactCounts(0, 0, 0, 0, 0, 0),
                await ReadArtifactCountsAsync(database, authority));
            Assert.Equal(0, await ReadHeadCountAsync(database, authority));
        }

        string[] typedIntakePoisonDocuments =
        [
            ReplaceExactly(canonicalEvent, "\"generation\":1", "\"generation\":1e0"),
            ReplaceExactly(canonicalEvent, "\"kind\":0", "\"kind\":-0"),
            ReplaceExactly(
                canonicalEvent,
                "\"reasonCode\":\"malformed\"",
                "\"reasonCode\":7"),
            ReplaceExactly(
                canonicalEvent,
                "\"reasonCode\":\"malformed\"",
                "\"reasonCode\":\"\""),
            ReplaceExactly(
                canonicalEvent,
                "\"reasonCode\":\"malformed\"",
                "\"reasonCode\":\" malformed\""),
            ReplaceJsonStringProperty(
                canonicalEvent,
                "receivedAtUtc",
                "2026-08-23T00:00:00.0+00:00"),
            ReplaceJsonStringProperty(
                canonicalEvent,
                "receivedAtUtc",
                "2026-08-23T24:00:00+00:00")
        ];
        foreach (string typedPoisonEvent in typedIntakePoisonDocuments)
        {
            PostgresException typedRejected = await Assert.ThrowsAsync<PostgresException>(
                () => PersistRawAsync(
                    database,
                    context,
                    malformedSource.Reference,
                    typedPoisonEvent,
                    Sha256(typedPoisonEvent),
                    malformedSource.SnapshotJson));
            Assert.Equal(PostgresErrorCodes.InvalidParameterValue, typedRejected.SqlState);
            Assert.Equal(
                new ArtifactCounts(0, 0, 0, 0, 0, 0),
                await ReadArtifactCountsAsync(database, authority));
            Assert.Equal(0, await ReadHeadCountAsync(database, authority));
        }

        StrategyEventInputEvidence barInput = CreateBarInput(authority, 1);
        foreach (string invalidTimeframe in new[]
        {
            "00:01:00.0",
            "00:01:00.0000000",
            "99:00:00"
        })
        {
            string poisonedBar = ReplaceExactly(
                barInput.EventJson,
                "\"timeframe\":\"00:01:00\"",
                $"\"timeframe\":\"{invalidTimeframe}\"");
            PostgresException timeframeRejected = await Assert.ThrowsAsync<PostgresException>(
                () => PersistRawAsync(
                    database,
                    context,
                    barInput.Reference,
                    poisonedBar,
                    Sha256(poisonedBar),
                    barInput.SnapshotJson));
            Assert.Equal(PostgresErrorCodes.InvalidParameterValue, timeframeRejected.SqlState);
            Assert.Equal(
                new ArtifactCounts(0, 0, 0, 0, 0, 0),
                await ReadArtifactCountsAsync(database, authority));
            Assert.Equal(0, await ReadHeadCountAsync(database, authority));
        }

        string typedPoisonSnapshot = ReplaceExactly(
            malformedSource.SnapshotJson,
            "\"symbol\":\"XAUUSD\"",
            "\"symbol\":7");
        PostgresException typedSnapshotRejected = await Assert.ThrowsAsync<PostgresException>(
            () => PersistRawAsync(
                database,
                context,
                malformedSource.Reference,
                canonicalEvent,
                malformedSource.Reference.EventSha256,
                typedPoisonSnapshot,
                Sha256(typedPoisonSnapshot)));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, typedSnapshotRejected.SqlState);
        Assert.Equal(
            new ArtifactCounts(0, 0, 0, 0, 0, 0),
            await ReadArtifactCountsAsync(database, authority));
        Assert.Equal(0, await ReadHeadCountAsync(database, authority));

        JsonObject invalidNestedSnapshot = JsonNode.Parse(
            malformedSource.SnapshotJson)!.AsObject();
        invalidNestedSnapshot["account"]!["sequence"] = 0;
        string invalidNestedSnapshotJson = CanonicalJson.Serialize(invalidNestedSnapshot);
        PostgresException nestedSnapshotRejected = await Assert.ThrowsAsync<PostgresException>(
            () => PersistRawAsync(
                database,
                context,
                malformedSource.Reference,
                canonicalEvent,
                malformedSource.Reference.EventSha256,
                invalidNestedSnapshotJson,
                Sha256(invalidNestedSnapshotJson)));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, nestedSnapshotRejected.SqlState);
        Assert.Equal(
            new ArtifactCounts(0, 0, 0, 0, 0, 0),
            await ReadArtifactCountsAsync(database, authority));

        JsonObject malformedEvent = JsonNode.Parse(malformedSource.EventJson)!.AsObject();
        Assert.True(malformedEvent.Remove("deploymentId"));
        string malformedJson = CanonicalJson.Serialize(malformedEvent);
        PostgresException malformedRejected = await Assert.ThrowsAsync<PostgresException>(
            () => PersistRawAsync(
                database,
                context,
                malformedSource.Reference,
                malformedJson,
                Sha256(malformedJson),
                malformedSource.SnapshotJson));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, malformedRejected.SqlState);
        Assert.Equal(
            new ArtifactCounts(0, 0, 0, 0, 0, 0),
            await ReadArtifactCountsAsync(database, authority));

        foreach (string invalidDecimal in new[]
        {
            "-0.0",
            "79228162514264337593543950336"
        })
        {
            JsonObject decimalSnapshot = JsonNode.Parse(
                malformedSource.SnapshotJson)!.AsObject();
            decimalSnapshot["account"]!["balance"] = JsonNode.Parse(invalidDecimal);
            string decimalSnapshotJson = CanonicalJson.Serialize(decimalSnapshot);
            PostgresException decimalRejected = await Assert.ThrowsAsync<PostgresException>(
                () => PersistRawAsync(
                    database,
                    context,
                    malformedSource.Reference,
                    canonicalEvent,
                    malformedSource.Reference.EventSha256,
                    decimalSnapshotJson,
                    Sha256(decimalSnapshotJson)));
            Assert.Equal(PostgresErrorCodes.InvalidParameterValue, decimalRejected.SqlState);
            Assert.Equal(
                new ArtifactCounts(0, 0, 0, 0, 0, 0),
                await ReadArtifactCountsAsync(database, authority));
        }

        JsonObject extraSnapshot = JsonNode.Parse(malformedSource.SnapshotJson)!.AsObject();
        extraSnapshot["unexpected"] = true;
        string extraSnapshotJson = CanonicalJson.Serialize(extraSnapshot);
        PostgresException extraSnapshotRejected = await Assert.ThrowsAsync<PostgresException>(
            () => PersistRawAsync(
                database,
                context,
                malformedSource.Reference,
                malformedSource.EventJson,
                malformedSource.Reference.EventSha256,
                extraSnapshotJson,
                Sha256(extraSnapshotJson)));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, extraSnapshotRejected.SqlState);
        Assert.Equal(
            new ArtifactCounts(0, 0, 0, 0, 0, 0),
            await ReadArtifactCountsAsync(database, authority));

        JsonObject extraEvent = JsonNode.Parse(malformedSource.EventJson)!.AsObject();
        extraEvent["unexpected"] = true;
        string extraEventJson = CanonicalJson.Serialize(extraEvent);
        PostgresException extraEventRejected = await Assert.ThrowsAsync<PostgresException>(
            () => PersistRawAsync(
                database,
                context,
                malformedSource.Reference,
                extraEventJson,
                Sha256(extraEventJson),
                malformedSource.SnapshotJson));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, extraEventRejected.SqlState);
        Assert.Equal(
            new ArtifactCounts(0, 0, 0, 0, 0, 0),
            await ReadArtifactCountsAsync(database, authority));

        StrategyEventInputEvidence staleGeneration = CreateInput(
            authority with { Generation = 2 },
            1,
            "stale-generation");
        PostgresException staleRejected = await Assert.ThrowsAsync<PostgresException>(
            () => store.PersistAsync(context, staleGeneration, CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, staleRejected.SqlState);

        StrategyEventInputEvidence input = CreateInput(authority, 1, "claim-race");
        await store.PersistAsync(context, input, CancellationToken.None);
        Guid firstToken = Guid.CreateVersion7();
        Guid secondToken = Guid.CreateVersion7();
        StrategyEventClaimResult[] claims = await Task.WhenAll(
            store.ClaimAsync(context, input.Reference, firstToken, CancellationToken.None),
            store.ClaimAsync(context, input.Reference, secondToken, CancellationToken.None));
        StrategyEventClaimResult winner = Assert.Single(
            claims,
            value => value.Disposition == StrategyEventClaimDisposition.Claimed);
        StrategyEventClaimResult loser = Assert.Single(
            claims,
            value => value.Disposition == StrategyEventClaimDisposition.NoWork);
        Assert.Equal("strategy_event_claim_held", loser.Code);
        Assert.NotNull(winner.Claim);
        Assert.False(winner.Claim.Replayed);

        await Task.Delay(TimeSpan.FromMilliseconds(1_500));
        Assert.True(await RecoverExpiredClaimAsync(
            database,
            context,
            input.Reference,
            winner.Claim.ClaimToken));

        StrategyEventProcessingCoordinator coordinator = CreateCoordinator(
            store,
            new DeterministicStrategyHost(
                Guid.CreateVersion7(),
                "strategy-action-recovered"));
        StrategyEventProcessingResult recovered = await coordinator.ProcessAsync(
            context,
            input.Reference);
        Assert.Equal(StrategyEventProcessingOutcome.Committed, recovered.Outcome);
        Assert.Equal(
            new ArtifactCounts(2, 1, 1, 1, 1, 1),
            await ReadArtifactCountsAsync(database, authority));

        await ExpireWorkerAssignmentAsync(database, authority);
        StrategyEventInputEvidence afterLeaseExpiry = CreateInput(
            authority,
            2,
            "expired-assignment");
        PostgresException expiredRejected = await Assert.ThrowsAsync<PostgresException>(
            () => store.PersistAsync(context, afterLeaseExpiry, CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, expiredRejected.SqlState);
        Assert.Equal(
            new ArtifactCounts(2, 1, 1, 1, 1, 1),
            await ReadArtifactCountsAsync(database, authority));
    }

    [PostgresFact]
    public async Task DirectSupervisorRejectsNonCanonicalCommitEvidenceWithoutSideEffects()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        AuthoritySeed authority = await SeedApprovedAuthorityAsync(database);
        var context = new TenantExecutionContext(
            authority.TenantId,
            authority.SupervisorWorkloadId,
            Guid.CreateVersion7());
        var store = new PostgresStrategyEventTransactionStore(database.SupervisorRuntime);
        StrategyEventInputEvidence input = CreateInput(authority, 1, "canonical-boundary");
        await store.PersistAsync(context, input, CancellationToken.None);

        var capture = new RejectingCommitCaptureStore(store);
        StrategyEventProcessingCoordinator coordinator = CreateCoordinator(
            capture,
            new DeterministicStrategyHost(
                Guid.CreateVersion7(),
                "strategy-action-canonical-boundary"));
        StrategyEventProcessingResult preparation = await coordinator.ProcessAsync(
            context,
            input.Reference);
        Assert.Equal(
            StrategyEventProcessingOutcome.CommitRecoveryRequired,
            preparation.Outcome);
        StrategyEventCommitRequest request = Assert.IsType<StrategyEventCommitRequest>(
            capture.Request);

        ArtifactCounts beforeCommit = await ReadArtifactCountsAsync(database, authority);
        Assert.Equal(new ArtifactCounts(1, 1, 0, 0, 0, 0), beforeCommit);
        Assert.Equal(1, await ReadHeadCountAsync(database, authority));
        Assert.Equal(
            "claimed",
            await ReadEventStateAsync(database, authority, input.Reference.EventId));

        string canonicalCommit = request.Evidence.CanonicalJson;
        string[] nonCanonicalOuterDocuments =
        [
            canonicalCommit.Insert(1, " "),
            ReverseRootPropertyOrder(canonicalCommit),
            canonicalCommit.Insert(1, "\"contractVersion\":999,"),
            InsertAfter(canonicalCommit, "\"actions\":[{", "\"ordinal\":999,")
        ];
        foreach (string nonCanonicalCommit in nonCanonicalOuterDocuments)
        {
            await AssertCommitCanonicalRejectionHasNoSideEffectsAsync(
                database,
                context,
                authority,
                request,
                nonCanonicalCommit,
                beforeCommit);
        }

        string[] typedOuterPoisonDocuments =
        [
            ReplaceExactly(canonicalCommit, "\"eventKind\":0", "\"eventKind\":-0"),
            ReplaceExactly(
                canonicalCommit,
                "\"eventContractVersion\":1",
                "\"eventContractVersion\":1e0"),
            ReplaceExactly(
                canonicalCommit,
                "\"symbol\":\"XAUUSD\"",
                "\"symbol\":7")
        ];
        foreach (string typedPoisonCommit in typedOuterPoisonDocuments)
        {
            await AssertCommitCanonicalRejectionHasNoSideEffectsAsync(
                database,
                context,
                authority,
                request,
                typedPoisonCommit,
                beforeCommit);
        }

        JsonObject commitDocument = JsonNode.Parse(canonicalCommit)!.AsObject();
        string canonicalResult = commitDocument["resultJson"]!.GetValue<string>();
        string[] nonCanonicalEmbeddedResults =
        [
            canonicalResult.Insert(0, " "),
            ReverseRootPropertyOrder(canonicalResult),
            canonicalResult.Insert(1, "\"contractVersion\":999,"),
            InsertAfter(canonicalResult, "\"state\":{", "\"version\":999,")
        ];
        foreach (string nonCanonicalResult in nonCanonicalEmbeddedResults)
        {
            JsonObject poisonedCommit = JsonNode.Parse(canonicalCommit)!.AsObject();
            poisonedCommit["resultJson"] = nonCanonicalResult;
            poisonedCommit["resultSha256"] = Sha256(nonCanonicalResult);
            string poisonedCanonicalOuter = CanonicalJson.Serialize(poisonedCommit);
            await AssertCommitCanonicalRejectionHasNoSideEffectsAsync(
                database,
                context,
                authority,
                request,
                poisonedCanonicalOuter,
                beforeCommit);
        }


        string exponentResult = ReplaceExactly(
            canonicalResult,
            "\"version\":1",
            "\"version\":1e0");
        JsonObject typedEmbeddedCommit = JsonNode.Parse(canonicalCommit)!.AsObject();
        typedEmbeddedCommit["resultJson"] = exponentResult;
        typedEmbeddedCommit["resultSha256"] = Sha256(exponentResult);
        await AssertCommitCanonicalRejectionHasNoSideEffectsAsync(
            database,
            context,
            authority,
            request,
            CanonicalJson.Serialize(typedEmbeddedCommit),
            beforeCommit);

        JsonObject wrongPlaceExposureCommit = JsonNode.Parse(canonicalCommit)!.AsObject();
        JsonObject wrongPlaceExposureWrapper =
            wrongPlaceExposureCommit["actions"]![0]!.AsObject();
        wrongPlaceExposureWrapper["exposureHint"] = (int)RequestedExposureHint.Reduce;
        JsonObject wrongPlaceExposureAction = JsonNode.Parse(
            wrongPlaceExposureWrapper["actionJson"]!.GetValue<string>())!.AsObject();
        wrongPlaceExposureAction["exposureHint"] = (int)RequestedExposureHint.Reduce;
        string wrongPlaceExposureActionJson = CanonicalJson.Serialize(wrongPlaceExposureAction);
        string wrongPlaceExposureActionSha256 = Sha256(wrongPlaceExposureActionJson);
        wrongPlaceExposureWrapper["actionJson"] = wrongPlaceExposureActionJson;
        wrongPlaceExposureWrapper["actionSha256"] = wrongPlaceExposureActionSha256;
        JsonObject wrongPlaceExposureOutbox = JsonNode.Parse(
            wrongPlaceExposureWrapper["outboxPayloadJson"]!.GetValue<string>())!.AsObject();
        wrongPlaceExposureOutbox["exposureHint"] = (int)RequestedExposureHint.Reduce;
        wrongPlaceExposureOutbox["actionSha256"] = wrongPlaceExposureActionSha256;
        string wrongPlaceExposureOutboxJson = CanonicalJson.Serialize(wrongPlaceExposureOutbox);
        wrongPlaceExposureWrapper["outboxPayloadJson"] = wrongPlaceExposureOutboxJson;
        wrongPlaceExposureWrapper["outboxPayloadSha256"] = Sha256(
            wrongPlaceExposureOutboxJson);
        JsonObject wrongPlaceExposureResult = JsonNode.Parse(canonicalResult)!.AsObject();
        JsonArray wrongPlaceExposureResultActions =
            wrongPlaceExposureResult["actions"]!.AsArray();
        wrongPlaceExposureResultActions[0]!["exposureHint"] =
            (int)RequestedExposureHint.Reduce;
        string wrongPlaceExposureResultJson = CanonicalJson.Serialize(wrongPlaceExposureResult);
        wrongPlaceExposureCommit["resultJson"] = wrongPlaceExposureResultJson;
        wrongPlaceExposureCommit["resultSha256"] = Sha256(wrongPlaceExposureResultJson);
        wrongPlaceExposureCommit["combinedActionBytes"] = Encoding.UTF8.GetByteCount(
            CanonicalJson.Serialize(wrongPlaceExposureResultActions));
        await AssertCommitCanonicalRejectionHasNoSideEffectsAsync(
            database,
            context,
            authority,
            request,
            CanonicalJson.Serialize(wrongPlaceExposureCommit),
            beforeCommit);

        JsonObject scaleDriftCommit = JsonNode.Parse(canonicalCommit)!.AsObject();
        JsonObject scaleDriftActionWrapper = scaleDriftCommit["actions"]![0]!.AsObject();
        string originalActionJson = scaleDriftActionWrapper["actionJson"]!.GetValue<string>();
        string scaleDriftActionJson = ReplaceExactly(
            originalActionJson,
            "\"volume\":0.01",
            "\"volume\":1.0");
        string scaleDriftActionSha256 = Sha256(scaleDriftActionJson);
        scaleDriftActionWrapper["actionJson"] = scaleDriftActionJson;
        scaleDriftActionWrapper["actionSha256"] = scaleDriftActionSha256;
        JsonObject scaleDriftOutbox = JsonNode.Parse(
            scaleDriftActionWrapper["outboxPayloadJson"]!.GetValue<string>())!.AsObject();
        scaleDriftOutbox["actionSha256"] = scaleDriftActionSha256;
        string scaleDriftOutboxJson = CanonicalJson.Serialize(scaleDriftOutbox);
        scaleDriftActionWrapper["outboxPayloadJson"] = scaleDriftOutboxJson;
        scaleDriftActionWrapper["outboxPayloadSha256"] = Sha256(scaleDriftOutboxJson);
        string scaleDriftResultJson = ReplaceExactly(
            canonicalResult,
            "\"volume\":0.01",
            "\"volume\":1.00");
        scaleDriftCommit["resultJson"] = scaleDriftResultJson;
        scaleDriftCommit["resultSha256"] = Sha256(scaleDriftResultJson);
        JsonArray scaleDriftResultActions = JsonNode.Parse(
            scaleDriftResultJson)!["actions"]!.AsArray();
        scaleDriftCommit["combinedActionBytes"] = Encoding.UTF8.GetByteCount(
            CanonicalJson.Serialize(scaleDriftResultActions));
        await AssertCommitCanonicalRejectionHasNoSideEffectsAsync(
            database,
            context,
            authority,
            request,
            CanonicalJson.Serialize(scaleDriftCommit),
            beforeCommit);

        StrategyEventCommitReceipt receipt = await store.CommitAsync(
            context,
            request,
            CancellationToken.None);
        Assert.False(receipt.Replayed);
        Assert.Equal(
            new ArtifactCounts(2, 1, 1, 1, 1, 1),
            await ReadArtifactCountsAsync(database, authority));
    }

    private static StrategyEventProcessingCoordinator CreateCoordinator(
        IStrategyEventTransactionStore store,
        IStrategyHostClient strategyHost) => new(
        store,
        strategyHost,
        new StrategyEventProcessingOptions
        {
            ResultBounds = StrategyResultBounds.Create(
                maximumStateBytes: 1024 * 1024,
                maximumActionCount: 16,
                maximumCombinedActionBytes: 1024 * 1024,
                maximumWallTime: TimeSpan.FromSeconds(5)),
            CommitAcknowledgementRecoveryAttempts = 1
        },
        TimeProvider.System);

    private static StrategyEventInputEvidence CreateInput(
        AuthoritySeed authority,
        long sequence,
        string reasonCode,
        Guid? eventId = null,
        DateTimeOffset? occurredAtUtc = null)
    {
        DateTimeOffset occurredAt = occurredAtUtc ?? UtcNow();
        var eventValue = new InitializeEvent(occurredAt, reasonCode);
        var envelope = new RuntimeEnvelope<StrategyEvent>(
            RuntimeContractVersions.EnvelopeV1,
            authority.DeploymentId,
            authority.WorkerNodeId,
            authority.Generation,
            sequence,
            eventId ?? Guid.CreateVersion7(),
            occurredAt,
            null,
            eventValue);
        StrategySnapshot snapshot = StrategySnapshot.Create(
            sequence,
            occurredAt,
            occurredAt,
            new StrategyAccountSnapshot(sequence, 10_000m, 10_000m, 9_000m, "USD"),
            [new StrategyQuoteSnapshot(sequence, "XAUUSD", 2_000m, 2_000.5m, occurredAt)]);
        return StrategyEventInputEvidence.Create(envelope, snapshot);
    }

    private static StrategyEventInputEvidence CreateBarInput(
        AuthoritySeed authority,
        long sequence)
    {
        DateTimeOffset occurredAt = UtcNow();
        var eventValue = new BarClosedEvent(
            occurredAt,
            "XAUUSD",
            TimeSpan.FromMinutes(1),
            occurredAt.AddMinutes(-1),
            2_000m,
            2_001m,
            1_999m,
            2_000.5m,
            100,
            sequence);
        var envelope = new RuntimeEnvelope<StrategyEvent>(
            RuntimeContractVersions.EnvelopeV1,
            authority.DeploymentId,
            authority.WorkerNodeId,
            authority.Generation,
            sequence,
            Guid.CreateVersion7(),
            occurredAt,
            null,
            eventValue);
        StrategySnapshot snapshot = StrategySnapshot.Create(
            sequence,
            occurredAt,
            occurredAt,
            new StrategyAccountSnapshot(sequence, 10_000m, 10_000m, 9_000m, "USD"),
            [new StrategyQuoteSnapshot(sequence, "XAUUSD", 2_000m, 2_000.5m, occurredAt)]);
        return StrategyEventInputEvidence.Create(envelope, snapshot);
    }

    private static async Task<AuthoritySeed> SeedApprovedAuthorityAsync(
        PostgresTestDatabase database)
    {
        object verification = (await InvokeAuthorityFixtureAsync(
            "SeedVerifiedStrategyAsync",
            database))!;
        await InvokeAuthorityFixtureAsync("RecordVerificationAsync", database, verification);
        await InvokeAuthorityFixtureAsync("PromoteAsync", database, verification);
        await InvokeAuthorityFixtureAsync(
            "SeedRuntimeAuthorityAsync",
            database,
            verification,
            "running");
        return new AuthoritySeed(
            ReadProperty<Guid>(verification, "TenantId"),
            ReadProperty<Guid>(verification, "DeploymentId"),
            ReadProperty<Guid>(verification, "WorkerNodeId"),
            ReadProperty<Guid>(verification, "WorkerAssignmentId"),
            ReadProperty<Guid>(verification, "SupervisorWorkloadId"),
            1);
    }

    private static async Task<object?> InvokeAuthorityFixtureAsync(
        string methodName,
        params object[] arguments)
    {
        MethodInfo method = typeof(BrokerCommandAuthorizationPostgresTests)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(candidate =>
                candidate.Name == methodName
                && candidate.GetParameters().Length == arguments.Length);
        object invocation = method.Invoke(null, arguments)
            ?? throw new InvalidOperationException($"{methodName} did not return a task.");
        Task task = Assert.IsAssignableFrom<Task>(invocation);
        await task;
        return invocation.GetType().GetProperty("Result")?.GetValue(invocation);
    }

    private static T ReadProperty<T>(object instance, string name) =>
        (T)(instance.GetType().GetProperty(name)?.GetValue(instance)
            ?? throw new InvalidOperationException($"Authority fixture omitted {name}."));

    private static async Task<ArtifactCounts> ReadArtifactCountsAsync(
        PostgresTestDatabase database,
        AuthoritySeed authority)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select
                (select count(*) from operations.strategy_state_revisions
                 where tenant_id = @tenant_id and deployment_id = @deployment_id
                   and generation = @generation),
                (select count(*) from operations.strategy_event_journal
                 where tenant_id = @tenant_id and deployment_id = @deployment_id
                   and generation = @generation),
                (select count(*) from operations.strategy_event_journal
                 where tenant_id = @tenant_id and deployment_id = @deployment_id
                   and generation = @generation and processing_state = 'committed'),
                (select count(*) from operations.strategy_requested_actions
                 where tenant_id = @tenant_id and deployment_id = @deployment_id
                   and generation = @generation),
                (select count(*) from messaging.outbox_messages
                 where tenant_id = @tenant_id and message_type = @topic
                   and causation_id in
                   (select event_id from operations.strategy_event_journal
                    where tenant_id = @tenant_id and deployment_id = @deployment_id
                      and generation = @generation)),
                (select count(*) from audit.audit_events
                 where tenant_id = @tenant_id and action = 'strategy_event_committed'
                   and payload ->> 'deploymentId' = @deployment_id_text
                   and (payload ->> 'generation')::bigint = @generation)
            """,
            connection);
        AddAuthorityParameters(command, authority);
        command.Parameters.AddWithValue("topic", NpgsqlDbType.Text, RiskEvaluationTopic);
        command.Parameters.AddWithValue(
            "deployment_id_text",
            NpgsqlDbType.Text,
            authority.DeploymentId.ToString());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new ArtifactCounts(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5));
    }

    private static async Task<long> ReadHeadCountAsync(
        PostgresTestDatabase database,
        AuthoritySeed authority)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select count(*)
            from operations.strategy_deployment_heads
            where tenant_id = @tenant_id and deployment_id = @deployment_id
              and generation = @generation
            """,
            connection);
        AddAuthorityParameters(command, authority);
        return Assert.IsType<long>(await command.ExecuteScalarAsync());
    }

    private static async Task AssertCommitCanonicalRejectionHasNoSideEffectsAsync(
        PostgresTestDatabase database,
        TenantExecutionContext context,
        AuthoritySeed authority,
        StrategyEventCommitRequest request,
        string evidenceJson,
        ArtifactCounts expectedCounts)
    {
        PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
            () => CommitRawAsync(
                database,
                context,
                request.Claim.Reference,
                request.Claim.ClaimToken,
                evidenceJson,
                Sha256(evidenceJson)));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, rejected.SqlState);
        Assert.Equal(expectedCounts, await ReadArtifactCountsAsync(database, authority));
        Assert.Equal(1, await ReadHeadCountAsync(database, authority));
        Assert.Equal(
            "claimed",
            await ReadEventStateAsync(
                database,
                authority,
                request.Claim.Reference.EventId));
    }

    private static async Task<string> ReadEventStateAsync(
        PostgresTestDatabase database,
        AuthoritySeed authority,
        Guid eventId)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select processing_state
            from operations.strategy_event_journal
            where tenant_id = @tenant_id and deployment_id = @deployment_id
              and generation = @generation and event_id = @event_id
            """,
            connection);
        AddAuthorityParameters(command, authority);
        command.Parameters.AddWithValue("event_id", NpgsqlDbType.Uuid, eventId);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static async Task PersistRawAsync(
        PostgresTestDatabase database,
        TenantExecutionContext context,
        StrategyEventReference reference,
        string eventJson,
        string eventSha256,
        string snapshotJson,
        string? snapshotSha256 = null)
    {
        await using TenantPostgresTransaction transaction =
            await database.SupervisorRuntime.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select * from control.persist_strategy_event(
                @deployment_id, @worker_instance_id, @generation, @sequence,
                @event_id, @event_kind, @event_contract_version, @event_sha256,
                @snapshot_sequence, @snapshot_contract_version, @snapshot_sha256,
                @event_content, @snapshot_content)
            """);
        AddReferenceParameters(command, reference);
        command.Parameters["event_sha256"].Value = eventSha256;
        if (snapshotSha256 is not null)
        {
            command.Parameters["snapshot_sha256"].Value = snapshotSha256;
        }

        command.Parameters.AddWithValue(
            "event_content",
            NpgsqlDbType.Bytea,
            Encoding.UTF8.GetBytes(eventJson));
        command.Parameters.AddWithValue(
            "snapshot_content",
            NpgsqlDbType.Bytea,
            Encoding.UTF8.GetBytes(snapshotJson));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> RecoverExpiredClaimAsync(
        PostgresTestDatabase database,
        TenantExecutionContext context,
        StrategyEventReference reference,
        Guid claimToken)
    {
        await using TenantPostgresTransaction transaction =
            await database.SupervisorRuntime.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select control.recover_expired_strategy_event_claim(
                @deployment_id, @worker_instance_id, @generation, @sequence,
                @event_id, @claim_token)
            """);
        AddEventKeyParameters(command, reference);
        command.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
        bool recovered = Assert.IsType<bool>(await command.ExecuteScalarAsync());
        await transaction.CommitAsync();
        return recovered;
    }

    private static async Task CommitRawAsync(
        PostgresTestDatabase database,
        TenantExecutionContext context,
        StrategyEventReference reference,
        Guid claimToken,
        string evidenceJson,
        string evidenceSha256)
    {
        await using TenantPostgresTransaction transaction =
            await database.SupervisorRuntime.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select * from control.commit_strategy_event(
                @deployment_id, @worker_instance_id, @generation, @sequence,
                @event_id, @claim_token, @evidence_content, @evidence_sha256)
            """);
        AddEventKeyParameters(command, reference);
        command.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
        command.Parameters.AddWithValue(
            "evidence_content",
            NpgsqlDbType.Bytea,
            Encoding.UTF8.GetBytes(evidenceJson));
        command.Parameters.AddWithValue(
            "evidence_sha256",
            NpgsqlDbType.Text,
            evidenceSha256);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertSupervisorRawAccessDeniedAsync(
        PostgresTestDatabase database,
        TenantExecutionContext context)
    {
        string[] deniedSql =
        [
            "select count(*) from operations.strategy_deployment_heads",
            "select count(*) from operations.strategy_state_revisions",
            "select count(*) from operations.strategy_event_journal",
            "select count(*) from operations.strategy_requested_actions",
            "select count(*) from operations.deployments",
            "select count(*) from operations.worker_assignments",
            "select count(*) from operations.execution_leases",
            "select count(*) from governance.strategy_version_source_bindings",
            "select count(*) from audit.audit_events",
            "select count(*) from messaging.outbox_messages",
            "insert into operations.strategy_event_journal default values",
            "update operations.strategy_event_journal set row_version = row_version where false",
            "insert into messaging.outbox_messages default values",
            "update messaging.outbox_messages set attempts = attempts where false",
            "select * from control.lock_active_strategy_supervisor_authority(null, null, null)",
            "select control.is_dotnet_canonical_json('{}')",
            "select control.signed_execution_lease_has_typed_shape('{}'::json)",
            "select control.broker_authorization_evidence_has_typed_shape(" +
                "'{}'::json, '{}'::json, '{}'::json, '{}'::json, '{}'::json, '{}'::json)",
            "select control.strategy_event_input_has_typed_shape('{}'::json, '{}'::json)",
            "select control.strategy_commit_has_typed_shape('{}'::json)"
        ];
        foreach (string sql in deniedSql)
        {
            await using TenantPostgresTransaction transaction =
                await database.SupervisorRuntime.BeginTenantTransactionAsync(context);
            await using NpgsqlCommand command = transaction.CreateCommand(sql);
            PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
        }
    }

    private static async Task AssertImmutableDeleteRejectedAsync(
        PostgresTestDatabase database,
        AuthoritySeed authority,
        Guid eventId,
        bool deleteHead)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        string sql = deleteHead
            ? """
              delete from operations.strategy_deployment_heads
              where tenant_id = @tenant_id and deployment_id = @deployment_id
                and generation = @generation
              """
            : """
              delete from operations.strategy_event_journal
              where tenant_id = @tenant_id and deployment_id = @deployment_id
                and generation = @generation and event_id = @event_id
              """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddAuthorityParameters(command, authority);
        if (!deleteHead)
        {
            command.Parameters.AddWithValue("event_id", NpgsqlDbType.Uuid, eventId);
        }

        PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, rejected.SqlState);
    }

    private static async Task ExpireWorkerAssignmentAsync(
        PostgresTestDatabase database,
        AuthoritySeed authority)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            """
            set local session_replication_role = replica;
            update operations.worker_assignments
            set lease_expires_at = assigned_at + interval '1 microsecond'
            where tenant_id = @tenant_id and id = @assignment_id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, authority.TenantId);
        command.Parameters.AddWithValue(
            "assignment_id",
            NpgsqlDbType.Uuid,
            authority.WorkerAssignmentId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        await transaction.CommitAsync();
    }

    private static void AddReferenceParameters(
        NpgsqlCommand command,
        StrategyEventReference reference)
    {
        AddEventKeyParameters(command, reference);
        command.Parameters.AddWithValue(
            "event_kind",
            NpgsqlDbType.Integer,
            (int)reference.EventKind);
        command.Parameters.AddWithValue(
            "event_contract_version",
            NpgsqlDbType.Integer,
            reference.EventContractVersion);
        command.Parameters.AddWithValue(
            "event_sha256",
            NpgsqlDbType.Text,
            reference.EventSha256);
        command.Parameters.AddWithValue(
            "snapshot_sequence",
            NpgsqlDbType.Bigint,
            reference.SnapshotSequence);
        command.Parameters.AddWithValue(
            "snapshot_contract_version",
            NpgsqlDbType.Integer,
            reference.SnapshotContractVersion);
        command.Parameters.AddWithValue(
            "snapshot_sha256",
            NpgsqlDbType.Text,
            reference.SnapshotSha256);
    }

    private static void AddEventKeyParameters(
        NpgsqlCommand command,
        StrategyEventReference reference)
    {
        command.Parameters.AddWithValue(
            "deployment_id",
            NpgsqlDbType.Uuid,
            reference.DeploymentId);
        command.Parameters.AddWithValue(
            "worker_instance_id",
            NpgsqlDbType.Uuid,
            reference.WorkerInstanceId);
        command.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, reference.Generation);
        command.Parameters.AddWithValue("sequence", NpgsqlDbType.Bigint, reference.Sequence);
        command.Parameters.AddWithValue("event_id", NpgsqlDbType.Uuid, reference.EventId);
    }

    private static void AddAuthorityParameters(
        NpgsqlCommand command,
        AuthoritySeed authority)
    {
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, authority.TenantId);
        command.Parameters.AddWithValue(
            "deployment_id",
            NpgsqlDbType.Uuid,
            authority.DeploymentId);
        command.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, authority.Generation);
    }

    private static DateTimeOffset UtcNow()
    {
        DateTimeOffset utc = DateTimeOffset.UtcNow;
        long ticks = utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string ReverseRootPropertyOrder(string canonicalJson)
    {
        JsonObject source = JsonNode.Parse(canonicalJson)!.AsObject();
        var reordered = new JsonObject();
        foreach ((string name, JsonNode? value) in source.Reverse())
        {
            reordered.Add(name, value?.DeepClone());
        }

        return reordered.ToJsonString();
    }

    private static string InsertAfter(string source, string marker, string insertion)
    {
        int markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Canonical JSON omitted marker {marker}.");
        return source.Insert(markerIndex + marker.Length, insertion);
    }

    private static string ReplaceExactly(string source, string oldValue, string newValue)
    {
        int firstIndex = source.IndexOf(oldValue, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Canonical JSON omitted marker {oldValue}.");
        Assert.Equal(
            -1,
            source.IndexOf(oldValue, firstIndex + oldValue.Length, StringComparison.Ordinal));
        return source.Replace(oldValue, newValue, StringComparison.Ordinal);
    }

    private static string ReplaceJsonStringProperty(
        string source,
        string propertyName,
        string value)
    {
        JsonObject document = JsonNode.Parse(source)!.AsObject();
        Assert.True(document.ContainsKey(propertyName));
        document[propertyName] = value;
        return CanonicalJson.Serialize(document);
    }

    private sealed class DeterministicStrategyHost(Guid actionId, string idempotencyKey) :
        IStrategyHostClient
    {
        public Task<StrategyResult?> EvaluateAsync(
            StrategyHostEvaluationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StrategyState nextState = StrategyState.FromJson(
                checked(request.PriorState.Version + 1),
                CanonicalJson.Serialize(new { EventSequence = request.Sequence }));
            RequestedAction action = new PlaceOrderAction(
                actionId,
                idempotencyKey,
                "XAUUSD",
                "integration-proof-only",
                request.Sequence,
                RequestedExposureHint.Increase,
                RequestedOrderSide.Buy,
                RequestedOrderType.Market,
                0.01m,
                null,
                1_900m,
                2_100m,
                10);
            return Task.FromResult<StrategyResult?>(new StrategyResult(nextState, [action]));
        }
    }

    private sealed class LostAcknowledgementOnceStore(
        IStrategyEventTransactionStore inner) : IStrategyEventTransactionStore
    {
        private bool acknowledgementLost;

        public StrategyEventCommitRequest? FirstRequest { get; private set; }

        public StrategyEventCommitReceipt? FirstReceipt { get; private set; }

        public Task<StrategyEventClaimResult> ClaimAsync(
            TenantExecutionContext context,
            StrategyEventReference reference,
            Guid claimToken,
            CancellationToken cancellationToken) =>
            inner.ClaimAsync(context, reference, claimToken, cancellationToken);

        public async Task<StrategyEventCommitReceipt> CommitAsync(
            TenantExecutionContext context,
            StrategyEventCommitRequest request,
            CancellationToken cancellationToken)
        {
            StrategyEventCommitReceipt receipt = await inner.CommitAsync(
                context,
                request,
                cancellationToken);
            if (!acknowledgementLost)
            {
                acknowledgementLost = true;
                FirstRequest = request;
                FirstReceipt = receipt;
                throw new IOException("Injected lost acknowledgement after durable commit.");
            }

            return receipt;
        }
    }

    private sealed class RejectingCommitCaptureStore(
        IStrategyEventTransactionStore inner) : IStrategyEventTransactionStore
    {
        public StrategyEventCommitRequest? Request { get; private set; }

        public Task<StrategyEventClaimResult> ClaimAsync(
            TenantExecutionContext context,
            StrategyEventReference reference,
            Guid claimToken,
            CancellationToken cancellationToken) =>
            inner.ClaimAsync(context, reference, claimToken, cancellationToken);

        public Task<StrategyEventCommitReceipt> CommitAsync(
            TenantExecutionContext context,
            StrategyEventCommitRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request ??= request;
            return Task.FromException<StrategyEventCommitReceipt>(
                new IOException("Injected failure before durable commit."));
        }
    }

    private sealed record AuthoritySeed(
        Guid TenantId,
        Guid DeploymentId,
        Guid WorkerNodeId,
        Guid WorkerAssignmentId,
        Guid SupervisorWorkloadId,
        long Generation);

    private sealed record ArtifactCounts(
        long States,
        long Events,
        long CommittedEvents,
        long Actions,
        long OutboxMessages,
        long AuditEvents);
}
