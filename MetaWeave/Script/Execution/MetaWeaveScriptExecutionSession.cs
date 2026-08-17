using System.Globalization;
using Meta.Operations.Domain;

namespace MetaWeaveScript.Execution;

internal sealed partial class MetaWeaveScriptExecutionSession
{
    private const int MaximumRecursiveCommonTableExpressionIterations = 32_767;

    private readonly MetaWeaveModel model;
    private readonly SelectStatement selectStatement;
    private readonly IReadOnlyDictionary<string, InMemoryWorkspace> sourceWorkspaces;
    private readonly IReadOnlyDictionary<string, MetaWeaveScriptValue> parameters;
    private readonly MetaWeaveScriptSemanticNavigator navigator;
    private readonly RuntimeSourceTableContext sourceTables;
    private readonly RuntimeNamedRelationContext? namedRelations;
    private readonly Dictionary<string, RuntimeCommonTableExpressionDefinition> cteDefinitions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeCommonTableExpressionState> cteStates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeRowset> cteRowsets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeRowset> recursiveCteIterationRowsets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeResolvedColumnReference> resolvedColumns =
        new(StringComparer.Ordinal);
    private readonly Dictionary<RuntimeWindowEvaluationKey, long[]> windowRowNumbers = [];
    private string? inspectedRecursiveCteName;
    private int inspectedRecursiveCteReferenceCount;

    public MetaWeaveScriptExecutionSession(
        MetaWeaveModel model,
        SelectStatement selectStatement,
        InMemoryWorkspace sourceWorkspace)
        : this(
            model,
            selectStatement,
            new Dictionary<string, InMemoryWorkspace>(StringComparer.OrdinalIgnoreCase)
            {
                ["source"] = sourceWorkspace ?? throw new ArgumentNullException(nameof(sourceWorkspace))
            },
            new Dictionary<string, MetaWeaveScriptValue>(StringComparer.OrdinalIgnoreCase))
    {
    }

    public MetaWeaveScriptExecutionSession(
        MetaWeaveModel model,
        SelectStatement selectStatement,
        IReadOnlyDictionary<string, InMemoryWorkspace> sourceWorkspaces,
        IReadOnlyDictionary<string, MetaWeaveScriptValue> parameters)
        : this(model, selectStatement, sourceWorkspaces, parameters, namedRelations: null)
    {
    }

