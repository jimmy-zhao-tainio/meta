namespace Meta.Operations;

public sealed class MetaOperationException : InvalidOperationException
{
    public MetaOperationException(
        int operationIndex,
        Operation operation,
        Exception cause)
        : this(operationIndex, operation, cause, diagnostics: null)
    {
    }

    public MetaOperationException(
        int operationIndex,
        Operation operation,
        Exception cause,
        WorkspaceDiagnostics? diagnostics)
        : base(
            $"Operation {operationIndex + 1} ({operation.GetType().Name}) failed. {cause.Message}",
            cause)
    {
        OperationIndex = operationIndex;
        Operation = operation;
        Diagnostics = diagnostics;
    }

    public int OperationIndex { get; }
    public Operation Operation { get; }
    public WorkspaceDiagnostics? Diagnostics { get; }
}
