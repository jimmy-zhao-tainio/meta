using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Serialization;
using Meta.Core.Services;
using MetaWeaveModel = global::MetaWeave.MetaWeaveModel;
using WeaveModelReference = global::MetaWeave.ModelReference;
using WeavePropertyBinding = global::MetaWeave.PropertyBinding;

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

public sealed record WeaveMaterializationResult(
    InMemoryWorkspace Workspace,
    int BindingsMaterialized);

public interface IMetaWeaveService
{
    Task<WeaveCheckResult> CheckAsync(
        MetaWeaveModel weaveModel,
        string weaveWorkspaceRootPath,
        CancellationToken cancellationToken = default);

    Task<WeaveMaterializationResult> MaterializeAsync(
        MetaWeaveModel weaveModel,
        string weaveWorkspaceRootPath,
        string materializedWorkspaceRootPath,
        string mergedModelName,
        CancellationToken cancellationToken = default);
}

public sealed class MetaWeaveService : IMetaWeaveService
{
    private readonly IWorkspaceMergeService _workspaceMergeService;

    public MetaWeaveService()
        : this(new WorkspaceMergeService())
    {
    }

    public MetaWeaveService(IWorkspaceMergeService workspaceMergeService)
    {
        _workspaceMergeService = workspaceMergeService ?? throw new ArgumentNullException(nameof(workspaceMergeService));
    }

