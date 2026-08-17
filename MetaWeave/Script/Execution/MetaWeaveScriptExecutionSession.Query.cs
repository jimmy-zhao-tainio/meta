namespace MetaWeaveScript.Execution;

internal sealed partial class MetaWeaveScriptExecutionSession
{
    private RuntimeRowset ExecuteQueryExpression(
        QueryExpression queryExpression,
        int visibleCommonTableExpressionOrdinal,
        RuntimeFrame? outerFrame)
    {
        if (navigator.TrySubtype<QuerySpecification>(queryExpression.Id) is { } querySpecification)
        {
            return ExecuteQuerySpecification(
                querySpecification,
                visibleCommonTableExpressionOrdinal,
                outerFrame);
        }

        if (navigator.TrySubtype<BinaryQueryExpression>(queryExpression.Id) is { } binaryExpression)
        {
            var first = navigator.RequireOwnerLink<BinaryQueryExpressionFirstQueryExpressionLink>(
                binaryExpression.Id,
                "BinaryQueryExpression.FirstQueryExpression").QueryExpression;
            var second = navigator.RequireOwnerLink<BinaryQueryExpressionSecondQueryExpressionLink>(
                binaryExpression.Id,
                "BinaryQueryExpression.SecondQueryExpression").QueryExpression;
            var firstResult = ExecuteQueryExpression(first, visibleCommonTableExpressionOrdinal, outerFrame);
            var secondResult = ExecuteQueryExpression(second, visibleCommonTableExpressionOrdinal, outerFrame);
            return AppendUnionAll(firstResult, secondResult, binaryExpression.Id);
        }

        if (navigator.TrySubtype<QueryParenthesisExpression>(queryExpression.Id) is { } parenthesis)
        {
            var inner = navigator.RequireOwnerLink<QueryParenthesisExpressionQueryExpressionLink>(
                parenthesis.Id,
                "QueryParenthesisExpression.QueryExpression").QueryExpression;
            return ExecuteQueryExpression(inner, visibleCommonTableExpressionOrdinal, outerFrame);
        }

        throw Fault(
            "QueryExpressionShapeUnsupported",
            $"QueryExpression '{queryExpression.Id}' has no retained semantic subtype.",
            queryExpression.Id);
    }

