using System.Text;
using MetaWeave;

namespace MetaWeaveScript.Sql;

internal sealed partial class MetaWeaveScriptSqlEmitter
{
    private string RenderWithClause(WithCtes withCtes)
    {
        var ctes = GetOrderedItems(model.WithCtesCommonTableExpressionsItemList, withCtes.Id)
            .Select(row => RenderCommonTableExpression(row.CommonTableExpression))
            .ToArray();
        return "WITH " + string.Join(", ", ctes);
    }

    private string RenderCommonTableExpression(CommonTableExpression cte)
    {
        var name = RenderIdentifier(GetOwnerLink(model.CommonTableExpressionExpressionNameLinkList, cte.Id, "CommonTableExpression.ExpressionName").Identifier);
        var queryExpression = GetOwnerLink(model.CommonTableExpressionQueryExpressionLinkList, cte.Id, "CommonTableExpression.QueryExpression").QueryExpression;
        return $"{name} AS ({RenderQueryExpression(queryExpression)})";
    }

    private string RenderQueryExpression(QueryExpression queryExpression)
    {
        string renderedCore;

        var querySpecification = FindByBaseId(model.QuerySpecificationList, queryExpression.Id);
        if (querySpecification is not null)
        {
            renderedCore = RenderQuerySpecification(querySpecification);
        }
        else
        {
            var binaryQueryExpression = FindByBaseId(model.BinaryQueryExpressionList, queryExpression.Id);
            if (binaryQueryExpression is not null)
            {
                var first = RenderQueryExpression(GetOwnerLink(model.BinaryQueryExpressionFirstQueryExpressionLinkList, binaryQueryExpression.Id, "BinaryQueryExpression.FirstQueryExpression").QueryExpression);
                var second = RenderQueryExpression(GetOwnerLink(model.BinaryQueryExpressionSecondQueryExpressionLinkList, binaryQueryExpression.Id, "BinaryQueryExpression.SecondQueryExpression").QueryExpression);
                renderedCore = $"{first}{Environment.NewLine}UNION ALL{Environment.NewLine}{second}";
            }
            else
            {
                var queryParenthesisExpression = FindByBaseId(model.QueryParenthesisExpressionList, queryExpression.Id);
                if (queryParenthesisExpression is not null)
                {
                    var child = GetOwnerLink(
                        model.QueryParenthesisExpressionQueryExpressionLinkList,
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

        var selectElements = GetOrderedItems(model.QuerySpecificationSelectElementsItemList, querySpecification.Id)
            .Select(row => RenderSelectElement(row.SelectElement))
            .ToArray();

        builder.AppendLine();
        builder.Append("    ");
        builder.Append(string.Join("," + Environment.NewLine + "    ", selectElements));

        var fromClauseLink = FindOwnerLink(model.QuerySpecificationFromClauseLinkList, querySpecification.Id);
        if (fromClauseLink is not null)
        {
            builder.AppendLine();
            builder.Append("FROM ");
            builder.Append(RenderFromClause(fromClauseLink.FromClause));
        }

        var whereClauseLink = FindOwnerLink(model.QuerySpecificationWhereClauseLinkList, querySpecification.Id);
        if (whereClauseLink is not null)
        {
            builder.AppendLine();
            builder.Append("WHERE ");
            builder.Append(RenderBooleanExpression(GetOwnerLink(model.WhereClauseSearchConditionLinkList, whereClauseLink.WhereClause.Id, "WhereClause.SearchCondition").BooleanExpression));
        }

        var groupByClauseLink = FindOwnerLink(model.QuerySpecificationGroupByClauseLinkList, querySpecification.Id);
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
        var groupingSpecifications = GetOrderedItems(model.GroupByClauseGroupingSpecificationsItemList, groupByClause.Id)
            .Select(row => RenderGroupingSpecification(row.GroupingSpecification))
            .ToArray();
        return string.Join(", ", groupingSpecifications);
    }

    private string RenderGroupingSpecification(GroupingSpecification groupingSpecification)
    {
        var expressionGroupingSpecification = FindByBaseId(model.ExpressionGroupingSpecificationList, groupingSpecification.Id);
        if (expressionGroupingSpecification is not null)
        {
            return RenderScalarExpression(GetOwnerLink(
                model.ExpressionGroupingSpecificationExpressionLinkList,
                expressionGroupingSpecification.Id,
                "ExpressionGroupingSpecification.Expression").ScalarExpression);
        }

        throw new InvalidOperationException($"Unsupported MetaWeaveScript GroupingSpecification id '{groupingSpecification.Id}'.");
    }

    private string RenderOrderByClause(OrderByClause orderByClause)
    {
        var elements = GetOrderedItems(model.OrderByClauseOrderByElementsItemList, orderByClause.Id)
            .Select(row => RenderExpressionWithSortOrder(row.ExpressionWithSortOrder))
            .ToArray();
        return "ORDER BY " + string.Join(", ", elements);
    }

    private string RenderExpressionWithSortOrder(ExpressionWithSortOrder expressionWithSortOrder)
    {
        var rendered = RenderScalarExpression(GetOwnerLink(
            model.ExpressionWithSortOrderExpressionLinkList,
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
