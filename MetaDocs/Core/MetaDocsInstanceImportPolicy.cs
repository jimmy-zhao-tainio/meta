using MetaDocs;

namespace MetaDocs.Core;

public sealed class MetaDocsInstanceImportPolicy
{
    private readonly MetaDocsModel model;

    public MetaDocsInstanceImportPolicy(MetaDocsModel model)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public bool IncludesEntity(string entityName, string sourceId = "") =>
        FindEntitySpec(entityName, sourceId) is not null;

    public bool IncludesProperty(string entityName, string propertyName, string sourceId = "")
    {
        var entitySpec = FindEntitySpec(entityName, sourceId);
        if (entitySpec is null)
        {
            return false;
        }

        return model.DocumentationPropertyImportSpecList.Any(spec =>
            ReferenceEquals(spec.DocumentationEntityImportSpec, entitySpec) &&
            string.Equals(spec.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase) &&
            IsIncluded(spec.Include));
    }

    public bool IncludesRelationship(string entityName, string relationshipSelector, string sourceId = "")
    {
        var entitySpec = FindEntitySpec(entityName, sourceId);
        if (entitySpec is null)
        {
            return false;
        }

        return model.DocumentationRelationshipImportSpecList.Any(spec =>
            ReferenceEquals(spec.DocumentationEntityImportSpec, entitySpec) &&
            string.Equals(spec.RelationshipSelector, relationshipSelector, StringComparison.OrdinalIgnoreCase) &&
            IsIncluded(spec.Include));
    }

    public DocumentationEntityImportSpec? FindEntitySpec(string entityName, string sourceId = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);

        return model.DocumentationEntityImportSpecList
            .Where(spec => string.Equals(spec.EntityName, entityName, StringComparison.OrdinalIgnoreCase))
            .Where(spec => IsIncluded(spec.IncludeInstances))
            .Where(spec => IsInstanceSpecIncluded(spec.DocumentationInstanceImportSpec, sourceId))
            .OrderBy(spec => IsSourceSpecific(spec.DocumentationInstanceImportSpec, sourceId) ? 0 : 1)
            .ThenBy(spec => spec.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool IsInstanceSpecIncluded(DocumentationInstanceImportSpec? spec, string sourceId)
    {
        if (spec is null || !IsIncluded(spec.IncludeInstances))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return true;
        }

        return spec.DocumentationSource is null ||
               string.Equals(spec.DocumentationSource.Id, sourceId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSourceSpecific(DocumentationInstanceImportSpec? spec, string sourceId) =>
        !string.IsNullOrWhiteSpace(sourceId) &&
        spec?.DocumentationSource is not null &&
        string.Equals(spec.DocumentationSource.Id, sourceId, StringComparison.OrdinalIgnoreCase);

    private static bool IsIncluded(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("include", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("included", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("enabled", StringComparison.OrdinalIgnoreCase);
    }

}
