using System.Text;
using MetaWeave;

namespace MetaWeaveScript.Sql;

internal sealed partial class MetaWeaveScriptSqlEmitter
{
    private string RenderWithClause(WithCtes withCtes)
    {
        var ctes = GetOrderedItems(model.WithCtesCommonTableExpressionsItemList, static row => row.WithCtes?.Id, static row => row.Ordinal, withCtes.Id)
            .Select(row => RenderCommonTableExpression(row.CommonTableExpression))
            .ToArray();
        return "WITH " + string.Join(", ", ctes);
    }

    private string RenderCommonTableExpression(CommonTableExpression cte)
    {
        var name = RenderIdentifier(GetOwnerLink(model.CommonTableExpressionExpressionNameLinkList, static row => row.CommonTableExpression?.Id, cte.Id, "CommonTableExpression.ExpressionName").Identifier);
        var queryExpression = GetOwnerLink(model.CommonTableExpressionQueryExpressionLinkList, static row => row.CommonTableExpression?.Id, cte.Id, "CommonTableExpression.QueryExpression").QueryExpression;
        return $"{name} AS ({RenderQueryExpression(queryExpression)})";
    }

    private string RenderQueryExpression(QueryExpression queryExpression)
    {
        string renderedCore;

        var querySpecification = FindByBaseId(model.QuerySpecificationList, static row => row.QueryExpression?.Id, queryExpression.Id);
        if (querySpecification is not null)
        {
            renderedCore = RenderQuerySpecification(querySpecification);
        }
        else
        {
            var binaryQueryExpression = FindByBaseId(model.BinaryQueryExpressionList, static row => row.QueryExpression?.Id, queryExpression.Id);
            if (binaryQueryExpression is not null)
            {
                var first = RenderQueryExpression(GetOwnerLink(model.BinaryQueryExpressionFirstQueryExpressionLinkList, static row => row.BinaryQueryExpression?.Id, binaryQueryExpression.Id, "BinaryQueryExpression.FirstQueryExpression").QueryExpression);
                var second = RenderQueryExpression(GetOwnerLink(model.BinaryQueryExpressionSecondQueryExpressionLinkList, static row => row.BinaryQueryExpression?.Id, binaryQueryExpression.Id, "BinaryQueryExpression.SecondQueryExpression").QueryExpression);
                renderedCore = $"{first}{Environment.NewLine}UNION ALL{Environment.NewLine}{second}";
            }
            else
            {
                var queryParenthesisExpression = FindByBaseId(model.QueryParenthesisExpressionList, static row => row.QueryExpression?.Id, queryExpression.Id);
                if (queryParenthesisExpression is not null)
                {
                    var child = GetOwnerLink(
                        model.QueryParenthesisExpressionQueryExpressionLinkList, static row => row.QueryParenthesisExpression?.Id,
                        queryParenthesisExpression.Id,
                        "QueryParenthesisExpression.QueryExpression").QueryExpression;
                    renderedCore = "(" + RenderQueryExpression(child) + ")";
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported MetaWeaveScript QueryExpression id '{queryExpression.Id}'.");
                }
            }
        }

        return renderedCore;
    }

