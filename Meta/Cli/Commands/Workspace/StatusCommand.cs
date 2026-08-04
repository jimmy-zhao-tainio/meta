internal sealed partial class CliRuntime
{
    async Task<int> StatusWorkspaceAsync(string[] commandArgs)
    {
        var options = ReadWorkspaceOnlyOptions(commandArgs, startIndex: 1);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        await PrintWorkspaceSummaryAsync(CurrentWorkspace).ConfigureAwait(false);

        return 0;
    }
}

