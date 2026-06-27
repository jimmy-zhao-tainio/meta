internal sealed partial class CliRuntime
{
    async Task<int> QueryAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var options = ReadQueryCommandOptions(commandArgs, startIndex: 2);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        try
        {
            var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
            var rows = QueryRows(workspace, entityName, options.Filters);
            var renderedFilter = BuildFilterSummary(options.Filters);
            PrintQueryResult(workspace, entityName, renderedFilter, rows, options.Top);

            return 0;
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}

