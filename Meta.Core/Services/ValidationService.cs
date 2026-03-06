using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Meta.Core.Domain;

namespace Meta.Core.Services;

public sealed class ValidationService : IValidationService
{
    private static readonly Regex NamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public WorkspaceDiagnostics Validate(Workspace workspace)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        var diagnostics = new WorkspaceDiagnostics();
        ValidateModel(workspace.Model, diagnostics);
        ValidateInstance(workspace.Model, workspace.Instance, diagnostics);
        return diagnostics;
    }

    public WorkspaceDiagnostics ValidateIncremental(Workspace workspace, IReadOnlyCollection<string> touchedEntities)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        if (touchedEntities == null || touchedEntities.Count == 0)
        {
            return Validate(workspace);
        }

        var filter = new HashSet<string>(touchedEntities.Where(entity => !string.IsNullOrWhiteSpace(entity)),
            StringComparer.OrdinalIgnoreCase);

        var diagnostics = new WorkspaceDiagnostics();
        ValidateModel(workspace.Model, diagnostics, filter);
        ValidateInstance(workspace.Model, workspace.Instance, diagnostics, filter);
        return diagnostics;
    }

    private static void ValidateModel(
        GenericModel model,
        WorkspaceDiagnostics diagnostics,
        HashSet<string>? filter = null)
    {
        if (model == null)
        {
            diagnostics.Issues.Add(new DiagnosticIssue
            {
                Code = "model.null",
                Message = "Model is missing.",
                Severity = IssueSeverity.Error,
                Location = "model",
            });
            return;
        }

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

            if (filter != null && !filter.Contains(entity.Name))
            {
                continue;
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
            ValidatePendingRelationshipPromotion(entity, model, diagnostics);
        }

        ValidateRelationships(model, diagnostics, filter);
        ValidateCycles(model, diagnostics, filter);
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
            if (string.IsNullOrWhiteSpace(property.DataType))
            {
                diagnostics.Issues.Add(new DiagnosticIssue
                {
                    Code = "property.datatype.empty",
                    Message = $"Property '{entity.Name}.{property.Name}' has empty data type.",
                    Severity = IssueSeverity.Warning,
                    Location = $"model/entity/{entity.Name}/property/{property.Name}/@dataType",
                });
            }
        }
    }

    private static void ValidateEntityMemberNameCollisions(GenericEntity entity, WorkspaceDiagnostics diagnostics)
    {
        var memberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in entity.Properties)
        {
            if (string.IsNullOrWhiteSpace(property.Name))
            {
                continue;
            }

            memberNames.Add(property.Name);
        }

        foreach (var relationship in entity.Relationships)
        {
            var usageName = relationship.GetColumnName();
            if (string.IsNullOrWhiteSpace(usageName))
            {
                continue;
            }

            if (!memberNames.Add(usageName))
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

    private static void ValidatePendingRelationshipPromotion(
        GenericEntity entity,
        GenericModel model,
        WorkspaceDiagnostics diagnostics)
    {
        var relationshipNames = entity.Relationships
            .Select(item => item.GetColumnName())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var property in entity.Properties)
        {
            if (string.IsNullOrWhiteSpace(property.Name) ||
                string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase) ||
                !property.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Length <= 2 ||
                relationshipNames.Contains(property.Name))
            {
                continue;
            }

            var targetEntityName = property.Name[..^2];
            var targetEntityExists = model.Entities.Any(item =>
                string.Equals(item.Name, targetEntityName, StringComparison.OrdinalIgnoreCase));
            if (!targetEntityExists)
            {
                continue;
            }

            diagnostics.Issues.Add(new DiagnosticIssue
            {
                Code = "property.relationship.pending",
                Message =
                    $"Entity '{entity.Name}' has scalar property '{property.Name}' matching entity '{targetEntityName}' without a relationship.",
                Severity = IssueSeverity.Warning,
                Location = $"model/entity/{entity.Name}/property/{property.Name}",
            });
        }
    }

    private static void ValidateRelationships(
        GenericModel model,
        WorkspaceDiagnostics diagnostics,
        HashSet<string>? filter = null)
    {
        var entityNames = new HashSet<string>(model.Entities.Select(entity => entity.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var entity in model.Entities)
        {
            if (filter != null && !filter.Contains(entity.Name))
            {
                continue;
            }

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

                if (!entityNames.Contains(relationship.Entity))
                {
                    diagnostics.Issues.Add(new DiagnosticIssue
                    {
                        Code = "relationship.target.missing",
                        Message = $"Relationship target '{relationship.Entity}' in entity '{entity.Name}' does not exist.",
                        Severity = IssueSeverity.Error,
                        Location = $"model/entity/{entity.Name}/relationship/{relationship.Entity}",
                    });
                }
            }
        }
    }

    private static void ValidateCycles(GenericModel model, WorkspaceDiagnostics diagnostics, HashSet<string>? filter)
    {
        var graph = model.Entities.ToDictionary(
            entity => entity.Name,
            entity => entity.Relationships.Select(relationship => relationship.Entity).ToList(),
            StringComparer.OrdinalIgnoreCase);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in graph.Keys)
        {
            if (filter != null && !filter.Contains(entity))
            {
                continue;
            }

            if (DetectCycle(entity, graph, visited, stack))
            {
                diagnostics.Issues.Add(new DiagnosticIssue
                {
                    Code = "relationship.cycle",
                    Message = $"Cycle detected from entity '{entity}'.",
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
        WorkspaceDiagnostics diagnostics,
        HashSet<string>? filter = null)
    {
        if (instance == null)
        {
            diagnostics.Issues.Add(new DiagnosticIssue
            {
                Code = "instance.null",
                Message = "Instance data is missing.",
                Severity = IssueSeverity.Error,
                Location = "instance",
            });
            return;
        }

        var modelByEntity = model.Entities.ToDictionary(entity => entity.Name, StringComparer.OrdinalIgnoreCase);
        var idsByEntity = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entityRecords in instance.RecordsByEntity)
        {
            var entityName = entityRecords.Key;
            if (filter != null && !filter.Contains(entityName))
            {
                continue;
            }

            if (!modelByEntity.TryGetValue(entityName, out var modelEntity))
            {
                diagnostics.Issues.Add(new DiagnosticIssue
                {
                    Code = "instance.entity.unknown",
                    Message = $"Instance includes unknown entity '{entityName}'.",
                    Severity = IssueSeverity.Warning,
                    Location = $"instance/{entityName}",
                });
                continue;
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            idsByEntity[entityName] = ids;

            foreach (var record in entityRecords.Value)
            {
                var recordId = NormalizeIdentity(record.Id);
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
                else if (!IsValidIdentity(recordId))
                {
                    diagnostics.Issues.Add(new DiagnosticIssue
                    {
                        Code = "instance.id.invalid",
                        Message = $"Entity '{entityName}' has invalid Id '{record.Id}'.",
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

                foreach (var property in modelEntity.Properties
                             .Where(item => !string.Equals(item.Name, "Id", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!record.Values.TryGetValue(property.Name, out var propertyValue) || propertyValue == null)
                    {
                        continue;
                    }

                    if (IsStringDataType(property.DataType))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(propertyValue))
                    {
                        diagnostics.Issues.Add(new DiagnosticIssue
                        {
                            Code = "instance.property.parse",
                            Message =
                                $"Entity '{entityName}' record '{record.Id}' has invalid empty value for non-string property '{property.Name}'.",
                            Severity = IssueSeverity.Error,
                            Location = $"instance/{entityName}/{record.Id}/{property.Name}",
                        });
                    }
                }
            }
        }

        foreach (var entityRecords in instance.RecordsByEntity)
        {
            var entityName = entityRecords.Key;
            if (filter != null && !filter.Contains(entityName))
            {
                continue;
            }

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
                        diagnostics.Issues.Add(new DiagnosticIssue
                        {
                            Code = "instance.relationship.missing",
                            Message = $"Entity '{entityName}' record '{record.Id}' is missing relationship '{relationshipName}'.",
                            Severity = IssueSeverity.Error,
                            Location = $"instance/{entityName}/{record.Id}/relationship/{relationshipName}",
                        });
                        continue;
                    }

                    var normalizedRelatedId = NormalizeIdentity(relatedId);
                    if (!IsValidIdentity(normalizedRelatedId))
                    {
                        diagnostics.Issues.Add(new DiagnosticIssue
                        {
                            Code = "instance.relationship.invalid",
                            Message =
                                $"Entity '{entityName}' record '{record.Id}' has invalid relationship '{relationshipName}' id '{relatedId}'.",
                            Severity = IssueSeverity.Error,
                            Location = $"instance/{entityName}/{record.Id}/relationship/{relationshipName}/{relatedId}",
                        });
                        continue;
                    }

                    if (!idsByEntity.TryGetValue(relationship.Entity, out var targetIds))
                    {
                        targetIds = new HashSet<string>(
                            instance.RecordsByEntity.TryGetValue(relationship.Entity, out var targetRecords)
                                ? targetRecords.Select(targetRecord => NormalizeIdentity(targetRecord.Id))
                                : Enumerable.Empty<string>(),
                            StringComparer.OrdinalIgnoreCase);
                        idsByEntity[relationship.Entity] = targetIds;
                    }

                    if (!targetIds.Contains(normalizedRelatedId))
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

    private static string NormalizeIdentity(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static bool IsValidIdentity(string? value)
    {
        return !string.IsNullOrWhiteSpace(NormalizeIdentity(value));
    }

    private static bool IsStringDataType(string? dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType))
        {
            return true;
        }

        return string.Equals(dataType.Trim(), "string", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidName(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && NamePattern.IsMatch(value);
    }

}


