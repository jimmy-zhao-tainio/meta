using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Services;
using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace MetaWeave.Core;

public sealed record WeaveBindingResult(
    string BindingId,
    string BindingName,
    int SourceRows,
    int ResolvedRows,
    IReadOnlyList<string> Errors);

public sealed record WeaveCheckResult(
    IReadOnlyList<WeaveBindingResult> Bindings)
{
    public bool HasErrors => Bindings.Any(binding => binding.Errors.Count > 0);
    public int BindingCount => Bindings.Count;
    public int ErrorCount => Bindings.Sum(binding => binding.Errors.Count);
    public int ResolvedRowCount => Bindings.Sum(binding => binding.ResolvedRows);
    public int SourceRowCount => Bindings.Sum(binding => binding.SourceRows);
}

public interface IMetaWeaveService
{
    Task<WeaveCheckResult> CheckAsync(Workspace weaveWorkspace, CancellationToken cancellationToken = default);
    Task<Workspace> MaterializeAsync(Workspace weaveWorkspace, string materializedWorkspaceRootPath, string mergedModelName, CancellationToken cancellationToken = default);
}

public sealed class MetaWeaveService : IMetaWeaveService
{
    private readonly IWorkspaceService _workspaceService;
    private readonly IWorkspaceMergeService _workspaceMergeService;

    public MetaWeaveService()
        : this(new WorkspaceService(), new WorkspaceMergeService())
    {
    }

    public MetaWeaveService(IWorkspaceService workspaceService, IWorkspaceMergeService workspaceMergeService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _workspaceMergeService = workspaceMergeService ?? throw new ArgumentNullException(nameof(workspaceMergeService));
    }

