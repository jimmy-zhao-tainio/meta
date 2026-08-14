using System.Globalization;
using MetaWeave;

namespace MetaWeaveScript.Sql.Parsing;

internal sealed partial class MetaWeaveScriptSqlModelBuilder
{
    public BuiltNode CreateExpressionGroupingSpecification(BuiltNode expression)
    {
        var groupingSpecification = new GroupingSpecification
        {
            Id = NextId(nameof(GroupingSpecification))
        };
        model.GroupingSpecificationList.Add(groupingSpecification);
        var expressionGroupingSpecification = new ExpressionGroupingSpecification
        {
            Id = NextId(nameof(ExpressionGroupingSpecification)),
            GroupingSpecification = groupingSpecification
        };
        model.ExpressionGroupingSpecificationList.Add(expressionGroupingSpecification);
        model.ExpressionGroupingSpecificationExpressionLinkList.Add(new ExpressionGroupingSpecificationExpressionLink
        {
            Id = NextId(nameof(ExpressionGroupingSpecificationExpressionLink)),
            ExpressionGroupingSpecification = expressionGroupingSpecification,
            ScalarExpression = expression.GetRef<ScalarExpression>(nameof(ScalarExpression))
        });
        return BuiltNode.Create((nameof(GroupingSpecification), groupingSpecification.Id), (nameof(ExpressionGroupingSpecification), expressionGroupingSpecification.Id));
    }

    public BuiltNode CreateGroupByClause(IReadOnlyList<BuiltNode> groupingSpecifications, bool all = false)
    {
        if (all)
        {
            throw new InvalidOperationException("MetaWeaveScript GROUP BY does not support ALL.");
        }

        var groupByClause = new GroupByClause
        {
            Id = NextId(nameof(GroupByClause))
        };
        model.GroupByClauseList.Add(groupByClause);
        for (var ordinal = 0; ordinal < groupingSpecifications.Count; ordinal++)
        {
            model.GroupByClauseGroupingSpecificationsItemList.Add(new GroupByClauseGroupingSpecificationsItem
            {
                Id = NextId(nameof(GroupByClauseGroupingSpecificationsItem)),
                GroupByClause = groupByClause,
                GroupingSpecification = groupingSpecifications[ordinal].GetRef<GroupingSpecification>(nameof(GroupingSpecification)),
                Ordinal = ordinal.ToString(CultureInfo.InvariantCulture)
            });
        }
        return BuiltNode.Create((nameof(GroupByClause), groupByClause.Id));
    }

    public BuiltNode CreateExpressionWithSortOrder(BuiltNode expression, string sortOrder)
    {
        var expressionWithSortOrder = new ExpressionWithSortOrder
        {
            Id = NextId(nameof(ExpressionWithSortOrder)),
            SortOrder = sortOrder
        };
        model.ExpressionWithSortOrderList.Add(expressionWithSortOrder);
        model.ExpressionWithSortOrderExpressionLinkList.Add(new ExpressionWithSortOrderExpressionLink
        {
            Id = NextId(nameof(ExpressionWithSortOrderExpressionLink)),
            ExpressionWithSortOrder = expressionWithSortOrder,
            ScalarExpression = expression.GetRef<ScalarExpression>(nameof(ScalarExpression))
        });
        return BuiltNode.Create((nameof(ExpressionWithSortOrder), expressionWithSortOrder.Id));
    }

    public BuiltNode CreateOrderByClause(IReadOnlyList<BuiltNode> orderByElements)
    {
        var orderByClause = new OrderByClause
        {
            Id = NextId(nameof(OrderByClause))
        };
        model.OrderByClauseList.Add(orderByClause);
        for (var ordinal = 0; ordinal < orderByElements.Count; ordinal++)
        {
            model.OrderByClauseOrderByElementsItemList.Add(new OrderByClauseOrderByElementsItem
            {
                Id = NextId(nameof(OrderByClauseOrderByElementsItem)),
                OrderByClause = orderByClause,
                ExpressionWithSortOrder = orderByElements[ordinal].GetRef<ExpressionWithSortOrder>(nameof(ExpressionWithSortOrder)),
                Ordinal = ordinal.ToString(CultureInfo.InvariantCulture)
            });
        }
        return BuiltNode.Create((nameof(OrderByClause), orderByClause.Id));
    }

