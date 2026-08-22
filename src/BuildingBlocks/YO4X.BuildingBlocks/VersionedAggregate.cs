namespace YO4X.BuildingBlocks;

public abstract class VersionedAggregate
{
    protected VersionedAggregate(Guid id, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An aggregate identifier cannot be empty.", nameof(id));
        }

        Id = id;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; }

    public long Version { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    protected void RecordChange(DateTimeOffset occurredAt)
    {
        Version = checked(Version + 1);
        UpdatedAt = occurredAt.ToUniversalTime();
    }

    /// <summary>
    /// Restores persistence-owned concurrency metadata while keeping aggregate
    /// mutation setters private to the domain model.
    /// </summary>
    protected void RestorePersistenceState(long version, DateTimeOffset updatedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version);

        DateTimeOffset normalizedUpdatedAt = updatedAt.ToUniversalTime();
        if (normalizedUpdatedAt < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedAt));
        }

        Version = version;
        UpdatedAt = normalizedUpdatedAt;
    }
}
