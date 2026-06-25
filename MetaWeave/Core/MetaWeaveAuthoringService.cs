using Meta.Core.Domain;
using Meta.Core.Services;
using MetaWeaveModel = global::MetaWeave.MetaWeaveModel;
using WeaveModelReference = global::MetaWeave.ModelReference;
using WeavePropertyBinding = global::MetaWeave.PropertyBinding;

namespace MetaWeave.Core;

public interface IMetaWeaveAuthoringService
{
    Task AddModelReferenceAsync(
        MetaWeaveModel weaveModel,
        string weaveWorkspaceRootPath,
        string alias,
        string modelName,
        string workspacePath,
        CancellationToken cancellationToken = default);

    Task AddPropertyBindingAsync(
        MetaWeaveModel weaveModel,
        string weaveWorkspaceRootPath,
        string name,
        string sourceModelAlias,
        string sourceEntity,
        string sourceProperty,
        string targetModelAlias,
        string targetEntity,
        string targetProperty,
        CancellationToken cancellationToken = default);
}

public sealed class MetaWeaveAuthoringService : IMetaWeaveAuthoringService
{
    private readonly IWorkspaceService _workspaceService;

    public MetaWeaveAuthoringService()
        : this(new WorkspaceService())
    {
    }

    public MetaWeaveAuthoringService(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
    }

