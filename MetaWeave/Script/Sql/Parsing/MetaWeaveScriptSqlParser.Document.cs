using static MetaWeaveScript.Sql.Parsing.MetaWeaveScriptSqlModelBuilder;

namespace MetaWeaveScript.Sql.Parsing;

public sealed partial class MetaWeaveScriptSqlParser
{
    private sealed partial class Parser
    {
        public BuiltNode ParseDocument()
        {
            if (PeekKeyword("CREATE"))
            {
                throw Unsupported("MetaWeaveScript accepts a bare SELECT document; CREATE wrappers are not part of the language.");
            }

            var selectStatement = ParseSelectStatement();
            SkipSemicolons();
            ExpectEndOfFile();
            return selectStatement;
        }

        private BuiltNode ParseSelectStatement()
        {
            var commonTableExpressions = ParseStatementPrefix();
            var queryExpression = ParseQueryExpression();
            return builder.CreateSelectStatement(queryExpression, commonTableExpressions);
        }

        private IReadOnlyList<BuiltNode>? ParseStatementPrefix()
        {
            return MatchKeyword("WITH")
                ? ParseCommonTableExpressions()
                : null;
        }

        private List<BuiltNode> ParseCommonTableExpressions()
        {
            var commonTableExpressions = new List<BuiltNode> { ParseCommonTableExpression() };
            while (Match(MetaWeaveScriptSqlTokenKind.Comma))
            {
                commonTableExpressions.Add(ParseCommonTableExpression());
            }

            return commonTableExpressions;
        }

        private BuiltNode ParseCommonTableExpression()
        {
            var expressionName = ParseIdentifier().Node;
            if (Current.Kind == MetaWeaveScriptSqlTokenKind.OpenParen)
            {
                throw Unsupported("Common-table-expression column lists are not part of MetaWeaveScript.");
            }

            ExpectKeyword("AS");
            Expect(MetaWeaveScriptSqlTokenKind.OpenParen);
            var queryExpression = ParseQueryExpression();
            Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
            return builder.CreateCommonTableExpression(expressionName, queryExpression);
        }
    }
}
