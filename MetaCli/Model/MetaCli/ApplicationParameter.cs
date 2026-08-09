#nullable enable

namespace MetaCli
{
    public sealed class ApplicationParameter
    {
        public string Id { get; set; } = string.Empty;

        public Application Application { get; set; } = null!;

        public Parameter Parameter { get; set; } = null!;

    }
}
