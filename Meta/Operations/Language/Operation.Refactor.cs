namespace Meta.Operations;

public abstract partial record Operation
{
    public sealed record PropertyToRelationship : RefactorOperation
    {
        public PropertyToRelationship(
            string SourceEntityName,
            string SourcePropertyName,
            string TargetEntityName,
            string LookupPropertyName,
            string? Role = null,
            bool PreserveProperty = false)
        {
            this.SourceEntityName = RequireName(
                SourceEntityName,
                "Source entity name.");
            this.SourcePropertyName = RequireName(
                SourcePropertyName,
                "Source property name.");
            this.TargetEntityName = RequireName(
                TargetEntityName,
                "Target entity name.");
            this.LookupPropertyName = MetaName.Comparer.Equals(
                LookupPropertyName,
                "Id")
                ? "Id"
                : RequireName(LookupPropertyName, "Lookup property name.");
            this.Role = string.IsNullOrWhiteSpace(Role) ||
                   MetaName.Comparer.Equals(Role.Trim(), TargetEntityName)
                ? string.Empty
                : RequireName(Role.Trim(), "Relationship role.");
            this.PreserveProperty = PreserveProperty;
        }

        public string SourceEntityName { get; }
        public string SourcePropertyName { get; }
        public string TargetEntityName { get; }
        public string LookupPropertyName { get; }
        public string Role { get; }
        public bool PreserveProperty { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record RelationshipToProperty : RefactorOperation
    {
        public RelationshipToProperty(
            string SourceEntityName,
            string TargetEntityName,
            string? Role = null,
            string? PropertyName = null)
        {
            this.SourceEntityName = RequireName(
                SourceEntityName,
                "Source entity name.");
            this.TargetEntityName = RequireName(
                TargetEntityName,
                "Target entity name.");
            this.Role = string.IsNullOrWhiteSpace(Role) ||
                   MetaName.Comparer.Equals(Role.Trim(), TargetEntityName)
                ? string.Empty
                : RequireName(Role.Trim(), "Relationship role.");
            this.PropertyName = string.IsNullOrWhiteSpace(PropertyName)
                ? string.Empty
                : RequireName(PropertyName.Trim(), "Property name.");
        }

        public string SourceEntityName { get; }
        public string TargetEntityName { get; }
        public string Role { get; }
        public string PropertyName { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }
}
