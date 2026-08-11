namespace Meta.Operations;

public static class WorkspaceSynchronization
{
    public static IReadOnlyList<Operation> PlanCreation(
        InMemoryWorkspace desired,
        string destinationModelName)
    {
        ArgumentNullException.ThrowIfNull(desired);
        var modelName = MetaName.Require(
            destinationModelName,
            "Destination model name.");
        EnsureValid(desired, "Desired metadata is invalid.");
        if (!MetaName.Comparer.Equals(desired.Model.Name, modelName))
        {
            throw new InvalidOperationException(
                $"Destination model '{modelName}' does not match workspace model '{desired.Model.Name}'.");
        }

        var operations = new List<Operation>();
        operations.AddRange(desired.Model.Entities.Select(
            entity => new Operation.AddEntity(entity.Name)));
        operations.AddRange(desired.Model.Entities.SelectMany(
            entity => entity.Properties.Select(
                property => new Operation.AddProperty(
                    entity.Name,
                    property.Name,
                    !property.IsNullable))));
        operations.AddRange(desired.Model.Entities.SelectMany(
            entity => entity.Relationships.Select(
                relationship => new Operation.AddRelationship(
                    entity.Name,
                    relationship.Entity,
                    relationship.Role,
                    !relationship.IsNullable))));

        var empty = new InMemoryWorkspace(
            desired.Model.Clone(),
            new GenericInstance { ModelName = desired.Model.Name });
        operations.AddRange(PlanInstanceChanges(empty, desired));
        return operations;
    }

    public static IReadOnlyList<Operation> PlanInstanceChanges(
        InMemoryWorkspace current,
        InMemoryWorkspace desired)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(desired);

        EnsureValid(current, "Current metadata is invalid.");
        EnsureValid(desired, "Desired metadata is invalid.");
        EnsureSameModel(current, desired);

        var operations = new List<Operation>();
        var currentRecords = IndexRecords(current);
        var desiredRecords = IndexRecords(desired);

        AddIdentitySpellingChanges(
            current,
            desiredRecords,
            operations);
        AddNewRecords(
            desired,
            currentRecords,
            desiredRecords,
            operations);
        AddExistingRecordChanges(
            current,
            currentRecords,
            desiredRecords,
            operations);
        AddNewOptionalRelationships(
            desired,
            currentRecords,
            desiredRecords,
            operations);
        AddRemovedOptionalRelationshipClears(
            current,
            desiredRecords,
            operations);
        AddRemovedRecords(
            current,
            currentRecords,
            desiredRecords,
            operations);

        var result = InMemoryOperations.ApplyBatch(current, operations);
        var difference = InMemoryWorkspaceComparer.FindDifference(
            result,
            desired);
        if (difference != null)
        {
            throw new InvalidOperationException(
                $"Instance synchronization did not produce the desired metadata. {difference}");
        }

