using Meta.Core.Domain;

namespace MetaTypeConversion.Core;

public sealed record TypeMappingResolution(
    string MappingId,
    string SourceTypeId,
    string TargetTypeId,
    string ConversionImplementationId,
    string ConversionImplementationName,
    string? Notes);

public sealed record MetaTypeConversionCheckResult(
    int MappingCount,
    int ImplementationCount,
    IReadOnlyList<string> Errors)
{
    public bool HasErrors => Errors.Count > 0;
}

public interface IMetaTypeConversionService
{
    MetaTypeConversionCheckResult Check(Workspace workspace);
    TypeMappingResolution Resolve(Workspace workspace, string sourceTypeId);
}

public sealed class MetaTypeConversionService : IMetaTypeConversionService
{
    public MetaTypeConversionCheckResult Check(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var implementations = workspace.Instance.GetOrCreateEntityRecords("ConversionImplementation")
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();
        var mappings = workspace.Instance.GetOrCreateEntityRecords("TypeMapping")
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();

        var implementationById = implementations.ToDictionary(record => record.Id, StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (var mapping in mappings)
        {
            var sourceTypeId = RequireValue(mapping, "SourceTypeId");
            var targetTypeId = RequireValue(mapping, "TargetTypeId");
            _ = targetTypeId;

            if (!mapping.RelationshipIds.TryGetValue("ConversionImplementationId", out var implementationId) ||
                string.IsNullOrWhiteSpace(implementationId))
            {
                errors.Add($"TypeMapping '{mapping.Id}' is missing required relationship 'ConversionImplementationId'.");
                continue;
            }

            if (!implementationById.ContainsKey(implementationId))
            {
                errors.Add($"TypeMapping '{mapping.Id}' references missing ConversionImplementation '{implementationId}'.");
            }
        }

        var duplicateSources = mappings
            .GroupBy(record => RequireValue(record, "SourceTypeId"), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var duplicateSource in duplicateSources)
        {
            var ids = string.Join(", ", duplicateSource.Select(record => record.Id).OrderBy(id => id, StringComparer.Ordinal));
            errors.Add($"SourceTypeId '{duplicateSource.Key}' is mapped more than once ({ids}).");
        }

        return new MetaTypeConversionCheckResult(mappings.Count, implementations.Count, errors);
    }

    public TypeMappingResolution Resolve(Workspace workspace, string sourceTypeId)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceTypeId);

        var check = Check(workspace);
        if (check.HasErrors)
        {
            throw new InvalidOperationException("MetaTypeConversion workspace is invalid. Run 'meta-type-conversion check' first.");
        }

        var mappings = workspace.Instance.GetOrCreateEntityRecords("TypeMapping")
            .Where(record => string.Equals(RequireValue(record, "SourceTypeId"), sourceTypeId, StringComparison.Ordinal))
            .ToList();

        if (mappings.Count == 0)
        {
            throw new InvalidOperationException($"No TypeMapping exists for source type '{sourceTypeId}'.");
        }

        if (mappings.Count > 1)
        {
            throw new InvalidOperationException($"Source type '{sourceTypeId}' resolves ambiguously to {mappings.Count} TypeMappings.");
        }

        var mapping = mappings[0];
        var implementations = workspace.Instance.GetOrCreateEntityRecords("ConversionImplementation")
            .ToDictionary(record => record.Id, StringComparer.Ordinal);

        var implementationId = mapping.RelationshipIds["ConversionImplementationId"];
        var implementation = implementations[implementationId];

        return new TypeMappingResolution(
            mapping.Id,
            RequireValue(mapping, "SourceTypeId"),
            RequireValue(mapping, "TargetTypeId"),
            implementationId,
            RequireValue(implementation, "Name"),
            mapping.Values.TryGetValue("Notes", out var notes) ? notes : null);
    }

    private static string RequireValue(GenericRecord record, string propertyName)
    {
        if (!record.Values.TryGetValue(propertyName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Record '{record.Id}' is missing required property '{propertyName}'.");
        }

        return value;
    }
}
