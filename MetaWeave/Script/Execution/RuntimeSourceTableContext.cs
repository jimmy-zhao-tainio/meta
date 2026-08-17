using Meta.Operations.Domain;

namespace MetaWeaveScript.Execution;

internal sealed class RuntimeSourceTableContext
{
    private readonly IReadOnlyDictionary<string, InMemoryWorkspace> sourceWorkspaces;
    private readonly Dictionary<string, Dictionary<string, RuntimeRowset>> rowsetsBySource =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<MetaWeaveScriptValue>> valueIndexes =
        new(StringComparer.OrdinalIgnoreCase);

    public RuntimeSourceTableContext(
        IReadOnlyDictionary<string, InMemoryWorkspace> sourceWorkspaces)
    {
        this.sourceWorkspaces = sourceWorkspaces ?? throw new ArgumentNullException(nameof(sourceWorkspaces));
    }

    public RuntimeRowset Resolve(
        IReadOnlyList<string> parts,
        string entityName,
        string syntaxId)
    {
        var sourceName = ResolveSourceName(parts, entityName, syntaxId);
        if (!rowsetsBySource.TryGetValue(sourceName, out var rowsetsByEntity))
        {
            rowsetsByEntity = new Dictionary<string, RuntimeRowset>(StringComparer.OrdinalIgnoreCase);
            rowsetsBySource.Add(sourceName, rowsetsByEntity);
        }

        if (rowsetsByEntity.TryGetValue(entityName, out var cached))
        {
            return cached;
        }

        var sourceWorkspace = sourceWorkspaces[sourceName];
        var entity = sourceWorkspace.Model.FindEntity(entityName)!;
        var columns = new List<RuntimeColumn> { new("Id") };
        columns.AddRange(entity.Properties.Select(property => new RuntimeColumn(property.Name)));
        columns.AddRange(entity.Relationships.Select(relationship => new RuntimeColumn(relationship.GetColumnName())));
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
                return new RuntimeRow(values.ToArray());
            })
            .ToArray();
        var rowset = new RuntimeRowset(columns, rows);
        rowsetsByEntity.Add(entityName, rowset);
        return rowset;
    }

    public bool ContainsValue(
        IReadOnlyList<string> parts,
        string entityName,
        string columnName,
        MetaWeaveScriptValue value,
        string syntaxId)
    {
        if (value.IsNull)
        {
            return false;
        }

        var sourceName = ResolveSourceName(parts, entityName, syntaxId);
        var rowset = Resolve(parts, entityName, syntaxId);
        var columnOrdinal = -1;
        for (var ordinal = 0; ordinal < rowset.Columns.Count; ordinal++)
        {
            if (string.Equals(
                    rowset.Columns[ordinal].Name,
                    columnName,
                    StringComparison.OrdinalIgnoreCase))
            {
                columnOrdinal = ordinal;
                break;
            }
        }

        if (columnOrdinal < 0)
        {
            throw Fault(
                "ColumnReferenceNotFound",
                $"Source entity '{string.Join(".", parts)}' does not expose member '{columnName}'.",
                syntaxId);
        }

        var indexName = sourceName + "\0" + entityName + "\0" + columnName;
        if (!valueIndexes.TryGetValue(indexName, out var index))
        {
            index = new HashSet<MetaWeaveScriptValue>(MetaWeaveScriptValueEqualityComparer.Instance);
            foreach (var row in rowset.Rows)
            {
                var candidate = row.Values[columnOrdinal];
                if (!candidate.IsNull)
                {
                    index.Add(candidate);
                }
            }

            valueIndexes.Add(indexName, index);
        }

        return index.Contains(value);
    }

    private string ResolveSourceName(
        IReadOnlyList<string> parts,
        string entityName,
        string syntaxId)
    {
        if (parts.Count == 2)
        {
            if (!sourceWorkspaces.TryGetValue(parts[0], out var qualifiedWorkspace))
            {
                throw Fault(
                    "SourceWorkspaceNotFound",
                    $"Source workspace '{parts[0]}' was not supplied.",
                    syntaxId);
            }

            if (qualifiedWorkspace.Model.FindEntity(entityName) is null)
            {
                throw Fault(
                    "SourceEntityNotFound",
                    $"Source workspace '{parts[0]}' does not contain entity '{entityName}'.",
                    syntaxId);
            }

            return sourceWorkspaces.Keys.Single(name =>
                StringComparer.OrdinalIgnoreCase.Equals(name, parts[0]));
        }

        var matches = sourceWorkspaces
            .Where(source => source.Value.Model.FindEntity(entityName) is not null)
            .Select(source => source.Key)
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw Fault(
                "SourceEntityNotFound",
                $"Entity '{entityName}' was not found in any supplied source workspace.",
                syntaxId),
            _ => throw Fault(
                "SourceEntityAmbiguous",
                $"Entity '{entityName}' exists in more than one source workspace; qualify it with the source workspace name.",
                syntaxId)
        };
    }

    private static MetaWeaveScriptExecutionFault Fault(
        string code,
        string message,
        string? syntaxId = null) =>
        new(code, message, syntaxId);
}
