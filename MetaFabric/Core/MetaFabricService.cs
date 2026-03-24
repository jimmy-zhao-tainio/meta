using Meta.Core.Domain;
using Meta.Core.Services;
using MetaWeave.Core;

namespace MetaFabric.Core;

public sealed record FabricBindingResult(
    string BindingId,
    string BindingName,
    int SourceRows,
    int ResolvedRows,
    IReadOnlyList<string> Errors);

public sealed record FabricCheckResult(
    IReadOnlyList<FabricBindingResult> Bindings,
    int WeaveCount)
{
    public bool HasErrors => Bindings.Any(binding => binding.Errors.Count > 0);
    public int BindingCount => Bindings.Count;
    public int ErrorCount => Bindings.Sum(binding => binding.Errors.Count);
    public int ResolvedRowCount => Bindings.Sum(binding => binding.ResolvedRows);
    public int SourceRowCount => Bindings.Sum(binding => binding.SourceRows);
}

public interface IMetaFabricService
{
    Task<FabricCheckResult> CheckAsync(Workspace fabricWorkspace, CancellationToken cancellationToken = default);
}

public sealed class MetaFabricService : IMetaFabricService
{
    private readonly IWorkspaceService _workspaceService;

    public MetaFabricService()
        : this(new WorkspaceService())
    {
    }

    public MetaFabricService(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
    }

    public async Task<FabricCheckResult> CheckAsync(Workspace fabricWorkspace, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(fabricWorkspace);

        var weaveReferences = fabricWorkspace.Instance.GetOrCreateEntityRecords("WeaveReference")
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();
        var bindingReferences = fabricWorkspace.Instance.GetOrCreateEntityRecords("BindingReference")
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();
        var scopeRequirements = fabricWorkspace.Instance.GetOrCreateEntityRecords("BindingScopeRequirement")
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();
        var pathSteps = fabricWorkspace.Instance.GetOrCreateEntityRecords("BindingScopePathStep")
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();

        var loadedWeaves = await LoadWeavesAsync(fabricWorkspace, weaveReferences, cancellationToken).ConfigureAwait(false);
        var bindingDefinitions = BuildBindingDefinitions(bindingReferences, scopeRequirements, pathSteps, loadedWeaves);
        var orderedBindings = TopologicallyOrderBindings(bindingDefinitions);

        var resolvedBindings = new Dictionary<string, FabricResolvedBinding>(StringComparer.Ordinal);
        var results = new List<FabricBindingResult>();
        foreach (var binding in orderedBindings)
        {
            var evaluation = EvaluateBinding(binding, resolvedBindings);
            results.Add(evaluation.Result);
            resolvedBindings[binding.ReferenceId] = evaluation.ResolvedBinding;
        }

        return new FabricCheckResult(results, weaveReferences.Count);
    }

