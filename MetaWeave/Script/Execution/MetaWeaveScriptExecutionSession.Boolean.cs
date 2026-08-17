using System.Text;
using System.Text.RegularExpressions;

namespace MetaWeaveScript.Execution;

internal sealed partial class MetaWeaveScriptExecutionSession
{
    private RuntimeTruth EvaluateBooleanExpression(
        BooleanExpression expression,
        RuntimeEvaluationContext context)
    {
        if (navigator.TrySubtype<BooleanBinaryExpression>(expression.Id) is { } binary)
        {
            var first = EvaluateBooleanExpression(
                navigator.RequireOwnerLink<BooleanBinaryExpressionFirstExpressionLink>(
                    binary.Id,
                    "BooleanBinaryExpression.FirstExpression").BooleanExpression,
                context);
            var second = EvaluateBooleanExpression(
                navigator.RequireOwnerLink<BooleanBinaryExpressionSecondExpressionLink>(
                    binary.Id,
                    "BooleanBinaryExpression.SecondExpression").BooleanExpression,
                context);
            return binary.BinaryExpressionType switch
            {
                "And" => And(first, second),
                "Or" => Or(first, second),
                _ => throw Fault(
                    "BooleanBinaryOperatorUnsupported",
                    $"Boolean operator '{binary.BinaryExpressionType}' is outside the retained surface.",
                    binary.Id)
            };
        }

        if (navigator.TrySubtype<BooleanComparisonExpression>(expression.Id) is { } comparison)
        {
            var first = EvaluateScalarExpression(
                navigator.RequireOwnerLink<BooleanComparisonExpressionFirstExpressionLink>(
                    comparison.Id,
                    "BooleanComparisonExpression.FirstExpression").ScalarExpression,
                context);
            var second = EvaluateScalarExpression(
                navigator.RequireOwnerLink<BooleanComparisonExpressionSecondExpressionLink>(
                    comparison.Id,
                    "BooleanComparisonExpression.SecondExpression").ScalarExpression,
                context);
            if (first.IsNull || second.IsNull)
            {
                return RuntimeTruth.Unknown;
            }

            var compared = CompareValues(first, second);
            return comparison.ComparisonType switch
            {
                "Equals" => FromBoolean(compared == 0),
                "NotEqualToBrackets" => FromBoolean(compared != 0),
                "GreaterThan" => FromBoolean(compared > 0),
                "GreaterThanOrEqualTo" => FromBoolean(compared >= 0),
                "LessThan" => FromBoolean(compared < 0),
                "LessThanOrEqualTo" => FromBoolean(compared <= 0),
                _ => throw Fault(
                    "ComparisonOperatorUnsupported",
                    $"Comparison operator '{comparison.ComparisonType}' is outside the retained surface.",
                    comparison.Id)
            };
        }

        if (navigator.TrySubtype<BooleanParenthesisExpression>(expression.Id) is { } parenthesis)
        {
            return EvaluateBooleanExpression(
                navigator.RequireOwnerLink<BooleanParenthesisExpressionExpressionLink>(
                    parenthesis.Id,
                    "BooleanParenthesisExpression.Expression").BooleanExpression,
                context);
        }

        if (navigator.TrySubtype<BooleanNotExpression>(expression.Id) is { } not)
        {
            return Not(EvaluateBooleanExpression(
                navigator.RequireOwnerLink<BooleanNotExpressionExpressionLink>(
                    not.Id,
                    "BooleanNotExpression.Expression").BooleanExpression,
                context));
        }

        if (navigator.TrySubtype<BooleanIsNullExpression>(expression.Id) is { } isNull)
        {
            var value = EvaluateScalarExpression(
                navigator.RequireOwnerLink<BooleanIsNullExpressionExpressionLink>(
                    isNull.Id,
                    "BooleanIsNullExpression.Expression").ScalarExpression,
                context);
            return FromBoolean(IsTrue(isNull.IsNot) ? !value.IsNull : value.IsNull);
        }

        if (navigator.TrySubtype<InPredicate>(expression.Id) is { } inPredicate)
        {
            var tested = EvaluateScalarExpression(
                navigator.RequireOwnerLink<InPredicateExpressionLink>(
                    inPredicate.Id,
                    "InPredicate.Expression").ScalarExpression,
                context);
            if (tested.IsNull)
            {
                return RuntimeTruth.Unknown;
            }

            var sawNull = false;
            foreach (var item in navigator.OrderedItems<InPredicateValuesItem>(inPredicate.Id))
            {
                var candidate = EvaluateScalarExpression(item.ScalarExpression, context);
                if (candidate.IsNull)
                {
                    sawNull = true;
                }
                else if (EqualValues(tested, candidate) == RuntimeTruth.True)
                {
                    return RuntimeTruth.True;
                }
            }

            return sawNull ? RuntimeTruth.Unknown : RuntimeTruth.False;
        }

        if (navigator.TrySubtype<LikePredicate>(expression.Id) is { } like)
        {
            var value = EvaluateScalarExpression(
                navigator.RequireOwnerLink<LikePredicateFirstExpressionLink>(
                    like.Id,
                    "LikePredicate.FirstExpression").ScalarExpression,
                context);
            var pattern = EvaluateScalarExpression(
                navigator.RequireOwnerLink<LikePredicateSecondExpressionLink>(
                    like.Id,
                    "LikePredicate.SecondExpression").ScalarExpression,
                context);
            if (value.IsNull || pattern.IsNull)
            {
                return RuntimeTruth.Unknown;
            }

            if (value.Kind != MetaWeaveScriptValueKind.String || pattern.Kind != MetaWeaveScriptValueKind.String)
            {
                throw Fault(
                    "LikeOperandInvalid",
                    "LIKE requires string operands.",
                    like.Id);
            }

            return FromBoolean(Like(value.StringValue!, pattern.StringValue!));
        }

        if (navigator.TrySubtype<ExistsPredicate>(expression.Id) is { } exists)
        {
            var subquery = navigator.RequireOwnerLink<ExistsPredicateSubqueryLink>(
                exists.Id,
                "ExistsPredicate.Subquery").ScalarSubquery;
            var query = navigator.RequireOwnerLink<ScalarSubqueryQueryExpressionLink>(
                subquery.Id,
                "ScalarSubquery.QueryExpression").QueryExpression;
            if (TryEvaluateIndexedExists(query, context, out var indexedResult))
            {
                return FromBoolean(indexedResult);
            }

            var result = ExecuteQueryExpression(
                query,
                context.VisibleCommonTableExpressionOrdinal,
                context.Frame);
            return FromBoolean(result.Rows.Count > 0);
        }

        throw Fault(
            "BooleanExpressionShapeUnsupported",
            $"BooleanExpression '{expression.Id}' has no retained semantic subtype.",
            expression.Id);
    }

