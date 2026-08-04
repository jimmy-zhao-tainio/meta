using Meta.Core.Domain;
using Meta.Core.Services;
using MetaWorkspaceGenerated = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace Meta.Core.Serialization;

public sealed class OpenedXmlWorkspace
{
    internal OpenedXmlWorkspace(
        string rootPath,
        InMemoryWorkspace state,
        MetaWorkspaceGenerated configuration,
        XmlWorkspaceLayout layout,
        WorkspaceLoadOptions? loadOptions,
        string fingerprint)
    {
        RootPath = Path.GetFullPath(rootPath);
        State = state ?? throw new ArgumentNullException(nameof(state));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        LoadOptions = loadOptions;
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

    internal MetaWorkspaceGenerated Configuration { get; private set; }
    internal XmlWorkspaceLayout Layout { get; private set; }
    internal WorkspaceLoadOptions? LoadOptions { get; }

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
}
