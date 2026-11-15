using System.Globalization;

namespace MetaWeaveScript.Execution;

internal sealed class MetaWeaveScriptSemanticNavigator
{
    private readonly Dictionary<Type, EntityAccess> entitiesByType = [];
    private readonly Dictionary<Type, Dictionary<string, object>> rowsById = [];
    private readonly Dictionary<Type, Dictionary<string, object>> rowsByBaseId = [];
    private readonly Dictionary<Type, Dictionary<string, List<object>>> rowsByOwnerId = [];
    private readonly Dictionary<Type, Dictionary<string, object>> orderedRowsByOwnerId = [];

    public MetaWeaveScriptSemanticNavigator(MetaWeaveModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var access = new ModelAccess(model, entitiesByType);
        MetaWeaveModelAccess.VisitCollections(access);
        MetaWeaveModelAccess.VisitRelationships(access);
        foreach (var (rowType, entity) in entitiesByType)
        {
            IndexRows(rowType, entity);
        }
    }

    public T RequireById<T>(string id, string label) where T : class
    {
        if (rowsById.TryGetValue(typeof(T), out var rows) && rows.TryGetValue(id, out var row))
        {
            return (T)row;
        }

        throw Fault("SemanticReferenceMissing", $"Required semantic reference '{label}' to {entitiesByType[typeof(T)].EntityName} '{id}' was not found.", id);
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
                $"Semantic link {entitiesByType[typeof(TLink)].EntityName} has {matches.Count} rows for owner '{ownerId}', where at most one is allowed.",
                ownerId);
        }

        return (TLink)matches[0];
    }

    public TLink RequireOwnerLink<TLink>(string ownerId, string label) where TLink : class
    {
        return TryOwnerLink<TLink>(ownerId)
            ?? throw Fault("SemanticLinkMissing", $"Required semantic link '{label}' for owner '{ownerId}' was not found.", ownerId);
    }

    public IReadOnlyList<TItem> OrderedItems<TItem>(string ownerId, Func<TItem, string?> selectOrdinal) where TItem : class
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

        var entity = entitiesByType[typeof(TItem)];
        var ordered = matches
            .Select(row => (Row: (TItem)row, Ordinal: ParseOrdinal(selectOrdinal((TItem)row), entity.EntityName, entity.ReadId(row)), Id: entity.ReadId(row)))
            .OrderBy(item => item.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => item.Row)
            .ToArray();
        orderedByOwner.Add(ownerId, ordered);
        return ordered;
    }

    public IReadOnlyList<string> IdentifierParts(MultiPartIdentifier identifier)
    {
        return OrderedItems<MultiPartIdentifierIdentifiersItem>(identifier.Id, static row => row.Ordinal)
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

    private void IndexRows(Type rowType, EntityAccess entity)
    {
        var idIndex = new Dictionary<string, object>(StringComparer.Ordinal);
        var baseIndex = new Dictionary<string, object>(StringComparer.Ordinal);
        var ownerIndex = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        var baseReference = entity.References.Count == 1 ? entity.References[0] : null;
        var ownerReference = entity.References
            .OrderByDescending(reference => reference.NavigationName.Length)
            .FirstOrDefault(reference => entity.EntityName.StartsWith(reference.NavigationName, StringComparison.Ordinal))
            ?? entity.References.FirstOrDefault();

        foreach (var row in entity.Rows)
        {
            var id = entity.ReadId(row);
            if (string.IsNullOrWhiteSpace(id))
            {
                throw Fault("SemanticIdentityMissing", $"{entity.EntityName} contains a row with a blank Id.");
            }

            if (!idIndex.TryAdd(id, row))
            {
                throw Fault("SemanticIdentityDuplicate", $"{entity.EntityName} contains duplicate Id '{id}'.", id);
            }

            AddSingleReferenceIndex(baseIndex, baseReference, row, entity.EntityName, "base");

            var ownerId = ownerReference?.ReadTargetId(row) ?? string.Empty;
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
        ReferenceAccess? reference,
        object row,
        string entityName,
        string role)
    {
        var relatedId = reference?.ReadTargetId(row) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(relatedId))
        {
            return;
        }

        if (!index.TryAdd(relatedId, row))
        {
            throw Fault(
                "SemanticSubtypeMultiplicityInvalid",
                $"{entityName} contains more than one row for {role} '{relatedId}'.",
                relatedId);
        }
    }

    private static int ParseOrdinal(string? text, string entityName, string id)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ordinal) || ordinal < 0)
        {
            throw Fault("SemanticOrdinalInvalid", $"{entityName} '{id}' has invalid ordinal '{text}'.", id);
        }

        return ordinal;
    }

    private sealed class EntityAccess(string entityName, IEnumerable<object> rows, Func<object, string> readId)
    {
        public string EntityName { get; } = entityName;
        public IEnumerable<object> Rows { get; } = rows;
        public Func<object, string> ReadId { get; } = readId;
        public List<ReferenceAccess> References { get; } = [];
    }

    private sealed record ReferenceAccess(string NavigationName, Func<object, string> ReadTargetId);

    private sealed class ModelAccess(MetaWeaveModel model, IDictionary<Type, EntityAccess> entities)
        : IMetaWeaveCollectionVisitor, IMetaWeaveRelationshipVisitor
    {
        public void Visit<TEntity>(string entityName, Func<MetaWeaveModel, IList<TEntity>> rows,
            Func<TEntity, string> readId, Action<TEntity, string> writeId) where TEntity : class
        {
            if (!IsDirectionScaffold(entityName))
            {
                var collection = rows(model)
                    ?? throw Fault("SemanticCollectionMissing", $"Semantic collection '{entityName}List' is missing.");
                entities.Add(typeof(TEntity), new EntityAccess(entityName, collection, row => readId((TEntity)row) ?? string.Empty));
            }
        }

        public void Visit<TRow, TTarget>(string entityName, string navigationName, string targetEntityName, bool isNullable,
            Func<MetaWeaveModel, IList<TRow>> rows, Func<TRow, TTarget?> read, Action<TRow, TTarget?> write,
            Func<TTarget, string> readTargetId) where TRow : class where TTarget : class
        {
            if (entities.TryGetValue(typeof(TRow), out var entity))
            {
                entity.References.Add(new ReferenceAccess(navigationName, row =>
                {
                    var target = read((TRow)row);
                    return target is null ? string.Empty : readTargetId(target) ?? string.Empty;
                }));
            }
        }
    }

    private static bool IsDirectionScaffold(string entityName) =>
        entityName is nameof(Weave) or nameof(Direction) or nameof(DirectionRelation) or
            nameof(DirectionSourceWorkspace) or nameof(DirectionStringParameter) or
            nameof(DirectionRequirement) or nameof(Transformation);

    private static MetaWeaveScriptExecutionFault Fault(string code, string message, string? syntaxId = null) =>
        new(code, message, syntaxId);
}