    public async Task<WeaveCheckResult> CheckAsync(
        MetaWeaveModel weaveModel,
        string weaveWorkspaceRootPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(weaveModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(weaveWorkspaceRootPath);

        var loadedModels = await LoadReferencedWorkspacesAsync(
                weaveModel.ModelReferenceList,
                weaveWorkspaceRootPath,
                cancellationToken)
            .ConfigureAwait(false);
        return CheckBindings(weaveModel, loadedModels);
    }

    private static WeaveCheckResult CheckBindings(
        MetaWeaveModel weaveModel,
        IReadOnlyDictionary<string, OpenedXmlWorkspace> loadedModels)
    {
        var modelRefs = weaveModel.ModelReferenceList
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();
        var propertyBindings = weaveModel.PropertyBindingList
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();
        var modelRefById = modelRefs.ToDictionary(record => RequireValue(record.Id, "ModelReference Id"), StringComparer.Ordinal);

        var results = new List<WeaveBindingResult>();
        foreach (var binding in propertyBindings)
        {
            var sourceModelRef = RequireModelReference(binding.SourceModel, binding, "source", modelRefById);
            var targetModelRef = RequireModelReference(binding.TargetModel, binding, "target", modelRefById);
            var sourceWorkspace = loadedModels[sourceModelRef.Id].State;
            var targetWorkspace = loadedModels[targetModelRef.Id].State;
            var sourceEntityName = RequireValue(binding.SourceEntity, $"PropertyBinding '{binding.Id}' SourceEntity");
            var sourcePropertyName = RequireValue(binding.SourceProperty, $"PropertyBinding '{binding.Id}' SourceProperty");
            var targetEntityName = RequireValue(binding.TargetEntity, $"PropertyBinding '{binding.Id}' TargetEntity");
            var targetPropertyName = RequireValue(binding.TargetProperty, $"PropertyBinding '{binding.Id}' TargetProperty");
            var bindingName = RequireValue(binding.Name, $"PropertyBinding '{binding.Id}' Name");

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

            var targetRows = MetaWeaveWorkspaceData.ReadRecords(
                targetWorkspace,
                targetEntityName);
            var targetIndex = BuildTargetIndex(targetRows, targetPropertyName, binding.Id, targetEntityName);
            var sourceRows = MetaWeaveWorkspaceData.ReadRecords(
                    sourceWorkspace,
                    sourceEntityName)
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

    public async Task<WeaveMaterializationResult> MaterializeAsync(
        MetaWeaveModel weaveModel,
        string weaveWorkspaceRootPath,
        string materializedWorkspaceRootPath,
        string mergedModelName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(weaveModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(weaveWorkspaceRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(materializedWorkspaceRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mergedModelName);

        var modelRefs = weaveModel.ModelReferenceList
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();
        var propertyBindings = weaveModel.PropertyBindingList
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();

        var referencedWorkspaces = await LoadReferencedWorkspacesAsync(
                modelRefs,
                weaveWorkspaceRootPath,
                cancellationToken)
            .ConfigureAwait(false);
        var check = CheckBindings(weaveModel, referencedWorkspaces);
        if (check.HasErrors)
        {
            throw new InvalidOperationException("Weave check failed. Run 'meta-weave check' and fix the reported errors before materialize.");
        }

        var mergePlan = await _workspaceMergeService.MergeAsync(
                modelRefs
                    .Select(item => (IMetaWorkspaceSource)new InMemoryWorkspaceSource(
                        referencedWorkspaces[item.Id].State))
                    .ToArray(),
                new WorkspaceMergeOptions(mergedModelName),
                cancellationToken)
            .ConfigureAwait(false);
        var materializedWorkspace = mergePlan.Workspace;

        var modelRefById = modelRefs.ToDictionary(record => RequireValue(record.Id, "ModelReference Id"), StringComparer.Ordinal);
        var operations = new List<Operation>(propertyBindings.Count);
        foreach (var binding in propertyBindings)
        {
            _ = RequireModelReference(binding.SourceModel, binding, "source", modelRefById);
            _ = RequireModelReference(binding.TargetModel, binding, "target", modelRefById);

            var sourceEntity = RequireValue(binding.SourceEntity, $"PropertyBinding '{binding.Id}' SourceEntity");
            var sourceProperty = RequireValue(binding.SourceProperty, $"PropertyBinding '{binding.Id}' SourceProperty");
            var targetEntity = RequireValue(binding.TargetEntity, $"PropertyBinding '{binding.Id}' TargetEntity");
            var targetProperty = RequireValue(binding.TargetProperty, $"PropertyBinding '{binding.Id}' TargetProperty");
            var role = DeriveMaterializedRole(sourceProperty, targetEntity);

            operations.Add(new Operation.PropertyToRelationship(
                sourceEntity,
                sourceProperty,
                targetEntity,
                targetProperty,
                role));
        }

        if (operations.Count > 0)
        {
            var execution = InMemoryOperations.Execute(
                materializedWorkspace,
                operations);
            materializedWorkspace = execution.Workspace;
        }

        var validation = WorkspaceValidator.Validate(
            materializedWorkspace.Model,
            materializedWorkspace.Instance);
        if (validation.HasErrors)
        {
            var message = string.Join(" ", validation.Issues
                .Where(issue => issue.Severity == IssueSeverity.Error)
                .Select(issue => issue.Message));
            throw new InvalidOperationException($"Merged workspace is invalid: {message}");
        }

        await XmlWorkspaceWriter.WriteMergedAsync(
                materializedWorkspace,
                materializedWorkspaceRootPath,
                modelRefs
                    .Select(item => referencedWorkspaces[item.Id])
                    .ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        return new WeaveMaterializationResult(
            materializedWorkspace,
            operations.Count);
    }

    private static async Task<Dictionary<string, OpenedXmlWorkspace>> LoadReferencedWorkspacesAsync(
        IReadOnlyCollection<WeaveModelReference> modelRefs,
        string weaveWorkspaceRootPath,
        CancellationToken cancellationToken)
    {
        var loadedModels = new Dictionary<string, OpenedXmlWorkspace>(StringComparer.Ordinal);
        foreach (var modelRef in modelRefs)
        {
            var id = RequireValue(modelRef.Id, "ModelReference Id");
            var path = RequireValue(modelRef.WorkspacePath, $"ModelReference '{id}' WorkspacePath");
            var resolvedPath = ResolveWorkspacePath(weaveWorkspaceRootPath, path);
            var loaded = await XmlWorkspaceReader.OpenAsync(
                    resolvedPath,
                    cancellationToken)
                .ConfigureAwait(false);
            var expectedModelName = RequireValue(modelRef.ModelName, $"ModelReference '{id}' ModelName");
            if (!string.Equals(loaded.Model.Name, expectedModelName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"ModelReference '{id}' expected model '{expectedModelName}' but workspace '{resolvedPath}' contained '{loaded.Model.Name}'.");
            }

            loadedModels[id] = loaded;
        }

        return loadedModels;
    }

    private static WeaveModelReference RequireModelReference(
        WeaveModelReference? modelReference,
        WeavePropertyBinding binding,
        string role,
        IReadOnlyDictionary<string, WeaveModelReference> modelRefById)
    {
        if (modelReference is null || string.IsNullOrWhiteSpace(modelReference.Id))
        {
            throw new InvalidOperationException($"PropertyBinding '{binding.Id}' references missing {role} model.");
        }

        if (!modelRefById.TryGetValue(modelReference.Id, out var canonicalModelReference) ||
            !ReferenceEquals(canonicalModelReference, modelReference))
        {
            throw new InvalidOperationException($"PropertyBinding '{binding.Id}' references missing {role} model '{modelReference.Id}'.");
        }

        return canonicalModelReference;
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

    private static string RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required.");
        }

        return value;
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
