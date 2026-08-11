namespace Meta.Surfaces;

public sealed class WorkspacePublicationException : IOException
{
    public WorkspacePublicationException(
        string workspacePath,
        string recoveryPath,
        Exception publicationFailure,
        Exception rollbackFailure)
        : base(
            $"C# workspace publication failed for '{workspacePath}'. " +
            $"Rollback also failed: {rollbackFailure.Message}. " +
            $"The recovery backup was preserved at '{recoveryPath}'.",
            new AggregateException(
                "C# workspace publication and rollback failed.",
                publicationFailure,
                rollbackFailure))
    {
        WorkspacePath = workspacePath;
        RecoveryPath = recoveryPath;
        PublicationFailure = publicationFailure;
        RollbackFailure = rollbackFailure;
    }

    public string WorkspacePath { get; }

    public string RecoveryPath { get; }

    public Exception PublicationFailure { get; }

    public Exception RollbackFailure { get; }
}
