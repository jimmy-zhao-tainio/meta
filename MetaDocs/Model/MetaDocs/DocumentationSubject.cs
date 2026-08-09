#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationSubject
    {
        public string Id { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string? DisplayPath { get; set; }

        public string? NativeId { get; set; }

        public string? SourceTypeName { get; set; }

        public string Status { get; set; } = string.Empty;

        public string? Summary { get; set; }

        public DocumentationSource DocumentationSource { get; set; } = null!;

        public DocumentationSubjectType DocumentationSubjectType { get; set; } = null!;

        public DocumentationSubject? ParentSubject { get; set; }

        public DocumentationSubject? PreviousSubject { get; set; }

    }
}
