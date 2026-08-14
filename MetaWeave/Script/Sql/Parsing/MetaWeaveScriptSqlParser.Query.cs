using static MetaWeaveScript.Sql.Parsing.MetaWeaveScriptSqlModelBuilder;

namespace MetaWeaveScript.Sql.Parsing;

public sealed partial class MetaWeaveScriptSqlParser
{
    private sealed partial class Parser
    {
        private BuiltNode ParseQueryExpression()
        {
            var currentExpression = ParseQueryExpressionPrimary();
            while (MatchKeyword("UNION"))
            {
                if (!MatchKeyword("ALL"))
                {
                    throw Unsupported("MetaWeaveScript supports UNION ALL, not duplicate-eliminating UNION.");
                }
                currentExpression = builder.CreateBinaryQueryExpression(currentExpression, ParseQueryExpressionPrimary(), "Union", all: true);
            }
            if (PeekKeyword("EXCEPT") || PeekKeyword("INTERSECT"))
            {
                throw Unsupported("MetaWeaveScript supports UNION ALL but not EXCEPT or INTERSECT.");
            }
            if (PeekKeyword("ORDER") || PeekKeyword("OFFSET"))
            {
                throw Unsupported("MetaWeaveScript does not support query-level ORDER BY or OFFSET.");
            }
            return currentExpression;
        }

        private BuiltNode ParseQueryExpressionPrimary()
        {
            if (Current.Kind == MetaWeaveScriptSqlTokenKind.OpenParen)
            {
                return ParseQueryParenthesisExpression();
            }
            return ParseQuerySpecification();
        }

        private BuiltNode ParseQueryParenthesisExpression()
        {
            Expect(MetaWeaveScriptSqlTokenKind.OpenParen);
            var queryExpression = ParseQueryExpression();
            Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
            return builder.CreateQueryParenthesisExpression(queryExpression);
        }

        private BuiltNode ParseQuerySpecification()
        {
            ExpectKeyword("SELECT");
            var uniqueRowFilter = MatchKeyword("DISTINCT") ? "Distinct" : string.Empty;
            if (PeekKeyword("TOP"))
            {
                throw Unsupported("MetaWeaveScript does not support TOP.");
            }

            var selectElements = new List<BuiltNode> { ParseSelectElement() };
            while (Match(MetaWeaveScriptSqlTokenKind.Comma))
            {
                selectElements.Add(ParseSelectElement());
            }
            if (PeekKeyword("AS"))
            {
                throw ParseError("Unexpected AS after projection list while parsing projection alias. Check the preceding projection expression.");
            }

            BuiltNode? fromClause = null;
            if (MatchKeyword("FROM"))
            {
                fromClause = ParseFromClause();
            }
            BuiltNode? whereClause = null;
            if (MatchKeyword("WHERE"))
            {
                whereClause = builder.CreateWhereClause(ParseBooleanExpression());
            }
            BuiltNode? groupByClause = null;
            if (MatchKeyword("GROUP"))
            {
                ExpectKeyword("BY");
                groupByClause = ParseGroupByClause();
            }
            if (PeekKeyword("HAVING") || PeekKeyword("WINDOW"))
            {
                throw Unsupported("MetaWeaveScript does not support HAVING or WINDOW.");
            }
            return builder.CreateQuerySpecification(
                selectElements,
                fromClause,
                whereClause,
                groupByClause,
                uniqueRowFilter: uniqueRowFilter);
        }

        private BuiltNode ParseOrderByClause()
        {
            var orderByElements = new List<BuiltNode> { ParseExpressionWithSortOrder() };
            while (Match(MetaWeaveScriptSqlTokenKind.Comma))
            {
                orderByElements.Add(ParseExpressionWithSortOrder());
            }
            return builder.CreateOrderByClause(orderByElements);
        }

        private BuiltNode ParseExpressionWithSortOrder()
        {
            var expression = ParseScalarExpression();
            var sortOrder =
                MatchKeyword("DESC") ? "Descending" :
                MatchKeyword("ASC") ? "Ascending" :
                "NotSpecified";
            return builder.CreateExpressionWithSortOrder(expression, sortOrder);
        }

        private BuiltNode ParseGroupByClause()
        {
            if (PeekKeyword("ALL") || PeekKeyword("GROUPING") || PeekKeyword("ROLLUP") || PeekKeyword("CUBE") || Current.Kind == MetaWeaveScriptSqlTokenKind.OpenParen)
            {
                throw Unsupported("MetaWeaveScript GROUP BY supports expression lists only.");
            }
            var groupingSpecifications = new List<BuiltNode>
            {
                builder.CreateExpressionGroupingSpecification(ParseScalarExpression())
            };
            while (Match(MetaWeaveScriptSqlTokenKind.Comma))
            {
                groupingSpecifications.Add(builder.CreateExpressionGroupingSpecification(ParseScalarExpression()));
            }
            return builder.CreateGroupByClause(groupingSpecifications);
        }

        private BuiltNode ParseSelectElement()
        {
            if (Current.Kind == MetaWeaveScriptSqlTokenKind.Star || FormsQualifiedStar())
            {
                throw Unsupported("MetaWeaveScript projections require explicit scalar expressions; SELECT * is not supported.");
            }
            if (Current.Kind == MetaWeaveScriptSqlTokenKind.Identifier && Peek().Kind == MetaWeaveScriptSqlTokenKind.Equals)
            {
                throw Unsupported("MetaWeaveScript does not support assignment-form projection aliases.");
            }
            var expression = ParseScalarExpression();
            BuiltNode? aliasNode = null;
            if (MatchKeyword("AS") || CanStartSelectAlias())
            {
                aliasNode = ParseSelectAlias();
            }
            else if (Current.Kind == MetaWeaveScriptSqlTokenKind.StringLiteral)
            {
                throw Unsupported("MetaWeaveScript does not support single-quoted projection aliases.");
            }
            return builder.CreateSelectScalarExpression(expression, aliasNode);
        }

        private bool CanStartSelectAlias() => CanStartAlias();

        private BuiltNode ParseSelectAlias()
        {
            if (Current.Kind == MetaWeaveScriptSqlTokenKind.StringLiteral)
            {
                throw Unsupported("MetaWeaveScript does not support single-quoted projection aliases.");
            }
            var identifierToken = ParseIdentifierToken();
            return builder.CreateIdentifierOrValueExpression(builder.CreateIdentifier(identifierToken.Value, identifierToken.QuoteType));
        }
    }
}
