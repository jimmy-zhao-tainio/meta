namespace MetaDocs.Core;

public static class MetaDocsVocabulary
{
    public static DocumentationWorkspaceType EnsureWorkspaceType(MetaDocsModel model, string name, string description = "") =>
        Ensure(
            model.DocumentationWorkspaceTypeList,
            Id("workspace-type", name),
            name,
            description,
            static row => row.Name,
            static (row, value) => row.Name = value,
            static (row, value) => row.Description = value,
            static id => new DocumentationWorkspaceType { Id = id });

    public static DocumentationSourceType EnsureSourceType(MetaDocsModel model, string name, string description = "") =>
        Ensure(
            model.DocumentationSourceTypeList,
            Id("source-type", name),
            name,
            description,
            static row => row.Name,
            static (row, value) => row.Name = value,
            static (row, value) => row.Description = value,
            static id => new DocumentationSourceType { Id = id });

    public static DocumentationSubjectType EnsureSubjectType(MetaDocsModel model, string name, string description = "") =>
        Ensure(
            model.DocumentationSubjectTypeList,
            Id("subject-type", name),
            name,
            description,
            static row => row.Name,
            static (row, value) => row.Name = value,
            static (row, value) => row.Description = value,
            static id => new DocumentationSubjectType { Id = id });

    public static DocumentationFactType EnsureFactType(MetaDocsModel model, string name, string description = "") =>
        Ensure(
            model.DocumentationFactTypeList,
            Id("fact-type", name),
            name,
            description,
            static row => row.Name,
            static (row, value) => row.Name = value,
            static (row, value) => row.Description = value,
            static id => new DocumentationFactType { Id = id });

    public static DocumentationValueType EnsureValueType(MetaDocsModel model, string name, string description = "") =>
        Ensure(
            model.DocumentationValueTypeList,
            Id("value-type", name),
            name,
            description,
            static row => row.Name,
            static (row, value) => row.Name = value,
            static (row, value) => row.Description = value,
            static id => new DocumentationValueType { Id = id });

    public static DocumentationRelationshipType EnsureRelationshipType(MetaDocsModel model, string name, string description = "") =>
        Ensure(
            model.DocumentationRelationshipTypeList,
            Id("relationship-type", name),
            name,
            description,
            static row => row.Name,
            static (row, value) => row.Name = value,
            static (row, value) => row.Description = value,
            static id => new DocumentationRelationshipType { Id = id });

    public static DocumentationViewType EnsureViewType(MetaDocsModel model, string name, string description = "") =>
        Ensure(
            model.DocumentationViewTypeList,
            Id("view-type", name),
            name,
            description,
            static row => row.Name,
            static (row, value) => row.Name = value,
            static (row, value) => row.Description = value,
            static id => new DocumentationViewType { Id = id });

    public static DocumentationTemplateType EnsureTemplateType(MetaDocsModel model, string name, string description = "") =>
        Ensure(
            model.DocumentationTemplateTypeList,
            Id("template-type", name),
            name,
            description,
            static row => row.Name,
            static (row, value) => row.Name = value,
            static (row, value) => row.Description = value,
            static id => new DocumentationTemplateType { Id = id });

    public static DocumentationTemplateRegionType EnsureTemplateRegionType(MetaDocsModel model, string name, string description = "") =>
        Ensure(
            model.DocumentationTemplateRegionTypeList,
            Id("template-region-type", name),
            name,
            description,
            static row => row.Name,
            static (row, value) => row.Name = value,
            static (row, value) => row.Description = value,
            static id => new DocumentationTemplateRegionType { Id = id });

    public static DocumentationThemeAssetType EnsureThemeAssetType(MetaDocsModel model, string name, string description = "") =>
        Ensure(
            model.DocumentationThemeAssetTypeList,
            Id("theme-asset-type", name),
            name,
            description,
            static row => row.Name,
            static (row, value) => row.Name = value,
            static (row, value) => row.Description = value,
            static id => new DocumentationThemeAssetType { Id = id });

    public static DocumentationLayoutType EnsureLayoutType(MetaDocsModel model, string name, string description = "") =>
        Ensure(
            model.DocumentationLayoutTypeList,
            Id("layout-type", name),
            name,
            description,
            static row => row.Name,
            static (row, value) => row.Name = value,
            static (row, value) => row.Description = value,
            static id => new DocumentationLayoutType { Id = id });

