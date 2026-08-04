
namespace Meta.Core.Operations;

public static class InMemoryOperations
{
    public static InMemoryWorkspace Apply(
        InMemoryWorkspace source,
        params Operation[] operations)
    {
        return Apply(source, (IReadOnlyList<Operation>)operations);
    }

    public static InMemoryWorkspace Apply(
        InMemoryWorkspace source,
        IReadOnlyList<Operation> operations)
    {
        return Execute(source, operations).Workspace;
    }

    public static InMemoryOperationResult Execute(
        InMemoryWorkspace source,
        params Operation[] operations)
    {
        return Execute(source, (IReadOnlyList<Operation>)operations);
    }

    public static InMemoryOperationResult Execute(
        InMemoryWorkspace source,
        IReadOnlyList<Operation> operations)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Any(operation => operation == null))
        {
            throw new ArgumentException(
                "Operations cannot contain null.",
                nameof(operations));
        }

        EnsureValid(source, "Source metadata is invalid.");
        var candidate = source.Clone();
        var target = new InMemoryOperationTarget(candidate);
        var results = new List<OperationResult>(operations.Count);

        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            try
            {
                results.Add(operation.ApplyTo(target));
                var diagnostics = WorkspaceValidator.Validate(
                    candidate.Model,
                    candidate.Instance);
                if (diagnostics.HasErrors)
                {
                    throw new MetaOperationException(
                        index,
                        operation,
                        new InvalidOperationException(BuildValidationMessage(
                            diagnostics,
                            $"Operation {index + 1} ({operation.GetType().Name}) produced invalid metadata.")),
                        diagnostics);
                }
            }
            catch (MetaOperationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new MetaOperationException(index, operation, exception);
            }
        }

        return new InMemoryOperationResult(candidate, results);
    }

    private static void EnsureValid(
        InMemoryWorkspace workspace,
        string message)
    {
        var diagnostics = WorkspaceValidator.Validate(
            workspace.Model,
            workspace.Instance);
        if (!diagnostics.HasErrors)
        {
            return;
        }

        throw new InvalidOperationException(
            BuildValidationMessage(diagnostics, message));
    }

    private static string BuildValidationMessage(
        WorkspaceDiagnostics diagnostics,
        string message)
    {
        var errors = diagnostics.Issues
            .Where(issue => issue.Severity == IssueSeverity.Error)
            .Take(5)
            .Select(issue =>
                $"{issue.Code} {issue.Location} - {issue.Message}");
        return message + " " + string.Join(" | ", errors);
    }
}

internal sealed partial class InMemoryOperationTarget : IOperationTarget
{
    private readonly InMemoryWorkspace _workspace;

    public InMemoryOperationTarget(InMemoryWorkspace workspace)
    {
        _workspace = workspace;
    }

    RenameModelResult IOperationTarget.Apply(Operation.RenameModel operation) => Apply(_workspace, operation);
    OperationResult IOperationTarget.Apply(Operation.AddEntity operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.RemoveEntity operation) => Complete(operation, Apply);
    RenameEntityResult IOperationTarget.Apply(Operation.RenameEntity operation) => Apply(_workspace, operation);
    OperationResult IOperationTarget.Apply(Operation.AddProperty operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.RemoveProperty operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.RenameProperty operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.SetPropertyRequired operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.AddRelationship operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.RemoveRelationship operation) => Complete(operation, Apply);
    RenameRelationshipResult IOperationTarget.Apply(Operation.RenameRelationship operation) => Apply(_workspace, operation);
    OperationResult IOperationTarget.Apply(Operation.RetargetRelationship operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.SetRelationshipRequired operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.InsertRecord operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.DeleteRecord operation) => Complete(operation, Apply);
    RenameRecordResult IOperationTarget.Apply(Operation.RenameRecord operation) => Apply(_workspace, operation);
    OperationResult IOperationTarget.Apply(Operation.SetProperty operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.ClearProperty operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.SetRelationship operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.ClearRelationship operation) => Complete(operation, Apply);
    PropertyToRelationshipResult IOperationTarget.Apply(
        Operation.PropertyToRelationship operation) =>
        Apply(_workspace, operation);
    RelationshipToPropertyResult IOperationTarget.Apply(
        Operation.RelationshipToProperty operation) =>
        Apply(_workspace, operation);