    private RuntimeRowset ExecuteQuerySpecification(
        QuerySpecification querySpecification,
        int visibleCommonTableExpressionOrdinal,
        RuntimeFrame? outerFrame)
    {
        var selectItems = navigator.OrderedItems<QuerySpecificationSelectElementsItem>(querySpecification.Id);
        if (selectItems.Count == 0)
        {
            throw Fault(
                "SelectProjectionMissing",
                $"QuerySpecification '{querySpecification.Id}' has no select elements.",
                querySpecification.Id);
        }

        var projections = selectItems.Select(item => CreateProjection(item.SelectElement)).ToArray();
        var fromLink = navigator.TryOwnerLink<QuerySpecificationFromClauseLink>(querySpecification.Id);
        var frames = fromLink is null
            ? new List<RuntimeFrame> { new(new RuntimeLocalRow(), outerFrame) }
            : ExecuteFromClause(
                    fromLink.FromClause,
                    visibleCommonTableExpressionOrdinal,
                    outerFrame)
                .Rows
                .Select(row => new RuntimeFrame(row, outerFrame))
                .ToList();

        var whereLink = navigator.TryOwnerLink<QuerySpecificationWhereClauseLink>(querySpecification.Id);
        if (whereLink is not null)
        {
            var predicate = navigator.RequireOwnerLink<WhereClauseSearchConditionLink>(
                whereLink.WhereClause.Id,
                "WhereClause.SearchCondition").BooleanExpression;
            frames = frames
                .Where(frame => EvaluateBooleanExpression(
                    predicate,
                    new RuntimeEvaluationContext(frame, visibleCommonTableExpressionOrdinal)) == RuntimeTruth.True)
                .ToList();
        }

        var groupByLink = navigator.TryOwnerLink<QuerySpecificationGroupByClauseLink>(querySpecification.Id);
        var hasAggregates = projections.Any(projection => ContainsAggregate(projection.Expression));
        var rows = groupByLink is not null || hasAggregates
            ? ExecuteGroupedProjection(
                querySpecification,
                projections,
                frames,
                groupByLink?.GroupByClause,
                visibleCommonTableExpressionOrdinal,
                outerFrame)
            : frames.Select((frame, frameOrdinal) => ProjectRow(
                    projections,
                    new RuntimeEvaluationContext(
                        frame,
                        visibleCommonTableExpressionOrdinal,
                        WindowFrames: frames,
                        WindowFrameOrdinal: frameOrdinal)))
                .ToList();

        var uniqueRowFilter = querySpecification.UniqueRowFilter?.Trim() ?? string.Empty;
        if (string.Equals(uniqueRowFilter, "Distinct", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Distinct(RuntimeRowEqualityComparer.Instance).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(uniqueRowFilter) &&
                 !string.Equals(uniqueRowFilter, "NotSpecified", StringComparison.OrdinalIgnoreCase))
        {
            throw Fault(
                "UniqueRowFilterUnsupported",
                $"QuerySpecification '{querySpecification.Id}' uses unsupported unique row filter '{querySpecification.UniqueRowFilter}'.",
                querySpecification.Id);
        }

        return new RuntimeRowset(
            projections.Select(projection => new RuntimeColumn(projection.Name)).ToArray(),
            rows);
    }

    private List<RuntimeRow> ExecuteGroupedProjection(
        QuerySpecification querySpecification,
        IReadOnlyList<RuntimeProjection> projections,
        IReadOnlyList<RuntimeFrame> frames,
        GroupByClause? groupByClause,
        int visibleCommonTableExpressionOrdinal,
        RuntimeFrame? outerFrame)
    {
        var groupingExpressions = groupByClause is null
            ? []
            : navigator.OrderedItems<GroupByClauseGroupingSpecificationsItem>(groupByClause.Id)
                .Select(item => RequireGroupingExpression(item.GroupingSpecification))
                .ToArray();

        foreach (var groupingExpression in groupingExpressions)
        {
            if (ContainsAggregate(groupingExpression))
            {
                throw Fault(
                    "GroupingExpressionAggregateInvalid",
                    "GROUP BY expressions cannot contain aggregate functions.",
                    groupingExpression.Id);
            }
        }

        if (groupByClause is null)
        {
            foreach (var projection in projections)
            {
                if (ContainsColumnOutsideAggregate(projection.Expression))
                {
                    throw Fault(
                        "UngroupedColumnReference",
                        $"Aggregate query projection '{projection.Name}' references a source column outside an aggregate without GROUP BY.",
                        projection.Expression.Id);
                }
            }
        }
        else
        {
            ValidateGroupedProjectionStructure(projections, groupingExpressions);
        }

        var groups = new List<List<RuntimeFrame>>();
        if (groupByClause is null)
        {
            groups.Add(frames.ToList());
        }
        else
        {
            var groupsByKey = new Dictionary<RuntimeRow, List<RuntimeFrame>>(RuntimeRowEqualityComparer.Instance);
            foreach (var frame in frames)
            {
                var context = new RuntimeEvaluationContext(frame, visibleCommonTableExpressionOrdinal);
                var key = new RuntimeRow(groupingExpressions
                    .Select(expression => EvaluateScalarExpression(expression, context))
                    .ToArray());
                if (!groupsByKey.TryGetValue(key, out var group))
                {
                    group = [];
                    groupsByKey.Add(key, group);
                    groups.Add(group);
                }

                group.Add(frame);
            }
        }

        var rows = new List<RuntimeRow>();
        foreach (var group in groups)
        {
            var frame = group.Count > 0
                ? group[0]
                : new RuntimeFrame(new RuntimeLocalRow(), outerFrame);
            var context = new RuntimeEvaluationContext(
                frame,
                visibleCommonTableExpressionOrdinal,
                group);
            var projected = ProjectRow(projections, context);

            if (group.Count > 1)
            {
                ValidateGroupedProjectionDeterminism(projections, group, context, projected);
            }

            rows.Add(projected);
        }

        return rows;
    }

    private void ValidateGroupedProjectionDeterminism(
        IReadOnlyList<RuntimeProjection> projections,
        IReadOnlyList<RuntimeFrame> group,
        RuntimeEvaluationContext baseContext,
        RuntimeRow projected)
    {
        for (var frameIndex = 1; frameIndex < group.Count; frameIndex++)
        {
            var context = baseContext with { Frame = group[frameIndex] };
            for (var projectionIndex = 0; projectionIndex < projections.Count; projectionIndex++)
            {
                var candidate = EvaluateScalarExpression(projections[projectionIndex].Expression, context);
                if (!MetaWeaveScriptValueEqualityComparer.Instance.Equals(
                        projected.Values[projectionIndex],
                        candidate))
                {
                    throw Fault(
                        "GroupedProjectionNotDeterministic",
                        $"Grouped projection '{projections[projectionIndex].Name}' depends on values that are not stable within its group.",
                        projections[projectionIndex].Expression.Id);
                }
            }
        }
    }

    private ScalarExpression RequireGroupingExpression(GroupingSpecification groupingSpecification)
    {
        var expressionGrouping = navigator.TrySubtype<ExpressionGroupingSpecification>(groupingSpecification.Id)
            ?? throw Fault(
                "GroupingSpecificationShapeUnsupported",
                $"GroupingSpecification '{groupingSpecification.Id}' has no retained expression subtype.",
                groupingSpecification.Id);
        return navigator.RequireOwnerLink<ExpressionGroupingSpecificationExpressionLink>(
            expressionGrouping.Id,
            "ExpressionGroupingSpecification.Expression").ScalarExpression;
    }

    private RuntimeProjection CreateProjection(SelectElement selectElement)
    {
        var scalar = navigator.TrySubtype<SelectScalarExpression>(selectElement.Id)
            ?? throw Fault(
                "SelectElementShapeUnsupported",
                $"SelectElement '{selectElement.Id}' has no retained scalar subtype.",
                selectElement.Id);
        var expression = navigator.RequireOwnerLink<SelectScalarExpressionExpressionLink>(
            scalar.Id,
            "SelectScalarExpression.Expression").ScalarExpression;
        var nameLink = navigator.TryOwnerLink<SelectScalarExpressionColumnNameLink>(scalar.Id);
        var name = nameLink is null
            ? TryDeriveProjectionName(expression) ?? string.Empty
            : RequireIdentifierOrValueExpression(nameLink.IdentifierOrValueExpression);
        return new RuntimeProjection(name, expression);
    }

    private RuntimeRow ProjectRow(
        IReadOnlyList<RuntimeProjection> projections,
        RuntimeEvaluationContext context) =>
        new(projections.Select(projection => EvaluateScalarExpression(projection.Expression, context)).ToArray());

    private string RequireIdentifierOrValueExpression(IdentifierOrValueExpression value)
    {
        var link = navigator.TryOwnerLink<IdentifierOrValueExpressionIdentifierLink>(value.Id)
            ?? throw Fault(
                "ProjectionAliasMissing",
                $"IdentifierOrValueExpression '{value.Id}' has no retained identifier.",
                value.Id);
        return navigator.RequireIdentifier(link.Identifier, "SelectScalarExpression.ColumnName");
    }

    private string? TryDeriveProjectionName(ScalarExpression expression)
    {
        var parts = TryGetDirectColumnReferenceParts(expression);
        return parts is { Count: > 0 } ? parts[^1] : null;
    }

    private sealed record RuntimeProjection(string Name, ScalarExpression Expression);
}
