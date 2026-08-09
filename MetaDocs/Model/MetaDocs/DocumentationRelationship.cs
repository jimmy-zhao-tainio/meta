#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationRelationship
    {
        public string Id { get; set; } = string.Empty;

        public DocumentationImportBatch DocumentationImportBatch { get; set; } = null!;

        public DocumentationRelationshipType DocumentationRelationshipType { get; set; } = null!;

        public DocumentationSource DocumentationSource { get; set; } = null!;

        public DocumentationSubject FromSubject { get; set; } = null!;

        public DocumentationRelationship? PreviousRelationship { get; set; }

        public DocumentationSubject ToSubject { get; set; } = null!;

    }
}
