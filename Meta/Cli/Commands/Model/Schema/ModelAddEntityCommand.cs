internal sealed partial class CliRuntime
{
    async Task<int> ModelAddEntityAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 3)
        {
            return PrintUsageError("Usage: model add-entity <Name> [--workspace <path>]");
        }
    
        var entityName = commandArgs[2];
        var options = ParseMutatingCommonOptions(commandArgs, startIndex: 3);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }
    
        var operation = new WorkspaceOp
        {
            Type = WorkspaceOpTypes.AddEntity,
            EntityName = entityName,
        };
    
        return await ExecuteOperationAsync(
                options.WorkspacePath,
                operation,
                "model add-entity",
                "entity created",
                ("Entity", entityName))
            .ConfigureAwait(false);
    }
}
