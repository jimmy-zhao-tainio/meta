#nullable enable

namespace MetaDocs
{
    public sealed class DocumentationWorkspace
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? Summary { get; set; }

        public DocumentationWorkspaceType DocumentationWorkspaceType { get; set; } = null!;

    }
}
