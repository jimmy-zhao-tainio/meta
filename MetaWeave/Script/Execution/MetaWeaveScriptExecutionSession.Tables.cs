using System.Globalization;
using Meta.Operations.Domain;

namespace MetaWeaveScript.Execution;

internal sealed partial class MetaWeaveScriptExecutionSession
{
    private RuntimeTableResult ExecuteFromClause(
        FromClause fromClause,
        int visibleCommonTableExpressionOrdinal,
        RuntimeFrame? outerFrame)
    {
        var result = new RuntimeTableResult([], [new RuntimeLocalRow()]);
        foreach (var item in navigator.OrderedItems<FromClauseTableReferencesItem>(fromClause.Id))
        {
            var next = ExecuteTableReference(
                item.TableReference,
                visibleCommonTableExpressionOrdinal,
                outerFrame);
            result = CrossProduct(result, next);
        }

        return result;
    }

    private RuntimeTableResult ExecuteTableReference(
        TableReference tableReference,
        int visibleCommonTableExpressionOrdinal,
        RuntimeFrame? outerFrame)
    {
        var aliasBase = navigator.TrySubtype<TableReferenceWithAlias>(tableReference.Id);
        var aliasAndColumnsBase = aliasBase is null
            ? null
            : navigator.TrySubtype<TableReferenceWithAliasAndColumns>(aliasBase.Id);

        var named = aliasBase is null
            ? null
            : navigator.TrySubtype<NamedTableReference>(aliasBase.Id);
        if (named is not null)
        {
            if (aliasAndColumnsBase is not null)
            {
                throw Fault(
                    "NamedSourceColumnAliasesUnsupported",
                    "Named workspace sources do not support table column-alias lists.",
                    named.Id);
            }

            return ExecuteNamedTableReference(
                tableReference,
                named,
                visibleCommonTableExpressionOrdinal);
        }

        var function = aliasBase is null
            ? null
            : navigator.TrySubtype<GlobalFunctionTableReference>(aliasBase.Id);
        if (function is not null)
        {
            if (aliasAndColumnsBase is not null)
            {
                throw Fault(
                    "TableFunctionColumnAliasesUnsupported",
                    "STRING_SPLIT does not support table column-alias lists.",
                    function.Id);
            }

            return ExecuteStringSplit(
                tableReference,
                function,
                visibleCommonTableExpressionOrdinal,
                outerFrame);
        }

        var queryDerived = aliasAndColumnsBase is null
            ? null
            : navigator.TrySubtype<QueryDerivedTable>(aliasAndColumnsBase.Id);
        if (queryDerived is not null)
        {
            return ExecuteQueryDerivedTable(
                tableReference,
                queryDerived,
                visibleCommonTableExpressionOrdinal,
                outerFrame);
        }

        var inlineDerived = aliasAndColumnsBase is null
            ? null
            : navigator.TrySubtype<InlineDerivedTable>(aliasAndColumnsBase.Id);
        if (inlineDerived is not null)
        {
            return ExecuteInlineDerivedTable(
                tableReference,
                inlineDerived,
                visibleCommonTableExpressionOrdinal,
                outerFrame);
        }

        var join = navigator.TrySubtype<JoinTableReference>(tableReference.Id);
        if (join is not null)
        {
            return ExecuteJoin(
                join,
                visibleCommonTableExpressionOrdinal,
                outerFrame);
        }

        throw Fault(
            "TableReferenceShapeUnsupported",
            $"TableReference '{tableReference.Id}' has no retained semantic subtype.",
            tableReference.Id);
    }

