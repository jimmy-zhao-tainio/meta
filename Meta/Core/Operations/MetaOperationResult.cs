namespace Meta.Core.Operations;

public class MetaOperationResult
{
    public MetaOperationResult(int appliedOperationCount)
    {
        AppliedOperationCount = appliedOperationCount;
    }

    public int AppliedOperationCount { get; }
}

public sealed class GenericMetaOperationResult : MetaOperationResult
{
    public GenericMetaOperationResult(
        GenericMetadataState state,
        int appliedOperationCount)
        : base(appliedOperationCount)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public GenericMetadataState State { get; }
}
