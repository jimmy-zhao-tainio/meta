using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Services;
using MetaWeave.Core;

namespace MetaFabric.Core;

public sealed record FabricScopeSuggestion(
    string ChildBindingReferenceName,
    string ChildBindingName,
    string ParentBindingReferenceName,
    string ParentBindingName,
    string SourceParentReferenceName,
    string TargetParentReferenceName);

public sealed record FabricWeakScopeSuggestion(
    string ChildBindingReferenceName,
    string ChildBindingName,
    IReadOnlyList<FabricScopeSuggestion> Candidates);

public sealed record FabricSuggestResult(
    IReadOnlyList<FabricScopeSuggestion> Suggestions,
    IReadOnlyList<FabricWeakScopeSuggestion> WeakSuggestions)
{
    public int SuggestionCount => Suggestions.Count;
    public int WeakSuggestionCount => WeakSuggestions.Sum(item => item.Candidates.Count);
}

public interface IMetaFabricSuggestService
{
    Task<FabricSuggestResult> SuggestAsync(Workspace fabricWorkspace, CancellationToken cancellationToken = default);
}

public sealed class MetaFabricSuggestService : IMetaFabricSuggestService
{
    private readonly IWorkspaceService _workspaceService;
    private readonly IMetaFabricService _fabricService;

    public MetaFabricSuggestService()
        : this(new WorkspaceService(), null)
    {
    }

