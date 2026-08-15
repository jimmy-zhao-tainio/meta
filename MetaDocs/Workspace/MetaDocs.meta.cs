#nullable enable
using System;
using System.Collections.Generic;

namespace MetaDocs;
public sealed partial class DocumentationComponentTemplate
{
    public string Id { get; set; } = null !;
    public string Name { get; set; } = null !;
    public string? TemplateText { get; set; }
    public DocumentationComponentTemplateType DocumentationComponentTemplateType { get; set; } = null !;
    public DocumentationTheme DocumentationTheme { get; set; } = null !;
    public DocumentationComponentTemplate? PreviousComponent { get; set; }
}

public sealed partial class DocumentationComponentTemplateType
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string Name { get; set; } = null !;
}

public sealed partial class DocumentationEntityImportSpec
{
    public string Id { get; set; } = null !;
    public string? DisplayNameProperty { get; set; }
    public string EntityName { get; set; } = null !;
    public string IncludeInstances { get; set; } = null !;
    public string ReviewStatus { get; set; } = null !;
    public string? SummaryProperty { get; set; }
    public DocumentationInstanceImportSpec DocumentationInstanceImportSpec { get; set; } = null !;
}

public sealed partial class DocumentationFact
{
    public string Id { get; set; } = null !;
    public string Name { get; set; } = null !;
    public string? SourceFingerprint { get; set; }
    public string Status { get; set; } = null !;
    public string? Value { get; set; }
    public DocumentationFactType DocumentationFactType { get; set; } = null !;
    public DocumentationImportBatch DocumentationImportBatch { get; set; } = null !;
    public DocumentationSource DocumentationSource { get; set; } = null !;
    public DocumentationSubject DocumentationSubject { get; set; } = null !;
    public DocumentationValueType DocumentationValueType { get; set; } = null !;
}

public sealed partial class DocumentationFactType
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string Name { get; set; } = null !;
}

public sealed partial class DocumentationImportBatch
{
    public string Id { get; set; } = null !;
    public string ImportedAt { get; set; } = null !;
    public string ImporterId { get; set; } = null !;
    public string ImporterVersion { get; set; } = null !;
    public string? SourceFingerprint { get; set; }
    public string Status { get; set; } = null !;
    public DocumentationSource DocumentationSource { get; set; } = null !;
}

public sealed partial class DocumentationInstanceImportSpec
{
    public string Id { get; set; } = null !;
    public string IncludeInstances { get; set; } = null !;
    public string Name { get; set; } = null !;
    public string SafetyStatus { get; set; } = null !;
    public DocumentationSource? DocumentationSource { get; set; }
}

public sealed partial class DocumentationLayout
{
    public string Id { get; set; } = null !;
    public string Name { get; set; } = null !;
    public DocumentationLayoutType DocumentationLayoutType { get; set; } = null !;
    public DocumentationTheme DocumentationTheme { get; set; } = null !;
    public DocumentationLayout? PreviousLayout { get; set; }
}

public sealed partial class DocumentationLayoutType
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string Name { get; set; } = null !;
}

public sealed partial class DocumentationNarrative
{
    public string Id { get; set; } = null !;
    public string? Body { get; set; }
    public string? LastReviewedImportBatchId { get; set; }
    public string Origin { get; set; } = null !;
    public string ReviewStatus { get; set; } = null !;
    public string Slot { get; set; } = null !;
    public string? Title { get; set; }
    public DocumentationSubject DocumentationSubject { get; set; } = null !;
    public DocumentationNarrative? PreviousNarrative { get; set; }
}

public sealed partial class DocumentationPropertyImportSpec
{
    public string Id { get; set; } = null !;
    public string Include { get; set; } = null !;
    public string PropertyName { get; set; } = null !;
    public string ReviewStatus { get; set; } = null !;
    public DocumentationEntityImportSpec DocumentationEntityImportSpec { get; set; } = null !;
}

public sealed partial class DocumentationRelationship
{
    public string Id { get; set; } = null !;
    public DocumentationImportBatch DocumentationImportBatch { get; set; } = null !;
    public DocumentationRelationshipType DocumentationRelationshipType { get; set; } = null !;
    public DocumentationSource DocumentationSource { get; set; } = null !;
    public DocumentationSubject FromSubject { get; set; } = null !;
    public DocumentationRelationship? PreviousRelationship { get; set; }
    public DocumentationSubject ToSubject { get; set; } = null !;
}