    public static DocumentationComponentTemplateType EnsureComponentTemplateType(MetaDocsModel model, string name, string description = "") =>
        Ensure(
            model.DocumentationComponentTemplateTypeList,
            Id("component-template-type", name),
            name,
            description,
            static row => row.Name,
            static (row, value) => row.Name = value,
            static (row, value) => row.Description = value,
            static id => new DocumentationComponentTemplateType { Id = id });

    public static string SubjectTypeName(DocumentationSubject? subject) =>
        subject?.DocumentationSubjectType?.Name ?? string.Empty;

    public static string WorkspaceTypeName(DocumentationWorkspace? workspace) =>
        workspace?.DocumentationWorkspaceType?.Name ?? string.Empty;

    public static bool IsWorkspaceType(DocumentationWorkspace? workspace, string name) =>
        string.Equals(WorkspaceTypeName(workspace), name, StringComparison.OrdinalIgnoreCase);

    public static string SourceTypeName(DocumentationSource? source) =>
        source?.DocumentationSourceType?.Name ?? string.Empty;

    public static bool IsSourceType(DocumentationSource? source, string name) =>
        string.Equals(SourceTypeName(source), name, StringComparison.OrdinalIgnoreCase);

    public static bool IsSubjectType(DocumentationSubject? subject, string name) =>
        string.Equals(SubjectTypeName(subject), name, StringComparison.OrdinalIgnoreCase);

    public static string FactTypeName(DocumentationFact? fact) =>
        fact?.DocumentationFactType?.Name ?? string.Empty;

    public static bool IsFactType(DocumentationFact? fact, string name) =>
        string.Equals(FactTypeName(fact), name, StringComparison.OrdinalIgnoreCase);

    public static string RelationshipTypeName(DocumentationRelationship? relationship) =>
        relationship?.DocumentationRelationshipType?.Name ?? string.Empty;

    public static bool IsRelationshipType(DocumentationRelationship? relationship, string name) =>
        string.Equals(RelationshipTypeName(relationship), name, StringComparison.OrdinalIgnoreCase);

    public static string ThemeAssetTypeName(DocumentationThemeAsset? asset) =>
        asset?.DocumentationThemeAssetType?.Name ?? string.Empty;

    public static bool IsThemeAssetType(DocumentationThemeAsset? asset, string name) =>
        string.Equals(ThemeAssetTypeName(asset), name, StringComparison.OrdinalIgnoreCase);

    public static string TemplateTypeName(DocumentationTemplate? template) =>
        template?.DocumentationTemplateType?.Name ?? string.Empty;

    public static bool IsTemplateType(DocumentationTemplate? template, string name) =>
        string.Equals(TemplateTypeName(template), name, StringComparison.OrdinalIgnoreCase);

    public static string ViewTypeName(DocumentationView? view) =>
        view?.DocumentationViewType?.Name ?? string.Empty;

    public static bool IsViewType(DocumentationView? view, string name) =>
        string.Equals(ViewTypeName(view), name, StringComparison.OrdinalIgnoreCase);

    public static string ValueTypeName(DocumentationFact? fact) =>
        fact?.DocumentationValueType?.Name ?? string.Empty;

    private static T Ensure<T>(
        IList<T> rows,
        string id,
        string name,
        string description,
        Func<T, string> getName,
        Action<T, string> setName,
        Action<T, string?> setDescription,
        Func<string, T> create)
        where T : class
    {
        var row = rows.FirstOrDefault(item => string.Equals(GetId(item), id, StringComparison.OrdinalIgnoreCase)) ??
                  rows.FirstOrDefault(item => string.Equals(getName(item), name, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            row = create(id);
            rows.Add(row);
        }

        setName(row, name);
        setDescription(row, string.IsNullOrWhiteSpace(description) ? null : description);
        return row;
    }

    private static string GetId<T>(T row)
        where T : class =>
        row switch
        {
            DocumentationWorkspaceType item => item.Id,
            DocumentationSourceType item => item.Id,
            DocumentationSubjectType item => item.Id,
            DocumentationFactType item => item.Id,
            DocumentationValueType item => item.Id,
            DocumentationRelationshipType item => item.Id,
            DocumentationViewType item => item.Id,
            DocumentationTemplateType item => item.Id,
            DocumentationTemplateRegionType item => item.Id,
            DocumentationThemeAssetType item => item.Id,
            DocumentationLayoutType item => item.Id,
            DocumentationComponentTemplateType item => item.Id,
            _ => throw new InvalidOperationException($"Unsupported MetaDocs vocabulary row '{typeof(T).Name}'."),
        };

    private static string Id(string prefix, string name) =>
        $"{prefix}:{MetaDocsImportSession.NormalizeKey(name)}";
}
