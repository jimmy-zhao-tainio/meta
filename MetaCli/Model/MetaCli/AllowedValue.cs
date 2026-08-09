#nullable enable

namespace MetaCli
{
    public sealed class AllowedValue
    {
        public string Id { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string Value { get; set; } = string.Empty;

        public ValueShape ValueShape { get; set; } = null!;

    }
}
