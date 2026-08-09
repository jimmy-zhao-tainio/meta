#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationRelationshipImportSpec
    {
        public string Id { get; set; } = string.Empty;

        public string Include { get; set; } = string.Empty;

        public string RelationshipSelector { get; set; } = string.Empty;

        public string ReviewStatus { get; set; } = string.Empty;

        public DocumentationEntityImportSpec DocumentationEntityImportSpec { get; set; } = null!;

    }
}
