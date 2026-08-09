#nullable enable

namespace MetaMesh
{
    public sealed class OperationStep
    {
        public string Id { get; set; } = string.Empty;

        public string? Arguments { get; set; }

        public string? Description { get; set; }

        public string Executable { get; set; } = string.Empty;

        public string? ExpectedExitCode { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? WorkingDirectory { get; set; }

        public Operation Operation { get; set; } = null!;

        public OperationStep? PreviousStep { get; set; }

    }
}
