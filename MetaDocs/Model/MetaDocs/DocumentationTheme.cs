#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationTheme
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? RenderOptions { get; set; }

        public string? Version { get; set; }

    }
}
