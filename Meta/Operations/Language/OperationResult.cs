namespace Meta.Operations;

public record OperationResult
{
    public static OperationResult Completed { get; } = new();
}

public sealed record RenameModelResult(
    string OldName,
    string NewName) : OperationResult;

public sealed record RenameEntityResult(
    string OldName,
    string NewName,
    long RecordCount,
    long RelationshipCount,
    long RelationshipValueCount) : OperationResult;

public sealed record RenameRelationshipResult(
    string SourceEntityName,
    string TargetEntityName,
    string OldName,
    string NewName,
    long RelationshipValueCount) : OperationResult;

public sealed record RenameRecordResult(
    string EntityName,
    string OldId,
    string NewId,
    long RelationshipValueCount) : OperationResult;

public sealed record PropertyToRelationshipResult(
    long SourceRecordCount,
    long RelationshipValueCount,
    bool PropertyRemoved,
    string RelationshipName) : OperationResult;

public sealed record RelationshipToPropertyResult(
    long SourceRecordCount,
    long PropertyValueCount,
    bool IsRequired,
    string PropertyName) : OperationResult;

public sealed record InMemoryOperationResult(
    InMemoryWorkspace Workspace,
    IReadOnlyList<OperationResult> Results);