public sealed partial class DocumentationRelationshipImportSpec
{
    public string Id { get; set; } = null !;
    public string Include { get; set; } = null !;
    public string RelationshipSelector { get; set; } = null !;
    public string ReviewStatus { get; set; } = null !;
    public DocumentationEntityImportSpec DocumentationEntityImportSpec { get; set; } = null !;
}

public sealed partial class DocumentationRelationshipType
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string Name { get; set; } = null !;
}

public sealed partial class DocumentationSource
{
    public string Id { get; set; } = null !;
    public string DisplayName { get; set; } = null !;
    public string? ImportedAt { get; set; }
    public string? ImporterId { get; set; }
    public string? Locator { get; set; }
    public string? SourceFingerprint { get; set; }
    public string Status { get; set; } = null !;
    public DocumentationSourceType DocumentationSourceType { get; set; } = null !;
    public DocumentationWorkspace? DocumentationWorkspace { get; set; }
}

public sealed partial class DocumentationSourceType
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string Name { get; set; } = null !;
}

public sealed partial class DocumentationSubject
{
    public string Id { get; set; } = null !;
    public string DisplayName { get; set; } = null !;
    public string? DisplayPath { get; set; }
    public string? NativeId { get; set; }
    public string? SourceTypeName { get; set; }
    public string Status { get; set; } = null !;
    public string? Summary { get; set; }
    public DocumentationSource DocumentationSource { get; set; } = null !;
    public DocumentationSubjectType DocumentationSubjectType { get; set; } = null !;
    public DocumentationSubject? ParentSubject { get; set; }
    public DocumentationSubject? PreviousSubject { get; set; }
}

public sealed partial class DocumentationSubjectAlias
{
    public string Id { get; set; } = null !;
    public string Alias { get; set; } = null !;
    public string? Reason { get; set; }
    public DocumentationSubject DocumentationSubject { get; set; } = null !;
}

public sealed partial class DocumentationSubjectType
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string Name { get; set; } = null !;
}

public sealed partial class DocumentationTemplate
{
    public string Id { get; set; } = null !;
    public string? Html { get; set; }
    public string Name { get; set; } = null !;
    public string? SourceUrl { get; set; }
    public DocumentationTemplateType DocumentationTemplateType { get; set; } = null !;
    public DocumentationTheme DocumentationTheme { get; set; } = null !;
    public DocumentationTemplate? PreviousTemplate { get; set; }
}

public sealed partial class DocumentationTemplateRegion
{
    public string Id { get; set; } = null !;
    public string Name { get; set; } = null !;
    public DocumentationTemplate DocumentationTemplate { get; set; } = null !;
    public DocumentationTemplateRegionType DocumentationTemplateRegionType { get; set; } = null !;
    public DocumentationTemplateRegion? PreviousRegion { get; set; }
}

public sealed partial class DocumentationTemplateRegionType
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string Name { get; set; } = null !;
}

public sealed partial class DocumentationTemplateType
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string Name { get; set; } = null !;
}

public sealed partial class DocumentationTheme
{
    public string Id { get; set; } = null !;
    public string Name { get; set; } = null !;
    public string? RenderOptions { get; set; }
    public string? Version { get; set; }
}

public sealed partial class DocumentationThemeAsset
{
    public string Id { get; set; } = null !;
    public string? Content { get; set; }
    public string? Hash { get; set; }
    public string? Href { get; set; }
    public string? MediaType { get; set; }
    public string Name { get; set; } = null !;
    public DocumentationThemeAssetType DocumentationThemeAssetType { get; set; } = null !;
    public DocumentationTheme DocumentationTheme { get; set; } = null !;
    public DocumentationThemeAsset? PreviousAsset { get; set; }
}

public sealed partial class DocumentationThemeAssetType
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string Name { get; set; } = null !;
}

public sealed partial class DocumentationValueType
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string Name { get; set; } = null !;
}

public sealed partial class DocumentationView
{
    public string Id { get; set; } = null !;
    public string Name { get; set; } = null !;
    public string? Summary { get; set; }
    public string? Title { get; set; }
    public DocumentationViewType DocumentationViewType { get; set; } = null !;
    public DocumentationSubject? RootSubject { get; set; }
}

