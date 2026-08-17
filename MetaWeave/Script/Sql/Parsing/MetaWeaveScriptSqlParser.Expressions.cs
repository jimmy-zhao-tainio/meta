using static MetaWeaveScript.Sql.Parsing.MetaWeaveScriptSqlModelBuilder;

namespace MetaWeaveScript.Sql.Parsing;

public sealed partial class MetaWeaveScriptSqlParser
{
    private sealed partial class Parser
    {
        private BuiltNode ParseScalarExpression()
        {
            var left = ParseScalarTerm();
            if (Current.Kind is MetaWeaveScriptSqlTokenKind.Plus or MetaWeaveScriptSqlTokenKind.Minus)
            {
                throw Unsupported("MetaWeaveScript does not support scalar arithmetic.");
            }
            return left;
        }

        private BuiltNode ParseScalarTerm()
        {
            var left = ParseScalarPrimary();
            if (Current.Kind is MetaWeaveScriptSqlTokenKind.Star
                or MetaWeaveScriptSqlTokenKind.Slash
                or MetaWeaveScriptSqlTokenKind.Percent)
            {
                throw Unsupported("MetaWeaveScript does not support scalar arithmetic.");
            }
            return left;
        }

        private BuiltNode ParseScalarPrimary()
        {
            if (Match(MetaWeaveScriptSqlTokenKind.Minus))
            {
                throw Unsupported("MetaWeaveScript does not support unary numeric expressions.");
            }

            if (Match(MetaWeaveScriptSqlTokenKind.Plus))
            {
                throw Unsupported("MetaWeaveScript does not support unary numeric expressions.");
            }

            if (PeekKeyword("CASE"))
            {
                return ParseCaseExpression();
            }

            if (PeekKeyword("NEXT"))
            {
                throw Unsupported("MetaWeaveScript does not support NEXT VALUE FOR.");
            }

            if (PeekKeyword("NULL"))
            {
                Advance();
                return builder.CreateNullLiteral();
            }

            if (PeekKeyword("CURRENT_TIMESTAMP"))
            {
                throw Unsupported("MetaWeaveScript does not support date/time expressions.");
            }

            if (Current.Kind == MetaWeaveScriptSqlTokenKind.Identifier &&
                string.Equals(Current.Value, "@@SPID", StringComparison.OrdinalIgnoreCase))
            {
                throw Unsupported("MetaWeaveScript does not support global variables.");
            }

            if (Current.Kind == MetaWeaveScriptSqlTokenKind.Identifier &&
                Current.Value.StartsWith('@') &&
                !Current.Value.StartsWith("@@", StringComparison.Ordinal))
            {
                var token = Advance();
                if (token.Value.Length == 1)
                {
                    throw ParseError("A WeaveScript parameter reference requires a name after '@'.");
                }

                return builder.CreateParameterReferenceExpression(token.Value[1..]);
            }

            if (Current.Kind == MetaWeaveScriptSqlTokenKind.StringLiteral)
            {
                var token = Advance();
                return builder.CreateStringLiteral(token.Value);
            }

            if (MatchNationalStringLiteral(out var nationalStringLiteralToken))
            {
                throw Unsupported("MetaWeaveScript does not support national string literal syntax.");
            }

            if (Current.Kind == MetaWeaveScriptSqlTokenKind.BinaryLiteral)
            {
                throw Unsupported("MetaWeaveScript does not support binary literals.");
            }

            if (Current.Kind == MetaWeaveScriptSqlTokenKind.NumberLiteral)
            {
                var token = Advance();
                if (token.Value.IndexOfAny(['.', 'E', 'e']) >= 0)
                {
                    throw Unsupported("MetaWeaveScript supports integer literals only.");
                }
                return builder.CreateNumberLiteral(token.Value);
            }

            if (Current.Kind == MetaWeaveScriptSqlTokenKind.Identifier)
            {
                var identifiers = ParseIdentifierTokenChain();
                if (Current.Kind == MetaWeaveScriptSqlTokenKind.OpenParen)
                {
                    return ParseFunctionLikeExpression(identifiers, callTarget: null);
                }

                var multiPartIdentifier = builder.CreateMultiPartIdentifier(
                    identifiers.Select(token => builder.CreateIdentifier(token.Value, token.QuoteType)).ToArray());
                return ParseTrailingScalarSuffixes(builder.CreateColumnReferenceExpression(multiPartIdentifier));
            }

            if (Current.Kind == MetaWeaveScriptSqlTokenKind.OpenParen)
            {
                if (PeekKeywordAfterOpenParen("SELECT"))
                {
                    return ParseScalarSubquery();
                }

                Expect(MetaWeaveScriptSqlTokenKind.OpenParen);
                var expression = ParseScalarExpression();
                Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
                return ParseTrailingScalarSuffixes(builder.CreateParenthesisExpression(expression));
            }

            throw ParseError($"Expected a scalar expression but found '{Current.Text}'.");
        }

