using System.Text;
using MetaWeave;

namespace MetaWeaveScript.Sql;

internal sealed partial class MetaWeaveScriptSqlEmitter
{
    internal string RenderScalarExpressionForScriptObject(ScalarExpression scalarExpression) =>
        RenderScalarExpression(scalarExpression);

    private string RenderScalarExpression(ScalarExpression scalarExpression)
    {
        var primaryExpression = FindByBaseId(model.PrimaryExpressionList, static row => row.ScalarExpression?.Id, scalarExpression.Id)
            ?? throw new InvalidOperationException($"Unsupported MetaWeaveScript ScalarExpression id '{scalarExpression.Id}'.");
        return RenderPrimaryExpression(primaryExpression);
    }

    private string RenderPrimaryExpression(PrimaryExpression primaryExpression)
    {
        string rendered;

        if (FindByBaseId(model.ColumnReferenceExpressionList, static row => row.PrimaryExpression?.Id, primaryExpression.Id) is { } columnReference)
        {
            rendered = RenderColumnReferenceExpression(columnReference);
        }
        else if (FindByBaseId(model.ParenthesisExpressionList, static row => row.PrimaryExpression?.Id, primaryExpression.Id) is { } parenthesisExpression)
        {
            rendered = "(" + RenderScalarExpression(GetOwnerLink(
                model.ParenthesisExpressionExpressionLinkList, static row => row.ParenthesisExpression?.Id,
                parenthesisExpression.Id,
                "ParenthesisExpression.Expression").ScalarExpression) + ")";
        }
        else if (FindByBaseId(model.CaseExpressionList, static row => row.PrimaryExpression?.Id, primaryExpression.Id) is { } caseExpression)
        {
            if (FindByBaseId(model.SearchedCaseExpressionList, static row => row.CaseExpression?.Id, caseExpression.Id) is { } searchedCaseExpression)
            {
                rendered = RenderSearchedCaseExpression(searchedCaseExpression);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported MetaWeaveScript CaseExpression id '{caseExpression.Id}'.");
            }
        }
        else if (FindByBaseId(model.CoalesceExpressionList, static row => row.PrimaryExpression?.Id, primaryExpression.Id) is { } coalesceExpression)
        {
            rendered = RenderCoalesceExpression(coalesceExpression);
        }
        else if (FindByBaseId(model.NullIfExpressionList, static row => row.PrimaryExpression?.Id, primaryExpression.Id) is { } nullIfExpression)
        {
            rendered = RenderNullIfExpression(nullIfExpression);
        }
        else if (FindByBaseId(model.IIfCallList, static row => row.PrimaryExpression?.Id, primaryExpression.Id) is { } iIfCall)
        {
            rendered = RenderIIfCall(iIfCall);
        }
        else if (FindByBaseId(model.TryConvertCallList, static row => row.PrimaryExpression?.Id, primaryExpression.Id) is { } tryConvertCall)
        {
            rendered = RenderTryConvertCall(tryConvertCall);
        }
        else if (FindByBaseId(model.FunctionCallList, static row => row.PrimaryExpression?.Id, primaryExpression.Id) is { } functionCall)
        {
            rendered = RenderFunctionCall(functionCall);
        }
        else if (FindByBaseId(model.ScalarSubqueryList, static row => row.PrimaryExpression?.Id, primaryExpression.Id) is { } scalarSubquery)
        {
            rendered = RenderScalarSubquery(scalarSubquery);
        }
        else if (FindByBaseId(model.ValueExpressionList, static row => row.PrimaryExpression?.Id, primaryExpression.Id) is { } valueExpression)
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
            model.ScalarSubqueryQueryExpressionLinkList, static row => row.ScalarSubquery?.Id,
            scalarSubquery.Id,
            "ScalarSubquery.QueryExpression").QueryExpression;
        return "(" + RenderQueryExpression(queryExpression) + ")";
    }

    private string RenderColumnReferenceExpression(ColumnReferenceExpression columnReference)
    {
        var multiPartIdentifierLink = FindOwnerLink(model.ColumnReferenceExpressionMultiPartIdentifierLinkList, static row => row.ColumnReferenceExpression?.Id, columnReference.Id);
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
        var literal = FindByBaseId(model.LiteralList, static row => row.ValueExpression?.Id, valueExpression.Id);
        if (literal is not null)
        {
            return RenderLiteral(literal);
        }

        var parameter = FindByBaseId(model.ParameterReferenceExpressionList, static row => row.ValueExpression?.Id, valueExpression.Id);
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
        var caseExpression = GetById(model.CaseExpressionList, static row => row.Id, searchedCaseExpression.CaseExpression.Id, "SearchedCaseExpression.Base");
        var whenClauses = GetOrderedItems(model.SearchedCaseExpressionWhenClausesItemList, static row => row.SearchedCaseExpression?.Id, static row => row.Ordinal, searchedCaseExpression.Id)
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

        var elseExpressionLink = FindOwnerLink(model.CaseExpressionElseExpressionLinkList, static row => row.CaseExpression?.Id, caseExpression.Id);
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
        var whenClause = GetById(model.WhenClauseList, static row => row.Id, searchedWhenClause.WhenClause.Id, "SearchedWhenClause.Base");
        var whenExpression = RenderBooleanExpression(GetOwnerLink(
            model.SearchedWhenClauseWhenExpressionLinkList, static row => row.SearchedWhenClause?.Id,
            searchedWhenClause.Id,
            "SearchedWhenClause.WhenExpression").BooleanExpression);
        var thenExpression = RenderScalarExpression(GetOwnerLink(
            model.WhenClauseThenExpressionLinkList, static row => row.WhenClause?.Id,
            whenClause.Id,
            "WhenClause.ThenExpression").ScalarExpression);
        return $"WHEN {whenExpression} THEN {thenExpression}";
    }
}
