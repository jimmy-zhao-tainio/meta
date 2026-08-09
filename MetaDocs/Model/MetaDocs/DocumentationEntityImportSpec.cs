#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationEntityImportSpec
    {
        public string Id { get; set; } = string.Empty;

        public string? DisplayNameProperty { get; set; }

        public string EntityName { get; set; } = string.Empty;

        public string IncludeInstances { get; set; } = string.Empty;

        public string ReviewStatus { get; set; } = string.Empty;

        public string? SummaryProperty { get; set; }

        public DocumentationInstanceImportSpec DocumentationInstanceImportSpec { get; set; } = null!;

    }
}