public sealed partial class DocumentationViewNode
{
    public string Id { get; set; } = null !;
    public string? ParentNodeId { get; set; }
    public string? Selection { get; set; }
    public string Title { get; set; } = null !;
    public DocumentationSubject? DocumentationSubject { get; set; }
    public DocumentationView DocumentationView { get; set; } = null !;
    public DocumentationViewNode? PreviousNode { get; set; }
}

public sealed partial class DocumentationViewType
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string Name { get; set; } = null !;
}

public sealed partial class DocumentationWorkspace
{
    public string Id { get; set; } = null !;
    public string Name { get; set; } = null !;
    public string? Summary { get; set; }
    public DocumentationWorkspaceType DocumentationWorkspaceType { get; set; } = null !;
}

public sealed partial class DocumentationWorkspaceType
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string Name { get; set; } = null !;
}

public sealed partial class MetaDocsModel
{
    public static MetaDocsModel CreateEmpty() => new();
    public List<DocumentationComponentTemplate> DocumentationComponentTemplateList { get; set; } = new();
    public List<DocumentationComponentTemplateType> DocumentationComponentTemplateTypeList { get; set; } = new();
    public List<DocumentationEntityImportSpec> DocumentationEntityImportSpecList { get; set; } = new();
    public List<DocumentationFact> DocumentationFactList { get; set; } = new();
    public List<DocumentationFactType> DocumentationFactTypeList { get; set; } = new();
    public List<DocumentationImportBatch> DocumentationImportBatchList { get; set; } = new();
    public List<DocumentationInstanceImportSpec> DocumentationInstanceImportSpecList { get; set; } = new();
    public List<DocumentationLayout> DocumentationLayoutList { get; set; } = new();
    public List<DocumentationLayoutType> DocumentationLayoutTypeList { get; set; } = new();
    public List<DocumentationNarrative> DocumentationNarrativeList { get; set; } = new();
    public List<DocumentationPropertyImportSpec> DocumentationPropertyImportSpecList { get; set; } = new();
    public List<DocumentationRelationship> DocumentationRelationshipList { get; set; } = new();
    public List<DocumentationRelationshipImportSpec> DocumentationRelationshipImportSpecList { get; set; } = new();
    public List<DocumentationRelationshipType> DocumentationRelationshipTypeList { get; set; } = new();
    public List<DocumentationSource> DocumentationSourceList { get; set; } = new();
    public List<DocumentationSourceType> DocumentationSourceTypeList { get; set; } = new();
    public List<DocumentationSubject> DocumentationSubjectList { get; set; } = new();
    public List<DocumentationSubjectAlias> DocumentationSubjectAliasList { get; set; } = new();
    public List<DocumentationSubjectType> DocumentationSubjectTypeList { get; set; } = new();
    public List<DocumentationTemplate> DocumentationTemplateList { get; set; } = new();
    public List<DocumentationTemplateRegion> DocumentationTemplateRegionList { get; set; } = new();
    public List<DocumentationTemplateRegionType> DocumentationTemplateRegionTypeList { get; set; } = new();
    public List<DocumentationTemplateType> DocumentationTemplateTypeList { get; set; } = new();
    public List<DocumentationTheme> DocumentationThemeList { get; set; } = new();
    public List<DocumentationThemeAsset> DocumentationThemeAssetList { get; set; } = new();
    public List<DocumentationThemeAssetType> DocumentationThemeAssetTypeList { get; set; } = new();
    public List<DocumentationValueType> DocumentationValueTypeList { get; set; } = new();
    public List<DocumentationView> DocumentationViewList { get; set; } = new();
    public List<DocumentationViewNode> DocumentationViewNodeList { get; set; } = new();
    public List<DocumentationViewType> DocumentationViewTypeList { get; set; } = new();
    public List<DocumentationWorkspace> DocumentationWorkspaceList { get; set; } = new();
    public List<DocumentationWorkspaceType> DocumentationWorkspaceTypeList { get; set; } = new();
}

public static partial class MetaDocsInstance
{
    private static readonly MetaDocsModel _builtIn = CreateBuiltIn();
    public static MetaDocsModel BuiltIn => _builtIn;

    public static MetaDocsModel CreateBuiltIn()
    {
        var model = MetaDocsModel.CreateEmpty();
        return model;
    }
}