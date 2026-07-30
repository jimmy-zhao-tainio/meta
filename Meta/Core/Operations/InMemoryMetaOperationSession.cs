namespace Meta.Core.Operations;

public sealed class InMemoryMetaOperationSession
{
    private readonly MetaOperationInterpreter _interpreter;
    private GenericMetadataState _baseline;
    private GenericMetadataState _working;

    public InMemoryMetaOperationSession(
        GenericMetadataState source,
        MetaOperationInterpreter? interpreter = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        _interpreter = interpreter ?? new MetaOperationInterpreter();
        _baseline = source.Clone();
        _working = _baseline.Clone();
    }

    public GenericMetadataState Snapshot()
    {
        return _working.Clone();
    }

    public GenericMetaOperationResult Apply(MetaOperationPlan plan)
    {
        var result = _interpreter.Apply(_working, plan);
        _working = result.State;
        return new GenericMetaOperationResult(
            _working.Clone(),
            result.AppliedOperationCount);
    }

    public GenericMetadataState Commit()
    {
        _baseline = _working.Clone();
        return _baseline.Clone();
    }

    public void Discard()
    {
        _working = _baseline.Clone();
    }
}
