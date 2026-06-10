using MetaDocs;

namespace MetaDocs.Core;

public sealed class MetaDocsInstanceImportPolicyEditor
{
    public DocumentationEntityImportSpec IncludeEntity(
        MetaDocsModel model,
        string entityName,
        string sourceId = "",
        string displayNameProperty = "",
        string summaryProperty = "",
        int ordinal = 100)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);

        var root = EnsureRootSpec(model, sourceId);
        var entityId = BuildEntitySpecId(root.Id, entityName);
        var entity = model.DocumentationEntityImportSpecList.FirstOrDefault(row =>
            string.Equals(row.Id, entityId, StringComparison.OrdinalIgnoreCase))
            ?? new DocumentationEntityImportSpec
            {
                Id = entityId,
                DocumentationInstanceImportSpec = root,
            };
        if (!model.DocumentationEntityImportSpecList.Contains(entity))
        {
            model.DocumentationEntityImportSpecList.Add(entity);
        }

        entity.DocumentationInstanceImportSpec = root;
        entity.EntityName = entityName.Trim();
        entity.IncludeInstances = "include";
        entity.DisplayNameProperty = !string.IsNullOrWhiteSpace(displayNameProperty)
            ? displayNameProperty.Trim()
            : entity.DisplayNameProperty;
        entity.SummaryProperty = !string.IsNullOrWhiteSpace(summaryProperty)
            ? summaryProperty.Trim()
            : entity.SummaryProperty;
        entity.Ordinal = ordinal.ToString("000");
        entity.ReviewStatus = "Current";
        return entity;
    }

    public DocumentationPropertyImportSpec IncludeProperty(
        MetaDocsModel model,
        string entityName,
        string propertyName,
        string sourceId = "",
        int ordinal = 100)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        var entity = IncludeEntity(model, entityName, sourceId, ordinal: ordinal);
        var specId = $"{entity.Id}:property:{MetaDocsImportSession.NormalizeKey(propertyName)}";
        var spec = model.DocumentationPropertyImportSpecList.FirstOrDefault(row =>
            string.Equals(row.Id, specId, StringComparison.OrdinalIgnoreCase))
            ?? new DocumentationPropertyImportSpec
            {
                Id = specId,
                DocumentationEntityImportSpec = entity,
            };
        if (!model.DocumentationPropertyImportSpecList.Contains(spec))
        {
            model.DocumentationPropertyImportSpecList.Add(spec);
        }

        spec.DocumentationEntityImportSpec = entity;
        spec.PropertyName = propertyName.Trim();
        spec.Include = "include";
        spec.Ordinal = ordinal.ToString("000");
        spec.ReviewStatus = "Current";
        return spec;
    }

    public DocumentationRelationshipImportSpec IncludeRelationship(
        MetaDocsModel model,
        string entityName,
        string relationshipSelector,
        string sourceId = "",
        int ordinal = 100)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipSelector);

        var entity = IncludeEntity(model, entityName, sourceId, ordinal: ordinal);
        var specId = $"{entity.Id}:relationship:{MetaDocsImportSession.NormalizeKey(relationshipSelector)}";
        var spec = model.DocumentationRelationshipImportSpecList.FirstOrDefault(row =>
            string.Equals(row.Id, specId, StringComparison.OrdinalIgnoreCase))
            ?? new DocumentationRelationshipImportSpec
            {
                Id = specId,
                DocumentationEntityImportSpec = entity,
            };
        if (!model.DocumentationRelationshipImportSpecList.Contains(spec))
        {
            model.DocumentationRelationshipImportSpecList.Add(spec);
        }

        spec.DocumentationEntityImportSpec = entity;
        spec.RelationshipSelector = relationshipSelector.Trim();
        spec.Include = "include";
        spec.Ordinal = ordinal.ToString("000");
        spec.ReviewStatus = "Current";
        return spec;
    }

    private static DocumentationInstanceImportSpec EnsureRootSpec(MetaDocsModel model, string sourceId)
    {
        MetaDocsDefaults.EnsureDocumentationWorkspace(model, "workspace:default", "Documentation", "SourceDocumentation");

        var normalizedSourceId = sourceId.Trim();
        var id = string.IsNullOrWhiteSpace(normalizedSourceId)
            ? "instance-spec:global"
            : $"instance-spec:{MetaDocsImportSession.NormalizeKey(normalizedSourceId)}";
        var root = model.DocumentationInstanceImportSpecList.FirstOrDefault(row =>
            string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? new DocumentationInstanceImportSpec
            {
                Id = id,
            };
        if (!model.DocumentationInstanceImportSpecList.Contains(root))
        {
            model.DocumentationInstanceImportSpecList.Add(root);
        }

        root.Name = string.IsNullOrWhiteSpace(normalizedSourceId)
            ? "Global instance documentation policy"
            : $"Instance documentation policy for {normalizedSourceId}";
        root.IncludeInstances = "include";
        root.SafetyStatus = "Approved";
        root.Ordinal = "010";
        root.DocumentationSource = string.IsNullOrWhiteSpace(normalizedSourceId)
            ? null
            : EnsureSource(model, normalizedSourceId);
        return root;
    }

    private static DocumentationSource EnsureSource(MetaDocsModel model, string sourceId)
    {
        var source = model.DocumentationSourceList.FirstOrDefault(row =>
            string.Equals(row.Id, sourceId, StringComparison.OrdinalIgnoreCase))
            ?? new DocumentationSource
            {
                Id = sourceId,
                DocumentationWorkspace = model.DocumentationWorkspaceList.First(),
            };
        if (!model.DocumentationSourceList.Contains(source))
        {
            model.DocumentationSourceList.Add(source);
        }

        source.Kind = string.IsNullOrWhiteSpace(source.Kind) ? "Workspace" : source.Kind;
        source.DisplayName = string.IsNullOrWhiteSpace(source.DisplayName) ? sourceId : source.DisplayName;
        source.Status = string.IsNullOrWhiteSpace(source.Status) ? "Current" : source.Status;
        return source;
    }

    private static string BuildEntitySpecId(string rootSpecId, string entityName) =>
        $"{rootSpecId}:entity:{MetaDocsImportSession.NormalizeKey(entityName)}";

    private static string? NormalizeOptional(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
