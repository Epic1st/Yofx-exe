using YO4X.Runtime.Contracts;

namespace YO4X.RuntimeOperations;

public sealed class RuntimeEnvelopeCursor
{
    private readonly object _sync = new();
    private readonly int _rememberedEventCapacity;
    private readonly HashSet<Guid> _rememberedEventIds = [];
    private readonly Queue<Guid> _rememberedEventOrder = [];
    private long _generation;
    private long _lastAcceptedSequence;

    public RuntimeEnvelopeCursor(
        Guid deploymentId,
        Guid workerInstanceId,
        long generation,
        int rememberedEventCapacity = 4096)
    {
        if (deploymentId == Guid.Empty)
        {
            throw new ArgumentException("Deployment identifier cannot be empty.", nameof(deploymentId));
        }

        if (workerInstanceId == Guid.Empty)
        {
            throw new ArgumentException("Worker identifier cannot be empty.", nameof(workerInstanceId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rememberedEventCapacity);
        DeploymentId = deploymentId;
        WorkerInstanceId = workerInstanceId;
        _generation = generation;
        _rememberedEventCapacity = rememberedEventCapacity;
    }

    public Guid DeploymentId { get; }

    public Guid WorkerInstanceId { get; }

    public long Generation
    {
        get
        {
            lock (_sync)
            {
                return _generation;
            }
        }
    }

    public long LastAcceptedSequence
    {
        get
        {
            lock (_sync)
            {
                return _lastAcceptedSequence;
            }
        }
    }

    public void ActivateGeneration(long generation)
    {
        lock (_sync)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(generation, _generation);

            _generation = generation;
            _lastAcceptedSequence = 0;
            _rememberedEventIds.Clear();
            _rememberedEventOrder.Clear();
        }
    }

    public RuntimeEnvelopeValidation ValidateAndRecord<TPayload>(RuntimeEnvelope<TPayload> envelope)
        where TPayload : notnull
    {
        ArgumentNullException.ThrowIfNull(envelope);

        lock (_sync)
        {
            long expectedSequence = checked(_lastAcceptedSequence + 1);
            if (envelope.ContractVersion != RuntimeContractVersions.EnvelopeV1)
            {
                return Reject(
                    RuntimeEnvelopeDecision.UnsupportedVersion,
                    "runtime_envelope_version_unsupported",
                    expectedSequence);
            }

            if (envelope.DeploymentId == Guid.Empty
                || envelope.WorkerInstanceId == Guid.Empty
                || envelope.EventId == Guid.Empty
                || envelope.Generation <= 0
                || envelope.Sequence <= 0
                || envelope.ReceivedAtUtc.Offset != TimeSpan.Zero
                || envelope.BrokerTimestampUtc is { Offset: var offset } && offset != TimeSpan.Zero
                || envelope.Payload is null)
            {
                return Reject(RuntimeEnvelopeDecision.InvalidIdentity, "runtime_envelope_shape_invalid", expectedSequence);
            }

            if (envelope.DeploymentId != DeploymentId)
            {
                return Reject(RuntimeEnvelopeDecision.WrongDeployment, "runtime_envelope_deployment_mismatch", expectedSequence);
            }


            if (envelope.WorkerInstanceId != WorkerInstanceId)
            {
                return Reject(RuntimeEnvelopeDecision.WrongWorker, "runtime_envelope_worker_mismatch", expectedSequence);
            }

            if (envelope.Generation != _generation)
            {
                return Reject(RuntimeEnvelopeDecision.FencedGeneration, "runtime_envelope_generation_fenced", expectedSequence);
            }

            if (_rememberedEventIds.Contains(envelope.EventId))
            {
                return new RuntimeEnvelopeValidation(
                    RuntimeEnvelopeDecision.Duplicate,
                    "runtime_envelope_duplicate",
                    _generation,
                    expectedSequence);
            }

            if (envelope.Sequence < expectedSequence)
            {
                return Reject(RuntimeEnvelopeDecision.StaleSequence, "runtime_envelope_sequence_stale", expectedSequence);
            }

            if (envelope.Sequence > expectedSequence)
            {
                return Reject(RuntimeEnvelopeDecision.SequenceGap, "runtime_envelope_sequence_gap", expectedSequence);
            }

            _lastAcceptedSequence = envelope.Sequence;
            Remember(envelope.EventId);
            return new RuntimeEnvelopeValidation(
                RuntimeEnvelopeDecision.Accepted,
                "runtime_envelope_accepted",
                _generation,
                checked(_lastAcceptedSequence + 1));
        }
    }

    private RuntimeEnvelopeValidation Reject(
        RuntimeEnvelopeDecision decision,
        string code,
        long expectedSequence) =>
        new(decision, code, _generation, expectedSequence);

    private void Remember(Guid eventId)
    {
        _rememberedEventIds.Add(eventId);
        _rememberedEventOrder.Enqueue(eventId);
        while (_rememberedEventOrder.Count > _rememberedEventCapacity)
        {
            Guid forgottenEventId = _rememberedEventOrder.Dequeue();
            _rememberedEventIds.Remove(forgottenEventId);
        }
    }
}
