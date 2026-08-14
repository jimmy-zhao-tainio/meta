using System.Globalization;
using MetaWeave;

namespace MetaWeaveScript.Sql.Parsing;

internal sealed partial class MetaWeaveScriptSqlModelBuilder
{
    private BuiltNode CreateBooleanExpressionBase()
    {
        var row = new BooleanExpression
        {
            Id = NextId(nameof(BooleanExpression))
        };
        model.BooleanExpressionList.Add(row);
        return BuiltNode.Create((nameof(BooleanExpression), row.Id));
    }

    public BuiltNode CreateBooleanBinaryExpression(BuiltNode firstExpression, BuiltNode secondExpression, string binaryExpressionType)
    {
        var booleanExpression = CreateBooleanExpressionBase();
        var row = new BooleanBinaryExpression
        {
            Id = NextId(nameof(BooleanBinaryExpression)),
            BooleanExpression = booleanExpression.GetRef<BooleanExpression>(nameof(BooleanExpression)),
            BinaryExpressionType = binaryExpressionType
        };
        model.BooleanBinaryExpressionList.Add(row);
        model.BooleanBinaryExpressionFirstExpressionLinkList.Add(new BooleanBinaryExpressionFirstExpressionLink
        {
            Id = NextId(nameof(BooleanBinaryExpressionFirstExpressionLink)),
            BooleanBinaryExpression = row,
            BooleanExpression = firstExpression.GetRef<BooleanExpression>(nameof(BooleanExpression))
        });
        model.BooleanBinaryExpressionSecondExpressionLinkList.Add(new BooleanBinaryExpressionSecondExpressionLink
        {
            Id = NextId(nameof(BooleanBinaryExpressionSecondExpressionLink)),
            BooleanBinaryExpression = row,
            BooleanExpression = secondExpression.GetRef<BooleanExpression>(nameof(BooleanExpression))
        });
        return BuiltNode.Create((nameof(BooleanExpression), booleanExpression.GetId(nameof(BooleanExpression))), (nameof(BooleanBinaryExpression), row.Id));
    }

    public BuiltNode CreateBooleanComparisonExpression(BuiltNode firstExpression, BuiltNode secondExpression, string comparisonType)
    {
        var booleanExpression = CreateBooleanExpressionBase();
        var row = new BooleanComparisonExpression
        {
            Id = NextId(nameof(BooleanComparisonExpression)),
            BooleanExpression = booleanExpression.GetRef<BooleanExpression>(nameof(BooleanExpression)),
            ComparisonType = comparisonType
        };
        model.BooleanComparisonExpressionList.Add(row);
        model.BooleanComparisonExpressionFirstExpressionLinkList.Add(new BooleanComparisonExpressionFirstExpressionLink
        {
            Id = NextId(nameof(BooleanComparisonExpressionFirstExpressionLink)),
            BooleanComparisonExpression = row,
            ScalarExpression = firstExpression.GetRef<ScalarExpression>(nameof(ScalarExpression))
        });
        model.BooleanComparisonExpressionSecondExpressionLinkList.Add(new BooleanComparisonExpressionSecondExpressionLink
        {
            Id = NextId(nameof(BooleanComparisonExpressionSecondExpressionLink)),
            BooleanComparisonExpression = row,
            ScalarExpression = secondExpression.GetRef<ScalarExpression>(nameof(ScalarExpression))
        });
        return BuiltNode.Create((nameof(BooleanExpression), booleanExpression.GetId(nameof(BooleanExpression))), (nameof(BooleanComparisonExpression), row.Id));
    }

    public BuiltNode CreateBooleanParenthesisExpression(BuiltNode expression)
    {
        var booleanExpression = CreateBooleanExpressionBase();
        var row = new BooleanParenthesisExpression
        {
            Id = NextId(nameof(BooleanParenthesisExpression)),
            BooleanExpression = booleanExpression.GetRef<BooleanExpression>(nameof(BooleanExpression))
        };
        model.BooleanParenthesisExpressionList.Add(row);
        model.BooleanParenthesisExpressionExpressionLinkList.Add(new BooleanParenthesisExpressionExpressionLink
        {
            Id = NextId(nameof(BooleanParenthesisExpressionExpressionLink)),
            BooleanParenthesisExpression = row,
            BooleanExpression = expression.GetRef<BooleanExpression>(nameof(BooleanExpression))
        });
        return BuiltNode.Create((nameof(BooleanExpression), booleanExpression.GetId(nameof(BooleanExpression))), (nameof(BooleanParenthesisExpression), row.Id));
    }

    public BuiltNode CreateBooleanNotExpression(BuiltNode expression)
    {
        var booleanExpression = CreateBooleanExpressionBase();
        var row = new BooleanNotExpression
        {
            Id = NextId(nameof(BooleanNotExpression)),
            BooleanExpression = booleanExpression.GetRef<BooleanExpression>(nameof(BooleanExpression))
        };
        model.BooleanNotExpressionList.Add(row);
        model.BooleanNotExpressionExpressionLinkList.Add(new BooleanNotExpressionExpressionLink
        {
            Id = NextId(nameof(BooleanNotExpressionExpressionLink)),
            BooleanNotExpression = row,
            BooleanExpression = expression.GetRef<BooleanExpression>(nameof(BooleanExpression))
        });
        return BuiltNode.Create((nameof(BooleanExpression), booleanExpression.GetId(nameof(BooleanExpression))), (nameof(BooleanNotExpression), row.Id));
    }

