namespace Meta.Core.Operations;

public sealed class AddEntityOperation : ModelOperation
{
    public AddEntityOperation(string entityName)
    {
        EntityName = entityName;
    }

    public string EntityName { get; }
}

public sealed class RemoveEntityOperation : ModelOperation
{
    public RemoveEntityOperation(string entityName)
    {
        EntityName = entityName;
    }

    public string EntityName { get; }
}
