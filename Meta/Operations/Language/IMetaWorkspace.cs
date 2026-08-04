namespace Meta.Core.Operations;

public interface IMetaWorkspace : IMetaWorkspaceSource, IAsyncDisposable
{
    ValueTask<IReadOnlyList<OperationResult>> ExecuteAsync(
        IReadOnlyList<Operation> operations,
        CancellationToken cancellationToken = default);
}