    private OperationResult Complete<T>(
        T operation,
        Action<InMemoryWorkspace, T> apply)
    {
        apply(_workspace, operation);
        return OperationResult.Completed;
    }

    private static GenericEntity RequireEntity(
        InMemoryWorkspace state,
        string name)
    {
        var requiredName = MetaName.Require(name, "Entity name.");
        return state.Model.FindEntity(requiredName) ??
               throw new InvalidOperationException(
                   $"Entity '{requiredName}' does not exist.");
    }

    private static GenericProperty RequireProperty(
        GenericEntity entity,
        string name)
    {
        var requiredName = MetaName.Require(name, "Property name.");
        return entity.Properties.FirstOrDefault(property =>
                   MetaName.Comparer.Equals(property.Name, requiredName)) ??
               throw new InvalidOperationException(
                   $"Property '{entity.Name}.{requiredName}' does not exist.");
    }

    private static GenericRelationship RequireRelationship(
        GenericEntity entity,
        string name)
    {
        var requiredName = MetaName.Require(name, "Relationship name.");
        return entity.Relationships.FirstOrDefault(relationship =>
                   MetaName.Comparer.Equals(
                       relationship.GetColumnName(),
                       requiredName) ||
                   MetaName.Comparer.Equals(
                       relationship.GetRoleOrDefault(),
                       requiredName)) ??
               throw new InvalidOperationException(
                   $"Relationship '{entity.Name}.{requiredName}' does not exist.");
    }

    private static GenericRecord RequireRecord(
        InMemoryWorkspace state,
        GenericEntity entity,
        string id)
    {
        var requiredId = MetaIdentity.Require(id, "Record Id.");
        var records = GetRecords(state, entity.Name);
        return records.FirstOrDefault(record =>
                   MetaIdentity.Comparer.Equals(record.Id, requiredId)) ??
               throw new InvalidOperationException(
                   $"Record '{entity.Name}:{requiredId}' does not exist.");
    }

    private static List<GenericRecord> GetRecords(
        InMemoryWorkspace state,
        string entityName)
    {
        return state.Instance.RecordsByEntity.TryGetValue(
            entityName,
            out var records)
            ? records
            : [];
    }

    private static void EnsurePropertyNameAvailable(
        GenericEntity entity,
        string name,
        object? except = null)
    {
        var collision =
            MetaName.Comparer.Equals(entity.Name, name) ||
            entity.Properties.Any(property =>
                !ReferenceEquals(property, except) &&
                MetaName.Comparer.Equals(property.Name, name)) ||
            entity.Relationships.Any(relationship =>
                !ReferenceEquals(relationship, except) &&
                (MetaName.Comparer.Equals(
                     relationship.GetColumnName(),
                     name) ||
                 MetaName.Comparer.Equals(
                     relationship.GetNavigationName(),
                     name)));
        if (collision)
        {
            throw new InvalidOperationException(
                $"Member '{entity.Name}.{name}' already exists.");
        }
    }

    private static void EnsureRelationshipNameAvailable(
        GenericEntity entity,
        GenericRelationship candidate,
        object? except = null,
        GenericProperty? replacedProperty = null)
    {
        var columnName = candidate.GetColumnName();
        var navigationName = candidate.GetNavigationName();
        var collision =
            MetaName.Comparer.Equals(
                entity.Name,
                navigationName) ||
            entity.Properties.Any(property =>
                !ReferenceEquals(property, replacedProperty) &&
                (MetaName.Comparer.Equals(
                    property.Name,
                    columnName) ||
                 MetaName.Comparer.Equals(
                     property.Name,
                     navigationName))) ||
            entity.Relationships.Any(relationship =>
                !ReferenceEquals(relationship, except) &&
                (MetaName.Comparer.Equals(
                     relationship.GetColumnName(),
                     columnName) ||
                 MetaName.Comparer.Equals(
                     relationship.GetNavigationName(),
                     navigationName)));
        if (collision)
        {
            throw new InvalidOperationException(
                $"Relationship '{entity.Name}.{navigationName}' conflicts with an existing member.");
        }
    }
}
