namespace Meta.Operations;

public abstract partial record Operation
{
    public sealed record RenameModel : ModelOperation
    {
        public RenameModel(string Name, string NewName)
        {
            this.Name = RequireName(Name, "Model name.");
            this.NewName = RequireName(NewName, "New model name.");
        }

        public string Name { get; }
        public string NewName { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record AddEntity : ModelOperation
    {
        public AddEntity(string Name) =>
            this.Name = RequireName(Name, "Entity name.");

        public string Name { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record RemoveEntity : ModelOperation
    {
        public RemoveEntity(string Name) =>
            this.Name = RequireName(Name, "Entity name.");

        public string Name { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record RenameEntity : ModelOperation
    {
        public RenameEntity(string Name, string NewName)
        {
            this.Name = RequireName(Name, "Entity name.");
            this.NewName = RequireName(NewName, "New entity name.");
        }

        public string Name { get; }
        public string NewName { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record AddProperty : ModelOperation
    {
        public AddProperty(
            string EntityName,
            string Name,
            bool IsRequired,
            string? ExistingRecordValue = null)
        {
            this.EntityName = RequireName(EntityName, "Entity name.");
            this.Name = RequireName(Name, "Property name.");
            this.IsRequired = IsRequired;
            this.ExistingRecordValue = ExistingRecordValue;
        }

        public string EntityName { get; }
        public string Name { get; }
        public bool IsRequired { get; }
        public string? ExistingRecordValue { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record RemoveProperty : ModelOperation
    {
        public RemoveProperty(string EntityName, string Name)
        {
            this.EntityName = RequireName(EntityName, "Entity name.");
            this.Name = RequireName(Name, "Property name.");
        }

        public string EntityName { get; }
        public string Name { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record RenameProperty : ModelOperation
    {
        public RenameProperty(
            string EntityName,
            string Name,
            string NewName)
        {
            this.EntityName = RequireName(EntityName, "Entity name.");
            this.Name = RequireName(Name, "Property name.");
            this.NewName = RequireName(NewName, "New property name.");
        }

        public string EntityName { get; }
        public string Name { get; }
        public string NewName { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record SetPropertyRequired : ModelOperation
    {
        public SetPropertyRequired(
            string EntityName,
            string Name,
            bool IsRequired,
            string? MissingRecordValue = null)
        {
            this.EntityName = RequireName(EntityName, "Entity name.");
            this.Name = RequireName(Name, "Property name.");
            this.IsRequired = IsRequired;
            this.MissingRecordValue = MissingRecordValue;
        }

        public string EntityName { get; }
        public string Name { get; }
        public bool IsRequired { get; }
        public string? MissingRecordValue { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record AddRelationship : ModelOperation
    {
        public AddRelationship(
            string SourceEntityName,
            string TargetEntityName,
            string? Role,
            bool IsRequired,
            string? ExistingRecordTargetId = null)
        {
            this.SourceEntityName = RequireName(
                SourceEntityName,
                "Source entity name.");
            this.TargetEntityName = RequireName(
                TargetEntityName,
                "Target entity name.");
            this.Role = Role is null || Role.Length == 0
                ? null
                : RequireName(Role, "Relationship role.");
            this.IsRequired = IsRequired;
            this.ExistingRecordTargetId = ExistingRecordTargetId is null
                ? null
                : RequireIdentity(
                    ExistingRecordTargetId,
                    "Existing record target Id.");
        }

        public string SourceEntityName { get; }
        public string TargetEntityName { get; }
        public string? Role { get; }
        public bool IsRequired { get; }
        public string? ExistingRecordTargetId { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record RemoveRelationship : ModelOperation
    {
        public RemoveRelationship(string SourceEntityName, string Name)
        {
            this.SourceEntityName = RequireName(
                SourceEntityName,
                "Source entity name.");
            this.Name = RequireName(Name, "Relationship name.");
        }

        public string SourceEntityName { get; }
        public string Name { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record RenameRelationship : ModelOperation
    {
        public RenameRelationship(
            string SourceEntityName,
            string Name,
            string NewRole)
        {
            this.SourceEntityName = RequireName(
                SourceEntityName,
                "Source entity name.");
            this.Name = RequireName(Name, "Relationship name.");
            ArgumentNullException.ThrowIfNull(NewRole);
            this.NewRole = NewRole.Length == 0
                ? string.Empty
                : RequireName(NewRole, "New relationship role.");
        }

        public string SourceEntityName { get; }
        public string Name { get; }
        public string NewRole { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record RetargetRelationship : ModelOperation
    {
        public RetargetRelationship(
            string SourceEntityName,
            string Name,
            string TargetEntityName)
        {
            this.SourceEntityName = RequireName(
                SourceEntityName,
                "Source entity name.");
            this.Name = RequireName(Name, "Relationship name.");
            this.TargetEntityName = RequireName(
                TargetEntityName,
                "Target entity name.");
        }

        public string SourceEntityName { get; }
        public string Name { get; }
        public string TargetEntityName { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }

    public sealed record SetRelationshipRequired : ModelOperation
    {
        public SetRelationshipRequired(
            string SourceEntityName,
            string Name,
            bool IsRequired,
            string? MissingRecordTargetId = null)
        {
            this.SourceEntityName = RequireName(
                SourceEntityName,
                "Source entity name.");
            this.Name = RequireName(Name, "Relationship name.");
            this.IsRequired = IsRequired;
            this.MissingRecordTargetId = MissingRecordTargetId is null
                ? null
                : RequireIdentity(
                    MissingRecordTargetId,
                    "Missing record target Id.");
        }

        public string SourceEntityName { get; }
        public string Name { get; }
        public bool IsRequired { get; }
        public string? MissingRecordTargetId { get; }

        public override OperationResult ApplyTo(IOperationTarget target) =>
            target.Apply(this);
    }
}
