using System.Globalization;

namespace MetaWeaveScript.Sql.Parsing;

internal enum MetaWeaveScriptSqlTokenKind
{
    Identifier,
    StringLiteral,
    NumberLiteral,
    BinaryLiteral,
    Comma,
    Dot,
    Star,
    Slash,
    Percent,
    OpenParen,
    CloseParen,
    Plus,
    Minus,
    Equals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    NotEqual,
    Semicolon,
    EndOfFile
}

internal readonly record struct MetaWeaveScriptSqlToken(
    MetaWeaveScriptSqlTokenKind Kind,
    string Text,
    string Value,
    string QuoteType,
    int Offset,
    int Line,
    int Column);

internal sealed class MetaWeaveScriptSqlLexer
{
    private readonly string text;
    private int index;
    private int line = 1;
    private int column = 1;

    public MetaWeaveScriptSqlLexer(string text)
    {
        this.text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public IReadOnlyList<MetaWeaveScriptSqlToken> Tokenize()
    {
        var tokens = new List<MetaWeaveScriptSqlToken>();

        while (true)
        {
            SkipTrivia();

            if (IsEnd)
            {
                tokens.Add(new MetaWeaveScriptSqlToken(
                    MetaWeaveScriptSqlTokenKind.EndOfFile,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    index,
                    line,
                    column));
                return tokens;
            }

            tokens.Add(Current switch
            {
                '[' => ReadBracketIdentifier(),
                '"' => ReadDoubleQuotedIdentifier(),
                '`' => ReadBacktickQuotedIdentifier(),
                '\'' => ReadStringLiteral(),
                ',' => ReadSingleCharacterToken(MetaWeaveScriptSqlTokenKind.Comma),
                '.' when char.IsDigit(Peek(1)) => ReadNumberLiteral(),
                '.' => ReadSingleCharacterToken(MetaWeaveScriptSqlTokenKind.Dot),
                '*' => ReadSingleCharacterToken(MetaWeaveScriptSqlTokenKind.Star),
                '/' => ReadSingleCharacterToken(MetaWeaveScriptSqlTokenKind.Slash),
                '%' => ReadSingleCharacterToken(MetaWeaveScriptSqlTokenKind.Percent),
                '(' => ReadSingleCharacterToken(MetaWeaveScriptSqlTokenKind.OpenParen),
                ')' => ReadSingleCharacterToken(MetaWeaveScriptSqlTokenKind.CloseParen),
                '+' => ReadSingleCharacterToken(MetaWeaveScriptSqlTokenKind.Plus),
                '-' => ReadSingleCharacterToken(MetaWeaveScriptSqlTokenKind.Minus),
                ';' => ReadSingleCharacterToken(MetaWeaveScriptSqlTokenKind.Semicolon),
                '=' => ReadSingleCharacterToken(MetaWeaveScriptSqlTokenKind.Equals),
                '>' => ReadGreaterThan(),
                '<' => ReadLessThan(),
                '!' => ReadExclamationMark(),
                '$' => ReadMergeActionPseudoColumn(),
                _ when IsIdentifierStart(Current) => ReadIdentifier(),
                _ when char.IsDigit(Current) => ReadNumberLiteral(),
                _ => throw Error(
                    MetaWeaveScriptSqlParserFailureKind.ParseError,
                    $"Unexpected character '{Current}'.")
            });
        }
    }

    private bool IsEnd => index >= text.Length;

    private char Current => IsEnd ? '\0' : text[index];

    private char Peek(int offset)
    {
        var target = index + offset;
        return target >= 0 && target < text.Length ? text[target] : '\0';
    }

    private void SkipTrivia()
    {
        while (!IsEnd)
        {
            // UTF-8 non-breaking spaces decoded as Windows-1252 appear as "Â ".
            // Treat that common export artifact as trivia when it appears between tokens.
            if (Current == '\u00C2' && Peek(1) == '\u00A0')
            {
                Advance();
                Advance();
                continue;
            }

            // Some script exports normalize the NBSP continuation byte to ordinary
            // whitespace, leaving orphan "Â " indentation before comments/tokens.
            if (Current == '\u00C2' && char.IsWhiteSpace(Peek(1)))
            {
                Advance();
                continue;
            }

            if (char.IsWhiteSpace(Current))
            {
                Advance();
                continue;
            }

            if (Current == '-' && Peek(1) == '-')
            {
                Advance();
                Advance();
                while (!IsEnd && !IsLineTerminator(Current))
                {
                    Advance();
                }

                continue;
            }

            if (Current == '/' && Peek(1) == '*')
            {
                Advance();
                Advance();
                while (!IsEnd && !(Current == '*' && Peek(1) == '/'))
                {
                    Advance();
                }

                if (IsEnd)
                {
                    throw Error(
                        MetaWeaveScriptSqlParserFailureKind.ParseError,
                        "Unterminated block comment.");
                }

                Advance();
                Advance();
                continue;
            }

            break;
        }
    }

    private MetaWeaveScriptSqlToken ReadIdentifier()
    {
        var startOffset = index;
        var startLine = line;
        var startColumn = column;

        Advance();
        while (!IsEnd && IsIdentifierPart(Current, text[index - 1]))
        {
            Advance();
        }

        var value = text[startOffset..index];
        return new MetaWeaveScriptSqlToken(
            MetaWeaveScriptSqlTokenKind.Identifier,
            value,
            value,
            "NotQuoted",
            startOffset,
            startLine,
            startColumn);
    }

    private MetaWeaveScriptSqlToken ReadBracketIdentifier()
    {
        var startOffset = index;
        var startLine = line;
        var startColumn = column;
        Advance();

        var builder = new System.Text.StringBuilder();
        while (!IsEnd)
        {
            if (Current == ']')
            {
                if (Peek(1) == ']')
                {
                    builder.Append(']');
                    Advance();
                    Advance();
                    continue;
                }

                Advance();
                return new MetaWeaveScriptSqlToken(
                    MetaWeaveScriptSqlTokenKind.Identifier,
                    text[startOffset..index],
                    builder.ToString(),
                    "SquareBracket",
                    startOffset,
                    startLine,
                    startColumn);
            }

            builder.Append(Current);
            Advance();
        }

        throw Error(
            MetaWeaveScriptSqlParserFailureKind.ParseError,
            "Unterminated bracketed identifier.");
    }

    private MetaWeaveScriptSqlToken ReadDoubleQuotedIdentifier()
    {
        var startOffset = index;
        var startLine = line;
        var startColumn = column;
        Advance();

        var builder = new System.Text.StringBuilder();
        while (!IsEnd)
        {
            if (Current == '"')
            {
                if (Peek(1) == '"')
                {
                    builder.Append('"');
                    Advance();
                    Advance();
                    continue;
                }

                Advance();
                return new MetaWeaveScriptSqlToken(
                    MetaWeaveScriptSqlTokenKind.Identifier,
                    text[startOffset..index],
                    builder.ToString(),
                    "DoubleQuote",
                    startOffset,
                    startLine,
                    startColumn);
            }

            builder.Append(Current);
            Advance();
        }

        throw Error(
            MetaWeaveScriptSqlParserFailureKind.ParseError,
            "Unterminated double-quoted identifier.");
    }

    private MetaWeaveScriptSqlToken ReadBacktickQuotedIdentifier()
    {
        var startOffset = index;
        var startLine = line;
        var startColumn = column;
        Advance();

        var builder = new System.Text.StringBuilder();
        while (!IsEnd)
        {
            if (Current == '`')
            {
                if (Peek(1) == '`')
                {
                    builder.Append('`');
                    Advance();
                    Advance();
                    continue;
                }

                Advance();
                return new MetaWeaveScriptSqlToken(
                    MetaWeaveScriptSqlTokenKind.Identifier,
                    text[startOffset..index],
                    builder.ToString(),
                    "Backtick",
                    startOffset,
                    startLine,
                    startColumn);
            }

            builder.Append(Current);
            Advance();
        }

        throw Error(
            MetaWeaveScriptSqlParserFailureKind.ParseError,
            "Unterminated backtick-quoted identifier.");
    }

    private MetaWeaveScriptSqlToken ReadStringLiteral()
    {
        var startOffset = index;
        var startLine = line;
        var startColumn = column;
        Advance();

        var builder = new System.Text.StringBuilder();
        while (!IsEnd)
        {
            if (Current == '\'')
            {
                if (Peek(1) == '\'')
                {
                    builder.Append('\'');
                    Advance();
                    Advance();
                    continue;
                }

                Advance();
                return new MetaWeaveScriptSqlToken(
                    MetaWeaveScriptSqlTokenKind.StringLiteral,
                    text[startOffset..index],
                    builder.ToString(),
                    string.Empty,
                    startOffset,
                    startLine,
                    startColumn);
            }

            builder.Append(Current);
            Advance();
        }

        throw Error(
            MetaWeaveScriptSqlParserFailureKind.ParseError,
            "Unterminated string literal.");
    }

    private MetaWeaveScriptSqlToken ReadMergeActionPseudoColumn()
    {
        const string pseudoColumn = "$action";
        var startOffset = index;
        var startLine = line;
        var startColumn = column;

        if (!text.AsSpan(index).StartsWith(pseudoColumn, StringComparison.OrdinalIgnoreCase))
        {
            throw Error(
                MetaWeaveScriptSqlParserFailureKind.ParseError,
                "Only the MERGE OUTPUT pseudo-column '$action' may start with '$'.");
        }

        for (var i = 0; i < pseudoColumn.Length; i++)
        {
            Advance();
        }

        if (!IsEnd && IsIdentifierPart(Current))
        {
            throw Error(
                MetaWeaveScriptSqlParserFailureKind.ParseError,
                "Only the MERGE OUTPUT pseudo-column '$action' may start with '$'.");
        }

        return new MetaWeaveScriptSqlToken(
            MetaWeaveScriptSqlTokenKind.Identifier,
            text[startOffset..index],
            pseudoColumn,
            "MergeActionPseudoColumn",
            startOffset,
            startLine,
            startColumn);
    }

    private MetaWeaveScriptSqlToken ReadNumberLiteral()
    {
        var startOffset = index;
        var startLine = line;
        var startColumn = column;
        var startsWithDot = Current == '.';

        if (Current == '0' && (Peek(1) is 'x' or 'X'))
        {
            Advance();
            Advance();
            if (IsEnd || !IsHexDigit(Current))
            {
                throw Error(
                    MetaWeaveScriptSqlParserFailureKind.ParseError,
                    "Expected hexadecimal digits after binary literal prefix.");
            }

            while (!IsEnd && IsHexDigit(Current))
            {
                Advance();
            }

            var binaryValue = text[startOffset..index];
            return new MetaWeaveScriptSqlToken(
                MetaWeaveScriptSqlTokenKind.BinaryLiteral,
                binaryValue,
                binaryValue,
                string.Empty,
                startOffset,
                startLine,
                startColumn);
        }

        while (!IsEnd && char.IsDigit(Current))
        {
            Advance();
        }

        if (!IsEnd && Current == '.' && char.IsDigit(Peek(1)))
        {
            Advance();
            while (!IsEnd && char.IsDigit(Current))
            {
                Advance();
            }
        }

        if (!IsEnd && (Current is 'E' or 'e'))
        {
            var exponentStart = index;
            Advance();

            if (!IsEnd && (Current is '+' or '-'))
            {
                Advance();
            }

            if (IsEnd || !char.IsDigit(Current))
            {
                if (startsWithDot)
                {
                    throw Error(
                        MetaWeaveScriptSqlParserFailureKind.ParseError,
                        "Expected decimal digits after the exponent in a leading-dot numeric literal.");
                }

                index = exponentStart;
            }
            else
            {
                while (!IsEnd && char.IsDigit(Current))
                {
                    Advance();
                }
            }
        }

        var value = text[startOffset..index];
        return new MetaWeaveScriptSqlToken(
            MetaWeaveScriptSqlTokenKind.NumberLiteral,
            value,
            value,
            string.Empty,
            startOffset,
            startLine,
            startColumn);
    }

    private MetaWeaveScriptSqlToken ReadSingleCharacterToken(MetaWeaveScriptSqlTokenKind kind)
    {
        var token = new MetaWeaveScriptSqlToken(
            kind,
            Current.ToString(),
            Current.ToString(),
            string.Empty,
            index,
            line,
            column);
        Advance();
        return token;
    }

    private MetaWeaveScriptSqlToken ReadGreaterThan()
    {
        var startOffset = index;
        var startLine = line;
        var startColumn = column;
        Advance();

        if (!IsEnd && Current == '=')
        {
            Advance();
            return new MetaWeaveScriptSqlToken(
                MetaWeaveScriptSqlTokenKind.GreaterThanOrEqual,
                ">=",
                ">=",
                string.Empty,
                startOffset,
                startLine,
                startColumn);
        }

        return new MetaWeaveScriptSqlToken(
            MetaWeaveScriptSqlTokenKind.GreaterThan,
            ">",
            ">",
            string.Empty,
            startOffset,
            startLine,
            startColumn);
    }

    private MetaWeaveScriptSqlToken ReadLessThan()
    {
        var startOffset = index;
        var startLine = line;
        var startColumn = column;
        Advance();

        if (!IsEnd && Current == '=')
        {
            Advance();
            return new MetaWeaveScriptSqlToken(
                MetaWeaveScriptSqlTokenKind.LessThanOrEqual,
                "<=",
                "<=",
                string.Empty,
                startOffset,
                startLine,
                startColumn);
        }

        if (!IsEnd && Current == '>')
        {
            Advance();
            return new MetaWeaveScriptSqlToken(
                MetaWeaveScriptSqlTokenKind.NotEqual,
                "<>",
                "<>",
                string.Empty,
                startOffset,
                startLine,
                startColumn);
        }

        return new MetaWeaveScriptSqlToken(
            MetaWeaveScriptSqlTokenKind.LessThan,
            "<",
            "<",
            string.Empty,
            startOffset,
            startLine,
            startColumn);
    }

    private MetaWeaveScriptSqlToken ReadExclamationMark()
    {
        var startOffset = index;
        var startLine = line;
        var startColumn = column;
        Advance();

        if (!IsEnd && Current == '=')
        {
            Advance();
            return new MetaWeaveScriptSqlToken(
                MetaWeaveScriptSqlTokenKind.NotEqual,
                "!=",
                "!=",
                string.Empty,
                startOffset,
                startLine,
                startColumn);
        }

        throw Error(
            MetaWeaveScriptSqlParserFailureKind.ParseError,
            "Unexpected character '!'.");
    }

    private void Advance()
    {
        if (IsEnd)
        {
            return;
        }

        if (Current == '\r')
        {
            index++;
            if (!IsEnd && Current == '\n')
            {
                index++;
            }

            line++;
            column = 1;
            return;
        }

        if (Current == '\n')
        {
            index++;
            line++;
            column = 1;
            return;
        }

        if (Current is '\u0085' or '\u2028' or '\u2029')
        {
            index++;
            line++;
            column = 1;
            return;
        }

        index++;
        column++;
    }

    private static bool IsLineTerminator(char value) =>
        value is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029';

    private static bool IsIdentifierStart(char value) =>
        char.IsLetter(value) || value is '_' or '@' or '#';

    private static bool IsIdentifierPart(char value) =>
        char.IsLetterOrDigit(value) ||
        IsUnicodeIdentifierContinuation(value) ||
        IsLegacyMojibakeIdentifierPart(value) ||
        value is '_' or '@' or '#' or '$';

    private static bool IsIdentifierPart(char value, char previous) =>
        IsIdentifierPart(value) ||
        value == '\u00A0' && IsLegacyMojibakeLead(previous);

    private static bool IsUnicodeIdentifierContinuation(char value) =>
        CharUnicodeInfo.GetUnicodeCategory(value) is
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.ConnectorPunctuation or
            UnicodeCategory.Format;

    // UTF-8-as-Windows-1252 continuation glyphs found in legacy SQL identifiers.
    private static bool IsLegacyMojibakeIdentifierPart(char value) =>
        value is >= '\u00A1' and <= '\u00BF' or
            '\u0192' or '\u02C6' or '\u02DC' or '\u0152' or '\u0153' or '\u0160' or '\u0161' or
            '\u0178' or '\u017D' or '\u017E' or
            '\u2013' or '\u2014' or '\u2018' or '\u2019' or '\u201A' or '\u201C' or '\u201D' or
            '\u201E' or '\u2020' or '\u2021' or '\u2022' or '\u2026' or '\u2030' or '\u2039' or
            '\u203A' or '\u20AC' or '\u2122';

    private static bool IsLegacyMojibakeLead(char value) =>
        value is '\u00C2' or '\u00C3' or '\u00C4' or '\u00C5' or '\u00E2';

    private static bool IsHexDigit(char value) =>
        value is >= '0' and <= '9'
        or >= 'a' and <= 'f'
        or >= 'A' and <= 'F';

    private MetaWeaveScriptSqlParserException Error(
        MetaWeaveScriptSqlParserFailureKind failureKind,
        string message) =>
        new(failureKind, message, line, column, index);
}
