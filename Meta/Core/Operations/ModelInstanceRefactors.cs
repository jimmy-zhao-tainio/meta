namespace Meta.Core.Operations;

public sealed class AddPropertyOperation : ModelInstanceRefactor
{
    public AddPropertyOperation(
        string entityName,
        string propertyName,
        bool isRequired,
        string? existingRecordValue = null)
    {
        EntityName = entityName;
        PropertyName = propertyName;
        IsRequired = isRequired;
        ExistingRecordValue = existingRecordValue;
    }

    public string EntityName { get; }
    public string PropertyName { get; }
    public bool IsRequired { get; }
    public string? ExistingRecordValue { get; }
}

public sealed class RemovePropertyOperation : ModelInstanceRefactor
{
    public RemovePropertyOperation(
        string entityName,
        string propertyName)
    {
        EntityName = entityName;
        PropertyName = propertyName;
    }

    public string EntityName { get; }
    public string PropertyName { get; }
}

public sealed class RenamePropertyOperation : ModelInstanceRefactor
{
    public RenamePropertyOperation(
        string entityName,
        string propertyName,
        string newPropertyName)
    {
        EntityName = entityName;
        PropertyName = propertyName;
        NewPropertyName = newPropertyName;
    }

    public string EntityName { get; }
    public string PropertyName { get; }
    public string NewPropertyName { get; }
}

public sealed class SetPropertyRequiredOperation : ModelInstanceRefactor
{
    public SetPropertyRequiredOperation(
        string entityName,
        string propertyName,
        bool isRequired,
        string? missingRecordValue = null)
    {
        EntityName = entityName;
        PropertyName = propertyName;
        IsRequired = isRequired;
        MissingRecordValue = missingRecordValue;
    }

    public string EntityName { get; }
    public string PropertyName { get; }
    public bool IsRequired { get; }
    public string? MissingRecordValue { get; }
}

public sealed class AddRelationshipOperation : ModelInstanceRefactor
{
    public AddRelationshipOperation(
        string sourceEntityName,
        string targetEntityName,
        string role,
        bool isRequired,
        string? existingRecordTargetId = null)
    {
        SourceEntityName = sourceEntityName;
        TargetEntityName = targetEntityName;
        Role = role;
        IsRequired = isRequired;
        ExistingRecordTargetId = existingRecordTargetId;
    }

    public string SourceEntityName { get; }
    public string TargetEntityName { get; }
    public string Role { get; }
    public bool IsRequired { get; }
    public string? ExistingRecordTargetId { get; }
}

public sealed class RemoveRelationshipOperation : ModelInstanceRefactor
{
    public RemoveRelationshipOperation(
        string sourceEntityName,
        string relationshipName)
    {
        SourceEntityName = sourceEntityName;
        RelationshipName = relationshipName;
    }

    public string SourceEntityName { get; }
    public string RelationshipName { get; }
}
