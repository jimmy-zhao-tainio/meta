#nullable enable

namespace MetaCli
{
    public sealed class ValueShape
    {
        public string Id { get; set; } = string.Empty;

        public string? AllowsOptionLikeValue { get; set; }

        public string? Description { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? ValueLabel { get; set; }

        public ValueArity ValueArity { get; set; } = null!;

    }
}
