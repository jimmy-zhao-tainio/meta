using System.Globalization;
using MetaWeave;

namespace MetaWeaveScript.Sql.Parsing;

internal sealed partial class MetaWeaveScriptSqlModelBuilder
{
    public BuiltNode CreateCoalesceExpression(IReadOnlyList<BuiltNode> expressions)
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

        var coalesceExpression = new CoalesceExpression
        {
            Id = NextId(nameof(CoalesceExpression)),
            PrimaryExpression = primary
        };
        model.CoalesceExpressionList.Add(coalesceExpression);

        for (var ordinal = 0; ordinal < expressions.Count; ordinal++)
        {
            model.CoalesceExpressionExpressionsItemList.Add(new CoalesceExpressionExpressionsItem
            {
                Id = NextId(nameof(CoalesceExpressionExpressionsItem)),
                CoalesceExpression = coalesceExpression,
                ScalarExpression = expressions[ordinal].GetRef<ScalarExpression>(nameof(ScalarExpression)),
                Ordinal = ordinal.ToString(CultureInfo.InvariantCulture)
            });
        }

        return BuiltNode.Create(
            (nameof(ScalarExpression), scalar.Id),
            (nameof(PrimaryExpression), primary.Id),
            (nameof(CoalesceExpression), coalesceExpression.Id));
    }

    public BuiltNode CreateNullIfExpression(BuiltNode firstExpression, BuiltNode secondExpression)
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

        var nullIfExpression = new NullIfExpression
        {
            Id = NextId(nameof(NullIfExpression)),
            PrimaryExpression = primary
        };
        model.NullIfExpressionList.Add(nullIfExpression);
        model.NullIfExpressionFirstExpressionLinkList.Add(new NullIfExpressionFirstExpressionLink
        {
            Id = NextId(nameof(NullIfExpressionFirstExpressionLink)),
            NullIfExpression = nullIfExpression,
            ScalarExpression = firstExpression.GetRef<ScalarExpression>(nameof(ScalarExpression))
        });
        model.NullIfExpressionSecondExpressionLinkList.Add(new NullIfExpressionSecondExpressionLink
        {
            Id = NextId(nameof(NullIfExpressionSecondExpressionLink)),
            NullIfExpression = nullIfExpression,
            ScalarExpression = secondExpression.GetRef<ScalarExpression>(nameof(ScalarExpression))
        });

        return BuiltNode.Create(
            (nameof(ScalarExpression), scalar.Id),
            (nameof(PrimaryExpression), primary.Id),
            (nameof(NullIfExpression), nullIfExpression.Id));
    }

    public BuiltNode CreateIIfCall(BuiltNode predicate, BuiltNode thenExpression, BuiltNode elseExpression)
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

        var iIfCall = new IIfCall
        {
            Id = NextId(nameof(IIfCall)),
            PrimaryExpression = primary
        };
        model.IIfCallList.Add(iIfCall);
        model.IIfCallPredicateLinkList.Add(new IIfCallPredicateLink
        {
            Id = NextId(nameof(IIfCallPredicateLink)),
            IIfCall = iIfCall,
            BooleanExpression = predicate.GetRef<BooleanExpression>(nameof(BooleanExpression))
        });
        model.IIfCallThenExpressionLinkList.Add(new IIfCallThenExpressionLink
        {
            Id = NextId(nameof(IIfCallThenExpressionLink)),
            IIfCall = iIfCall,
            ScalarExpression = thenExpression.GetRef<ScalarExpression>(nameof(ScalarExpression))
        });
        model.IIfCallElseExpressionLinkList.Add(new IIfCallElseExpressionLink
        {
            Id = NextId(nameof(IIfCallElseExpressionLink)),
            IIfCall = iIfCall,
            ScalarExpression = elseExpression.GetRef<ScalarExpression>(nameof(ScalarExpression))
        });

        return BuiltNode.Create(
            (nameof(ScalarExpression), scalar.Id),
            (nameof(PrimaryExpression), primary.Id),
            (nameof(IIfCall), iIfCall.Id));
    }
}
