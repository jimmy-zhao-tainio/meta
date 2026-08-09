#nullable enable

namespace MetaMesh
{
    public sealed class Operation
    {
        public string Id { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string Name { get; set; } = string.Empty;

        public Mesh Mesh { get; set; } = null!;

    }
}
