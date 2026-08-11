using System;
using System.Collections.Generic;
using System.Linq;

namespace Meta.Operations;

public static class WorkspaceValidator
{
    public static WorkspaceDiagnostics Validate(
        GenericModel model,
        GenericInstance instance)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(instance);

        var diagnostics = new WorkspaceDiagnostics();
        ValidateModel(model, diagnostics);
        ValidateInstance(model, instance, diagnostics);
        return diagnostics;
    }

    private static void ValidateModel(
        GenericModel model,
        WorkspaceDiagnostics diagnostics)
    {
        if (!IsValidName(model.Name))
        {
            diagnostics.Issues.Add(new DiagnosticIssue
            {
                Code = "model.name.invalid",
                Message = $"Model name '{model.Name}' is invalid.",
                Severity = IssueSeverity.Error,
                Location = "model/@name",
            });
        }
        var entityNameMap = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in model.Entities)
        {
            if (!entityNameMap.Add(entity.Name))
            {
                diagnostics.Issues.Add(new DiagnosticIssue
                {
                    Code = "entity.duplicate",
                    Message = $"Entity '{entity.Name}' is duplicated.",
                    Severity = IssueSeverity.Error,
                    Location = $"model/entity/{entity.Name}",
                });
            }

            var containerName = entity.GetListName();
            if (!IsValidName(containerName))
            {
                diagnostics.Issues.Add(new DiagnosticIssue
                {
                    Code = "entity.list.invalid",
                    Message = $"Entity list container name '{containerName}' on '{entity.Name}' is invalid.",
                    Severity = IssueSeverity.Error,
                    Location = $"model/entity/{entity.Name}",
                });
            }

            if (!IsValidName(entity.Name))
            {
                diagnostics.Issues.Add(new DiagnosticIssue
                {
                    Code = "entity.name.invalid",
                    Message = $"Entity name '{entity.Name}' is invalid.",
                    Severity = IssueSeverity.Error,
                    Location = $"model/entity/{entity.Name}",
                });
            }
            ValidateEntityProperties(entity, diagnostics);
            ValidateEntityIdProperty(entity, diagnostics);
            ValidateEntityMemberNameCollisions(entity, diagnostics);
        }

        ValidateRelationships(model, diagnostics);
        ValidateCycles(model, diagnostics);
    }

    private static void ValidateEntityProperties(GenericEntity entity, WorkspaceDiagnostics diagnostics)
    {
        var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in entity.Properties)
        {
            if (!propertyNames.Add(property.Name))
            {
                diagnostics.Issues.Add(new DiagnosticIssue
                {
                    Code = "property.duplicate",
                    Message = $"Property '{entity.Name}.{property.Name}' is duplicated.",
                    Severity = IssueSeverity.Error,
                    Location = $"model/entity/{entity.Name}/property/{property.Name}",
                });
            }

            if (!IsValidName(property.Name))
            {
                diagnostics.Issues.Add(new DiagnosticIssue
                {
                    Code = "property.name.invalid",
                    Message = $"Property name '{entity.Name}.{property.Name}' is invalid.",
                    Severity = IssueSeverity.Error,
                    Location = $"model/entity/{entity.Name}/property/{property.Name}",
                });
            }
        }
    }

    private static void ValidateEntityMemberNameCollisions(GenericEntity entity, WorkspaceDiagnostics diagnostics)
    {
        var storageNames = new HashSet<string>(
            MetaName.Comparer);
        var csharpMemberNames = new HashSet<string>(
            MetaName.Comparer);
        foreach (var property in entity.Properties)
        {
            if (string.IsNullOrWhiteSpace(property.Name))
            {
                continue;
            }

            storageNames.Add(property.Name);
            csharpMemberNames.Add(property.Name);
            if (MetaName.Comparer.Equals(
                    property.Name,
                    entity.Name))
            {
                diagnostics.Issues.Add(new DiagnosticIssue
                {
                    Code = "entity.member.matches-entity",
                    Message =
                        $"Property '{entity.Name}.{property.Name}' cannot have the same name as its entity.",
                    Severity = IssueSeverity.Error,
                    Location = $"model/entity/{entity.Name}/property/{property.Name}",
                });
            }
        }

        foreach (var relationship in entity.Relationships)
        {
            var usageName = relationship.GetColumnName();
            if (string.IsNullOrWhiteSpace(usageName))
            {
                continue;
            }

            if (!storageNames.Add(usageName))
            {
                diagnostics.Issues.Add(new DiagnosticIssue
                {
                    Code = "entity.member.collision",
                    Message =
                        $"Entity '{entity.Name}' has a name collision between property/member '{usageName}' and relationship '{usageName}'.",
                    Severity = IssueSeverity.Error,
                    Location = $"model/entity/{entity.Name}/relationship/{usageName}",
                });
            }

            var navigationName = relationship.GetNavigationName();
            if (!csharpMemberNames.Add(navigationName))
            {
                diagnostics.Issues.Add(new DiagnosticIssue
                {
                    Code = "entity.member.collision",
                    Message =
                        $"Entity '{entity.Name}' has both a property and relationship navigation named '{navigationName}'.",
                    Severity = IssueSeverity.Error,
                    Location = $"model/entity/{entity.Name}/relationship/{usageName}",
                });
            }
            else if (MetaName.Comparer.Equals(
                         navigationName,
                         entity.Name))
            {
                diagnostics.Issues.Add(new DiagnosticIssue
                {
                    Code = "entity.member.matches-entity",
                    Message =
                        $"Relationship '{entity.Name}.{navigationName}' cannot have the same name as its entity. Give the relationship a role.",
                    Severity = IssueSeverity.Error,
                    Location = $"model/entity/{entity.Name}/relationship/{usageName}",
                });
            }
        }
    }

    private static void ValidateEntityIdProperty(GenericEntity entity, WorkspaceDiagnostics diagnostics)
    {
        var explicitId = entity.Properties.FirstOrDefault(property =>
            string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase));
        if (explicitId == null)
        {
            return;
        }

        diagnostics.Issues.Add(new DiagnosticIssue
        {
            Code = "property.id.explicit",
            Message = $"Entity '{entity.Name}' must not declare property 'Id'. It is implicit.",
            Severity = IssueSeverity.Error,
            Location = $"model/entity/{entity.Name}/property/Id",
        });
    }

    private static void ValidateRelationships(
        GenericModel model,
        WorkspaceDiagnostics diagnostics)
    {
        var entityNames = new HashSet<string>(
            model.Entities.Select(entity => entity.Name),
            MetaName.Comparer);
        foreach (var entity in model.Entities)
        {
            var relationNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var relationship in entity.Relationships)
            {
                var relationshipName = relationship.GetColumnName();
                if (!IsValidName(relationshipName))
                {
                    diagnostics.Issues.Add(new DiagnosticIssue
                    {
                        Code = "relationship.name.invalid",
                        Message = $"Relationship name '{entity.Name}.{relationshipName}' is invalid.",
                        Severity = IssueSeverity.Error,
                        Location = $"model/entity/{entity.Name}/relationship/{relationshipName}",
                    });
                }

                if (!relationNames.Add(relationshipName))
                {
                    diagnostics.Issues.Add(new DiagnosticIssue
                    {
                        Code = "relationship.duplicate",
                        Message = $"Relationship '{entity.Name}.{relationshipName}' is duplicated.",
                        Severity = IssueSeverity.Error,
                        Location = $"model/entity/{entity.Name}/relationship/{relationshipName}",
                    });
                }

                var targetEntity = model.Entities.FirstOrDefault(
                    candidate => MetaName.Comparer.Equals(
                        candidate.Name,
                        relationship.Entity));
                if (targetEntity == null)
                {
                    diagnostics.Issues.Add(new DiagnosticIssue
                    {
                        Code = "relationship.target.missing",
                        Message = $"Relationship target '{relationship.Entity}' in entity '{entity.Name}' does not exist.",
                        Severity = IssueSeverity.Error,
                        Location = $"model/entity/{entity.Name}/relationship/{relationship.Entity}",
                    });
                }
                else if (!string.Equals(
                             targetEntity.Name,
                             relationship.Entity,
                             StringComparison.Ordinal))
                {
                    diagnostics.Issues.Add(new DiagnosticIssue
                    {
                        Code = "relationship.target.spelling",
                        Message =
                            $"Relationship target '{relationship.Entity}' in entity '{entity.Name}' must use the declared spelling '{targetEntity.Name}'.",
                        Severity = IssueSeverity.Error,
                        Location = $"model/entity/{entity.Name}/relationship/{relationship.Entity}",
                    });
                }
            }
        }
    }

    private static void ValidateCycles(GenericModel model, WorkspaceDiagnostics diagnostics)
    {
        var graph = model.Entities.ToDictionary(
            entity => entity.Name,
            entity => entity.Relationships
                .Where(relationship => !relationship.IsNullable)
                .Select(relationship => relationship.Entity)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in graph.Keys)
        {
            if (DetectCycle(entity, graph, visited, stack))
            {
                diagnostics.Issues.Add(new DiagnosticIssue
                {
                    Code = "relationship.cycle",
                    Message =
                        $"Required relationship cycle detected from entity '{entity}'.",
                    Severity = IssueSeverity.Error,
                    Location = $"model/entity/{entity}",
                });
            }
        }
    }

    private static bool DetectCycle(
        string entity,
        IReadOnlyDictionary<string, List<string>> graph,
        HashSet<string> visited,
        HashSet<string> stack)
    {
        if (stack.Contains(entity))
        {
            return true;
        }

        if (visited.Contains(entity))
        {
            return false;
        }

        visited.Add(entity);
        stack.Add(entity);
        if (graph.TryGetValue(entity, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (!graph.ContainsKey(neighbor))
                {
                    continue;
                }

                if (DetectCycle(neighbor, graph, visited, stack))
                {
                    return true;
                }
            }
        }

        stack.Remove(entity);
        return false;
    }

    private static void ValidateInstance(
        GenericModel model,
        GenericInstance instance,
        WorkspaceDiagnostics diagnostics)
    {
        if (!string.Equals(
                instance.ModelName,
                model.Name,
                StringComparison.Ordinal))
        {
            diagnostics.Issues.Add(new DiagnosticIssue
            {
                Code = "instance.model.mismatch",
                Message =
                    $"Instance model name '{instance.ModelName}' must match model '{model.Name}'.",
                Severity = IssueSeverity.Error,
                Location = "instance",
            });
        }

        var modelByEntity = model.Entities.ToDictionary(
            entity => entity.Name,
            MetaName.Comparer);
        var idsByEntity = new Dictionary<string, HashSet<string>>(
            MetaName.Comparer);

        foreach (var entityRecords in instance.RecordsByEntity)
        {
            var entityName = entityRecords.Key;
            if (!modelByEntity.TryGetValue(entityName, out var modelEntity))
            {
                diagnostics.Issues.Add(new DiagnosticIssue
                {
                    Code = "instance.entity.unknown",
                    Message = $"Instance includes unknown entity '{entityName}'.",
                    Severity = IssueSeverity.Error,
                    Location = $"instance/{entityName}",
                });
                continue;
            }

            var ids = new HashSet<string>(MetaIdentity.Comparer);
            idsByEntity[entityName] = ids;
            var propertyNames = modelEntity.Properties
                .Select(property => property.Name)
                .ToHashSet(MetaName.Comparer);
            var relationshipNames = modelEntity.Relationships
                .Select(relationship => relationship.GetColumnName())
                .ToHashSet(MetaName.Comparer);

            foreach (var record in entityRecords.Value)
            {
                var recordId = record.Id;
                if (string.IsNullOrWhiteSpace(recordId))
                {
                    diagnostics.Issues.Add(new DiagnosticIssue
                    {
                        Code = "instance.id.missing",
                        Message = $"Entity '{entityName}' has a record with missing Id.",
                        Severity = IssueSeverity.Error,
                        Location = $"instance/{entityName}",
                    });
                }
                else if (!MetaIdentity.TryValidate(recordId, out var identityError))
                {
                    diagnostics.Issues.Add(new DiagnosticIssue
                    {
                        Code = "instance.id.invalid",
                        Message = $"Entity '{entityName}' has invalid Id '{record.Id}'. {identityError}",
                        Severity = IssueSeverity.Error,
                        Location = $"instance/{entityName}/{record.Id}",
                    });
                }
                else if (!ids.Add(recordId))
                {
                    diagnostics.Issues.Add(new DiagnosticIssue
                    {
                        Code = "instance.id.duplicate",
                        Message = $"Entity '{entityName}' has duplicate Id '{recordId}'.",
                        Severity = IssueSeverity.Error,
                        Location = $"instance/{entityName}/{recordId}",
                    });
                }

                foreach (var propertyName in record.Values.Keys.Where(
                             name => !propertyNames.Contains(name)))
                {
                    diagnostics.Issues.Add(new DiagnosticIssue
                    {
                        Code = "instance.property.unknown",
                        Message =
                            $"Entity '{entityName}' record '{record.Id}' contains unknown property '{propertyName}'.",
                        Severity = IssueSeverity.Error,
                        Location = $"instance/{entityName}/{record.Id}/{propertyName}",
                    });
                }

                foreach (var relationshipName in
                         record.RelationshipIds.Keys.Where(
                             name => !relationshipNames.Contains(name)))
                {
                    diagnostics.Issues.Add(new DiagnosticIssue
                    {
                        Code = "instance.relationship.unknown",
                        Message =
                            $"Entity '{entityName}' record '{record.Id}' contains unknown relationship '{relationshipName}'.",
                        Severity = IssueSeverity.Error,
                        Location = $"instance/{entityName}/{record.Id}/relationship/{relationshipName}",
                    });
                }

                foreach (var requiredProperty in modelEntity.Properties
                             .Where(property =>
                                 !property.IsNullable &&
                                 !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)))
                {
                    var hasValue = record.Values.TryGetValue(requiredProperty.Name, out var value);
                    if (!hasValue)
                    {
                        diagnostics.Issues.Add(new DiagnosticIssue
                        {
                            Code = "instance.required.missing",
                            Message = $"Entity '{entityName}' record '{record.Id}' is missing required value '{requiredProperty.Name}'.",
                            Severity = IssueSeverity.Error,
                            Location = $"instance/{entityName}/{record.Id}/{requiredProperty.Name}",
                        });

                        continue;
                    }

                    if (value == null)
                    {
                        diagnostics.Issues.Add(new DiagnosticIssue
                        {
                            Code = "instance.required.missing",
                            Message = $"Entity '{entityName}' record '{record.Id}' is missing required value '{requiredProperty.Name}'.",
                            Severity = IssueSeverity.Error,
                            Location = $"instance/{entityName}/{record.Id}/{requiredProperty.Name}",
                        });
                    }
                }

            }
        }

        foreach (var entityRecords in instance.RecordsByEntity)
        {
            var entityName = entityRecords.Key;
            if (!modelByEntity.TryGetValue(entityName, out var modelEntity))
            {
                continue;
            }

            foreach (var record in entityRecords.Value)
            {
                foreach (var relationship in modelEntity.Relationships)
                {
                    var relationshipName = relationship.GetColumnName();
                    if (!record.RelationshipIds.TryGetValue(relationshipName, out var relatedId) ||
                        string.IsNullOrWhiteSpace(relatedId))
                    {
                        if (relationship.IsNullable)
                        {
                            continue;
                        }

                        diagnostics.Issues.Add(new DiagnosticIssue
                        {
                            Code = "instance.relationship.missing",
                            Message = $"Entity '{entityName}' record '{record.Id}' is missing relationship '{relationshipName}'.",
                            Severity = IssueSeverity.Error,
                            Location = $"instance/{entityName}/{record.Id}/relationship/{relationshipName}",
                        });
                        continue;
                    }

                    if (!MetaIdentity.TryValidate(relatedId, out var relationshipIdentityError))
                    {
                        diagnostics.Issues.Add(new DiagnosticIssue
                        {
                            Code = "instance.relationship.invalid",
                            Message =
                                $"Entity '{entityName}' record '{record.Id}' has invalid relationship '{relationshipName}' id '{relatedId}'. {relationshipIdentityError}",
                            Severity = IssueSeverity.Error,
                            Location = $"instance/{entityName}/{record.Id}/relationship/{relationshipName}/{relatedId}",
                        });
                        continue;
                    }

                    if (!idsByEntity.TryGetValue(relationship.Entity, out var targetIds))
                    {
                        targetIds = new HashSet<string>(
                            instance.RecordsByEntity.TryGetValue(relationship.Entity, out var targetRecords)
                                ? targetRecords.Select(targetRecord => targetRecord.Id)
                                : Enumerable.Empty<string>(),
                            MetaIdentity.Comparer);
                        idsByEntity[relationship.Entity] = targetIds;
                    }

                    if (!targetIds.Contains(relatedId))
                    {
                        diagnostics.Issues.Add(new DiagnosticIssue
                        {
                            Code = "instance.relationship.orphan",
                            Message = $"Entity '{entityName}' record '{record.Id}' points to missing '{relationship.Entity}' id '{relatedId}'.",
                            Severity = IssueSeverity.Error,
                            Location = $"instance/{entityName}/{record.Id}/relationship/{relationship.Entity}/{relatedId}",
                        });
                    }
                }
            }
        }
    }

    private static bool IsValidName(string value)
    {
        return MetaName.IsValid(value);
    }

}


