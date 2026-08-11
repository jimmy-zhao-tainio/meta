using System;
using System.Collections.Generic;

namespace Meta.Operations.Domain;

public sealed class GenericInstance
{
    public string ModelName { get; set; } = string.Empty;
    public Dictionary<string, List<GenericRecord>> RecordsByEntity { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<GenericRecord> GetOrCreateEntityRecords(string entityName)
    {
        if (!RecordsByEntity.TryGetValue(entityName, out var records))
        {
            records = new List<GenericRecord>();
            RecordsByEntity[entityName] = records;
        }

        return records;
    }

    public GenericInstance Clone()
    {
        var clone = new GenericInstance
        {
            ModelName = ModelName,
        };

        foreach (var entityRecords in RecordsByEntity)
        {
            var records = clone.GetOrCreateEntityRecords(entityRecords.Key);
            foreach (var source in entityRecords.Value)
            {
                var record = new GenericRecord
                {
                    Id = source.Id,
                };

                foreach (var value in source.Values)
                {
                    record.Values.Add(value.Key, value.Value);
                }

                foreach (var relationship in source.RelationshipIds)
                {
                    record.RelationshipIds.Add(
                        relationship.Key,
                        relationship.Value);
                }

                records.Add(record);
            }
        }

        return clone;
    }
}

public sealed class GenericRecord
{
    public string Id { get; set; } = string.Empty;
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> RelationshipIds { get; } = new(StringComparer.OrdinalIgnoreCase);
}
