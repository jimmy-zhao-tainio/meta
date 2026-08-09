#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationTemplate
    {
        public string Id { get; set; } = string.Empty;

        public string? Html { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? SourceUrl { get; set; }

        public DocumentationTemplateType DocumentationTemplateType { get; set; } = null!;

        public DocumentationTheme DocumentationTheme { get; set; } = null!;

        public DocumentationTemplate? PreviousTemplate { get; set; }

    }
}
