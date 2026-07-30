using System.Text;
using Meta.Core.Domain;
using Meta.Core.Operations;

namespace Meta.Adapters;

internal static class CSharpMetaStateSignature
{
    public static string Build(GenericMetadataState state)
    {
        var builder = new StringBuilder();
        Append(builder, state.Model.ComputeContractSignature());
        Append(builder, state.Instance.ModelName);

        var modeledEntityNames = state.Model.Entities
            .Select(entity => entity.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var canonicalIdsByEntity = BuildCanonicalIds(state);
        foreach (var entity in state.Model.Entities
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            var records = state.Instance.RecordsByEntity.TryGetValue(
                entity.Name,
                out var entityRecords)
                ? entityRecords
                : [];
            AppendEntityRecords(
                builder,
                entity,
                records,
                canonicalIdsByEntity);
        }

        foreach (var entityRecords in state.Instance.RecordsByEntity
                     .Where(item => !modeledEntityNames.Contains(item.Key))
                     .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Key, StringComparer.Ordinal))
        {
            AppendEntityRecords(
                builder,
                entityRecords.Key,
                entityRecords.Value);
        }

        return builder.ToString();
    }

    private static Dictionary<string, Dictionary<string, string>>
        BuildCanonicalIds(GenericMetadataState state)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var entityRecords in state.Instance.RecordsByEntity)
        {
            var ids = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var record in entityRecords.Value)
            {
                ids.TryAdd(record.Id, record.Id);
            }

            result[entityRecords.Key] = ids;
        }

        return result;
    }

    private static void AppendEntityRecords(
        StringBuilder builder,
        GenericEntity entity,
        IEnumerable<GenericRecord> records,
        IReadOnlyDictionary<string, Dictionary<string, string>>
            canonicalIdsByEntity)
    {
        Append(builder, "entity-records", entity.Name);
        var properties = entity.Properties
            .OrderBy(
                item => item.Name,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        var modeledProperties = properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationships = entity.Relationships
            .Select(relationship => new
            {
                Relationship = relationship,
                Name = relationship.GetColumnName(),
            })
            .OrderBy(
                item => item.Name,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        var modeledRelationships = relationships
            .Select(relationship => relationship.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records
                     .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            Append(builder, record.Id);
            foreach (var property in properties)
            {
                if (record.Values.TryGetValue(
                        property.Name,
                        out var value))
                {
                    Append(
                        builder,
                        "property",
                        property.Name,
                        value);
                }
            }

            foreach (var value in record.Values
                         .Where(item =>
                             !modeledProperties.Contains(item.Key))
                         .OrderBy(
                             item => item.Key,
                             StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Key, StringComparer.Ordinal))
            {
                Append(
                    builder,
                    "unknown-property",
                    value.Key,
                    value.Value);
            }

            foreach (var relationship in relationships)
            {
                if (!record.RelationshipIds.TryGetValue(
                        relationship.Name,
                        out var targetId))
                {
                    continue;
                }

                if (canonicalIdsByEntity.TryGetValue(
                        relationship.Relationship.Entity,
                        out var targetIds) &&
                    targetIds.TryGetValue(
                        targetId,
                        out var canonicalTargetId))
                {
                    targetId = canonicalTargetId;
                }

                Append(
                    builder,
                    "relationship",
                    relationship.Name,
                    targetId);
            }

            foreach (var relationship in record.RelationshipIds
                         .Where(item =>
                             !modeledRelationships.Contains(item.Key))
                         .OrderBy(
                             item => item.Key,
                             StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Key, StringComparer.Ordinal))
            {
                Append(
                    builder,
                    "unknown-relationship",
                    relationship.Key,
                    relationship.Value);
            }
        }
    }

    private static void AppendEntityRecords(
        StringBuilder builder,
        string entityName,
        IEnumerable<GenericRecord> records)
    {
        Append(builder, "entity-records", entityName);
        foreach (var record in records
                     .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            Append(builder, record.Id);
            foreach (var value in record.Values
                         .OrderBy(
                             item => item.Key,
                             StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Key, StringComparer.Ordinal))
            {
                Append(builder, "property", value.Key, value.Value);
            }

            foreach (var relationship in record.RelationshipIds
                         .OrderBy(
                             item => item.Key,
                             StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Key, StringComparer.Ordinal))
            {
                Append(
                    builder,
                    "relationship",
                    relationship.Key,
                    relationship.Value);
            }
        }
    }

    private static void Append(
        StringBuilder builder,
        params string?[] values)
    {
        foreach (var value in values)
        {
            if (value == null)
            {
                builder.Append("-1:");
                continue;
            }

            builder.Append(value.Length);
            builder.Append(':');
            builder.Append(value);
        }

        builder.AppendLine();
    }
}
