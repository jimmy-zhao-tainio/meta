using System.Globalization;
using Meta.Core.Domain;
using Meta.Core.Services;

namespace MetaWeave.Core;

public sealed record WeaveBindingSuggestion(
    string BindingName,
    string SourceModelAlias,
    string SourceModelName,
    string SourceEntity,
    string SourceProperty,
    string TargetModelAlias,
    string TargetModelName,
    string TargetEntity,
    string TargetProperty,
    int SourceComparableRowCount,
    int MatchedSourceRowCount);

public sealed record WeaveWeakBindingSuggestion(
    string SourceModelAlias,
    string SourceModelName,
    string SourceEntity,
    string SourceProperty,
    IReadOnlyList<WeaveBindingSuggestion> Candidates);

public sealed record WeaveSuggestResult(
    IReadOnlyList<WeaveBindingSuggestion> Suggestions,
    IReadOnlyList<WeaveWeakBindingSuggestion> WeakSuggestions)
{
    public int SuggestionCount => Suggestions.Count;
    public int WeakSuggestionCount => WeakSuggestions.Count;
}

public interface IMetaWeaveSuggestService
{
    Task<WeaveSuggestResult> SuggestAsync(Workspace weaveWorkspace, CancellationToken cancellationToken = default);
}

public sealed class MetaWeaveSuggestService : IMetaWeaveSuggestService
{
    private readonly IWorkspaceService _workspaceService;

    public MetaWeaveSuggestService()
        : this(new WorkspaceService())
    {
    }

    public MetaWeaveSuggestService(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
    }

