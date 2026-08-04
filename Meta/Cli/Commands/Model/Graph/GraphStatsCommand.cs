internal sealed partial class CliRuntime
{
    async Task<int> GraphStatsAsync(string[] commandArgs)
    {
        var options = ReadGraphStatsOptions(commandArgs, startIndex: 2);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        try
        {
            var model = await WorkspaceComposition.MaterializeModelAsync(CurrentWorkspace)
                .ConfigureAwait(false);
            var stats = GraphStatsService.Compute(model, options.TopN, options.CycleSampleLimit);

            PrintGraphStats(model, stats, options.TopN);

            return 0;
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}

