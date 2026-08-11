using System.Globalization;
using Meta.Operations.Domain;

namespace Meta.Core.Services;

public sealed partial class InstanceDiffService : IInstanceDiffService
{
    private sealed class IdentityAllocator
    {
        private readonly Dictionary<string, int> nextIdByEntity = new(StringComparer.OrdinalIgnoreCase);

        public string NextId(string entityName)
        {
            if (string.IsNullOrWhiteSpace(entityName))
            {
                throw new InvalidOperationException("Identity allocator requires a non-empty entity name.");
            }

            var next = nextIdByEntity.TryGetValue(entityName, out var current) ? current + 1 : 1;
            nextIdByEntity[entityName] = next;
            return next.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AlignmentReferenceFieldsByEntity =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [ModelLeftEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelId"] = ModelEntityName,
            },
            [ModelRightEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelId"] = ModelEntityName,
            },
            [AlignmentEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelLeftId"] = ModelLeftEntityName,
                ["ModelRightId"] = ModelRightEntityName,
            },
            [ModelLeftEntityEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelLeftId"] = ModelLeftEntityName,
            },
            [ModelRightEntityEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelRightId"] = ModelRightEntityName,
            },
            [ModelLeftPropertyEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelLeftEntityId"] = ModelLeftEntityEntityName,
            },
            [ModelRightPropertyEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelRightEntityId"] = ModelRightEntityEntityName,
            },
            [EntityMapEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelLeftEntityId"] = ModelLeftEntityEntityName,
                ["ModelRightEntityId"] = ModelRightEntityEntityName,
            },
            [PropertyMapEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelLeftPropertyId"] = ModelLeftPropertyEntityName,
                ["ModelRightPropertyId"] = ModelRightPropertyEntityName,
            },
        };

