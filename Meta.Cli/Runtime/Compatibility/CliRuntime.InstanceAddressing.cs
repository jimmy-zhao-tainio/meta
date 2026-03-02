internal sealed partial class CliRuntime
{
    string BuildEntityInstanceAddress(string entityName, string id)
    {
        return $"{entityName} {QuoteInstanceId(id)}";
    }

    string QuoteInstanceId(string id)
    {
        var value = id ?? string.Empty;
        if (value.IndexOfAny([' ', '\t', '"']) >= 0)
        {
            return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }
    
        return value;
    }

    bool TryGetRelationshipId(GenericRecord record, string relationshipEntity, out string relationshipId)
    {
        if (record.RelationshipIds.TryGetValue(relationshipEntity, out var directValue) &&
            !string.IsNullOrWhiteSpace(directValue))
        {
            relationshipId = directValue;
            return true;
        }
    
        foreach (var pair in record.RelationshipIds)
        {
            if (string.Equals(pair.Key, relationshipEntity, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(pair.Value))
            {
                relationshipId = pair.Value;
                return true;
            }
        }
    
        relationshipId = string.Empty;
        return false;
    }
}


