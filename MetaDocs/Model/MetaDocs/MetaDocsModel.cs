#nullable enable

using System.Collections.Generic;

namespace MetaDocs
{
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
}
