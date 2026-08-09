#nullable enable

namespace MetaCli
{
    public sealed class Parameter
    {
        public string Id { get; set; } = string.Empty;

        public string? DefaultValue { get; set; }

        public string? Description { get; set; }

        public string? IsRepeatable { get; set; }

        public string? IsRequired { get; set; }

        public string Name { get; set; } = string.Empty;

        public ValueShape ValueShape { get; set; } = null!;

    }
}
