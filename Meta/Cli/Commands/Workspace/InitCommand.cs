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

        var workspace = new Workspace
        {
            WorkspaceRootPath = workspaceRoot,
            MetadataRootPath = metadataRoot,
            WorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace.CreateDefault(),
            Model = new GenericModel
            {
                Name = "MetadataModel",
            },
            Instance = new GenericInstance
            {
                ModelName = "MetadataModel",
            },
            IsDirty = true,
        };

        await services.WorkspaceService.SaveAsync(workspace).ConfigureAwait(false);
        presenter.WriteOk(
            "workspace initialized",
            ("Path", workspaceRoot));

        return 0;
    }
}



