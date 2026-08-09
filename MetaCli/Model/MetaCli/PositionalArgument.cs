#nullable enable

namespace MetaCli
{
    public sealed class PositionalArgument
    {
        public string Id { get; set; } = string.Empty;

        public Parameter Parameter { get; set; } = null!;

        public PositionalArgument? PreviousArgument { get; set; }

    }
}
