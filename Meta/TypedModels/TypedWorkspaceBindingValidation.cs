using Meta.Operations;
using Meta.Operations.Domain;

namespace Meta.TypedModels;

/// <summary>Nonreflective integrity checks shared by generated typed workspace bindings.</summary>
public static class TypedWorkspaceBindingValidation
{
    public static void RequireContract(GenericModel contract, string expectedSignature)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (!string.Equals(contract.ComputeContractSignature(), expectedSignature, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The selected workspace model does not match the generated binding contract. Regenerate the binding for the selected model.");
        }
    }

    public static void RequireValid(InMemoryWorkspace state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var diagnostics = WorkspaceValidator.Validate(state.Model, state.Instance);
        if (diagnostics.HasErrors)
        {
            var errors = diagnostics.Issues
                .Where(issue => issue.Severity == IssueSeverity.Error)
                .Take(5)
                .Select(issue => $"{issue.Code} {issue.Location} - {issue.Message}");
            throw new InvalidOperationException("Cannot bind invalid workspace metadata. " + string.Join(" | ", errors));
        }
    }

    public static IReadOnlyList<GenericRecord> Records(InMemoryWorkspace state, string entityName) =>
        state.Instance.RecordsByEntity.TryGetValue(entityName, out var records)
            ? records.OrderBy(record => record.Id, MetaIdentity.Comparer)
                .ThenBy(record => record.Id, StringComparer.Ordinal).ToArray()
            : Array.Empty<GenericRecord>();

    public static Dictionary<string, T> Index<T>(
        IEnumerable<T>? rows,
        Func<T, string> getId,
        string entityName)
        where T : class
    {
        if (rows is null)
        {
            throw new InvalidOperationException($"Entity '{entityName}' has no typed collection.");
        }

        var index = new Dictionary<string, T>(MetaIdentity.Comparer);
        foreach (var row in rows)
        {
            if (row is null)
            {
                throw new InvalidOperationException($"Entity '{entityName}' contains a null row.");
            }

            var id = MetaIdentity.Require(getId(row), $"Entity '{entityName}' contains an invalid Id.");
            if (!index.TryAdd(id, row))
            {
                throw new InvalidOperationException($"Entity '{entityName}' contains duplicate Id '{id}'.");
            }
        }

        return index;
    }

    public static IEnumerable<T> Ordered<T>(IReadOnlyDictionary<string, T> index) =>
        index.OrderBy(pair => pair.Key, MetaIdentity.Comparer)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value);

    public static string? OptionalValue(GenericRecord record, string propertyName) =>
        record.Values.TryGetValue(propertyName, out var value) ? value : null;

    public static string RequiredValue(GenericRecord record, string propertyName) =>
        OptionalValue(record, propertyName) ?? throw new InvalidOperationException(
            $"Record '{record.Id}' is missing required property '{propertyName}'.");

    public static T? OptionalTarget<T>(
        GenericRecord record,
        string relationshipName,
        IReadOnlyDictionary<string, T> index)
        where T : class
    {
        if (!record.RelationshipIds.TryGetValue(relationshipName, out var id))
        {
            return null;
        }

        return index.TryGetValue(id, out var target)
            ? target
            : throw new InvalidOperationException(
                $"Record '{record.Id}' relationship '{relationshipName}' references missing Id '{id}'.");
    }

    public static T RequiredTarget<T>(
        GenericRecord record,
        string relationshipName,
        IReadOnlyDictionary<string, T> index)
        where T : class =>
        OptionalTarget(record, relationshipName, index) ?? throw new InvalidOperationException(
            $"Record '{record.Id}' is missing required relationship '{relationshipName}'.");

    public static void RequireCanonical<T>(
        T target,
        string id,
        IReadOnlyDictionary<string, T> index,
        string relationshipName)
        where T : class
    {
        if (!index.TryGetValue(id, out var canonical) || !ReferenceEquals(canonical, target))
        {
            throw new InvalidOperationException(
                $"Relationship '{relationshipName}' references Id '{id}' without its canonical typed target.");
        }
    }
}
