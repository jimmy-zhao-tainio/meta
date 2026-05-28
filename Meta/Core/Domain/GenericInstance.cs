using System;
using System.Collections.Generic;

namespace Meta.Core.Domain;

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
}

public sealed class GenericRecord
{
    public string Id { get; set; } = string.Empty;
    public string SourceShardFileName { get; set; } = string.Empty;
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> RelationshipIds { get; } = new(StringComparer.OrdinalIgnoreCase);
}