    private RuntimeTableResult ExecuteNamedTableReference(
        TableReference tableReference,
        NamedTableReference named,
        int visibleCommonTableExpressionOrdinal)
    {
        var schemaObject = navigator.RequireOwnerLink<NamedTableReferenceSchemaObjectLink>(
            named.Id,
            "NamedTableReference.SchemaObject").SchemaObjectName;
        var identifier = navigator.RequireById<MultiPartIdentifier>(
            schemaObject.MultiPartIdentifier.Id,
            "SchemaObjectName.MultiPartIdentifier");
        var parts = navigator.IdentifierParts(identifier);
        if (parts.Count != 1)
        {
            throw Fault(
                "SourceEntityNameShapeInvalid",
                $"Named source '{string.Join(".", parts)}' is not a one-part workspace entity name.",
                named.Id);
        }

        var baseIdentifier = navigator.RequireOwnerLink<SchemaObjectNameBaseIdentifierLink>(
            schemaObject.Id,
            "SchemaObjectName.BaseIdentifier").Identifier;
        var baseIdentifierValue = navigator.RequireIdentifier(
            baseIdentifier,
            "SchemaObjectName.BaseIdentifier");
        if (!string.Equals(baseIdentifierValue, parts[0], StringComparison.Ordinal) ||
            !string.Equals(baseIdentifier.Id, navigator.OrderedItems<MultiPartIdentifierIdentifiersItem>(identifier.Id)[0].Identifier.Id, StringComparison.Ordinal))
        {
            throw Fault(
                "SchemaObjectNameBaseIdentifierMismatch",
                $"SchemaObjectName '{schemaObject.Id}' does not preserve one consistent base identifier.",
                schemaObject.Id);
        }

        var name = parts[0];
        var exposedName = TryGetTableAlias(tableReference) ?? name;
        if (cteDefinitions.TryGetValue(name, out var definition))
        {
            if (definition.Ordinal >= visibleCommonTableExpressionOrdinal)
            {
                throw Fault(
                    "CommonTableExpressionForwardReference",
                    $"Common table expression '{name}' is not visible at this use; only earlier declarations may be referenced.",
                    named.Id);
            }

            return ExposeRowset(
                ExecuteCommonTableExpression(definition),
                exposedName,
                $"Common table expression '{definition.Name}'",
                definition.Id);
        }

        var entity = sourceWorkspace.Model.FindEntity(name)
            ?? throw Fault(
                "SourceEntityNotFound",
                $"Source workspace entity '{name}' was not found.",
                named.Id);
        var columns = new List<RuntimeColumn> { new("Id") };
        columns.AddRange(entity.Properties.Select(property => new RuntimeColumn(property.Name)));
        columns.AddRange(entity.Relationships.Select(relationship => new RuntimeColumn(relationship.GetColumnName())));
        var shape = new RuntimeSourceShape(exposedName, columns);
        var sourceRecords = sourceWorkspace.Instance.RecordsByEntity.TryGetValue(entity.Name, out var records)
            ? records
            : [];
        var rows = sourceRecords
            .OrderBy(record => record.Id, MetaIdentity.Comparer)
            .Select(record =>
            {
                var values = new List<MetaWeaveScriptValue>
                {
                    MetaWeaveScriptValue.FromString(record.Id)
                };
                values.AddRange(entity.Properties.Select(property =>
                    record.Values.TryGetValue(property.Name, out var value) && value is not null
                        ? MetaWeaveScriptValue.FromString(value)
                        : MetaWeaveScriptValue.Null));
                values.AddRange(entity.Relationships.Select(relationship =>
                    record.RelationshipIds.TryGetValue(relationship.GetColumnName(), out var value) && value is not null
                        ? MetaWeaveScriptValue.FromString(value)
                        : MetaWeaveScriptValue.Null));
                return RuntimeLocalRow.From(shape, new RuntimeRow(values.ToArray()));
            })
            .ToArray();
        return new RuntimeTableResult([shape], rows);
    }

