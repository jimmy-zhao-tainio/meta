#nullable enable

namespace MetaCli
{
    public sealed class Application
    {
        public string Id { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? ExecutableName { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Version { get; set; }

    }
}
