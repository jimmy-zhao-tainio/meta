
namespace Meta.Operations;

internal sealed partial class InMemoryOperationTarget
{
    internal RenameModelResult Apply(
        InMemoryWorkspace state,
        Operation.RenameModel operation)
    {
        var expectedName = MetaName.Require(operation.Name, "Model name.");
        if (!MetaName.Comparer.Equals(state.Model.Name, expectedName))
        {
            throw new InvalidOperationException(
                $"Workspace model is '{state.Model.Name}', not '{expectedName}'.");
        }

        var name = MetaName.Require(operation.NewName, "New model name.");
        var oldName = state.Model.Name;
        state.Model.Name = name;
        state.Instance.ModelName = name;
        return new RenameModelResult(oldName, name);
    }

    internal void Apply(
        InMemoryWorkspace state,
        Operation.AddEntity operation)
    {
        var name = MetaName.Require(operation.Name, "Entity name.");
        if (state.Model.FindEntity(name) != null)
        {
            throw new InvalidOperationException(
                $"Entity '{name}' already exists.");
        }

        state.Model.Entities.Add(new GenericEntity
        {
            Name = name,
        });
    }

    internal void Apply(
        InMemoryWorkspace state,
        Operation.RemoveEntity operation)
    {
        var entity = RequireEntity(state, operation.Name);
        if (state.Instance.RecordsByEntity.TryGetValue(
                entity.Name,
                out var records) &&
            records.Count > 0)
        {
            throw new InvalidOperationException(
                $"Entity '{entity.Name}' has records and cannot be removed.");
        }

        var inbound = state.Model.Entities.FirstOrDefault(candidate =>
            !ReferenceEquals(candidate, entity) &&
            candidate.Relationships.Any(relationship =>
                MetaName.Comparer.Equals(
                    relationship.Entity,
                    entity.Name)));
        if (inbound != null)
        {
            throw new InvalidOperationException(
                $"Entity '{entity.Name}' is referenced by entity '{inbound.Name}' and cannot be removed.");
        }

        state.Model.Entities.Remove(entity);
        state.Instance.RecordsByEntity.Remove(entity.Name);
    }

