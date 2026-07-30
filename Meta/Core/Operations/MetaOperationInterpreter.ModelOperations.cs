using Meta.Core.Domain;

namespace Meta.Core.Operations;

public sealed partial class MetaOperationInterpreter
{
    private static void ApplyAddEntity(
        GenericMetadataState state,
        AddEntityOperation operation)
    {
        var entityName = RequireName(operation.EntityName, nameof(operation.EntityName));
        if (state.Model.FindEntity(entityName) != null)
        {
            throw new InvalidOperationException($"Entity '{entityName}' already exists.");
        }

        state.Model.Entities.Add(new GenericEntity
        {
            Name = entityName,
        });
        state.Instance.GetOrCreateEntityRecords(entityName);
    }

    private static void ApplyRemoveEntity(
        GenericMetadataState state,
        RemoveEntityOperation operation)
    {
        var entity = RequireEntity(state.Model, operation.EntityName);
        var rows = state.Instance.GetOrCreateEntityRecords(entity.Name);
        if (rows.Count > 0)
        {
            throw new InvalidOperationException(
                $"Entity '{entity.Name}' has records and cannot be removed.");
        }

        var inbound = state.Model.Entities.FirstOrDefault(candidate =>
            !ReferenceEquals(candidate, entity) &&
            candidate.Relationships.Any(relationship =>
                string.Equals(
                    relationship.Entity,
                    entity.Name,
                    StringComparison.OrdinalIgnoreCase)));
        if (inbound != null)
        {
            throw new InvalidOperationException(
                $"Entity '{entity.Name}' is referenced by entity '{inbound.Name}' and cannot be removed.");
        }

        state.Model.Entities.Remove(entity);
        state.Instance.RecordsByEntity.Remove(entity.Name);
    }

    private static void ApplyAddProperty(
        GenericMetadataState state,
        AddPropertyOperation operation)
    {
        var entity = RequireEntity(state.Model, operation.EntityName);
        var propertyName = RequireName(
            operation.PropertyName,
            nameof(operation.PropertyName));
        EnsureMemberNameAvailable(entity, propertyName);

        var records = state.Instance.GetOrCreateEntityRecords(entity.Name);
        if (operation.IsRequired &&
            records.Count > 0 &&
            operation.ExistingRecordValue == null)
        {
            throw new ExistingRecordsRequirePropertyValueException(
                entity.Name,
                propertyName);
        }

        entity.Properties.Add(new GenericProperty
        {
            Name = propertyName,
            IsNullable = !operation.IsRequired,
        });

        if (operation.ExistingRecordValue == null)
        {
            return;
        }

        foreach (var record in records)
        {
            record.Values.Add(propertyName, operation.ExistingRecordValue);
        }
    }

    private static void ApplyRemoveProperty(
        GenericMetadataState state,
        RemovePropertyOperation operation)
    {
        var entity = RequireEntity(state.Model, operation.EntityName);
        var property = RequireProperty(entity, operation.PropertyName);
        entity.Properties.Remove(property);

        if (!state.Instance.RecordsByEntity.TryGetValue(
                entity.Name,
                out var records))
        {
            return;
        }

        foreach (var record in records)
        {
            record.Values.Remove(property.Name);
        }
    }

