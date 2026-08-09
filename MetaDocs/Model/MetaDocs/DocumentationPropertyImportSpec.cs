#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationPropertyImportSpec
    {
        public string Id { get; set; } = string.Empty;

        public string Include { get; set; } = string.Empty;

        public string PropertyName { get; set; } = string.Empty;

        public string ReviewStatus { get; set; } = string.Empty;

        public DocumentationEntityImportSpec DocumentationEntityImportSpec { get; set; } = null!;

    }
}
