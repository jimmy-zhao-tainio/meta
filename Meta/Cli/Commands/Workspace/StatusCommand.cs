internal sealed partial class CliRuntime
{
    async Task<int> StatusWorkspaceAsync(string[] commandArgs)
    {
        var options = ParseWorkspaceOnlyOptions(commandArgs, startIndex: 1);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }
    
        var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
        var dataSizes = CalculateWorkspaceDataSizes(workspace);
        PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
        PrintWorkspaceSummary(workspace);
    
        return 0;
    }
}

