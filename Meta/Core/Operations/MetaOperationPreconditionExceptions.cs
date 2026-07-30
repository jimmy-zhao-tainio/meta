namespace Meta.Core.Operations;

public sealed class ExistingRecordsRequirePropertyValueException
    : InvalidOperationException
{
    public ExistingRecordsRequirePropertyValueException(
        string entityName,
        string propertyName)
        : base(
            $"Property '{entityName}.{propertyName}' needs a value for existing records.")
    {
        EntityName = entityName;
        PropertyName = propertyName;
    }

    public string EntityName { get; }
    public string PropertyName { get; }
}

public sealed class ExistingRecordsRequireRelationshipTargetException
    : InvalidOperationException
{
    public ExistingRecordsRequireRelationshipTargetException(
        string entityName,
        string relationshipName)
        : base(
            $"Relationship '{entityName}.{relationshipName}' needs a target for existing records.")
    {
        EntityName = entityName;
        RelationshipName = relationshipName;
    }

    public string EntityName { get; }
    public string RelationshipName { get; }
}
