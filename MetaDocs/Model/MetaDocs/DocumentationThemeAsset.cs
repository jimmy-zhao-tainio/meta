#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationThemeAsset
    {
        public string Id { get; set; } = string.Empty;

        public string? Content { get; set; }

        public string? Hash { get; set; }

        public string? Href { get; set; }

        public string? MediaType { get; set; }

        public string Name { get; set; } = string.Empty;

        public DocumentationThemeAssetType DocumentationThemeAssetType { get; set; } = null!;

        public DocumentationTheme DocumentationTheme { get; set; } = null!;

        public DocumentationThemeAsset? PreviousAsset { get; set; }

    }
}