    public async Task<WeaveSuggestResult> SuggestAsync(Workspace weaveWorkspace, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(weaveWorkspace);

        var loadedModels = await LoadModelReferencesAsync(weaveWorkspace, cancellationToken).ConfigureAwait(false);
        var propertyBindings = weaveWorkspace.Instance.GetOrCreateEntityRecords("PropertyBinding")
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();
        var boundSourceKeys = propertyBindings
            .Select(MakeBoundSourceKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var boundBindingKeys = propertyBindings
            .Select(MakeEquivalentBindingKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var suggestions = new List<WeaveBindingSuggestion>();
        var weakSuggestions = new List<WeaveWeakBindingSuggestion>();
        foreach (var sourceModel in loadedModels)
        {
            foreach (var sourceEntity in sourceModel.Workspace.Model.Entities
                         .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Name, StringComparer.Ordinal))
            {
                var sourceRows = sourceModel.Workspace.Instance.GetOrCreateEntityRecords(sourceEntity.Name)
                    .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .ToList();

                foreach (var sourceProperty in sourceEntity.Properties
                             .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(item => item.Name, StringComparer.Ordinal))
                {
                    if (string.Equals(sourceProperty.Name, "Id", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var sourceKey = MakeBoundSourceKey(sourceModel.Reference.Id, sourceEntity.Name, sourceProperty.Name);
                    if (boundSourceKeys.Contains(sourceKey))
                    {
                        continue;
                    }

                    var sourceProfile = ProfileProperty(sourceEntity.Name, sourceProperty, sourceRows);
                    if (!IsEligibleSource(sourceProfile))
                    {
                        continue;
                    }

                    var eligibleTargets = new List<WeaveBindingSuggestion>();
                    foreach (var targetModel in loadedModels)
                    {
                        if (string.Equals(targetModel.Reference.Id, sourceModel.Reference.Id, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        foreach (var targetCandidate in EnumerateTargetCandidates(targetModel.Workspace, sourceProperty.Name))
                        {
                            var equivalentBindingKey = MakeEquivalentBindingKey(
                                sourceModel.Reference.Id,
                                sourceEntity.Name,
                                sourceProperty.Name,
                                targetModel.Reference.Id,
                                targetCandidate.EntityName,
                                targetCandidate.PropertyName);
                            if (boundBindingKeys.Contains(equivalentBindingKey))
                            {
                                continue;
                            }

                            var targetProfile = targetCandidate.IsImplicitId
                                ? ProfileImplicitId(targetCandidate.EntityName, targetCandidate.Rows)
                                : ProfileProperty(targetCandidate.EntityName, targetCandidate.Property!, targetCandidate.Rows);
                            if (!IsEligibleTarget(targetProfile))
                            {
                                continue;
                            }

                            if (!IsTypeCompatibleStrict(sourceProfile.DataType, targetProfile.DataType))
                            {
                                continue;
                            }

                            var coverage = BuildCoverage(sourceProfile, targetProfile);
                            if (coverage.UnmatchedDistinctCount > 0)
                            {
                                continue;
                            }

                            eligibleTargets.Add(new WeaveBindingSuggestion(
                                BindingName: $"{sourceModel.Alias}.{sourceEntity.Name}.{sourceProperty.Name} -> {targetModel.Alias}.{targetCandidate.EntityName}.{targetCandidate.PropertyName}",
                                SourceModelAlias: sourceModel.Alias,
                                SourceModelName: sourceModel.ModelName,
                                SourceEntity: sourceEntity.Name,
                                SourceProperty: sourceProperty.Name,
                                TargetModelAlias: targetModel.Alias,
                                TargetModelName: targetModel.ModelName,
                                TargetEntity: targetCandidate.EntityName,
                                TargetProperty: targetCandidate.PropertyName,
                                SourceComparableRowCount: sourceProfile.NonBlankCount,
                                MatchedSourceRowCount: coverage.MatchedSourceRowCount));
                        }
                    }

                    if (eligibleTargets.Count == 1)
                    {
                        suggestions.Add(eligibleTargets[0]);
                    }
                    else if (eligibleTargets.Count > 1)
                    {
                        weakSuggestions.Add(new WeaveWeakBindingSuggestion(
                            SourceModelAlias: sourceModel.Alias,
                            SourceModelName: sourceModel.ModelName,
                            SourceEntity: sourceEntity.Name,
                            SourceProperty: sourceProperty.Name,
                            Candidates: eligibleTargets
                                .OrderBy(item => item.TargetModelAlias, StringComparer.OrdinalIgnoreCase)
                                .ThenBy(item => item.TargetEntity, StringComparer.OrdinalIgnoreCase)
                                .ThenBy(item => item.TargetProperty, StringComparer.OrdinalIgnoreCase)
                                .ThenBy(item => item.TargetModelAlias, StringComparer.Ordinal)
                                .ThenBy(item => item.TargetEntity, StringComparer.Ordinal)
                                .ThenBy(item => item.TargetProperty, StringComparer.Ordinal)
                                .ToList()));
                    }
                }
            }
        }

        return new WeaveSuggestResult(
            suggestions
                .OrderBy(item => item.SourceModelAlias, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SourceEntity, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SourceProperty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TargetModelAlias, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TargetEntity, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TargetProperty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SourceModelAlias, StringComparer.Ordinal)
                .ThenBy(item => item.SourceEntity, StringComparer.Ordinal)
                .ThenBy(item => item.SourceProperty, StringComparer.Ordinal)
                .ThenBy(item => item.TargetModelAlias, StringComparer.Ordinal)
                .ThenBy(item => item.TargetEntity, StringComparer.Ordinal)
                .ThenBy(item => item.TargetProperty, StringComparer.Ordinal)
                .ToList(),
            weakSuggestions
                .OrderBy(item => item.SourceModelAlias, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SourceEntity, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SourceProperty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SourceModelAlias, StringComparer.Ordinal)
                .ThenBy(item => item.SourceEntity, StringComparer.Ordinal)
                .ThenBy(item => item.SourceProperty, StringComparer.Ordinal)
                .ToList());
    }

    private async Task<List<LoadedModelReference>> LoadModelReferencesAsync(Workspace weaveWorkspace, CancellationToken cancellationToken)
    {
        var modelReferences = weaveWorkspace.Instance.GetOrCreateEntityRecords("ModelReference")
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();
        var loaded = new List<LoadedModelReference>(modelReferences.Count);
        foreach (var modelReference in modelReferences)
        {
            var path = RequireValue(modelReference, "WorkspacePath");
            var resolvedPath = ResolveWorkspacePath(weaveWorkspace.WorkspaceRootPath, path);
            var workspace = await _workspaceService.LoadAsync(resolvedPath, searchUpward: false, cancellationToken).ConfigureAwait(false);
            var modelName = RequireValue(modelReference, "ModelName");
            if (!string.Equals(workspace.Model.Name, modelName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"ModelReference '{modelReference.Id}' expected model '{modelName}' but workspace '{resolvedPath}' contained '{workspace.Model.Name}'.");
            }

            loaded.Add(new LoadedModelReference(
                Reference: modelReference,
                Alias: RequireValue(modelReference, "Alias"),
                ModelName: modelName,
                Workspace: workspace));
        }

        return loaded;
    }

    private static IEnumerable<TargetCandidate> EnumerateTargetCandidates(Workspace workspace, string sourcePropertyName)
    {
        var allowImplicitId = sourcePropertyName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(sourcePropertyName, "Id", StringComparison.OrdinalIgnoreCase);

        foreach (var entity in workspace.Model.Entities
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            var rows = workspace.Instance.GetOrCreateEntityRecords(entity.Name)
                .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToList();

            if (allowImplicitId)
            {
                yield return new TargetCandidate(entity.Name, "Id", null, rows, IsImplicitId: true);
            }

            foreach (var property in entity.Properties
                         .Where(item => string.Equals(item.Name, sourcePropertyName, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Name, StringComparer.Ordinal))
            {
                yield return new TargetCandidate(entity.Name, property.Name, property, rows, IsImplicitId: false);
            }
        }
    }

    private static PropertyProfile ProfileProperty(string entityName, GenericProperty property, IReadOnlyList<GenericRecord> rows)
    {
        var dataType = NormalizeDataType(property.DataType);
        var nonNullCount = 0;
        var blankCount = 0;
        var distinctNonBlank = new HashSet<string>(StringComparer.Ordinal);
        var comparableValueCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!row.Values.TryGetValue(property.Name, out var rawValue))
            {
                continue;
            }

            var value = rawValue ?? string.Empty;
            nonNullCount++;
            if (IsBlank(value))
            {
                blankCount++;
                continue;
            }

            distinctNonBlank.Add(value);
            comparableValueCounts.TryGetValue(value, out var existing);
            comparableValueCounts[value] = existing + 1;
        }

        return new PropertyProfile(
            EntityName: entityName,
            PropertyName: property.Name,
            DataType: dataType,
            RowCount: rows.Count,
            NonNullCount: nonNullCount,
            NullCount: rows.Count - nonNullCount,
            BlankStringCount: blankCount,
            NonBlankCount: nonNullCount - blankCount,
            DistinctNonBlankCount: distinctNonBlank.Count,
            IsUniqueOverNonBlank: distinctNonBlank.Count == (nonNullCount - blankCount) && distinctNonBlank.Count > 0,
            ComparableValueCounts: comparableValueCounts);
    }

    private static PropertyProfile ProfileImplicitId(string entityName, IReadOnlyList<GenericRecord> rows)
    {
        var nonNullCount = 0;
        var blankCount = 0;
        var distinctNonBlank = new HashSet<string>(StringComparer.Ordinal);
        var comparableValueCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var observedIds = new List<string>();

        foreach (var row in rows)
        {
            var value = row.Id ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                blankCount++;
                continue;
            }

            nonNullCount++;
            observedIds.Add(value);
            distinctNonBlank.Add(value);
            comparableValueCounts.TryGetValue(value, out var existing);
            comparableValueCounts[value] = existing + 1;
        }

        return new PropertyProfile(
            EntityName: entityName,
            PropertyName: "Id",
            DataType: DetectImplicitIdDataType(observedIds),
            RowCount: rows.Count,
            NonNullCount: nonNullCount,
            NullCount: rows.Count - nonNullCount,
            BlankStringCount: blankCount,
            NonBlankCount: nonNullCount,
            DistinctNonBlankCount: distinctNonBlank.Count,
            IsUniqueOverNonBlank: distinctNonBlank.Count == nonNullCount && distinctNonBlank.Count > 0,
            ComparableValueCounts: comparableValueCounts);
    }

    private static bool IsEligibleSource(PropertyProfile profile)
    {
        return profile.RowCount >= 2 &&
               profile.NonBlankCount > 0 &&
               profile.BlankStringCount == 0 &&
               profile.NullCount == 0 &&
               profile.NonBlankCount > profile.DistinctNonBlankCount;
    }

    private static bool IsEligibleTarget(PropertyProfile profile)
    {
        return profile.RowCount >= 2 &&
               profile.NonBlankCount > 0 &&
               profile.BlankStringCount == 0 &&
               profile.NullCount == 0 &&
               profile.IsUniqueOverNonBlank;
    }

    private static CoverageMetrics BuildCoverage(PropertyProfile source, PropertyProfile target)
    {
        var matchedSourceRows = 0;
        var unmatchedDistinct = 0;

        foreach (var sourceValue in source.ComparableValueCounts)
        {
            if (target.ComparableValueCounts.ContainsKey(sourceValue.Key))
            {
                matchedSourceRows += sourceValue.Value;
            }
            else
            {
                unmatchedDistinct++;
            }
        }

        return new CoverageMetrics(
            MatchedSourceRowCount: matchedSourceRows,
            UnmatchedDistinctCount: unmatchedDistinct);
    }

    private static string MakeBoundSourceKey(GenericRecord binding)
    {
        return MakeBoundSourceKey(
            RequireRelationshipId(binding, "SourceModelId"),
            RequireValue(binding, "SourceEntity"),
            RequireValue(binding, "SourceProperty"));
    }

    private static string MakeBoundSourceKey(string sourceModelId, string sourceEntity, string sourceProperty)
    {
        return sourceModelId + "|" + sourceEntity + "|" + sourceProperty;
    }

    private static string MakeEquivalentBindingKey(GenericRecord binding)
    {
        return MakeEquivalentBindingKey(
            RequireRelationshipId(binding, "SourceModelId"),
            RequireValue(binding, "SourceEntity"),
            RequireValue(binding, "SourceProperty"),
            RequireRelationshipId(binding, "TargetModelId"),
            RequireValue(binding, "TargetEntity"),
            RequireValue(binding, "TargetProperty"));
    }

    private static string MakeEquivalentBindingKey(string sourceModelId, string sourceEntity, string sourceProperty, string targetModelId, string targetEntity, string targetProperty)
    {
        return sourceModelId + "|" + sourceEntity + "|" + sourceProperty + "|" + targetModelId + "|" + targetEntity + "|" + targetProperty;
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

    private static string NormalizeDataType(string? dataType)
    {
        var value = (dataType ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "string";
        }

        return value.ToLowerInvariant() switch
        {
            "boolean" => "bool",
            "int32" => "int",
            "int64" => "long",
            _ => value,
        };
    }

    private static bool IsTypeCompatibleStrict(string sourceType, string targetType)
    {
        return string.Equals(NormalizeDataType(sourceType), NormalizeDataType(targetType), StringComparison.OrdinalIgnoreCase);
    }

    private static string DetectImplicitIdDataType(IReadOnlyCollection<string> ids)
    {
        if (ids.Count == 0)
        {
            return "int";
        }

        if (ids.All(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            return "int";
        }

        if (ids.All(value => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            return "long";
        }

        return "string";
    }

    private static bool IsBlank(string value)
    {
        return string.IsNullOrEmpty(value);
    }

    private sealed record LoadedModelReference(
        GenericRecord Reference,
        string Alias,
        string ModelName,
        Workspace Workspace);

    private sealed record TargetCandidate(
        string EntityName,
        string PropertyName,
        GenericProperty? Property,
        IReadOnlyList<GenericRecord> Rows,
        bool IsImplicitId);

    private sealed record PropertyProfile(
        string EntityName,
        string PropertyName,
        string DataType,
        int RowCount,
        int NonNullCount,
        int NullCount,
        int BlankStringCount,
        int NonBlankCount,
        int DistinctNonBlankCount,
        bool IsUniqueOverNonBlank,
        IReadOnlyDictionary<string, int> ComparableValueCounts);

    private sealed record CoverageMetrics(
        int MatchedSourceRowCount,
        int UnmatchedDistinctCount);
}
