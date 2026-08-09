#nullable enable

namespace MetaMesh
{
    public sealed class CSharpWorkspace
    {
        public string Id { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public Workspace Workspace { get; set; } = null!;

    }
}
