#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationSubjectAlias
    {
        public string Id { get; set; } = string.Empty;

        public string Alias { get; set; } = string.Empty;

        public string? Reason { get; set; }

        public DocumentationSubject DocumentationSubject { get; set; } = null!;

    }
}
