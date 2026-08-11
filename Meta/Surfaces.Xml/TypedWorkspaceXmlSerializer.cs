using System.Xml;
using Meta.Operations.Domain;
using Meta.TypedModels;

namespace Meta.Surfaces.Xml;

public static class TypedWorkspaceXmlSerializer
{
    public static TModel Load<TModel>(string workspacePath, bool searchUpward = false)
        where TModel : class, new()
    {
        if (searchUpward)
        {
            throw new NotSupportedException(
                "Typed workspace loading does not search parent directories. Pass an explicit workspace path.");
        }

        var rootPath = RequireWorkspace<TModel>(workspacePath);
        var opened = XmlWorkspaceReader.OpenAsync(rootPath).GetAwaiter().GetResult();
        return TypedModelMapper.FromWorkspace(opened.State, static () => new TModel());
    }

    public static async Task<TModel> LoadAsync<TModel>(
        string workspacePath,
        bool searchUpward = false,
        CancellationToken cancellationToken = default)
        where TModel : class, new()
    {
        if (searchUpward)
        {
            throw new NotSupportedException(
                "Typed workspace loading does not search parent directories. Pass an explicit workspace path.");
        }

        var rootPath = RequireWorkspace<TModel>(workspacePath);
        var opened = await XmlWorkspaceReader.OpenAsync(rootPath, cancellationToken).ConfigureAwait(false);
        return TypedModelMapper.FromWorkspace(opened.State, static () => new TModel());
    }

    public static string CreateWorkspace<TModel>(string workspacePath)
        where TModel : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var rootPath = Path.GetFullPath(workspacePath);
        if (Directory.Exists(rootPath) && Directory.EnumerateFileSystemEntries(rootPath).Any())
        {
            throw new InvalidOperationException($"Target directory must be empty: {rootPath}");
        }

        XmlWorkspaceWriter.WriteNewAsync(
                TypedModelMapper.ToWorkspace(new TModel()),
                rootPath)
            .GetAwaiter()
            .GetResult();
        return rootPath;
    }

    public static bool IsWorkspace<TModel>(string workspacePath)
        where TModel : class, new()
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return false;
        }

        try
        {
            RequireWorkspace<TModel>(workspacePath);
            return true;
        }
        catch (Exception exception) when (
            exception is DirectoryNotFoundException or
                FileNotFoundException or
                InvalidDataException or
                InvalidOperationException or
                UnauthorizedAccessException or
                XmlException)
        {
            return false;
        }
    }

    public static string RequireWorkspace<TModel>(string workspacePath)
        where TModel : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var rootPath = TypedWorkspacePathResolver.ResolveWorkspaceRootFromPath(workspacePath);
        if (Directory.Exists(rootPath) &&
            !File.Exists(Path.Combine(rootPath, Meta.Surfaces.WorkspaceMetaFile.FileName)))
        {
            throw new InvalidDataException(
                $"Workspace '{rootPath}' does not contain {Meta.Surfaces.WorkspaceMetaFile.FileName}.");
        }

        var opened = XmlWorkspaceReader.OpenAsync(rootPath).GetAwaiter().GetResult();
        TypedModelMapper.FromWorkspace(opened.State, static () => new TModel());
        return rootPath;
    }

    public static void Save<TModel>(TModel model, string workspacePath)
        where TModel : class, new() =>
        SaveAsync(model, workspacePath).GetAwaiter().GetResult();

    public static async Task SaveAsync<TModel>(
        TModel model,
        string workspacePath,
        CancellationToken cancellationToken = default)
        where TModel : class, new()
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var desired = TypedModelMapper.ToWorkspace(model);
        var rootPath = Path.GetFullPath(workspacePath);
        var descriptorPath = Path.Combine(rootPath, Meta.Surfaces.WorkspaceMetaFile.FileName);
        if (!File.Exists(descriptorPath))
        {
            await XmlWorkspaceWriter.WriteNewAsync(desired, rootPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        var opened = await XmlWorkspaceReader.OpenAsync(rootPath, cancellationToken).ConfigureAwait(false);
        if (InMemoryWorkspaceComparer.FindDifference(opened.State, desired) == null)
        {
            return;
        }

        await XmlWorkspaceWriter.WriteAsync(opened, desired, [], cancellationToken).ConfigureAwait(false);
    }

    public static void SaveModel<TModel>(TModel model, string workspacePath)
        where TModel : class, new()
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var desired = TypedModelMapper.ToWorkspace(model);
        var rootPath = Path.GetFullPath(workspacePath);
        var descriptorPath = Path.Combine(rootPath, Meta.Surfaces.WorkspaceMetaFile.FileName);
        if (!File.Exists(descriptorPath))
        {
            XmlWorkspaceWriter.WriteNewAsync(
                    new InMemoryWorkspace(
                        desired.Model,
                        new GenericInstance { ModelName = desired.Model.Name }),
                    rootPath)
                .GetAwaiter()
                .GetResult();
            return;
        }

        var opened = XmlWorkspaceReader.OpenAsync(rootPath).GetAwaiter().GetResult();
        XmlWorkspaceWriter.WriteAsync(
                opened,
                new InMemoryWorkspace(desired.Model, opened.State.Instance),
                [])
            .GetAwaiter()
            .GetResult();
    }

    public static string ResolveWorkspaceRootFromPath(string inputPath) =>
        TypedWorkspacePathResolver.ResolveWorkspaceRootFromPath(inputPath);

    public static string ResolveInstanceDirectoryPath(string workspaceRootPath) =>
        TypedWorkspacePathResolver.ResolveInstanceDirectoryPath(workspaceRootPath);
}
