namespace Meta.Operations;

public abstract partial record Operation
{
    public sealed record InsertRecord : InstanceOperation
    {
        public InsertRecord(
            string EntityName,
            string Id,
            IReadOnlyDictionary<string, string>? Values = null,
            IReadOnlyDictionary<string, string>? RelationshipIds = null)
        {
            this.EntityName = RequireName(EntityName, "Entity name.");
            this.Id = RequireIdentity(Id, "Record Id.");
            this.Values = CopyValues(Values, identities: false);
            this.RelationshipIds = CopyValues(
                RelationshipIds,
                identities: true);
        }

        public string EntityName { get; }
        public string Id { get; }
        public IReadOnlyDictionary<string, string> Values { get; }
        public IReadOnlyDictionary<string, string> RelationshipIds { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record DeleteRecord : InstanceOperation
    {
        public DeleteRecord(string EntityName, string Id)
        {
            this.EntityName = RequireName(EntityName, "Entity name.");
            this.Id = RequireIdentity(Id, "Record Id.");
        }

        public string EntityName { get; }
        public string Id { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record RenameRecord : InstanceOperation
    {
        public RenameRecord(string EntityName, string Id, string NewId)
        {
            this.EntityName = RequireName(EntityName, "Entity name.");
            this.Id = RequireIdentity(Id, "Record Id.");
            this.NewId = RequireIdentity(NewId, "New record Id.");
            if (string.Equals(this.Id, this.NewId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Old Id and new Id must differ.");
            }
        }

        public string EntityName { get; }
        public string Id { get; }
        public string NewId { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record SetProperty : InstanceOperation
    {
        public SetProperty(
            string EntityName,
            string Id,
            string PropertyName,
            string Value)
        {
            this.EntityName = RequireName(EntityName, "Entity name.");
            this.Id = RequireIdentity(Id, "Record Id.");
            this.PropertyName = RequireName(
                PropertyName,
                "Property name.");
            this.Value = RequireText(Value, "Property value.");
        }

        public string EntityName { get; }
        public string Id { get; }
        public string PropertyName { get; }
        public string Value { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record ClearProperty : InstanceOperation
    {
        public ClearProperty(
            string EntityName,
            string Id,
            string PropertyName)
        {
            this.EntityName = RequireName(EntityName, "Entity name.");
            this.Id = RequireIdentity(Id, "Record Id.");
            this.PropertyName = RequireName(
                PropertyName,
                "Property name.");
        }

        public string EntityName { get; }
        public string Id { get; }
        public string PropertyName { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record SetRelationship : InstanceOperation
    {
        public SetRelationship(
            string EntityName,
            string Id,
            string RelationshipName,
            string TargetId)
        {
            this.EntityName = RequireName(EntityName, "Entity name.");
            this.Id = RequireIdentity(Id, "Record Id.");
            this.RelationshipName = RequireName(
                RelationshipName,
                "Relationship name.");
            this.TargetId = RequireIdentity(TargetId, "Target record Id.");
        }

        public string EntityName { get; }
        public string Id { get; }
        public string RelationshipName { get; }
        public string TargetId { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record ClearRelationship : InstanceOperation
    {
        public ClearRelationship(
            string EntityName,
            string Id,
            string RelationshipName)
        {
            this.EntityName = RequireName(EntityName, "Entity name.");
            this.Id = RequireIdentity(Id, "Record Id.");
            this.RelationshipName = RequireName(
                RelationshipName,
                "Relationship name.");
        }

        public string EntityName { get; }
        public string Id { get; }
        public string RelationshipName { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }
}
