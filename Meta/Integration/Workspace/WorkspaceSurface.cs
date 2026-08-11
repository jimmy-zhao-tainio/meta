using Meta.Core.Connections;
using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Surfaces;
using Meta.Surfaces.CSharp;
using Meta.Surfaces.Sql;
using Meta.Surfaces.Xml;

namespace Meta.Integration;

public static class WorkspaceSurface
{
    public static async Task<IMetaWorkspace> OpenAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var rootPath = Path.GetFullPath(workspacePath);
        var metadata = WorkspaceMetaFile.Read(rootPath);

        return metadata.Representation switch
        {
            "xml" => await XmlWorkspaceReader.OpenAsync(
                    rootPath,
                    cancellationToken)
                .ConfigureAwait(false),
            "csharp" => await CSharpWorkspace.OpenAsync(
                    rootPath,
                    cancellationToken)
                .ConfigureAwait(false),
            "sql" => await SqlWorkspace.OpenAsync(
                    ConnectionEnvironmentVariableResolver.ResolveRequired(metadata.Location),
                    cancellationToken)
                .ConfigureAwait(false),
            _ => throw new InvalidDataException(
                $"Workspace '{rootPath}' selects unsupported representation '{metadata.Representation}'."),
        };
    }

    public static async Task CreateAsync(
        InMemoryWorkspace workspace,
        string workspacePath,
        string representation,
        string? connectionEnvironmentVariable = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var rootPath = Path.GetFullPath(workspacePath);
        var selectedRepresentation = representation.Trim().ToLowerInvariant();
        switch (selectedRepresentation)
        {
            case "xml":
                await XmlWorkspaceWriter.WriteNewAsync(
                        workspace,
                        rootPath,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            case "csharp":
                await CSharpWorkspace.CreateAsync(
                        workspace,
                        rootPath,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            case "sql":
                var environmentVariable = connectionEnvironmentVariable ??
                    throw new ArgumentException(
                        "SQL workspace creation requires a connection environment variable.",
                        nameof(connectionEnvironmentVariable));
                var connectionString = ConnectionEnvironmentVariableResolver.ResolveRequired(
                    environmentVariable);
                await SqlWorkspace.CreateAsync(
                        connectionString,
                        workspace,
                        cancellationToken)
                    .ConfigureAwait(false);
                WorkspaceMetaFile.WriteSql(rootPath, environmentVariable);
                return;
            default:
                throw new ArgumentException(
                    $"Unsupported workspace representation '{representation}'.",
                    nameof(representation));
        }
    }
}
