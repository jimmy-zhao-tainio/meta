internal sealed partial class CliRuntime
{
    async Task<int> ViewInstanceAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var id = RequiredValue("Id");
        var options = ReadWorkspaceOnlyOptions(commandArgs, startIndex: 4);
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