    private string RenderQuerySpecification(QuerySpecification querySpecification)
    {
        var builder = new StringBuilder();
        builder.Append("SELECT");

        if (!string.IsNullOrWhiteSpace(querySpecification.UniqueRowFilter) &&
            !string.Equals(querySpecification.UniqueRowFilter, "NotSpecified", StringComparison.Ordinal))
        {
            builder.Append(' ');
            builder.Append(querySpecification.UniqueRowFilter.ToUpperInvariant());
        }

        var selectElements = GetOrderedItems(model.QuerySpecificationSelectElementsItemList, static row => row.QuerySpecification?.Id, static row => row.Ordinal, querySpecification.Id)
            .Select(row => RenderSelectElement(row.SelectElement))
            .ToArray();

        builder.AppendLine();
        builder.Append("    ");
        builder.Append(string.Join("," + Environment.NewLine + "    ", selectElements));

        var fromClauseLink = FindOwnerLink(model.QuerySpecificationFromClauseLinkList, static row => row.QuerySpecification?.Id, querySpecification.Id);
        if (fromClauseLink is not null)
        {
            builder.AppendLine();
            builder.Append("FROM ");
            builder.Append(RenderFromClause(fromClauseLink.FromClause));
        }

        var whereClauseLink = FindOwnerLink(model.QuerySpecificationWhereClauseLinkList, static row => row.QuerySpecification?.Id, querySpecification.Id);
        if (whereClauseLink is not null)
        {
            builder.AppendLine();
            builder.Append("WHERE ");
            builder.Append(RenderBooleanExpression(GetOwnerLink(model.WhereClauseSearchConditionLinkList, static row => row.WhereClause?.Id, whereClauseLink.WhereClause.Id, "WhereClause.SearchCondition").BooleanExpression));
        }

        var groupByClauseLink = FindOwnerLink(model.QuerySpecificationGroupByClauseLinkList, static row => row.QuerySpecification?.Id, querySpecification.Id);
        if (groupByClauseLink is not null)
        {
            builder.AppendLine();
            builder.Append("GROUP BY ");
            builder.Append(RenderGroupByClause(groupByClauseLink.GroupByClause));
        }

        return builder.ToString();
    }

    private string RenderGroupByClause(GroupByClause groupByClause)
    {
        var groupingSpecifications = GetOrderedItems(model.GroupByClauseGroupingSpecificationsItemList, static row => row.GroupByClause?.Id, static row => row.Ordinal, groupByClause.Id)
            .Select(row => RenderGroupingSpecification(row.GroupingSpecification))
            .ToArray();
        return string.Join(", ", groupingSpecifications);
    }

    private string RenderGroupingSpecification(GroupingSpecification groupingSpecification)
    {
        var expressionGroupingSpecification = FindByBaseId(model.ExpressionGroupingSpecificationList, static row => row.GroupingSpecification?.Id, groupingSpecification.Id);
        if (expressionGroupingSpecification is not null)
        {
            return RenderScalarExpression(GetOwnerLink(
                model.ExpressionGroupingSpecificationExpressionLinkList, static row => row.ExpressionGroupingSpecification?.Id,
                expressionGroupingSpecification.Id,
                "ExpressionGroupingSpecification.Expression").ScalarExpression);
        }

        throw new InvalidOperationException($"Unsupported MetaWeaveScript GroupingSpecification id '{groupingSpecification.Id}'.");
    }

    private string RenderOrderByClause(OrderByClause orderByClause)
    {
        var elements = GetOrderedItems(model.OrderByClauseOrderByElementsItemList, static row => row.OrderByClause?.Id, static row => row.Ordinal, orderByClause.Id)
            .Select(row => RenderExpressionWithSortOrder(row.ExpressionWithSortOrder))
            .ToArray();
        return "ORDER BY " + string.Join(", ", elements);
    }

    private string RenderExpressionWithSortOrder(ExpressionWithSortOrder expressionWithSortOrder)
    {
        var rendered = RenderScalarExpression(GetOwnerLink(
            model.ExpressionWithSortOrderExpressionLinkList, static row => row.ExpressionWithSortOrder?.Id,
            expressionWithSortOrder.Id,
            "ExpressionWithSortOrder.Expression").ScalarExpression);

        return expressionWithSortOrder.SortOrder switch
        {
            "Descending" => rendered + " DESC",
            "Ascending" => rendered + " ASC",
            "NotSpecified" or "" => rendered,
            _ => throw new InvalidOperationException($"Unsupported MetaWeaveScript SortOrder '{expressionWithSortOrder.SortOrder}'.")
        };
    }

}
