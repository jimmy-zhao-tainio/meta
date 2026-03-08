using Meta.Core.Domain;
using Meta.Core.Services;
using MetaWeave.Core;

namespace MetaFabric.Core;

public interface IMetaFabricAuthoringService
{
    Task AddWeaveReferenceAsync(Workspace fabricWorkspace, string alias, string workspacePath, CancellationToken cancellationToken = default);
    Task AddBindingReferenceAsync(
        Workspace fabricWorkspace,
        string name,
        string weaveAlias,
        string sourceEntity,
        string sourceProperty,
        string targetEntity,
        string targetProperty,
        CancellationToken cancellationToken = default);
    Task AddScopeRequirementAsync(
        Workspace fabricWorkspace,
        string bindingReferenceName,
        string parentBindingReferenceName,
        string sourceParentReferenceName,
        string targetParentReferenceName,
        CancellationToken cancellationToken = default);
}

public sealed class MetaFabricAuthoringService : IMetaFabricAuthoringService
{
    private readonly IWorkspaceService _workspaceService;

    public MetaFabricAuthoringService()
        : this(new WorkspaceService())
    {
    }

    public MetaFabricAuthoringService(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
    }

    public async Task AddWeaveReferenceAsync(Workspace fabricWorkspace, string alias, string workspacePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fabricWorkspace);
        RequireNonEmpty(alias, nameof(alias));
        RequireNonEmpty(workspacePath, nameof(workspacePath));
        if (string.IsNullOrWhiteSpace(fabricWorkspace.WorkspaceRootPath))
        {
            throw new InvalidOperationException("Fabric workspace root path is required.");
        }

