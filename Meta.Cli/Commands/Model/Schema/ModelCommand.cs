internal sealed partial class CliRuntime
{
    async Task<int> ModelAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 2)
        {
            return PrintUsageError(
                "Usage: model <subcommand> [arguments] [--workspace <path>]");
        }
    
        var mode = commandArgs[1].Trim().ToLowerInvariant();
        return mode switch
        {
            "add-entity" => await ModelAddEntityAsync(commandArgs).ConfigureAwait(false),
            "rename-entity" => await ModelRenameEntityAsync(commandArgs).ConfigureAwait(false),
            "add-property" => await ModelAddPropertyAsync(commandArgs).ConfigureAwait(false),
            "set-property-required" => await ModelSetPropertyRequiredAsync(commandArgs).ConfigureAwait(false),
            "rename-property" => await ModelRenamePropertyAsync(commandArgs).ConfigureAwait(false),
            "add-relationship" => await ModelAddRelationshipAsync(commandArgs).ConfigureAwait(false),
            "rename-relationship" => await ModelRenameRelationshipAsync(commandArgs).ConfigureAwait(false),
            "refactor" => await ModelRefactorAsync(commandArgs).ConfigureAwait(false),
            "drop-property" => await ModelDropPropertyAsync(commandArgs).ConfigureAwait(false),
            "drop-relationship" => await ModelDropRelationshipAsync(commandArgs).ConfigureAwait(false),
            "drop-entity" => await ModelDropEntityAsync(commandArgs).ConfigureAwait(false),
            "suggest" => await ModelSuggestAsync(commandArgs).ConfigureAwait(false),
            _ => UnknownModelCommand(mode),
        };
    }
    
    int UnknownModelCommand(string mode)
    {
        return PrintCommandUnknownError($"model {mode}");
    }
}
