internal sealed partial class CliRuntime
{
    async Task<int> WorkspaceAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 2)
        {
            return PrintUsageError("Usage: workspace <merge> ...");
        }

        var mode = commandArgs[1].Trim().ToLowerInvariant();
        return mode switch
        {
            "merge" => await WorkspaceMergeAsync(commandArgs).ConfigureAwait(false),
            _ => PrintCommandUnknownError($"workspace {mode}"),
        };
    }
}
