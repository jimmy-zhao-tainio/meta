namespace Meta.Surfaces;

internal enum CSharpWorkspacePublicationCheckpoint
{
    AfterNewStatePublished,
    BeforeRollback,
    BeforeRestore,
}

internal static class CSharpWorkspacePublicationTestHooks
{
    public static Action<string, CSharpWorkspacePublicationCheckpoint>? Checkpoint { get; set; }

    public static void Invoke(
        string workspacePath,
        CSharpWorkspacePublicationCheckpoint checkpoint) =>
        Checkpoint?.Invoke(workspacePath, checkpoint);
}
