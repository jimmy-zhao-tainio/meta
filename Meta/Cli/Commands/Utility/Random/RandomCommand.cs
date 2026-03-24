internal sealed partial class CliRuntime
{
    async Task<int> RandomAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 2)
        {
            return PrintUsageError("Usage: random create [options]");
        }
    
        var mode = commandArgs[1].Trim().ToLowerInvariant();
        return mode switch
        {
            "create" => await RandomCreateAsync(commandArgs).ConfigureAwait(false),
            _ => UnknownRandomCommand(mode),
        };
    }
    
    int UnknownRandomCommand(string mode)
    {
        return PrintCommandUnknownError($"random {mode}");
    }
}
