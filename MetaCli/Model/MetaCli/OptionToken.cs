#nullable enable

namespace MetaCli
{
    public sealed class OptionToken
    {
        public string Id { get; set; } = string.Empty;

        public string Token { get; set; } = string.Empty;

        public Option Option { get; set; } = null!;

        public OptionToken? PreviousToken { get; set; }

    }
}
