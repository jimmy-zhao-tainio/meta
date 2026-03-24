internal sealed partial class CliRuntime
{
    async Task<int> InitWorkspaceAsync(string[] commandArgs)
    {
        if (commandArgs.Length > 2)
        {
            return PrintUsageError("Usage: init [<path>]");
        }
    
        var workspacePath = commandArgs.Length == 2 ? commandArgs[1] : ".";
        var workspaceRoot = Path.GetFullPath(workspacePath);
        var metadataRoot = Path.Combine(workspaceRoot, "metadata");
    
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



