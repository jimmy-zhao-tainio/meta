using System;
using System.Collections.Generic;
using System.Linq;
using Meta.Core.Domain;

namespace Meta.Core.Operations;

public static class WorkspaceOperationApplier
{
    public static void Apply(Workspace workspace, WorkspaceOp operation)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        if (operation == null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        switch (operation.Type)
        {
            case WorkspaceOpTypes.AddEntity:
                AddEntity(workspace, operation);
                break;
            case WorkspaceOpTypes.DeleteEntity:
                DeleteEntity(workspace, operation);
                break;
            case WorkspaceOpTypes.RenameEntity:
                RenameEntity(workspace, operation);
                break;
            case WorkspaceOpTypes.AddProperty:
                AddProperty(workspace, operation);
                break;
            case WorkspaceOpTypes.DeleteProperty:
                DeleteProperty(workspace, operation);
                break;
            case WorkspaceOpTypes.RenameProperty:
                RenameProperty(workspace, operation);
                break;
            case WorkspaceOpTypes.ChangeNullability:
                ChangeNullability(workspace, operation);
                break;
            case WorkspaceOpTypes.AddRelationship:
                AddRelationship(workspace, operation);
                break;
            case WorkspaceOpTypes.DeleteRelationship:
                DeleteRelationship(workspace, operation);
                break;
            case WorkspaceOpTypes.BulkUpsertRows:
                BulkUpsertRows(workspace, operation);
                break;
            case WorkspaceOpTypes.DeleteRows:
                DeleteRows(workspace, operation);
                break;
            case WorkspaceOpTypes.TransformInstances:
                break;
            default:
                throw new InvalidOperationException($"Unsupported operation type '{operation.Type}'.");
        }

        workspace.IsDirty = true;
    }

    private static void AddEntity(Workspace workspace, WorkspaceOp operation)
    {
        var entityName = RequireValue(operation.EntityName, nameof(operation.EntityName));
        EnsureEntityDoesNotExist(workspace.Model, entityName);
        var entity = new GenericEntity
        {
            Name = entityName,
        };
        workspace.Model.Entities.Add(entity);
        workspace.Instance.GetOrCreateEntityRecords(entityName);
    }

    private static void DeleteEntity(Workspace workspace, WorkspaceOp operation)
    {
        var entityName = RequireValue(operation.EntityName, nameof(operation.EntityName));
        var entity = RequireEntity(workspace.Model, entityName);
        if (workspace.Instance.RecordsByEntity.TryGetValue(entityName, out var rows) && rows.Count > 0)
        {
            throw new InvalidOperationException($"Entity '{entityName}' has rows and cannot be removed.");
        }

        var hasInboundRelationships = workspace.Model.Entities.Any(modelEntity =>
            !string.Equals(modelEntity.Name, entityName, StringComparison.OrdinalIgnoreCase) &&
            modelEntity.Relationships.Any(relationship =>
                string.Equals(relationship.Entity, entityName, StringComparison.OrdinalIgnoreCase)));
        if (hasInboundRelationships)
        {
            throw new InvalidOperationException($"Entity '{entityName}' has inbound relationships and cannot be removed.");
        }

        var hasRelationshipUsage = workspace.Instance.RecordsByEntity.Any(entityRecords =>
        {
            var sourceEntity = workspace.Model.FindEntity(entityRecords.Key);
            if (sourceEntity == null)
            {
                return false;
            }

            var relationshipUsageNames = sourceEntity.Relationships
                .Where(relationship => string.Equals(relationship.Entity, entityName, StringComparison.OrdinalIgnoreCase))
                .Select(relationship => relationship.GetColumnName())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (relationshipUsageNames.Count == 0)
            {
                return false;
            }

            return entityRecords.Value.Any(record => relationshipUsageNames.Any(usageName =>
                record.RelationshipIds.TryGetValue(usageName, out var relationshipId) &&
                !string.IsNullOrWhiteSpace(relationshipId)));
        });
        if (hasRelationshipUsage)
        {
            throw new InvalidOperationException($"Entity '{entityName}' has relationship usage and cannot be removed.");
        }

        workspace.Model.Entities.Remove(entity);
        workspace.Instance.RecordsByEntity.Remove(entityName);
    }

