namespace MetaWeaveScript.Execution;

internal sealed partial class MetaWeaveScriptExecutionSession
{
    private static readonly HashSet<string> ScalarFunctionNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CONCAT", "LOWER", "UPPER", "TRIM", "LTRIM", "RTRIM", "REPLACE", "SUBSTRING", "LEFT", "RIGHT",
            "LEN", "IS_BLANK"
        };

    private static readonly HashSet<string> WindowFunctionNames =
        new(StringComparer.OrdinalIgnoreCase) { "ROW_NUMBER" };

    private void PrepareQueryExpression(
        QueryExpression queryExpression,
        int visibleCommonTableExpressionOrdinal,
        RuntimeFrame? outerFrame)
    {
        if (navigator.TrySubtype<QuerySpecification>(queryExpression.Id) is { } querySpecification)
        {
            PrepareQuerySpecification(
                querySpecification,
                visibleCommonTableExpressionOrdinal,
                outerFrame);
            return;
        }

        if (navigator.TrySubtype<BinaryQueryExpression>(queryExpression.Id) is { } binary)
        {
            PrepareQueryExpression(
                navigator.RequireOwnerLink<BinaryQueryExpressionFirstQueryExpressionLink>(
                    binary.Id,
                    "BinaryQueryExpression.FirstQueryExpression").QueryExpression,
                visibleCommonTableExpressionOrdinal,
                outerFrame);
            PrepareQueryExpression(
                navigator.RequireOwnerLink<BinaryQueryExpressionSecondQueryExpressionLink>(
                    binary.Id,
                    "BinaryQueryExpression.SecondQueryExpression").QueryExpression,
                visibleCommonTableExpressionOrdinal,
                outerFrame);
            return;
        }

        if (navigator.TrySubtype<QueryParenthesisExpression>(queryExpression.Id) is { } parenthesis)
        {
            PrepareQueryExpression(
                navigator.RequireOwnerLink<QueryParenthesisExpressionQueryExpressionLink>(
                    parenthesis.Id,
                    "QueryParenthesisExpression.QueryExpression").QueryExpression,
                visibleCommonTableExpressionOrdinal,
                outerFrame);
            return;
        }

        throw Fault(
            "QueryExpressionShapeUnsupported",
            $"QueryExpression '{queryExpression.Id}' has no retained semantic subtype.",
            queryExpression.Id);
    }

    private void PrepareQuerySpecification(
        QuerySpecification querySpecification,
        int visibleCommonTableExpressionOrdinal,
        RuntimeFrame? outerFrame)
    {
        var fromLink = navigator.TryOwnerLink<QuerySpecificationFromClauseLink>(querySpecification.Id);
        var tableResult = fromLink is null
            ? new RuntimeTableResult([], [new RuntimeLocalRow()])
            : ExecuteFromClause(
                fromLink.FromClause,
                visibleCommonTableExpressionOrdinal,
                outerFrame);
        var frame = new RuntimeFrame(CreateNullLocalRow(tableResult.Sources), outerFrame);

        var whereLink = navigator.TryOwnerLink<QuerySpecificationWhereClauseLink>(querySpecification.Id);
        if (whereLink is not null)
        {
            PrepareBooleanExpression(
                navigator.RequireOwnerLink<WhereClauseSearchConditionLink>(
                    whereLink.WhereClause.Id,
                    "WhereClause.SearchCondition").BooleanExpression,
                frame,
                visibleCommonTableExpressionOrdinal,
                allowAggregate: false,
                withinAggregate: false);
        }

        var groupByLink = navigator.TryOwnerLink<QuerySpecificationGroupByClauseLink>(querySpecification.Id);
        if (groupByLink is not null)
        {
            foreach (var item in navigator.OrderedItems<GroupByClauseGroupingSpecificationsItem>(groupByLink.GroupByClause.Id))
            {
                PrepareScalarExpression(
                    RequireGroupingExpression(item.GroupingSpecification),
                    frame,
                    visibleCommonTableExpressionOrdinal,
                    allowAggregate: false,
                    withinAggregate: false,
                    wildcardAllowed: false);
            }
        }

        foreach (var item in navigator.OrderedItems<QuerySpecificationSelectElementsItem>(querySpecification.Id))
        {
            var projection = CreateProjection(item.SelectElement);
            PrepareScalarExpression(
                projection.Expression,
                frame,
                visibleCommonTableExpressionOrdinal,
                allowAggregate: true,
                withinAggregate: false,
                wildcardAllowed: false,
                allowWindow: true);
        }
    }

    private void PrepareScalarExpression(
        ScalarExpression expression,
        RuntimeFrame frame,
        int visibleCommonTableExpressionOrdinal,
        bool allowAggregate,
        bool withinAggregate,
        bool wildcardAllowed,
        bool allowWindow = false)
    {
        var primary = navigator.TrySubtype<PrimaryExpression>(expression.Id)
            ?? throw Fault(
                "ScalarExpressionShapeUnsupported",
                $"ScalarExpression '{expression.Id}' has no retained primary-expression subtype.",
                expression.Id);

        if (navigator.TrySubtype<ColumnReferenceExpression>(primary.Id) is { } column)
        {
            if (string.Equals(column.ColumnType, "Wildcard", StringComparison.Ordinal))
            {
                if (!wildcardAllowed)
                {
                    throw Fault(
                        "WildcardOutsideCount",
                        "Wildcard column references are executable only as COUNT(*).",
                        column.Id);
                }

                return;
            }

            var link = navigator.RequireOwnerLink<ColumnReferenceExpressionMultiPartIdentifierLink>(
                column.Id,
                "ColumnReferenceExpression.MultiPartIdentifier");
            ResolveColumn(navigator.IdentifierParts(link.MultiPartIdentifier), frame, column.Id);
            return;
        }

        if (navigator.TrySubtype<ParenthesisExpression>(primary.Id) is { } parenthesis)
        {
            PrepareScalarExpression(
                navigator.RequireOwnerLink<ParenthesisExpressionExpressionLink>(
                    parenthesis.Id,
                    "ParenthesisExpression.Expression").ScalarExpression,
                frame,
                visibleCommonTableExpressionOrdinal,
                allowAggregate,
                withinAggregate,
                wildcardAllowed: false);
            return;
        }

        if (navigator.TrySubtype<CaseExpression>(primary.Id) is { } caseExpression)
        {
            var searched = navigator.TrySubtype<SearchedCaseExpression>(caseExpression.Id)
                ?? throw Fault(
                    "CaseExpressionShapeUnsupported",
                    $"CaseExpression '{caseExpression.Id}' has no retained searched-case subtype.",
                    caseExpression.Id);
            foreach (var item in navigator.OrderedItems<SearchedCaseExpressionWhenClausesItem>(searched.Id))
            {
                PrepareBooleanExpression(
                    navigator.RequireOwnerLink<SearchedWhenClauseWhenExpressionLink>(
                        item.SearchedWhenClause.Id,
                        "SearchedWhenClause.WhenExpression").BooleanExpression,
                    frame,
                    visibleCommonTableExpressionOrdinal,
                    allowAggregate,
                    withinAggregate);
                PrepareScalarExpression(
                    navigator.RequireOwnerLink<WhenClauseThenExpressionLink>(
                        item.SearchedWhenClause.WhenClause.Id,
                        "WhenClause.ThenExpression").ScalarExpression,
                    frame,
                    visibleCommonTableExpressionOrdinal,
                    allowAggregate,
                    withinAggregate,
                    wildcardAllowed: false);
            }

            if (navigator.TryOwnerLink<CaseExpressionElseExpressionLink>(caseExpression.Id) is { } elseLink)
            {
                PrepareScalarExpression(
                    elseLink.ScalarExpression,
                    frame,
                    visibleCommonTableExpressionOrdinal,
                    allowAggregate,
                    withinAggregate,
                    wildcardAllowed: false);
            }

            return;
        }

        if (navigator.TrySubtype<CoalesceExpression>(primary.Id) is { } coalesce)
        {
            foreach (var item in navigator.OrderedItems<CoalesceExpressionExpressionsItem>(coalesce.Id))
            {
                PrepareScalarExpression(
                    item.ScalarExpression,
                    frame,
                    visibleCommonTableExpressionOrdinal,
                    allowAggregate,
                    withinAggregate,
                    wildcardAllowed: false);
            }

            return;
        }

        if (navigator.TrySubtype<NullIfExpression>(primary.Id) is { } nullIf)
        {
            PrepareScalarExpression(
                navigator.RequireOwnerLink<NullIfExpressionFirstExpressionLink>(
                    nullIf.Id,
                    "NullIfExpression.FirstExpression").ScalarExpression,
                frame,
                visibleCommonTableExpressionOrdinal,
                allowAggregate,
                withinAggregate,
                wildcardAllowed: false);
            PrepareScalarExpression(
                navigator.RequireOwnerLink<NullIfExpressionSecondExpressionLink>(
                    nullIf.Id,
                    "NullIfExpression.SecondExpression").ScalarExpression,
                frame,
                visibleCommonTableExpressionOrdinal,
                allowAggregate,
                withinAggregate,
                wildcardAllowed: false);
            return;
        }

        if (navigator.TrySubtype<IIfCall>(primary.Id) is { } iIf)
        {
            PrepareBooleanExpression(
                navigator.RequireOwnerLink<IIfCallPredicateLink>(
                    iIf.Id,
                    "IIfCall.Predicate").BooleanExpression,
                frame,
                visibleCommonTableExpressionOrdinal,
                allowAggregate,
                withinAggregate);
            PrepareScalarExpression(
                navigator.RequireOwnerLink<IIfCallThenExpressionLink>(
                    iIf.Id,
                    "IIfCall.ThenExpression").ScalarExpression,
                frame,
                visibleCommonTableExpressionOrdinal,
                allowAggregate,
                withinAggregate,
                wildcardAllowed: false);
            PrepareScalarExpression(
                navigator.RequireOwnerLink<IIfCallElseExpressionLink>(
                    iIf.Id,
                    "IIfCall.ElseExpression").ScalarExpression,
                frame,
                visibleCommonTableExpressionOrdinal,
                allowAggregate,
                withinAggregate,
                wildcardAllowed: false);
            return;
        }

        if (navigator.TrySubtype<TryConvertCall>(primary.Id) is { } tryConvert)
        {
            ValidateTryConvertDataType(tryConvert);
            PrepareScalarExpression(
                navigator.RequireOwnerLink<TryConvertCallParameterLink>(
                    tryConvert.Id,
                    "TryConvertCall.Parameter").ScalarExpression,
                frame,
                visibleCommonTableExpressionOrdinal,
                allowAggregate,
                withinAggregate,
                wildcardAllowed: false);
            return;
        }

        if (navigator.TrySubtype<FunctionCall>(primary.Id) is { } function)
        {
            var name = FunctionName(function);
            var isAggregate = AggregateFunctionNames.Contains(name);
            var isWindow = WindowFunctionNames.Contains(name);
            if (!isAggregate && !isWindow && !ScalarFunctionNames.Contains(name))
            {
                throw Fault(
                    "ScalarFunctionUnsupported",
                    $"Function '{name}' is outside the WeaveScript function catalog.",
                    function.Id);
            }


            if (isWindow && !allowWindow)
            {
                throw Fault(
                    "WindowFunctionContextInvalid",
                    $"Window function '{name}' is executable only as a direct SELECT projection.",
                    function.Id);
            }

            if (isAggregate && (!allowAggregate || withinAggregate))
            {
                throw Fault(
                    withinAggregate ? "NestedAggregateInvalid" : "AggregateOutsideGrouping",
                    withinAggregate
                        ? $"Aggregate function '{name}' cannot be nested inside another aggregate."
                        : $"Aggregate function '{name}' is not allowed in this expression context.",
                    function.Id);
            }

            var parameters = navigator.OrderedItems<FunctionCallParametersItem>(function.Id);
            ValidatePreparedFunctionShape(function, name, parameters.Count);
            for (var index = 0; index < parameters.Count; index++)
            {
                PrepareScalarExpression(
                    parameters[index].ScalarExpression,
                    frame,
                    visibleCommonTableExpressionOrdinal,
                    allowAggregate,
                    withinAggregate || isAggregate,
                    wildcardAllowed: isAggregate &&
                                     string.Equals(name, "COUNT", StringComparison.OrdinalIgnoreCase) &&
                                     index == 0);
            }

            var withinGroup = navigator.TryOwnerLink<FunctionCallWithinGroupOrderByClauseLink>(function.Id);
            if (withinGroup is not null)
            {
                if (!string.Equals(name, "STRING_AGG", StringComparison.OrdinalIgnoreCase))
                {
                    throw Fault(
                        "WithinGroupFunctionInvalid",
                        "WITHIN GROUP is executable only for STRING_AGG.",
                        function.Id);
                }

                foreach (var item in navigator.OrderedItems<OrderByClauseOrderByElementsItem>(withinGroup.OrderByClause.Id))
                {
                    PrepareScalarExpression(
                        navigator.RequireOwnerLink<ExpressionWithSortOrderExpressionLink>(
                            item.ExpressionWithSortOrder.Id,
                            "ExpressionWithSortOrder.Expression").ScalarExpression,
                        frame,
                        visibleCommonTableExpressionOrdinal,
                        allowAggregate: false,
                        withinAggregate: true,
                        wildcardAllowed: false);
                }
            }

            var overClause = navigator.TryOwnerLink<FunctionCallOverClauseLink>(function.Id);
            if (overClause is not null)
            {
                foreach (var partition in navigator.OrderedItems<OverClausePartitionsItem>(overClause.OverClause.Id))
                {
                    PrepareScalarExpression(
                        partition.ScalarExpression,
                        frame,
                        visibleCommonTableExpressionOrdinal,
                        allowAggregate: false,
                        withinAggregate: false,
                        wildcardAllowed: false);
                }

                var orderByClause = navigator.RequireOwnerLink<OverClauseOrderByClauseLink>(
                    overClause.OverClause.Id,
                    "OverClause.OrderByClause").OrderByClause;
                foreach (var item in navigator.OrderedItems<OrderByClauseOrderByElementsItem>(orderByClause.Id))
                {
                    PrepareScalarExpression(
                        navigator.RequireOwnerLink<ExpressionWithSortOrderExpressionLink>(
                            item.ExpressionWithSortOrder.Id,
                            "ExpressionWithSortOrder.Expression").ScalarExpression,
                        frame,
                        visibleCommonTableExpressionOrdinal,
                        allowAggregate: false,
                        withinAggregate: false,
                        wildcardAllowed: false);
                }
            }

            return;
        }

        if (navigator.TrySubtype<ScalarSubquery>(primary.Id) is { } subquery)
        {
            PrepareQueryExpression(
                navigator.RequireOwnerLink<ScalarSubqueryQueryExpressionLink>(
                    subquery.Id,
                    "ScalarSubquery.QueryExpression").QueryExpression,
                visibleCommonTableExpressionOrdinal,
                frame);
            return;
        }

        if (navigator.TrySubtype<ValueExpression>(primary.Id) is { } valueExpression)
        {
            if (navigator.TrySubtype<ParameterReferenceExpression>(valueExpression.Id) is { } parameter &&
                !parameters.ContainsKey(parameter.Name))
            {
                throw Fault(
                    "ParameterValueMissing",
                    $"No value was supplied for WeaveScript parameter '@{parameter.Name}'.",
                    parameter.Id);
            }

            return;
        }

        throw Fault(
            "PrimaryExpressionShapeUnsupported",
            $"PrimaryExpression '{primary.Id}' has no retained semantic subtype.",
            primary.Id);
    }

    private void PrepareBooleanExpression(
        BooleanExpression expression,
        RuntimeFrame frame,
        int visibleCommonTableExpressionOrdinal,
        bool allowAggregate,
        bool withinAggregate)
    {
        if (navigator.TrySubtype<BooleanBinaryExpression>(expression.Id) is { } binary)
        {
            PrepareBooleanExpression(
                navigator.RequireOwnerLink<BooleanBinaryExpressionFirstExpressionLink>(
                    binary.Id,
                    "BooleanBinaryExpression.FirstExpression").BooleanExpression,
                frame,
                visibleCommonTableExpressionOrdinal,
                allowAggregate,
                withinAggregate);
            PrepareBooleanExpression(
                navigator.RequireOwnerLink<BooleanBinaryExpressionSecondExpressionLink>(
                    binary.Id,
                    "BooleanBinaryExpression.SecondExpression").BooleanExpression,
                frame,
                visibleCommonTableExpressionOrdinal,
                allowAggregate,
                withinAggregate);
            return;
        }

        if (navigator.TrySubtype<BooleanComparisonExpression>(expression.Id) is { } comparison)
        {
            PrepareScalarExpression(
                navigator.RequireOwnerLink<BooleanComparisonExpressionFirstExpressionLink>(
                    comparison.Id,
                    "BooleanComparisonExpression.FirstExpression").ScalarExpression,
                frame,
                visibleCommonTableExpressionOrdinal,
                allowAggregate,
                withinAggregate,
                wildcardAllowed: false);
            PrepareScalarExpression(
                navigator.RequireOwnerLink<BooleanComparisonExpressionSecondExpressionLink>(
                    comparison.Id,
                    "BooleanComparisonExpression.SecondExpression").ScalarExpression,
                frame,
                visibleCommonTableExpressionOrdinal,
                allowAggregate,
                withinAggregate,
                wildcardAllowed: false);
            return;
        }

        if (navigator.TrySubtype<BooleanParenthesisExpression>(expression.Id) is { } parenthesis)
        {
            PrepareBooleanExpression(
                navigator.RequireOwnerLink<BooleanParenthesisExpressionExpressionLink>(
                    parenthesis.Id,
                    "BooleanParenthesisExpression.Expression").BooleanExpression,
                frame,
                visibleCommonTableExpressionOrdinal,
                allowAggregate,
                withinAggregate);
            return;
        }

        if (navigator.TrySubtype<BooleanNotExpression>(expression.Id) is { } not)
        {
            PrepareBooleanExpression(
                navigator.RequireOwnerLink<BooleanNotExpressionExpressionLink>(
                    not.Id,
                    "BooleanNotExpression.Expression").BooleanExpression,
                frame,
                visibleCommonTableExpressionOrdinal,
                allowAggregate,
                withinAggregate);
            return;
        }

        ScalarExpression? first = null;
        ScalarExpression? second = null;
        IReadOnlyList<ScalarExpression> rest = [];
        if (navigator.TrySubtype<BooleanIsNullExpression>(expression.Id) is { } isNull)
        {
            first = navigator.RequireOwnerLink<BooleanIsNullExpressionExpressionLink>(
                isNull.Id,
                "BooleanIsNullExpression.Expression").ScalarExpression;
        }
        else if (navigator.TrySubtype<InPredicate>(expression.Id) is { } inPredicate)
        {
            first = navigator.RequireOwnerLink<InPredicateExpressionLink>(
                inPredicate.Id,
                "InPredicate.Expression").ScalarExpression;
            rest = navigator.OrderedItems<InPredicateValuesItem>(inPredicate.Id)
                .Select(item => item.ScalarExpression)
                .ToArray();
        }
        else if (navigator.TrySubtype<LikePredicate>(expression.Id) is { } like)
        {
            first = navigator.RequireOwnerLink<LikePredicateFirstExpressionLink>(
                like.Id,
                "LikePredicate.FirstExpression").ScalarExpression;
            second = navigator.RequireOwnerLink<LikePredicateSecondExpressionLink>(
                like.Id,
                "LikePredicate.SecondExpression").ScalarExpression;
        }
        else if (navigator.TrySubtype<ExistsPredicate>(expression.Id) is { } exists)
        {
            var subquery = navigator.RequireOwnerLink<ExistsPredicateSubqueryLink>(
                exists.Id,
                "ExistsPredicate.Subquery").ScalarSubquery;
            PrepareQueryExpression(
                navigator.RequireOwnerLink<ScalarSubqueryQueryExpressionLink>(
                    subquery.Id,
                    "ScalarSubquery.QueryExpression").QueryExpression,
                visibleCommonTableExpressionOrdinal,
                frame);
            return;
        }
        else
        {
            throw Fault(
                "BooleanExpressionShapeUnsupported",
                $"BooleanExpression '{expression.Id}' has no retained semantic subtype.",
                expression.Id);
        }

        foreach (var scalar in new[] { first, second }.Where(item => item is not null).Cast<ScalarExpression>().Concat(rest))
        {
            PrepareScalarExpression(
                scalar,
                frame,
                visibleCommonTableExpressionOrdinal,
                allowAggregate,
                withinAggregate,
                wildcardAllowed: false);
        }
    }

    private void ValidatePreparedFunctionShape(
        FunctionCall function,
        string name,
        int parameterCount)
    {
        var validArity = name.ToUpperInvariant() switch
        {
            "COUNT" or "MIN" or "MAX" or "LOWER" or "UPPER" or "TRIM" or "LTRIM" or "RTRIM" or "LEN" or "IS_BLANK" =>
                parameterCount == 1,
            "STRING_AGG" or "LEFT" or "RIGHT" => parameterCount == 2,
            "REPLACE" or "SUBSTRING" => parameterCount == 3,
            "CONCAT" => parameterCount >= 2,
            "ROW_NUMBER" => parameterCount == 0,
            _ => false
        };
        if (!validArity)
        {
            throw Fault(
                "FunctionArityInvalid",
                $"Function '{name}' has invalid argument count {parameterCount}.",
                function.Id);
        }

        var withinGroup = navigator.TryOwnerLink<FunctionCallWithinGroupOrderByClauseLink>(function.Id);
        if (string.Equals(name, "STRING_AGG", StringComparison.OrdinalIgnoreCase) && withinGroup is null)
        {
            throw Fault(
                "StringAggregateOrderMissing",
                "STRING_AGG requires WITHIN GROUP ordering.",
                function.Id);
        }

        if (!string.Equals(name, "STRING_AGG", StringComparison.OrdinalIgnoreCase) && withinGroup is not null)
        {
            throw Fault(
                "WithinGroupFunctionInvalid",
                "WITHIN GROUP is executable only for STRING_AGG.",
                function.Id);
        }

        var overClause = navigator.TryOwnerLink<FunctionCallOverClauseLink>(function.Id);
        if (string.Equals(name, "ROW_NUMBER", StringComparison.OrdinalIgnoreCase) && overClause is null)
        {
            throw Fault(
                "RowNumberOverClauseMissing",
                "ROW_NUMBER requires an OVER clause.",
                function.Id);
        }

        if (!string.Equals(name, "ROW_NUMBER", StringComparison.OrdinalIgnoreCase) && overClause is not null)
        {
            throw Fault(
                "OverClauseFunctionInvalid",
                "OVER is executable only for ROW_NUMBER.",
                function.Id);
        }
    }

    private void ValidateTryConvertDataType(TryConvertCall tryConvert)
    {
        var dataType = navigator.RequireOwnerLink<TryConvertCallDataTypeLink>(
            tryConvert.Id,
            "TryConvertCall.DataType").DataTypeReference;
        var parameterized = navigator.TrySubtype<ParameterizedDataTypeReference>(dataType.Id)
            ?? throw Fault(
                "TryConvertDataTypeUnsupported",
                "TRY_CONVERT supports the int data type only.",
                tryConvert.Id);
        var sqlDataType = navigator.TrySubtype<SqlDataTypeReference>(parameterized.Id);
        if (sqlDataType is null || !string.Equals(sqlDataType.SqlDataTypeOption, "Int", StringComparison.Ordinal))
        {
            throw Fault(
                "TryConvertDataTypeUnsupported",
                "TRY_CONVERT supports the int data type only.",
                tryConvert.Id);
        }
    }
}
