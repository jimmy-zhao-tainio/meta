internal sealed partial class CliRuntime
{
    async Task<int> InstanceRelationshipAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 3)
        {
            return PrintUsageError("Usage: instance relationship <set|list> ...");
        }
    
        var mode = commandArgs[2].Trim().ToLowerInvariant();
        return mode switch
        {
            "set" => await InstanceRelationshipSetAsync(commandArgs).ConfigureAwait(false),
            "list" => await InstanceRelationshipListAsync(commandArgs).ConfigureAwait(false),
            _ => PrintCommandUnknownError($"instance relationship {mode}"),
        };
    }
}

