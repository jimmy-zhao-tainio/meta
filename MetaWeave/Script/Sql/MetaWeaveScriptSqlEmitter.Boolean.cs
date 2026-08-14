using MetaWeave;

namespace MetaWeaveScript.Sql;

internal sealed partial class MetaWeaveScriptSqlEmitter
{
    private string RenderBooleanExpression(BooleanExpression booleanExpression)
    {
        var booleanBinary = FindByBaseId(model.BooleanBinaryExpressionList, booleanExpression.Id);
        if (booleanBinary is not null)
        {
            var left = RenderBooleanExpression(GetOwnerLink(model.BooleanBinaryExpressionFirstExpressionLinkList, booleanBinary.Id, "BooleanBinaryExpression.FirstExpression").BooleanExpression);
            var right = RenderBooleanExpression(GetOwnerLink(model.BooleanBinaryExpressionSecondExpressionLinkList, booleanBinary.Id, "BooleanBinaryExpression.SecondExpression").BooleanExpression);
            var op = booleanBinary.BinaryExpressionType switch
            {
                "And" => "AND",
                "Or" => "OR",
                _ => throw new InvalidOperationException($"Unsupported MetaWeaveScript BooleanBinaryExpressionType '{booleanBinary.BinaryExpressionType}'.")
            };
            return $"{left} {op} {right}";
        }

        var booleanComparison = FindByBaseId(model.BooleanComparisonExpressionList, booleanExpression.Id);
        if (booleanComparison is not null)
        {
            var left = RenderScalarExpression(GetOwnerLink(model.BooleanComparisonExpressionFirstExpressionLinkList, booleanComparison.Id, "BooleanComparisonExpression.FirstExpression").ScalarExpression);
            var right = RenderScalarExpression(GetOwnerLink(model.BooleanComparisonExpressionSecondExpressionLinkList, booleanComparison.Id, "BooleanComparisonExpression.SecondExpression").ScalarExpression);
            return $"{left} {RenderComparisonOperator(booleanComparison.ComparisonType)} {right}";
        }

        var parenthesisExpression = FindByBaseId(model.BooleanParenthesisExpressionList, booleanExpression.Id);
        if (parenthesisExpression is not null)
        {
            var inner = GetOwnerLink(model.BooleanParenthesisExpressionExpressionLinkList, parenthesisExpression.Id, "BooleanParenthesisExpression.Expression").BooleanExpression;
            return $"({RenderBooleanExpression(inner)})";
        }

        var notExpression = FindByBaseId(model.BooleanNotExpressionList, booleanExpression.Id);
        if (notExpression is not null)
        {
            var inner = GetOwnerLink(model.BooleanNotExpressionExpressionLinkList, notExpression.Id, "BooleanNotExpression.Expression").BooleanExpression;
            return $"NOT {RenderBooleanExpression(inner)}";
        }

        var isNullExpression = FindByBaseId(model.BooleanIsNullExpressionList, booleanExpression.Id);
        if (isNullExpression is not null)
        {
            var expression = RenderScalarExpression(GetOwnerLink(model.BooleanIsNullExpressionExpressionLinkList, isNullExpression.Id, "BooleanIsNullExpression.Expression").ScalarExpression);
            return $"{expression} IS{(IsTrue(isNullExpression.IsNot) ? " NOT" : string.Empty)} NULL";
        }

        var inPredicate = FindByBaseId(model.InPredicateList, booleanExpression.Id);
        if (inPredicate is not null)
        {
            var expression = RenderScalarExpression(GetOwnerLink(model.InPredicateExpressionLinkList, inPredicate.Id, "InPredicate.Expression").ScalarExpression);
            var values = GetOrderedItems(model.InPredicateValuesItemList, inPredicate.Id)
                .Select(row => RenderScalarExpression(row.ScalarExpression));
            return $"{expression} IN ({string.Join(", ", values)})";
        }

        var likePredicate = FindByBaseId(model.LikePredicateList, booleanExpression.Id);
        if (likePredicate is not null)
        {
            var first = RenderScalarExpression(GetOwnerLink(model.LikePredicateFirstExpressionLinkList, likePredicate.Id, "LikePredicate.FirstExpression").ScalarExpression);
            var second = RenderScalarExpression(GetOwnerLink(model.LikePredicateSecondExpressionLinkList, likePredicate.Id, "LikePredicate.SecondExpression").ScalarExpression);
            return $"{first} LIKE {second}";
        }

        var existsPredicate = FindByBaseId(model.ExistsPredicateList, booleanExpression.Id);
        if (existsPredicate is not null)
        {
            var subquery = RenderScalarSubquery(GetOwnerLink(model.ExistsPredicateSubqueryLinkList, existsPredicate.Id, "ExistsPredicate.Subquery").ScalarSubquery);
            return "EXISTS " + subquery;
        }

        throw new InvalidOperationException($"Unsupported MetaWeaveScript BooleanExpression id '{booleanExpression.Id}'.");
    }

    private static string RenderComparisonOperator(string? comparisonType) => comparisonType switch
    {
        "Equals" => "=",
        "GreaterThan" => ">",
        "GreaterThanOrEqualTo" => ">=",
        "LessThan" => "<",
        "LessThanOrEqualTo" => "<=",
        "NotEqualToBrackets" => "<>",
        _ => throw new InvalidOperationException($"Unsupported MetaWeaveScript ComparisonType '{comparisonType}'.")
    };
}
