#nullable enable

namespace MetaMesh
{
    public sealed class SqlWorkspace
    {
        public string Id { get; set; } = string.Empty;

        public string ConnectionEnvironmentVariable { get; set; } = string.Empty;

        public Workspace Workspace { get; set; } = null!;

    }
}
