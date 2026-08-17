using System.Globalization;
using MetaWeave;

namespace MetaWeaveScript.Sql.Parsing;

internal sealed partial class MetaWeaveScriptSqlModelBuilder
{
    public BuiltNode CreateIdentifier(string value, string quoteType)
    {
        var row = new Identifier
        {
            Id = NextId(nameof(Identifier)),
            Value = value,
            QuoteType = quoteType
        };
        model.IdentifierList.Add(row);
        return BuiltNode.Create((nameof(Identifier), row.Id));
    }

    public BuiltNode CreateIdentifierOrValueExpression(BuiltNode identifier)
    {
        var row = new IdentifierOrValueExpression
        {
            Id = NextId(nameof(IdentifierOrValueExpression))
        };
        model.IdentifierOrValueExpressionList.Add(row);
        model.IdentifierOrValueExpressionIdentifierLinkList.Add(new IdentifierOrValueExpressionIdentifierLink
        {
            Id = NextId(nameof(IdentifierOrValueExpressionIdentifierLink)),
            IdentifierOrValueExpression = row,
            Identifier = identifier.GetRef<Identifier>(nameof(Identifier))
        });
        return BuiltNode.Create((nameof(IdentifierOrValueExpression), row.Id));
    }

    public BuiltNode CreateMultiPartIdentifier(IReadOnlyList<BuiltNode> identifiers)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        if (identifiers.Count == 0)
        {
            throw new InvalidOperationException("MultiPartIdentifier requires at least one Identifier.");
        }

        var row = new MultiPartIdentifier
        {
            Id = NextId(nameof(MultiPartIdentifier))
        };
        model.MultiPartIdentifierList.Add(row);
        for (var ordinal = 0; ordinal < identifiers.Count; ordinal++)
        {
            model.MultiPartIdentifierIdentifiersItemList.Add(new MultiPartIdentifierIdentifiersItem
            {
                Id = NextId(nameof(MultiPartIdentifierIdentifiersItem)),
                MultiPartIdentifier = row,
                Identifier = identifiers[ordinal].GetRef<Identifier>(nameof(Identifier)),
                Ordinal = ordinal.ToString(CultureInfo.InvariantCulture)
            });
        }
        return BuiltNode.Create((nameof(MultiPartIdentifier), row.Id));
    }

    public BuiltNode CreateSchemaObjectName(IReadOnlyList<BuiltNode> identifiers)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        if (identifiers.Count is < 1 or > 2)
        {
            throw new InvalidOperationException("MetaWeaveScript source names require one entity identifier with an optional source-workspace qualifier.");
        }

        var multiPart = CreateMultiPartIdentifier(identifiers);
        var row = new SchemaObjectName
        {
            Id = NextId(nameof(SchemaObjectName)),
            MultiPartIdentifier = multiPart.GetRef<MultiPartIdentifier>(nameof(MultiPartIdentifier))
        };
        model.SchemaObjectNameList.Add(row);
        model.SchemaObjectNameBaseIdentifierLinkList.Add(new SchemaObjectNameBaseIdentifierLink
        {
            Id = NextId(nameof(SchemaObjectNameBaseIdentifierLink)),
            SchemaObjectName = row,
            Identifier = identifiers[^1].GetRef<Identifier>(nameof(Identifier))
        });
        return BuiltNode.Create((nameof(MultiPartIdentifier), multiPart.GetId(nameof(MultiPartIdentifier))), (nameof(SchemaObjectName), row.Id));
    }

    public BuiltNode CreateStringLiteral(string value, bool isNational = false)
    {
        if (isNational)
        {
            throw new InvalidOperationException("MetaWeaveScript does not distinguish national string literals.");
        }

        var scalar = new ScalarExpression
        {
            Id = NextId(nameof(ScalarExpression))
        };
        model.ScalarExpressionList.Add(scalar);

        var primary = new PrimaryExpression
        {
            Id = NextId(nameof(PrimaryExpression)),
            ScalarExpression = scalar
        };
        model.PrimaryExpressionList.Add(primary);

        var valueExpression = new ValueExpression
        {
            Id = NextId(nameof(ValueExpression)),
            PrimaryExpression = primary
        };
        model.ValueExpressionList.Add(valueExpression);

        var literal = new Literal
        {
            Id = NextId(nameof(Literal)),
            ValueExpression = valueExpression,
            Value = value
        };
        model.LiteralList.Add(literal);

        var stringLiteral = new StringLiteral
        {
            Id = NextId(nameof(StringLiteral)),
            Literal = literal
        };
        model.StringLiteralList.Add(stringLiteral);

        return BuiltNode.Create(
            (nameof(ScalarExpression), scalar.Id),
            (nameof(PrimaryExpression), primary.Id),
            (nameof(ValueExpression), valueExpression.Id),
            (nameof(Literal), literal.Id),
            (nameof(StringLiteral), stringLiteral.Id));
    }

    public BuiltNode CreateNumberLiteral(string value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidOperationException($"MetaWeaveScript supports integer literals only; found '{value}'.");
        }
        var scalar = new ScalarExpression
        {
            Id = NextId(nameof(ScalarExpression))
        };
        model.ScalarExpressionList.Add(scalar);

        var primary = new PrimaryExpression
        {
            Id = NextId(nameof(PrimaryExpression)),
            ScalarExpression = scalar
        };
        model.PrimaryExpressionList.Add(primary);

        var valueExpression = new ValueExpression
        {
            Id = NextId(nameof(ValueExpression)),
            PrimaryExpression = primary
        };
        model.ValueExpressionList.Add(valueExpression);

        var literal = new Literal
        {
            Id = NextId(nameof(Literal)),
            ValueExpression = valueExpression,
            Value = value
        };
        model.LiteralList.Add(literal);

        var integerLiteral = new IntegerLiteral
        {
            Id = NextId(nameof(IntegerLiteral)),
            Literal = literal
        };
        model.IntegerLiteralList.Add(integerLiteral);

        return BuiltNode.Create(
            (nameof(ScalarExpression), scalar.Id),
            (nameof(PrimaryExpression), primary.Id),
            (nameof(ValueExpression), valueExpression.Id),
            (nameof(Literal), literal.Id),
            (nameof(IntegerLiteral), integerLiteral.Id));
    }

    public BuiltNode CreateNullLiteral()
    {
        var scalar = new ScalarExpression
        {
            Id = NextId(nameof(ScalarExpression))
        };
        model.ScalarExpressionList.Add(scalar);

        var primary = new PrimaryExpression
        {
            Id = NextId(nameof(PrimaryExpression)),
            ScalarExpression = scalar
        };
        model.PrimaryExpressionList.Add(primary);

        var valueExpression = new ValueExpression
        {
            Id = NextId(nameof(ValueExpression)),
            PrimaryExpression = primary
        };
        model.ValueExpressionList.Add(valueExpression);

        var literal = new Literal
        {
            Id = NextId(nameof(Literal)),
            ValueExpression = valueExpression,
            Value = string.Empty
        };
        model.LiteralList.Add(literal);

        var nullLiteral = new NullLiteral
        {
            Id = NextId(nameof(NullLiteral)),
            Literal = literal
        };
        model.NullLiteralList.Add(nullLiteral);

        return BuiltNode.Create(
            (nameof(ScalarExpression), scalar.Id),
            (nameof(PrimaryExpression), primary.Id),
            (nameof(ValueExpression), valueExpression.Id),
            (nameof(Literal), literal.Id),
            (nameof(NullLiteral), nullLiteral.Id));
    }
}
