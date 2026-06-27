namespace MetaCli.Core;

public sealed class MetaCliExitException : Exception
{
    public MetaCliExitException(int exitCode, string message = "")
        : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
