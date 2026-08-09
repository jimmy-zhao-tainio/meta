#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationNarrative
    {
        public string Id { get; set; } = string.Empty;

        public string? Body { get; set; }

        public string? LastReviewedImportBatchId { get; set; }

        public string Origin { get; set; } = string.Empty;

        public string ReviewStatus { get; set; } = string.Empty;

        public string Slot { get; set; } = string.Empty;

        public string? Title { get; set; }

        public DocumentationSubject DocumentationSubject { get; set; } = null!;

        public DocumentationNarrative? PreviousNarrative { get; set; }

    }
}
