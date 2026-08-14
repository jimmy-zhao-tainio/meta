using MetaWeave;
using MetaWeaveScript.Sql.Parsing;

namespace MetaWeaveScript.Sql;

public sealed class MetaWeaveScriptSqlService
{
    public MetaWeaveModel ImportFromSqlCode(string sqlCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlCode);

        try
        {
            return new MetaWeaveScriptSqlParser().ParseSqlCode(sqlCode);
        }
        catch (MetaWeaveScriptSqlParserException ex)
        {
            throw CreateImportException(ex);
        }
    }

    public SelectStatement ImportIntoModel(
        MetaWeaveModel model,
        string sqlCode)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlCode);

        try
        {
            return new MetaWeaveScriptSqlParser().ParseSqlCodeIntoModel(sqlCode, model);
        }
        catch (MetaWeaveScriptSqlParserException ex)
        {
            throw CreateImportException(ex);
        }
    }

    public string ExportToSqlCode(MetaWeaveModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var selectStatement = model.SelectStatementList.Count switch
        {
            1 => model.SelectStatementList[0],
            _ => throw new InvalidOperationException(
                $"Expected exactly one WeaveScript SELECT document, but found {model.SelectStatementList.Count}.")
        };

        return ExportToSqlCode(model, selectStatement);
    }

    public string ExportToSqlCode(
        MetaWeaveModel model,
        SelectStatement selectStatement)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(selectStatement);
        return new MetaWeaveScriptSqlEmitter(model).Render(selectStatement);
    }

    private static MetaWeaveScriptSqlImportException CreateImportException(
        MetaWeaveScriptSqlParserException exception) =>
        new(
            exception.FailureKind switch
            {
                MetaWeaveScriptSqlParserFailureKind.UnsupportedFunctionWrapper => MetaWeaveScriptSqlImportFailureKind.UnsupportedFunctionWrapper,
                MetaWeaveScriptSqlParserFailureKind.UnsupportedSyntax => MetaWeaveScriptSqlImportFailureKind.UnsupportedSql,
                _ => MetaWeaveScriptSqlImportFailureKind.ParseFailed
            },
            exception.Message,
            exception,
            exception.Line,
            exception.Column,
            exception.Offset);
}
