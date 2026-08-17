using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace MetaWeaveScript.Execution;

internal sealed class MetaWeaveScriptSemanticNavigator
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> BasePropertyByType = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> OwnerPropertyByType = new();

    private readonly Dictionary<Type, Dictionary<string, object>> rowsById = [];
    private readonly Dictionary<Type, Dictionary<string, object>> rowsByBaseId = [];
    private readonly Dictionary<Type, Dictionary<string, List<object>>> rowsByOwnerId = [];
    private readonly Dictionary<Type, Dictionary<string, object>> orderedRowsByOwnerId = [];

    public MetaWeaveScriptSemanticNavigator(MetaWeaveModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        foreach (var listProperty in typeof(MetaWeaveModel).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!listProperty.PropertyType.IsGenericType ||
                listProperty.PropertyType.GetGenericTypeDefinition() != typeof(List<>))
            {
                continue;
            }

            var rowType = listProperty.PropertyType.GetGenericArguments()[0];
            if (IsDirectionScaffold(rowType))
            {
                continue;
            }

            var rows = (IEnumerable?)listProperty.GetValue(model)
                ?? throw Fault("SemanticCollectionMissing", $"Semantic collection '{listProperty.Name}' is missing.");
            IndexRows(rowType, rows.Cast<object>());
        }
    }

    public T RequireById<T>(string id, string label) where T : class
    {
        if (rowsById.TryGetValue(typeof(T), out var rows) && rows.TryGetValue(id, out var row))
        {
            return (T)row;
        }

        throw Fault("SemanticReferenceMissing", $"Required semantic reference '{label}' to {typeof(T).Name} '{id}' was not found.", id);
    }

    public T? TrySubtype<T>(string baseId) where T : class
    {
        return rowsByBaseId.TryGetValue(typeof(T), out var rows) && rows.TryGetValue(baseId, out var row)
            ? (T)row
            : null;
    }

    public TLink? TryOwnerLink<TLink>(string ownerId) where TLink : class
    {
        if (!rowsByOwnerId.TryGetValue(typeof(TLink), out var rows) ||
            !rows.TryGetValue(ownerId, out var matches))
        {
            return null;
        }

        if (matches.Count != 1)
        {
            throw Fault(
                "SemanticLinkMultiplicityInvalid",
                $"Semantic link {typeof(TLink).Name} has {matches.Count} rows for owner '{ownerId}', where at most one is allowed.",
                ownerId);
        }

        return (TLink)matches[0];
    }

    public TLink RequireOwnerLink<TLink>(string ownerId, string label) where TLink : class
    {
        return TryOwnerLink<TLink>(ownerId)
            ?? throw Fault("SemanticLinkMissing", $"Required semantic link '{label}' for owner '{ownerId}' was not found.", ownerId);
    }

    public IReadOnlyList<TItem> OrderedItems<TItem>(string ownerId) where TItem : class
    {
        if (!rowsByOwnerId.TryGetValue(typeof(TItem), out var rows) ||
            !rows.TryGetValue(ownerId, out var matches))
        {
            return [];
        }

        if (!orderedRowsByOwnerId.TryGetValue(typeof(TItem), out var orderedByOwner))
        {
            orderedByOwner = new Dictionary<string, object>(StringComparer.Ordinal);
            orderedRowsByOwnerId.Add(typeof(TItem), orderedByOwner);
        }

        if (orderedByOwner.TryGetValue(ownerId, out var cached))
        {
            return (IReadOnlyList<TItem>)cached;
        }

        var ordered = matches
            .Select(row => (Row: (TItem)row, Ordinal: ParseOrdinal(row), Id: GetId(row)))
            .OrderBy(item => item.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => item.Row)
            .ToArray();
        orderedByOwner.Add(ownerId, ordered);
        return ordered;
    }

    public IReadOnlyList<string> IdentifierParts(MultiPartIdentifier identifier)
    {
        return OrderedItems<MultiPartIdentifierIdentifiersItem>(identifier.Id)
            .Select(item => RequireIdentifier(item.Identifier, "MultiPartIdentifier.Identifier"))
            .ToArray();
    }

    public string RequireIdentifier(Identifier identifier, string label)
    {
        if (string.IsNullOrWhiteSpace(identifier.Value))
        {
            throw Fault("IdentifierMissing", $"{label} is blank.", identifier.Id);
        }

        return identifier.Value;
    }

    private void IndexRows(Type rowType, IEnumerable<object> rows)
    {
        var idIndex = new Dictionary<string, object>(StringComparer.Ordinal);
        var baseIndex = new Dictionary<string, object>(StringComparer.Ordinal);
        var ownerIndex = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        var baseProperty = ResolveBaseProperty(rowType);
        var ownerProperty = ResolveOwnerProperty(rowType);

        foreach (var row in rows)
        {
            var id = GetId(row);
            if (string.IsNullOrWhiteSpace(id))
            {
                throw Fault("SemanticIdentityMissing", $"{rowType.Name} contains a row with a blank Id.");
            }

            if (!idIndex.TryAdd(id, row))
            {
                throw Fault("SemanticIdentityDuplicate", $"{rowType.Name} contains duplicate Id '{id}'.", id);
            }

            AddSingleReferenceIndex(baseIndex, baseProperty, row, rowType, "base");

            var ownerId = GetRelatedId(ownerProperty, row);
            if (!string.IsNullOrWhiteSpace(ownerId))
            {
                if (!ownerIndex.TryGetValue(ownerId, out var ownerRows))
                {
                    ownerRows = [];
                    ownerIndex.Add(ownerId, ownerRows);
                }

                ownerRows.Add(row);
            }
        }

        rowsById[rowType] = idIndex;
        rowsByBaseId[rowType] = baseIndex;
        rowsByOwnerId[rowType] = ownerIndex;
    }

    private static void AddSingleReferenceIndex(
        IDictionary<string, object> index,
        PropertyInfo? property,
        object row,
        Type rowType,
        string role)
    {
        var relatedId = GetRelatedId(property, row);
        if (string.IsNullOrWhiteSpace(relatedId))
        {
            return;
        }

        if (!index.TryAdd(relatedId, row))
        {
            throw Fault(
                "SemanticSubtypeMultiplicityInvalid",
                $"{rowType.Name} contains more than one row for {role} '{relatedId}'.",
                relatedId);
        }
    }

    private static string GetId(object row) =>
        (string?)row.GetType().GetProperty("Id")?.GetValue(row) ?? string.Empty;

    private static string GetRelatedId(PropertyInfo? property, object row)
    {
        var related = property?.GetValue(row);
        return related is null ? string.Empty : GetId(related);
    }

    private static int ParseOrdinal(object row)
    {
        var text = (string?)row.GetType().GetProperty("Ordinal")?.GetValue(row);
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ordinal) || ordinal < 0)
        {
            throw Fault("SemanticOrdinalInvalid", $"{row.GetType().Name} '{GetId(row)}' has invalid ordinal '{text}'.", GetId(row));
        }

        return ordinal;
    }

    private static PropertyInfo? ResolveBaseProperty(Type type)
    {
        return BasePropertyByType.GetOrAdd(type, static rowType =>
        {
            var references = rowType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(IsReferenceProperty)
                .ToArray();
            return references.Length == 1 ? references[0] : null;
        });
    }

    private static PropertyInfo? ResolveOwnerProperty(Type type)
    {
        return OwnerPropertyByType.GetOrAdd(type, static rowType =>
        {
            var references = rowType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(IsReferenceProperty)
                .ToArray();
            if (references.Length <= 1)
            {
                return references.FirstOrDefault();
            }

            return references
                .OrderByDescending(property => property.Name.Length)
                .FirstOrDefault(property => rowType.Name.StartsWith(property.Name, StringComparison.Ordinal))
                ?? references[0];
        });
    }

    private static bool IsReferenceProperty(PropertyInfo property) =>
        property.PropertyType != typeof(string) &&
        property.PropertyType.GetProperty("Id") is not null;

    private static bool IsDirectionScaffold(Type rowType) =>
        rowType == typeof(Weave) ||
        rowType == typeof(Direction) ||
        rowType == typeof(DirectionRelation) ||
        rowType == typeof(DirectionSourceWorkspace) ||
        rowType == typeof(DirectionStringParameter) ||
        rowType == typeof(DirectionRequirement) ||
        rowType == typeof(Transformation);

    private static MetaWeaveScriptExecutionFault Fault(string code, string message, string? syntaxId = null) =>
        new(code, message, syntaxId);
}
