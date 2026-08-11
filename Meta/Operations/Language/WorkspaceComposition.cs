
namespace Meta.Operations;

public static class WorkspaceComposition
{
    public static async Task<GenericModel> MaterializeModelAsync(
        IMetaWorkspaceSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var modelName = MetaName.Require(
            await source.ReadModelNameAsync(cancellationToken)
                .ConfigureAwait(false),
            "Model name.");
        var workspace = new InMemoryWorkspace(
            new GenericModel { Name = modelName },
            new GenericInstance { ModelName = modelName });
        var target = new InMemoryOperationTarget(workspace);
        var entityNames = new List<string>();
        await foreach (var entityName in source.ReadEntityNamesAsync(cancellationToken))
        {
            entityNames.Add(MetaName.Require(entityName, "Entity name."));
        }

        foreach (var entityName in entityNames)
        {
            Apply(new Operation.AddEntity(entityName), target);
        }

        foreach (var entityName in entityNames)
        {
            await foreach (var property in source.ReadPropertiesAsync(entityName, cancellationToken))
            {
                Apply(new Operation.AddProperty(entityName, property.Name, property.IsRequired), target);
            }
        }

        foreach (var entityName in entityNames)
        {
            await foreach (var relationship in source.ReadRelationshipsAsync(entityName, cancellationToken))
            {
                Apply(
                    new Operation.AddRelationship(
                        entityName,
                        relationship.TargetEntityName,
                        relationship.Role,
                        relationship.IsRequired),
                    target);
            }
        }

        EnsureValid(workspace);
        return workspace.Model;
    }

    public static async Task<InMemoryWorkspace> MaterializeAsync(
        IMetaWorkspaceSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var sourceData = await ReadAsync(source, cancellationToken)
            .ConfigureAwait(false);
        return Compose(sourceData.ModelName, [sourceData]);
    }

    public static async Task<InMemoryWorkspace> MergeAsync(
        string modelName,
        IReadOnlyList<IMetaWorkspaceSource> sources,
        CancellationToken cancellationToken = default)
    {
        var name = MetaName.Require(modelName, "Model name.");
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new InvalidOperationException(
                "Workspace merge requires at least one source.");
        }

