namespace MetaWeaveScript.Sql.Parsing;

public sealed partial class MetaWeaveScriptSqlParser
{
    private sealed partial class Parser
    {
        private MetaWeaveScriptSqlToken Expect(MetaWeaveScriptSqlTokenKind kind)
        {
            if (Current.Kind != kind)
            {
                throw ParseError($"Expected {kind} but found '{Current.Text}'.");
            }

            return Advance();
        }

        private void ExpectKeyword(string keyword)
        {
            if (!MatchKeyword(keyword))
            {
                throw ParseError($"Expected keyword '{keyword}' but found '{Current.Text}'.");
            }
        }

        private bool MatchKeyword(string keyword)
        {
            if (!PeekKeyword(keyword))
            {
                return false;
            }

            Advance();
            return true;
        }

        private bool PeekKeyword(string keyword) => IsKeyword(Current, keyword);

        private bool Match(MetaWeaveScriptSqlTokenKind kind)
        {
            if (Current.Kind != kind)
            {
                return false;
            }

            Advance();
            return true;
        }

        private bool Peek(MetaWeaveScriptSqlTokenKind kind) =>
            Peek().Kind == kind;

        private void SkipSemicolons()
        {
            while (Match(MetaWeaveScriptSqlTokenKind.Semicolon))
            {
            }
        }

        private void ExpectEndOfFile()
        {
            if (Current.Kind != MetaWeaveScriptSqlTokenKind.EndOfFile)
            {
                if (PeekKeyword("GO"))
                {
                    throw Unsupported("GO-separated batches are not supported in direct parser input.");
                }

                throw ParseError($"Unexpected trailing token '{Current.Text}'.");
            }
        }

        private MetaWeaveScriptSqlToken Advance()
        {
            var token = Current;
            if (position < tokens.Count - 1)
            {
                position++;
            }

            return token;
        }

        private MetaWeaveScriptSqlToken Peek() => PeekToken(position + 1);

        private MetaWeaveScriptSqlToken PeekToken(int absolutePosition) =>
            absolutePosition < tokens.Count ? tokens[absolutePosition] : tokens[^1];

        private MetaWeaveScriptSqlToken Current => tokens[position];

        private static bool IsKeyword(MetaWeaveScriptSqlToken token, string keyword) =>
            token.Kind == MetaWeaveScriptSqlTokenKind.Identifier
            && string.Equals(token.QuoteType, "NotQuoted", StringComparison.Ordinal)
            && string.Equals(token.Value, keyword, StringComparison.OrdinalIgnoreCase);

        private static bool IsKeyword(MetaWeaveScriptSqlToken token, IReadOnlySet<string> keywords) =>
            token.Kind == MetaWeaveScriptSqlTokenKind.Identifier
            && string.Equals(token.QuoteType, "NotQuoted", StringComparison.Ordinal)
            && keywords.Contains(token.Value);

        private static string RenderIdentifier(MetaWeaveScriptSqlToken token) =>
            token.QuoteType switch
            {
                "SquareBracket" => "[" + token.Value.Replace("]", "]]", StringComparison.Ordinal) + "]",
                "DoubleQuote" => "\"" + token.Value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"",
                "Backtick" => "`" + token.Value.Replace("`", "``", StringComparison.Ordinal) + "`",
                _ => token.Value
            };

        private MetaWeaveScriptSqlParserException ParseError(string message) =>
            new(MetaWeaveScriptSqlParserFailureKind.ParseError, message, Current.Line, Current.Column, Current.Offset);

        private MetaWeaveScriptSqlParserException Unsupported(string message) =>
            new(MetaWeaveScriptSqlParserFailureKind.UnsupportedSyntax, message, Current.Line, Current.Column, Current.Offset);

        private MetaWeaveScriptSqlParserException UnsupportedFunctionWrapper(string message) =>
            new(MetaWeaveScriptSqlParserFailureKind.UnsupportedFunctionWrapper, message, Current.Line, Current.Column, Current.Offset);

        private bool MatchNationalStringLiteral(out MetaWeaveScriptSqlToken stringLiteralToken)
        {
            stringLiteralToken = default;
            if (!IsNationalStringLiteralPrefix(Current, Peek()))
            {
                return false;
            }

            Advance();
            stringLiteralToken = Advance();
            return true;
        }

        private bool IsNationalStringLiteralPrefixAt(int absolutePosition) =>
            IsNationalStringLiteralPrefix(PeekToken(absolutePosition), PeekToken(absolutePosition + 1));

        private static bool IsNationalStringLiteralPrefix(
            MetaWeaveScriptSqlToken prefix,
            MetaWeaveScriptSqlToken stringLiteral) =>
            IsKeyword(prefix, "N")
            && stringLiteral.Kind == MetaWeaveScriptSqlTokenKind.StringLiteral
            && prefix.Offset + prefix.Text.Length == stringLiteral.Offset;

        private sealed record ParsedIdentifier(MetaWeaveScriptSqlToken Token, MetaWeaveScriptSqlModelBuilder.BuiltNode Node);
    }
}
