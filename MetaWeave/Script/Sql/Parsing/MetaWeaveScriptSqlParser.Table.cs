using static MetaWeaveScript.Sql.Parsing.MetaWeaveScriptSqlModelBuilder;

namespace MetaWeaveScript.Sql.Parsing;

public sealed partial class MetaWeaveScriptSqlParser
{
    private sealed partial class Parser
    {
        private BuiltNode ParseFromClause()
        {
            var tableReferences = new List<BuiltNode> { ParseTableReference() };
            while (Match(MetaWeaveScriptSqlTokenKind.Comma))
            {
                tableReferences.Add(ParseTableReference());
            }
            return builder.CreateFromClause(tableReferences);
        }

        private BuiltNode ParseTableReference()
        {
            var currentReference = ParseTableReferencePrimary();
            while (true)
            {
                if (PeekKeyword("PIVOT") || PeekKeyword("UNPIVOT"))
                {
                    throw Unsupported("MetaWeaveScript does not support PIVOT or UNPIVOT.");
                }
                if (MatchKeyword("INNER") || MatchKeyword("JOIN"))
                {
                    if (!string.Equals(tokens[position - 1].Value, "JOIN", StringComparison.OrdinalIgnoreCase))
                    {
                        ExpectKeyword("JOIN");
                    }
                    var right = ParseTableReferencePrimary();
                    ExpectKeyword("ON");
                    currentReference = builder.CreateQualifiedJoin(currentReference, right, "Inner", ParseBooleanExpression());
                    continue;
                }
                if (MatchKeyword("LEFT"))
                {
                    MatchKeyword("OUTER");
                    ExpectKeyword("JOIN");
                    var right = ParseTableReferencePrimary();
                    ExpectKeyword("ON");
                    currentReference = builder.CreateQualifiedJoin(currentReference, right, "LeftOuter", ParseBooleanExpression());
                    continue;
                }
                if (PeekKeyword("RIGHT") || PeekKeyword("FULL"))
                {
                    throw Unsupported("MetaWeaveScript supports INNER and LEFT joins, not RIGHT or FULL joins.");
                }
                if (MatchKeyword("CROSS"))
                {
                    if (MatchKeyword("JOIN"))
                    {
                        currentReference = builder.CreateUnqualifiedJoin(currentReference, ParseTableReferencePrimary(), "CrossJoin");
                        continue;
                    }
                    if (MatchKeyword("APPLY"))
                    {
                        currentReference = builder.CreateUnqualifiedJoin(currentReference, ParseTableReferencePrimary(), "CrossApply");
                        continue;
                    }
                    throw Unsupported("Unsupported CROSS table-reference form.");
                }
                if (MatchKeyword("OUTER"))
                {
                    ExpectKeyword("APPLY");
                    currentReference = builder.CreateUnqualifiedJoin(currentReference, ParseTableReferencePrimary(), "OuterApply");
                    continue;
                }
                return currentReference;
            }
        }

        private BuiltNode ParseTableReferencePrimary()
        {
            if (CanStartXmlNodesTableReference())
            {
                throw Unsupported("MetaWeaveScript does not support XML nodes table references.");
            }
            if (Current.Kind == MetaWeaveScriptSqlTokenKind.OpenParen)
            {
                return ParseParenthesizedTableReference();
            }
            return ParseNamedOrFunctionTableReference();
        }

        private bool CanStartXmlNodesTableReference()
        {
            if (Current.Kind != MetaWeaveScriptSqlTokenKind.Identifier)
            {
                return false;
            }
            var probe = position + 1;
            while (PeekToken(probe).Kind == MetaWeaveScriptSqlTokenKind.Dot && PeekToken(probe + 1).Kind == MetaWeaveScriptSqlTokenKind.Identifier)
            {
                if (string.Equals(PeekToken(probe + 1).Value, "nodes", StringComparison.OrdinalIgnoreCase) && PeekToken(probe + 2).Kind == MetaWeaveScriptSqlTokenKind.OpenParen)
                {
                    return true;
                }
                probe += 2;
            }
            return false;
        }

        private BuiltNode ParseNamedOrFunctionTableReference()
        {
            var identifiers = ParseIdentifierChain();
            if (Match(MetaWeaveScriptSqlTokenKind.OpenParen))
            {
                return ParseFunctionTableReference(identifiers);
            }
            if (identifiers.Count is < 1 or > 2)
            {
                throw Unsupported("MetaWeaveScript source names allow an entity name with one optional source-workspace qualifier.");
            }
            var schemaObjectName = builder.CreateSchemaObjectName(identifiers.Select(static identifier => identifier.Node).ToArray());
            BuiltNode? alias = null;
            if (MatchKeyword("AS") || CanStartAlias())
            {
                alias = ParseIdentifier().Node;
            }
            if (PeekKeyword("WITH") && Peek().Kind == MetaWeaveScriptSqlTokenKind.OpenParen)
            {
                throw Unsupported("MetaWeaveScript does not support table hints.");
            }
            if (PeekKeyword("TABLESAMPLE"))
            {
                throw Unsupported("MetaWeaveScript does not support TABLESAMPLE.");
            }
            return builder.CreateNamedTableReference(schemaObjectName, alias);
        }

