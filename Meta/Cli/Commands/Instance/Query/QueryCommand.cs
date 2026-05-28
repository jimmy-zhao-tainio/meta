internal sealed partial class CliRuntime
{
    async Task<int> QueryAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 2)
        {
            return PrintUsageError("Usage: query <Entity> [--equals <Field> <Value>]... [--contains <Field> <Value>]... [--top <n>] [--workspace <path>]");
        }
    
        var entityName = commandArgs[1];
        var options = ParseQueryCommandOptions(commandArgs, startIndex: 2);
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

