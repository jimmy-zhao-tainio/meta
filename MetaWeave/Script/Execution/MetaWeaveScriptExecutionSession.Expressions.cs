using System.Globalization;

namespace MetaWeaveScript.Execution;

internal sealed partial class MetaWeaveScriptExecutionSession
{
    private static readonly HashSet<string> AggregateFunctionNames =
        new(StringComparer.OrdinalIgnoreCase) { "COUNT", "MIN", "MAX", "STRING_AGG" };

    private MetaWeaveScriptValue EvaluateScalarExpression(
        ScalarExpression expression,
        RuntimeEvaluationContext context)
    {
        var primary = navigator.TrySubtype<PrimaryExpression>(expression.Id)
            ?? throw Fault(
                "ScalarExpressionShapeUnsupported",
                $"ScalarExpression '{expression.Id}' has no retained primary-expression subtype.",
                expression.Id);

        if (navigator.TrySubtype<ColumnReferenceExpression>(primary.Id) is { } columnReference)
        {
            return EvaluateColumnReference(columnReference, context.Frame);
        }

        if (navigator.TrySubtype<ParenthesisExpression>(primary.Id) is { } parenthesis)
        {
            var inner = navigator.RequireOwnerLink<ParenthesisExpressionExpressionLink>(
                parenthesis.Id,
                "ParenthesisExpression.Expression").ScalarExpression;
            return EvaluateScalarExpression(inner, context);
        }

        if (navigator.TrySubtype<CaseExpression>(primary.Id) is { } caseExpression)
        {
            var searched = navigator.TrySubtype<SearchedCaseExpression>(caseExpression.Id)
                ?? throw Fault(
                    "CaseExpressionShapeUnsupported",
                    $"CaseExpression '{caseExpression.Id}' has no retained searched-case subtype.",
                    caseExpression.Id);
            return EvaluateSearchedCaseExpression(searched, caseExpression, context);
        }

        if (navigator.TrySubtype<CoalesceExpression>(primary.Id) is { } coalesce)
        {
            foreach (var item in navigator.OrderedItems<CoalesceExpressionExpressionsItem>(coalesce.Id))
            {
                var value = EvaluateScalarExpression(item.ScalarExpression, context);
                if (!value.IsNull)
                {
                    return value;
                }
            }

            return MetaWeaveScriptValue.Null;
        }

        if (navigator.TrySubtype<NullIfExpression>(primary.Id) is { } nullIf)
        {
            var first = EvaluateScalarExpression(
                navigator.RequireOwnerLink<NullIfExpressionFirstExpressionLink>(
                    nullIf.Id,
                    "NullIfExpression.FirstExpression").ScalarExpression,
                context);
            var second = EvaluateScalarExpression(
                navigator.RequireOwnerLink<NullIfExpressionSecondExpressionLink>(
                    nullIf.Id,
                    "NullIfExpression.SecondExpression").ScalarExpression,
                context);
            return EqualValues(first, second) == RuntimeTruth.True
                ? MetaWeaveScriptValue.Null
                : first;
        }

        if (navigator.TrySubtype<IIfCall>(primary.Id) is { } iIf)
        {
            var predicate = navigator.RequireOwnerLink<IIfCallPredicateLink>(
                iIf.Id,
                "IIfCall.Predicate").BooleanExpression;
            var branch = EvaluateBooleanExpression(predicate, context) == RuntimeTruth.True
                ? navigator.RequireOwnerLink<IIfCallThenExpressionLink>(
                    iIf.Id,
                    "IIfCall.ThenExpression").ScalarExpression
                : navigator.RequireOwnerLink<IIfCallElseExpressionLink>(
                    iIf.Id,
                    "IIfCall.ElseExpression").ScalarExpression;
            return EvaluateScalarExpression(branch, context);
        }

        if (navigator.TrySubtype<TryConvertCall>(primary.Id) is { } tryConvert)
        {
            return EvaluateTryConvert(tryConvert, context);
        }

        if (navigator.TrySubtype<FunctionCall>(primary.Id) is { } functionCall)
        {
            return EvaluateFunctionCall(functionCall, context);
        }

        if (navigator.TrySubtype<ScalarSubquery>(primary.Id) is { } subquery)
        {
            return EvaluateScalarSubquery(subquery, context);
        }

        if (navigator.TrySubtype<ValueExpression>(primary.Id) is { } valueExpression)
        {
            return EvaluateValueExpression(valueExpression);
        }

        throw Fault(
            "PrimaryExpressionShapeUnsupported",
            $"PrimaryExpression '{primary.Id}' has no retained semantic subtype.",
            primary.Id);
    }

    private MetaWeaveScriptValue EvaluateColumnReference(
        ColumnReferenceExpression columnReference,
        RuntimeFrame frame)
    {
        if (string.Equals(columnReference.ColumnType, "Wildcard", StringComparison.Ordinal))
        {
            throw Fault(
                "WildcardOutsideCount",
                "Wildcard column references are executable only as COUNT(*).",
                columnReference.Id);
        }

        var identifierLink = navigator.TryOwnerLink<ColumnReferenceExpressionMultiPartIdentifierLink>(columnReference.Id)
            ?? throw Fault(
                "ColumnReferenceIdentifierMissing",
                $"ColumnReferenceExpression '{columnReference.Id}' has no multipart identifier.",
                columnReference.Id);
        var parts = navigator.IdentifierParts(identifierLink.MultiPartIdentifier);
        return ResolveColumn(parts, frame, columnReference.Id);
    }