    private bool TryEvaluateIndexedExists(
        QueryExpression query,
        RuntimeEvaluationContext context,
        out bool result)
    {
        result = false;
        var specification = navigator.TrySubtype<QuerySpecification>(query.Id);
        if (specification is null ||
            navigator.TryOwnerLink<QuerySpecificationGroupByClauseLink>(specification.Id) is not null)
        {
            return false;
        }

        var selectItems = navigator.OrderedItems<QuerySpecificationSelectElementsItem>(specification.Id);
        if (selectItems.Any(item => ContainsAggregate(CreateProjection(item.SelectElement).Expression)))
        {
            return false;
        }

        var fromLink = navigator.TryOwnerLink<QuerySpecificationFromClauseLink>(specification.Id);
        var fromItems = fromLink is null
            ? []
            : navigator.OrderedItems<FromClauseTableReferencesItem>(fromLink.FromClause.Id);
        if (fromItems.Count != 1)
        {
            return false;
        }

        var tableReference = fromItems[0].TableReference;
        var aliasBase = navigator.TrySubtype<TableReferenceWithAlias>(tableReference.Id);
        var named = aliasBase is null
            ? null
            : navigator.TrySubtype<NamedTableReference>(aliasBase.Id);
        if (named is null || navigator.TrySubtype<TableReferenceWithAliasAndColumns>(aliasBase!.Id) is not null)
        {
            return false;
        }

        var schemaObject = navigator.RequireOwnerLink<NamedTableReferenceSchemaObjectLink>(
            named.Id,
            "NamedTableReference.SchemaObject").SchemaObjectName;
        var identifier = navigator.RequireById<MultiPartIdentifier>(
            schemaObject.MultiPartIdentifier.Id,
            "SchemaObjectName.MultiPartIdentifier");
        var sourceParts = navigator.IdentifierParts(identifier);
        if (sourceParts.Count != 2)
        {
            return false;
        }

        var localAlias = TryGetTableAlias(tableReference) ?? sourceParts[^1];
        var whereLink = navigator.TryOwnerLink<QuerySpecificationWhereClauseLink>(specification.Id);
        var whereExpression = whereLink is null
            ? null
            : navigator.TryOwnerLink<WhereClauseSearchConditionLink>(whereLink.WhereClause.Id)?.BooleanExpression;
        var comparison = whereExpression is null
            ? null
            : navigator.TrySubtype<BooleanComparisonExpression>(whereExpression.Id);
        if (comparison is null ||
            !string.Equals(comparison.ComparisonType, "Equals", StringComparison.Ordinal))
        {
            return false;
        }

        var first = navigator.RequireOwnerLink<BooleanComparisonExpressionFirstExpressionLink>(
            comparison.Id,
            "BooleanComparisonExpression.FirstExpression").ScalarExpression;
        var second = navigator.RequireOwnerLink<BooleanComparisonExpressionSecondExpressionLink>(
            comparison.Id,
            "BooleanComparisonExpression.SecondExpression").ScalarExpression;
        var firstParts = TryGetDirectColumnReferenceParts(first);
        var secondParts = TryGetDirectColumnReferenceParts(second);
        if (firstParts is null || secondParts is null)
        {
            return false;
        }

        ScalarExpression outerExpression;
        string localColumn;
        if (firstParts.Count == 2 &&
            string.Equals(firstParts[0], localAlias, StringComparison.OrdinalIgnoreCase) &&
            !(secondParts.Count == 2 &&
              string.Equals(secondParts[0], localAlias, StringComparison.OrdinalIgnoreCase)))
        {
            localColumn = firstParts[1];
            outerExpression = second;
        }
        else if (secondParts.Count == 2 &&
                 string.Equals(secondParts[0], localAlias, StringComparison.OrdinalIgnoreCase) &&
                 !(firstParts.Count == 2 &&
                   string.Equals(firstParts[0], localAlias, StringComparison.OrdinalIgnoreCase)))
        {
            localColumn = secondParts[1];
            outerExpression = first;
        }
        else
        {
            return false;
        }

        var outerValue = EvaluateScalarExpression(
            outerExpression,
            context with
            {
                Frame = new RuntimeFrame(new RuntimeLocalRow(), context.Frame)
            });
        result = sourceTables.ContainsValue(
            sourceParts,
            sourceParts[^1],
            localColumn,
            outerValue,
            comparison.Id);
        return true;
    }