    private FabricBindingEvaluation EvaluateBinding(
        FabricBindingDefinition binding,
        IReadOnlyDictionary<string, FabricResolvedBinding> resolvedBindings)
    {
        var targetRows = binding.TargetWorkspace.Instance.GetOrCreateEntityRecords(binding.TargetEntityName);
        var targetIndex = BuildTargetIndex(targetRows, binding.TargetPropertyName, binding.ReferenceName, binding.TargetEntityName);
        var sourceRows = binding.SourceWorkspace.Instance.GetOrCreateEntityRecords(binding.SourceEntityName)
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();
        var errors = new List<string>();
        var sourceToTarget = new Dictionary<string, string>(StringComparer.Ordinal);
        var resolvedRows = 0;

        foreach (var sourceRow in sourceRows)
        {
            if (!TryGetReferenceValue(sourceRow, binding.SourcePropertyName, out var sourceValue))
            {
                errors.Add($"Source row '{binding.SourceEntityName}:{sourceRow.Id}' is missing '{binding.SourcePropertyName}'.");
                continue;
            }

            if (!targetIndex.TryGetValue(sourceValue, out var candidates))
            {
                errors.Add($"Source row '{binding.SourceEntityName}:{sourceRow.Id}' value '{sourceValue}' did not resolve to '{binding.TargetEntityName}.{binding.TargetPropertyName}'.");
                continue;
            }

            var scopedCandidates = candidates;
            var scopeFailure = false;
            foreach (var scopeRequirement in binding.ScopeRequirements)
            {
                if (!resolvedBindings.TryGetValue(scopeRequirement.ParentBinding.ReferenceId, out var parentResolution))
                {
                    throw new InvalidOperationException(
                        $"Fabric binding '{binding.ReferenceName}' depends on unresolved parent binding '{scopeRequirement.ParentBinding.ReferenceName}'.");
                }

                if (!MetaFabricPathing.TryResolvePath(binding.SourceWorkspace, binding.SourceEntityName, sourceRow, scopeRequirement.SourcePathSteps, out var sourcePath, out var sourceError))
                {
                    errors.Add($"Source row '{binding.SourceEntityName}:{sourceRow.Id}' {sourceError}");
                    scopeFailure = true;
                    break;
                }

                if (!string.Equals(sourcePath.EntityName, scopeRequirement.ParentBinding.SourceEntityName, StringComparison.Ordinal))
                {
                    errors.Add($"Source row '{binding.SourceEntityName}:{sourceRow.Id}' source path for scope '{scopeRequirement.RequirementId}' terminates at '{sourcePath.EntityName}', expected '{scopeRequirement.ParentBinding.SourceEntityName}'.");
                    scopeFailure = true;
                    break;
                }

                var sourceParentKey = BuildRowKey(scopeRequirement.ParentBinding.SourceWorkspace, sourcePath.EntityName, sourcePath.RowId);
                if (!parentResolution.SourceToTargetRowKey.TryGetValue(sourceParentKey, out var expectedTargetParentKey))
                {
                    errors.Add(
                        $"Source row '{binding.SourceEntityName}:{sourceRow.Id}' scope source path '{MetaFabricPathing.SerializePath(scopeRequirement.SourcePathSteps)}' is not resolved by parent binding '{scopeRequirement.ParentBinding.ReferenceName}'.");
                    scopeFailure = true;
                    break;
                }

                scopedCandidates = scopedCandidates
                    .Where(candidate => CandidateMatchesScope(candidate, binding, scopeRequirement, expectedTargetParentKey, out _))
                    .ToList();
            }

            if (scopeFailure)
            {
                continue;
            }

            if (scopedCandidates.Count == 0)
            {
                errors.Add(
                    $"Source row '{binding.SourceEntityName}:{sourceRow.Id}' value '{sourceValue}' did not resolve uniquely to '{binding.TargetEntityName}.{binding.TargetPropertyName}' within fabric scope.");
                continue;
            }

            if (scopedCandidates.Count > 1)
            {
                errors.Add(
                    $"Source row '{binding.SourceEntityName}:{sourceRow.Id}' value '{sourceValue}' resolved ambiguously to '{binding.TargetEntityName}.{binding.TargetPropertyName}' within fabric scope.");
                continue;
            }

            var sourceRowKey = BuildRowKey(binding.SourceWorkspace, binding.SourceEntityName, sourceRow.Id);
            var targetRowKey = BuildRowKey(binding.TargetWorkspace, binding.TargetEntityName, scopedCandidates[0].Id);
            sourceToTarget[sourceRowKey] = targetRowKey;
            resolvedRows++;
        }

        return new FabricBindingEvaluation(
            new FabricBindingResult(binding.ReferenceId, binding.ReferenceName, sourceRows.Count, resolvedRows, errors),
            new FabricResolvedBinding(binding, sourceToTarget));
    }

