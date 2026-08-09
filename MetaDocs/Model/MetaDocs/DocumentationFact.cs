#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationFact
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? SourceFingerprint { get; set; }

        public string Status { get; set; } = string.Empty;

        public string? Value { get; set; }

        public DocumentationFactType DocumentationFactType { get; set; } = null!;

        public DocumentationImportBatch DocumentationImportBatch { get; set; } = null!;

        public DocumentationSource DocumentationSource { get; set; } = null!;

        public DocumentationSubject DocumentationSubject { get; set; } = null!;

        public DocumentationValueType DocumentationValueType { get; set; } = null!;

    }
}
