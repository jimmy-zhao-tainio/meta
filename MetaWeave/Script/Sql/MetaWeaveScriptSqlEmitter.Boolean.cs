using MetaWeave;

namespace MetaWeaveScript.Sql;

internal sealed partial class MetaWeaveScriptSqlEmitter
{
    private string RenderBooleanExpression(BooleanExpression booleanExpression)
    {
        var booleanBinary = FindByBaseId(model.BooleanBinaryExpressionList, static row => row.BooleanExpression?.Id, booleanExpression.Id);
        if (booleanBinary is not null)
        {
            var left = RenderBooleanExpression(GetOwnerLink(model.BooleanBinaryExpressionFirstExpressionLinkList, static row => row.BooleanBinaryExpression?.Id, booleanBinary.Id, "BooleanBinaryExpression.FirstExpression").BooleanExpression);
            var right = RenderBooleanExpression(GetOwnerLink(model.BooleanBinaryExpressionSecondExpressionLinkList, static row => row.BooleanBinaryExpression?.Id, booleanBinary.Id, "BooleanBinaryExpression.SecondExpression").BooleanExpression);
            var op = booleanBinary.BinaryExpressionType switch
            {
                "And" => "AND",
                "Or" => "OR",
                _ => throw new InvalidOperationException($"Unsupported MetaWeaveScript BooleanBinaryExpressionType '{booleanBinary.BinaryExpressionType}'.")
            };
            return $"{left} {op} {right}";
        }

        var booleanComparison = FindByBaseId(model.BooleanComparisonExpressionList, static row => row.BooleanExpression?.Id, booleanExpression.Id);
        if (booleanComparison is not null)
        {
            var left = RenderScalarExpression(GetOwnerLink(model.BooleanComparisonExpressionFirstExpressionLinkList, static row => row.BooleanComparisonExpression?.Id, booleanComparison.Id, "BooleanComparisonExpression.FirstExpression").ScalarExpression);
            var right = RenderScalarExpression(GetOwnerLink(model.BooleanComparisonExpressionSecondExpressionLinkList, static row => row.BooleanComparisonExpression?.Id, booleanComparison.Id, "BooleanComparisonExpression.SecondExpression").ScalarExpression);
            return $"{left} {RenderComparisonOperator(booleanComparison.ComparisonType)} {right}";
        }

        var parenthesisExpression = FindByBaseId(model.BooleanParenthesisExpressionList, static row => row.BooleanExpression?.Id, booleanExpression.Id);
        if (parenthesisExpression is not null)
        {
            var inner = GetOwnerLink(model.BooleanParenthesisExpressionExpressionLinkList, static row => row.BooleanParenthesisExpression?.Id, parenthesisExpression.Id, "BooleanParenthesisExpression.Expression").BooleanExpression;
            return $"({RenderBooleanExpression(inner)})";
        }

        var notExpression = FindByBaseId(model.BooleanNotExpressionList, static row => row.BooleanExpression?.Id, booleanExpression.Id);
        if (notExpression is not null)
        {
            var inner = GetOwnerLink(model.BooleanNotExpressionExpressionLinkList, static row => row.BooleanNotExpression?.Id, notExpression.Id, "BooleanNotExpression.Expression").BooleanExpression;
            return $"NOT {RenderBooleanExpression(inner)}";
        }

        var isNullExpression = FindByBaseId(model.BooleanIsNullExpressionList, static row => row.BooleanExpression?.Id, booleanExpression.Id);
        if (isNullExpression is not null)
        {
            var expression = RenderScalarExpression(GetOwnerLink(model.BooleanIsNullExpressionExpressionLinkList, static row => row.BooleanIsNullExpression?.Id, isNullExpression.Id, "BooleanIsNullExpression.Expression").ScalarExpression);
            return $"{expression} IS{(IsTrue(isNullExpression.IsNot) ? " NOT" : string.Empty)} NULL";
        }

        var inPredicate = FindByBaseId(model.InPredicateList, static row => row.BooleanExpression?.Id, booleanExpression.Id);
        if (inPredicate is not null)
        {
            var expression = RenderScalarExpression(GetOwnerLink(model.InPredicateExpressionLinkList, static row => row.InPredicate?.Id, inPredicate.Id, "InPredicate.Expression").ScalarExpression);
            var values = GetOrderedItems(model.InPredicateValuesItemList, static row => row.InPredicate?.Id, static row => row.Ordinal, inPredicate.Id)
                .Select(row => RenderScalarExpression(row.ScalarExpression));
            return $"{expression} IN ({string.Join(", ", values)})";
        }

        var likePredicate = FindByBaseId(model.LikePredicateList, static row => row.BooleanExpression?.Id, booleanExpression.Id);
        if (likePredicate is not null)
        {
            var first = RenderScalarExpression(GetOwnerLink(model.LikePredicateFirstExpressionLinkList, static row => row.LikePredicate?.Id, likePredicate.Id, "LikePredicate.FirstExpression").ScalarExpression);
            var second = RenderScalarExpression(GetOwnerLink(model.LikePredicateSecondExpressionLinkList, static row => row.LikePredicate?.Id, likePredicate.Id, "LikePredicate.SecondExpression").ScalarExpression);
            return $"{first} LIKE {second}";
        }

        var existsPredicate = FindByBaseId(model.ExistsPredicateList, static row => row.BooleanExpression?.Id, booleanExpression.Id);
        if (existsPredicate is not null)
        {
            var subquery = RenderScalarSubquery(GetOwnerLink(model.ExistsPredicateSubqueryLinkList, static row => row.ExistsPredicate?.Id, existsPredicate.Id, "ExistsPredicate.Subquery").ScalarSubquery);
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
