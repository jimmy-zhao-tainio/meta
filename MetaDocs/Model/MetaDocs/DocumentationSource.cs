#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationSource
    {
        public string Id { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string? ImportedAt { get; set; }

        public string? ImporterId { get; set; }

        public string? Locator { get; set; }

        public string? SourceFingerprint { get; set; }

        public string Status { get; set; } = string.Empty;

        public DocumentationSourceType DocumentationSourceType { get; set; } = null!;

        public DocumentationWorkspace? DocumentationWorkspace { get; set; }

    }
}
