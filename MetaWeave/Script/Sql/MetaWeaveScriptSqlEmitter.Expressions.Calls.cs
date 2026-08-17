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
        var overClauseLink = FindOwnerLink(model.FunctionCallOverClauseLinkList, functionCall.Id);
        var overSuffix = overClauseLink is null
            ? string.Empty
            : " " + RenderOverClause(overClauseLink.OverClause);
        return $"{functionName}({string.Join(", ", args)}){withinGroupSuffix}{overSuffix}";
    }

    private string RenderTryConvertCall(TryConvertCall tryConvertCall)
    {
        var dataType = RenderDataTypeReference(GetOwnerLink(
            model.TryConvertCallDataTypeLinkList,
            tryConvertCall.Id,
            "TryConvertCall.DataType").DataTypeReference);
        var parameter = RenderScalarExpression(GetOwnerLink(
            model.TryConvertCallParameterLinkList,
            tryConvertCall.Id,
            "TryConvertCall.Parameter").ScalarExpression);
        return $"TRY_CONVERT({dataType}, {parameter})";
    }

    private string RenderDataTypeReference(DataTypeReference dataTypeReference)
    {
        var parameterized = FindByBaseId(model.ParameterizedDataTypeReferenceList, dataTypeReference.Id)
            ?? throw new InvalidOperationException(
                $"Unsupported MetaWeaveScript DataTypeReference id '{dataTypeReference.Id}'.");
        var sqlDataType = FindByBaseId(model.SqlDataTypeReferenceList, parameterized.Id)
            ?? throw new InvalidOperationException(
                $"Unsupported MetaWeaveScript ParameterizedDataTypeReference id '{parameterized.Id}'.");
        return string.Equals(sqlDataType.SqlDataTypeOption, "Int", StringComparison.Ordinal)
            ? "int"
            : throw new InvalidOperationException(
                $"Unsupported MetaWeaveScript SqlDataTypeOption '{sqlDataType.SqlDataTypeOption}'.");
    }

    private string RenderOverClause(OverClause overClause)
    {
        var parts = new List<string>();
        var partitions = GetOrderedItems(model.OverClausePartitionsItemList, overClause.Id)
            .Select(row => RenderScalarExpression(row.ScalarExpression))
            .ToArray();
        if (partitions.Length > 0)
        {
            parts.Add("PARTITION BY " + string.Join(", ", partitions));
        }

        var orderByClause = GetOwnerLink(
            model.OverClauseOrderByClauseLinkList,
            overClause.Id,
            "OverClause.OrderByClause").OrderByClause;
        parts.Add(RenderOrderByClause(orderByClause));
        return "OVER (" + string.Join(" ", parts) + ")";
    }
}
