namespace Meta.Surfaces.CSharp;

internal enum CSharpWorkspacePublicationCheckpoint
{
    AfterCreationSourcesMoved,
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
