internal sealed partial class CliRuntime
{
    async Task<int> GraphStatsAsync(string[] commandArgs)
    {
        var options = ParseGraphStatsOptions(commandArgs, startIndex: 2);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }
    
        try
        {
            var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
            var stats = GraphStatsService.Compute(workspace.Model, options.TopN, options.CycleSampleLimit);

            PrintGraphStats(workspace, stats, options.TopN);
    
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}