    public BuiltNode CreateBooleanIsNullExpression(BuiltNode expression, bool isNot)
    {
        var booleanExpression = CreateBooleanExpressionBase();
        var row = new BooleanIsNullExpression
        {
            Id = NextId(nameof(BooleanIsNullExpression)),
            BooleanExpression = booleanExpression.GetRef<BooleanExpression>(nameof(BooleanExpression)),
            IsNot = isNot ? "true" : string.Empty
        };
        model.BooleanIsNullExpressionList.Add(row);
        model.BooleanIsNullExpressionExpressionLinkList.Add(new BooleanIsNullExpressionExpressionLink
        {
            Id = NextId(nameof(BooleanIsNullExpressionExpressionLink)),
            BooleanIsNullExpression = row,
            ScalarExpression = expression.GetRef<ScalarExpression>(nameof(ScalarExpression))
        });
        return BuiltNode.Create((nameof(BooleanExpression), booleanExpression.GetId(nameof(BooleanExpression))), (nameof(BooleanIsNullExpression), row.Id));
    }

    public BuiltNode CreateLikePredicate(BuiltNode firstExpression, BuiltNode secondExpression, bool notDefined, BuiltNode? escapeExpression = null)
    {
        if (notDefined || escapeExpression is not null)
        {
            throw new InvalidOperationException("MetaWeaveScript supports LIKE without NOT or ESCAPE.");
        }

        var booleanExpression = CreateBooleanExpressionBase();
        var row = new LikePredicate
        {
            Id = NextId(nameof(LikePredicate)),
            BooleanExpression = booleanExpression.GetRef<BooleanExpression>(nameof(BooleanExpression))
        };
        model.LikePredicateList.Add(row);
        model.LikePredicateFirstExpressionLinkList.Add(new LikePredicateFirstExpressionLink
        {
            Id = NextId(nameof(LikePredicateFirstExpressionLink)),
            LikePredicate = row,
            ScalarExpression = firstExpression.GetRef<ScalarExpression>(nameof(ScalarExpression))
        });
        model.LikePredicateSecondExpressionLinkList.Add(new LikePredicateSecondExpressionLink
        {
            Id = NextId(nameof(LikePredicateSecondExpressionLink)),
            LikePredicate = row,
            ScalarExpression = secondExpression.GetRef<ScalarExpression>(nameof(ScalarExpression))
        });
        return BuiltNode.Create((nameof(BooleanExpression), booleanExpression.GetId(nameof(BooleanExpression))), (nameof(LikePredicate), row.Id));
    }

    public BuiltNode CreateInPredicate(BuiltNode expression, IReadOnlyList<BuiltNode> values, bool notDefined)
    {
        if (notDefined)
        {
            throw new InvalidOperationException("MetaWeaveScript supports IN without NOT.");
        }

        var booleanExpression = CreateBooleanExpressionBase();
        var row = new InPredicate
        {
            Id = NextId(nameof(InPredicate)),
            BooleanExpression = booleanExpression.GetRef<BooleanExpression>(nameof(BooleanExpression))
        };
        model.InPredicateList.Add(row);
        model.InPredicateExpressionLinkList.Add(new InPredicateExpressionLink
        {
            Id = NextId(nameof(InPredicateExpressionLink)),
            InPredicate = row,
            ScalarExpression = expression.GetRef<ScalarExpression>(nameof(ScalarExpression))
        });
        for (var ordinal = 0; ordinal < values.Count; ordinal++)
        {
            model.InPredicateValuesItemList.Add(new InPredicateValuesItem
            {
                Id = NextId(nameof(InPredicateValuesItem)),
                InPredicate = row,
                ScalarExpression = values[ordinal].GetRef<ScalarExpression>(nameof(ScalarExpression)),
                Ordinal = ordinal.ToString(CultureInfo.InvariantCulture)
            });
        }
        return BuiltNode.Create((nameof(BooleanExpression), booleanExpression.GetId(nameof(BooleanExpression))), (nameof(InPredicate), row.Id));
    }

    public BuiltNode CreateExistsPredicate(BuiltNode subquery)
    {
        var booleanExpression = CreateBooleanExpressionBase();
        var row = new ExistsPredicate
        {
            Id = NextId(nameof(ExistsPredicate)),
            BooleanExpression = booleanExpression.GetRef<BooleanExpression>(nameof(BooleanExpression))
        };
        model.ExistsPredicateList.Add(row);
        model.ExistsPredicateSubqueryLinkList.Add(new ExistsPredicateSubqueryLink
        {
            Id = NextId(nameof(ExistsPredicateSubqueryLink)),
            ExistsPredicate = row,
            ScalarSubquery = subquery.GetRef<ScalarSubquery>(nameof(ScalarSubquery))
        });
        return BuiltNode.Create((nameof(BooleanExpression), booleanExpression.GetId(nameof(BooleanExpression))), (nameof(ExistsPredicate), row.Id));
    }
}
