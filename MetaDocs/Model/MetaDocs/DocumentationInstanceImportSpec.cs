#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationInstanceImportSpec
    {
        public string Id { get; set; } = string.Empty;

        public string IncludeInstances { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string SafetyStatus { get; set; } = string.Empty;

        public DocumentationSource? DocumentationSource { get; set; }

    }
}