    private MetaWeaveScriptValue ResolveColumn(
        IReadOnlyList<string> parts,
        RuntimeFrame frame,
        string syntaxId)
    {
        if (resolvedColumns.TryGetValue(syntaxId, out var resolved))
        {
            return ReadResolvedColumn(resolved, frame, syntaxId);
        }

        if (parts.Count == 1)
        {
            var depth = 0;
            for (var current = frame; current is not null; current = current.Parent, depth++)
            {
                var matches = new List<RuntimeResolvedColumnReference>();
                foreach (var source in current.Local.Sources.Values)
                {
                    AddColumnMatches(source, parts[0], depth, matches);
                }

                if (matches.Count > 1)
                {
                    throw Fault(
                        "ColumnReferenceAmbiguous",
                        $"Unqualified member reference '{parts[0]}' is ambiguous in its nearest scope.",
                        syntaxId);
                }

                if (matches.Count == 1)
                {
                    resolvedColumns.Add(syntaxId, matches[0]);
                    return ReadResolvedColumn(matches[0], frame, syntaxId);
                }
            }

            throw Fault(
                "ColumnReferenceNotFound",
                $"Member reference '{parts[0]}' was not found in the current or correlated scopes.",
                syntaxId);
        }

        if (parts.Count == 2)
        {
            var depth = 0;
            for (var current = frame; current is not null; current = current.Parent, depth++)
            {
                if (!current.Local.Sources.TryGetValue(parts[0], out var source))
                {
                    continue;
                }

                var matches = new List<RuntimeResolvedColumnReference>();
                AddColumnMatches(source, parts[1], depth, matches);
                if (matches.Count == 1)
                {
                    resolvedColumns.Add(syntaxId, matches[0]);
                    return ReadResolvedColumn(matches[0], frame, syntaxId);
                }

                return matches.Count switch
                {
                    > 1 => throw Fault(
                        "ColumnReferenceAmbiguous",
                        $"Member reference '{parts[0]}.{parts[1]}' is ambiguous.",
                        syntaxId),
                    _ => throw Fault(
                        "ColumnReferenceNotFound",
                        $"Source or alias '{parts[0]}' does not expose member '{parts[1]}'.",
                        syntaxId)
                };
            }

            throw Fault(
                "TableAliasNotFound",
                $"Source or alias '{parts[0]}' was not found in the current or correlated scopes.",
                syntaxId);
        }

        throw Fault(
            "ColumnReferenceShapeInvalid",
            $"Member reference '{string.Join(".", parts)}' must contain one member or one alias/member pair.",
            syntaxId);
    }

