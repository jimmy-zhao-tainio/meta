using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace Meta.Operations;

public interface IMetaWorkspaceSource
{
    ValueTask<string> ReadModelNameAsync(
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> ReadEntityNamesAsync(
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<PropertyDefinition> ReadPropertiesAsync(
        string entityName,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<RelationshipDefinition> ReadRelationshipsAsync(
        string entityName,
        CancellationToken cancellationToken = default);

    // Record streams are ordered by Id using ordinal comparison.
    IAsyncEnumerable<RecordData> ReadRecordsAsync(
        string entityName,
        CancellationToken cancellationToken = default);

    ValueTask<long> CountRecordsAsync(
        string entityName,
        CancellationToken cancellationToken = default);

    ValueTask<RecordQueryResult> QueryRecordsAsync(
        string entityName,
        RecordQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<RecordData?> ReadRecordAsync(
        string entityName,
        string id,
        CancellationToken cancellationToken = default);
}

public sealed record PropertyDefinition(
    string Name,
    bool IsRequired);

public sealed record RelationshipDefinition(
    string TargetEntityName,
    string? Role,
    bool IsRequired)
{
    public string GetRoleOrDefault() =>
        string.IsNullOrWhiteSpace(Role) ? TargetEntityName : Role;

    public string GetColumnName() => GetRoleOrDefault() + "Id";
}

public sealed class RecordData
{
    public RecordData(
        string id,
        IReadOnlyDictionary<string, string>? values = null,
        IReadOnlyDictionary<string, string>? relationshipIds = null)
    {
        Id = id;
        Values = Copy(values);
        RelationshipIds = Copy(relationshipIds);
    }

    public string Id { get; }
    public IReadOnlyDictionary<string, string> Values { get; }
    public IReadOnlyDictionary<string, string> RelationshipIds { get; }

    private static IReadOnlyDictionary<string, string> Copy(
        IReadOnlyDictionary<string, string>? source)
    {
        var copy = new Dictionary<string, string>(MetaName.Comparer);
        if (source != null)
        {
            foreach (var item in source)
            {
                copy.Add(item.Key, item.Value);
            }
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}

public sealed class InMemoryWorkspaceSource : IMetaWorkspaceSource
{
    private readonly InMemoryWorkspace _workspace;

    public InMemoryWorkspaceSource(InMemoryWorkspace workspace)
    {
        _workspace = workspace ??
                     throw new ArgumentNullException(nameof(workspace));
    }

    public ValueTask<string> ReadModelNameAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_workspace.Model.Name);
    }

    public async IAsyncEnumerable<string> ReadEntityNamesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var entity in _workspace.Model.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entity.Name;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async IAsyncEnumerable<PropertyDefinition> ReadPropertiesAsync(
        string entityName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var entity = RequireEntity(entityName);
        foreach (var property in entity.Properties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new PropertyDefinition(
                property.Name,
                IsRequired: !property.IsNullable);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async IAsyncEnumerable<RelationshipDefinition> ReadRelationshipsAsync(
        string entityName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var entity = RequireEntity(entityName);
        foreach (var relationship in entity.Relationships)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new RelationshipDefinition(
                relationship.Entity,
                string.IsNullOrEmpty(relationship.Role)
                    ? null
                    : relationship.Role,
                IsRequired: !relationship.IsNullable);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async IAsyncEnumerable<RecordData> ReadRecordsAsync(
        string entityName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var entity = RequireEntity(entityName);
        if (_workspace.Instance.RecordsByEntity.TryGetValue(
                entity.Name,
                out var records))
        {
            foreach (var record in records.OrderBy(
                         item => item.Id,
                         StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new RecordData(
                    record.Id,
                    record.Values,
                    record.RelationshipIds);
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public ValueTask<RecordData?> ReadRecordAsync(
        string entityName,
        string id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entity = RequireEntity(entityName);
        var identity = MetaIdentity.Require(id, "Record Id.");
        if (!_workspace.Instance.RecordsByEntity.TryGetValue(
                entity.Name,
                out var records))
        {
            return ValueTask.FromResult<RecordData?>(null);
        }

        var record = records.FirstOrDefault(candidate =>
            MetaIdentity.Comparer.Equals(candidate.Id, identity));
        return ValueTask.FromResult(
            record == null
                ? null
                : new RecordData(
                    record.Id,
                    record.Values,
                    record.RelationshipIds));
    }

    public ValueTask<long> CountRecordsAsync(
        string entityName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entity = RequireEntity(entityName);
        var count = _workspace.Instance.RecordsByEntity.TryGetValue(
            entity.Name,
            out var records)
            ? records.Count
            : 0;
        return ValueTask.FromResult((long)count);
    }

    public ValueTask<RecordQueryResult> QueryRecordsAsync(
        string entityName,
        RecordQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var entity = RequireEntity(entityName);
        IEnumerable<GenericRecord> records =
            _workspace.Instance.RecordsByEntity.TryGetValue(
                entity.Name,
                out var existing)
                ? existing
                : [];
        var conditions = query.Conditions
            .Select(condition => new ResolvedCondition(
                condition,
                ResolveField(entity, condition.FieldName)))
            .ToArray();
        foreach (var condition in conditions)
        {
            records = records.Where(record => Matches(record, condition));
        }

        var matches = records
            .OrderBy(record => record.Id, MetaIdentity.Comparer)
            .ToArray();
        var selected = matches
            .Take(query.MaximumRecords)
            .Select(record => new RecordData(
                record.Id,
                record.Values,
                record.RelationshipIds))
            .ToArray();
        return ValueTask.FromResult(
            new RecordQueryResult(matches.LongLength, selected));
    }

    private GenericEntity RequireEntity(string entityName)
    {
        var name = MetaName.Require(entityName, "Entity name.");
        return _workspace.Model.FindEntity(name) ??
               throw new InvalidOperationException(
                   $"Entity '{name}' does not exist.");
    }

    private static string ResolveField(
        GenericEntity entity,
        string fieldName)
    {
        if (MetaName.Comparer.Equals(fieldName, "Id"))
        {
            return "Id";
        }

        var property = entity.Properties.FirstOrDefault(candidate =>
            MetaName.Comparer.Equals(candidate.Name, fieldName));
        if (property != null)
        {
            return property.Name;
        }

        var relationship = entity.Relationships.FirstOrDefault(candidate =>
            MetaName.Comparer.Equals(
                candidate.GetRoleOrDefault(),
                fieldName) ||
            MetaName.Comparer.Equals(candidate.GetColumnName(), fieldName));
        return relationship?.GetColumnName() ??
               throw new InvalidOperationException(
                   $"Field '{fieldName}' does not exist on entity '{entity.Name}'.");
    }

    private static bool Matches(
        GenericRecord record,
        ResolvedCondition resolved)
    {
        var fieldValue = MetaName.Comparer.Equals(
            resolved.FieldName,
            "Id")
            ? record.Id
            : record.Values.TryGetValue(resolved.FieldName, out var value)
                ? value
                : record.RelationshipIds.TryGetValue(
                    resolved.FieldName,
                    out var relationshipId)
                    ? relationshipId
                    : string.Empty;

        return resolved.Condition switch
        {
            RecordCondition.Equal equal => string.Equals(
                fieldValue,
                equal.Value,
                StringComparison.OrdinalIgnoreCase),
            RecordCondition.Contains contains => fieldValue.Contains(
                contains.Value,
                StringComparison.OrdinalIgnoreCase),
            _ => throw new InvalidOperationException(
                $"Unsupported record condition '{resolved.Condition.GetType().Name}'."),
        };
    }

    private sealed record ResolvedCondition(
        RecordCondition Condition,
        string FieldName);
}
