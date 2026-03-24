internal sealed partial class CliRuntime
{
    async Task<int> ViewInstanceAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 4)
        {
            return PrintUsageError("Usage: view instance <Entity> <Id> [--workspace <path>]");
        }
    
        var entityName = commandArgs[2];
        var id = commandArgs[3];
        var options = ParseWorkspaceOnlyOptions(commandArgs, startIndex: 4);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }
    
        var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
        PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
        RequireEntity(workspace, entityName);
        var row = ResolveRowById(workspace, entityName, id);

        PrintSelectedRecord(entityName, row);
        return 0;
    }
}


