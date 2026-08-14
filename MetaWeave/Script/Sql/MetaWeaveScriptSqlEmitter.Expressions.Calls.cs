using MetaWeave;

namespace MetaWeaveScript.Sql;

internal sealed partial class MetaWeaveScriptSqlEmitter
{
    private string RenderCoalesceExpression(CoalesceExpression coalesceExpression)
    {
        var expressions = GetOrderedItems(model.CoalesceExpressionExpressionsItemList, coalesceExpression.Id)
            .Select(row => RenderScalarExpression(row.ScalarExpression))
            .ToArray();
        return "COALESCE(" + string.Join(", ", expressions) + ")";
    }

    private string RenderNullIfExpression(NullIfExpression nullIfExpression)
    {
        var first = RenderScalarExpression(GetOwnerLink(
            model.NullIfExpressionFirstExpressionLinkList,
            nullIfExpression.Id,
            "NullIfExpression.FirstExpression").ScalarExpression);
        var second = RenderScalarExpression(GetOwnerLink(
            model.NullIfExpressionSecondExpressionLinkList,
            nullIfExpression.Id,
            "NullIfExpression.SecondExpression").ScalarExpression);
        return $"NULLIF({first}, {second})";
    }

    private string RenderIIfCall(IIfCall iIfCall)
    {
        var predicate = RenderBooleanExpression(GetOwnerLink(
            model.IIfCallPredicateLinkList,
            iIfCall.Id,
            "IIfCall.Predicate").BooleanExpression);
        var thenExpression = RenderScalarExpression(GetOwnerLink(
            model.IIfCallThenExpressionLinkList,
            iIfCall.Id,
            "IIfCall.ThenExpression").ScalarExpression);
        var elseExpression = RenderScalarExpression(GetOwnerLink(
            model.IIfCallElseExpressionLinkList,
            iIfCall.Id,
            "IIfCall.ElseExpression").ScalarExpression);
        return $"IIF({predicate}, {thenExpression}, {elseExpression})";
    }

    private string RenderLiteral(Literal literal)
    {
        if (FindByBaseId(model.StringLiteralList, literal.Id) is not null)
        {
            return "'" + (literal.Value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal) + "'";
        }

        if (FindByBaseId(model.IntegerLiteralList, literal.Id) is not null)
        {
            return RequireLiteralValue(literal);
        }

        if (FindByBaseId(model.NullLiteralList, literal.Id) is not null)
        {
            return "NULL";
        }

        throw new InvalidOperationException($"Unsupported MetaWeaveScript Literal id '{literal.Id}'.");
    }

    private static string RequireLiteralValue(Literal literal)
    {
        return literal.Value ?? throw new InvalidOperationException($"Literal '{literal.Id}' is missing its required value.");
    }

    private string RenderFunctionCall(FunctionCall functionCall)
    {
        var functionName = RenderIdentifier(GetOwnerLink(
            model.FunctionCallFunctionNameLinkList,
            functionCall.Id,
            "FunctionCall.FunctionName").Identifier);
        var args = GetOrderedItems(model.FunctionCallParametersItemList, functionCall.Id)
            .Select(row => RenderScalarExpression(row.ScalarExpression))
            .ToArray();

        var withinGroupOrderByClauseLink = FindOwnerLink(
            model.FunctionCallWithinGroupOrderByClauseLinkList,
            functionCall.Id);
        var withinGroupSuffix = withinGroupOrderByClauseLink is null
            ? string.Empty
            : $" WITHIN GROUP ({RenderOrderByClause(withinGroupOrderByClauseLink.OrderByClause)})";
        return $"{functionName}({string.Join(", ", args)}){withinGroupSuffix}";
    }
}
