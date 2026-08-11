using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Surfaces;
using MetaWorkspaceGenerated = Meta.Surfaces.Configuration.MetaWorkspace;

namespace Meta.Surfaces.Xml;

public sealed class OpenedXmlWorkspace : IMetaWorkspace
{
    internal OpenedXmlWorkspace(
        string rootPath,
        InMemoryWorkspace state,
        MetaWorkspaceGenerated configuration,
        XmlWorkspaceLayout layout,
        string fingerprint)
    {
        RootPath = Path.GetFullPath(rootPath);
        State = state ?? throw new ArgumentNullException(nameof(state));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        Fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
    }

    public string RootPath { get; }
    public InMemoryWorkspace State { get; private set; }
    public GenericModel Model => State.Model;
    public GenericInstance Instance => State.Instance;
    public string ContractVersion => MetaWorkspaceGenerated.GetContractVersion(Configuration);
    public string ModelFilePath => WorkspacePathResolver.ResolvePathFromWorkspaceRoot(
        RootPath,
        MetaWorkspaceGenerated.GetModelFile(Configuration));
    public string InstanceDirectoryPath => WorkspacePathResolver.ResolvePathFromWorkspaceRoot(
        RootPath,
        MetaWorkspaceGenerated.GetInstanceDir(Configuration));
    public string Fingerprint { get; private set; }
    private IMetaWorkspaceSource Source => new InMemoryWorkspaceSource(State);

    internal MetaWorkspaceGenerated Configuration { get; private set; }
    internal XmlWorkspaceLayout Layout { get; private set; }

    internal void Accept(
        InMemoryWorkspace state,
        MetaWorkspaceGenerated configuration,
        XmlWorkspaceLayout layout,
        string fingerprint)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        Fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
    }

    public async ValueTask<IReadOnlyList<OperationResult>> ExecuteAsync(
        IReadOnlyList<Operation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        cancellationToken.ThrowIfCancellationRequested();

        var applied = InMemoryOperations.ExecuteBatch(State, operations);
        await XmlWorkspaceWriter.WriteAsync(
                this,
                applied.Workspace,
                applied.Results,
                cancellationToken)
            .ConfigureAwait(false);
        return applied.Results;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ValueTask<string> ReadModelNameAsync(
        CancellationToken cancellationToken = default) =>
        Source.ReadModelNameAsync(cancellationToken);

    public IAsyncEnumerable<string> ReadEntityNamesAsync(
        CancellationToken cancellationToken = default) =>
        Source.ReadEntityNamesAsync(cancellationToken);

    public IAsyncEnumerable<PropertyDefinition> ReadPropertiesAsync(
        string entityName,
        CancellationToken cancellationToken = default) =>
        Source.ReadPropertiesAsync(entityName, cancellationToken);

    public IAsyncEnumerable<RelationshipDefinition> ReadRelationshipsAsync(
        string entityName,
        CancellationToken cancellationToken = default) =>
        Source.ReadRelationshipsAsync(entityName, cancellationToken);

    public IAsyncEnumerable<RecordData> ReadRecordsAsync(
        string entityName,
        CancellationToken cancellationToken = default) =>
        Source.ReadRecordsAsync(entityName, cancellationToken);

    public ValueTask<long> CountRecordsAsync(
        string entityName,
        CancellationToken cancellationToken = default) =>
        Source.CountRecordsAsync(entityName, cancellationToken);

    public ValueTask<RecordQueryResult> QueryRecordsAsync(
        string entityName,
        RecordQuery query,
        CancellationToken cancellationToken = default) =>
        Source.QueryRecordsAsync(entityName, query, cancellationToken);

    public ValueTask<RecordData?> ReadRecordAsync(
        string entityName,
        string id,
        CancellationToken cancellationToken = default) =>
        Source.ReadRecordAsync(entityName, id, cancellationToken);
}
