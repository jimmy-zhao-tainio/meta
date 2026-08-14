namespace MetaWeaveScript.Execution;

internal sealed partial class MetaWeaveScriptExecutionSession
{
    private void ValidateGroupedProjectionStructure(
        IReadOnlyList<RuntimeProjection> projections,
        IReadOnlyList<ScalarExpression> groupingExpressions)
    {
        var groupingSignatures = groupingExpressions
            .Select(CreateScalarExpressionSignature)
            .ToHashSet(StringComparer.Ordinal);
        var directlyGroupedColumns = groupingExpressions
            .Select(TryGetDirectColumnReferenceParts)
            .Where(parts => parts is not null)
            .Select(parts => CreateColumnSignature(parts!))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var projection in projections)
        {
            ValidateGroupedScalarExpression(
                projection.Expression,
                groupingSignatures,
                directlyGroupedColumns);
        }
    }

    private void ValidateGroupedScalarExpression(
        ScalarExpression expression,
        IReadOnlySet<string> groupingSignatures,
        IReadOnlySet<string> directlyGroupedColumns)
    {
        if (groupingSignatures.Contains(CreateScalarExpressionSignature(expression)))
        {
            return;
        }

        var primary = navigator.TrySubtype<PrimaryExpression>(expression.Id)
            ?? throw Fault(
                "ScalarExpressionShapeUnsupported",
                $"ScalarExpression '{expression.Id}' has no retained primary-expression subtype.",
                expression.Id);
        if (navigator.TrySubtype<ColumnReferenceExpression>(primary.Id) is { } column)
        {
            var parts = TryGetDirectColumnReferenceParts(expression);
            if (parts is null || !directlyGroupedColumns.Contains(CreateColumnSignature(parts)))
            {
                throw Fault(
                    "UngroupedColumnReference",
                    $"Grouped projection member '{string.Join(".", parts ?? [])}' is neither grouped nor inside an aggregate.",
                    column.Id);
            }

            return;
        }

        if (navigator.TrySubtype<ParenthesisExpression>(primary.Id) is { } parenthesis)
        {
            ValidateGroupedScalarExpression(
                navigator.RequireOwnerLink<ParenthesisExpressionExpressionLink>(
                    parenthesis.Id,
                    "ParenthesisExpression.Expression").ScalarExpression,
                groupingSignatures,
                directlyGroupedColumns);
            return;
        }

        if (navigator.TrySubtype<FunctionCall>(primary.Id) is { } function)
        {
            if (AggregateFunctionNames.Contains(FunctionName(function)))
            {
                return;
            }

            foreach (var item in navigator.OrderedItems<FunctionCallParametersItem>(function.Id))
            {
                ValidateGroupedScalarExpression(item.ScalarExpression, groupingSignatures, directlyGroupedColumns);
            }

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
                ValidateGroupedBooleanExpression(
                    navigator.RequireOwnerLink<SearchedWhenClauseWhenExpressionLink>(
                        item.SearchedWhenClause.Id,
                        "SearchedWhenClause.WhenExpression").BooleanExpression,
                    groupingSignatures,
                    directlyGroupedColumns);
                ValidateGroupedScalarExpression(
                    navigator.RequireOwnerLink<WhenClauseThenExpressionLink>(
                        item.SearchedWhenClause.WhenClause.Id,
                        "WhenClause.ThenExpression").ScalarExpression,
                    groupingSignatures,
                    directlyGroupedColumns);
            }

            if (navigator.TryOwnerLink<CaseExpressionElseExpressionLink>(caseExpression.Id) is { } elseLink)
            {
                ValidateGroupedScalarExpression(
                    elseLink.ScalarExpression,
                    groupingSignatures,
                    directlyGroupedColumns);
            }

            return;
        }

        if (navigator.TrySubtype<CoalesceExpression>(primary.Id) is { } coalesce)
        {
            foreach (var item in navigator.OrderedItems<CoalesceExpressionExpressionsItem>(coalesce.Id))
            {
                ValidateGroupedScalarExpression(item.ScalarExpression, groupingSignatures, directlyGroupedColumns);
            }

            return;
        }

        if (navigator.TrySubtype<NullIfExpression>(primary.Id) is { } nullIf)
        {
            ValidateGroupedScalarExpression(
                navigator.RequireOwnerLink<NullIfExpressionFirstExpressionLink>(
                    nullIf.Id,
                    "NullIfExpression.FirstExpression").ScalarExpression,
                groupingSignatures,
                directlyGroupedColumns);
            ValidateGroupedScalarExpression(
                navigator.RequireOwnerLink<NullIfExpressionSecondExpressionLink>(
                    nullIf.Id,
                    "NullIfExpression.SecondExpression").ScalarExpression,
                groupingSignatures,
                directlyGroupedColumns);
            return;
        }

        if (navigator.TrySubtype<IIfCall>(primary.Id) is { } iIf)
        {
            ValidateGroupedBooleanExpression(
                navigator.RequireOwnerLink<IIfCallPredicateLink>(
                    iIf.Id,
                    "IIfCall.Predicate").BooleanExpression,
                groupingSignatures,
                directlyGroupedColumns);
            ValidateGroupedScalarExpression(
                navigator.RequireOwnerLink<IIfCallThenExpressionLink>(
                    iIf.Id,
                    "IIfCall.ThenExpression").ScalarExpression,
                groupingSignatures,
                directlyGroupedColumns);
            ValidateGroupedScalarExpression(
                navigator.RequireOwnerLink<IIfCallElseExpressionLink>(
                    iIf.Id,
                    "IIfCall.ElseExpression").ScalarExpression,
                groupingSignatures,
                directlyGroupedColumns);
            return;
        }

        // A correlated subquery resolves its own local and outer scopes during
        // invocation preparation. Its result is also checked for stability
        // across every row in the group before materialization.
        if (navigator.TrySubtype<ScalarSubquery>(primary.Id) is not null ||
            navigator.TrySubtype<ValueExpression>(primary.Id) is not null)
        {
            return;
        }
    }

    private void ValidateGroupedBooleanExpression(
        BooleanExpression expression,
        IReadOnlySet<string> groupingSignatures,
        IReadOnlySet<string> directlyGroupedColumns)
    {
        if (navigator.TrySubtype<BooleanBinaryExpression>(expression.Id) is { } binary)
        {
            ValidateGroupedBooleanExpression(
                navigator.RequireOwnerLink<BooleanBinaryExpressionFirstExpressionLink>(
                    binary.Id,
                    "BooleanBinaryExpression.FirstExpression").BooleanExpression,
                groupingSignatures,
                directlyGroupedColumns);
            ValidateGroupedBooleanExpression(
                navigator.RequireOwnerLink<BooleanBinaryExpressionSecondExpressionLink>(
                    binary.Id,
                    "BooleanBinaryExpression.SecondExpression").BooleanExpression,
                groupingSignatures,
                directlyGroupedColumns);
            return;
        }

        if (navigator.TrySubtype<BooleanParenthesisExpression>(expression.Id) is { } parenthesis)
        {
            ValidateGroupedBooleanExpression(
                navigator.RequireOwnerLink<BooleanParenthesisExpressionExpressionLink>(
                    parenthesis.Id,
                    "BooleanParenthesisExpression.Expression").BooleanExpression,
                groupingSignatures,
                directlyGroupedColumns);
            return;
        }

        if (navigator.TrySubtype<BooleanNotExpression>(expression.Id) is { } not)
        {
            ValidateGroupedBooleanExpression(
                navigator.RequireOwnerLink<BooleanNotExpressionExpressionLink>(
                    not.Id,
                    "BooleanNotExpression.Expression").BooleanExpression,
                groupingSignatures,
                directlyGroupedColumns);
            return;
        }

        var scalars = new List<ScalarExpression>();
        if (navigator.TrySubtype<BooleanComparisonExpression>(expression.Id) is { } comparison)
        {
            scalars.Add(navigator.RequireOwnerLink<BooleanComparisonExpressionFirstExpressionLink>(
                comparison.Id,
                "BooleanComparisonExpression.FirstExpression").ScalarExpression);
            scalars.Add(navigator.RequireOwnerLink<BooleanComparisonExpressionSecondExpressionLink>(
                comparison.Id,
                "BooleanComparisonExpression.SecondExpression").ScalarExpression);
        }
        else if (navigator.TrySubtype<BooleanIsNullExpression>(expression.Id) is { } isNull)
        {
            scalars.Add(navigator.RequireOwnerLink<BooleanIsNullExpressionExpressionLink>(
                isNull.Id,
                "BooleanIsNullExpression.Expression").ScalarExpression);
        }
        else if (navigator.TrySubtype<InPredicate>(expression.Id) is { } inPredicate)
        {
            scalars.Add(navigator.RequireOwnerLink<InPredicateExpressionLink>(
                inPredicate.Id,
                "InPredicate.Expression").ScalarExpression);
            scalars.AddRange(navigator.OrderedItems<InPredicateValuesItem>(inPredicate.Id)
                .Select(item => item.ScalarExpression));
        }
        else if (navigator.TrySubtype<LikePredicate>(expression.Id) is { } like)
        {
            scalars.Add(navigator.RequireOwnerLink<LikePredicateFirstExpressionLink>(
                like.Id,
                "LikePredicate.FirstExpression").ScalarExpression);
            scalars.Add(navigator.RequireOwnerLink<LikePredicateSecondExpressionLink>(
                like.Id,
                "LikePredicate.SecondExpression").ScalarExpression);
        }
        else if (navigator.TrySubtype<ExistsPredicate>(expression.Id) is not null)
        {
            return;
        }

        foreach (var scalar in scalars)
        {
            ValidateGroupedScalarExpression(scalar, groupingSignatures, directlyGroupedColumns);
        }
    }

    private string CreateScalarExpressionSignature(ScalarExpression expression)
    {
        var primary = navigator.TrySubtype<PrimaryExpression>(expression.Id);
        if (primary is null)
        {
            return "missing:" + expression.Id;
        }
        if (navigator.TrySubtype<ColumnReferenceExpression>(primary.Id) is not null)
        {
            return CreateColumnSignature(TryGetDirectColumnReferenceParts(expression) ?? []);
        }

        if (navigator.TrySubtype<ParenthesisExpression>(primary.Id) is { } parenthesis)
        {
            return CreateScalarExpressionSignature(navigator.RequireOwnerLink<ParenthesisExpressionExpressionLink>(
                parenthesis.Id,
                "ParenthesisExpression.Expression").ScalarExpression);
        }

        if (navigator.TrySubtype<FunctionCall>(primary.Id) is { } function)
        {
            return "function:" + FunctionName(function).ToUpperInvariant() + "(" +
                   string.Join(",", navigator.OrderedItems<FunctionCallParametersItem>(function.Id)
                       .Select(item => CreateScalarExpressionSignature(item.ScalarExpression))) + ")";
        }

        if (navigator.TrySubtype<CoalesceExpression>(primary.Id) is { } coalesce)
        {
            return "coalesce(" + string.Join(",", navigator.OrderedItems<CoalesceExpressionExpressionsItem>(coalesce.Id)
                .Select(item => CreateScalarExpressionSignature(item.ScalarExpression))) + ")";
        }

        if (navigator.TrySubtype<NullIfExpression>(primary.Id) is { } nullIf)
        {
            return "nullif(" +
                   CreateScalarExpressionSignature(navigator.RequireOwnerLink<NullIfExpressionFirstExpressionLink>(
                       nullIf.Id,
                       "NullIfExpression.FirstExpression").ScalarExpression) + "," +
                   CreateScalarExpressionSignature(navigator.RequireOwnerLink<NullIfExpressionSecondExpressionLink>(
                       nullIf.Id,
                       "NullIfExpression.SecondExpression").ScalarExpression) + ")";
        }

        if (navigator.TrySubtype<IIfCall>(primary.Id) is { } iIf)
        {
            return "iif(" +
                   CreateBooleanExpressionSignature(navigator.RequireOwnerLink<IIfCallPredicateLink>(
                       iIf.Id,
                       "IIfCall.Predicate").BooleanExpression) + "," +
                   CreateScalarExpressionSignature(navigator.RequireOwnerLink<IIfCallThenExpressionLink>(
                       iIf.Id,
                       "IIfCall.ThenExpression").ScalarExpression) + "," +
                   CreateScalarExpressionSignature(navigator.RequireOwnerLink<IIfCallElseExpressionLink>(
                       iIf.Id,
                       "IIfCall.ElseExpression").ScalarExpression) + ")";
        }

        if (navigator.TrySubtype<CaseExpression>(primary.Id) is { } caseExpression &&
            navigator.TrySubtype<SearchedCaseExpression>(caseExpression.Id) is { } searched)
        {
            var whenSignatures = navigator.OrderedItems<SearchedCaseExpressionWhenClausesItem>(searched.Id)
                .Select(item =>
                    CreateBooleanExpressionSignature(navigator.RequireOwnerLink<SearchedWhenClauseWhenExpressionLink>(
                        item.SearchedWhenClause.Id,
                        "SearchedWhenClause.WhenExpression").BooleanExpression) + ":" +
                    CreateScalarExpressionSignature(navigator.RequireOwnerLink<WhenClauseThenExpressionLink>(
                        item.SearchedWhenClause.WhenClause.Id,
                        "WhenClause.ThenExpression").ScalarExpression));
            var elseLink = navigator.TryOwnerLink<CaseExpressionElseExpressionLink>(caseExpression.Id);
            return "case(" + string.Join(",", whenSignatures) + ";else:" +
                   (elseLink is null ? "null" : CreateScalarExpressionSignature(elseLink.ScalarExpression)) + ")";
        }

        if (navigator.TrySubtype<ValueExpression>(primary.Id) is { } value)
        {
            var literal = navigator.TrySubtype<Literal>(value.Id);
            if (literal is null)
            {
                return "value:" + value.Id;
            }

            var kind = navigator.TrySubtype<StringLiteral>(literal.Id) is not null
                ? "string"
                : navigator.TrySubtype<IntegerLiteral>(literal.Id) is not null
                    ? "integer"
                    : "null";
            var valueSignature = kind == "integer" && long.TryParse(literal.Value, out var integer)
                ? integer.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : (literal.Value ?? string.Empty).ToUpperInvariant();
            return "literal:" + kind + ":" + valueSignature;
        }

        if (navigator.TrySubtype<ScalarSubquery>(primary.Id) is { } subquery)
        {
            return "subquery:" + subquery.Id;
        }

        return "expression:" + primary.Id;
    }

    private static string CreateColumnSignature(IReadOnlyList<string> parts) =>
        "column:" + string.Join(".", parts.Select(part => part.ToUpperInvariant()));

    private string CreateBooleanExpressionSignature(BooleanExpression expression)
    {
        if (navigator.TrySubtype<BooleanBinaryExpression>(expression.Id) is { } binary)
        {
            return "boolean:" + binary.BinaryExpressionType + "(" +
                   CreateBooleanExpressionSignature(navigator.RequireOwnerLink<BooleanBinaryExpressionFirstExpressionLink>(
                       binary.Id,
                       "BooleanBinaryExpression.FirstExpression").BooleanExpression) + "," +
                   CreateBooleanExpressionSignature(navigator.RequireOwnerLink<BooleanBinaryExpressionSecondExpressionLink>(
                       binary.Id,
                       "BooleanBinaryExpression.SecondExpression").BooleanExpression) + ")";
        }

        if (navigator.TrySubtype<BooleanComparisonExpression>(expression.Id) is { } comparison)
        {
            return "comparison:" + comparison.ComparisonType + "(" +
                   CreateScalarExpressionSignature(navigator.RequireOwnerLink<BooleanComparisonExpressionFirstExpressionLink>(
                       comparison.Id,
                       "BooleanComparisonExpression.FirstExpression").ScalarExpression) + "," +
                   CreateScalarExpressionSignature(navigator.RequireOwnerLink<BooleanComparisonExpressionSecondExpressionLink>(
                       comparison.Id,
                       "BooleanComparisonExpression.SecondExpression").ScalarExpression) + ")";
        }

        if (navigator.TrySubtype<BooleanParenthesisExpression>(expression.Id) is { } parenthesis)
        {
            return CreateBooleanExpressionSignature(navigator.RequireOwnerLink<BooleanParenthesisExpressionExpressionLink>(
                parenthesis.Id,
                "BooleanParenthesisExpression.Expression").BooleanExpression);
        }

        if (navigator.TrySubtype<BooleanNotExpression>(expression.Id) is { } not)
        {
            return "not(" + CreateBooleanExpressionSignature(navigator.RequireOwnerLink<BooleanNotExpressionExpressionLink>(
                not.Id,
                "BooleanNotExpression.Expression").BooleanExpression) + ")";
        }

        if (navigator.TrySubtype<BooleanIsNullExpression>(expression.Id) is { } isNull)
        {
            return "isnull:" + isNull.IsNot + "(" +
                   CreateScalarExpressionSignature(navigator.RequireOwnerLink<BooleanIsNullExpressionExpressionLink>(
                       isNull.Id,
                       "BooleanIsNullExpression.Expression").ScalarExpression) + ")";
        }

        if (navigator.TrySubtype<InPredicate>(expression.Id) is { } inPredicate)
        {
            return "in(" + CreateScalarExpressionSignature(navigator.RequireOwnerLink<InPredicateExpressionLink>(
                       inPredicate.Id,
                       "InPredicate.Expression").ScalarExpression) + ";" +
                   string.Join(",", navigator.OrderedItems<InPredicateValuesItem>(inPredicate.Id)
                       .Select(item => CreateScalarExpressionSignature(item.ScalarExpression))) + ")";
        }

        if (navigator.TrySubtype<LikePredicate>(expression.Id) is { } like)
        {
            return "like(" + CreateScalarExpressionSignature(navigator.RequireOwnerLink<LikePredicateFirstExpressionLink>(
                       like.Id,
                       "LikePredicate.FirstExpression").ScalarExpression) + "," +
                   CreateScalarExpressionSignature(navigator.RequireOwnerLink<LikePredicateSecondExpressionLink>(
                       like.Id,
                       "LikePredicate.SecondExpression").ScalarExpression) + ")";
        }

        return "predicate:" + expression.Id;
    }
}
