using System.Xml;
using Meta.Operations.Domain;
using Meta.TypedModels;

namespace Meta.Surfaces.Xml;

public static class TypedWorkspaceXmlSerializer
{
    public static TModel Load<TModel>(string workspacePath, ITypedWorkspaceBinding<TModel> binding, bool searchUpward = false)
        where TModel : class =>
        LoadAsync(workspacePath, binding, searchUpward).GetAwaiter().GetResult();

    public static async Task<TModel> LoadAsync<TModel>(
        string workspacePath,
        ITypedWorkspaceBinding<TModel> binding,
        bool searchUpward = false,
        CancellationToken cancellationToken = default)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (searchUpward)
        {
            throw new NotSupportedException(
                "Typed workspace loading does not search parent directories. Pass an explicit workspace path.");
        }

        var rootPath = RequireDescriptorRoot(workspacePath);
        await using var opened = await XmlWorkspaceReader.OpenAsync(rootPath, cancellationToken).ConfigureAwait(false);
        return binding.Read(opened.State);
    }

    public static string CreateWorkspace<TModel>(
        string workspacePath, GenericModel contract, ITypedWorkspaceBinding<TModel> binding)
        where TModel : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(binding);
        var rootPath = Path.GetFullPath(workspacePath);
        if (Directory.Exists(rootPath) && Directory.EnumerateFileSystemEntries(rootPath).Any())
        {
            throw new InvalidOperationException($"Target directory must be empty: {rootPath}");
        }

        var empty = new InMemoryWorkspace(contract.Clone(), new GenericInstance { ModelName = contract.Name });
        _ = binding.Read(empty);
        XmlWorkspaceWriter.WriteNewAsync(empty, rootPath).GetAwaiter().GetResult();
        return rootPath;
    }

    public static bool IsWorkspace<TModel>(string workspacePath, ITypedWorkspaceBinding<TModel> binding)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return false;
        }

        try
        {
            RequireWorkspace(workspacePath, binding);
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

    public static string RequireWorkspace<TModel>(string workspacePath, ITypedWorkspaceBinding<TModel> binding)
        where TModel : class
    {
        _ = Load(workspacePath, binding);
        return TypedWorkspacePathResolver.ResolveWorkspaceRootFromPath(workspacePath);
    }

    public static void Save<TModel>(
        TModel model, string workspacePath, GenericModel contract, ITypedWorkspaceBinding<TModel> binding)
        where TModel : class =>
        SaveAsync(model, workspacePath, contract, binding).GetAwaiter().GetResult();

    public static async Task SaveAsync<TModel>(
        TModel model,
        string workspacePath,
        GenericModel contract,
        ITypedWorkspaceBinding<TModel> binding,
        CancellationToken cancellationToken = default)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(binding);
        var rootPath = Path.GetFullPath(workspacePath);
        var descriptorPath = Path.Combine(rootPath, Meta.Surfaces.WorkspaceMetaFile.FileName);
        if (!File.Exists(descriptorPath))
        {
            var desired = binding.Write(model, contract);
            await XmlWorkspaceWriter.WriteNewAsync(desired, rootPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var opened = await XmlWorkspaceReader.OpenAsync(rootPath, cancellationToken).ConfigureAwait(false);
        var updated = binding.Write(model, opened.State.Model);
        if (InMemoryWorkspaceComparer.FindDifference(opened.State, updated) == null)
        {
            return;
        }

        await XmlWorkspaceWriter.WriteAsync(opened, updated, [], cancellationToken).ConfigureAwait(false);
    }

    public static void SaveModel<TModel>(
        TModel model, string workspacePath, GenericModel contract, ITypedWorkspaceBinding<TModel> binding)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(binding);
        SaveModelAsync(model, workspacePath, contract, binding).GetAwaiter().GetResult();
    }

    private static async Task SaveModelAsync<TModel>(
        TModel model, string workspacePath, GenericModel contract, ITypedWorkspaceBinding<TModel> binding)
        where TModel : class
    {
        var desired = binding.Write(model, contract);
        var rootPath = Path.GetFullPath(workspacePath);
        var descriptorPath = Path.Combine(rootPath, Meta.Surfaces.WorkspaceMetaFile.FileName);
        if (!File.Exists(descriptorPath))
        {
            await XmlWorkspaceWriter.WriteNewAsync(
                    new InMemoryWorkspace(desired.Model, new GenericInstance { ModelName = desired.Model.Name }),
                    rootPath)
                .ConfigureAwait(false);
            return;
        }

        await using var opened = await XmlWorkspaceReader.OpenAsync(rootPath).ConfigureAwait(false);
        await XmlWorkspaceWriter.WriteAsync(opened, new InMemoryWorkspace(desired.Model, opened.State.Instance), [])
            .ConfigureAwait(false);
    }

    private static string RequireDescriptorRoot(string workspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var rootPath = TypedWorkspacePathResolver.ResolveWorkspaceRootFromPath(workspacePath);
        if (Directory.Exists(rootPath) &&
            !File.Exists(Path.Combine(rootPath, Meta.Surfaces.WorkspaceMetaFile.FileName)))
        {
            throw new InvalidDataException(
                $"Workspace '{rootPath}' does not contain {Meta.Surfaces.WorkspaceMetaFile.FileName}.");
        }

        return rootPath;
    }

    public static string ResolveWorkspaceRootFromPath(string inputPath) =>
        TypedWorkspacePathResolver.ResolveWorkspaceRootFromPath(inputPath);

    public static string ResolveInstanceDirectoryPath(string workspaceRootPath) =>
        TypedWorkspacePathResolver.ResolveInstanceDirectoryPath(workspaceRootPath);
}
