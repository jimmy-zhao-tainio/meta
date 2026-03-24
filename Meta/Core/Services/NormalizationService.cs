using System;
using System.Collections.Generic;
using System.Linq;
using Meta.Core.Domain;
using Meta.Core.Operations;

namespace Meta.Core.Services;

public sealed class NormalizeOptions
{
    public string EntityName { get; set; } = string.Empty;
    public bool DropUnknown { get; set; }
}

public static class NormalizationService
{
    public static IReadOnlyList<WorkspaceOp> BuildNormalizeOperations(Workspace workspace, NormalizeOptions? options = null)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        options ??= new NormalizeOptions();
        var entityNames = ResolveEntityNames(workspace, options);
        var operations = new List<WorkspaceOp>();

        foreach (var entityName in entityNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            var modelEntity = workspace.Model.FindEntity(entityName);
            if (modelEntity == null)
            {
                continue;
            }

            var rowPatches = BuildEntityRowPatches(workspace, modelEntity, options.DropUnknown);
            if (rowPatches.Count == 0)
            {
                continue;
            }

            operations.Add(new WorkspaceOp
            {
                Type = WorkspaceOpTypes.BulkUpsertRows,
                EntityName = entityName,
                RowPatches = rowPatches,
            });
        }

        return operations;
    }

    private static IReadOnlyList<string> ResolveEntityNames(Workspace workspace, NormalizeOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.EntityName))
        {
            var entity = workspace.Model.FindEntity(options.EntityName);
            if (entity == null)
            {
                throw new InvalidOperationException($"Entity '{options.EntityName}' does not exist.");
            }

            return new[] { entity.Name };
        }

        return workspace.Model.Entities
            .Select(entity => entity.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<RowPatch> BuildEntityRowPatches(Workspace workspace, GenericEntity entity, bool dropUnknown)
    {
        if (!workspace.Instance.RecordsByEntity.TryGetValue(entity.Name, out var records))
        {
            return new List<RowPatch>();
        }

        var propertyNames = entity.Properties
            .Select(property => property.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var relationshipNames = entity.Relationships
            .Select(relationship => relationship.GetColumnName())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rowPatches = new List<RowPatch>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            var normalizedId = NormalizeId(record);
            if (!ids.Add(normalizedId))
            {
                throw new InvalidOperationException(
                    $"Cannot normalize entity '{entity.Name}' because Id '{normalizedId}' is duplicated.");
            }

            var normalizedValues = NormalizeValues(record, propertyNames, dropUnknown);
            var normalizedRelationships = NormalizeRelationships(record, relationshipNames, dropUnknown);

            if (DictionaryEquals(record.Values, normalizedValues) &&
                DictionaryEquals(record.RelationshipIds, normalizedRelationships))
            {
                continue;
            }

            var patch = new RowPatch
            {
                Id = normalizedId,
                ReplaceExisting = true,
            };

            foreach (var value in normalizedValues
                         .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                patch.Values[value.Key] = value.Value;
            }

            foreach (var relationship in normalizedRelationships
                         .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                patch.RelationshipIds[relationship.Key] = relationship.Value;
            }

            rowPatches.Add(patch);
        }

        return rowPatches
            .OrderBy(patch => patch.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeId(GenericRecord record)
    {
        var id = record.Id?.Trim() ?? string.Empty;
        if (!IsValidIdentity(id))
        {
            throw new InvalidOperationException($"Cannot normalize row with invalid Id '{record.Id}'.");
        }

        return id;
    }

    private static Dictionary<string, string> NormalizeValues(
        GenericRecord record,
        IReadOnlySet<string> propertyNames,
        bool dropUnknown)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (dropUnknown)
        {
            foreach (var key in propertyNames)
            {
                if (record.Values.TryGetValue(key, out var value) && value != null)
                {
                    normalized[key] = value;
                }
            }
        }
        else
        {
            foreach (var value in record.Values)
            {
                if (value.Value != null)
                {
                    normalized[value.Key] = value.Value;
                }
            }
        }

        return normalized;
    }

    private static Dictionary<string, string> NormalizeRelationships(
        GenericRecord record,
        IReadOnlySet<string> relationshipNames,
        bool dropUnknown)
    {
        if (!dropUnknown)
        {
            var allRelationships = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var relationship in record.RelationshipIds)
            {
                if (!string.IsNullOrWhiteSpace(relationship.Value))
                {
                    var normalizedValue = relationship.Value.Trim();
                    if (!IsValidIdentity(normalizedValue))
                    {
                        throw new InvalidOperationException(
                            $"Cannot normalize row '{record.Id}' because relationship '{relationship.Key}' has invalid target '{relationship.Value}'.");
                    }

                    allRelationships[relationship.Key] = normalizedValue;
                }
            }

            return allRelationships;
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationshipName in relationshipNames)
        {
            if (record.RelationshipIds.TryGetValue(relationshipName, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                var normalizedValue = value.Trim();
                if (!IsValidIdentity(normalizedValue))
                {
                    throw new InvalidOperationException(
                        $"Cannot normalize row '{record.Id}' because relationship '{relationshipName}' has invalid target '{value}'.");
                }

                normalized[relationshipName] = normalizedValue;
            }
        }

        return normalized;
    }

    private static bool IsValidIdentity(string? value)
    {
        return !string.IsNullOrWhiteSpace(value?.Trim());
    }

    private static bool DictionaryEquals(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var item in left)
        {
            if (!right.TryGetValue(item.Key, out var value))
            {
                return false;
            }

            if (!string.Equals(item.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