    private bool ContainsAggregate(BooleanExpression expression)
    {
        if (navigator.TrySubtype<BooleanBinaryExpression>(expression.Id) is { } binary)
        {
            return ContainsAggregate(navigator.RequireOwnerLink<BooleanBinaryExpressionFirstExpressionLink>(
                       binary.Id,
                       "BooleanBinaryExpression.FirstExpression").BooleanExpression) ||
                   ContainsAggregate(navigator.RequireOwnerLink<BooleanBinaryExpressionSecondExpressionLink>(
                       binary.Id,
                       "BooleanBinaryExpression.SecondExpression").BooleanExpression);
        }

        if (navigator.TrySubtype<BooleanComparisonExpression>(expression.Id) is { } comparison)
        {
            return ContainsAggregate(navigator.RequireOwnerLink<BooleanComparisonExpressionFirstExpressionLink>(
                       comparison.Id,
                       "BooleanComparisonExpression.FirstExpression").ScalarExpression) ||
                   ContainsAggregate(navigator.RequireOwnerLink<BooleanComparisonExpressionSecondExpressionLink>(
                       comparison.Id,
                       "BooleanComparisonExpression.SecondExpression").ScalarExpression);
        }

        if (navigator.TrySubtype<BooleanParenthesisExpression>(expression.Id) is { } parenthesis)
        {
            return ContainsAggregate(navigator.RequireOwnerLink<BooleanParenthesisExpressionExpressionLink>(
                parenthesis.Id,
                "BooleanParenthesisExpression.Expression").BooleanExpression);
        }

        if (navigator.TrySubtype<BooleanNotExpression>(expression.Id) is { } not)
        {
            return ContainsAggregate(navigator.RequireOwnerLink<BooleanNotExpressionExpressionLink>(
                not.Id,
                "BooleanNotExpression.Expression").BooleanExpression);
        }

        if (navigator.TrySubtype<BooleanIsNullExpression>(expression.Id) is { } isNull)
        {
            return ContainsAggregate(navigator.RequireOwnerLink<BooleanIsNullExpressionExpressionLink>(
                isNull.Id,
                "BooleanIsNullExpression.Expression").ScalarExpression);
        }

        if (navigator.TrySubtype<InPredicate>(expression.Id) is { } inPredicate)
        {
            return ContainsAggregate(navigator.RequireOwnerLink<InPredicateExpressionLink>(
                       inPredicate.Id,
                       "InPredicate.Expression").ScalarExpression) ||
                   navigator.OrderedItems<InPredicateValuesItem>(inPredicate.Id)
                       .Any(item => ContainsAggregate(item.ScalarExpression));
        }

        if (navigator.TrySubtype<LikePredicate>(expression.Id) is { } like)
        {
            return ContainsAggregate(navigator.RequireOwnerLink<LikePredicateFirstExpressionLink>(
                       like.Id,
                       "LikePredicate.FirstExpression").ScalarExpression) ||
                   ContainsAggregate(navigator.RequireOwnerLink<LikePredicateSecondExpressionLink>(
                       like.Id,
                       "LikePredicate.SecondExpression").ScalarExpression);
        }

        return false;
    }