        var sourceData = new List<WorkspaceData>(sources.Count);
        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            sourceData.Add(await ReadAsync(source, cancellationToken)
                .ConfigureAwait(false));
        }

        return Compose(name, sourceData);
    }

    private static async Task<WorkspaceData> ReadAsync(
        IMetaWorkspaceSource source,
        CancellationToken cancellationToken)
    {
        var modelName = MetaName.Require(
            await source.ReadModelNameAsync(cancellationToken)
                .ConfigureAwait(false),
            "Model name.");
        var entityNames = new List<string>();
        await foreach (var entityName in source
                           .ReadEntityNamesAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            entityNames.Add(MetaName.Require(entityName, "Entity name."));
        }

        var entities = new List<EntityData>(entityNames.Count);
        foreach (var entityName in entityNames)
        {
            var properties = new List<PropertyDefinition>();
            await foreach (var property in source
                               .ReadPropertiesAsync(entityName, cancellationToken)
                               .ConfigureAwait(false))
            {
                properties.Add(property);
            }

            var relationships = new List<RelationshipDefinition>();
            await foreach (var relationship in source
                               .ReadRelationshipsAsync(entityName, cancellationToken)
                               .ConfigureAwait(false))
            {
                relationships.Add(relationship);
            }

            var records = new List<RecordData>();
            await foreach (var record in source
                               .ReadRecordsAsync(entityName, cancellationToken)
                               .ConfigureAwait(false))
            {
                records.Add(record);
            }

            entities.Add(new EntityData(
                entityName,
                properties,
                relationships,
                records));
        }

        return new WorkspaceData(modelName, entities);
    }

    private static InMemoryWorkspace Compose(
        string modelName,
        IReadOnlyList<WorkspaceData> sources)
    {
        var workspace = new InMemoryWorkspace(
            new GenericModel { Name = modelName },
            new GenericInstance { ModelName = modelName });
        var target = new InMemoryOperationTarget(workspace);

        foreach (var entity in sources.SelectMany(source => source.Entities))
        {
            Apply(new Operation.AddEntity(entity.Name), target);
        }

        foreach (var entity in sources.SelectMany(source => source.Entities))
        {
            foreach (var property in entity.Properties)
            {
                Apply(
                    new Operation.AddProperty(
                        entity.Name,
                        property.Name,
                        property.IsRequired),
                    target);
            }
        }

        foreach (var entity in sources.SelectMany(source => source.Entities))
        {
            foreach (var relationship in entity.Relationships)
            {
                Apply(
                    new Operation.AddRelationship(
                        entity.Name,
                        relationship.TargetEntityName,
                        relationship.Role,
                        relationship.IsRequired),
                    target);
            }
        }

        var records = new Dictionary<string, List<RecordData>>(
            MetaName.Comparer);
        foreach (var entity in sources.SelectMany(source => source.Entities))
        {
            records.Add(entity.Name, entity.Records.ToList());
        }

        foreach (var record in OrderRecords(workspace.Model, records))
        {
            var entity = workspace.Model.FindEntity(record.EntityName)!;
            var requiredRelationships = new Dictionary<string, string>(
                MetaName.Comparer);
            foreach (var relationship in entity.Relationships
                         .Where(item => !item.IsNullable))
            {
                var name = relationship.GetColumnName();
                if (record.Data.RelationshipIds.TryGetValue(
                        name,
                        out var targetId))
                {
                    requiredRelationships.Add(name, targetId);
                }
            }

            Apply(
                new Operation.InsertRecord(
                    record.EntityName,
                    record.Data.Id,
                    record.Data.Values,
                    requiredRelationships),
                target);
        }

        foreach (var entityData in sources.SelectMany(source => source.Entities))
        {
            var entity = workspace.Model.FindEntity(entityData.Name)!;
            foreach (var record in entityData.Records)
            {
                foreach (var relationship in entity.Relationships
                             .Where(item => item.IsNullable))
                {
                    var name = relationship.GetColumnName();
                    if (record.RelationshipIds.TryGetValue(
                            name,
                            out var targetId))
                    {
                        Apply(
                            new Operation.SetRelationship(
                                entity.Name,
                                record.Id,
                                name,
                                targetId),
                            target);
                    }
                }
            }
        }

        EnsureValid(workspace);
        return workspace;
    }

    private static IReadOnlyList<EntityRecord> OrderRecords(
        GenericModel model,
        IReadOnlyDictionary<string, List<RecordData>> records)
    {
        var byEntity = records.ToDictionary(
            item => item.Key,
            item => item.Value.ToDictionary(
                record => record.Id,
                MetaIdentity.Comparer),
            MetaName.Comparer);
        var result = new List<EntityRecord>();
        var visiting = new HashSet<RecordKey>(RecordKeyComparer.Instance);
        var visited = new HashSet<RecordKey>(RecordKeyComparer.Instance);

        foreach (var entity in model.Entities)
        {
            foreach (var record in records[entity.Name])
            {
                Visit(entity, record);
            }
        }

        return result;

        void Visit(GenericEntity entity, RecordData record)
        {
            var key = new RecordKey(entity.Name, record.Id);
            if (visited.Contains(key))
            {
                return;
            }

            if (!visiting.Add(key))
            {
                throw new InvalidOperationException(
                    $"Required relationship cycle includes '{entity.Name}:{record.Id}'.");
            }

            foreach (var relationship in entity.Relationships
                         .Where(item => !item.IsNullable))
            {
                var relationshipName = relationship.GetColumnName();
                if (!record.RelationshipIds.TryGetValue(
                        relationshipName,
                        out var targetId))
                {
                    throw new InvalidOperationException(
                        $"Record '{entity.Name}:{record.Id}' has no value for required relationship '{relationshipName}'.");
                }

                if (!byEntity.TryGetValue(
                        relationship.Entity,
                        out var targetRecords) ||
                    !targetRecords.TryGetValue(targetId, out var targetRecord))
                {
                    throw new InvalidOperationException(
                        $"Record '{entity.Name}:{record.Id}' references missing record '{relationship.Entity}:{targetId}'.");
                }

                Visit(model.FindEntity(relationship.Entity)!, targetRecord);
            }

            visiting.Remove(key);
            visited.Add(key);
            result.Add(new EntityRecord(entity.Name, record));
        }
    }

    private static void Apply(
        Operation operation,
        IOperationTarget target)
    {
        try
        {
            operation.ApplyTo(target);
        }
        catch (MetaOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new MetaOperationException(0, operation, exception);
        }
    }

    private static void EnsureValid(InMemoryWorkspace workspace)
    {
        var diagnostics = WorkspaceValidator.Validate(
            workspace.Model,
            workspace.Instance);
        if (!diagnostics.HasErrors)
        {
            return;
        }

        var errors = diagnostics.Issues
            .Where(issue => issue.Severity == IssueSeverity.Error)
            .Take(5)
            .Select(issue =>
                $"{issue.Code} {issue.Location} - {issue.Message}");
        throw new InvalidOperationException(
            "Cannot materialize invalid metadata. " +
            string.Join(" | ", errors));
    }

    private sealed record EntityRecord(
        string EntityName,
        RecordData Data);

    private sealed record WorkspaceData(
        string ModelName,
        IReadOnlyList<EntityData> Entities);

    private sealed record EntityData(
        string Name,
        IReadOnlyList<PropertyDefinition> Properties,
        IReadOnlyList<RelationshipDefinition> Relationships,
        IReadOnlyList<RecordData> Records);

    private sealed record RecordKey(
        string EntityName,
        string Id);

    private sealed class RecordKeyComparer : IEqualityComparer<RecordKey>
    {
        public static RecordKeyComparer Instance { get; } = new();

        public bool Equals(RecordKey? left, RecordKey? right)
        {
            return ReferenceEquals(left, right) ||
                   left != null &&
                   right != null &&
                   MetaName.Comparer.Equals(
                       left.EntityName,
                       right.EntityName) &&
                   MetaIdentity.Comparer.Equals(left.Id, right.Id);
        }

        public int GetHashCode(RecordKey value)
        {
            return HashCode.Combine(
                MetaName.Comparer.GetHashCode(value.EntityName),
                MetaIdentity.Comparer.GetHashCode(value.Id));
        }
    }
}
