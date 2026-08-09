#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationViewNode
    {
        public string Id { get; set; } = string.Empty;

        public string? ParentNodeId { get; set; }

        public string? Selection { get; set; }

        public string Title { get; set; } = string.Empty;

        public DocumentationSubject? DocumentationSubject { get; set; }

        public DocumentationView DocumentationView { get; set; } = null!;

        public DocumentationViewNode? PreviousNode { get; set; }

    }
}