        return operations;
    }

    private static void AddIdentitySpellingChanges(
        InMemoryWorkspace current,
        IReadOnlyDictionary<RecordKey, GenericRecord> desiredRecords,
        ICollection<Operation> operations)
    {
        foreach (var entity in current.Model.Entities)
        {
            foreach (var currentRecord in GetRecords(current.Instance, entity.Name))
            {
                var key = new RecordKey(entity.Name, currentRecord.Id);
                if (desiredRecords.TryGetValue(key, out var desiredRecord) &&
                    !string.Equals(
                        currentRecord.Id,
                        desiredRecord.Id,
                        StringComparison.Ordinal))
                {
                    operations.Add(new Operation.RenameRecord(
                        entity.Name,
                        currentRecord.Id,
                        desiredRecord.Id));
                }
            }
        }
    }

    private static void AddNewRecords(
        InMemoryWorkspace desired,
        IReadOnlyDictionary<RecordKey, GenericRecord> currentRecords,
        IReadOnlyDictionary<RecordKey, GenericRecord> desiredRecords,
        ICollection<Operation> operations)
    {
        var visiting = new HashSet<RecordKey>(RecordKeyComparer.Instance);
        var added = new HashSet<RecordKey>(RecordKeyComparer.Instance);

        foreach (var entity in desired.Model.Entities)
        {
            foreach (var record in GetRecords(desired.Instance, entity.Name))
            {
                var key = new RecordKey(entity.Name, record.Id);
                if (!currentRecords.ContainsKey(key))
                {
                    Visit(entity, record);
                }
            }
        }

        void Visit(GenericEntity entity, GenericRecord record)
        {
            var key = new RecordKey(entity.Name, record.Id);
            if (currentRecords.ContainsKey(key) || added.Contains(key))
            {
                return;
            }

            if (!visiting.Add(key))
            {
                throw new InvalidOperationException(
                    $"Required relationship cycle includes '{entity.Name}:{record.Id}'.");
            }

            var requiredRelationships = new Dictionary<string, string>(
                MetaName.Comparer);
            foreach (var relationship in entity.Relationships.Where(
                         item => !item.IsNullable))
            {
                var name = relationship.GetColumnName();
                if (!record.RelationshipIds.TryGetValue(name, out var targetId))
                {
                    throw new InvalidOperationException(
                        $"Record '{entity.Name}:{record.Id}' has no value for required relationship '{name}'.");
                }

                var targetKey = new RecordKey(relationship.Entity, targetId);
                if (!desiredRecords.TryGetValue(targetKey, out var targetRecord))
                {
                    throw new InvalidOperationException(
                        $"Record '{entity.Name}:{record.Id}' references missing record '{relationship.Entity}:{targetId}'.");
                }

                var targetEntity = desired.Model.FindEntity(relationship.Entity)!;
                Visit(targetEntity, targetRecord);
                requiredRelationships.Add(name, targetRecord.Id);
            }

            visiting.Remove(key);
            operations.Add(new Operation.InsertRecord(
                entity.Name,
                record.Id,
                record.Values,
                requiredRelationships));
            added.Add(key);
        }
    }

    private static void AddExistingRecordChanges(
        InMemoryWorkspace current,
        IReadOnlyDictionary<RecordKey, GenericRecord> currentRecords,
        IReadOnlyDictionary<RecordKey, GenericRecord> desiredRecords,
        ICollection<Operation> operations)
    {
        foreach (var entity in current.Model.Entities)
        {
            foreach (var currentRecord in GetRecords(current.Instance, entity.Name))
            {
                var key = new RecordKey(entity.Name, currentRecord.Id);
                if (!desiredRecords.TryGetValue(key, out var desiredRecord))
                {
                    continue;
                }

                foreach (var property in entity.Properties)
                {
                    var hasCurrent = currentRecord.Values.TryGetValue(
                        property.Name,
                        out var currentValue);
                    var hasDesired = desiredRecord.Values.TryGetValue(
                        property.Name,
                        out var desiredValue);
                    if (hasDesired &&
                        (!hasCurrent || !string.Equals(
                            currentValue,
                            desiredValue,
                            StringComparison.Ordinal)))
                    {
                        operations.Add(new Operation.SetProperty(
                            entity.Name,
                            desiredRecord.Id,
                            property.Name,
                            desiredValue!));
                    }
                    else if (hasCurrent && !hasDesired)
                    {
                        operations.Add(new Operation.ClearProperty(
                            entity.Name,
                            desiredRecord.Id,
                            property.Name));
                    }
                }

                foreach (var relationship in entity.Relationships)
                {
                    var name = relationship.GetColumnName();
                    var hasCurrent = currentRecord.RelationshipIds.TryGetValue(
                        name,
                        out var currentTargetId);
                    var hasDesired = desiredRecord.RelationshipIds.TryGetValue(
                        name,
                        out var desiredTargetId);
                    if (hasDesired &&
                        (!hasCurrent || !string.Equals(
                            currentTargetId,
                            desiredTargetId,
                            StringComparison.Ordinal)))
                    {
                        operations.Add(new Operation.SetRelationship(
                            entity.Name,
                            desiredRecord.Id,
                            name,
                            desiredTargetId!));
                    }
                    else if (hasCurrent && !hasDesired)
                    {
                        operations.Add(new Operation.ClearRelationship(
                            entity.Name,
                            desiredRecord.Id,
                            name));
                    }
                }
            }
        }
    }

    private static void AddNewOptionalRelationships(
        InMemoryWorkspace desired,
        IReadOnlyDictionary<RecordKey, GenericRecord> currentRecords,
        IReadOnlyDictionary<RecordKey, GenericRecord> desiredRecords,
        ICollection<Operation> operations)
    {
        foreach (var entity in desired.Model.Entities)
        {
            foreach (var record in GetRecords(desired.Instance, entity.Name))
            {
                if (currentRecords.ContainsKey(new RecordKey(entity.Name, record.Id)))
                {
                    continue;
                }

                foreach (var relationship in entity.Relationships.Where(
                             item => item.IsNullable))
                {
                    var name = relationship.GetColumnName();
                    if (!record.RelationshipIds.TryGetValue(name, out var targetId))
                    {
                        continue;
                    }

                    var target = desiredRecords[new RecordKey(
                        relationship.Entity,
                        targetId)];
                    operations.Add(new Operation.SetRelationship(
                        entity.Name,
                        record.Id,
                        name,
                        target.Id));
                }
            }
        }
    }

    private static void AddRemovedOptionalRelationshipClears(
        InMemoryWorkspace current,
        IReadOnlyDictionary<RecordKey, GenericRecord> desiredRecords,
        ICollection<Operation> operations)
    {
        foreach (var entity in current.Model.Entities)
        {
            foreach (var record in GetRecords(current.Instance, entity.Name))
            {
                if (desiredRecords.ContainsKey(new RecordKey(entity.Name, record.Id)))
                {
                    continue;
                }

                foreach (var relationship in entity.Relationships.Where(
                             item => item.IsNullable))
                {
                    var name = relationship.GetColumnName();
                    if (record.RelationshipIds.ContainsKey(name))
                    {
                        operations.Add(new Operation.ClearRelationship(
                            entity.Name,
                            record.Id,
                            name));
                    }
                }
            }
        }
    }

    private static void AddRemovedRecords(
        InMemoryWorkspace current,
        IReadOnlyDictionary<RecordKey, GenericRecord> currentRecords,
        IReadOnlyDictionary<RecordKey, GenericRecord> desiredRecords,
        ICollection<Operation> operations)
    {
        var removed = currentRecords
            .Where(item => !desiredRecords.ContainsKey(item.Key))
            .ToDictionary(item => item.Key, item => item.Value, RecordKeyComparer.Instance);
        var dependents = removed.Keys.ToDictionary(
            key => key,
            _ => new List<RecordKey>(),
            RecordKeyComparer.Instance);

        foreach (var item in removed)
        {
            var entity = current.Model.FindEntity(item.Key.EntityName)!;
            foreach (var relationship in entity.Relationships.Where(
                         candidate => !candidate.IsNullable))
            {
                var name = relationship.GetColumnName();
                if (!item.Value.RelationshipIds.TryGetValue(name, out var targetId))
                {
                    continue;
                }

                var targetKey = new RecordKey(relationship.Entity, targetId);
                if (dependents.TryGetValue(targetKey, out var targetDependents))
                {
                    targetDependents.Add(item.Key);
                }
            }
        }

        var visiting = new HashSet<RecordKey>(RecordKeyComparer.Instance);
        var deleted = new HashSet<RecordKey>(RecordKeyComparer.Instance);
        foreach (var key in removed.Keys)
        {
            Visit(key);
        }

        void Visit(RecordKey key)
        {
            if (deleted.Contains(key))
            {
                return;
            }

            if (!visiting.Add(key))
            {
                throw new InvalidOperationException(
                    $"Required relationship cycle includes '{key.EntityName}:{key.Id}'.");
            }

            foreach (var dependent in dependents[key])
            {
                Visit(dependent);
            }

            visiting.Remove(key);
            operations.Add(new Operation.DeleteRecord(key.EntityName, key.Id));
            deleted.Add(key);
        }
    }

    private static IReadOnlyDictionary<RecordKey, GenericRecord> IndexRecords(
        InMemoryWorkspace workspace)
    {
        var result = new Dictionary<RecordKey, GenericRecord>(
            RecordKeyComparer.Instance);
        foreach (var entity in workspace.Model.Entities)
        {
            foreach (var record in GetRecords(workspace.Instance, entity.Name))
            {
                result.Add(new RecordKey(entity.Name, record.Id), record);
            }
        }

        return result;
    }

    private static IReadOnlyCollection<GenericRecord> GetRecords(
        GenericInstance instance,
        string entityName) =>
        instance.RecordsByEntity.TryGetValue(entityName, out var records)
            ? records
            : [];

    private static void EnsureSameModel(
        InMemoryWorkspace current,
        InMemoryWorkspace desired)
    {
        var currentModel = new InMemoryWorkspace(
            current.Model.Clone(),
            new GenericInstance { ModelName = current.Model.Name });
        var desiredModel = new InMemoryWorkspace(
            desired.Model.Clone(),
            new GenericInstance { ModelName = desired.Model.Name });
        var difference = InMemoryWorkspaceComparer.FindDifference(
            currentModel,
            desiredModel);
        if (difference != null)
        {
            throw new InvalidOperationException(
                $"Instance synchronization requires the same model. {difference}");
        }
    }

    private static void EnsureValid(
        InMemoryWorkspace workspace,
        string message)
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
            .Select(issue => $"{issue.Code} {issue.Location} - {issue.Message}");
        throw new InvalidOperationException(
            message + " " + string.Join(" | ", errors));
    }

    private sealed record RecordKey(string EntityName, string Id);

    private sealed class RecordKeyComparer : IEqualityComparer<RecordKey>
    {
        public static RecordKeyComparer Instance { get; } = new();

        public bool Equals(RecordKey? left, RecordKey? right) =>
            ReferenceEquals(left, right) ||
            left != null &&
            right != null &&
            MetaName.Comparer.Equals(left.EntityName, right.EntityName) &&
            MetaIdentity.Comparer.Equals(left.Id, right.Id);

        public int GetHashCode(RecordKey value) =>
            HashCode.Combine(
                MetaName.Comparer.GetHashCode(value.EntityName),
                MetaIdentity.Comparer.GetHashCode(value.Id));
    }
}