    private RuntimeTableResult ExecuteQueryDerivedTable(
        TableReference tableReference,
        QueryDerivedTable queryDerived,
        int visibleCommonTableExpressionOrdinal,
        RuntimeFrame? outerFrame)
    {
        var alias = TryGetTableAlias(tableReference)
            ?? throw Fault(
                "DerivedTableAliasMissing",
                $"QueryDerivedTable '{queryDerived.Id}' requires an alias.",
                queryDerived.Id);
        var query = navigator.RequireOwnerLink<QueryDerivedTableQueryExpressionLink>(
            queryDerived.Id,
            "QueryDerivedTable.QueryExpression").QueryExpression;
        PrepareQueryExpression(query, visibleCommonTableExpressionOrdinal, outerFrame);
        var rowset = ExecuteQueryExpression(query, visibleCommonTableExpressionOrdinal, outerFrame);
        rowset = ApplyDerivedColumnAliases(tableReference, rowset, "Query-derived table", queryDerived.Id);
        return ExposeRowset(rowset, alias, $"Query-derived table '{alias}'", queryDerived.Id);
    }

    private RuntimeTableResult ExecuteInlineDerivedTable(
        TableReference tableReference,
        InlineDerivedTable inlineDerived,
        int visibleCommonTableExpressionOrdinal,
        RuntimeFrame? outerFrame)
    {
        var alias = TryGetTableAlias(tableReference)
            ?? throw Fault(
                "InlineValuesAliasMissing",
                $"InlineDerivedTable '{inlineDerived.Id}' requires an alias.",
                inlineDerived.Id);
        var aliasInfo = TryGetAliasAndColumns(tableReference)
            ?? throw Fault(
                "InlineValuesColumnAliasesMissing",
                $"Inline VALUES table '{alias}' requires a complete column-alias list.",
                inlineDerived.Id);
        if (aliasInfo.Columns.Count == 0)
        {
            throw Fault(
                "InlineValuesColumnAliasesMissing",
                $"Inline VALUES table '{alias}' requires a complete column-alias list.",
                inlineDerived.Id);
        }

        var evaluationFrame = outerFrame ?? new RuntimeFrame(new RuntimeLocalRow());
        var context = new RuntimeEvaluationContext(
            evaluationFrame,
            visibleCommonTableExpressionOrdinal);
        var rows = new List<RuntimeRow>();
        foreach (var item in navigator.OrderedItems<InlineDerivedTableRowValuesItem>(inlineDerived.Id))
        {
            var values = navigator.OrderedItems<RowValueColumnValuesItem>(item.RowValue.Id)
                .Select(value => EvaluateScalarExpression(value.ScalarExpression, context))
                .ToArray();
            if (values.Length != aliasInfo.Columns.Count)
            {
                throw Fault(
                    "InlineValuesColumnCountMismatch",
                    $"Inline VALUES table '{alias}' declares {aliasInfo.Columns.Count} columns but a row contains {values.Length} values.",
                    item.RowValue.Id);
            }

            rows.Add(new RuntimeRow(values));
        }

        return ExposeRowset(
            new RuntimeRowset(aliasInfo.Columns.Select(name => new RuntimeColumn(name)).ToArray(), rows),
            alias,
            $"Inline VALUES table '{alias}'",
            inlineDerived.Id);
    }