    private bool ContainsColumnOutsideAggregate(BooleanExpression expression)
    {
        if (navigator.TrySubtype<BooleanBinaryExpression>(expression.Id) is { } binary)
        {
            return ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<BooleanBinaryExpressionFirstExpressionLink>(
                       binary.Id,
                       "BooleanBinaryExpression.FirstExpression").BooleanExpression) ||
                   ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<BooleanBinaryExpressionSecondExpressionLink>(
                       binary.Id,
                       "BooleanBinaryExpression.SecondExpression").BooleanExpression);
        }

        if (navigator.TrySubtype<BooleanComparisonExpression>(expression.Id) is { } comparison)
        {
            return ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<BooleanComparisonExpressionFirstExpressionLink>(
                       comparison.Id,
                       "BooleanComparisonExpression.FirstExpression").ScalarExpression) ||
                   ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<BooleanComparisonExpressionSecondExpressionLink>(
                       comparison.Id,
                       "BooleanComparisonExpression.SecondExpression").ScalarExpression);
        }

        if (navigator.TrySubtype<BooleanParenthesisExpression>(expression.Id) is { } parenthesis)
        {
            return ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<BooleanParenthesisExpressionExpressionLink>(
                parenthesis.Id,
                "BooleanParenthesisExpression.Expression").BooleanExpression);
        }

        if (navigator.TrySubtype<BooleanNotExpression>(expression.Id) is { } not)
        {
            return ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<BooleanNotExpressionExpressionLink>(
                not.Id,
                "BooleanNotExpression.Expression").BooleanExpression);
        }

        if (navigator.TrySubtype<BooleanIsNullExpression>(expression.Id) is { } isNull)
        {
            return ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<BooleanIsNullExpressionExpressionLink>(
                isNull.Id,
                "BooleanIsNullExpression.Expression").ScalarExpression);
        }

        if (navigator.TrySubtype<InPredicate>(expression.Id) is { } inPredicate)
        {
            return ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<InPredicateExpressionLink>(
                       inPredicate.Id,
                       "InPredicate.Expression").ScalarExpression) ||
                   navigator.OrderedItems<InPredicateValuesItem>(inPredicate.Id)
                       .Any(item => ContainsColumnOutsideAggregate(item.ScalarExpression));
        }

        if (navigator.TrySubtype<LikePredicate>(expression.Id) is { } like)
        {
            return ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<LikePredicateFirstExpressionLink>(
                       like.Id,
                       "LikePredicate.FirstExpression").ScalarExpression) ||
                   ContainsColumnOutsideAggregate(navigator.RequireOwnerLink<LikePredicateSecondExpressionLink>(
                       like.Id,
                       "LikePredicate.SecondExpression").ScalarExpression);
        }

        return false;
    }

    private static RuntimeTruth EqualValues(MetaWeaveScriptValue first, MetaWeaveScriptValue second)
    {
        if (first.IsNull || second.IsNull)
        {
            return RuntimeTruth.Unknown;
        }

        if (first.Kind != second.Kind)
        {
            throw Fault(
                "ValueKindMismatch",
                $"Cannot compare {first.Kind} with {second.Kind}; WeaveScript performs no implicit type conversion.");
        }

        return FromBoolean(MetaWeaveScriptValueEqualityComparer.Instance.Equals(first, second));
    }

    private static RuntimeTruth And(RuntimeTruth first, RuntimeTruth second)
    {
        if (first == RuntimeTruth.False || second == RuntimeTruth.False)
        {
            return RuntimeTruth.False;
        }

        return first == RuntimeTruth.True && second == RuntimeTruth.True
            ? RuntimeTruth.True
            : RuntimeTruth.Unknown;
    }

    private static RuntimeTruth Or(RuntimeTruth first, RuntimeTruth second)
    {
        if (first == RuntimeTruth.True || second == RuntimeTruth.True)
        {
            return RuntimeTruth.True;
        }

        return first == RuntimeTruth.False && second == RuntimeTruth.False
            ? RuntimeTruth.False
            : RuntimeTruth.Unknown;
    }

    private static RuntimeTruth Not(RuntimeTruth value) => value switch
    {
        RuntimeTruth.True => RuntimeTruth.False,
        RuntimeTruth.False => RuntimeTruth.True,
        _ => RuntimeTruth.Unknown
    };

    private static RuntimeTruth FromBoolean(bool value) =>
        value ? RuntimeTruth.True : RuntimeTruth.False;

    private static bool Like(string value, string pattern)
    {
        var expression = new StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            if (character == '%')
            {
                expression.Append(".*");
            }
            else if (character == '_')
            {
                expression.Append('.');
            }
            else if (character == '[')
            {
                var close = pattern.IndexOf(']', index + 1);
                if (close < 0)
                {
                    expression.Append("\\[");
                    continue;
                }

                var content = pattern[(index + 1)..close];
                expression.Append('[');
                if (content.StartsWith('^'))
                {
                    expression.Append('^');
                    content = content[1..];
                }

                expression.Append(content.Replace("\\", "\\\\", StringComparison.Ordinal));
                expression.Append(']');
                index = close;
            }
            else
            {
                expression.Append(Regex.Escape(character.ToString()));
            }
        }

        expression.Append('$');
        try
        {
            return Regex.IsMatch(
                value,
                expression.ToString(),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline,
                TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException exception)
        {
            throw Fault(
                "LikePatternInvalid",
                $"LIKE pattern '{pattern}' is invalid: {exception.Message}");
        }
    }
}
