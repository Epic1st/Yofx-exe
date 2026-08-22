namespace YO4X.BuildingBlocks;

public sealed class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException()
        : base("The resource was not found.")
    {
    }
}