    private async Task<Dictionary<string, LoadedWeaveReference>> LoadWeavesAsync(
        Workspace fabricWorkspace,
        IReadOnlyCollection<GenericRecord> weaveReferences,
        CancellationToken cancellationToken)
    {
        var loadedWeaves = new Dictionary<string, LoadedWeaveReference>(StringComparer.Ordinal);
        var loadedModelWorkspaces = new Dictionary<string, Workspace>(StringComparer.Ordinal);

        foreach (var weaveReference in weaveReferences)
        {
            var configuredPath = RequireValue(weaveReference, "WorkspacePath");
            var resolvedPath = ResolveWorkspacePath(fabricWorkspace.WorkspaceRootPath, configuredPath);
            var weaveWorkspace = await _workspaceService.LoadAsync(resolvedPath, searchUpward: false, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(weaveWorkspace.Model.Name, MetaWeaveModels.MetaWeaveModelName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"WeaveReference '{weaveReference.Id}' expected model '{MetaWeaveModels.MetaWeaveModelName}' but workspace '{resolvedPath}' contained '{weaveWorkspace.Model.Name}'.");
            }

            var weaveModelReferences = weaveWorkspace.Instance.GetOrCreateEntityRecords("ModelReference")
                .OrderBy(record => record.Id, StringComparer.Ordinal)
                .ToList();
            var referencedWorkspaces = new Dictionary<string, Workspace>(StringComparer.Ordinal);
            foreach (var weaveModelReference in weaveModelReferences)
            {
                var modelPath = ResolveWorkspacePath(weaveWorkspace.WorkspaceRootPath, RequireValue(weaveModelReference, "WorkspacePath"));
                if (!loadedModelWorkspaces.TryGetValue(modelPath, out var loadedModelWorkspace))
                {
                    loadedModelWorkspace = await _workspaceService.LoadAsync(modelPath, searchUpward: false, cancellationToken).ConfigureAwait(false);
                    loadedModelWorkspaces[modelPath] = loadedModelWorkspace;
                }

                var expectedModelName = RequireValue(weaveModelReference, "ModelName");
                if (!string.Equals(loadedModelWorkspace.Model.Name, expectedModelName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"ModelReference '{weaveModelReference.Id}' expected model '{expectedModelName}' but workspace '{modelPath}' contained '{loadedModelWorkspace.Model.Name}'.");
                }

                referencedWorkspaces[weaveModelReference.Id] = loadedModelWorkspace;
            }

            loadedWeaves[weaveReference.Id] = new LoadedWeaveReference(
                weaveReference.Id,
                RequireValue(weaveReference, "Alias"),
                resolvedPath,
                weaveWorkspace,
                weaveModelReferences.ToDictionary(record => record.Id, StringComparer.Ordinal),
                referencedWorkspaces);
        }

        return loadedWeaves;
    }