    public async Task<WeaveCheckResult> CheckAsync(Workspace weaveWorkspace, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(weaveWorkspace);

        var modelRefs = weaveWorkspace.Instance.GetOrCreateEntityRecords("ModelReference")
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();
        var propertyBindings = weaveWorkspace.Instance.GetOrCreateEntityRecords("PropertyBinding")
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();

        var loadedModels = new Dictionary<string, Workspace>(StringComparer.Ordinal);
        var modelRefById = modelRefs.ToDictionary(record => record.Id, StringComparer.Ordinal);

        foreach (var modelRef in modelRefs)
        {
            var path = RequireValue(modelRef, "WorkspacePath");
            var resolvedPath = ResolveWorkspacePath(weaveWorkspace.WorkspaceRootPath, path);
            var loaded = await _workspaceService.LoadAsync(resolvedPath, searchUpward: false, cancellationToken).ConfigureAwait(false);
            var expectedModelName = RequireValue(modelRef, "ModelName");
            if (!string.Equals(loaded.Model.Name, expectedModelName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"ModelReference '{modelRef.Id}' expected model '{expectedModelName}' but workspace '{resolvedPath}' contained '{loaded.Model.Name}'.");
            }

            loadedModels[modelRef.Id] = loaded;
        }

        var results = new List<WeaveBindingResult>();
        foreach (var binding in propertyBindings)
        {
            var sourceModelRefId = RequireRelationshipId(binding, "SourceModelId");
            var targetModelRefId = RequireRelationshipId(binding, "TargetModelId");
            if (!modelRefById.ContainsKey(sourceModelRefId))
            {
                throw new InvalidOperationException($"PropertyBinding '{binding.Id}' references missing source model '{sourceModelRefId}'.");
            }

            if (!modelRefById.ContainsKey(targetModelRefId))
            {
                throw new InvalidOperationException($"PropertyBinding '{binding.Id}' references missing target model '{targetModelRefId}'.");
            }

            var sourceWorkspace = loadedModels[sourceModelRefId];
            var targetWorkspace = loadedModels[targetModelRefId];
            var sourceEntityName = RequireValue(binding, "SourceEntity");
            var sourcePropertyName = RequireValue(binding, "SourceProperty");
            var targetEntityName = RequireValue(binding, "TargetEntity");
            var targetPropertyName = RequireValue(binding, "TargetProperty");
            var bindingName = RequireValue(binding, "Name");

            var sourceEntity = sourceWorkspace.Model.FindEntity(sourceEntityName)
                ?? throw new InvalidOperationException($"PropertyBinding '{binding.Id}' source entity '{sourceEntityName}' was not found in model '{sourceWorkspace.Model.Name}'.");
            if (!sourceEntity.Properties.Any(property => string.Equals(property.Name, sourcePropertyName, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"PropertyBinding '{binding.Id}' source property '{sourceEntityName}.{sourcePropertyName}' was not found in model '{sourceWorkspace.Model.Name}'.");
            }

            var targetEntity = targetWorkspace.Model.FindEntity(targetEntityName)
                ?? throw new InvalidOperationException($"PropertyBinding '{binding.Id}' target entity '{targetEntityName}' was not found in model '{targetWorkspace.Model.Name}'.");
            if (!string.Equals(targetPropertyName, "Id", StringComparison.Ordinal) &&
                !targetEntity.Properties.Any(property => string.Equals(property.Name, targetPropertyName, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"PropertyBinding '{binding.Id}' target property '{targetEntityName}.{targetPropertyName}' was not found in model '{targetWorkspace.Model.Name}'.");
            }

            var targetRows = targetWorkspace.Instance.GetOrCreateEntityRecords(targetEntityName);
            var targetIndex = BuildTargetIndex(targetRows, targetPropertyName, binding.Id, targetEntityName);
            var sourceRows = sourceWorkspace.Instance.GetOrCreateEntityRecords(sourceEntityName)
                .OrderBy(record => record.Id, StringComparer.Ordinal)
                .ToList();
            var errors = new List<string>();
            var resolvedRows = 0;
            foreach (var sourceRow in sourceRows)
            {
                if (!sourceRow.Values.TryGetValue(sourcePropertyName, out var sourceValue) || string.IsNullOrWhiteSpace(sourceValue))
                {
                    errors.Add($"Source row '{sourceEntityName}:{sourceRow.Id}' is missing '{sourcePropertyName}'.");
                    continue;
                }

                if (!targetIndex.TryGetValue(sourceValue, out var targetMatches))
                {
                    errors.Add($"Source row '{sourceEntityName}:{sourceRow.Id}' value '{sourceValue}' did not resolve to '{targetEntityName}.{targetPropertyName}'.");
                    continue;
                }

                if (targetMatches.Count != 1)
                {
                    errors.Add($"Source row '{sourceEntityName}:{sourceRow.Id}' value '{sourceValue}' resolved ambiguously to '{targetEntityName}.{targetPropertyName}'.");
                    continue;
                }

                resolvedRows++;
            }

            results.Add(new WeaveBindingResult(binding.Id, bindingName, sourceRows.Count, resolvedRows, errors));
        }

        return new WeaveCheckResult(results);
    }

    public async Task<Workspace> MaterializeAsync(Workspace weaveWorkspace, string materializedWorkspaceRootPath, string mergedModelName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(weaveWorkspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(materializedWorkspaceRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mergedModelName);

        var check = await CheckAsync(weaveWorkspace, cancellationToken).ConfigureAwait(false);
        if (check.HasErrors)
        {
            throw new InvalidOperationException("Weave check failed. Run 'meta-weave check' and fix the reported errors before materialize.");
        }

        var modelRefs = weaveWorkspace.Instance.GetOrCreateEntityRecords("ModelReference")
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();
        var propertyBindings = weaveWorkspace.Instance.GetOrCreateEntityRecords("PropertyBinding")
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();

        var referencedWorkspaces = new Dictionary<string, Workspace>(StringComparer.Ordinal);
        foreach (var modelRef in modelRefs)
        {
            var resolvedPath = ResolveWorkspacePath(weaveWorkspace.WorkspaceRootPath, RequireValue(modelRef, "WorkspacePath"));
            referencedWorkspaces[modelRef.Id] = await _workspaceService.LoadAsync(resolvedPath, searchUpward: false, cancellationToken).ConfigureAwait(false);
        }

        var mergedWorkspace = CreateWorkspaceShell(materializedWorkspaceRootPath, mergedModelName);
        _workspaceMergeService.MergeInto(
            mergedWorkspace,
            modelRefs.Select(item => referencedWorkspaces[item.Id]).ToArray(),
            new WorkspaceMergeOptions(mergedModelName));

        var beforeBindings = WorkspaceSnapshotCloner.Capture(mergedWorkspace);

        var modelRefById = modelRefs.ToDictionary(record => record.Id, StringComparer.Ordinal);
        var refactorService = new ModelRefactorService();
        try
        {
            foreach (var binding in propertyBindings)
            {
                var sourceModelRef = modelRefById[RequireRelationshipId(binding, "SourceModelId")];
                var targetModelRef = modelRefById[RequireRelationshipId(binding, "TargetModelId")];
                _ = sourceModelRef;
                _ = targetModelRef;

                var sourceEntity = RequireValue(binding, "SourceEntity");
                var sourceProperty = RequireValue(binding, "SourceProperty");
                var targetEntity = RequireValue(binding, "TargetEntity");
                var targetProperty = RequireValue(binding, "TargetProperty");
                var role = DeriveMaterializedRole(sourceProperty, targetEntity);

                refactorService.RefactorPropertyToRelationship(
                    mergedWorkspace,
                    new PropertyToRelationshipRefactorOptions(
                        SourceEntityName: sourceEntity,
                        SourcePropertyName: sourceProperty,
                        TargetEntityName: targetEntity,
                        LookupPropertyName: targetProperty,
                        Role: role,
                        DropSourceProperty: true,
                        RequireSourceReuse: false));
            }
        }
        catch
        {
            WorkspaceSnapshotCloner.Restore(mergedWorkspace, beforeBindings);
            throw;
        }

        var validation = new ValidationService().Validate(mergedWorkspace);
        if (validation.HasErrors)
        {
            var message = string.Join(" ", validation.Issues
                .Where(issue => issue.Severity == IssueSeverity.Error)
                .Select(issue => issue.Message));
            throw new InvalidOperationException($"Merged workspace is invalid: {message}");
        }

        mergedWorkspace.IsDirty = true;
        return mergedWorkspace;
    }

    private static Dictionary<string, List<GenericRecord>> BuildTargetIndex(
        IReadOnlyCollection<GenericRecord> targetRows,
        string targetPropertyName,
        string bindingId,
        string targetEntityName)
    {
        var index = new Dictionary<string, List<GenericRecord>>(StringComparer.Ordinal);
        foreach (var row in targetRows)
        {
            string key;
            if (string.Equals(targetPropertyName, "Id", StringComparison.Ordinal))
            {
                key = row.Id;
            }
            else if (!row.Values.TryGetValue(targetPropertyName, out key!) || string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException($"PropertyBinding '{bindingId}' target row '{targetEntityName}:{row.Id}' is missing '{targetPropertyName}'.");
            }

            if (!index.TryGetValue(key, out var matches))
            {
                matches = new List<GenericRecord>();
                index[key] = matches;
            }

            matches.Add(row);
        }

        return index;
    }

    private static string ResolveWorkspacePath(string weaveWorkspaceRootPath, string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.GetFullPath(Path.Combine(weaveWorkspaceRootPath, configuredPath));
    }

    private static string RequireValue(GenericRecord record, string propertyName)
    {
        if (!record.Values.TryGetValue(propertyName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Record '{record.Id}' is missing required property '{propertyName}'.");
        }

        return value;
    }

    private static string RequireRelationshipId(GenericRecord record, string relationshipName)
    {
        if (!record.RelationshipIds.TryGetValue(relationshipName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Record '{record.Id}' is missing required relationship '{relationshipName}'.");
        }

        return value;
    }

    private static Workspace CreateWorkspaceShell(string workspaceRootPath, string mergedModelName)
    {
        var rootPath = Path.GetFullPath(workspaceRootPath);
        return new Workspace
        {
            WorkspaceRootPath = rootPath,
            MetadataRootPath = rootPath,
            WorkspaceConfig = MetaWorkspaceConfig.CreateDefault(),
            Model = new GenericModel { Name = mergedModelName },
            Instance = new GenericInstance { ModelName = mergedModelName },
            IsDirty = true,
        };
    }

    private static string DeriveMaterializedRole(string sourcePropertyName, string targetEntityName)
    {
        var defaultRelationshipColumnName = targetEntityName + "Id";
        if (string.Equals(sourcePropertyName, defaultRelationshipColumnName, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (sourcePropertyName.EndsWith("Id", StringComparison.Ordinal) && sourcePropertyName.Length > 2)
        {
            return sourcePropertyName[..^2];
        }

        throw new InvalidOperationException(
            $"Cannot materialize weave binding for property '{sourcePropertyName}'. A materialized binding must use a source property ending with 'Id'.");
    }
}
