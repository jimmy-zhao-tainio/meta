namespace Meta.Surfaces;

public sealed class WorkspaceCreationException : IOException
{
    public WorkspaceCreationException(
        string workspacePath,
        string stagingPath,
        Exception creationFailure,
        Exception cleanupFailure)
        : base(
            $"C# workspace creation failed for '{workspacePath}'. " +
            $"Cleanup also failed: {cleanupFailure.Message}. " +
            $"The staging directory was preserved at '{stagingPath}'.",
            new AggregateException(
                "C# workspace creation and cleanup failed.",
                creationFailure,
                cleanupFailure))
    {
        WorkspacePath = workspacePath;
        StagingPath = stagingPath;
        CreationFailure = creationFailure;
        CleanupFailure = cleanupFailure;
    }

    public string WorkspacePath { get; }

    public string StagingPath { get; }

    public Exception CreationFailure { get; }

    public Exception CleanupFailure { get; }
}