    private RuntimeTableResult ExecuteStringSplit(
        TableReference tableReference,
        GlobalFunctionTableReference function,
        int visibleCommonTableExpressionOrdinal,
        RuntimeFrame? outerFrame)
    {
        var name = navigator.RequireIdentifier(
            navigator.RequireOwnerLink<GlobalFunctionTableReferenceNameLink>(
                function.Id,
                "GlobalFunctionTableReference.Name").Identifier,
            "GlobalFunctionTableReference.Name");
        if (!string.Equals(name, "STRING_SPLIT", StringComparison.OrdinalIgnoreCase))
        {
            throw Fault(
                "TableFunctionUnsupported",
                $"Table function '{name}' is outside the WeaveScript table-function catalog.",
                function.Id);
        }

        var parameters = navigator.OrderedItems<GlobalFunctionTableReferenceParametersItem>(function.Id);
        if (parameters.Count is < 2 or > 3)
        {
            throw Fault(
                "StringSplitArgumentCountInvalid",
                $"STRING_SPLIT requires two or three arguments, but received {parameters.Count}.",
                function.Id);
        }

        var frame = outerFrame ?? new RuntimeFrame(new RuntimeLocalRow());
        var context = new RuntimeEvaluationContext(frame, visibleCommonTableExpressionOrdinal);
        var input = EvaluateScalarExpression(parameters[0].ScalarExpression, context);
        var separatorValue = EvaluateScalarExpression(parameters[1].ScalarExpression, context);
        if (separatorValue.IsNull)
        {
            throw Fault("StringSplitSeparatorNull", "STRING_SPLIT separator cannot be NULL.", function.Id);
        }

        if (separatorValue.Kind != MetaWeaveScriptValueKind.String || separatorValue.StringValue!.Length != 1)
        {
            throw Fault(
                "StringSplitSeparatorInvalid",
                "STRING_SPLIT separator must be a one-character string.",
                function.Id);
        }

        var includeOrdinal = false;
        if (parameters.Count == 3)
        {
            var enableOrdinal = TryGetIntegerLiteralValue(parameters[2].ScalarExpression);
            if (enableOrdinal is not 0 and not 1)
            {
                throw Fault(
                    "StringSplitOrdinalFlagInvalid",
                    "STRING_SPLIT third argument must be the integer literal 0 or 1.",
                    parameters[2].ScalarExpression.Id);
            }

            includeOrdinal = enableOrdinal == 1;
        }

        var columns = includeOrdinal
            ? new[] { new RuntimeColumn("value"), new RuntimeColumn("ordinal") }
            : [new RuntimeColumn("value")];
        var rows = new List<RuntimeRow>();
        if (!input.IsNull)
        {
            if (input.Kind != MetaWeaveScriptValueKind.String)
            {
                throw Fault(
                    "StringSplitInputInvalid",
                    $"STRING_SPLIT input must be a string or NULL, but received {input.Kind}.",
                    function.Id);
            }

            var parts = input.StringValue!.Split(separatorValue.StringValue![0]);
            for (var index = 0; index < parts.Length; index++)
            {
                rows.Add(new RuntimeRow(includeOrdinal
                    ? [MetaWeaveScriptValue.FromString(parts[index]), MetaWeaveScriptValue.FromInteger(index + 1)]
                    : [MetaWeaveScriptValue.FromString(parts[index])]));
            }
        }

        var alias = TryGetTableAlias(tableReference) ?? "STRING_SPLIT";
        return ExposeRowset(
            new RuntimeRowset(columns, rows),
            alias,
            $"STRING_SPLIT source '{alias}'",
            function.Id);
    }