    internal RenameEntityResult Apply(
        InMemoryWorkspace state,
        Operation.RenameEntity operation)
    {
        var entity = RequireEntity(state, operation.Name);
        var newName = MetaName.Require(
            operation.NewName,
            "New entity name.");
        var collision = state.Model.Entities.FirstOrDefault(candidate =>
            !ReferenceEquals(candidate, entity) &&
            MetaName.Comparer.Equals(candidate.Name, newName));
        if (collision != null)
        {
            throw new InvalidOperationException(
                $"Entity '{newName}' already exists.");
        }

        var oldName = entity.Name;
        var recordCount = state.Instance.RecordsByEntity.TryGetValue(
            oldName,
            out var records)
            ? records.Count
            : 0;
        var inboundRelationships = state.Model.Entities
            .SelectMany(sourceEntity => sourceEntity.Relationships
                .Where(relationship =>
                    MetaName.Comparer.Equals(
                        relationship.Entity,
                        oldName))
                .Select(relationship => (SourceEntity: sourceEntity, Relationship: relationship)))
            .ToList();
        foreach (var inbound in inboundRelationships)
        {
            EnsureRelationshipNameAvailable(
                inbound.SourceEntity,
                new GenericRelationship
                {
                    Entity = newName,
                    Role = inbound.Relationship.Role,
                    IsNullable = inbound.Relationship.IsNullable,
                },
                inbound.Relationship);
        }

        var relationshipValueCount = 0L;
        entity.Name = newName;
        if (state.Instance.RecordsByEntity.Remove(
                oldName,
                out var entityRecords))
        {
            state.Instance.RecordsByEntity.Add(newName, entityRecords);
        }

        foreach (var inbound in inboundRelationships)
        {
            var sourceEntity = inbound.SourceEntity;
            var relationship = inbound.Relationship;
            var oldUsageName = relationship.GetColumnName();
            relationship.Entity = newName;
            var newUsageName = relationship.GetColumnName();
            if (string.Equals(
                    oldUsageName,
                    newUsageName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!state.Instance.RecordsByEntity.TryGetValue(
                    sourceEntity.Name,
                    out var sourceRecords))
            {
                continue;
            }

            foreach (var record in sourceRecords)
            {
                if (!record.RelationshipIds.Remove(
                        oldUsageName,
                        out var targetId))
                {
                    continue;
                }

                record.RelationshipIds.Add(
                    newUsageName,
                    targetId);
                relationshipValueCount++;
            }
        }

        return new RenameEntityResult(
            oldName,
            newName,
            recordCount,
            inboundRelationships.Count,
            relationshipValueCount);
    }

    internal void Apply(
        InMemoryWorkspace state,
        Operation.AddProperty operation)
    {
        var entity = RequireEntity(state, operation.EntityName);
        var name = MetaName.Require(operation.Name, "Property name.");
        if (MetaName.Comparer.Equals(name, "Id"))
        {
            throw new InvalidOperationException(
                "Property 'Id' is implicit and cannot be added.");
        }

        EnsurePropertyNameAvailable(entity, name);
        var records = GetRecords(state, entity.Name);
        if (operation.IsRequired &&
            records.Count > 0 &&
            operation.ExistingRecordValue == null)
        {
            throw new InvalidOperationException(
                $"Property '{entity.Name}.{name}' requires a value for existing records.");
        }

        entity.Properties.Add(new GenericProperty
        {
            Name = name,
            IsNullable = !operation.IsRequired,
        });

        if (operation.ExistingRecordValue == null)
        {
            return;
        }

        foreach (var record in records)
        {
            record.Values.Add(name, operation.ExistingRecordValue);
        }
    }

    internal void Apply(
        InMemoryWorkspace state,
        Operation.RemoveProperty operation)
    {
        var entity = RequireEntity(state, operation.EntityName);
        var property = RequireProperty(entity, operation.Name);
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

    internal void Apply(
        InMemoryWorkspace state,
        Operation.RenameProperty operation)
    {
        var entity = RequireEntity(state, operation.EntityName);
        var property = RequireProperty(entity, operation.Name);
        var newName = MetaName.Require(
            operation.NewName,
            "New property name.");
        if (MetaName.Comparer.Equals(newName, "Id"))
        {
            throw new InvalidOperationException(
                "Property 'Id' is implicit and cannot be used.");
        }

        EnsurePropertyNameAvailable(entity, newName, property);
        var oldName = property.Name;
        property.Name = newName;

        if (!state.Instance.RecordsByEntity.TryGetValue(
                entity.Name,
                out var records))
        {
            return;
        }

        foreach (var record in records)
        {
            if (!record.Values.Remove(oldName, out var value))
            {
                continue;
            }

            record.Values.Add(newName, value);
        }
    }

    internal void Apply(
        InMemoryWorkspace state,
        Operation.SetPropertyRequired operation)
    {
        var entity = RequireEntity(state, operation.EntityName);
        var property = RequireProperty(entity, operation.Name);
        if (!operation.IsRequired &&
            operation.MissingRecordValue != null)
        {
            throw new InvalidOperationException(
                "A value for missing records is only valid when making a property required.");
        }

        if (operation.IsRequired)
        {
            var records = GetRecords(state, entity.Name);
            var missing = records
                .Where(record => !record.Values.ContainsKey(property.Name))
                .ToList();
            if (missing.Count > 0 &&
                operation.MissingRecordValue == null)
            {
                throw new InvalidOperationException(
                    $"Property '{entity.Name}.{property.Name}' requires a value for existing records.");
            }

            foreach (var record in missing)
            {
                record.Values.Add(
                    property.Name,
                    operation.MissingRecordValue!);
            }
        }

        property.IsNullable = !operation.IsRequired;
    }

    internal void Apply(
        InMemoryWorkspace state,
        Operation.AddRelationship operation)
    {
        var sourceEntity = RequireEntity(
            state,
            operation.SourceEntityName);
        var targetEntity = RequireEntity(
            state,
            operation.TargetEntityName);
        var role = string.IsNullOrEmpty(operation.Role)
            ? string.Empty
            : MetaName.Require(operation.Role, "Relationship role.");
        var relationship = new GenericRelationship
        {
            Entity = targetEntity.Name,
            Role = role,
            IsNullable = !operation.IsRequired,
        };
        var name = relationship.GetColumnName();
        if (!MetaName.IsValid(name))
        {
            throw new InvalidOperationException(
                $"Relationship name '{sourceEntity.Name}.{name}' is invalid.");
        }

        EnsureRelationshipNameAvailable(
            sourceEntity,
            relationship);

        string? targetId = null;
        if (operation.ExistingRecordTargetId != null)
        {
            targetId = RequireRecord(
                state,
                targetEntity,
                operation.ExistingRecordTargetId).Id;
        }

        var sourceRecords = GetRecords(state, sourceEntity.Name);
        if (operation.IsRequired &&
            sourceRecords.Count > 0 &&
            targetId == null)
        {
            throw new InvalidOperationException(
                $"Relationship '{sourceEntity.Name}.{name}' requires a target for existing records.");
        }

        sourceEntity.Relationships.Add(relationship);
        if (targetId == null)
        {
            return;
        }

        foreach (var record in sourceRecords)
        {
            record.RelationshipIds.Add(name, targetId);
        }
    }

    internal void Apply(
        InMemoryWorkspace state,
        Operation.RemoveRelationship operation)
    {
        var sourceEntity = RequireEntity(
            state,
            operation.SourceEntityName);
        var relationship = RequireRelationship(
            sourceEntity,
            operation.Name);
        var name = relationship.GetColumnName();
        sourceEntity.Relationships.Remove(relationship);

        if (!state.Instance.RecordsByEntity.TryGetValue(
                sourceEntity.Name,
                out var records))
        {
            return;
        }

        foreach (var record in records)
        {
            record.RelationshipIds.Remove(name);
        }
    }

    internal RenameRelationshipResult Apply(
        InMemoryWorkspace state,
        Operation.RenameRelationship operation)
    {
        var sourceEntity = RequireEntity(
            state,
            operation.SourceEntityName);
        var relationship = RequireRelationship(
            sourceEntity,
            operation.Name);
        var newRole = string.IsNullOrWhiteSpace(operation.NewRole) ||
                      MetaName.Comparer.Equals(
                          operation.NewRole.Trim(),
                          relationship.Entity)
            ? string.Empty
            : MetaName.Require(
                operation.NewRole.Trim(),
                "New relationship role.");
        if (MetaName.Comparer.Equals(
                relationship.Role ?? string.Empty,
                newRole))
        {
            throw new InvalidOperationException(
                $"Relationship '{sourceEntity.Name}.{relationship.GetColumnName()}' already uses the requested role.");
        }

        var candidate = new GenericRelationship
        {
            Entity = relationship.Entity,
            Role = newRole,
            IsNullable = relationship.IsNullable,
        };
        EnsureRelationshipNameAvailable(
            sourceEntity,
            candidate,
            relationship);

        var oldName = relationship.GetColumnName();
        var newName = candidate.GetColumnName();
        relationship.Role = newRole;
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return new RenameRelationshipResult(
                sourceEntity.Name,
                relationship.Entity,
                oldName,
                newName,
                0);
        }

        var relationshipValueCount = 0L;
        foreach (var record in GetRecords(state, sourceEntity.Name))
        {
            if (!record.RelationshipIds.Remove(oldName, out var targetId))
            {
                continue;
            }

            record.RelationshipIds.Add(newName, targetId);
            relationshipValueCount++;
        }

        return new RenameRelationshipResult(
            sourceEntity.Name,
            relationship.Entity,
            oldName,
            newName,
            relationshipValueCount);
    }

    internal void Apply(
        InMemoryWorkspace state,
        Operation.RetargetRelationship operation)
    {
        var sourceEntity = RequireEntity(
            state,
            operation.SourceEntityName);
        var relationship = RequireRelationship(
            sourceEntity,
            operation.Name);
        var targetEntity = RequireEntity(
            state,
            operation.TargetEntityName);
        var candidate = new GenericRelationship
        {
            Entity = targetEntity.Name,
            Role = relationship.Role,
            IsNullable = relationship.IsNullable,
        };
        EnsureRelationshipNameAvailable(
            sourceEntity,
            candidate,
            relationship);

        var oldName = relationship.GetColumnName();
        var newName = candidate.GetColumnName();
        var records = GetRecords(state, sourceEntity.Name);
        foreach (var record in records)
        {
            if (!record.RelationshipIds.TryGetValue(
                    oldName,
                    out var targetId))
            {
                continue;
            }

            RequireRecord(state, targetEntity, targetId);
        }

        relationship.Entity = targetEntity.Name;
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var record in records)
        {
            if (!record.RelationshipIds.Remove(oldName, out var targetId))
            {
                continue;
            }

            record.RelationshipIds.Add(newName, targetId);
        }
    }