    public BuiltNode AttachWithinGroupOrderByClause(BuiltNode functionCall, BuiltNode orderByClause)
    {
        model.FunctionCallWithinGroupOrderByClauseLinkList.Add(new FunctionCallWithinGroupOrderByClauseLink
        {
            Id = NextId(nameof(FunctionCallWithinGroupOrderByClauseLink)),
            FunctionCall = functionCall.GetRef<FunctionCall>(nameof(FunctionCall)),
            OrderByClause = orderByClause.GetRef<OrderByClause>(nameof(OrderByClause))
        });
        return functionCall;
    }

    public BuiltNode CreateWhereClause(BuiltNode searchCondition)
    {
        var row = new WhereClause
        {
            Id = NextId(nameof(WhereClause))
        };
        model.WhereClauseList.Add(row);
        model.WhereClauseSearchConditionLinkList.Add(new WhereClauseSearchConditionLink
        {
            Id = NextId(nameof(WhereClauseSearchConditionLink)),
            WhereClause = row,
            BooleanExpression = searchCondition.GetRef<BooleanExpression>(nameof(BooleanExpression))
        });
        return BuiltNode.Create((nameof(WhereClause), row.Id));
    }

    public BuiltNode CreateQuerySpecification(
        IReadOnlyList<BuiltNode> selectElements,
        BuiltNode? fromClause = null,
        BuiltNode? whereClause = null,
        BuiltNode? groupByClause = null,
        BuiltNode? havingClause = null,
        BuiltNode? topRowFilter = null,
        BuiltNode? windowClause = null,
        string? uniqueRowFilter = null)
    {
        if (havingClause is not null || topRowFilter is not null || windowClause is not null)
        {
            throw new InvalidOperationException("MetaWeaveScript query specifications do not support HAVING, TOP, or WINDOW.");
        }

        var queryExpression = new QueryExpression
        {
            Id = NextId(nameof(QueryExpression))
        };
        model.QueryExpressionList.Add(queryExpression);
        var specification = new QuerySpecification
        {
            Id = NextId(nameof(QuerySpecification)),
            QueryExpression = queryExpression,
            UniqueRowFilter = uniqueRowFilter ?? string.Empty
        };
        model.QuerySpecificationList.Add(specification);
        for (var ordinal = 0; ordinal < selectElements.Count; ordinal++)
        {
            model.QuerySpecificationSelectElementsItemList.Add(new QuerySpecificationSelectElementsItem
            {
                Id = NextId(nameof(QuerySpecificationSelectElementsItem)),
                QuerySpecification = specification,
                SelectElement = selectElements[ordinal].GetRef<SelectElement>(nameof(SelectElement)),
                Ordinal = ordinal.ToString(CultureInfo.InvariantCulture)
            });
        }
        if (fromClause is not null)
        {
            model.QuerySpecificationFromClauseLinkList.Add(new QuerySpecificationFromClauseLink
            {
                Id = NextId(nameof(QuerySpecificationFromClauseLink)),
                QuerySpecification = specification,
                FromClause = fromClause.GetRef<FromClause>(nameof(FromClause))
            });
        }
        if (whereClause is not null)
        {
            model.QuerySpecificationWhereClauseLinkList.Add(new QuerySpecificationWhereClauseLink
            {
                Id = NextId(nameof(QuerySpecificationWhereClauseLink)),
                QuerySpecification = specification,
                WhereClause = whereClause.GetRef<WhereClause>(nameof(WhereClause))
            });
        }
        if (groupByClause is not null)
        {
            model.QuerySpecificationGroupByClauseLinkList.Add(new QuerySpecificationGroupByClauseLink
            {
                Id = NextId(nameof(QuerySpecificationGroupByClauseLink)),
                QuerySpecification = specification,
                GroupByClause = groupByClause.GetRef<GroupByClause>(nameof(GroupByClause))
            });
        }
        return BuiltNode.Create((nameof(QueryExpression), queryExpression.Id), (nameof(QuerySpecification), specification.Id));
    }
}