        private bool PeekKeywordAfterOpenParen(string keyword)
        {
            if (Current.Kind != MetaWeaveScriptSqlTokenKind.OpenParen)
            {
                return false;
            }

            var probe = position + 1;
            while (probe < tokens.Count)
            {
                var token = tokens[probe];
                if (token.Kind == MetaWeaveScriptSqlTokenKind.Semicolon)
                {
                    probe++;
                    continue;
                }

                return IsKeyword(token, keyword);
            }

            return false;
        }

        private BuiltNode ParseBooleanExpression() => ParseBooleanOr();

        private BuiltNode ParseBooleanOr()
        {
            var left = ParseBooleanAnd();
            while (MatchKeyword("OR"))
            {
                left = builder.CreateBooleanBinaryExpression(left, ParseBooleanAnd(), "Or");
            }

            return left;
        }

        private BuiltNode ParseBooleanAnd()
        {
            var left = ParseBooleanNot();
            while (MatchKeyword("AND"))
            {
                left = builder.CreateBooleanBinaryExpression(left, ParseBooleanNot(), "And");
            }

            return left;
        }

        private BuiltNode ParseBooleanNot()
        {
            if (MatchKeyword("NOT"))
            {
                return builder.CreateBooleanNotExpression(ParseBooleanPrimary());
            }

            return ParseBooleanPrimary();
        }

        private BuiltNode ParseBooleanPrimary()
        {
            if (MatchKeyword("EXISTS"))
            {
                return builder.CreateExistsPredicate(ParseScalarSubquery());
            }

            if (PeekKeyword("CONTAINS") || PeekKeyword("FREETEXT"))
            {
                throw Unsupported("MetaWeaveScript does not support full-text predicates.");
            }

            if (Current.Kind == MetaWeaveScriptSqlTokenKind.OpenParen)
            {
                if (FormsScalarComparison())
                {
                    return ParseComparisonExpression();
                }

                Expect(MetaWeaveScriptSqlTokenKind.OpenParen);
                var inner = ParseBooleanExpression();
                Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
                return builder.CreateBooleanParenthesisExpression(inner);
            }

            return ParseComparisonExpression();
        }

        private bool FormsScalarComparison()
        {
            var probe = position;
            return ProbeScalarExpression(ref probe) && IsScalarComparisonOperator(PeekToken(probe));
        }

        private bool ProbeScalarExpression(ref int probe)
        {
            if (!ProbeScalarTerm(ref probe))
            {
                return false;
            }

            while (PeekToken(probe).Kind is MetaWeaveScriptSqlTokenKind.Plus or MetaWeaveScriptSqlTokenKind.Minus)
            {
                probe++;
                if (!ProbeScalarTerm(ref probe))
                {
                    return false;
                }
            }

            return true;
        }

        private bool ProbeScalarTerm(ref int probe)
        {
            if (!ProbeScalarPrimary(ref probe))
            {
                return false;
            }

            while (PeekToken(probe).Kind is MetaWeaveScriptSqlTokenKind.Star
                or MetaWeaveScriptSqlTokenKind.Slash
                or MetaWeaveScriptSqlTokenKind.Percent)
            {
                probe++;
                if (!ProbeScalarPrimary(ref probe))
                {
                    return false;
                }
            }

            return true;
        }