    private static void ApplyRenameProperty(
        GenericMetadataState state,
        RenamePropertyOperation operation)
    {
        var entity = RequireEntity(state.Model, operation.EntityName);
        var propertyName = RequireName(operation.PropertyName, nameof(operation.PropertyName));
        var newPropertyName = RequireName(operation.NewPropertyName, nameof(operation.NewPropertyName));
        var property = RequireProperty(entity, propertyName);

        if (string.Equals(propertyName, "Id", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(newPropertyName, "Id", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Property 'Id' is implicit and cannot be renamed.");
        }

        if (string.Equals(
                property.Name,
                newPropertyName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Property '{entity.Name}.{property.Name}' already has that name.");
        }

        if (entity.Properties.Any(candidate =>
                !ReferenceEquals(candidate, property) &&
                string.Equals(candidate.Name, newPropertyName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Property '{entity.Name}.{newPropertyName}' already exists.");
        }

        if (entity.Relationships.Any(relationship =>
                string.Equals(
                    relationship.GetColumnName(),
                    newPropertyName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Property '{entity.Name}.{newPropertyName}' conflicts with a relationship.");
        }

        property.Name = newPropertyName;
        if (!state.Instance.RecordsByEntity.TryGetValue(entity.Name, out var records))
        {
            return;
        }

        foreach (var record in records)
        {
            if (!record.Values.TryGetValue(propertyName, out var value))
            {
                continue;
            }

            record.Values.Remove(propertyName);
            record.Values.Add(newPropertyName, value);
        }
    }

    private static void ApplySetPropertyRequired(
        GenericMetadataState state,
        SetPropertyRequiredOperation operation)
    {
        var entity = RequireEntity(state.Model, operation.EntityName);
        var property = RequireProperty(entity, operation.PropertyName);

        if (!operation.IsRequired && operation.MissingRecordValue != null)
        {
            throw new InvalidOperationException(
                "A value for missing records is only valid when making a property required.");
        }

        if (operation.IsRequired)
        {
            var records = state.Instance.GetOrCreateEntityRecords(entity.Name);
            var missing = records
                .Where(record =>
                    !record.Values.TryGetValue(property.Name, out var value) ||
                    value == null)
                .ToList();
            if (missing.Count > 0 && operation.MissingRecordValue == null)
            {
                throw new ExistingRecordsRequirePropertyValueException(
                    entity.Name,
                    property.Name);
            }

            if (operation.MissingRecordValue != null)
            {
                foreach (var record in missing)
                {
                    record.Values[property.Name] =
                        operation.MissingRecordValue;
                }
            }
        }

        property.IsNullable = !operation.IsRequired;
    }

    private static void ApplyAddRelationship(
        GenericMetadataState state,
        AddRelationshipOperation operation)
    {
        var sourceEntity = RequireEntity(
            state.Model,
            operation.SourceEntityName);
        var targetEntity = RequireEntity(
            state.Model,
            operation.TargetEntityName);
        var role = RequireOptionalName(operation.Role, nameof(operation.Role));
        var relationship = new GenericRelationship
        {
            Entity = targetEntity.Name,
            Role = role,
            IsNullable = !operation.IsRequired,
        };
        var relationshipName = relationship.GetColumnName();
        EnsureMemberNameAvailable(sourceEntity, relationshipName);

        string? targetId = null;
        if (operation.ExistingRecordTargetId != null)
        {
            var suppliedTargetId = RequireIdentity(
                operation.ExistingRecordTargetId,
                nameof(operation.ExistingRecordTargetId));
            var target = FindRecord(
                             state.Instance.GetOrCreateEntityRecords(
                                 targetEntity.Name),
                             suppliedTargetId)
                         ?? throw new InvalidOperationException(
                             $"Relationship target '{targetEntity.Name}.{suppliedTargetId}' does not exist.");
            targetId = target.Id;
        }

        var sourceRecords = state.Instance.GetOrCreateEntityRecords(
            sourceEntity.Name);
        if (operation.IsRequired &&
            sourceRecords.Count > 0 &&
            targetId == null)
        {
            throw new ExistingRecordsRequireRelationshipTargetException(
                sourceEntity.Name,
                relationshipName);
        }

        sourceEntity.Relationships.Add(relationship);
        if (targetId == null)
        {
            return;
        }

        foreach (var record in sourceRecords)
        {
            record.RelationshipIds.Add(relationshipName, targetId);
        }
    }

    private static void ApplyRemoveRelationship(
        GenericMetadataState state,
        RemoveRelationshipOperation operation)
    {
        var sourceEntity = RequireEntity(
            state.Model,
            operation.SourceEntityName);
        var relationship = ResolveRelationship(
            sourceEntity,
            operation.RelationshipName);
        var relationshipName = relationship.GetColumnName();
        sourceEntity.Relationships.Remove(relationship);

        if (!state.Instance.RecordsByEntity.TryGetValue(
                sourceEntity.Name,
                out var records))
        {
            return;
        }

        foreach (var record in records)
        {
            record.RelationshipIds.Remove(relationshipName);
        }
    }
}