    private static void AddColumnMatches(
        RuntimeSourceRow source,
        string columnName,
        int scopeDepth,
        ICollection<RuntimeResolvedColumnReference> matches)
    {
        for (var ordinal = 0; ordinal < source.Shape.Columns.Count; ordinal++)
        {
            if (string.Equals(
                    source.Shape.Columns[ordinal].Name,
                    columnName,
                    StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(new RuntimeResolvedColumnReference(
                    scopeDepth,
                    source.Shape.Name,
                    ordinal));
            }
        }
    }

    private static MetaWeaveScriptValue ReadResolvedColumn(
        RuntimeResolvedColumnReference resolved,
        RuntimeFrame frame,
        string syntaxId)
    {
        var current = frame;
        for (var depth = 0; depth < resolved.ScopeDepth; depth++)
        {
            current = current.Parent
                ?? throw Fault(
                    "ResolvedColumnScopeChanged",
                    "A previously resolved member reference no longer has its expected outer scope.",
                    syntaxId);
        }

        if (!current.Local.Sources.TryGetValue(resolved.SourceName, out var source) ||
            resolved.ColumnOrdinal >= source.Row.Values.Length ||
            resolved.ColumnOrdinal >= source.Shape.Columns.Count)
        {
            throw Fault(
                "ResolvedColumnShapeChanged",
                "A previously resolved member reference no longer has its expected rowset shape.",
                syntaxId);
        }

        return source.Row.Values[resolved.ColumnOrdinal];
    }

    private MetaWeaveScriptValue EvaluateSearchedCaseExpression(
        SearchedCaseExpression searched,
        CaseExpression caseExpression,
        RuntimeEvaluationContext context)
    {
        foreach (var item in navigator.OrderedItems<SearchedCaseExpressionWhenClausesItem>(searched.Id))
        {
            var searchedWhen = item.SearchedWhenClause;
            var predicate = navigator.RequireOwnerLink<SearchedWhenClauseWhenExpressionLink>(
                searchedWhen.Id,
                "SearchedWhenClause.WhenExpression").BooleanExpression;
            if (EvaluateBooleanExpression(predicate, context) != RuntimeTruth.True)
            {
                continue;
            }

            var whenClause = navigator.RequireById<WhenClause>(
                searchedWhen.WhenClause.Id,
                "SearchedWhenClause.WhenClause");
            var thenExpression = navigator.RequireOwnerLink<WhenClauseThenExpressionLink>(
                whenClause.Id,
                "WhenClause.ThenExpression").ScalarExpression;
            return EvaluateScalarExpression(thenExpression, context);
        }

        var elseLink = navigator.TryOwnerLink<CaseExpressionElseExpressionLink>(caseExpression.Id);
        return elseLink is null
            ? MetaWeaveScriptValue.Null
            : EvaluateScalarExpression(elseLink.ScalarExpression, context);
    }

    private MetaWeaveScriptValue EvaluateValueExpression(ValueExpression valueExpression)
    {
        if (navigator.TrySubtype<ParameterReferenceExpression>(valueExpression.Id) is { } parameter)
        {
            if (!parameters.TryGetValue(parameter.Name, out var value))
            {
                throw Fault(
                    "ParameterValueMissing",
                    $"No value was supplied for WeaveScript parameter '@{parameter.Name}'.",
                    parameter.Id);
            }

            return value;
        }

        var literal = navigator.TrySubtype<Literal>(valueExpression.Id);
        if (literal is null)
        {
            throw Fault(
                "ValueExpressionShapeUnsupported",
                $"ValueExpression '{valueExpression.Id}' has no retained value subtype.",
                valueExpression.Id);
        }
        if (navigator.TrySubtype<StringLiteral>(literal.Id) is not null)
        {
            return MetaWeaveScriptValue.FromString(literal.Value ?? string.Empty);
        }

        if (navigator.TrySubtype<IntegerLiteral>(literal.Id) is not null)
        {
            if (!long.TryParse(literal.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                throw Fault(
                    "IntegerLiteralInvalid",
                    $"Integer literal '{literal.Value}' is outside the WeaveScript 64-bit integer range.",
                    literal.Id);
            }

            return MetaWeaveScriptValue.FromInteger(value);
        }

        if (navigator.TrySubtype<NullLiteral>(literal.Id) is not null)
        {
            return MetaWeaveScriptValue.Null;
        }

        throw Fault(
            "LiteralShapeUnsupported",
            $"Literal '{literal.Id}' has no retained literal subtype.",
            literal.Id);
    }

    private MetaWeaveScriptValue EvaluateScalarSubquery(
        ScalarSubquery subquery,
        RuntimeEvaluationContext context)
    {
        var query = navigator.RequireOwnerLink<ScalarSubqueryQueryExpressionLink>(
            subquery.Id,
            "ScalarSubquery.QueryExpression").QueryExpression;
        var result = ExecuteQueryExpression(
            query,
            context.VisibleCommonTableExpressionOrdinal,
            context.Frame);
        if (result.Columns.Count != 1)
        {
            throw Fault(
                "ScalarSubqueryColumnCountInvalid",
                $"Scalar subquery '{subquery.Id}' produces {result.Columns.Count} columns; exactly one is required.",
                subquery.Id);
        }

        return result.Rows.Count switch
        {
            0 => MetaWeaveScriptValue.Null,
            1 => result.Rows[0].Values[0],
            _ => throw Fault(
                "ScalarSubqueryCardinalityInvalid",
                $"Scalar subquery '{subquery.Id}' produces {result.Rows.Count} rows; at most one is allowed.",
                subquery.Id)
        };
    }

    private MetaWeaveScriptValue EvaluateFunctionCall(
        FunctionCall functionCall,
        RuntimeEvaluationContext context)
    {
        var name = FunctionName(functionCall);
        return AggregateFunctionNames.Contains(name)
            ? EvaluateAggregateFunction(functionCall, name, context)
            : WindowFunctionNames.Contains(name)
                ? EvaluateWindowFunction(functionCall, name, context)
            : EvaluateScalarFunction(functionCall, name, context);
    }

    private MetaWeaveScriptValue EvaluateTryConvert(
        TryConvertCall tryConvert,
        RuntimeEvaluationContext context)
    {
        var value = EvaluateScalarExpression(
            navigator.RequireOwnerLink<TryConvertCallParameterLink>(
                tryConvert.Id,
                "TryConvertCall.Parameter").ScalarExpression,
            context);
        if (value.IsNull)
        {
            return MetaWeaveScriptValue.Null;
        }

        if (value.Kind == MetaWeaveScriptValueKind.Integer)
        {
            return value.IntegerValue is >= int.MinValue and <= int.MaxValue
                ? value
                : MetaWeaveScriptValue.Null;
        }

        if (value.Kind != MetaWeaveScriptValueKind.String)
        {
            throw Fault(
                "TryConvertArgumentInvalid",
                $"TRY_CONVERT(int, ...) requires a string or integer argument, but received {value.Kind}.",
                tryConvert.Id);
        }

        return int.TryParse(
            value.StringValue,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var converted)
            ? MetaWeaveScriptValue.FromInteger(converted)
            : MetaWeaveScriptValue.Null;
    }

    private MetaWeaveScriptValue EvaluateWindowFunction(
        FunctionCall functionCall,
        string name,
        RuntimeEvaluationContext context)
    {
        if (!string.Equals(name, "ROW_NUMBER", StringComparison.OrdinalIgnoreCase))
        {
            throw Fault(
                "WindowFunctionUnsupported",
                $"Window function '{name}' is outside the WeaveScript function catalog.",
                functionCall.Id);
        }

        if (context.WindowFrames is null ||
            context.WindowFrameOrdinal < 0 ||
            context.WindowFrameOrdinal >= context.WindowFrames.Count)
        {
            throw Fault(
                "WindowFunctionContextInvalid",
                "ROW_NUMBER is executable only as a direct SELECT projection.",
                functionCall.Id);
        }

        var overClause = navigator.RequireOwnerLink<FunctionCallOverClauseLink>(
            functionCall.Id,
            "FunctionCall.OverClause").OverClause;
        var partitions = navigator.OrderedItems<OverClausePartitionsItem>(overClause.Id);
        var orderByClause = navigator.RequireOwnerLink<OverClauseOrderByClauseLink>(
            overClause.Id,
            "OverClause.OrderByClause").OrderByClause;
        var orderItems = navigator.OrderedItems<OrderByClauseOrderByElementsItem>(orderByClause.Id);
        if (orderItems.Count == 0)
        {
            throw Fault(
                "RowNumberOrderMissing",
                "ROW_NUMBER requires at least one ordering expression.",
                functionCall.Id);
        }

        var key = new RuntimeWindowEvaluationKey(functionCall.Id, context.WindowFrames);
        if (!windowRowNumbers.TryGetValue(key, out var rowNumbers))
        {
            rowNumbers = EvaluateAllWindowRowNumbers(
                partitions,
                orderItems,
                context.VisibleCommonTableExpressionOrdinal,
                context.WindowFrames);
            windowRowNumbers.Add(key, rowNumbers);
        }

        return MetaWeaveScriptValue.FromInteger(rowNumbers[context.WindowFrameOrdinal]);
    }

    private long[] EvaluateAllWindowRowNumbers(
        IReadOnlyList<OverClausePartitionsItem> partitions,
        IReadOnlyList<OrderByClauseOrderByElementsItem> orderItems,
        int visibleCommonTableExpressionOrdinal,
        IReadOnlyList<RuntimeFrame> windowFrames)
    {
        var groups = new Dictionary<RuntimeRow, List<(RuntimeFrame Frame, int Ordinal)>>(
            RuntimeRowEqualityComparer.Instance);
        for (var ordinal = 0; ordinal < windowFrames.Count; ordinal++)
        {
            var partition = EvaluateWindowPartition(
                partitions,
                windowFrames[ordinal],
                visibleCommonTableExpressionOrdinal,
                windowFrames,
                ordinal);
            if (!groups.TryGetValue(partition, out var rows))
            {
                rows = [];
                groups.Add(partition, rows);
            }

            rows.Add((windowFrames[ordinal], ordinal));
        }

        var rowNumbers = new long[windowFrames.Count];
        foreach (var rows in groups.Values)
        {
            rows.Sort((left, right) => CompareWindowRows(
                left,
                right,
                orderItems,
                visibleCommonTableExpressionOrdinal,
                windowFrames));
            for (var rank = 0; rank < rows.Count; rank++)
            {
                rowNumbers[rows[rank].Ordinal] = rank + 1L;
            }
        }

        return rowNumbers;
    }

    private RuntimeRow EvaluateWindowPartition(
        IReadOnlyList<OverClausePartitionsItem> partitions,
        RuntimeFrame frame,
        int visibleCommonTableExpressionOrdinal,
        IReadOnlyList<RuntimeFrame> windowFrames,
        int frameOrdinal) =>
        new(partitions.Select(item => EvaluateScalarExpression(
            item.ScalarExpression,
            new RuntimeEvaluationContext(
                frame,
                visibleCommonTableExpressionOrdinal,
                WindowFrames: windowFrames,
                WindowFrameOrdinal: frameOrdinal))).ToArray());

    private int CompareWindowRows(
        (RuntimeFrame Frame, int Ordinal) left,
        (RuntimeFrame Frame, int Ordinal) right,
        IReadOnlyList<OrderByClauseOrderByElementsItem> orderItems,
        int visibleCommonTableExpressionOrdinal,
        IReadOnlyList<RuntimeFrame> windowFrames)
    {
        foreach (var item in orderItems)
        {
            var expression = navigator.RequireOwnerLink<ExpressionWithSortOrderExpressionLink>(
                item.ExpressionWithSortOrder.Id,
                "ExpressionWithSortOrder.Expression").ScalarExpression;
            var leftValue = EvaluateScalarExpression(
                expression,
                new RuntimeEvaluationContext(
                    left.Frame,
                    visibleCommonTableExpressionOrdinal,
                    WindowFrames: windowFrames,
                    WindowFrameOrdinal: left.Ordinal));
            var rightValue = EvaluateScalarExpression(
                expression,
                new RuntimeEvaluationContext(
                    right.Frame,
                    visibleCommonTableExpressionOrdinal,
                    WindowFrames: windowFrames,
                    WindowFrameOrdinal: right.Ordinal));
            var comparison = CompareValues(leftValue, rightValue, nullsFirst: true);
            if (comparison != 0)
            {
                return string.Equals(
                    item.ExpressionWithSortOrder.SortOrder,
                    "Descending",
                    StringComparison.Ordinal)
                    ? -comparison
                    : comparison;
            }
        }

        return left.Ordinal.CompareTo(right.Ordinal);
    }

    private MetaWeaveScriptValue EvaluateAggregateFunction(
        FunctionCall functionCall,
        string name,
        RuntimeEvaluationContext context)
    {
        if (context.WithinAggregate)
        {
            throw Fault(
                "NestedAggregateInvalid",
                $"Aggregate function '{name}' cannot be nested inside another aggregate.",
                functionCall.Id);
        }

        if (context.GroupFrames is null)
        {
            throw Fault(
                "AggregateOutsideGrouping",
                $"Aggregate function '{name}' is not being evaluated in a grouped query.",
                functionCall.Id);
        }

        var parameters = navigator.OrderedItems<FunctionCallParametersItem>(functionCall.Id);
        if (string.Equals(name, "COUNT", StringComparison.OrdinalIgnoreCase))
        {
            RequireArity(name, parameters.Count, 1, functionCall.Id);
            if (IsWildcardExpression(parameters[0].ScalarExpression))
            {
                return MetaWeaveScriptValue.FromInteger(context.GroupFrames.Count);
            }

            var count = context.GroupFrames.Count(frame => !EvaluateScalarExpression(
                parameters[0].ScalarExpression,
                new RuntimeEvaluationContext(
                    frame,
                    context.VisibleCommonTableExpressionOrdinal,
                    WithinAggregate: true)).IsNull);
            return MetaWeaveScriptValue.FromInteger(count);
        }

        if (string.Equals(name, "STRING_AGG", StringComparison.OrdinalIgnoreCase))
        {
            RequireArity(name, parameters.Count, 2, functionCall.Id);
            return EvaluateStringAggregate(functionCall, parameters, context);
        }

        RequireArity(name, parameters.Count, 1, functionCall.Id);
        var values = context.GroupFrames
            .Select(frame => EvaluateScalarExpression(
                parameters[0].ScalarExpression,
                new RuntimeEvaluationContext(
                    frame,
                    context.VisibleCommonTableExpressionOrdinal,
                    WithinAggregate: true)))
            .Where(value => !value.IsNull)
            .ToArray();
        if (values.Length == 0)
        {
            return MetaWeaveScriptValue.Null;
        }

        var selected = values[0];
        foreach (var candidate in values.Skip(1))
        {
            var comparison = CompareValues(candidate, selected);
            if ((string.Equals(name, "MIN", StringComparison.OrdinalIgnoreCase) && comparison < 0) ||
                (string.Equals(name, "MAX", StringComparison.OrdinalIgnoreCase) && comparison > 0))
            {
                selected = candidate;
            }
        }

        return selected;
    }

    private MetaWeaveScriptValue EvaluateStringAggregate(
        FunctionCall functionCall,
        IReadOnlyList<FunctionCallParametersItem> parameters,
        RuntimeEvaluationContext context)
    {
        var orderLink = navigator.TryOwnerLink<FunctionCallWithinGroupOrderByClauseLink>(functionCall.Id)
            ?? throw Fault(
                "StringAggregateOrderMissing",
                "STRING_AGG requires WITHIN GROUP ordering.",
                functionCall.Id);
        var orderItems = navigator.OrderedItems<OrderByClauseOrderByElementsItem>(orderLink.OrderByClause.Id);
        if (orderItems.Count == 0)
        {
            throw Fault(
                "StringAggregateOrderMissing",
                "STRING_AGG WITHIN GROUP requires at least one ordering expression.",
                functionCall.Id);
        }

        var separatorFrame = context.GroupFrames!.Count > 0
            ? context.GroupFrames[0]
            : context.Frame;
        var separator = EvaluateScalarExpression(
            parameters[1].ScalarExpression,
            new RuntimeEvaluationContext(
                separatorFrame,
                context.VisibleCommonTableExpressionOrdinal,
                WithinAggregate: true));
        if (!separator.IsNull && separator.Kind != MetaWeaveScriptValueKind.String)
        {
            throw Fault(
                "StringAggregateSeparatorInvalid",
                $"STRING_AGG separator must be a string or NULL, but received {separator.Kind}.",
                parameters[1].ScalarExpression.Id);
        }

        foreach (var frame in context.GroupFrames.Skip(1))
        {
            var candidate = EvaluateScalarExpression(
                parameters[1].ScalarExpression,
                new RuntimeEvaluationContext(
                    frame,
                    context.VisibleCommonTableExpressionOrdinal,
                    WithinAggregate: true));
            if (!MetaWeaveScriptValueEqualityComparer.Instance.Equals(separator, candidate))
            {
                throw Fault(
                    "StringAggregateSeparatorNotStable",
                    "STRING_AGG separator must be constant within each group.",
                    parameters[1].ScalarExpression.Id);
            }
        }

        var orderedFrames = context.GroupFrames
            .Select((frame, ordinal) => (Frame: frame, Ordinal: ordinal))
            .ToList();
        orderedFrames.Sort((left, right) => CompareAggregateOrderRows(
            left,
            right,
            orderItems,
            context.VisibleCommonTableExpressionOrdinal));

        var strings = orderedFrames
            .Select(item => EvaluateScalarExpression(
                parameters[0].ScalarExpression,
                new RuntimeEvaluationContext(
                    item.Frame,
                    context.VisibleCommonTableExpressionOrdinal,
                    WithinAggregate: true)))
            .Where(value => !value.IsNull)
            .Select(value => value.ToInvariantString())
            .ToArray();
        return strings.Length == 0
            ? MetaWeaveScriptValue.Null
            : MetaWeaveScriptValue.FromString(string.Join(separator.StringValue ?? string.Empty, strings));
    }

    private int CompareAggregateOrderRows(
        (RuntimeFrame Frame, int Ordinal) left,
        (RuntimeFrame Frame, int Ordinal) right,
        IReadOnlyList<OrderByClauseOrderByElementsItem> orderItems,
        int visibleCommonTableExpressionOrdinal)
    {
        foreach (var item in orderItems)
        {
            var expression = navigator.RequireOwnerLink<ExpressionWithSortOrderExpressionLink>(
                item.ExpressionWithSortOrder.Id,
                "ExpressionWithSortOrder.Expression").ScalarExpression;
            var leftValue = EvaluateScalarExpression(
                expression,
                new RuntimeEvaluationContext(
                    left.Frame,
                    visibleCommonTableExpressionOrdinal,
                    WithinAggregate: true));
            var rightValue = EvaluateScalarExpression(
                expression,
                new RuntimeEvaluationContext(
                    right.Frame,
                    visibleCommonTableExpressionOrdinal,
                    WithinAggregate: true));
            var descending = string.Equals(
                item.ExpressionWithSortOrder.SortOrder,
                "Descending",
                StringComparison.Ordinal);
            if (!descending &&
                !string.IsNullOrWhiteSpace(item.ExpressionWithSortOrder.SortOrder) &&
                !string.Equals(item.ExpressionWithSortOrder.SortOrder, "Ascending", StringComparison.Ordinal) &&
                !string.Equals(item.ExpressionWithSortOrder.SortOrder, "NotSpecified", StringComparison.Ordinal))
            {
                throw Fault(
                    "SortOrderUnsupported",
                    $"Sort order '{item.ExpressionWithSortOrder.SortOrder}' is outside the retained surface.",
                    item.ExpressionWithSortOrder.Id);
            }

            var comparison = CompareValues(leftValue, rightValue, nullsFirst: true);
            if (comparison != 0)
            {
                return descending ? -comparison : comparison;
            }
        }

        return left.Ordinal.CompareTo(right.Ordinal);
    }

    private MetaWeaveScriptValue EvaluateScalarFunction(
        FunctionCall functionCall,
        string name,
        RuntimeEvaluationContext context)
    {
        var parameters = navigator.OrderedItems<FunctionCallParametersItem>(functionCall.Id)
            .Select(item => EvaluateScalarExpression(item.ScalarExpression, context))
            .ToArray();
        switch (name.ToUpperInvariant())
        {
            case "CONCAT":
                if (parameters.Length < 2)
                {
                    throw Fault("FunctionArityInvalid", "CONCAT requires at least two arguments.", functionCall.Id);
                }

                return MetaWeaveScriptValue.FromString(string.Concat(parameters.Select(value =>
                    value.IsNull ? string.Empty : value.ToInvariantString())));
            case "LOWER":
                RequireArity(name, parameters.Length, 1, functionCall.Id);
                return MapString(parameters[0], value => value.ToLowerInvariant(), name, functionCall.Id);
            case "UPPER":
                RequireArity(name, parameters.Length, 1, functionCall.Id);
                return MapString(parameters[0], value => value.ToUpperInvariant(), name, functionCall.Id);
            case "TRIM":
                RequireArity(name, parameters.Length, 1, functionCall.Id);
                return MapString(parameters[0], value => value.Trim(' '), name, functionCall.Id);
            case "LTRIM":
                RequireArity(name, parameters.Length, 1, functionCall.Id);
                return MapString(parameters[0], value => value.TrimStart(' '), name, functionCall.Id);
            case "RTRIM":
                RequireArity(name, parameters.Length, 1, functionCall.Id);
                return MapString(parameters[0], value => value.TrimEnd(' '), name, functionCall.Id);
            case "REPLACE":
                RequireArity(name, parameters.Length, 3, functionCall.Id);
                return EvaluateReplace(parameters, functionCall.Id);
            case "SUBSTRING":
                RequireArity(name, parameters.Length, 3, functionCall.Id);
                return EvaluateSubstring(parameters, functionCall.Id);
            case "LEFT":
                RequireArity(name, parameters.Length, 2, functionCall.Id);
                return EvaluateEdgeSubstring(parameters, fromLeft: true, functionCall.Id);
            case "RIGHT":
                RequireArity(name, parameters.Length, 2, functionCall.Id);
                return EvaluateEdgeSubstring(parameters, fromLeft: false, functionCall.Id);
            case "LEN":
                RequireArity(name, parameters.Length, 1, functionCall.Id);
                if (parameters[0].IsNull)
                {
                    return MetaWeaveScriptValue.Null;
                }

                RequireStringArguments(parameters, name, functionCall.Id);
                return MetaWeaveScriptValue.FromInteger(
                    parameters[0].StringValue!.TrimEnd(' ').Length);
            case "IS_BLANK":
                RequireArity(name, parameters.Length, 1, functionCall.Id);
                if (parameters[0].IsNull)
                {
                    return MetaWeaveScriptValue.FromInteger(1);
                }

                if (parameters[0].Kind != MetaWeaveScriptValueKind.String)
                {
                    throw Fault(
                        "StringFunctionArgumentInvalid",
                        $"{name} requires a string argument, but received {parameters[0].Kind}.",
                        functionCall.Id);
                }

                return MetaWeaveScriptValue.FromInteger(
                    string.IsNullOrWhiteSpace(parameters[0].StringValue) ? 1 : 0);
            default:
                throw Fault(
                    "ScalarFunctionUnsupported",
                    $"Scalar function '{name}' is outside the WeaveScript function catalog.",
                    functionCall.Id);
        }
    }

    private static MetaWeaveScriptValue MapString(
        MetaWeaveScriptValue value,
        Func<string, string> map,
        string functionName,
        string syntaxId)
    {
        if (value.IsNull)
        {
            return MetaWeaveScriptValue.Null;
        }

        if (value.Kind != MetaWeaveScriptValueKind.String)
        {
            throw Fault(
                "StringFunctionArgumentInvalid",
                $"{functionName} requires a string argument, but received {value.Kind}.",
                syntaxId);
        }

        return MetaWeaveScriptValue.FromString(map(value.StringValue!));
    }

    private static MetaWeaveScriptValue EvaluateReplace(
        IReadOnlyList<MetaWeaveScriptValue> parameters,
        string syntaxId)
    {
        if (parameters.Any(value => value.IsNull))
        {
            return MetaWeaveScriptValue.Null;
        }

        RequireStringArguments(parameters, "REPLACE", syntaxId);
        var source = parameters[0].StringValue!;
        var oldValue = parameters[1].StringValue!;
        return MetaWeaveScriptValue.FromString(oldValue.Length == 0
            ? source
            : source.Replace(oldValue, parameters[2].StringValue!, StringComparison.OrdinalIgnoreCase));
    }

    private static MetaWeaveScriptValue EvaluateSubstring(
        IReadOnlyList<MetaWeaveScriptValue> parameters,
        string syntaxId)
    {
        if (parameters.Any(value => value.IsNull))
        {
            return MetaWeaveScriptValue.Null;
        }

        RequireStringArguments(parameters.Take(1), "SUBSTRING", syntaxId);
        var start = RequireInteger(parameters[1], "SUBSTRING start", syntaxId);
        var length = RequireInteger(parameters[2], "SUBSTRING length", syntaxId);
        if (length < 0)
        {
            throw Fault("SubstringLengthInvalid", "SUBSTRING length cannot be negative.", syntaxId);
        }

        var source = parameters[0].StringValue!;
        var zeroBasedStart = start <= 1 ? 0 : start - 1;
        var effectiveLength = length;
        if (start < 1)
        {
            if (start == long.MinValue)
            {
                effectiveLength = 0;
            }
            else
            {
                var skipped = 1 - start;
                effectiveLength = skipped >= length ? 0 : length - skipped;
            }
        }
        if (zeroBasedStart >= source.Length || effectiveLength == 0)
        {
            return MetaWeaveScriptValue.FromString(string.Empty);
        }

        var available = source.Length - zeroBasedStart;
        var take = Math.Min(effectiveLength, available);
        if (zeroBasedStart > int.MaxValue || take > int.MaxValue)
        {
            throw Fault("SubstringRangeInvalid", "SUBSTRING range exceeds the supported string range.", syntaxId);
        }

        return MetaWeaveScriptValue.FromString(source.Substring((int)zeroBasedStart, (int)take));
    }

    private static MetaWeaveScriptValue EvaluateEdgeSubstring(
        IReadOnlyList<MetaWeaveScriptValue> parameters,
        bool fromLeft,
        string syntaxId)
    {
        if (parameters.Any(value => value.IsNull))
        {
            return MetaWeaveScriptValue.Null;
        }

        RequireStringArguments(parameters.Take(1), fromLeft ? "LEFT" : "RIGHT", syntaxId);
        var count = RequireInteger(parameters[1], fromLeft ? "LEFT length" : "RIGHT length", syntaxId);
        if (count < 0)
        {
            throw Fault(
                "EdgeSubstringLengthInvalid",
                $"{(fromLeft ? "LEFT" : "RIGHT")} length cannot be negative.",
                syntaxId);
        }

        var source = parameters[0].StringValue!;
        var take = (int)Math.Min(count, source.Length);
        return MetaWeaveScriptValue.FromString(fromLeft
            ? source[..take]
            : source[(source.Length - take)..]);
    }

    private static void RequireStringArguments(
        IEnumerable<MetaWeaveScriptValue> values,
        string functionName,
        string syntaxId)
    {
        foreach (var value in values)
        {
            if (value.Kind != MetaWeaveScriptValueKind.String)
            {
                throw Fault(
                    "StringFunctionArgumentInvalid",
                    $"{functionName} requires string arguments, but received {value.Kind}.",
                    syntaxId);
            }
        }
    }

    private static void RequireArity(string name, int actual, int expected, string syntaxId)
    {
        if (actual != expected)
        {
            throw Fault(
                "FunctionArityInvalid",
                $"Function '{name}' requires {expected} arguments, but received {actual}.",
                syntaxId);
        }
    }

    private bool IsWildcardExpression(ScalarExpression expression)
    {
        var primary = navigator.TrySubtype<PrimaryExpression>(expression.Id);
        var column = primary is null ? null : navigator.TrySubtype<ColumnReferenceExpression>(primary.Id);
        return column is not null && string.Equals(column.ColumnType, "Wildcard", StringComparison.Ordinal);
    }

    private bool ContainsAggregate(ScalarExpression expression)
    {
        var primary = navigator.TrySubtype<PrimaryExpression>(expression.Id);
        if (primary is null)
        {
            return false;
        }

        if (navigator.TrySubtype<ParenthesisExpression>(primary.Id) is { } parenthesis)
        {
            return ContainsAggregate(navigator.RequireOwnerLink<ParenthesisExpressionExpressionLink>(
                parenthesis.Id,
                "ParenthesisExpression.Expression").ScalarExpression);
        }

        if (navigator.TrySubtype<FunctionCall>(primary.Id) is { } function)
        {
            return AggregateFunctionNames.Contains(FunctionName(function)) ||
                   navigator.OrderedItems<FunctionCallParametersItem>(function.Id)
                       .Any(item => ContainsAggregate(item.ScalarExpression));
        }

        if (navigator.TrySubtype<CaseExpression>(primary.Id) is { } caseExpression)
        {
            var searched = navigator.TrySubtype<SearchedCaseExpression>(caseExpression.Id);
            return searched is not null &&
                   (navigator.OrderedItems<SearchedCaseExpressionWhenClausesItem>(searched.Id).Any(item =>
                       ContainsAggregate(navigator.RequireOwnerLink<SearchedWhenClauseWhenExpressionLink>(
                           item.SearchedWhenClause.Id,
                           "SearchedWhenClause.WhenExpression").BooleanExpression) ||
                       ContainsAggregate(navigator.RequireOwnerLink<WhenClauseThenExpressionLink>(
                           item.SearchedWhenClause.WhenClause.Id,
                           "WhenClause.ThenExpression").ScalarExpression)) ||
                    navigator.TryOwnerLink<CaseExpressionElseExpressionLink>(caseExpression.Id) is { } elseLink &&
                    ContainsAggregate(elseLink.ScalarExpression));
        }

        if (navigator.TrySubtype<CoalesceExpression>(primary.Id) is { } coalesce)
        {
            return navigator.OrderedItems<CoalesceExpressionExpressionsItem>(coalesce.Id)
                .Any(item => ContainsAggregate(item.ScalarExpression));
        }

        if (navigator.TrySubtype<NullIfExpression>(primary.Id) is { } nullIf)
        {
            return ContainsAggregate(navigator.RequireOwnerLink<NullIfExpressionFirstExpressionLink>(
                       nullIf.Id,
                       "NullIfExpression.FirstExpression").ScalarExpression) ||
                   ContainsAggregate(navigator.RequireOwnerLink<NullIfExpressionSecondExpressionLink>(
                       nullIf.Id,
                       "NullIfExpression.SecondExpression").ScalarExpression);
        }

        if (navigator.TrySubtype<IIfCall>(primary.Id) is { } iIf)
        {
            return ContainsAggregate(navigator.RequireOwnerLink<IIfCallPredicateLink>(
                       iIf.Id,
                       "IIfCall.Predicate").BooleanExpression) ||
                   ContainsAggregate(navigator.RequireOwnerLink<IIfCallThenExpressionLink>(
                       iIf.Id,
                       "IIfCall.ThenExpression").ScalarExpression) ||
                   ContainsAggregate(navigator.RequireOwnerLink<IIfCallElseExpressionLink>(
                       iIf.Id,
                       "IIfCall.ElseExpression").ScalarExpression);
        }

        return false;
    }

    private bool ContainsColumnOutsideAggregate(ScalarExpression expression)
    {
        var primary = navigator.TrySubtype<PrimaryExpression>(expression.Id);
        if (primary is null)
        {
            return false;
        }

        if (navigator.TrySubtype<ColumnReferenceExpression>(primary.Id) is not null)
        {
            return true;
        }

        if (navigator.TrySubtype<ParenthesisExpression>(primary.Id) is { } parenthesis)
        {
            return ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<ParenthesisExpressionExpressionLink>(
                parenthesis.Id,
                "ParenthesisExpression.Expression").ScalarExpression);
        }

        if (navigator.TrySubtype<FunctionCall>(primary.Id) is { } function)
        {
            if (AggregateFunctionNames.Contains(FunctionName(function)))
            {
                return false;
            }

            return navigator.OrderedItems<FunctionCallParametersItem>(function.Id)
                .Any(item => ContainsColumnOutsideAggregate(item.ScalarExpression));
        }

        if (navigator.TrySubtype<CaseExpression>(primary.Id) is { } caseExpression)
        {
            var searched = navigator.TrySubtype<SearchedCaseExpression>(caseExpression.Id);
            return searched is not null &&
                   (navigator.OrderedItems<SearchedCaseExpressionWhenClausesItem>(searched.Id).Any(item =>
                       ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<SearchedWhenClauseWhenExpressionLink>(
                           item.SearchedWhenClause.Id,
                           "SearchedWhenClause.WhenExpression").BooleanExpression) ||
                       ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<WhenClauseThenExpressionLink>(
                           item.SearchedWhenClause.WhenClause.Id,
                           "WhenClause.ThenExpression").ScalarExpression)) ||
                    navigator.TryOwnerLink<CaseExpressionElseExpressionLink>(caseExpression.Id) is { } elseLink &&
                    ContainsColumnOutsideAggregate(elseLink.ScalarExpression));
        }

