using YO4X.BuildingBlocks;
using YO4X.SecretCoordination;

namespace YO4X.Domain.Tests;

public sealed class CredentialIngestionDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly string DigestA = new('a', 64);
    private static readonly string DigestB = new('b', 64);

    [Fact]
    public void SecretMaterialClearsOwnedBytesWhenDisposed()
    {
        byte[] bytes = [1, 2, 3, 4];

        using (var material = new SecretMaterial(bytes))
        {
            Assert.Equal(bytes, material.Bytes.ToArray());
            Assert.Equal("[REDACTED SECRET MATERIAL]", material.ToString());
        }

        Assert.All(bytes, value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void GrantReservationIsExclusiveUntilItsLeaseExpires()
    {
        CredentialIngestionGrant grant = CreateGrant();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        Assert.True(grant.TryReserve(first, Now, TimeSpan.FromSeconds(30)));
        Assert.False(grant.TryReserve(second, Now.AddSeconds(29), TimeSpan.FromSeconds(30)));
        Assert.True(grant.TryReserve(second, Now.AddSeconds(31), TimeSpan.FromSeconds(30)));
        Assert.Equal(second, grant.ReservationId);
        Assert.Equal(IngestionGrantState.Reserved, grant.State);
    }

    [Fact]
    public void CompletedGrantAcceptsOnlyItsReservationAndReceiptDigest()
    {
        CredentialIngestionGrant grant = CreateGrant();
        Guid reservationId = Guid.NewGuid();
        Assert.True(grant.TryReserve(reservationId, Now, TimeSpan.FromSeconds(30)));

        grant.MarkConsumed(reservationId, DigestB, Now.AddSeconds(2));
        grant.MarkConsumed(Guid.NewGuid(), DigestB, Now.AddSeconds(3));

        Assert.Equal(IngestionGrantState.Consumed, grant.State);
        Assert.Throws<DomainException>(() =>
            grant.MarkConsumed(reservationId, DigestA, Now.AddSeconds(4)));
    }

    [Fact]
    public async Task ProcessorNeverReadsBodyWhenDependencyIsUnavailable()
    {
        var store = new FakeStore { Ready = false };
        var broker = new FakeBroker();
        var processor = new CredentialIngestionProcessor(store, broker, new FixedClock(Now));
        bool bodyRead = false;

        await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(() => processor.ConsumeAsync(
            CreateProof(),
            _ =>
            {
                bodyRead = true;
                return ValueTask.FromResult(new SecretMaterial([1]));
            },
            CancellationToken.None));

        Assert.False(bodyRead);
        Assert.Equal(0, store.ReserveCount);
        Assert.Equal(0, broker.WriteCount);
    }

    [Fact]
    public async Task ProcessorReleasesReservationWhenBodyCannotBeRead()
    {
        var store = new FakeStore();
        var processor = new CredentialIngestionProcessor(store, new FakeBroker(), new FixedClock(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ConsumeAsync(
            CreateProof(),
            _ => ValueTask.FromException<SecretMaterial>(new InvalidOperationException("invalid body")),
            CancellationToken.None));

        Assert.Equal(1, store.ReleaseCount);
        Assert.Equal(0, store.CompleteCount);
    }

    [Fact]
    public async Task ProcessorKeepsReservationWhenVaultOutcomeIsUnknown()
    {
        var store = new FakeStore();
        var broker = new FakeBroker { WriteFailure = new TimeoutException("vault timeout") };
        var processor = new CredentialIngestionProcessor(store, broker, new FixedClock(Now));

        await Assert.ThrowsAsync<TimeoutException>(() => processor.ConsumeAsync(
            CreateProof(),
            _ => ValueTask.FromResult(new SecretMaterial([1, 2, 3])),
            CancellationToken.None));

        Assert.Equal(0, store.ReleaseCount);
        Assert.Equal(0, store.CompleteCount);
    }

    [Fact]
    public async Task ProcessorCompletesWriteAndClearsCredentialBytes()
    {
        var store = new FakeStore();
        var broker = new FakeBroker();
        var processor = new CredentialIngestionProcessor(store, broker, new FixedClock(Now));
        byte[] credentialBytes = [10, 20, 30];

        CredentialIngestionCompletion completion = await processor.ConsumeAsync(
            CreateProof(),
            _ => ValueTask.FromResult(new SecretMaterial(credentialBytes)),
            CancellationToken.None);

        Assert.Equal(store.Reservation.GrantId, completion.GrantId);
        Assert.Equal(1, broker.WriteCount);
        Assert.Equal(1, store.CompleteCount);
        Assert.All(credentialBytes, value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public async Task ProcessorRejectsUnverifiedProviderReceiptAndClearsCredentialBytes()
    {
        var store = new FakeStore();
        var broker = new FakeBroker { ReceiptValid = false };
        var processor = new CredentialIngestionProcessor(store, broker, new FixedClock(Now));
        byte[] credentialBytes = [10, 20, 30];

        await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(() => processor.ConsumeAsync(
            CreateProof(),
            _ => ValueTask.FromResult(new SecretMaterial(credentialBytes)),
            CancellationToken.None));

        Assert.Equal(1, broker.WriteCount);
        Assert.Equal(0, store.CompleteCount);
        Assert.All(credentialBytes, value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void ProviderReceiptRejectsUnsafeSchemesAndOversizedAttestationFields()
    {
        CredentialIngestionReservation reservation = FakeStore.CreateReservation(
            CredentialIngestionReservationDisposition.Acquired);
        SecretWriteBinding binding = reservation.ToWriteBinding();
        string signature = Convert.ToBase64String(new byte[64]);

        Assert.Throws<ArgumentException>(() => new SecretWriteReceipt(
            SecretBrokerProvider.HashiCorpVault,
            binding,
            "https://opaque.example/reference",
            SecretWriteReceiptState.Stored,
            "ed25519",
            "test-key-v1",
            signature));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecretWriteReceipt(
            SecretBrokerProvider.HashiCorpVault,
            binding,
            $"vault://opaque/{new string('a', 2_001)}",
            SecretWriteReceiptState.Stored,
            "ed25519",
            "test-key-v1",
            signature));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecretWriteReceipt(
            SecretBrokerProvider.HashiCorpVault,
            binding,
            "vault://opaque/reference",
            SecretWriteReceiptState.Stored,
            "ed25519",
            "test-key-v1",
            new string('A', 1_369)));
    }

    [Fact]
    public async Task CompletedReplayDoesNotReadOrRewriteCredential()
    {
        var store = new FakeStore
        {
            Reservation = FakeStore.CreateReservation(CredentialIngestionReservationDisposition.Completed)
        };
        var broker = new FakeBroker();
        var processor = new CredentialIngestionProcessor(store, broker, new FixedClock(Now));
        bool bodyRead = false;

        CredentialIngestionCompletion completion = await processor.ConsumeAsync(
            CreateProof(),
            _ =>
            {
                bodyRead = true;
                return ValueTask.FromResult(new SecretMaterial([1]));
            },
            CancellationToken.None);

        Assert.Equal(store.Reservation.GrantId, completion.GrantId);
        Assert.False(bodyRead);
        Assert.Equal(0, broker.WriteCount);
    }

    private static CredentialIngestionGrant CreateGrant() => CredentialIngestionGrant.Issue(
        Guid.NewGuid(),
        Guid.NewGuid(),
        CredentialIngestionOperation.Create,
        new Uri("https://ingestion.example"),
        DigestA,
        DigestB,
        Now.AddMinutes(5),
        new FixedClock(Now));

    private static CredentialIngestionProof CreateProof() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "https://ingestion.example", DigestA, DigestB);

    private sealed class FakeStore : ICredentialIngestionGrantStore
    {
        public bool Ready { get; init; } = true;

        public int ReserveCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public int CompleteCount { get; private set; }

        public CredentialIngestionReservation Reservation { get; init; } =
            CreateReservation(CredentialIngestionReservationDisposition.Acquired);

        public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Ready);

        public Task<CredentialIngestionReservation> ReserveAsync(
            CredentialIngestionProof proof,
            DateTimeOffset now,
            TimeSpan reservationDuration,
            CancellationToken cancellationToken)
        {
            ReserveCount++;
            return Task.FromResult(Reservation);
        }

        public Task ReleaseBeforeWriteAsync(
            CredentialIngestionReservation reservation,
            DateTimeOffset releasedAt,
            CancellationToken cancellationToken)
        {
            ReleaseCount++;
            return Task.CompletedTask;
        }

        public Task<CredentialIngestionCompletion> CompleteAsync(
            CredentialIngestionReservation reservation,
            SecretWriteReceipt receipt,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            CompleteCount++;
            return Task.FromResult(new CredentialIngestionCompletion(reservation.GrantId, completedAt));
        }

        public static CredentialIngestionReservation CreateReservation(
            CredentialIngestionReservationDisposition disposition) => new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                CredentialIngestionOperation.Create,
                Guid.NewGuid(),
                disposition,
                disposition == CredentialIngestionReservationDisposition.Completed ? Now : null);
    }

    private sealed class FakeBroker : IWriteOnlySecretBroker
    {
        public Exception? WriteFailure { get; init; }

        public bool ReceiptValid { get; init; } = true;

        public int WriteCount { get; private set; }

        public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public Task<SecretWriteReceipt> WriteAsync(
            SecretWriteBinding binding,
            SecretMaterial material,
            CancellationToken cancellationToken)
        {
            WriteCount++;
            if (WriteFailure is not null)
            {
                return Task.FromException<SecretWriteReceipt>(WriteFailure);
            }

            Assert.NotEmpty(material.Bytes.ToArray());
            return Task.FromResult(new SecretWriteReceipt(
                SecretBrokerProvider.HashiCorpVault,
                binding,
                "vault://opaque/reference",
                SecretWriteReceiptState.Stored,
                "ed25519",
                "test-key-v1",
                Convert.ToBase64String(new byte[64])));
        }

        public ValueTask<bool> VerifyReceiptAsync(
            SecretWriteReceipt receipt,
            CancellationToken cancellationToken) => ValueTask.FromResult(ReceiptValid);
    }
}
