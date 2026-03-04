using Meta.Core.Domain;
using Meta.Core.Services;

namespace MetaWeave.Core;

public interface IMetaWeaveAuthoringService
{
    Task AddModelReferenceAsync(Workspace weaveWorkspace, string alias, string modelName, string workspacePath, CancellationToken cancellationToken = default);
    Task AddPropertyBindingAsync(
        Workspace weaveWorkspace,
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

    public async Task AddModelReferenceAsync(Workspace weaveWorkspace, string alias, string modelName, string workspacePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(weaveWorkspace);
        RequireNonEmpty(alias, nameof(alias));
        RequireNonEmpty(modelName, nameof(modelName));
        RequireNonEmpty(workspacePath, nameof(workspacePath));
        if (string.IsNullOrWhiteSpace(weaveWorkspace.WorkspaceRootPath))
        {
            throw new InvalidOperationException("Weave workspace root path is required.");
        }

        var resolvedWorkspacePath = Path.GetFullPath(workspacePath);
        var referencedWorkspace = await _workspaceService.LoadAsync(resolvedWorkspacePath, searchUpward: false, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(referencedWorkspace.Model.Name, modelName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Referenced workspace '{resolvedWorkspacePath}' contained model '{referencedWorkspace.Model.Name}', not '{modelName}'.");
        }

        var records = weaveWorkspace.Instance.GetOrCreateEntityRecords("ModelReference");
        if (records.Any(record => string.Equals(GetRequiredValue(record, "Alias"), alias, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"ModelReference alias '{alias}' already exists.");
        }

        var normalizedWorkspacePath = NormalizeWorkspacePathForStorage(
            weaveWorkspace.WorkspaceRootPath,
            resolvedWorkspacePath);

        records.Add(new GenericRecord
        {
            Id = AllocateNumericId(records),
            Values =
            {
                ["Alias"] = alias,
                ["ModelName"] = modelName,
                ["WorkspacePath"] = normalizedWorkspacePath
            }
        });
    }

    public async Task AddPropertyBindingAsync(
        Workspace weaveWorkspace,
        string name,
        string sourceModelAlias,
        string sourceEntity,
        string sourceProperty,
        string targetModelAlias,
        string targetEntity,
        string targetProperty,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(weaveWorkspace);
        RequireNonEmpty(name, nameof(name));
        RequireNonEmpty(sourceModelAlias, nameof(sourceModelAlias));
        RequireNonEmpty(sourceEntity, nameof(sourceEntity));
        RequireNonEmpty(sourceProperty, nameof(sourceProperty));
        RequireNonEmpty(targetModelAlias, nameof(targetModelAlias));
        RequireNonEmpty(targetEntity, nameof(targetEntity));
        RequireNonEmpty(targetProperty, nameof(targetProperty));

        var modelReferences = weaveWorkspace.Instance.GetOrCreateEntityRecords("ModelReference");
        var sourceModel = modelReferences.SingleOrDefault(record => string.Equals(GetRequiredValue(record, "Alias"), sourceModelAlias, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"ModelReference alias '{sourceModelAlias}' was not found.");
        var targetModel = modelReferences.SingleOrDefault(record => string.Equals(GetRequiredValue(record, "Alias"), targetModelAlias, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"ModelReference alias '{targetModelAlias}' was not found.");

        if (string.IsNullOrWhiteSpace(weaveWorkspace.WorkspaceRootPath))
        {
            throw new InvalidOperationException("Weave workspace root path is required.");
        }

        var sourceWorkspace = await LoadReferencedWorkspaceAsync(weaveWorkspace, sourceModel, cancellationToken).ConfigureAwait(false);
        var targetWorkspace = await LoadReferencedWorkspaceAsync(weaveWorkspace, targetModel, cancellationToken).ConfigureAwait(false);
        ValidateBindingEndpoint(sourceWorkspace, sourceEntity, sourceProperty, allowId: false, bindingSide: "source");
        ValidateBindingEndpoint(targetWorkspace, targetEntity, targetProperty, allowId: true, bindingSide: "target");

        var bindings = weaveWorkspace.Instance.GetOrCreateEntityRecords("PropertyBinding");
        if (bindings.Any(record => string.Equals(GetRequiredValue(record, "Name"), name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"PropertyBinding '{name}' already exists.");
        }

        if (bindings.Any(record =>
                string.Equals(GetRequiredValue(record, "SourceEntity"), sourceEntity, StringComparison.Ordinal) &&
                string.Equals(GetRequiredValue(record, "SourceProperty"), sourceProperty, StringComparison.Ordinal) &&
                string.Equals(GetRequiredValue(record, "TargetEntity"), targetEntity, StringComparison.Ordinal) &&
                string.Equals(GetRequiredValue(record, "TargetProperty"), targetProperty, StringComparison.Ordinal) &&
                string.Equals(record.RelationshipIds["SourceModelId"], sourceModel.Id, StringComparison.Ordinal) &&
                string.Equals(record.RelationshipIds["TargetModelId"], targetModel.Id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("An equivalent PropertyBinding already exists.");
        }

        bindings.Add(new GenericRecord
        {
            Id = AllocateNumericId(bindings),
            Values =
            {
                ["Name"] = name,
                ["SourceEntity"] = sourceEntity,
                ["SourceProperty"] = sourceProperty,
                ["TargetEntity"] = targetEntity,
                ["TargetProperty"] = targetProperty
            },
            RelationshipIds =
            {
                ["SourceModelId"] = sourceModel.Id,
                ["TargetModelId"] = targetModel.Id
            }
        });
    }

    private static void RequireNonEmpty(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"'{name}' must not be empty.", name);
        }
    }

    private static string GetRequiredValue(GenericRecord record, string propertyName)
    {
        if (!record.Values.TryGetValue(propertyName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Record '{record.Id}' is missing required property '{propertyName}'.");
        }

        return value;
    }

    private static string AllocateNumericId(IReadOnlyCollection<GenericRecord> records)
    {
        var next = records
            .Select(record => int.TryParse(record.Id, out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return next.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

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

    private async Task<Workspace> LoadReferencedWorkspaceAsync(Workspace weaveWorkspace, GenericRecord modelReference, CancellationToken cancellationToken)
    {
        var configuredPath = GetRequiredValue(modelReference, "WorkspacePath");
        var expectedModelName = GetRequiredValue(modelReference, "ModelName");
        var resolvedPath = ResolveWorkspacePath(weaveWorkspace.WorkspaceRootPath!, configuredPath);
        var referencedWorkspace = await _workspaceService.LoadAsync(resolvedPath, searchUpward: false, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(referencedWorkspace.Model.Name, expectedModelName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"ModelReference alias '{GetRequiredValue(modelReference, "Alias")}' expected model '{expectedModelName}' but workspace '{resolvedPath}' contained '{referencedWorkspace.Model.Name}'.");
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
}
