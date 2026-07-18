namespace Meta.Core.Serialization;

public readonly record struct InstanceRelationshipColumnAlias(
    string EntityName,
    string AttributeName,
    string RelationshipColumnName);

public sealed class InstanceXmlLoadOptions
{
    public InstanceXmlLoadOptions(IReadOnlyList<InstanceRelationshipColumnAlias> relationshipColumnAliases)
    {
        RelationshipColumnAliases = relationshipColumnAliases ??
            throw new ArgumentNullException(nameof(relationshipColumnAliases));
    }

    public IReadOnlyList<InstanceRelationshipColumnAlias> RelationshipColumnAliases { get; }
}