    internal MetaWeaveScriptExecutionSession(
        MetaWeaveModel model,
        SelectStatement selectStatement,
        IReadOnlyDictionary<string, InMemoryWorkspace> sourceWorkspaces,
        IReadOnlyDictionary<string, MetaWeaveScriptValue> parameters,
        RuntimeNamedRelationContext? namedRelations)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        this.selectStatement = selectStatement ?? throw new ArgumentNullException(nameof(selectStatement));
        this.sourceWorkspaces = sourceWorkspaces ?? throw new ArgumentNullException(nameof(sourceWorkspaces));
        this.parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        this.namedRelations = namedRelations;
        navigator = namedRelations?.Navigator ?? new MetaWeaveScriptSemanticNavigator(model);
        sourceTables = namedRelations?.SourceTables ?? new RuntimeSourceTableContext(sourceWorkspaces);
    }

    public RuntimeRowset Execute()
    {
        InitializeCommonTableExpressions(selectStatement);
        var queryExpression = navigator.RequireOwnerLink<SelectStatementQueryExpressionLink>(
            selectStatement.Id,
            "SelectStatement.QueryExpression").QueryExpression;
        PrepareQueryExpression(
            queryExpression,
            int.MaxValue,
            outerFrame: null);
        return ExecuteQueryExpression(
            queryExpression,
            int.MaxValue,
            outerFrame: null);
    }

    private void InitializeCommonTableExpressions(SelectStatement selectStatement)
    {
        cteDefinitions.Clear();
        cteStates.Clear();
        cteRowsets.Clear();
        recursiveCteIterationRowsets.Clear();

        var statement = navigator.RequireById<StatementWithCtes>(
            selectStatement.StatementWithCtes.Id,
            "SelectStatement.StatementWithCtes");
        navigator.RequireById<TSqlStatement>(
            statement.TSqlStatement.Id,
            "StatementWithCtes.TSqlStatement");
        var withLink = navigator.TryOwnerLink<StatementWithCtesWithCtesLink>(statement.Id);
        if (withLink is null)
        {
            return;
        }

        var items = navigator.OrderedItems<WithCtesCommonTableExpressionsItem>(withLink.WithCtes.Id);
        for (var ordinal = 0; ordinal < items.Count; ordinal++)
        {
            var cte = items[ordinal].CommonTableExpression;
            var nameLink = navigator.RequireOwnerLink<CommonTableExpressionExpressionNameLink>(
                cte.Id,
                "CommonTableExpression.ExpressionName");
            var queryLink = navigator.RequireOwnerLink<CommonTableExpressionQueryExpressionLink>(
                cte.Id,
                "CommonTableExpression.QueryExpression");
            var name = navigator.RequireIdentifier(nameLink.Identifier, "CommonTableExpression.ExpressionName");
            var definition = new RuntimeCommonTableExpressionDefinition(
                cte.Id,
                name,
                queryLink.QueryExpression,
                ordinal);

            if (!cteDefinitions.TryAdd(name, definition))
            {
                throw Fault(
                    "CommonTableExpressionNameDuplicate",
                    $"Common table expression name '{name}' is declared more than once.",
                    cte.Id);
            }

            cteStates.Add(name, RuntimeCommonTableExpressionState.NotEvaluated);
        }
    }

    private RuntimeRowset ExecuteCommonTableExpression(RuntimeCommonTableExpressionDefinition definition)
    {
        var state = cteStates[definition.Name];
        if (state == RuntimeCommonTableExpressionState.Evaluated)
        {
            return cteRowsets[definition.Name];
        }

        if (state == RuntimeCommonTableExpressionState.Evaluating)
        {
            throw Fault(
                "CommonTableExpressionRecursive",
                $"Common table expression '{definition.Name}' recursively references itself.",
                definition.Id);
        }

        if (state == RuntimeCommonTableExpressionState.Failed)
        {
            throw Fault(
                "CommonTableExpressionEvaluationFailed",
                $"Common table expression '{definition.Name}' previously failed evaluation.",
                definition.Id);
        }

        cteStates[definition.Name] = RuntimeCommonTableExpressionState.Evaluating;
        try
        {
            var rowset = ExecuteCommonTableExpressionDefinition(definition);
            RequireNamedUniqueColumns(rowset, $"Common table expression '{definition.Name}'", definition.Id);
            cteRowsets[definition.Name] = rowset;
            cteStates[definition.Name] = RuntimeCommonTableExpressionState.Evaluated;
            return rowset;
        }
        catch
        {
            recursiveCteIterationRowsets.Remove(definition.Name);
            if (string.Equals(inspectedRecursiveCteName, definition.Name, StringComparison.OrdinalIgnoreCase))
            {
                inspectedRecursiveCteName = null;
            }
            cteStates[definition.Name] = RuntimeCommonTableExpressionState.Failed;
            throw;
        }
    }

    private RuntimeRowset ExecuteCommonTableExpressionDefinition(
        RuntimeCommonTableExpressionDefinition definition)
    {
        if (!TryGetBinaryQueryOperands(definition.QueryExpression, out var anchor, out var recursiveMember))
        {
            PrepareQueryExpression(
                definition.QueryExpression,
                definition.Ordinal,
                outerFrame: null);
            return ExecuteQueryExpression(
                definition.QueryExpression,
                definition.Ordinal,
                outerFrame: null);
        }

        PrepareQueryExpression(anchor, definition.Ordinal, outerFrame: null);
        var anchorRows = ExecuteQueryExpression(anchor, definition.Ordinal, outerFrame: null);
        RequireNamedUniqueColumns(
            anchorRows,
            $"Recursive common table expression '{definition.Name}' anchor",
            definition.Id);

        recursiveCteIterationRowsets[definition.Name] = anchorRows;
        var previousInspectedRecursiveCteName = inspectedRecursiveCteName;
        var previousInspectedRecursiveCteReferenceCount = inspectedRecursiveCteReferenceCount;
        inspectedRecursiveCteName = definition.Name;
        inspectedRecursiveCteReferenceCount = 0;
        int recursiveReferenceCount;
        try
        {
            PrepareQueryExpression(recursiveMember, definition.Ordinal, outerFrame: null);
            recursiveReferenceCount = inspectedRecursiveCteReferenceCount;
        }
        finally
        {
            inspectedRecursiveCteName = previousInspectedRecursiveCteName;
            inspectedRecursiveCteReferenceCount = previousInspectedRecursiveCteReferenceCount;
        }

        if (recursiveReferenceCount == 0)
        {
            recursiveCteIterationRowsets.Remove(definition.Name);
            var secondRows = ExecuteQueryExpression(
                recursiveMember,
                definition.Ordinal,
                outerFrame: null);
            return AppendUnionAll(anchorRows, secondRows, definition.Id);
        }

        if (recursiveReferenceCount != 1)
        {
            throw Fault(
                "CommonTableExpressionRecursiveReferenceCountInvalid",
                $"Recursive common table expression '{definition.Name}' must reference itself exactly once in its recursive member; found {recursiveReferenceCount} references.",
                definition.Id);
        }

        var rows = new List<RuntimeRow>(anchorRows.Rows);
        var iterationRows = anchorRows;
        try
        {
            for (var iteration = 1; iteration <= MaximumRecursiveCommonTableExpressionIterations; iteration++)
            {
                recursiveCteIterationRowsets[definition.Name] = iterationRows;
                var nextRows = ExecuteQueryExpression(
                    recursiveMember,
                    definition.Ordinal,
                    outerFrame: null);
                RequireCompatibleUnionColumns(anchorRows, nextRows, definition.Id);

                if (nextRows.Rows.Count == 0)
                {
                    return new RuntimeRowset(anchorRows.Columns, rows);
                }

                if (RowsAreEqual(iterationRows.Rows, nextRows.Rows))
                {
                    throw Fault(
                        "CommonTableExpressionRecursionDidNotAdvance",
                        $"Recursive common table expression '{definition.Name}' reproduced the preceding iteration and cannot terminate.",
                        definition.Id);
                }

                rows.AddRange(nextRows.Rows);
                iterationRows = new RuntimeRowset(anchorRows.Columns, nextRows.Rows);
            }

            throw Fault(
                "CommonTableExpressionRecursionLimitExceeded",
                $"Recursive common table expression '{definition.Name}' exceeded the WeaveScript limit of {MaximumRecursiveCommonTableExpressionIterations} iterations.",
                definition.Id);
        }
        finally
        {
            recursiveCteIterationRowsets.Remove(definition.Name);
        }
    }

    private bool TryGetBinaryQueryOperands(
        QueryExpression queryExpression,
        out QueryExpression first,
        out QueryExpression second)
    {
        while (navigator.TrySubtype<QueryParenthesisExpression>(queryExpression.Id) is { } parenthesis)
        {
            queryExpression = navigator.RequireOwnerLink<QueryParenthesisExpressionQueryExpressionLink>(
                parenthesis.Id,
                "QueryParenthesisExpression.QueryExpression").QueryExpression;
        }

        if (navigator.TrySubtype<BinaryQueryExpression>(queryExpression.Id) is not { } binary)
        {
            first = null!;
            second = null!;
            return false;
        }

        first = navigator.RequireOwnerLink<BinaryQueryExpressionFirstQueryExpressionLink>(
            binary.Id,
            "BinaryQueryExpression.FirstQueryExpression").QueryExpression;
        second = navigator.RequireOwnerLink<BinaryQueryExpressionSecondQueryExpressionLink>(
            binary.Id,
            "BinaryQueryExpression.SecondQueryExpression").QueryExpression;
        return true;
    }

    private RuntimeRowset AppendUnionAll(
        RuntimeRowset first,
        RuntimeRowset second,
        string syntaxId)
    {
        RequireCompatibleUnionColumns(first, second, syntaxId);
        return new RuntimeRowset(first.Columns, first.Rows.Concat(second.Rows).ToArray());
    }

    private void RequireCompatibleUnionColumns(
        RuntimeRowset first,
        RuntimeRowset second,
        string syntaxId)
    {
        if (first.Columns.Count != second.Columns.Count)
        {
            throw Fault(
                "UnionColumnCountMismatch",
                $"UNION ALL operands expose {first.Columns.Count} and {second.Columns.Count} columns.",
                syntaxId);
        }
    }

    private static bool RowsAreEqual(
        IReadOnlyList<RuntimeRow> first,
        IReadOnlyList<RuntimeRow> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Count; index++)
        {
            if (!RuntimeRowEqualityComparer.Instance.Equals(first[index], second[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static void RequireNamedUniqueColumns(RuntimeRowset rowset, string owner, string? syntaxId = null)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in rowset.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.Name))
            {
                throw Fault("RowsetColumnNameMissing", $"{owner} exposes an unnamed column.", syntaxId);
            }

            if (!names.Add(column.Name))
            {
                throw Fault("RowsetColumnNameDuplicate", $"{owner} exposes duplicate column name '{column.Name}'.", syntaxId);
            }
        }
    }

    private string? TryGetTableAlias(TableReference tableReference)
    {
        var aliasBase = navigator.TrySubtype<TableReferenceWithAlias>(tableReference.Id);
        if (aliasBase is null)
        {
            return null;
        }

        var aliasLink = navigator.TryOwnerLink<TableReferenceWithAliasAliasLink>(aliasBase.Id);
        return aliasLink is null
            ? null
            : navigator.RequireIdentifier(aliasLink.Identifier, "TableReferenceWithAlias.Alias");
    }

    private (TableReferenceWithAliasAndColumns Base, IReadOnlyList<string> Columns)?
        TryGetAliasAndColumns(TableReference tableReference)
    {
        var aliasBase = navigator.TrySubtype<TableReferenceWithAlias>(tableReference.Id);
        var aliasAndColumns = aliasBase is null
            ? null
            : navigator.TrySubtype<TableReferenceWithAliasAndColumns>(aliasBase.Id);
        if (aliasAndColumns is null)
        {
            return null;
        }

        var columns = navigator.OrderedItems<TableReferenceWithAliasAndColumnsColumnsItem>(aliasAndColumns.Id)
            .Select(item => navigator.RequireIdentifier(item.Identifier, "TableReferenceWithAliasAndColumns.Column"))
            .ToArray();
        return (aliasAndColumns, columns);
    }

    private string FunctionName(FunctionCall functionCall) =>
        navigator.RequireIdentifier(
            navigator.RequireOwnerLink<FunctionCallFunctionNameLink>(
                functionCall.Id,
                "FunctionCall.FunctionName").Identifier,
            "FunctionCall.FunctionName");

    private static int CompareValues(
        MetaWeaveScriptValue left,
        MetaWeaveScriptValue right,
        bool nullsFirst = true)
    {
        if (left.IsNull || right.IsNull)
        {
            if (left.IsNull && right.IsNull)
            {
                return 0;
            }

            return left.IsNull == nullsFirst ? -1 : 1;
        }

        if (left.Kind != right.Kind)
        {
            throw Fault(
                "ValueKindMismatch",
                $"Cannot compare {left.Kind} with {right.Kind}; WeaveScript performs no implicit type conversion.");
        }

        return left.Kind switch
        {
            MetaWeaveScriptValueKind.String => CompareStrings(left.StringValue!, right.StringValue!),
            MetaWeaveScriptValueKind.Integer => left.IntegerValue.CompareTo(right.IntegerValue),
            _ => 0
        };
    }

    private static int CompareStrings(string left, string right)
        => StringComparer.OrdinalIgnoreCase.Compare(left, right);

    private static long RequireInteger(MetaWeaveScriptValue value, string description, string? syntaxId = null)
    {
        if (value.Kind != MetaWeaveScriptValueKind.Integer)
        {
            throw Fault(
                "IntegerValueRequired",
                $"{description} requires an integer value, but received {value.Kind}.",
                syntaxId);
        }

        return value.IntegerValue;
    }

    private static string? AsNullableString(MetaWeaveScriptValue value) =>
        value.IsNull ? null : value.ToInvariantString();

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static MetaWeaveScriptExecutionFault Fault(
        string code,
        string message,
        string? syntaxId = null) =>
        new(code, message, syntaxId);
}
