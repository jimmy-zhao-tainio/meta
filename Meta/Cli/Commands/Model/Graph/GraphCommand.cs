internal sealed partial class CliRuntime
{
    async Task<int> GraphAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 2)
        {
            return PrintUsageError("Usage: graph <stats|inbound> ...");
        }
    
        var mode = commandArgs[1].Trim().ToLowerInvariant();
        return mode switch
        {
            "stats" => await GraphStatsAsync(commandArgs).ConfigureAwait(false),
            "inbound" => await GraphInboundAsync(commandArgs).ConfigureAwait(false),
            _ => UnknownGraphCommand(mode),
        };
    }
    
    int UnknownGraphCommand(string mode)
    {
        return PrintCommandUnknownError($"graph {mode}");
    }
}
