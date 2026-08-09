#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationImportBatch
    {
        public string Id { get; set; } = string.Empty;

        public string ImportedAt { get; set; } = string.Empty;

        public string ImporterId { get; set; } = string.Empty;

        public string ImporterVersion { get; set; } = string.Empty;

        public string? SourceFingerprint { get; set; }

        public string Status { get; set; } = string.Empty;

        public DocumentationSource DocumentationSource { get; set; } = null!;

    }
}
