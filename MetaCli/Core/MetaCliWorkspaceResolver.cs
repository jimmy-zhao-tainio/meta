using Meta.Surfaces;
using Meta.Core.Connections;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Serialization;

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
        var descriptor = MetaCliWorkspaceDescriptor.Read(fullDirectory);

        try
        {
            return descriptor switch
            {
                XmlWorkspaceDescriptor xml => await OpenXmlAsync(xml.Path, cancellationToken).ConfigureAwait(false),
                CSharpWorkspaceDescriptor csharp => await OpenCSharpAsync(csharp.Path, cancellationToken).ConfigureAwait(false),
                SqlWorkspaceDescriptor sql => await OpenSqlAsync(sql.ConnectionEnvironmentVariable, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException("Workspace representation is not supported."),
            };
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
            await XmlWorkspaceWriter.WriteNewAsync(
                    workspace,
                    path,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (creation is MetaCliCSharpWorkspaceCreation csharp)
        {
            await CSharpWorkspace.CreateAsync(
                    workspace,
                    Path.GetFullPath(csharp.Directory),
                    cancellationToken)
                .ConfigureAwait(false);
            MetaCliWorkspaceDescriptor.WriteCSharp(csharp.Directory);
            return;
        }

        if (creation is MetaCliSqlWorkspaceCreation sql)
        {
            var directory = RequireEmptyDirectory(sql.Directory, "SQL");
            var connectionString = ConnectionEnvironmentVariableResolver
                .ResolveRequired(sql.ConnectionEnvironmentVariable);
            await SqlWorkspace.CreateAsync(
                    connectionString,
                    workspace,
                    cancellationToken)
                .ConfigureAwait(false);
            MetaCliWorkspaceDescriptor.WriteSql(
                directory,
                sql.ConnectionEnvironmentVariable);
            return;
        }

        throw new InvalidOperationException("Workspace representation is not supported.");
    }

    private static async Task<IMetaWorkspace> OpenXmlAsync(
        string location,
        CancellationToken cancellationToken) =>
        await XmlWorkspaceReader.OpenAsync(
                Path.GetFullPath(location),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

    private static async Task<IMetaWorkspace> OpenCSharpAsync(
        string location,
        CancellationToken cancellationToken) =>
        await CSharpWorkspace.OpenAsync(
                Path.GetFullPath(location),
                cancellationToken)
            .ConfigureAwait(false);

    private static async Task<IMetaWorkspace> OpenSqlAsync(
        string location,
        CancellationToken cancellationToken)
    {
        var connectionString = ConnectionEnvironmentVariableResolver
            .ResolveRequired(location);
        return await SqlWorkspace.OpenAsync(
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);
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
