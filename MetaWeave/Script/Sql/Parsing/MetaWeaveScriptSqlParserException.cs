namespace MetaWeaveScript.Sql.Parsing;

public enum MetaWeaveScriptSqlParserFailureKind
{
    ParseError,
    UnsupportedSyntax,
    UnsupportedFunctionWrapper
}

public sealed class MetaWeaveScriptSqlParserException : Exception
{
    public MetaWeaveScriptSqlParserException(
        MetaWeaveScriptSqlParserFailureKind failureKind,
        string message,
        int line,
        int column,
        int offset)
        : base($"{message} (line {line}, column {column})")
    {
        FailureKind = failureKind;
        Line = line;
        Column = column;
        Offset = offset;
    }

    public MetaWeaveScriptSqlParserFailureKind FailureKind { get; }

    public int Line { get; }

    public int Column { get; }

    public int Offset { get; }
}
