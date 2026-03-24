using Meta.Core.WorkspaceConfig.Generated;

namespace Meta.Core.Domain;

public sealed class Workspace
{
    public string WorkspaceRootPath { get; set; } = string.Empty;
    public string MetadataRootPath { get; set; } = string.Empty;
    public MetaWorkspace WorkspaceConfig { get; set; } = new();
    public GenericModel Model { get; set; } = new();
    public GenericInstance Instance { get; set; } = new();
    public WorkspaceDiagnostics Diagnostics { get; set; } = new();
    public bool IsDirty { get; set; }
}

