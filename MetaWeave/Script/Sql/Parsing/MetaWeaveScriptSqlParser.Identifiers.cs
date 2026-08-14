using static MetaWeaveScript.Sql.Parsing.MetaWeaveScriptSqlModelBuilder;

namespace MetaWeaveScript.Sql.Parsing;

public sealed partial class MetaWeaveScriptSqlParser
{
    private sealed partial class Parser
    {
        private ParsedIdentifier ParseIdentifier()
        {
            var token = ParseIdentifierToken();
            return new ParsedIdentifier(token, builder.CreateIdentifier(token.Value, token.QuoteType));
        }

        private List<ParsedIdentifier> ParseIdentifierChain(bool expectTrailingStar = false)
        {
            var identifiers = new List<ParsedIdentifier> { ParseIdentifier() };
            while (Match(MetaWeaveScriptSqlTokenKind.Dot))
            {
                if (expectTrailingStar && Current.Kind == MetaWeaveScriptSqlTokenKind.Star)
                {
                    break;
                }

                identifiers.Add(ParseIdentifier());
            }

            return identifiers;
        }

        private MetaWeaveScriptSqlToken ParseIdentifierToken()
        {
            var token = Current;
            if (token.Kind != MetaWeaveScriptSqlTokenKind.Identifier)
            {
                throw ParseError($"Expected an identifier but found '{token.Text}'.");
            }

            if (string.Equals(token.QuoteType, "MergeActionPseudoColumn", StringComparison.Ordinal))
            {
                throw Unsupported("MetaWeaveScript does not support MERGE $action pseudo-columns.");
            }

            Advance();
            return token;
        }

        private List<MetaWeaveScriptSqlToken> ParseIdentifierTokenChain(bool expectTrailingStar = false)
        {
            var identifiers = new List<MetaWeaveScriptSqlToken> { ParseIdentifierToken() };
            while (Match(MetaWeaveScriptSqlTokenKind.Dot))
            {
                if (expectTrailingStar && Current.Kind == MetaWeaveScriptSqlTokenKind.Star)
                {
                    break;
                }

                identifiers.Add(ParseIdentifierToken());
            }

            return identifiers;
        }

        private BuiltNode ParseMultiPartIdentifier(bool expectTrailingStar = false)
        {
            return builder.CreateMultiPartIdentifier(ParseIdentifierChain(expectTrailingStar).Select(static part => part.Node).ToArray());
        }

        private bool FormsQualifiedStar()
        {
            var probe = position;
            if (PeekToken(probe).Kind != MetaWeaveScriptSqlTokenKind.Identifier)
            {
                return false;
            }

            probe++;
            while (PeekToken(probe).Kind == MetaWeaveScriptSqlTokenKind.Dot)
            {
                probe++;
                if (PeekToken(probe).Kind == MetaWeaveScriptSqlTokenKind.Star)
                {
                    return true;
                }

                if (PeekToken(probe).Kind != MetaWeaveScriptSqlTokenKind.Identifier)
                {
                    return false;
                }

                probe++;
            }

            return false;
        }

        private bool CanStartAlias()
        {
            if (Current.Kind != MetaWeaveScriptSqlTokenKind.Identifier)
            {
                return false;
            }

            return !IsKeyword(Current, ClauseKeywords);
        }
    }
}
