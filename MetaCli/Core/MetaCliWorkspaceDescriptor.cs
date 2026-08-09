using Meta.Surfaces;

namespace MetaCli.Core;

internal abstract record MetaCliWorkspaceDescriptor(string Directory)
{
    public const string FileName = "workspace.meta";

    public static MetaCliWorkspaceDescriptor Read(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var fullDirectory = Path.GetFullPath(directory);
        var metadata = WorkspaceMetaFile.Read(fullDirectory);
        return metadata.Representation switch
        {
            "xml" => new XmlWorkspaceDescriptor(fullDirectory, ResolvePath(fullDirectory, metadata.Location)),
            "csharp" => new CSharpWorkspaceDescriptor(fullDirectory, ResolvePath(fullDirectory, metadata.Location)),
            "sql" => new SqlWorkspaceDescriptor(fullDirectory, metadata.Location),
            _ => throw new InvalidOperationException("Workspace representation is not supported."),
        };
    }

    public static void WriteXml(string directory) =>
        WorkspaceMetaFile.WriteXml(directory);

    public static void WriteSql(string directory, string connectionEnvironmentVariable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionEnvironmentVariable);
        WorkspaceMetaFile.WriteSql(directory, connectionEnvironmentVariable);
    }

    private static string ResolvePath(string directory, string value) =>
        Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(directory, value));

}

internal sealed record XmlWorkspaceDescriptor(string WorkspaceDirectory, string Path)
    : MetaCliWorkspaceDescriptor(WorkspaceDirectory);

internal sealed record CSharpWorkspaceDescriptor(string WorkspaceDirectory, string Path)
    : MetaCliWorkspaceDescriptor(WorkspaceDirectory);

internal sealed record SqlWorkspaceDescriptor(string WorkspaceDirectory, string ConnectionEnvironmentVariable)
    : MetaCliWorkspaceDescriptor(WorkspaceDirectory);