    private RuntimeTableResult ExecuteJoin(
        JoinTableReference join,
        int visibleCommonTableExpressionOrdinal,
        RuntimeFrame? outerFrame)
    {
        var firstReference = navigator.RequireOwnerLink<JoinTableReferenceFirstTableReferenceLink>(
            join.Id,
            "JoinTableReference.FirstTableReference").TableReference;
        var secondReference = navigator.RequireOwnerLink<JoinTableReferenceSecondTableReferenceLink>(
            join.Id,
            "JoinTableReference.SecondTableReference").TableReference;
        var first = ExecuteTableReference(firstReference, visibleCommonTableExpressionOrdinal, outerFrame);

        if (navigator.TrySubtype<QualifiedJoin>(join.Id) is { } qualified)
        {
            var second = ExecuteTableReference(secondReference, visibleCommonTableExpressionOrdinal, outerFrame);
            var sources = CombineSourceShapes(first.Sources, second.Sources);
            var predicate = navigator.RequireOwnerLink<QualifiedJoinSearchConditionLink>(
                qualified.Id,
                "QualifiedJoin.SearchCondition").BooleanExpression;
            PrepareBooleanExpression(
                predicate,
                new RuntimeFrame(
                    CreateNullLocalRow(first.Sources).Combine(CreateNullLocalRow(second.Sources)),
                    outerFrame),
                visibleCommonTableExpressionOrdinal,
                allowAggregate: false,
                withinAggregate: false);
            var rows = new List<RuntimeLocalRow>();
            foreach (var left in first.Rows)
            {
                var matched = false;
                foreach (var right in second.Rows)
                {
                    var combined = left.Combine(right);
                    var truth = EvaluateBooleanExpression(
                        predicate,
                        new RuntimeEvaluationContext(
                            new RuntimeFrame(combined, outerFrame),
                            visibleCommonTableExpressionOrdinal));
                    if (truth == RuntimeTruth.True)
                    {
                        matched = true;
                        rows.Add(combined);
                    }
                }

                if (!matched && string.Equals(qualified.QualifiedJoinType, "LeftOuter", StringComparison.Ordinal))
                {
                    rows.Add(left.Combine(CreateNullLocalRow(second.Sources)));
                }
            }

            if (!string.Equals(qualified.QualifiedJoinType, "Inner", StringComparison.Ordinal) &&
                !string.Equals(qualified.QualifiedJoinType, "LeftOuter", StringComparison.Ordinal))
            {
                throw Fault(
                    "QualifiedJoinTypeUnsupported",
                    $"Qualified join type '{qualified.QualifiedJoinType}' is outside the retained surface.",
                    qualified.Id);
            }

            return new RuntimeTableResult(sources, rows);
        }

        var unqualified = navigator.TrySubtype<UnqualifiedJoin>(join.Id)
            ?? throw Fault(
                "JoinShapeUnsupported",
                $"JoinTableReference '{join.Id}' has no retained join subtype.",
                join.Id);
        if (string.Equals(unqualified.UnqualifiedJoinType, "CrossJoin", StringComparison.Ordinal))
        {
            var second = ExecuteTableReference(secondReference, visibleCommonTableExpressionOrdinal, outerFrame);
            return CrossProduct(first, second);
        }

        var isOuterApply = string.Equals(unqualified.UnqualifiedJoinType, "OuterApply", StringComparison.Ordinal);
        if (!isOuterApply &&
            !string.Equals(unqualified.UnqualifiedJoinType, "CrossApply", StringComparison.Ordinal))
        {
            throw Fault(
                "UnqualifiedJoinTypeUnsupported",
                $"Unqualified join type '{unqualified.UnqualifiedJoinType}' is outside the retained surface.",
                unqualified.Id);
        }

        RuntimeTableResult? shapeWitness = null;
        var applyRows = new List<RuntimeLocalRow>();
        foreach (var left in first.Rows)
        {
            var right = ExecuteTableReference(
                secondReference,
                visibleCommonTableExpressionOrdinal,
                new RuntimeFrame(left, outerFrame));
            shapeWitness ??= right;
            RequireEquivalentSourceShapes(shapeWitness.Sources, right.Sources, unqualified.Id);
            if (right.Rows.Count == 0)
            {
                if (isOuterApply)
                {
                    applyRows.Add(left.Combine(CreateNullLocalRow(right.Sources)));
                }

                continue;
            }

            applyRows.AddRange(right.Rows.Select(left.Combine));
        }

        if (shapeWitness is null)
        {
            var nullLeft = CreateNullLocalRow(first.Sources);
            shapeWitness = ExecuteTableReference(
                secondReference,
                visibleCommonTableExpressionOrdinal,
                new RuntimeFrame(nullLeft, outerFrame));
        }

        return new RuntimeTableResult(
            CombineSourceShapes(first.Sources, shapeWitness.Sources),
            applyRows);
    }