    public MetaFabricSuggestService(IWorkspaceService workspaceService, IMetaFabricService? fabricService = null)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _fabricService = fabricService ?? new MetaFabricService(workspaceService);
    }

    public async Task<FabricSuggestResult> SuggestAsync(Workspace fabricWorkspace, CancellationToken cancellationToken = default)
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
        var existingScopesByBindingId = scopeRequirements
            .GroupBy(record => RequireRelationshipId(record, "BindingId"), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var loadedWeaves = await LoadWeavesAsync(fabricWorkspace, weaveReferences, cancellationToken).ConfigureAwait(false);
        var bindingsById = LoadBindingReferences(bindingReferences, loadedWeaves);

        var suggestions = new List<FabricScopeSuggestion>();
        var weakSuggestions = new List<FabricWeakScopeSuggestion>();
        foreach (var childBinding in bindingsById.Values
                     .OrderBy(item => item.BindingReferenceId, StringComparer.Ordinal))
        {
            if (existingScopesByBindingId.ContainsKey(childBinding.BindingReferenceId))
            {
                continue;
            }

            var failsWithoutScope = await BindingFailsWithoutScopeAsync(fabricWorkspace, weaveReferences, childBinding.BindingReference, cancellationToken).ConfigureAwait(false);
            if (!failsWithoutScope)
            {
                continue;
            }

            var candidateSuggestions = new List<FabricScopeSuggestion>();
            foreach (var parentBinding in bindingsById.Values
                         .Where(item => !string.Equals(item.BindingReferenceId, childBinding.BindingReferenceId, StringComparison.Ordinal))
                         .OrderBy(item => item.BindingReferenceId, StringComparer.Ordinal))
            {
                foreach (var sourceParentReferenceName in EnumerateCandidateReferenceNames(childBinding.SourceEntity))
                {
                    foreach (var targetParentReferenceName in EnumerateCandidateReferenceNames(childBinding.TargetEntity))
                    {
                        if (!await CandidatePassesAsync(
                                fabricWorkspace,
                                weaveReferences,
                                existingScopesByBindingId,
                                childBinding,
                                parentBinding,
                                sourceParentReferenceName,
                                targetParentReferenceName,
                                cancellationToken).ConfigureAwait(false))
                        {
                            continue;
                        }

                        candidateSuggestions.Add(new FabricScopeSuggestion(
                            ChildBindingReferenceName: childBinding.ReferenceName,
                            ChildBindingName: childBinding.BindingName,
                            ParentBindingReferenceName: parentBinding.ReferenceName,
                            ParentBindingName: parentBinding.BindingName,
                            SourceParentReferenceName: sourceParentReferenceName,
                            TargetParentReferenceName: targetParentReferenceName));
                    }
                }
            }

            var distinctCandidates = candidateSuggestions
                .GroupBy(item => MakeSuggestionKey(item), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.ParentBindingReferenceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SourceParentReferenceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TargetParentReferenceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ParentBindingReferenceName, StringComparer.Ordinal)
                .ThenBy(item => item.SourceParentReferenceName, StringComparer.Ordinal)
                .ThenBy(item => item.TargetParentReferenceName, StringComparer.Ordinal)
                .ToList();

            if (distinctCandidates.Count == 1)
            {
                suggestions.Add(distinctCandidates[0]);
            }
            else if (distinctCandidates.Count > 1)
            {
                weakSuggestions.Add(new FabricWeakScopeSuggestion(
                    ChildBindingReferenceName: childBinding.ReferenceName,
                    ChildBindingName: childBinding.BindingName,
                    Candidates: distinctCandidates));
            }
        }

        return new FabricSuggestResult(
            suggestions
                .OrderBy(item => item.ChildBindingReferenceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ParentBindingReferenceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SourceParentReferenceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TargetParentReferenceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ChildBindingReferenceName, StringComparer.Ordinal)
                .ThenBy(item => item.ParentBindingReferenceName, StringComparer.Ordinal)
                .ThenBy(item => item.SourceParentReferenceName, StringComparer.Ordinal)
                .ThenBy(item => item.TargetParentReferenceName, StringComparer.Ordinal)
                .ToList(),
            weakSuggestions
                .OrderBy(item => item.ChildBindingReferenceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ChildBindingReferenceName, StringComparer.Ordinal)
                .ToList());
    }

    private async Task<bool> BindingFailsWithoutScopeAsync(
        Workspace fabricWorkspace,
        IReadOnlyCollection<GenericRecord> weaveReferences,
        GenericRecord childBindingReference,
        CancellationToken cancellationToken)
    {
        var tempWorkspace = CreateScopedWorkspaceClone(fabricWorkspace, weaveReferences, new[] { childBindingReference }, Array.Empty<GenericRecord>());
        var result = await _fabricService.CheckAsync(tempWorkspace, cancellationToken).ConfigureAwait(false);
        return result.HasErrors;
    }

    private async Task<bool> CandidatePassesAsync(
        Workspace fabricWorkspace,
        IReadOnlyCollection<GenericRecord> weaveReferences,
        IReadOnlyDictionary<string, List<GenericRecord>> existingScopesByBindingId,
        LoadedBindingReference childBinding,
        LoadedBindingReference parentBinding,
        string sourceParentReferenceName,
        string targetParentReferenceName,
        CancellationToken cancellationToken)
    {
        var includedBindingIds = new HashSet<string>(StringComparer.Ordinal)
        {
            childBinding.BindingReferenceId,
            parentBinding.BindingReferenceId,
        };

        AddBindingClosure(parentBinding.BindingReferenceId, existingScopesByBindingId, includedBindingIds);
        var selectedBindingReferences = fabricWorkspace.Instance.GetOrCreateEntityRecords("BindingReference")
            .Where(record => includedBindingIds.Contains(record.Id))
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();
        var selectedScopeRequirements = fabricWorkspace.Instance.GetOrCreateEntityRecords("BindingScopeRequirement")
            .Where(record => includedBindingIds.Contains(RequireRelationshipId(record, "BindingId")) &&
                             includedBindingIds.Contains(RequireRelationshipId(record, "ParentBindingId")))
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();

        var nextScopeId = GetNextSyntheticId(selectedScopeRequirements.Select(record => record.Id));
        var candidateScope = new GenericRecord { Id = nextScopeId };
        candidateScope.Values["SourceParentReferenceName"] = sourceParentReferenceName;
        candidateScope.Values["TargetParentReferenceName"] = targetParentReferenceName;
        candidateScope.RelationshipIds["BindingId"] = childBinding.BindingReferenceId;
        candidateScope.RelationshipIds["ParentBindingId"] = parentBinding.BindingReferenceId;
        selectedScopeRequirements.Add(candidateScope);

        var tempWorkspace = CreateScopedWorkspaceClone(fabricWorkspace, weaveReferences, selectedBindingReferences, selectedScopeRequirements);
        var result = await _fabricService.CheckAsync(tempWorkspace, cancellationToken).ConfigureAwait(false);
        return !result.HasErrors;
    }

    private static void AddBindingClosure(
        string bindingId,
        IReadOnlyDictionary<string, List<GenericRecord>> scopeRequirementsByBindingId,
        ISet<string> includedBindingIds)
    {
        if (!scopeRequirementsByBindingId.TryGetValue(bindingId, out var requirements))
        {
            return;
        }

        foreach (var requirement in requirements)
        {
            var parentBindingId = RequireRelationshipId(requirement, "ParentBindingId");
            if (includedBindingIds.Add(parentBindingId))
            {
                AddBindingClosure(parentBindingId, scopeRequirementsByBindingId, includedBindingIds);
            }
        }
    }

    private static Workspace CreateScopedWorkspaceClone(
        Workspace sourceWorkspace,
        IReadOnlyCollection<GenericRecord> weaveReferences,
        IReadOnlyCollection<GenericRecord> bindingReferences,
        IReadOnlyCollection<GenericRecord> scopeRequirements)
    {
        var snapshot = WorkspaceSnapshotCloner.Capture(sourceWorkspace);
        var clone = new Workspace
        {
            WorkspaceRootPath = sourceWorkspace.WorkspaceRootPath,
            MetadataRootPath = sourceWorkspace.MetadataRootPath,
            WorkspaceConfig = WorkspaceSnapshotCloner.CloneWorkspaceConfig(snapshot.WorkspaceConfig),
            Model = WorkspaceSnapshotCloner.CloneModel(snapshot.Model),
            Instance = new GenericInstance { ModelName = sourceWorkspace.Instance.ModelName },
        };

        CopyRecords(clone.Instance, "WeaveReference", weaveReferences);
        CopyRecords(clone.Instance, "BindingReference", bindingReferences);
        CopyRecords(clone.Instance, "BindingScopeRequirement", scopeRequirements);
        return clone;
    }

    private static void CopyRecords(GenericInstance instance, string entityName, IReadOnlyCollection<GenericRecord> sourceRecords)
    {
        var target = instance.GetOrCreateEntityRecords(entityName);
        foreach (var record in sourceRecords.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var clone = new GenericRecord
            {
                Id = record.Id,
                SourceShardFileName = record.SourceShardFileName,
            };

            foreach (var value in record.Values)
            {
                clone.Values[value.Key] = value.Value;
            }

            foreach (var relationship in record.RelationshipIds)
            {
                clone.RelationshipIds[relationship.Key] = relationship.Value;
            }

            target.Add(clone);
        }
    }

    private Dictionary<string, LoadedBindingReference> LoadBindingReferences(
        IReadOnlyCollection<GenericRecord> bindingReferences,
        IReadOnlyDictionary<string, LoadedWeaveReference> loadedWeaves)
    {
        var result = new Dictionary<string, LoadedBindingReference>(StringComparer.Ordinal);
        foreach (var bindingReference in bindingReferences)
        {
            var weaveReferenceId = RequireRelationshipId(bindingReference, "WeaveReferenceId");
            if (!loadedWeaves.TryGetValue(weaveReferenceId, out var loadedWeave))
            {
                throw new InvalidOperationException($"BindingReference '{bindingReference.Id}' references missing weave '{weaveReferenceId}'.");
            }

            var bindingName = RequireValue(bindingReference, "BindingName");
            var weaveBindings = loadedWeave.WeaveWorkspace.Instance.GetOrCreateEntityRecords("PropertyBinding")
                .Where(record => record.Values.TryGetValue("Name", out var value) && string.Equals(value, bindingName, StringComparison.Ordinal))
                .ToList();
            if (weaveBindings.Count != 1)
            {
                throw new InvalidOperationException($"BindingReference '{bindingReference.Id}' could not resolve unique weave binding '{bindingName}' in weave '{loadedWeave.Alias}'.");
            }

            var weaveBinding = weaveBindings[0];
            var sourceModelId = RequireRelationshipId(weaveBinding, "SourceModelId");
            var targetModelId = RequireRelationshipId(weaveBinding, "TargetModelId");
            var sourceWorkspace = loadedWeave.ReferencedWorkspacesById[sourceModelId];
            var targetWorkspace = loadedWeave.ReferencedWorkspacesById[targetModelId];
            var sourceEntityName = RequireValue(weaveBinding, "SourceEntity");
            var targetEntityName = RequireValue(weaveBinding, "TargetEntity");
            var sourceEntity = sourceWorkspace.Model.FindEntity(sourceEntityName)
                ?? throw new InvalidOperationException($"BindingReference '{bindingReference.Id}' source entity '{sourceEntityName}' was not found in model '{sourceWorkspace.Model.Name}'.");
            var targetEntity = targetWorkspace.Model.FindEntity(targetEntityName)
                ?? throw new InvalidOperationException($"BindingReference '{bindingReference.Id}' target entity '{targetEntityName}' was not found in model '{targetWorkspace.Model.Name}'.");

            result[bindingReference.Id] = new LoadedBindingReference(
                BindingReferenceId: bindingReference.Id,
                BindingReference: bindingReference,
                ReferenceName: RequireValue(bindingReference, "Name"),
                BindingName: bindingName,
                SourceEntity: sourceEntity,
                TargetEntity: targetEntity);
        }

        return result;
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
                weaveWorkspace,
                referencedWorkspaces);
        }

        return loadedWeaves;
    }

    private static IEnumerable<string> EnumerateCandidateReferenceNames(GenericEntity entity)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Id",
        };

        foreach (var property in entity.Properties)
        {
            names.Add(property.Name);
        }

        foreach (var relationship in entity.Relationships)
        {
            names.Add(relationship.GetColumnName());
        }

        return names
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item, StringComparer.Ordinal);
    }

    private static string GetNextSyntheticId(IEnumerable<string> existingIds)
    {
        var max = 0;
        foreach (var id in existingIds)
        {
            if (int.TryParse(id, out var parsed) && parsed > max)
            {
                max = parsed;
            }
        }

        return (max + 1).ToString();
    }

    private static string MakeSuggestionKey(FabricScopeSuggestion suggestion)
    {
        return string.Join("|", new[]
        {
            suggestion.ChildBindingReferenceName,
            suggestion.ParentBindingReferenceName,
            suggestion.SourceParentReferenceName,
            suggestion.TargetParentReferenceName,
        });
    }

    private static string ResolveWorkspacePath(string fabricWorkspaceRootPath, string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.GetFullPath(Path.Combine(fabricWorkspaceRootPath, configuredPath));
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

    private sealed record LoadedWeaveReference(
        string ReferenceId,
        string Alias,
        Workspace WeaveWorkspace,
        IReadOnlyDictionary<string, Workspace> ReferencedWorkspacesById);

    private sealed record LoadedBindingReference(
        string BindingReferenceId,
        GenericRecord BindingReference,
        string ReferenceName,
        string BindingName,
        GenericEntity SourceEntity,
        GenericEntity TargetEntity);
}
