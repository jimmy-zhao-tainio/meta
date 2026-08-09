#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationComponentTemplate
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? TemplateText { get; set; }

        public DocumentationComponentTemplateType DocumentationComponentTemplateType { get; set; } = null!;

        public DocumentationTheme DocumentationTheme { get; set; } = null!;

        public DocumentationComponentTemplate? PreviousComponent { get; set; }

    }
}
