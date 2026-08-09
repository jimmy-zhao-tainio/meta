#nullable enable

namespace MetaCli
{
    public sealed class ParameterGroup
    {
        public string Id { get; set; } = string.Empty;

        public string? AllowsMultiple { get; set; }

        public string? Description { get; set; }

        public string? IsRequired { get; set; }

        public string Name { get; set; } = string.Empty;

        public ExecutableCommand ExecutableCommand { get; set; } = null!;

    }
}
