
namespace Meta.Operations;

internal sealed partial class InMemoryOperationTarget
{
    internal void Apply(
        InMemoryWorkspace state,
        Operation.InsertRecord operation)
    {
        var entity = RequireEntity(state, operation.EntityName);
        var id = MetaIdentity.Require(operation.Id, "Record Id.");
        var records = state.Instance.GetOrCreateEntityRecords(entity.Name);
        var index = GetRecordIndex(state, entity.Name);
        if (index.ContainsKey(id))
        {
            throw new InvalidOperationException(
                $"Record '{entity.Name}:{id}' already exists.");
        }

        var record = new GenericRecord
        {
            Id = id,
        };

        foreach (var item in operation.Values)
        {
            var property = RequireProperty(entity, item.Key);
            record.Values.Add(property.Name, item.Value);
        }

        foreach (var item in operation.RelationshipIds)
        {
            var relationship = RequireRelationship(entity, item.Key);
            var targetEntity = RequireEntity(
                state,
                relationship.Entity);
            var target = RequireRecord(state, targetEntity, item.Value);
            record.RelationshipIds.Add(
                relationship.GetColumnName(),
                target.Id);
        }

        records.Add(record);
        index.Add(record.Id, record);
    }

    internal void Apply(
        InMemoryWorkspace state,
        Operation.DeleteRecord operation)
    {
        var entity = RequireEntity(state, operation.EntityName);
        var record = RequireRecord(state, entity, operation.Id);
        GetRecords(state, entity.Name).Remove(record);
        GetRecordIndex(state, entity.Name).Remove(record.Id);
    }

    internal RenameRecordResult Apply(
        InMemoryWorkspace state,
        Operation.RenameRecord operation)
    {
        var entity = RequireEntity(state, operation.EntityName);
        var record = RequireRecord(state, entity, operation.Id);
        var newId = MetaIdentity.Require(operation.NewId, "New record Id.");
        var index = GetRecordIndex(state, entity.Name);
        if (index.TryGetValue(newId, out var collision) &&
            !ReferenceEquals(collision, record))
        {
            throw new InvalidOperationException(
                $"Record '{entity.Name}:{newId}' already exists.");
        }

        var oldId = record.Id;
        var relationshipValueCount = 0L;
        record.Id = newId;
        index.Remove(oldId);
        index.Add(newId, record);
        foreach (var sourceEntity in state.Model.Entities)
        {
            if (!state.Instance.RecordsByEntity.TryGetValue(
                    sourceEntity.Name,
                    out var sourceRecords))
            {
                continue;
            }

            foreach (var relationship in sourceEntity.Relationships.Where(
                         relationship =>
                             MetaName.Comparer.Equals(
                                 relationship.Entity,
                                 entity.Name)))
            {
                var name = relationship.GetColumnName();
                foreach (var source in sourceRecords)
                {
                    if (source.RelationshipIds.TryGetValue(
                            name,
                            out var targetId) &&
                        MetaIdentity.Comparer.Equals(targetId, oldId))
                    {
                        source.RelationshipIds[name] = newId;
                        relationshipValueCount++;
                    }
                }
            }
        }

        return new RenameRecordResult(
            entity.Name,
            oldId,
            newId,
            relationshipValueCount);
    }

    internal void Apply(
        InMemoryWorkspace state,
        Operation.SetProperty operation)
    {
        var entity = RequireEntity(state, operation.EntityName);
        var record = RequireRecord(state, entity, operation.Id);
        var property = RequireProperty(entity, operation.PropertyName);
        record.Values[property.Name] = operation.Value ??
            throw new InvalidOperationException("Property value is required.");
    }

    internal void Apply(
        InMemoryWorkspace state,
        Operation.ClearProperty operation)
    {
        var entity = RequireEntity(state, operation.EntityName);
        var record = RequireRecord(state, entity, operation.Id);
        var property = RequireProperty(entity, operation.PropertyName);
        if (!property.IsNullable)
        {
            throw new InvalidOperationException(
                $"Required property '{entity.Name}.{property.Name}' cannot be cleared.");
        }

        record.Values.Remove(property.Name);
    }

    internal void Apply(
        InMemoryWorkspace state,
        Operation.SetRelationship operation)
    {
        var entity = RequireEntity(state, operation.EntityName);
        var record = RequireRecord(state, entity, operation.Id);
        var relationship = RequireRelationship(
            entity,
            operation.RelationshipName);
        var targetEntity = RequireEntity(state, relationship.Entity);
        var target = RequireRecord(state, targetEntity, operation.TargetId);
        record.RelationshipIds[relationship.GetColumnName()] = target.Id;
    }

    internal void Apply(
        InMemoryWorkspace state,
        Operation.ClearRelationship operation)
    {
        var entity = RequireEntity(state, operation.EntityName);
        var record = RequireRecord(state, entity, operation.Id);
        var relationship = RequireRelationship(
            entity,
            operation.RelationshipName);
        if (!relationship.IsNullable)
        {
            throw new InvalidOperationException(
                $"Required relationship '{entity.Name}.{relationship.GetColumnName()}' cannot be cleared.");
        }

        record.RelationshipIds.Remove(relationship.GetColumnName());
    }
}
