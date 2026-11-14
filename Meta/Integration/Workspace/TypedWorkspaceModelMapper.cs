using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Surfaces;
using Meta.TypedModels;

namespace Meta.Integration;

public static class TypedWorkspaceModelMapper
{
    public static TModel Load<TModel>(
        string workspacePath,
        ITypedWorkspaceBinding<TModel> binding,
        bool searchUpward = false,
        CancellationToken cancellationToken = default)
        where TModel : class =>
        LoadAsync(workspacePath, binding, searchUpward, cancellationToken)
            .GetAwaiter()
            .GetResult();

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

        var state = await LoadStateAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        return FromInMemoryWorkspace(state, binding);
    }

    public static TModel Load<TModel>(
        string workspacePath,
        bool searchUpward = false)
        where TModel : class, new() =>
        LoadAsync<TModel>(workspacePath, searchUpward)
            .GetAwaiter()
            .GetResult();

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

        var state = await LoadStateAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        return FromInMemoryWorkspace(
            state,
            static () => new TModel());
    }

    public static async Task<InMemoryWorkspace> LoadStateAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        await using var workspace = await WorkspaceSurface.OpenAsync(
                workspacePath,
                cancellationToken)
            .ConfigureAwait(false);
        return await WorkspaceComposition.MaterializeAsync(
                workspace,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static void Create<TModel>(
        TModel model,
        GenericModel contract,
        ITypedWorkspaceBinding<TModel> binding,
        string workspacePath,
        string representation,
        string? connectionEnvironmentVariable = null,
        CancellationToken cancellationToken = default)
        where TModel : class =>
        CreateAsync(
                model,
                contract,
                binding,
                workspacePath,
                representation,
                connectionEnvironmentVariable,
                cancellationToken)
            .GetAwaiter()
            .GetResult();

    public static async Task CreateAsync<TModel>(
        TModel model,
        GenericModel contract,
        ITypedWorkspaceBinding<TModel> binding,
        string workspacePath,
        string representation,
        string? connectionEnvironmentVariable = null,
        CancellationToken cancellationToken = default)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(representation);
        await WorkspaceSurface.CreateAsync(
                ToInMemoryWorkspace(model, contract, binding),
                workspacePath,
                representation,
                connectionEnvironmentVariable,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static void Create<TModel>(
        TModel model,
        string workspacePath,
        string representation,
        string? connectionEnvironmentVariable = null)
        where TModel : class, new() =>
        CreateAsync(
                model,
                workspacePath,
                representation,
                connectionEnvironmentVariable)
            .GetAwaiter()
            .GetResult();

    public static async Task CreateAsync<TModel>(
        TModel model,
        string workspacePath,
        string representation,
        string? connectionEnvironmentVariable = null,
        CancellationToken cancellationToken = default)
        where TModel : class, new()
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(representation);
        await WorkspaceSurface.CreateAsync(
                ToInMemoryWorkspace(model),
                workspacePath,
                representation,
                connectionEnvironmentVariable,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static void Save<TModel>(
        TModel model,
        string workspacePath,
        ITypedWorkspaceBinding<TModel> binding,
        CancellationToken cancellationToken = default)
        where TModel : class =>
        SaveAsync(model, workspacePath, binding, cancellationToken)
            .GetAwaiter()
            .GetResult();

    public static async Task SaveAsync<TModel>(
        TModel model,
        string workspacePath,
        ITypedWorkspaceBinding<TModel> binding,
        CancellationToken cancellationToken = default)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var rootPath = Path.GetFullPath(workspacePath);
        if (!File.Exists(Path.Combine(rootPath, WorkspaceMetaFile.FileName)))
        {
            throw new InvalidOperationException(
                $"Workspace '{rootPath}' does not exist. Use TypedWorkspaceModelMapper.Create with an explicit representation to create it.");
        }

        await using var workspace = await WorkspaceSurface.OpenAsync(
                rootPath,
                cancellationToken)
            .ConfigureAwait(false);
        var current = await WorkspaceComposition.MaterializeAsync(
                workspace,
                cancellationToken)
            .ConfigureAwait(false);
        var desired = ToInMemoryWorkspace(model, current.Model, binding);
        var operations = WorkspaceSynchronization.PlanInstanceChanges(current, desired);
        if (operations.Count > 0)
        {
            await workspace.ExecuteAsync(operations, cancellationToken).ConfigureAwait(false);
        }
    }

    public static void Save<TModel>(
        TModel model,
        string workspacePath)
        where TModel : class, new() =>
        SaveAsync(model, workspacePath)
            .GetAwaiter()
            .GetResult();

    public static async Task SaveAsync<TModel>(
        TModel model,
        string workspacePath,
        CancellationToken cancellationToken = default)
        where TModel : class, new()
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var desired = ToInMemoryWorkspace(model);
        var rootPath = Path.GetFullPath(workspacePath);
        if (!File.Exists(Path.Combine(rootPath, WorkspaceMetaFile.FileName)))
        {
            throw new InvalidOperationException(
                $"Workspace '{rootPath}' does not exist. Use TypedWorkspaceModelMapper.Create with an explicit representation to create it.");
        }

        await using var workspace = await WorkspaceSurface.OpenAsync(
                rootPath,
                cancellationToken)
            .ConfigureAwait(false);
        var current = await WorkspaceComposition.MaterializeAsync(
                workspace,
                cancellationToken)
            .ConfigureAwait(false);
        var operations = WorkspaceSynchronization.PlanInstanceChanges(
            current,
            desired);
        if (operations.Count > 0)
        {
            await workspace.ExecuteAsync(
                    operations,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public static TModel FromInMemoryWorkspace<TModel>(
        InMemoryWorkspace workspace,
        ITypedWorkspaceBinding<TModel> binding)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(binding);
        return binding.Read(workspace);
    }

    public static InMemoryWorkspace ToInMemoryWorkspace<TModel>(
        TModel model,
        GenericModel contract,
        ITypedWorkspaceBinding<TModel> binding)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(binding);
        return binding.Write(model, contract);
    }

    public static TModel FromInMemoryWorkspace<TModel>(
        InMemoryWorkspace workspace,
        Func<TModel> createModel)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(createModel);
        return TypedModelMapper.FromWorkspace(
            workspace,
            createModel);
    }

    public static InMemoryWorkspace ToInMemoryWorkspace<TModel>(TModel model)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(model);
        return TypedModelMapper.ToWorkspace(model);
    }
}
