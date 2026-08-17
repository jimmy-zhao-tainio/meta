using static MetaWeaveScript.Sql.Parsing.MetaWeaveScriptSqlModelBuilder;

namespace MetaWeaveScript.Sql.Parsing;

public sealed partial class MetaWeaveScriptSqlParser
{
    private sealed partial class Parser
    {
        private BuiltNode ParseOverClause()
        {
            ExpectKeyword("OVER");
            Expect(MetaWeaveScriptSqlTokenKind.OpenParen);

            List<BuiltNode>? partitions = null;
            if (MatchKeyword("PARTITION"))
            {
                ExpectKeyword("BY");
                partitions = [ParseScalarExpression()];
                while (Match(MetaWeaveScriptSqlTokenKind.Comma))
                {
                    partitions.Add(ParseScalarExpression());
                }
            }

            if (!MatchKeyword("ORDER"))
            {
                throw Unsupported("MetaWeaveScript ROW_NUMBER requires an OVER clause with ORDER BY.");
            }

            ExpectKeyword("BY");
            var orderByClause = ParseOrderByClause();
            if (PeekKeyword("ROWS") || PeekKeyword("RANGE"))
            {
                throw Unsupported("MetaWeaveScript ROW_NUMBER does not support window frames.");
            }

            Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
            return builder.CreateOverClause(partitions, orderByClause);
        }
    }
}