        private BuiltNode ParseFunctionTableReference(IReadOnlyList<ParsedIdentifier> identifiers)
        {
            if (identifiers.Count != 1 || !string.Equals(identifiers[0].Token.Value, "STRING_SPLIT", StringComparison.OrdinalIgnoreCase))
            {
                throw Unsupported("STRING_SPLIT is the only MetaWeaveScript table-valued function.");
            }
            var parameters = new List<BuiltNode>();
            if (!Match(MetaWeaveScriptSqlTokenKind.CloseParen))
            {
                parameters.Add(ParseScalarExpression());
                while (Match(MetaWeaveScriptSqlTokenKind.Comma))
                {
                    parameters.Add(ParseScalarExpression());
                }
                Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
            }
            if (parameters.Count is < 2 or > 3)
            {
                throw ParseError("STRING_SPLIT requires two or three arguments.");
            }
            BuiltNode? alias = null;
            if (MatchKeyword("AS") || CanStartAlias())
            {
                alias = ParseIdentifier().Node;
            }
            if (Current.Kind == MetaWeaveScriptSqlTokenKind.OpenParen)
            {
                throw Unsupported("MetaWeaveScript STRING_SPLIT references do not support column alias lists.");
            }
            return builder.CreateGlobalFunctionTableReference(identifiers[0].Node, parameters, alias);
        }

        private BuiltNode ParseParenthesizedTableReference()
        {
            Expect(MetaWeaveScriptSqlTokenKind.OpenParen);
            if (CanStartParenthesizedQueryDerivedTable())
            {
                var queryExpression = ParseQueryExpression();
                Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
                var (alias, columns) = ParseRequiredTableAliasAndColumns();
                return builder.CreateQueryDerivedTable(queryExpression, alias, columns);
            }
            if (PeekKeyword("VALUES"))
            {
                Advance();
                var rowValues = new List<BuiltNode> { ParseRowValue() };
                while (Match(MetaWeaveScriptSqlTokenKind.Comma))
                {
                    rowValues.Add(ParseRowValue());
                }
                Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
                var (alias, columns) = ParseRequiredTableAliasAndColumns();
                return builder.CreateInlineDerivedTable(rowValues, alias, columns);
            }
            throw Unsupported("MetaWeaveScript parenthesized table references must be derived SELECT or VALUES tables.");
        }

        private bool CanStartParenthesizedQueryDerivedTable()
        {
            if (PeekKeyword("SELECT") || PeekKeyword("WITH"))
            {
                return true;
            }
            return Current.Kind == MetaWeaveScriptSqlTokenKind.OpenParen && (PeekKeywordAfterOpenParen("SELECT") || PeekKeywordAfterOpenParen("WITH"));
        }

        private BuiltNode ParseRowValue()
        {
            Expect(MetaWeaveScriptSqlTokenKind.OpenParen);
            var columnValues = new List<BuiltNode> { ParseScalarExpression() };
            while (Match(MetaWeaveScriptSqlTokenKind.Comma))
            {
                columnValues.Add(ParseScalarExpression());
            }
            Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
            return builder.CreateRowValue(columnValues);
        }

        private (BuiltNode Alias, IReadOnlyList<BuiltNode> Columns) ParseRequiredTableAliasAndColumns()
        {
            BuiltNode? alias = null;
            if (MatchKeyword("AS") || CanStartAlias())
            {
                alias = ParseIdentifier().Node;
            }
            if (alias is null)
            {
                throw ParseError("Derived table references require an alias.");
            }
            var columns = new List<BuiltNode>();
            if (Match(MetaWeaveScriptSqlTokenKind.OpenParen))
            {
                columns.Add(ParseIdentifier().Node);
                while (Match(MetaWeaveScriptSqlTokenKind.Comma))
                {
                    columns.Add(ParseIdentifier().Node);
                }
                Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
            }
            return (alias, columns);
        }

        private BuiltNode ParseColumnReferenceExpression()
        {
            var multiPartIdentifier = ParseMultiPartIdentifier();
            return builder.CreateColumnReferenceExpression(multiPartIdentifier);
        }

        private BuiltNode ParseSchemaObjectName()
        {
            var identifiers = ParseIdentifierChain();
            if (identifiers.Count is < 1 or > 2)
            {
                throw Unsupported("MetaWeaveScript source names require an entity name with one optional source-workspace qualifier.");
            }
            return builder.CreateSchemaObjectName(identifiers.Select(static identifier => identifier.Node).ToArray());
        }
    }
}