    private static string EscapeCanonicalPart(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string CreateEntityInstanceKey(string entityId, string entityInstanceIdentifier)
    {
        return string.Join("\n", EscapeCanonicalPart(entityId), EscapeCanonicalPart(entityInstanceIdentifier));
    }

    private static string CreatePropertyTupleKey(string entityId, string entityInstanceIdentifier, string propertyId, string value)
    {
        return string.Join(
            "\n",
            EscapeCanonicalPart(entityId),
            EscapeCanonicalPart(entityInstanceIdentifier),
            EscapeCanonicalPart(propertyId),
            EscapeCanonicalPart(value));
    }

    private static string CreateEntityPropertyIdentityKey(string entityId, string entityInstanceIdentifier, string propertyId)
    {
        return string.Join(
            "\n",
            EscapeCanonicalPart(entityId),
            EscapeCanonicalPart(entityInstanceIdentifier),
            EscapeCanonicalPart(propertyId));
    }

    private static string CreateAlignedRowKey(string entityMapId, string rowId)
    {
        return string.Join("\n", EscapeCanonicalPart(entityMapId), EscapeCanonicalPart(rowId));
    }

    private static string CreateAlignedPropertyTupleKey(string entityMapId, string rowId, string propertyMapId, string value)
    {
        return string.Join(
            "\n",
            EscapeCanonicalPart(entityMapId),
            EscapeCanonicalPart(rowId),
            EscapeCanonicalPart(propertyMapId),
            EscapeCanonicalPart(value));
    }

    private static string CreateAlignedEntityRowPropertyMapKey(string entityMapId, string rowId, string propertyMapId)
    {
        return string.Join(
            "\n",
            EscapeCanonicalPart(entityMapId),
            EscapeCanonicalPart(rowId),
            EscapeCanonicalPart(propertyMapId));
    }

    private static GenericEntity RequireEntity(InMemoryWorkspace workspace, string entityName)
        => RequireEntity(workspace.Model, entityName);

    private static GenericEntity RequireEntity(GenericModel model, string entityName)
    {
        var entity = model.FindEntity(entityName);
        if (entity == null)
        {
            throw new InvalidOperationException($"Entity '{entityName}' does not exist.");
        }

        return entity;
    }

    private InMemoryWorkspace CreateWorkspaceFromDefinition(InstanceDiffWorkspaceDefinition definition)
    {
        var model = definition.Model.Clone();
        return new InMemoryWorkspace(
            model,
            new GenericInstance
            {
                ModelName = model.Name,
            });
    }

    private static bool IsModelContract(GenericModel model, string expectedSignature)
    {
        return string.Equals(
            model.ComputeContractSignature(),
            expectedSignature,
            StringComparison.Ordinal);
    }

    private static GenericRecord AddDiffRecord(
        InMemoryWorkspace workspace,
        string entityName,
        string id,
        IReadOnlyDictionary<string, string?> values)
    {
        var modelEntity = workspace.Model.FindEntity(entityName)
                          ?? throw new InvalidOperationException($"Diff model is missing entity '{entityName}'.");
        var propertyNames = modelEntity.Properties
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationshipByAlias = new Dictionary<string, GenericRelationship>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in modelEntity.Relationships)
        {
            relationshipByAlias[relationship.GetColumnName()] = relationship;
            relationshipByAlias[relationship.GetRoleOrDefault()] = relationship;
        }

        var row = new GenericRecord
        {
            Id = id,
        };

        foreach (var pair in values
                     .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                     .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(pair.Key, "Id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (pair.Value == null)
            {
                continue;
            }

            if (relationshipByAlias.TryGetValue(pair.Key, out var relationship))
            {
                row.RelationshipIds[relationship.GetColumnName()] = pair.Value;
                continue;
            }

            if (propertyNames.Contains(pair.Key))
            {
                row.Values[pair.Key] = pair.Value;
                continue;
            }

            throw new InvalidOperationException(
                $"Diff row '{entityName} {id}' contains unknown field '{pair.Key}'.");
        }

        workspace.Instance.GetOrCreateEntityRecords(entityName).Add(row);
        return row;
    }

    private static bool TryGetRecordFieldValue(GenericRecord row, string key, out string value)
    {
        if (row.Values.TryGetValue(key, out var propertyValue))
        {
            if (propertyValue == null)
            {
                throw new InvalidOperationException(
                    $"Instance '{row.Id}' contains null value for '{key}'.");
            }

            value = propertyValue;
            return true;
        }

        if (row.RelationshipIds.TryGetValue(key, out var relationshipValue))
        {
            if (relationshipValue == null)
            {
                throw new InvalidOperationException(
                    $"Instance '{row.Id}' contains null relationship value for '{key}'.");
            }

            value = relationshipValue;
            return true;
        }

        if (key.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
        {
            var relationshipUsageName = key[..^2];
            if (row.RelationshipIds.TryGetValue(relationshipUsageName, out var usageRelationshipValue))
            {
                if (usageRelationshipValue == null)
                {
                    throw new InvalidOperationException(
                        $"Instance '{row.Id}' contains null relationship value for '{key}'.");
                }

                value = usageRelationshipValue;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static IReadOnlyDictionary<string, GenericRecord> BuildRecordMap(InMemoryWorkspace workspace, string entityName)
        => BuildRecordMap(workspace.Instance, entityName);

    private static IReadOnlyDictionary<string, GenericRecord> BuildRecordMap(GenericInstance instance, string entityName)
    {
        if (!instance.RecordsByEntity.TryGetValue(entityName, out var rows))
        {
            return new Dictionary<string, GenericRecord>(StringComparer.OrdinalIgnoreCase);
        }

        var map = new Dictionary<string, GenericRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Id))
            {
                throw new InvalidOperationException($"Entity '{entityName}' has a record with blank Id.");
            }

            if (!map.TryAdd(row.Id, row))
            {
                throw new InvalidOperationException($"Entity '{entityName}' has duplicate Id '{row.Id}'.");
            }
        }

        return map;
    }

    private static bool TryGetPropertyLikeValue(GenericEntity entity, GenericRecord row, string propertyName, out string value)
    {
        if (row.Values.TryGetValue(propertyName, out var propertyValue))
        {
            if (propertyValue == null)
            {
                throw new InvalidOperationException(
                    $"Entity '{entity.Name}' row '{row.Id}' contains null value for property '{propertyName}'.");
            }

            value = propertyValue;
            return true;
        }

        var relationship = entity.FindRelationshipByRole(propertyName) ??
                           entity.FindRelationshipByColumnName(propertyName);
        if (relationship != null &&
            row.RelationshipIds.TryGetValue(relationship.GetColumnName(), out var relationshipValue))
        {
            if (relationshipValue == null)
            {
                throw new InvalidOperationException(
                    $"Entity '{entity.Name}' row '{row.Id}' contains null relationship target for '{relationship.GetColumnName()}'.");
            }

            if (string.IsNullOrWhiteSpace(relationshipValue))
            {
                throw new InvalidOperationException(
                    $"Entity '{entity.Name}' row '{row.Id}' contains blank relationship target for '{relationship.GetColumnName()}'.");
            }

            value = relationshipValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsRelationshipProperty(GenericEntity entity, string propertyName, out string relationshipUsageName)
    {
        relationshipUsageName = string.Empty;
        var relationship = entity.FindRelationshipByRole(propertyName) ??
                           entity.FindRelationshipByColumnName(propertyName);
        if (relationship == null)
        {
            return false;
        }

        relationshipUsageName = relationship.GetColumnName();
        return true;
    }

}
