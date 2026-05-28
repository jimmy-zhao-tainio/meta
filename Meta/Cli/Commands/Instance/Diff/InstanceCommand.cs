internal sealed partial class CliRuntime
{
    async Task<int> InstanceAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 2)
        {
            return PrintUsageError("Usage: instance <diff|merge|diff-aligned|merge-aligned|update|rename-id|relationship> ...");
        }

        var mode = commandArgs[1].Trim().ToLowerInvariant();
        return mode switch
        {
            "diff" => await InstanceDiffAsync(commandArgs).ConfigureAwait(false),
            "merge" => await InstanceMergeAsync(commandArgs).ConfigureAwait(false),
            "diff-aligned" => await InstanceDiffAlignedAsync(commandArgs).ConfigureAwait(false),
            "merge-aligned" => await InstanceMergeAlignedAsync(commandArgs).ConfigureAwait(false),
            "update" => await InstanceUpdateAsync(commandArgs).ConfigureAwait(false),
            "rename-id" => await InstanceRenameIdAsync(commandArgs).ConfigureAwait(false),
            "relationship" => await InstanceRelationshipAsync(commandArgs).ConfigureAwait(false),
            _ => PrintCommandUnknownError($"instance {mode}"),
        };
    }
}

