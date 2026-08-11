using Meta.Surfaces;
using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Integration;

namespace MetaCli.Core;

internal static class MetaCliWorkspaceResolver
{
    public static async Task<IMetaWorkspace> OpenAsync(
        MetaCliInvocation invocation,
        string workspaceParameter,
        bool useCurrentDirectoryWhenLocationIsMissing = true,
        CancellationToken cancellationToken = default)
    {
        var directory = Optional(invocation, workspaceParameter);
        if (string.IsNullOrWhiteSpace(directory))
        {
            if (!useCurrentDirectoryWhenLocationIsMissing)
            {
                throw new InvalidOperationException(
                    $"Workspace input requires --{workspaceParameter} <path>.");
            }

            directory = Directory.GetCurrentDirectory();
        }

        return await OpenAsync(directory, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<IMetaWorkspace> OpenAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var fullDirectory = Path.GetFullPath(directory);

        try
        {
            return await WorkspaceSurface.OpenAsync(
                    fullDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Workspace '{fullDirectory}' could not be opened. {exception.Message}",
                exception);
        }
    }

    public static async Task CreateAsync(
        MetaCliWorkspaceCreation creation,
        InMemoryWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(creation);
        ArgumentNullException.ThrowIfNull(workspace);

        if (creation is MetaCliXmlWorkspaceCreation xml)
        {
            var path = RequireEmptyDirectory(xml.Directory, "XML");
            await WorkspaceSurface.CreateAsync(
                    workspace,
                    path,
                    "xml",
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (creation is MetaCliCSharpWorkspaceCreation csharp)
        {
            await WorkspaceSurface.CreateAsync(
                    workspace,
                    Path.GetFullPath(csharp.Directory),
                    "csharp",
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (creation is MetaCliSqlWorkspaceCreation sql)
        {
            var directory = RequireEmptyDirectory(sql.Directory, "SQL");
            await WorkspaceSurface.CreateAsync(
                    workspace,
                    directory,
                    "sql",
                    sql.ConnectionEnvironmentVariable,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException("Workspace representation is not supported.");
    }

    private static string RequireEmptyDirectory(
        string location,
        string surface)
    {
        var path = Path.GetFullPath(location);
        if (Directory.Exists(path) &&
            Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new InvalidOperationException(
                $"{surface} workspace destination '{path}' must be empty.");
        }

        return path;
    }

    private static string? Optional(MetaCliInvocation invocation, string parameter)
    {
        try
        {
            return invocation.Optional(parameter);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

}