    private static Dictionary<string, FabricBindingDefinition> BuildBindingDefinitions(
        IReadOnlyCollection<GenericRecord> bindingReferences,
        IReadOnlyCollection<GenericRecord> scopeRequirements,
        IReadOnlyCollection<GenericRecord> pathSteps,
        IReadOnlyDictionary<string, LoadedWeaveReference> loadedWeaves)
    {
        var pathStepsByRequirementId = pathSteps
            .GroupBy(record => RequireRelationshipId(record, "BindingScopeRequirementId"), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(ParseRequiredOrdinal).ToList(), StringComparer.Ordinal);
        var scopeRequirementsByBindingId = scopeRequirements
            .GroupBy(record => RequireRelationshipId(record, "BindingId"), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(record => record.Id, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        var bindingDefinitions = new Dictionary<string, FabricBindingDefinition>(StringComparer.Ordinal);
        foreach (var bindingReference in bindingReferences)
        {
            var weaveReferenceId = RequireRelationshipId(bindingReference, "WeaveReferenceId");
            if (!loadedWeaves.TryGetValue(weaveReferenceId, out var loadedWeave))
            {
                throw new InvalidOperationException(
                    $"BindingReference '{bindingReference.Id}' references missing weave '{weaveReferenceId}'.");
            }

            var bindingName = RequireValue(bindingReference, "BindingName");
            var weaveBindings = loadedWeave.WeaveWorkspace.Instance.GetOrCreateEntityRecords("PropertyBinding")
                .Where(record =>
                    record.Values.TryGetValue("Name", out var value) &&
                    string.Equals(value, bindingName, StringComparison.Ordinal))
                .ToList();
            if (weaveBindings.Count == 0)
            {
                throw new InvalidOperationException(
                    $"BindingReference '{bindingReference.Id}' references missing weave binding '{bindingName}' in weave '{loadedWeave.Alias}'.");
            }

            if (weaveBindings.Count > 1)
            {
                throw new InvalidOperationException(
                    $"BindingReference '{bindingReference.Id}' matches multiple weave bindings named '{bindingName}' in weave '{loadedWeave.Alias}'.");
            }

            var weaveBinding = weaveBindings[0];
            var sourceModelId = RequireRelationshipId(weaveBinding, "SourceModelId");
            var targetModelId = RequireRelationshipId(weaveBinding, "TargetModelId");
            if (!loadedWeave.WeaveModelReferencesById.TryGetValue(sourceModelId, out var sourceModelReference))
            {
                throw new InvalidOperationException(
                    $"Weave binding '{bindingName}' references missing source model '{sourceModelId}'.");
            }

            if (!loadedWeave.WeaveModelReferencesById.TryGetValue(targetModelId, out var targetModelReference))
            {
                throw new InvalidOperationException(
                    $"Weave binding '{bindingName}' references missing target model '{targetModelId}'.");
            }

            var sourceWorkspace = loadedWeave.ReferencedWorkspacesById[sourceModelId];
            var targetWorkspace = loadedWeave.ReferencedWorkspacesById[targetModelId];
            var sourceEntityName = RequireValue(weaveBinding, "SourceEntity");
            var targetEntityName = RequireValue(weaveBinding, "TargetEntity");
            var sourcePropertyName = RequireValue(weaveBinding, "SourceProperty");
            var targetPropertyName = RequireValue(weaveBinding, "TargetProperty");

            ValidateBindingEndpoints(bindingReference.Id, sourceWorkspace, sourceEntityName, sourcePropertyName, targetWorkspace, targetEntityName, targetPropertyName);

            bindingDefinitions[bindingReference.Id] = new FabricBindingDefinition(
                ReferenceId: bindingReference.Id,
                ReferenceName: RequireValue(bindingReference, "Name"),
                BindingName: bindingName,
                WeaveAlias: loadedWeave.Alias,
                WeaveWorkspace: loadedWeave.WeaveWorkspace,
                SourceWorkspace: sourceWorkspace,
                SourceModelAlias: RequireValue(sourceModelReference, "Alias"),
                SourceEntityName: sourceEntityName,
                SourcePropertyName: sourcePropertyName,
                TargetWorkspace: targetWorkspace,
                TargetModelAlias: RequireValue(targetModelReference, "Alias"),
                TargetEntityName: targetEntityName,
                TargetPropertyName: targetPropertyName,
                ScopeRequirements: Array.Empty<FabricScopeRequirementDefinition>());
        }

        foreach (var bindingId in scopeRequirementsByBindingId.Keys)
        {
            if (!bindingDefinitions.TryGetValue(bindingId, out var binding))
            {
                throw new InvalidOperationException(
                    $"BindingScopeRequirement references missing binding '{bindingId}'.");
            }

            var requirements = scopeRequirementsByBindingId[bindingId]
                .Select(record =>
                {
                    var parentBindingId = RequireRelationshipId(record, "ParentBindingId");
                    if (!bindingDefinitions.TryGetValue(parentBindingId, out var parentBinding))
                    {
                        throw new InvalidOperationException(
                            $"BindingScopeRequirement '{record.Id}' references missing parent binding '{parentBindingId}'.");
                    }

                    var scopedPathSteps = pathStepsByRequirementId.TryGetValue(record.Id, out var definedSteps)
                        ? definedSteps
                        : new List<GenericRecord>();
                    var sourcePathSteps = scopedPathSteps
                        .Where(item => string.Equals(RequireValue(item, "Side"), "Source", StringComparison.Ordinal))
                        .Select(item => new FabricScopePathStepDefinition(RequireValue(item, "ReferenceName"), ParseRequiredOrdinal(item)))
                        .OrderBy(item => item.Ordinal)
                        .ToArray();
                    var targetPathSteps = scopedPathSteps
                        .Where(item => string.Equals(RequireValue(item, "Side"), "Target", StringComparison.Ordinal))
                        .Select(item => new FabricScopePathStepDefinition(RequireValue(item, "ReferenceName"), ParseRequiredOrdinal(item)))
                        .OrderBy(item => item.Ordinal)
                        .ToArray();
                    if (sourcePathSteps.Length == 0)
                    {
                        throw new InvalidOperationException($"BindingScopeRequirement '{record.Id}' is missing source path steps.");
                    }

                    if (targetPathSteps.Length == 0)
                    {
                        throw new InvalidOperationException($"BindingScopeRequirement '{record.Id}' is missing target path steps.");
                    }

                    MetaFabricPathing.ValidatePath(binding.SourceWorkspace.Model, binding.SourceEntityName, sourcePathSteps, parentBinding.SourceEntityName, $"BindingScopeRequirement '{record.Id}' source path");
                    MetaFabricPathing.ValidatePath(binding.TargetWorkspace.Model, binding.TargetEntityName, targetPathSteps, parentBinding.TargetEntityName, $"BindingScopeRequirement '{record.Id}' target path");

                    return new FabricScopeRequirementDefinition(
                        record.Id,
                        parentBinding,
                        sourcePathSteps,
                        targetPathSteps);
                })
                .ToArray();
            bindingDefinitions[bindingId] = binding with { ScopeRequirements = requirements };
        }

        return bindingDefinitions;
    }

    private static void ValidateBindingEndpoints(
        string bindingReferenceId,
        Workspace sourceWorkspace,
        string sourceEntityName,
        string sourcePropertyName,
        Workspace targetWorkspace,
        string targetEntityName,
        string targetPropertyName)
    {
        var sourceEntity = sourceWorkspace.Model.FindEntity(sourceEntityName)
            ?? throw new InvalidOperationException(
                $"BindingReference '{bindingReferenceId}' source entity '{sourceEntityName}' was not found in model '{sourceWorkspace.Model.Name}'.");
        if (!sourceEntity.Properties.Any(property => string.Equals(property.Name, sourcePropertyName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"BindingReference '{bindingReferenceId}' source property '{sourceEntityName}.{sourcePropertyName}' was not found in model '{sourceWorkspace.Model.Name}'.");
        }

        var targetEntity = targetWorkspace.Model.FindEntity(targetEntityName)
            ?? throw new InvalidOperationException(
                $"BindingReference '{bindingReferenceId}' target entity '{targetEntityName}' was not found in model '{targetWorkspace.Model.Name}'.");
        if (!string.Equals(targetPropertyName, "Id", StringComparison.Ordinal) &&
            !targetEntity.Properties.Any(property => string.Equals(property.Name, targetPropertyName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"BindingReference '{bindingReferenceId}' target property '{targetEntityName}.{targetPropertyName}' was not found in model '{targetWorkspace.Model.Name}'.");
        }
    }

    private static List<FabricBindingDefinition> TopologicallyOrderBindings(
        IReadOnlyDictionary<string, FabricBindingDefinition> bindingDefinitions)
    {
        var ordered = new List<FabricBindingDefinition>();
        var stateById = new Dictionary<string, VisitState>(StringComparer.Ordinal);

        foreach (var bindingId in bindingDefinitions.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            Visit(bindingDefinitions[bindingId]);
        }

        return ordered;

        void Visit(FabricBindingDefinition binding)
        {
            if (stateById.TryGetValue(binding.ReferenceId, out var state))
            {
                if (state == VisitState.Visiting)
                {
                    throw new InvalidOperationException(
                        $"Fabric scope graph contains a cycle at binding '{binding.ReferenceName}'.");
                }

                if (state == VisitState.Visited)
                {
                    return;
                }
            }

            stateById[binding.ReferenceId] = VisitState.Visiting;
            foreach (var requirement in binding.ScopeRequirements.OrderBy(item => item.ParentBinding.ReferenceId, StringComparer.Ordinal))
            {
                Visit(requirement.ParentBinding);
            }

            stateById[binding.ReferenceId] = VisitState.Visited;
            ordered.Add(binding);
        }
    }

    private static Dictionary<string, List<GenericRecord>> BuildTargetIndex(
        IReadOnlyCollection<GenericRecord> targetRows,
        string targetPropertyName,
        string bindingName,
        string targetEntityName)
    {
        var index = new Dictionary<string, List<GenericRecord>>(StringComparer.Ordinal);
        foreach (var row in targetRows)
        {
            if (!TryGetReferenceValue(row, targetPropertyName, out var key))
            {
                throw new InvalidOperationException(
                    $"Binding '{bindingName}' target row '{targetEntityName}:{row.Id}' is missing '{targetPropertyName}'.");
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

    private static bool CandidateMatchesScope(
        GenericRecord candidate,
        FabricBindingDefinition binding,
        FabricScopeRequirementDefinition scopeRequirement,
        string expectedTargetParentKey,
        out string? error)
    {
        if (!MetaFabricPathing.TryResolvePath(binding.TargetWorkspace, binding.TargetEntityName, candidate, scopeRequirement.TargetPathSteps, out var targetPath, out var targetError))
        {
            error = targetError;
            return false;
        }

        if (!string.Equals(targetPath.EntityName, scopeRequirement.ParentBinding.TargetEntityName, StringComparison.Ordinal))
        {
            error = $"Target path for scope '{scopeRequirement.RequirementId}' terminates at '{targetPath.EntityName}', expected '{scopeRequirement.ParentBinding.TargetEntityName}'.";
            return false;
        }

        var candidateTargetParentKey = BuildRowKey(scopeRequirement.ParentBinding.TargetWorkspace, targetPath.EntityName, targetPath.RowId);
        error = null;
        return string.Equals(candidateTargetParentKey, expectedTargetParentKey, StringComparison.Ordinal);
    }

    private static string BuildRowKey(Workspace workspace, string entityName, string rowId)
    {
        return string.Concat(workspace.WorkspaceRootPath, "::", entityName, "::", rowId);
    }

    private static bool TryGetReferenceValue(GenericRecord record, string referenceName, out string value)
    {
        if (string.Equals(referenceName, "Id", StringComparison.Ordinal))
        {
            value = record.Id;
            return !string.IsNullOrWhiteSpace(value);
        }

        if (record.Values.TryGetValue(referenceName, out value!) && !string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (record.RelationshipIds.TryGetValue(referenceName, out value!) && !string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string ResolveWorkspacePath(string? fabricWorkspaceRootPath, string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.GetFullPath(Path.Combine(fabricWorkspaceRootPath ?? string.Empty, configuredPath));
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

    private static int ParseRequiredOrdinal(GenericRecord record)
    {
        if (!int.TryParse(RequireValue(record, "Ordinal"), out var ordinal))
        {
            throw new InvalidOperationException($"Record '{record.Id}' has invalid Ordinal value.");
        }

        return ordinal;
    }

    private sealed record LoadedWeaveReference(
        string ReferenceId,
        string Alias,
        string WorkspacePath,
        Workspace WeaveWorkspace,
        IReadOnlyDictionary<string, GenericRecord> WeaveModelReferencesById,
        IReadOnlyDictionary<string, Workspace> ReferencedWorkspacesById);

    private sealed record FabricResolvedBinding(
        FabricBindingDefinition Binding,
        IReadOnlyDictionary<string, string> SourceToTargetRowKey);

    private sealed record FabricBindingEvaluation(
        FabricBindingResult Result,
        FabricResolvedBinding ResolvedBinding);

    private sealed record FabricBindingDefinition(
        string ReferenceId,
        string ReferenceName,
        string BindingName,
        string WeaveAlias,
        Workspace WeaveWorkspace,
        Workspace SourceWorkspace,
        string SourceModelAlias,
        string SourceEntityName,
        string SourcePropertyName,
        Workspace TargetWorkspace,
        string TargetModelAlias,
        string TargetEntityName,
        string TargetPropertyName,
        IReadOnlyList<FabricScopeRequirementDefinition> ScopeRequirements);

    private sealed record FabricScopeRequirementDefinition(
        string RequirementId,
        FabricBindingDefinition ParentBinding,
        IReadOnlyList<FabricScopePathStepDefinition> SourcePathSteps,
        IReadOnlyList<FabricScopePathStepDefinition> TargetPathSteps);

    private enum VisitState
    {
        Visiting,
        Visited,
    }
}
