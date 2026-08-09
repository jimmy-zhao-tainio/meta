#nullable enable

namespace MetaCli
{
    public sealed class ApplicationDefaultCommand
    {
        public string Id { get; set; } = string.Empty;

        public Application Application { get; set; } = null!;

        public ExecutableCommand ExecutableCommand { get; set; } = null!;

    }
}