    private RuntimeTableResult CrossProduct(RuntimeTableResult left, RuntimeTableResult right)
    {
        var sources = CombineSourceShapes(left.Sources, right.Sources);
        var rows = new List<RuntimeLocalRow>();
        foreach (var leftRow in left.Rows)
        {
            rows.AddRange(right.Rows.Select(leftRow.Combine));
        }

        return new RuntimeTableResult(sources, rows);
    }

    private RuntimeTableResult ExposeRowset(
        RuntimeRowset rowset,
        string exposedName,
        string description,
        string? syntaxId)
    {
        RequireNamedUniqueColumns(rowset, description, syntaxId);
        var shape = new RuntimeSourceShape(exposedName, rowset.Columns);
        return new RuntimeTableResult(
            [shape],
            rowset.Rows.Select(row => RuntimeLocalRow.From(shape, row)).ToArray());
    }

    private RuntimeRowset ApplyDerivedColumnAliases(
        TableReference tableReference,
        RuntimeRowset rowset,
        string description,
        string? syntaxId)
    {
        var aliasInfo = TryGetAliasAndColumns(tableReference);
        if (aliasInfo is null || aliasInfo.Value.Columns.Count == 0)
        {
            return rowset;
        }

        if (aliasInfo.Value.Columns.Count != rowset.Columns.Count)
        {
            throw Fault(
                "DerivedColumnAliasCountMismatch",
                $"{description} declares {aliasInfo.Value.Columns.Count} column aliases but produces {rowset.Columns.Count} columns.",
                syntaxId);
        }

        return new RuntimeRowset(
            aliasInfo.Value.Columns.Select(name => new RuntimeColumn(name)).ToArray(),
            rowset.Rows);
    }

    private static IReadOnlyList<RuntimeSourceShape> CombineSourceShapes(
        IReadOnlyList<RuntimeSourceShape> first,
        IReadOnlyList<RuntimeSourceShape> second)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in first.Concat(second))
        {
            if (!names.Add(source.Name))
            {
                throw Fault(
                    "TableAliasDuplicate",
                    $"Table source name or alias '{source.Name}' is exposed more than once in the same scope.");
            }
        }

        return first.Concat(second).ToArray();
    }

    private static RuntimeLocalRow CreateNullLocalRow(IReadOnlyList<RuntimeSourceShape> sources)
    {
        var row = new RuntimeLocalRow();
        foreach (var source in sources)
        {
            row = row.Combine(RuntimeLocalRow.From(
                source,
                new RuntimeRow(source.Columns.Select(_ => MetaWeaveScriptValue.Null).ToArray())));
        }

        return row;
    }

    private static void RequireEquivalentSourceShapes(
        IReadOnlyList<RuntimeSourceShape> expected,
        IReadOnlyList<RuntimeSourceShape> actual,
        string syntaxId)
    {
        var expectedSignature = string.Join("|", expected.Select(source =>
            source.Name + ":" + string.Join(",", source.Columns.Select(column => column.Name))));
        var actualSignature = string.Join("|", actual.Select(source =>
            source.Name + ":" + string.Join(",", source.Columns.Select(column => column.Name))));
        if (!string.Equals(expectedSignature, actualSignature, StringComparison.OrdinalIgnoreCase))
        {
            throw Fault(
                "ApplyRowsetShapeChanged",
                "A lateral rowset exposed a different column shape for different input rows.",
                syntaxId);
        }
    }

    private long? TryGetIntegerLiteralValue(ScalarExpression expression)
    {
        var primary = navigator.TrySubtype<PrimaryExpression>(expression.Id);
        var value = primary is null ? null : navigator.TrySubtype<ValueExpression>(primary.Id);
        var literal = value is null ? null : navigator.TrySubtype<Literal>(value.Id);
        if (literal is null || navigator.TrySubtype<IntegerLiteral>(literal.Id) is null)
        {
            return null;
        }

        return long.TryParse(literal.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
