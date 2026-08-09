#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationLayout
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public DocumentationLayoutType DocumentationLayoutType { get; set; } = null!;

        public DocumentationTheme DocumentationTheme { get; set; } = null!;

        public DocumentationLayout? PreviousLayout { get; set; }

    }
}
