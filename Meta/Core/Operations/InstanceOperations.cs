using System.Collections.ObjectModel;

namespace Meta.Core.Operations;

public sealed class InsertRecordOperation : InstanceOperation
{
    public InsertRecordOperation(
        string entityName,
        string id,
        IReadOnlyDictionary<string, string>? values = null,
        IReadOnlyDictionary<string, string>? relationshipIds = null)
    {
        EntityName = entityName;
        Id = id;
        Values = Copy(values);
        RelationshipIds = Copy(relationshipIds);
    }

    public string EntityName { get; }
    public string Id { get; }
    public IReadOnlyDictionary<string, string> Values { get; }
    public IReadOnlyDictionary<string, string> RelationshipIds { get; }

    private static IReadOnlyDictionary<string, string> Copy(
        IReadOnlyDictionary<string, string>? source)
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (source != null)
        {
            foreach (var pair in source)
            {
                copy.Add(pair.Key, pair.Value);
            }
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}

public sealed class SetPropertyOperation : InstanceOperation
{
    public SetPropertyOperation(
        string entityName,
        string id,
        string propertyName,
        string value)
    {
        EntityName = entityName;
        Id = id;
        PropertyName = propertyName;
        Value = value;
    }

    public string EntityName { get; }
    public string Id { get; }
    public string PropertyName { get; }
    public string Value { get; }
}

public sealed class ClearPropertyOperation : InstanceOperation
{
    public ClearPropertyOperation(
        string entityName,
        string id,
        string propertyName)
    {
        EntityName = entityName;
        Id = id;
        PropertyName = propertyName;
    }

    public string EntityName { get; }
    public string Id { get; }
    public string PropertyName { get; }
}

public sealed class SetRelationshipOperation : InstanceOperation
{
    public SetRelationshipOperation(
        string entityName,
        string id,
        string relationshipName,
        string targetId)
    {
        EntityName = entityName;
        Id = id;
        RelationshipName = relationshipName;
        TargetId = targetId;
    }

    public string EntityName { get; }
    public string Id { get; }
    public string RelationshipName { get; }
    public string TargetId { get; }
}

public sealed class ClearRelationshipOperation : InstanceOperation
{
    public ClearRelationshipOperation(
        string entityName,
        string id,
        string relationshipName)
    {
        EntityName = entityName;
        Id = id;
        RelationshipName = relationshipName;
    }

    public string EntityName { get; }
    public string Id { get; }
    public string RelationshipName { get; }
}

public sealed class DeleteRecordOperation : InstanceOperation
{
    public DeleteRecordOperation(string entityName, string id)
    {
        EntityName = entityName;
        Id = id;
    }

    public string EntityName { get; }
    public string Id { get; }
}
