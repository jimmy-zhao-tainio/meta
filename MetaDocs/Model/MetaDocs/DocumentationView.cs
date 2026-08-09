#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationView
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? Summary { get; set; }

        public string? Title { get; set; }

        public DocumentationViewType DocumentationViewType { get; set; } = null!;

        public DocumentationSubject? RootSubject { get; set; }

    }
}
