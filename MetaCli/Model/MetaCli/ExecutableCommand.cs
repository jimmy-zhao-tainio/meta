#nullable enable

namespace MetaCli
{
    public sealed class ExecutableCommand
    {
        public string Id { get; set; } = string.Empty;

        public Command Command { get; set; } = null!;

    }
}
