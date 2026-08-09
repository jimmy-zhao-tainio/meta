#nullable enable

namespace MetaCli
{
    public sealed class ExecutableCommandParameter
    {
        public string Id { get; set; } = string.Empty;

        public ExecutableCommand ExecutableCommand { get; set; } = null!;

        public Parameter Parameter { get; set; } = null!;

    }
}
