internal sealed partial class CliRuntime
{
    async Task<int> InitWorkspaceAsync(string[] commandArgs)
    {
        var workspacePath = OptionalValue("path", ".");
        var workspaceRoot = Path.GetFullPath(workspacePath);
        var metadataRoot = workspaceRoot;

        if (WorkspaceLooksInitialized(workspaceRoot, metadataRoot))
        {
            presenter.WriteOk(
                "workspace already initialized",
                ("Path", workspaceRoot));

            return 0;
        }

        var workspace = new InMemoryWorkspace(
            new GenericModel
            {
                Name = "MetadataModel",
            },
            new GenericInstance
            {
                ModelName = "MetadataModel",
            });

        await XmlWorkspaceWriter.WriteNewAsync(workspace, workspaceRoot).ConfigureAwait(false);
        presenter.WriteOk(
            "workspace initialized",
            ("Path", workspaceRoot));

        return 0;
    }
}



