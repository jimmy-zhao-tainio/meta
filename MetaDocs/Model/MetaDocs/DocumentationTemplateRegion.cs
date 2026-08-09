#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationTemplateRegion
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public DocumentationTemplate DocumentationTemplate { get; set; } = null!;

        public DocumentationTemplateRegionType DocumentationTemplateRegionType { get; set; } = null!;

        public DocumentationTemplateRegion? PreviousRegion { get; set; }

    }
}
