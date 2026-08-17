using System.Text;
using MetaWeave;

namespace MetaWeaveScript.Sql;

internal sealed partial class MetaWeaveScriptSqlEmitter
{
    internal string RenderScalarExpressionForScriptObject(ScalarExpression scalarExpression) =>
        RenderScalarExpression(scalarExpression);

    private string RenderScalarExpression(ScalarExpression scalarExpression)
    {
        var primaryExpression = FindByBaseId(model.PrimaryExpressionList, scalarExpression.Id)
            ?? throw new InvalidOperationException($"Unsupported MetaWeaveScript ScalarExpression id '{scalarExpression.Id}'.");
        return RenderPrimaryExpression(primaryExpression);
    }

    private string RenderPrimaryExpression(PrimaryExpression primaryExpression)
    {
        string rendered;

        if (FindByBaseId(model.ColumnReferenceExpressionList, primaryExpression.Id) is { } columnReference)
        {
            rendered = RenderColumnReferenceExpression(columnReference);
        }
        else if (FindByBaseId(model.ParenthesisExpressionList, primaryExpression.Id) is { } parenthesisExpression)
        {
            rendered = "(" + RenderScalarExpression(GetOwnerLink(
                model.ParenthesisExpressionExpressionLinkList,
                parenthesisExpression.Id,
                "ParenthesisExpression.Expression").ScalarExpression) + ")";
        }
        else if (FindByBaseId(model.CaseExpressionList, primaryExpression.Id) is { } caseExpression)
        {
            if (FindByBaseId(model.SearchedCaseExpressionList, caseExpression.Id) is { } searchedCaseExpression)
            {
                rendered = RenderSearchedCaseExpression(searchedCaseExpression);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported MetaWeaveScript CaseExpression id '{caseExpression.Id}'.");
            }
        }
        else if (FindByBaseId(model.CoalesceExpressionList, primaryExpression.Id) is { } coalesceExpression)
        {
            rendered = RenderCoalesceExpression(coalesceExpression);
        }
        else if (FindByBaseId(model.NullIfExpressionList, primaryExpression.Id) is { } nullIfExpression)
        {
            rendered = RenderNullIfExpression(nullIfExpression);
        }
        else if (FindByBaseId(model.IIfCallList, primaryExpression.Id) is { } iIfCall)
        {
            rendered = RenderIIfCall(iIfCall);
        }
        else if (FindByBaseId(model.TryConvertCallList, primaryExpression.Id) is { } tryConvertCall)
        {
            rendered = RenderTryConvertCall(tryConvertCall);
        }
        else if (FindByBaseId(model.FunctionCallList, primaryExpression.Id) is { } functionCall)
        {
            rendered = RenderFunctionCall(functionCall);
        }
        else if (FindByBaseId(model.ScalarSubqueryList, primaryExpression.Id) is { } scalarSubquery)
        {
            rendered = RenderScalarSubquery(scalarSubquery);
        }
        else if (FindByBaseId(model.ValueExpressionList, primaryExpression.Id) is { } valueExpression)
        {
            rendered = RenderValueExpression(valueExpression);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported MetaWeaveScript PrimaryExpression id '{primaryExpression.Id}'.");
        }

        return rendered;
    }

    private string RenderScalarSubquery(ScalarSubquery scalarSubquery)
    {
        var queryExpression = GetOwnerLink(
            model.ScalarSubqueryQueryExpressionLinkList,
            scalarSubquery.Id,
            "ScalarSubquery.QueryExpression").QueryExpression;
        return "(" + RenderQueryExpression(queryExpression) + ")";
    }

    private string RenderColumnReferenceExpression(ColumnReferenceExpression columnReference)
    {
        var multiPartIdentifierLink = FindOwnerLink(model.ColumnReferenceExpressionMultiPartIdentifierLinkList, columnReference.Id);
        if (multiPartIdentifierLink is not null)
        {
            return RenderMultiPartIdentifier(multiPartIdentifierLink.MultiPartIdentifier);
        }

        if (string.Equals(columnReference.ColumnType, "Wildcard", StringComparison.Ordinal))
        {
            return "*";
        }

        throw new InvalidOperationException($"Unsupported MetaWeaveScript ColumnReferenceExpression id '{columnReference.Id}'.");
    }

    private string RenderValueExpression(ValueExpression valueExpression)
    {
        var literal = FindByBaseId(model.LiteralList, valueExpression.Id);
        if (literal is not null)
        {
            return RenderLiteral(literal);
        }

        var parameter = FindByBaseId(model.ParameterReferenceExpressionList, valueExpression.Id);
        if (parameter is not null)
        {
            if (!IsPlainIdentifier(parameter.Name))
            {
                throw new InvalidOperationException(
                    $"ParameterReferenceExpression '{parameter.Id}' has invalid name '{parameter.Name}'.");
            }

            return "@" + parameter.Name;
        }

        throw new InvalidOperationException($"Unsupported MetaWeaveScript ValueExpression id '{valueExpression.Id}'.");
    }

    private string RenderSearchedCaseExpression(SearchedCaseExpression searchedCaseExpression)
    {
        var caseExpression = GetById(model.CaseExpressionList, searchedCaseExpression.CaseExpression.Id, "SearchedCaseExpression.Base");
        var whenClauses = GetOrderedItems(model.SearchedCaseExpressionWhenClausesItemList, searchedCaseExpression.Id)
            .Select(row => RenderSearchedWhenClause(row.SearchedWhenClause))
            .ToArray();
        return RenderCaseExpression("CASE", whenClauses, caseExpression);
    }

    private string RenderCaseExpression(string header, IReadOnlyList<string> whenClauses, CaseExpression caseExpression)
    {
        if (whenClauses.Count == 0)
        {
            throw new InvalidOperationException($"CaseExpression '{caseExpression.Id}' had no WHEN clauses.");
        }

        var builder = new StringBuilder();
        builder.AppendLine(header);

        foreach (var whenClause in whenClauses)
        {
            builder.Append("    ");
            builder.AppendLine(whenClause);
        }

        var elseExpressionLink = FindOwnerLink(model.CaseExpressionElseExpressionLinkList, caseExpression.Id);
        if (elseExpressionLink is not null)
        {
            builder.Append("    ELSE ");
            builder.AppendLine(RenderScalarExpression(elseExpressionLink.ScalarExpression));
        }

        builder.Append("END");
        return builder.ToString();
    }

    private string RenderSearchedWhenClause(SearchedWhenClause searchedWhenClause)
    {
        var whenClause = GetById(model.WhenClauseList, searchedWhenClause.WhenClause.Id, "SearchedWhenClause.Base");
        var whenExpression = RenderBooleanExpression(GetOwnerLink(
            model.SearchedWhenClauseWhenExpressionLinkList,
            searchedWhenClause.Id,
            "SearchedWhenClause.WhenExpression").BooleanExpression);
        var thenExpression = RenderScalarExpression(GetOwnerLink(
            model.WhenClauseThenExpressionLinkList,
            whenClause.Id,
            "WhenClause.ThenExpression").ScalarExpression);
        return $"WHEN {whenExpression} THEN {thenExpression}";
    }
}
