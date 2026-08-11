namespace Meta.Operations.Domain;

public static class InMemoryWorkspaceComparer
{
    public static string? FindDifference(InMemoryWorkspace left, InMemoryWorkspace right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var modelDifference = FindModelDifference(left.Model, right.Model);
        return modelDifference ?? FindInstanceDifference(left, right);
    }

    private static string? FindModelDifference(
        GenericModel left,
        GenericModel right)
    {
        if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal))
        {
            return $"Model names differ: '{left.Name}' and '{right.Name}'.";
        }

        if (left.Entities.Count != right.Entities.Count)
        {
            return $"Entity counts differ: {left.Entities.Count} and {right.Entities.Count}.";
        }

        var rightEntities = right.Entities.ToDictionary(
            entity => entity.Name,
            MetaName.Comparer);
        foreach (var leftEntity in left.Entities)
        {
            if (!rightEntities.TryGetValue(leftEntity.Name, out var rightEntity))
            {
                return $"Entity '{leftEntity.Name}' is missing.";
            }

            if (!string.Equals(
                    leftEntity.Name,
                    rightEntity.Name,
                    StringComparison.Ordinal))
            {
                return
                    $"Entity name spellings differ: '{leftEntity.Name}' and '{rightEntity.Name}'.";
            }

            var propertyDifference = FindPropertyDifference(
                leftEntity,
                rightEntity);
            if (propertyDifference != null)
            {
                return propertyDifference;
            }

            var relationshipDifference = FindRelationshipDifference(
                leftEntity,
                rightEntity);
            if (relationshipDifference != null)
            {
                return relationshipDifference;
            }
        }

        return null;
    }

    private static string? FindPropertyDifference(
        GenericEntity left,
        GenericEntity right)
    {
        if (left.Properties.Count != right.Properties.Count)
        {
            return
                $"Entity '{left.Name}' property counts differ: {left.Properties.Count} and {right.Properties.Count}.";
        }

        var rightProperties = right.Properties.ToDictionary(
            property => property.Name,
            MetaName.Comparer);
        foreach (var leftProperty in left.Properties)
        {
            if (!rightProperties.TryGetValue(
                    leftProperty.Name,
                    out var rightProperty))
            {
                return
                    $"Property '{left.Name}.{leftProperty.Name}' is missing.";
            }

            if (!string.Equals(
                    leftProperty.Name,
                    rightProperty.Name,
                    StringComparison.Ordinal) ||
                leftProperty.IsNullable != rightProperty.IsNullable)
            {
                return
                    $"Property '{left.Name}.{leftProperty.Name}' differs.";
            }
        }

        return null;
    }

    private static string? FindRelationshipDifference(
        GenericEntity left,
        GenericEntity right)
    {
        if (left.Relationships.Count != right.Relationships.Count)
        {
            return
                $"Entity '{left.Name}' relationship counts differ: {left.Relationships.Count} and {right.Relationships.Count}.";
        }

        var rightRelationships = right.Relationships.ToDictionary(
            relationship => relationship.GetColumnName(),
            MetaName.Comparer);
        foreach (var leftRelationship in left.Relationships)
        {
            var name = leftRelationship.GetColumnName();
            if (!rightRelationships.TryGetValue(
                    name,
                    out var rightRelationship))
            {
                return $"Relationship '{left.Name}.{name}' is missing.";
            }

            if (!string.Equals(
                    leftRelationship.Entity,
                    rightRelationship.Entity,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    leftRelationship.Role,
                    rightRelationship.Role,
                    StringComparison.Ordinal) ||
                leftRelationship.IsNullable != rightRelationship.IsNullable)
            {
                return $"Relationship '{left.Name}.{name}' differs.";
            }
        }

        return null;
    }

    private static string? FindInstanceDifference(
        InMemoryWorkspace left,
        InMemoryWorkspace right)
    {
        if (!string.Equals(
                left.Instance.ModelName,
                right.Instance.ModelName,
                StringComparison.Ordinal))
        {
            return
                $"Instance model names differ: '{left.Instance.ModelName}' and '{right.Instance.ModelName}'.";
        }

        var entityNames = left.Model.Entities
            .Select(entity => entity.Name)
            .ToHashSet(MetaName.Comparer);
        var unknownEntity = left.Instance.RecordsByEntity.Keys
            .Concat(right.Instance.RecordsByEntity.Keys)
            .FirstOrDefault(name => !entityNames.Contains(name));
        if (unknownEntity != null)
        {
            return $"Instance state contains unknown entity '{unknownEntity}'.";
        }

        foreach (var entityName in entityNames)
        {
            var leftRecords = GetRecords(left.Instance, entityName);
            var rightRecords = GetRecords(right.Instance, entityName);
            var difference = FindRecordDifference(
                entityName,
                leftRecords,
                rightRecords);
            if (difference != null)
            {
                return difference;
            }
        }

        return null;
    }

    private static IReadOnlyCollection<GenericRecord> GetRecords(
        GenericInstance instance,
        string entityName)
    {
        return instance.RecordsByEntity.TryGetValue(entityName, out var records)
            ? records
            : [];
    }

    private static string? FindRecordDifference(
        string entityName,
        IReadOnlyCollection<GenericRecord> left,
        IReadOnlyCollection<GenericRecord> right)
    {
        if (left.Count != right.Count)
        {
            return
                $"Entity '{entityName}' record counts differ: {left.Count} and {right.Count}.";
        }

        var rightById = right.ToDictionary(
            record => record.Id,
            MetaIdentity.Comparer);
        foreach (var leftRecord in left)
        {
            if (!rightById.TryGetValue(leftRecord.Id, out var rightRecord))
            {
                return
                    $"Entity '{entityName}' record '{leftRecord.Id}' is missing.";
            }

            if (!string.Equals(
                    leftRecord.Id,
                    rightRecord.Id,
                    StringComparison.Ordinal))
            {
                return
                    $"Entity '{entityName}' record Id spellings differ: '{leftRecord.Id}' and '{rightRecord.Id}'.";
            }

            if (!DictionariesAreEqual(
                    leftRecord.Values,
                    rightRecord.Values,
                    StringComparer.Ordinal))
            {
                return
                    $"Entity '{entityName}' record '{leftRecord.Id}' properties differ.";
            }

            if (!DictionariesAreEqual(
                    leftRecord.RelationshipIds,
                    rightRecord.RelationshipIds,
                    MetaIdentity.Comparer))
            {
                return
                    $"Entity '{entityName}' record '{leftRecord.Id}' relationships differ.";
            }
        }

        return null;
    }

    private static bool DictionariesAreEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right,
        StringComparer valueComparer)
    {
        return left.Count == right.Count &&
               left.All(item =>
                   right.TryGetValue(item.Key, out var rightValue) &&
                   valueComparer.Equals(item.Value, rightValue));
    }
}
