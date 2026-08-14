using System.Globalization;
using Meta.Operations.Domain;

namespace MetaWeaveScript.Execution;

internal sealed partial class MetaWeaveScriptExecutionSession
{
    private readonly MetaWeaveModel model;
    private readonly SelectStatement selectStatement;
    private readonly InMemoryWorkspace sourceWorkspace;
    private readonly MetaWeaveScriptSemanticNavigator navigator;
    private readonly Dictionary<string, RuntimeCommonTableExpressionDefinition> cteDefinitions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeCommonTableExpressionState> cteStates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeRowset> cteRowsets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeResolvedColumnReference> resolvedColumns =
        new(StringComparer.Ordinal);

    public MetaWeaveScriptExecutionSession(
        MetaWeaveModel model,
        SelectStatement selectStatement,
        InMemoryWorkspace sourceWorkspace)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        this.selectStatement = selectStatement ?? throw new ArgumentNullException(nameof(selectStatement));
        this.sourceWorkspace = sourceWorkspace ?? throw new ArgumentNullException(nameof(sourceWorkspace));
        navigator = new MetaWeaveScriptSemanticNavigator(model);
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
            PrepareQueryExpression(
                definition.QueryExpression,
                definition.Ordinal,
                outerFrame: null);
            var rowset = ExecuteQueryExpression(
                definition.QueryExpression,
                definition.Ordinal,
                outerFrame: null);
            RequireNamedUniqueColumns(rowset, $"Common table expression '{definition.Name}'", definition.Id);
            cteRowsets[definition.Name] = rowset;
            cteStates[definition.Name] = RuntimeCommonTableExpressionState.Evaluated;
            return rowset;
        }
        catch
        {
            cteStates[definition.Name] = RuntimeCommonTableExpressionState.Failed;
            throw;
        }
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
