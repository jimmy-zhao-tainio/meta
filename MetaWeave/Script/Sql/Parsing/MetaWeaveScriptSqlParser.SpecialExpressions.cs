using static MetaWeaveScript.Sql.Parsing.MetaWeaveScriptSqlModelBuilder;

namespace MetaWeaveScript.Sql.Parsing;

public sealed partial class MetaWeaveScriptSqlParser
{
    private sealed partial class Parser
    {
        private static readonly HashSet<string> SupportedScalarFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "CONCAT", "LOWER", "UPPER", "TRIM", "LTRIM", "RTRIM", "LEN", "IS_BLANK", "REPLACE", "SUBSTRING", "LEFT", "RIGHT"
        };

        private static readonly HashSet<string> SupportedAggregateFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "COUNT", "MIN", "MAX", "STRING_AGG"
        };

        private static readonly HashSet<string> SupportedWindowFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "ROW_NUMBER"
        };

        private BuiltNode ParseFunctionLikeExpression(IReadOnlyList<MetaWeaveScriptSqlToken> identifiers, BuiltNode? callTarget)
        {
            if (identifiers.Count == 0)
            {
                throw new InvalidOperationException("Function-like expression parsing requires at least one identifier.");
            }
            if (callTarget is not null || identifiers.Count != 1)
            {
                throw Unsupported("MetaWeaveScript supports unqualified function names only.");
            }

            var functionNameToken = identifiers[0];
            return functionNameToken.Value.ToUpperInvariant() switch
            {
                "COALESCE" => ParseCoalesceExpression(),
                "NULLIF" => ParseNullIfExpression(),
                "IIF" => ParseIIfCall(),
                "TRY_CONVERT" => ParseTryConvertCall(),
                _ => ParseGenericFunctionCall(functionNameToken)
            };
        }

        private BuiltNode ParseGenericFunctionCall(MetaWeaveScriptSqlToken functionNameToken)
        {
            var functionNameValue = functionNameToken.Value.ToUpperInvariant();
            if (!SupportedScalarFunctions.Contains(functionNameValue) &&
                !SupportedAggregateFunctions.Contains(functionNameValue) &&
                !SupportedWindowFunctions.Contains(functionNameValue))
            {
                throw Unsupported($"Function '{functionNameToken.Value}' is outside the MetaWeaveScript function catalog.");
            }

            Expect(MetaWeaveScriptSqlTokenKind.OpenParen);
            if (MatchKeyword("DISTINCT"))
            {
                throw Unsupported("MetaWeaveScript function calls do not support DISTINCT.");
            }
            var parameters = new List<BuiltNode>();
            if (!Match(MetaWeaveScriptSqlTokenKind.CloseParen))
            {
                if (Match(MetaWeaveScriptSqlTokenKind.Star))
                {
                    if (!string.Equals(functionNameValue, "COUNT", StringComparison.OrdinalIgnoreCase))
                    {
                        throw Unsupported("Only COUNT(*) may use a wildcard function argument.");
                    }
                    parameters.Add(builder.CreateWildcardColumnReferenceExpression());
                    Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
                }
                else
                {
                    parameters.Add(ParseScalarExpression());
                    while (Match(MetaWeaveScriptSqlTokenKind.Comma))
                    {
                        parameters.Add(ParseScalarExpression());
                    }
                    Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
                }
            }

            ValidateFunctionArity(functionNameValue, parameters.Count);
            var functionName = builder.CreateIdentifier(functionNameToken.Value, functionNameToken.QuoteType);
            var functionCall = builder.CreateFunctionCall(functionName, parameters);
            var hasWithinGroup = false;
            if (MatchKeyword("WITHIN"))
            {
                if (!string.Equals(functionNameValue, "STRING_AGG", StringComparison.OrdinalIgnoreCase))
                {
                    throw Unsupported("WITHIN GROUP is supported only for STRING_AGG.");
                }
                ExpectKeyword("GROUP");
                Expect(MetaWeaveScriptSqlTokenKind.OpenParen);
                ExpectKeyword("ORDER");
                ExpectKeyword("BY");
                var orderByClause = ParseOrderByClause();
                Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
                functionCall = builder.AttachWithinGroupOrderByClause(functionCall, orderByClause);
                hasWithinGroup = true;
            }
            if (string.Equals(functionNameValue, "STRING_AGG", StringComparison.OrdinalIgnoreCase) && !hasWithinGroup)
            {
                throw Unsupported("MetaWeaveScript STRING_AGG requires WITHIN GROUP ordering.");
            }
            if (PeekKeyword("OVER"))
            {
                if (!string.Equals(functionNameValue, "ROW_NUMBER", StringComparison.OrdinalIgnoreCase))
                {
                    throw Unsupported("MetaWeaveScript supports OVER only for ROW_NUMBER.");
                }

                functionCall = builder.AttachOverClause(functionCall, ParseOverClause());
            }
            else if (string.Equals(functionNameValue, "ROW_NUMBER", StringComparison.OrdinalIgnoreCase))
            {
                throw Unsupported("MetaWeaveScript ROW_NUMBER requires an OVER clause.");
            }
            return functionCall;
        }

        private void ValidateFunctionArity(string functionName, int count)
        {
            var valid = functionName switch
            {
                "COUNT" or "MIN" or "MAX" or "LOWER" or "UPPER" or "LTRIM" or "RTRIM" or "LEN" or "IS_BLANK" => count == 1,
                "STRING_AGG" or "LEFT" or "RIGHT" => count == 2,
                "REPLACE" => count == 3,
                "SUBSTRING" => count == 3,
                "TRIM" => count == 1,
                "CONCAT" => count >= 2,
                "ROW_NUMBER" => count == 0,
                _ => false
            };
            if (!valid)
            {
                throw ParseError($"Invalid argument count {count} for MetaWeaveScript function '{functionName}'.");
            }
        }

        private BuiltNode ParseCoalesceExpression()
        {
            var arguments = ParseScalarArgumentList();
            if (arguments.Count < 2)
            {
                throw ParseError("COALESCE requires at least two arguments.");
            }
            return builder.CreateCoalesceExpression(arguments);
        }

        private BuiltNode ParseNullIfExpression()
        {
            Expect(MetaWeaveScriptSqlTokenKind.OpenParen);
            var firstExpression = ParseScalarExpression();
            Expect(MetaWeaveScriptSqlTokenKind.Comma);
            var secondExpression = ParseScalarExpression();
            Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
            return builder.CreateNullIfExpression(firstExpression, secondExpression);
        }

        private BuiltNode ParseIIfCall()
        {
            Expect(MetaWeaveScriptSqlTokenKind.OpenParen);
            var predicate = ParseBooleanExpression();
            Expect(MetaWeaveScriptSqlTokenKind.Comma);
            var thenExpression = ParseScalarExpression();
            Expect(MetaWeaveScriptSqlTokenKind.Comma);
            var elseExpression = ParseScalarExpression();
            Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
            return builder.CreateIIfCall(predicate, thenExpression, elseExpression);
        }

        private BuiltNode ParseTryConvertCall()
        {
            Expect(MetaWeaveScriptSqlTokenKind.OpenParen);
            var dataType = ParseIdentifierToken();
            if (!string.Equals(dataType.Value, "int", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(dataType.QuoteType, "NotQuoted", StringComparison.Ordinal))
            {
                throw Unsupported("MetaWeaveScript TRY_CONVERT supports the int data type only.");
            }

            Expect(MetaWeaveScriptSqlTokenKind.Comma);
            var parameter = ParseScalarExpression();
            if (Match(MetaWeaveScriptSqlTokenKind.Comma))
            {
                throw Unsupported("MetaWeaveScript TRY_CONVERT does not support a style argument.");
            }

            Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
            return builder.CreateTryConvertCall(builder.CreateIntDataTypeReference(), parameter);
        }

        private List<BuiltNode> ParseScalarArgumentList()
        {
            Expect(MetaWeaveScriptSqlTokenKind.OpenParen);
            var arguments = new List<BuiltNode> { ParseScalarExpression() };
            while (Match(MetaWeaveScriptSqlTokenKind.Comma))
            {
                arguments.Add(ParseScalarExpression());
            }
            Expect(MetaWeaveScriptSqlTokenKind.CloseParen);
            return arguments;
        }
    }
}