        private bool ProbeScalarPrimary(ref int probe)
        {
            var current = PeekToken(probe);
            if (current.Kind is MetaWeaveScriptSqlTokenKind.Plus or MetaWeaveScriptSqlTokenKind.Minus)
            {
                probe++;
                return ProbeScalarPrimary(ref probe);
            }

            if (IsKeyword(current, "CASE"))
            {
                if (!ProbeCaseExpression(ref probe))
                {
                    return false;
                }

                ProbeTrailingScalarSuffixes(ref probe);
                return true;
            }

            if (IsKeyword(current, "NEXT"))
            {
                probe++;
                if (!IsKeyword(PeekToken(probe), "VALUE"))
                {
                    return false;
                }

                probe++;
                if (!IsKeyword(PeekToken(probe), "FOR"))
                {
                    return false;
                }

                probe++;
                if (!ProbeSchemaObjectName(ref probe))
                {
                    return false;
                }

                return true;
            }

            if (IsKeyword(current, "NULL") || IsKeyword(current, "CURRENT_TIMESTAMP"))
            {
                probe++;
                return true;
            }

            if (current.Kind == MetaWeaveScriptSqlTokenKind.Identifier &&
                string.Equals(current.Value, "@@SPID", StringComparison.OrdinalIgnoreCase))
            {
                probe++;
                return true;
            }

            if (current.Kind is MetaWeaveScriptSqlTokenKind.StringLiteral
                or MetaWeaveScriptSqlTokenKind.BinaryLiteral
                or MetaWeaveScriptSqlTokenKind.NumberLiteral)
            {
                probe++;
                return true;
            }

            if (IsNationalStringLiteralPrefixAt(probe))
            {
                probe += 2;
                return true;
            }

            if (current.Kind == MetaWeaveScriptSqlTokenKind.Identifier)
            {
                if (!ProbeIdentifierChain(ref probe))
                {
                    return false;
                }

                if (PeekToken(probe).Kind == MetaWeaveScriptSqlTokenKind.OpenParen)
                {
                    if (!ConsumeBalancedParentheses(ref probe))
                    {
                        return false;
                    }

                    if (IsKeyword(PeekToken(probe), "OVER"))
                    {
                        probe++;
                        if (PeekToken(probe).Kind == MetaWeaveScriptSqlTokenKind.OpenParen)
                        {
                            if (!ConsumeBalancedParentheses(ref probe))
                            {
                                return false;
                            }
                        }
                        else if (PeekToken(probe).Kind == MetaWeaveScriptSqlTokenKind.Identifier)
                        {
                            probe++;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }

                ProbeTrailingScalarSuffixes(ref probe);
                return true;
            }

            if (current.Kind == MetaWeaveScriptSqlTokenKind.OpenParen)
            {
                if (PeekKeywordAfterOpenParenAt(probe, "SELECT"))
                {
                    return ConsumeBalancedParentheses(ref probe);
                }

                probe++;
                if (!ProbeScalarExpression(ref probe))
                {
                    return false;
                }

                if (PeekToken(probe).Kind != MetaWeaveScriptSqlTokenKind.CloseParen)
                {
                    return false;
                }

                probe++;
                ProbeTrailingScalarSuffixes(ref probe);
                return true;
            }

            return false;
        }

        private void ProbeTrailingScalarSuffixes(ref int probe)
        {
            while (true)
            {
                if (IsKeyword(PeekToken(probe), "COLLATE"))
                {
                    probe++;
                    if (PeekToken(probe).Kind == MetaWeaveScriptSqlTokenKind.Identifier)
                    {
                        probe++;
                    }

                    continue;
                }

                if (IsKeyword(PeekToken(probe), "AT"))
                {
                    var checkpoint = probe;
                    probe++;
                    if (!IsKeyword(PeekToken(probe), "TIME"))
                    {
                        probe = checkpoint;
                        return;
                    }

                    probe++;
                    if (!IsKeyword(PeekToken(probe), "ZONE"))
                    {
                        probe = checkpoint;
                        return;
                    }

                    probe++;
                    if (!ProbeScalarExpression(ref probe))
                    {
                        probe = checkpoint;
                        return;
                    }

                    continue;
                }

                return;
            }
        }

        private bool ProbeCaseExpression(ref int probe)
        {
            if (!IsKeyword(PeekToken(probe), "CASE"))
            {
                return false;
            }

            var depth = 0;
            while (probe < tokens.Count)
            {
                var token = PeekToken(probe);
                if (IsKeyword(token, "CASE"))
                {
                    depth++;
                }
                else if (IsKeyword(token, "END"))
                {
                    depth--;
                    probe++;
                    return depth == 0;
                }

                probe++;
            }

            return false;
        }

        private bool ProbeSchemaObjectName(ref int probe) =>
            ProbeIdentifierChain(ref probe);

        private bool ProbeIdentifierChain(ref int probe)
        {
            if (PeekToken(probe).Kind != MetaWeaveScriptSqlTokenKind.Identifier)
            {
                return false;
            }

            probe++;
            while (PeekToken(probe).Kind == MetaWeaveScriptSqlTokenKind.Dot)
            {
                probe++;
                if (PeekToken(probe).Kind != MetaWeaveScriptSqlTokenKind.Identifier)
                {
                    return false;
                }

                probe++;
            }

            return true;
        }

        private bool ConsumeBalancedParentheses(ref int probe)
        {
            if (PeekToken(probe).Kind != MetaWeaveScriptSqlTokenKind.OpenParen)
            {
                return false;
            }

            var depth = 0;
            while (probe < tokens.Count)
            {
                var kind = PeekToken(probe).Kind;
                if (kind == MetaWeaveScriptSqlTokenKind.OpenParen)
                {
                    depth++;
                }
                else if (kind == MetaWeaveScriptSqlTokenKind.CloseParen)
                {
                    depth--;
                    probe++;
                    if (depth == 0)
                    {
                        return true;
                    }

                    continue;
                }

                probe++;
            }

            return false;
        }

        private bool PeekKeywordAfterOpenParenAt(int probe, string keyword)
        {
            if (PeekToken(probe).Kind != MetaWeaveScriptSqlTokenKind.OpenParen)
            {
                return false;
            }

            probe++;
            while (probe < tokens.Count)
            {
                var token = PeekToken(probe);
                if (token.Kind == MetaWeaveScriptSqlTokenKind.Semicolon)
                {
                    probe++;
                    continue;
                }

                return IsKeyword(token, keyword);
            }

            return false;
        }

        private bool IsScalarComparisonOperator(MetaWeaveScriptSqlToken token) =>
            token.Kind is MetaWeaveScriptSqlTokenKind.Equals
                or MetaWeaveScriptSqlTokenKind.GreaterThan
                or MetaWeaveScriptSqlTokenKind.GreaterThanOrEqual
                or MetaWeaveScriptSqlTokenKind.LessThan
                or MetaWeaveScriptSqlTokenKind.LessThanOrEqual
                or MetaWeaveScriptSqlTokenKind.NotEqual
            || IsKeyword(token, "BETWEEN")
            || IsKeyword(token, "IN")
            || IsKeyword(token, "LIKE")
            || IsKeyword(token, "IS");

        private BuiltNode ParseComparisonExpression()
        {
            var first = ParseScalarExpression();
            if (MatchKeyword("NOT"))
            {
                throw Unsupported("MetaWeaveScript does not support NOT BETWEEN, NOT IN, or NOT LIKE.");
            }

            if (MatchKeyword("BETWEEN"))
            {
                throw Unsupported("MetaWeaveScript does not support BETWEEN.");
            }

            if (MatchKeyword("IN"))
            {
                Expect(MetaWeaveScriptSqlTokenKind.OpenParen);
                if (PeekKeyword("SELECT"))
                {
                    throw Unsupported("MetaWeaveScript IN predicates require an explicit value list.");
                }

                var values = new List<BuiltNode> { ParseScalarExpression() };
                while (Match(MetaWeaveScriptSqlTokenKind.Comma))
                {
                    values.Add(ParseScalarExpression());
                }

                Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
                return builder.CreateInPredicate(first, values, notDefined: false);
            }

            if (MatchKeyword("LIKE"))
            {
                var pattern = ParseScalarExpression();
                BuiltNode? escapeExpression = null;
                if (MatchKeyword("ESCAPE"))
                {
                    throw Unsupported("MetaWeaveScript LIKE predicates do not support ESCAPE.");
                }

                return builder.CreateLikePredicate(first, pattern, notDefined: false, escapeExpression);
            }

            if (MatchKeyword("IS"))
            {
                var isNot = MatchKeyword("NOT");
                if (MatchKeyword("DISTINCT"))
                {
                    throw Unsupported("MetaWeaveScript does not support IS DISTINCT FROM.");
                }

                ExpectKeyword("NULL");
                return builder.CreateBooleanIsNullExpression(first, isNot);
            }

            var comparisonType = Current.Kind switch
            {
                MetaWeaveScriptSqlTokenKind.Equals => "Equals",
                MetaWeaveScriptSqlTokenKind.GreaterThan => "GreaterThan",
                MetaWeaveScriptSqlTokenKind.GreaterThanOrEqual => "GreaterThanOrEqualTo",
                MetaWeaveScriptSqlTokenKind.LessThan => "LessThan",
                MetaWeaveScriptSqlTokenKind.LessThanOrEqual => "LessThanOrEqualTo",
                MetaWeaveScriptSqlTokenKind.NotEqual => "NotEqualToBrackets",
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(comparisonType))
            {
                throw ParseError($"Expected a comparison operator but found '{Current.Text}'.");
            }

            if (string.Equals(Current.Text, "!=", StringComparison.Ordinal))
            {
                throw Unsupported("MetaWeaveScript uses <> for not-equal; != is not supported.");
            }

            Advance();
            if (MatchKeyword("ALL"))
            {
                throw Unsupported("MetaWeaveScript does not support ALL subquery comparisons.");
            }

            if (MatchKeyword("ANY"))
            {
                throw Unsupported("MetaWeaveScript does not support ANY subquery comparisons.");
            }

            var second = ParseScalarExpression();
            return builder.CreateBooleanComparisonExpression(first, second, comparisonType);
        }

        private BuiltNode ParseScalarSubquery()
        {
            Expect(MetaWeaveScriptSqlTokenKind.OpenParen);
            if (!PeekKeyword("SELECT"))
            {
                throw Unsupported("This parenthesized scalar expression shape is not supported yet.");
            }

            var queryExpression = ParseQueryExpression();
            Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
            return builder.CreateScalarSubquery(queryExpression);
        }

        private BuiltNode ParseNextValueForExpression()
        {
            throw Unsupported("MetaWeaveScript does not support NEXT VALUE FOR.");
        }

        private BuiltNode ParseTrailingScalarSuffixes(BuiltNode expression)
        {
            if (PeekKeyword("COLLATE") || PeekKeyword("AT"))
            {
                throw Unsupported("MetaWeaveScript does not support COLLATE or AT TIME ZONE expressions.");
            }
            return expression;
        }

        private BuiltNode ParseFullTextPredicate()
        {
            throw Unsupported("MetaWeaveScript does not support full-text predicates.");
        }

        private BuiltNode ParseCaseExpression()
        {
            ExpectKeyword("CASE");
            if (!PeekKeyword("WHEN"))
            {
                throw Unsupported("MetaWeaveScript supports searched CASE, not simple CASE.");
            }
            var whenClauses = new List<(BuiltNode WhenExpression, BuiltNode ThenExpression)>();
            while (MatchKeyword("WHEN"))
            {
                var whenExpression = ParseBooleanExpression();
                ExpectKeyword("THEN");
                var thenExpression = ParseScalarExpression();
                whenClauses.Add((whenExpression, thenExpression));
            }
            BuiltNode? elseExpression = null;
            if (MatchKeyword("ELSE"))
            {
                elseExpression = ParseScalarExpression();
            }
            ExpectKeyword("END");
            return builder.CreateSearchedCaseExpression(whenClauses, elseExpression);
        }
    }
}
