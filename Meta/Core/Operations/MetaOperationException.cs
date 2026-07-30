using Meta.Core.Domain;

namespace Meta.Core.Operations;

public sealed class MetaOperationException : InvalidOperationException
{
    public MetaOperationException(
        string message,
        int operationIndex = -1,
        MetaOperation? operation = null,
        Exception? innerException = null,
        WorkspaceDiagnostics? diagnostics = null)
        : base(message, innerException)
    {
        OperationIndex = operationIndex;
        Operation = operation;
        Diagnostics = diagnostics;
    }

    public int OperationIndex { get; }
    public MetaOperation? Operation { get; }
    public WorkspaceDiagnostics? Diagnostics { get; }
}