    private static void RenameEntity(Workspace workspace, WorkspaceOp operation)
    {
        var entityName = RequireValue(operation.EntityName, nameof(operation.EntityName));
        var newEntityName = RequireValue(operation.NewEntityName, nameof(operation.NewEntityName));
        var entity = RequireEntity(workspace.Model, entityName);
        EnsureEntityDoesNotExist(workspace.Model, newEntityName);

        entity.Name = newEntityName;

        if (workspace.Instance.RecordsByEntity.TryGetValue(entityName, out var records))
        {
            workspace.Instance.RecordsByEntity.Remove(entityName);
            workspace.Instance.RecordsByEntity[newEntityName] = records;
        }

        foreach (var modelEntity in workspace.Model.Entities)
        {
            var modelEntityRecords = workspace.Instance.GetOrCreateEntityRecords(modelEntity.Name);
            foreach (var relationship in modelEntity.Relationships)
            {
                if (string.Equals(relationship.Entity, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    var previousUsageName = relationship.GetColumnName();
                    relationship.Entity = newEntityName;
                    var updatedUsageName = relationship.GetColumnName();
                    if (!string.Equals(previousUsageName, updatedUsageName, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var record in modelEntityRecords)
                        {
                            if (!record.RelationshipIds.TryGetValue(previousUsageName, out var relatedId))
                            {
                                continue;
                            }

                            record.RelationshipIds.Remove(previousUsageName);
                            record.RelationshipIds[updatedUsageName] = relatedId;
                        }
                    }
                }
            }
        }
    }

    private static void AddProperty(Workspace workspace, WorkspaceOp operation)
    {
        var entity = RequireEntity(workspace.Model, RequireValue(operation.EntityName, nameof(operation.EntityName)));
        if (operation.Property == null)
        {
            throw new InvalidOperationException("AddProperty operation requires Property.");
        }

        if (string.Equals(operation.Property.Name, "Id", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Property 'Id' is implicit and cannot be added explicitly.");
        }

        if (entity.Properties.Any(property =>
                string.Equals(property.Name, operation.Property.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Property '{entity.Name}.{operation.Property.Name}' already exists.");
        }

        var sourceRows = workspace.Instance.GetOrCreateEntityRecords(entity.Name);
        var defaultValueProvided = operation.PropertyDefaultValue != null;
        if (!operation.Property.IsNullable && sourceRows.Count > 0 && !defaultValueProvided)
        {
            throw new InvalidOperationException(
                $"Property '{entity.Name}.{operation.Property.Name}' requires --default-value because entity '{entity.Name}' has existing rows.");
        }

        entity.Properties.Add(new GenericProperty
        {
            Name = operation.Property.Name,
            DataType = operation.Property.DataType,
            IsNullable = operation.Property.IsNullable,
        });

        if (!defaultValueProvided)
        {
            return;
        }

        foreach (var record in sourceRows)
        {
            record.Values[operation.Property.Name] = operation.PropertyDefaultValue!;
        }
    }

    private static void DeleteProperty(Workspace workspace, WorkspaceOp operation)
    {
        var entityName = RequireValue(operation.EntityName, nameof(operation.EntityName));
        var propertyName = RequireValue(operation.PropertyName, nameof(operation.PropertyName));
        if (string.Equals(propertyName, "Id", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Property 'Id' is implicit and cannot be removed.");
        }

        var entity = RequireEntity(workspace.Model, entityName);
        var property = RequireProperty(entity, propertyName);
        entity.Properties.Remove(property);

        if (workspace.Instance.RecordsByEntity.TryGetValue(entityName, out var records))
        {
            foreach (var record in records)
            {
                record.Values.Remove(propertyName);
            }
        }
    }

    private static void RenameProperty(Workspace workspace, WorkspaceOp operation)
    {
        var entityName = RequireValue(operation.EntityName, nameof(operation.EntityName));
        var propertyName = RequireValue(operation.PropertyName, nameof(operation.PropertyName));
        var newPropertyName = RequireValue(operation.NewPropertyName, nameof(operation.NewPropertyName));
        if (string.Equals(propertyName, "Id", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(newPropertyName, "Id", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Property 'Id' is implicit and cannot be renamed.");
        }

        var entity = RequireEntity(workspace.Model, entityName);
        var property = RequireProperty(entity, propertyName);

        if (entity.Properties.Any(item =>
                !ReferenceEquals(item, property) &&
                string.Equals(item.Name, newPropertyName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Property '{entityName}.{newPropertyName}' already exists.");
        }

        property.Name = newPropertyName;

        if (workspace.Instance.RecordsByEntity.TryGetValue(entityName, out var records))
        {
            foreach (var record in records)
            {
                if (record.Values.TryGetValue(propertyName, out var value))
                {
                    record.Values.Remove(propertyName);
                    record.Values[newPropertyName] = value;
                }
            }
        }
    }

    private static void ChangeNullability(Workspace workspace, WorkspaceOp operation)
    {
        var entity = RequireEntity(workspace.Model, RequireValue(operation.EntityName, nameof(operation.EntityName)));
        if (string.Equals(operation.PropertyName, "Id", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Property 'Id' is implicit and cannot change requiredness.");
        }

        var property = RequireProperty(entity, RequireValue(operation.PropertyName, nameof(operation.PropertyName)));
        if (operation.IsNullable == null)
        {
            throw new InvalidOperationException("ChangeNullability operation requires IsNullable.");
        }

        property.IsNullable = operation.IsNullable.Value;
    }

    private static void AddRelationship(Workspace workspace, WorkspaceOp operation)
    {
        var entity = RequireEntity(workspace.Model, RequireValue(operation.EntityName, nameof(operation.EntityName)));
        var relatedEntity = RequireValue(operation.RelatedEntity, nameof(operation.RelatedEntity));
        RequireEntity(workspace.Model, relatedEntity);
        var defaultTargetId = operation.RelatedDefaultId?.Trim() ?? string.Empty;
        var relationshipRole = operation.RelatedRole?.Trim() ?? string.Empty;
        var sourceRows = workspace.Instance.GetOrCreateEntityRecords(entity.Name);

        if (sourceRows.Count > 0 && string.IsNullOrWhiteSpace(defaultTargetId))
        {
            throw new InvalidOperationException(
                $"Relationship '{entity.Name}.{(string.IsNullOrWhiteSpace(relationshipRole) ? relatedEntity : relationshipRole)}Id' requires --default-id because entity '{entity.Name}' has existing rows.");
        }

        if (!string.IsNullOrWhiteSpace(defaultTargetId))
        {
            var targetRows = workspace.Instance.GetOrCreateEntityRecords(relatedEntity);
            if (!targetRows.Any(row => string.Equals(row.Id, defaultTargetId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Relationship default target '{relatedEntity} {defaultTargetId}' does not exist.");
            }
        }

        var newRelationship = new GenericRelationship
        {
            Entity = relatedEntity,
            Role = relationshipRole,
        };

        var relationshipName = newRelationship.GetColumnName();
        if (entity.Relationships.Any(relationship =>
                string.Equals(relationship.GetColumnName(), relationshipName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Relationship '{entity.Name}.{relationshipName}' already exists.");
        }

        entity.Relationships.Add(newRelationship);

        if (sourceRows.Count > 0)
        {
            foreach (var record in sourceRows)
            {
                record.RelationshipIds[relationshipName] = defaultTargetId;
            }
        }
    }

    private static void DeleteRelationship(Workspace workspace, WorkspaceOp operation)
    {
        var entityName = RequireValue(operation.EntityName, nameof(operation.EntityName));
        var relationshipSelector = RequireValue(operation.RelatedEntity, nameof(operation.RelatedEntity));
        var entity = RequireEntity(workspace.Model, entityName);
        var relationship = ResolveRelationship(entity, relationshipSelector);
        var relationshipColumnName = relationship.GetColumnName();

        if (workspace.Instance.RecordsByEntity.TryGetValue(entityName, out var records))
        {
            foreach (var record in records)
            {
                record.RelationshipIds.Remove(relationshipColumnName);
            }
        }

        entity.Relationships.Remove(relationship);
    }

    private static void BulkUpsertRows(Workspace workspace, WorkspaceOp operation)
    {
        var entityName = RequireValue(operation.EntityName, nameof(operation.EntityName));
        RequireEntity(workspace.Model, entityName);
        var records = workspace.Instance.GetOrCreateEntityRecords(entityName);

        foreach (var patch in operation.RowPatches)
        {
            if (string.IsNullOrWhiteSpace(patch.Id))
            {
                throw new InvalidOperationException($"Row patch in '{entityName}' is missing Id.");
            }

            var existing = records.FirstOrDefault(record =>
                string.Equals(record.Id, patch.Id, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new GenericRecord
                {
                    Id = patch.Id,
                };
                records.Add(existing);
            }
            else if (patch.ReplaceExisting)
            {
                // Replace mode is used by normalization to drop unknown fields deterministically.
                existing.Values.Clear();
                existing.RelationshipIds.Clear();
            }

            foreach (var value in patch.Values)
            {
                if (string.Equals(value.Key, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (value.Value == null)
                {
                    existing.Values.Remove(value.Key);
                    continue;
                }

                existing.Values[value.Key] = value.Value;
            }

            foreach (var relationship in patch.RelationshipIds)
            {
                if (string.IsNullOrWhiteSpace(relationship.Value))
                {
                    existing.RelationshipIds.Remove(relationship.Key);
                    continue;
                }

                existing.RelationshipIds[relationship.Key] = relationship.Value;
            }
        }
    }

    private static void DeleteRows(Workspace workspace, WorkspaceOp operation)
    {
        var entityName = RequireValue(operation.EntityName, nameof(operation.EntityName));
        RequireEntity(workspace.Model, entityName);
        var records = workspace.Instance.GetOrCreateEntityRecords(entityName);
        var ids = new HashSet<string>(operation.Ids, StringComparer.OrdinalIgnoreCase);
        records.RemoveAll(record => ids.Contains(record.Id));
    }

    private static GenericEntity RequireEntity(GenericModel model, string entityName)
    {
        var entity = model.Entities.FirstOrDefault(item =>
            string.Equals(item.Name, entityName, StringComparison.OrdinalIgnoreCase));
        if (entity == null)
        {
            throw new InvalidOperationException($"Entity '{entityName}' does not exist.");
        }

        return entity;
    }

    private static GenericProperty RequireProperty(GenericEntity entity, string propertyName)
    {
        var property = entity.Properties.FirstOrDefault(item =>
            string.Equals(item.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        if (property == null)
        {
            throw new InvalidOperationException($"Property '{entity.Name}.{propertyName}' does not exist.");
        }

        return property;
    }

    private static void EnsureEntityDoesNotExist(GenericModel model, string entityName)
    {
        if (model.Entities.Any(entity =>
                string.Equals(entity.Name, entityName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Entity '{entityName}' already exists.");
        }
    }

    private static string RequireValue(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Operation field '{fieldName}' is required.");
        }

        return value.Trim();
    }

    private static GenericRelationship ResolveRelationship(GenericEntity entity, string selector)
    {
        var normalizedSelector = selector.Trim();
        var byRole = entity.Relationships
            .Where(item => string.Equals(item.GetRoleOrDefault(), normalizedSelector, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byRole.Count == 1)
        {
            return byRole[0];
        }

        if (byRole.Count > 1)
        {
            throw new InvalidOperationException(
                $"Relationship selector '{selector}' is ambiguous in entity '{entity.Name}'.");
        }

        var byUsage = entity.Relationships
            .Where(item => string.Equals(item.GetColumnName(), normalizedSelector, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byUsage.Count == 1)
        {
            return byUsage[0];
        }

        if (byUsage.Count > 1)
        {
            throw new InvalidOperationException(
                $"Relationship selector '{selector}' is ambiguous in entity '{entity.Name}'.");
        }

        var byTarget = entity.Relationships
            .Where(item => string.Equals(item.Entity, normalizedSelector, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byTarget.Count == 1)
        {
            return byTarget[0];
        }

        if (byTarget.Count > 1)
        {
            throw new InvalidOperationException(
                $"Relationship target '{selector}' is ambiguous in entity '{entity.Name}'. Use relationship role or column.");
        }

        throw new InvalidOperationException(
            $"Relationship '{entity.Name}.{selector}' does not exist.");
    }
}