    public async Task AddModelReferenceAsync(
        MetaWeaveModel weaveModel,
        string weaveWorkspaceRootPath,
        string alias,
        string modelName,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(weaveModel);
        RequireNonEmpty(weaveWorkspaceRootPath, nameof(weaveWorkspaceRootPath));
        RequireNonEmpty(alias, nameof(alias));
        RequireNonEmpty(modelName, nameof(modelName));
        RequireNonEmpty(workspacePath, nameof(workspacePath));

        var resolvedWorkspacePath = Path.GetFullPath(workspacePath);
        var referencedWorkspace = await _workspaceService.LoadAsync(resolvedWorkspacePath, searchUpward: false, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(referencedWorkspace.Model.Name, modelName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Referenced workspace '{resolvedWorkspacePath}' contained model '{referencedWorkspace.Model.Name}', not '{modelName}'.");
        }

        if (weaveModel.ModelReferenceList.Any(record => string.Equals(record.Alias, alias, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"ModelReference alias '{alias}' already exists.");
        }

        var normalizedWorkspacePath = NormalizeWorkspacePathForStorage(
            weaveWorkspaceRootPath,
            resolvedWorkspacePath);

        weaveModel.ModelReferenceList.Add(new WeaveModelReference
        {
            Id = AllocateNumericId(weaveModel.ModelReferenceList),
            Alias = alias,
            ModelName = modelName,
            WorkspacePath = normalizedWorkspacePath
        });
    }

    public async Task AddPropertyBindingAsync(
        MetaWeaveModel weaveModel,
        string weaveWorkspaceRootPath,
        string name,
        string sourceModelAlias,
        string sourceEntity,
        string sourceProperty,
        string targetModelAlias,
        string targetEntity,
        string targetProperty,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(weaveModel);
        RequireNonEmpty(weaveWorkspaceRootPath, nameof(weaveWorkspaceRootPath));
        RequireNonEmpty(name, nameof(name));
        RequireNonEmpty(sourceModelAlias, nameof(sourceModelAlias));
        RequireNonEmpty(sourceEntity, nameof(sourceEntity));
        RequireNonEmpty(sourceProperty, nameof(sourceProperty));
        RequireNonEmpty(targetModelAlias, nameof(targetModelAlias));
        RequireNonEmpty(targetEntity, nameof(targetEntity));
        RequireNonEmpty(targetProperty, nameof(targetProperty));

        var sourceModel = weaveModel.ModelReferenceList.SingleOrDefault(record => string.Equals(record.Alias, sourceModelAlias, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"ModelReference alias '{sourceModelAlias}' was not found.");
        var targetModel = weaveModel.ModelReferenceList.SingleOrDefault(record => string.Equals(record.Alias, targetModelAlias, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"ModelReference alias '{targetModelAlias}' was not found.");

        var sourceWorkspace = await LoadReferencedWorkspaceAsync(weaveWorkspaceRootPath, sourceModel, cancellationToken).ConfigureAwait(false);
        var targetWorkspace = await LoadReferencedWorkspaceAsync(weaveWorkspaceRootPath, targetModel, cancellationToken).ConfigureAwait(false);
        ValidateBindingEndpoint(sourceWorkspace, sourceEntity, sourceProperty, allowId: false, bindingSide: "source");
        ValidateBindingEndpoint(targetWorkspace, targetEntity, targetProperty, allowId: true, bindingSide: "target");

        if (weaveModel.PropertyBindingList.Any(record => string.Equals(record.Name, name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"PropertyBinding '{name}' already exists.");
        }

        if (weaveModel.PropertyBindingList.Any(record =>
                string.Equals(record.SourceEntity, sourceEntity, StringComparison.Ordinal) &&
                string.Equals(record.SourceProperty, sourceProperty, StringComparison.Ordinal) &&
                string.Equals(record.TargetEntity, targetEntity, StringComparison.Ordinal) &&
                string.Equals(record.TargetProperty, targetProperty, StringComparison.Ordinal) &&
                ReferenceEquals(record.SourceModel, sourceModel) &&
                ReferenceEquals(record.TargetModel, targetModel)))
        {
            throw new InvalidOperationException("An equivalent PropertyBinding already exists.");
        }

        weaveModel.PropertyBindingList.Add(new WeavePropertyBinding
        {
            Id = AllocateNumericId(weaveModel.PropertyBindingList),
            Name = name,
            SourceEntity = sourceEntity,
            SourceProperty = sourceProperty,
            TargetEntity = targetEntity,
            TargetProperty = targetProperty,
            SourceModel = sourceModel,
            TargetModel = targetModel
        });
    }

    private static void RequireNonEmpty(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"'{name}' must not be empty.", name);
        }
    }

    private static string AllocateNumericId<T>(IReadOnlyCollection<T> records)
    {
        var next = records
            .Select(record => record switch
            {
                WeaveModelReference modelReference => ParseId(modelReference.Id),
                WeavePropertyBinding propertyBinding => ParseId(propertyBinding.Id),
                _ => 0
            })
            .DefaultIfEmpty(0)
            .Max() + 1;
        return next.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int ParseId(string id) =>
        int.TryParse(id, out var parsed) ? parsed : 0;

    private static string NormalizeWorkspacePathForStorage(string weaveWorkspaceRootPath, string workspacePath)
    {
        var absoluteTargetPath = Path.GetFullPath(workspacePath);
        var absoluteWeaveRootPath = Path.GetFullPath(weaveWorkspaceRootPath);
        var relativePath = Path.GetRelativePath(absoluteWeaveRootPath, absoluteTargetPath);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return ".";
        }

        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }

    private async Task<Workspace> LoadReferencedWorkspaceAsync(
        string weaveWorkspaceRootPath,
        WeaveModelReference modelReference,
        CancellationToken cancellationToken)
    {
        var configuredPath = RequireValue(modelReference.WorkspacePath, $"ModelReference '{modelReference.Id}' WorkspacePath");
        var expectedModelName = RequireValue(modelReference.ModelName, $"ModelReference '{modelReference.Id}' ModelName");
        var resolvedPath = ResolveWorkspacePath(weaveWorkspaceRootPath, configuredPath);
        var referencedWorkspace = await _workspaceService.LoadAsync(resolvedPath, searchUpward: false, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(referencedWorkspace.Model.Name, expectedModelName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"ModelReference alias '{RequireValue(modelReference.Alias, $"ModelReference '{modelReference.Id}' Alias")}' expected model '{expectedModelName}' but workspace '{resolvedPath}' contained '{referencedWorkspace.Model.Name}'.");
        }

        return referencedWorkspace;
    }

    private static void ValidateBindingEndpoint(Workspace workspace, string entityName, string propertyName, bool allowId, string bindingSide)
    {
        var entity = workspace.Model.FindEntity(entityName)
            ?? throw new InvalidOperationException(
                $"PropertyBinding {bindingSide} entity '{entityName}' was not found in model '{workspace.Model.Name}'.");

        if (allowId && string.Equals(propertyName, "Id", StringComparison.Ordinal))
        {
            return;
        }

        if (!entity.Properties.Any(property => string.Equals(property.Name, propertyName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"PropertyBinding {bindingSide} property '{entityName}.{propertyName}' was not found in model '{workspace.Model.Name}'.");
        }
    }

    private static string ResolveWorkspacePath(string weaveWorkspaceRootPath, string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.GetFullPath(Path.Combine(weaveWorkspaceRootPath, configuredPath));
    }

    private static string RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required.");
        }

        return value;
    }
}
