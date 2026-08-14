using MetaWeave;
using static MetaWeaveScript.Sql.Parsing.MetaWeaveScriptSqlModelBuilder;

namespace MetaWeaveScript.Sql.Parsing;

public sealed partial class MetaWeaveScriptSqlParser
{
    public MetaWeaveModel ParseSqlCode(
        string sqlCode,
        string? sourcePath = null,
        string? bareSelectName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlCode);

        var tokens = new MetaWeaveScriptSqlLexer(sqlCode).Tokenize();
        var builder = new MetaWeaveScriptSqlModelBuilder();
        new Parser(sqlCode, tokens, builder).ParseDocument();
        return builder.Build();
    }

    public SelectStatement ParseSqlCodeIntoModel(
        string sqlCode,
        MetaWeaveModel model,
        string? sourcePath = null,
        string? bareSelectName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlCode);
        ArgumentNullException.ThrowIfNull(model);

        var tokens = new MetaWeaveScriptSqlLexer(sqlCode).Tokenize();
        var builder = new MetaWeaveScriptSqlModelBuilder(model);
        return new Parser(sqlCode, tokens, builder)
            .ParseDocument()
            .GetRef<SelectStatement>(nameof(SelectStatement));
    }

    private sealed partial class Parser
    {
        private static readonly HashSet<string> ClauseKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "AS",
            "FROM",
            "WHERE",
            "GROUP",
            "HAVING",
            "WINDOW",
            "ORDER",
            "OFFSET",
            "FETCH",
            "WITH",
            "UNION",
            "EXCEPT",
            "INTERSECT",
            "JOIN",
            "INNER",
            "LEFT",
            "RIGHT",
            "FULL",
            "CROSS",
            "OUTER",
            "APPLY",
            "PIVOT",
            "UNPIVOT",
            "ON",
            "VALUES",
            "TABLESAMPLE",
            "BY",
            "OPTION",
            "TOP",
            "PERCENT",
            "GO"
        };

        private readonly IReadOnlyList<MetaWeaveScriptSqlToken> tokens;
        private readonly MetaWeaveScriptSqlModelBuilder builder;
        private readonly string sqlCode;
        private int position;

        public Parser(
            string sqlCode,
            IReadOnlyList<MetaWeaveScriptSqlToken> tokens,
            MetaWeaveScriptSqlModelBuilder builder)
        {
            this.sqlCode = sqlCode;
            this.tokens = tokens;
            this.builder = builder;
        }
    }
}