        var resolvedWorkspacePath = Path.GetFullPath(workspacePath);
        var referencedWorkspace = await _workspaceService.LoadAsync(resolvedWorkspacePath, searchUpward: false, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(referencedWorkspace.Model.Name, MetaWeaveModels.MetaWeaveModelName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Referenced workspace '{resolvedWorkspacePath}' contained model '{referencedWorkspace.Model.Name}', not '{MetaWeaveModels.MetaWeaveModelName}'.");
        }

        var records = fabricWorkspace.Instance.GetOrCreateEntityRecords("WeaveReference");
        if (records.Any(record => string.Equals(GetRequiredValue(record, "Alias"), alias, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"WeaveReference alias '{alias}' already exists.");
        }

        var normalizedWorkspacePath = NormalizeWorkspacePathForStorage(
            fabricWorkspace.WorkspaceRootPath,
            resolvedWorkspacePath);

        records.Add(new GenericRecord
        {
            Id = AllocateNumericId(records),
            Values =
            {
                ["Alias"] = alias,
                ["WorkspacePath"] = normalizedWorkspacePath,
            },
        });
    }

    public async Task AddBindingReferenceAsync(
        Workspace fabricWorkspace,
        string name,
        string weaveAlias,
        string sourceEntity,
        string sourceProperty,
        string targetEntity,
        string targetProperty,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fabricWorkspace);
        RequireNonEmpty(name, nameof(name));
        RequireNonEmpty(weaveAlias, nameof(weaveAlias));
        RequireNonEmpty(sourceEntity, nameof(sourceEntity));
        RequireNonEmpty(sourceProperty, nameof(sourceProperty));
        RequireNonEmpty(targetEntity, nameof(targetEntity));
        RequireNonEmpty(targetProperty, nameof(targetProperty));
        if (string.IsNullOrWhiteSpace(fabricWorkspace.WorkspaceRootPath))
        {
            throw new InvalidOperationException("Fabric workspace root path is required.");
        }

        var weaveReferences = fabricWorkspace.Instance.GetOrCreateEntityRecords("WeaveReference");
        var weaveReference = weaveReferences.SingleOrDefault(record => string.Equals(GetRequiredValue(record, "Alias"), weaveAlias, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"WeaveReference alias '{weaveAlias}' was not found.");

        var weaveWorkspace = await LoadReferencedWeaveWorkspaceAsync(fabricWorkspace, weaveReference, cancellationToken).ConfigureAwait(false);
        var matchingWeaveBindings = weaveWorkspace.Instance.GetOrCreateEntityRecords("PropertyBinding")
            .Where(record =>
                string.Equals(GetRequiredValue(record, "SourceEntity"), sourceEntity, StringComparison.Ordinal) &&
                string.Equals(GetRequiredValue(record, "SourceProperty"), sourceProperty, StringComparison.Ordinal) &&
                string.Equals(GetRequiredValue(record, "TargetEntity"), targetEntity, StringComparison.Ordinal) &&
                string.Equals(GetRequiredValue(record, "TargetProperty"), targetProperty, StringComparison.Ordinal))
            .ToList();

        if (matchingWeaveBindings.Count == 0)
        {
            throw new InvalidOperationException(
                $"No PropertyBinding in weave '{weaveAlias}' matched '{sourceEntity}.{sourceProperty} -> {targetEntity}.{targetProperty}'.");
        }

        if (matchingWeaveBindings.Count > 1)
        {
            throw new InvalidOperationException(
                $"Multiple PropertyBindings in weave '{weaveAlias}' matched '{sourceEntity}.{sourceProperty} -> {targetEntity}.{targetProperty}'.");
        }

        var weaveBindingName = GetRequiredValue(matchingWeaveBindings[0], "Name");
        var bindingReferences = fabricWorkspace.Instance.GetOrCreateEntityRecords("BindingReference");
        if (bindingReferences.Any(record => string.Equals(GetRequiredValue(record, "Name"), name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"BindingReference '{name}' already exists.");
        }

        if (bindingReferences.Any(record =>
                string.Equals(GetRequiredValue(record, "BindingName"), weaveBindingName, StringComparison.Ordinal) &&
                string.Equals(GetRequiredRelationshipId(record, "WeaveReferenceId"), weaveReference.Id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("An equivalent BindingReference already exists.");
        }

        bindingReferences.Add(new GenericRecord
        {
            Id = AllocateNumericId(bindingReferences),
            Values =
            {
                ["Name"] = name,
                ["BindingName"] = weaveBindingName,
            },
            RelationshipIds =
            {
                ["WeaveReferenceId"] = weaveReference.Id,
            },
        });
    }

    public Task AddScopeRequirementAsync(
        Workspace fabricWorkspace,
        string bindingReferenceName,
        string parentBindingReferenceName,
        string sourceParentReferenceName,
        string targetParentReferenceName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(fabricWorkspace);
        RequireNonEmpty(bindingReferenceName, nameof(bindingReferenceName));
        RequireNonEmpty(parentBindingReferenceName, nameof(parentBindingReferenceName));
        RequireNonEmpty(sourceParentReferenceName, nameof(sourceParentReferenceName));
        RequireNonEmpty(targetParentReferenceName, nameof(targetParentReferenceName));

        var bindingReferences = fabricWorkspace.Instance.GetOrCreateEntityRecords("BindingReference");
        var bindingReference = bindingReferences.SingleOrDefault(record => string.Equals(GetRequiredValue(record, "Name"), bindingReferenceName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"BindingReference '{bindingReferenceName}' was not found.");
        var parentBindingReference = bindingReferences.SingleOrDefault(record => string.Equals(GetRequiredValue(record, "Name"), parentBindingReferenceName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"BindingReference '{parentBindingReferenceName}' was not found.");

        if (string.Equals(bindingReference.Id, parentBindingReference.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("BindingReference and parent binding must be different.");
        }

        var scopeRequirements = fabricWorkspace.Instance.GetOrCreateEntityRecords("BindingScopeRequirement");
        if (scopeRequirements.Any(record =>
                string.Equals(GetRequiredRelationshipId(record, "BindingId"), bindingReference.Id, StringComparison.Ordinal) &&
                string.Equals(GetRequiredRelationshipId(record, "ParentBindingId"), parentBindingReference.Id, StringComparison.Ordinal) &&
                string.Equals(GetRequiredValue(record, "SourceParentReferenceName"), sourceParentReferenceName, StringComparison.Ordinal) &&
                string.Equals(GetRequiredValue(record, "TargetParentReferenceName"), targetParentReferenceName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("An equivalent BindingScopeRequirement already exists.");
        }

        scopeRequirements.Add(new GenericRecord
        {
            Id = AllocateNumericId(scopeRequirements),
            Values =
            {
                ["SourceParentReferenceName"] = sourceParentReferenceName,
                ["TargetParentReferenceName"] = targetParentReferenceName,
            },
            RelationshipIds =
            {
                ["BindingId"] = bindingReference.Id,
                ["ParentBindingId"] = parentBindingReference.Id,
            },
        });

        return Task.CompletedTask;
    }

    private async Task<Workspace> LoadReferencedWeaveWorkspaceAsync(Workspace fabricWorkspace, GenericRecord weaveReference, CancellationToken cancellationToken)
    {
        var configuredPath = GetRequiredValue(weaveReference, "WorkspacePath");
        var resolvedPath = ResolveWorkspacePath(fabricWorkspace.WorkspaceRootPath!, configuredPath);
        var referencedWorkspace = await _workspaceService.LoadAsync(resolvedPath, searchUpward: false, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(referencedWorkspace.Model.Name, MetaWeaveModels.MetaWeaveModelName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"WeaveReference alias '{GetRequiredValue(weaveReference, "Alias")}' expected model '{MetaWeaveModels.MetaWeaveModelName}' but workspace '{resolvedPath}' contained '{referencedWorkspace.Model.Name}'.");
        }

        return referencedWorkspace;
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

    private static string GetRequiredRelationshipId(GenericRecord record, string relationshipName)
    {
        if (!record.RelationshipIds.TryGetValue(relationshipName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Record '{record.Id}' is missing required relationship '{relationshipName}'.");
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

    private static string NormalizeWorkspacePathForStorage(string fabricWorkspaceRootPath, string workspacePath)
    {
        var absoluteTargetPath = Path.GetFullPath(workspacePath);
        var absoluteFabricRootPath = Path.GetFullPath(fabricWorkspaceRootPath);
        var relativePath = Path.GetRelativePath(absoluteFabricRootPath, absoluteTargetPath);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return ".";
        }

        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string ResolveWorkspacePath(string fabricWorkspaceRootPath, string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.GetFullPath(Path.Combine(fabricWorkspaceRootPath, configuredPath));
    }
}
