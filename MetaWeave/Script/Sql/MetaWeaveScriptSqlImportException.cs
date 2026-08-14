namespace MetaWeaveScript.Sql;

public enum MetaWeaveScriptSqlImportFailureKind
{
    ParseFailed,
    UnsupportedSql,
    UnsupportedFunctionWrapper
}

public sealed class MetaWeaveScriptSqlImportException : InvalidOperationException
{
    public MetaWeaveScriptSqlImportException(
        MetaWeaveScriptSqlImportFailureKind kind,
        string message,
        Exception? innerException = null,
        int? line = null,
        int? column = null,
        int? offset = null)
        : base(message, innerException)
    {
        Kind = kind;
        Line = line;
        Column = column;
        Offset = offset;
    }

    public MetaWeaveScriptSqlImportFailureKind Kind { get; }

    public int? Line { get; }

    public int? Column { get; }

    public int? Offset { get; }
}
