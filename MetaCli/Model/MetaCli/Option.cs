#nullable enable

namespace MetaCli
{
    public sealed class Option
    {
        public string Id { get; set; } = string.Empty;

        public Parameter Parameter { get; set; } = null!;

    }
}