        if (navigator.TrySubtype<CoalesceExpression>(primary.Id) is { } coalesce)
        {
            return navigator.OrderedItems<CoalesceExpressionExpressionsItem>(coalesce.Id)
                .Any(item => ContainsColumnOutsideAggregate(item.ScalarExpression));
        }

        if (navigator.TrySubtype<NullIfExpression>(primary.Id) is { } nullIf)
        {
            return ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<NullIfExpressionFirstExpressionLink>(
                       nullIf.Id,
                       "NullIfExpression.FirstExpression").ScalarExpression) ||
                   ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<NullIfExpressionSecondExpressionLink>(
                       nullIf.Id,
                       "NullIfExpression.SecondExpression").ScalarExpression);
        }

        if (navigator.TrySubtype<IIfCall>(primary.Id) is { } iIf)
        {
            return ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<IIfCallPredicateLink>(
                       iIf.Id,
                       "IIfCall.Predicate").BooleanExpression) ||
                   ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<IIfCallThenExpressionLink>(
                       iIf.Id,
                       "IIfCall.ThenExpression").ScalarExpression) ||
                   ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<IIfCallElseExpressionLink>(
                       iIf.Id,
                       "IIfCall.ElseExpression").ScalarExpression);
        }

        return false;
    }

    private IReadOnlyList<string>? TryGetDirectColumnReferenceParts(ScalarExpression expression)
    {
        var primary = navigator.TrySubtype<PrimaryExpression>(expression.Id);
        var column = primary is null ? null : navigator.TrySubtype<ColumnReferenceExpression>(primary.Id);
        var link = column is null
            ? null
            : navigator.TryOwnerLink<ColumnReferenceExpressionMultiPartIdentifierLink>(column.Id);
        return link is null ? null : navigator.IdentifierParts(link.MultiPartIdentifier);
    }
}
