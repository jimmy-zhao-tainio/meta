using System.Collections.ObjectModel;
using Meta.Operations.Domain;

namespace MetaWeaveScript.Execution;

internal sealed class RuntimeNamedRelationContext
{
    private readonly MetaWeaveModel model;
    private readonly IReadOnlyDictionary<string, InMemoryWorkspace> sourceWorkspaces;
    private readonly IReadOnlyDictionary<string, MetaWeaveScriptValue> parameters;
    private readonly MetaWeaveScriptSemanticNavigator navigator;
    private readonly RuntimeSourceTableContext sourceTables;
    private readonly Action<MetaWeaveScriptRelation>? relationEvaluated;
    private readonly IReadOnlyDictionary<string, MetaWeaveScriptRelation> definitions;
    private readonly Dictionary<string, RuntimeNamedRelationState> states;
    private readonly Dictionary<string, RuntimeRowset> rowsets =
        new(StringComparer.OrdinalIgnoreCase);

    public RuntimeNamedRelationContext(
        MetaWeaveModel model,
        IReadOnlyList<MetaWeaveScriptRelation> relations,
        IReadOnlyDictionary<string, InMemoryWorkspace> sourceWorkspaces,
        IReadOnlyDictionary<string, MetaWeaveScriptValue> parameters,
        Action<MetaWeaveScriptRelation>? relationEvaluated = null)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        ArgumentNullException.ThrowIfNull(relations);
        this.sourceWorkspaces = sourceWorkspaces ?? throw new ArgumentNullException(nameof(sourceWorkspaces));
        this.parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        this.relationEvaluated = relationEvaluated;
        navigator = new MetaWeaveScriptSemanticNavigator(model);
        sourceTables = new RuntimeSourceTableContext(sourceWorkspaces);
        definitions = relations.ToDictionary(relation => relation.Name, StringComparer.OrdinalIgnoreCase);
        states = definitions.Keys.ToDictionary(
            name => name,
            _ => RuntimeNamedRelationState.NotEvaluated,
            StringComparer.OrdinalIgnoreCase);
    }

    public MetaWeaveScriptSemanticNavigator Navigator => navigator;

    public RuntimeSourceTableContext SourceTables => sourceTables;

    public void EvaluateAll()
    {
        foreach (var definition in definitions.Values.OrderBy(
                     relation => relation.Name,
                     StringComparer.OrdinalIgnoreCase))
        {
            _ = Execute(definition);
        }
    }

    public IReadOnlyDictionary<string, MetaWeaveScriptQueryOutput> ExportOutputs()
    {
        var outputs = rowsets
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                pair => pair.Key,
                pair => new MetaWeaveScriptQueryOutput(
                    Array.AsReadOnly(pair.Value.Columns
                        .Select(column => new MetaWeaveScriptQueryColumn(column.Name))
                        .ToArray()),
                    Array.AsReadOnly(pair.Value.Rows
                        .Select(row => new MetaWeaveScriptQueryRow(
                            Array.AsReadOnly(row.Values)))
                        .ToArray())),
                StringComparer.OrdinalIgnoreCase);
        return new ReadOnlyDictionary<string, MetaWeaveScriptQueryOutput>(outputs);
    }

    public bool TryExecute(string name, out RuntimeRowset rowset)
    {
        if (!definitions.TryGetValue(name, out var definition))
        {
            rowset = null!;
            return false;
        }

        rowset = Execute(definition);
        return true;
    }

    private RuntimeRowset Execute(MetaWeaveScriptRelation definition)
    {
        var state = states[definition.Name];
        if (state == RuntimeNamedRelationState.Evaluated)
        {
            return rowsets[definition.Name];
        }

        if (state == RuntimeNamedRelationState.Evaluating)
        {
            throw new MetaWeaveScriptExecutionFault(
                "NamedRelationRecursive",
                $"Named relation '{definition.Name}' participates in a dependency cycle.",
                relationName: definition.Name);
        }

        if (state == RuntimeNamedRelationState.Failed)
        {
            throw new MetaWeaveScriptExecutionFault(
                "NamedRelationEvaluationFailed",
                $"Named relation '{definition.Name}' previously failed evaluation.",
                relationName: definition.Name);
        }

        states[definition.Name] = RuntimeNamedRelationState.Evaluating;
        try
        {
            var rowset = new MetaWeaveScriptExecutionSession(
                model,
                definition.SelectStatement,
                sourceWorkspaces,
                parameters,
                this).Execute();
            RequireNamedUniqueColumns(rowset, definition.Name);
            rowsets.Add(definition.Name, rowset);
            states[definition.Name] = RuntimeNamedRelationState.Evaluated;
            relationEvaluated?.Invoke(definition);
            return rowset;
        }
        catch (MetaWeaveScriptExecutionFault fault)
        {
            states[definition.Name] = RuntimeNamedRelationState.Failed;
            throw fault.RelationName is null
                ? new MetaWeaveScriptExecutionFault(
                    fault.Code,
                    fault.Message,
                    fault.SyntaxId,
                    definition.Name)
                : fault;
        }
        catch
        {
            states[definition.Name] = RuntimeNamedRelationState.Failed;
            throw;
        }
    }

    private static void RequireNamedUniqueColumns(RuntimeRowset rowset, string relationName)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in rowset.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.Name))
            {
                throw new MetaWeaveScriptExecutionFault(
                    "NamedRelationColumnNameMissing",
                    $"Named relation '{relationName}' produces an unnamed column.",
                    relationName: relationName);
            }

            if (!names.Add(column.Name))
            {
                throw new MetaWeaveScriptExecutionFault(
                    "NamedRelationColumnNameDuplicate",
                    $"Named relation '{relationName}' produces column '{column.Name}' more than once.",
                    relationName: relationName);
            }
        }
    }
}
