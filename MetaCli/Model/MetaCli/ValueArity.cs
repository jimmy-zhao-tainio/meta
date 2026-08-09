#nullable enable

namespace MetaCli
{
    public sealed class ValueArity
    {
        public string Id { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? MaxValueCount { get; set; }

        public string MinValueCount { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

    }
}
