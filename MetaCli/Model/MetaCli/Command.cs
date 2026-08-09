#nullable enable

namespace MetaCli
{
    public sealed class Command
    {
        public string Id { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Token { get; set; }

        public Application Application { get; set; } = null!;

        public Command? ParentCommand { get; set; }

    }
}