    internal void Apply(
        InMemoryWorkspace state,
        Operation.SetRelationshipRequired operation)
    {
        var sourceEntity = RequireEntity(
            state,
            operation.SourceEntityName);
        var relationship = RequireRelationship(
            sourceEntity,
            operation.Name);
        if (!operation.IsRequired &&
            operation.MissingRecordTargetId != null)
        {
            throw new InvalidOperationException(
                "A target for missing records is only valid when making a relationship required.");
        }

        if (operation.IsRequired)
        {
            var relationshipName = relationship.GetColumnName();
            var missing = GetRecords(state, sourceEntity.Name)
                .Where(record =>
                    !record.RelationshipIds.ContainsKey(relationshipName))
                .ToList();
            if (missing.Count > 0 &&
                operation.MissingRecordTargetId == null)
            {
                throw new InvalidOperationException(
                    $"Relationship '{sourceEntity.Name}.{relationshipName}' requires a target for existing records.");
            }

            if (operation.MissingRecordTargetId != null)
            {
                var targetEntity = RequireEntity(
                    state,
                    relationship.Entity);
                var targetId = RequireRecord(
                    state,
                    targetEntity,
                    operation.MissingRecordTargetId).Id;
                foreach (var record in missing)
                {
                    record.RelationshipIds.Add(
                        relationshipName,
                        targetId);
                }
            }
        }

        relationship.IsNullable = !operation.IsRequired;
    }
}
