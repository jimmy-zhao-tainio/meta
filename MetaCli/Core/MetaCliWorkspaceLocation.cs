namespace MetaCli.Core;

public sealed record MetaCliWorkspaceLocation(string Directory)
{
    public string Location => Directory;

    public string FileSystemPath => Directory;

    public static MetaCliWorkspaceLocation Resolve(
        MetaCliInvocation invocation,
        string workspaceParameter = "workspace",
        bool useCurrentDirectoryWhenLocationIsMissing = true)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceParameter);
        string? directory;
        try
        {
            directory = invocation.Optional(workspaceParameter);
        }
        catch (KeyNotFoundException)
        {
            directory = null;
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            if (!useCurrentDirectoryWhenLocationIsMissing)
            {
                throw new InvalidOperationException(
                    $"Workspace input requires --{workspaceParameter} <path>.");
            }

            directory = System.IO.Directory.GetCurrentDirectory();
        }

        var fullDirectory = Path.GetFullPath(directory);
        _ = MetaCliWorkspaceDescriptor.Read(fullDirectory);
        return new MetaCliWorkspaceLocation(fullDirectory);
    }
}
