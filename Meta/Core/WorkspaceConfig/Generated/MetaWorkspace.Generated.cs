namespace Meta.Core.WorkspaceConfig.Generated;

public sealed partial class MetaWorkspace
{
    public List<CanonicalOrder> CanonicalOrder { get; set; } = new();
    public List<Encoding> Encoding { get; set; } = new();
    public List<EntityStorage> EntityStorage { get; set; } = new();
    public List<Newlines> Newlines { get; set; } = new();
    public List<Workspace> Workspace { get; set; } = new();
    public List<WorkspaceLayout> WorkspaceLayout { get; set; } = new();

}

public sealed class Workspace
{
    public string Id { get; set; } = string.Empty;
    public string FormatVersion { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AttributesOrderId { get; set; } = string.Empty;
    public CanonicalOrder AttributesOrder { get; set; } = new();
    public string EncodingId { get; set; } = string.Empty;
    public Encoding Encoding { get; set; } = new();
    public string EntitiesOrderId { get; set; } = string.Empty;
    public CanonicalOrder EntitiesOrder { get; set; } = new();
    public string NewlinesId { get; set; } = string.Empty;
    public Newlines Newlines { get; set; } = new();
    public string PropertiesOrderId { get; set; } = string.Empty;
    public CanonicalOrder PropertiesOrder { get; set; } = new();
    public string RelationshipsOrderId { get; set; } = string.Empty;
    public CanonicalOrder RelationshipsOrder { get; set; } = new();
    public string RowsOrderId { get; set; } = string.Empty;
    public CanonicalOrder RowsOrder { get; set; } = new();
    public string WorkspaceLayoutId { get; set; } = string.Empty;
    public WorkspaceLayout WorkspaceLayout { get; set; } = new();
}

public sealed class WorkspaceLayout
{
    public string Id { get; set; } = string.Empty;
    public string InstanceDirPath { get; set; } = string.Empty;
    public string ModelFilePath { get; set; } = string.Empty;
}

public sealed class Encoding
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class Newlines
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class CanonicalOrder
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class EntityStorage
{
    public string Id { get; set; } = string.Empty;
    public string DirectoryPath { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public string StorageKind { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public Workspace Workspace { get; set; } = new();
}
