internal sealed partial class CliRuntime
{
    async Task<int> ModelRenamePropertyAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 5)
        {
            return PrintUsageError(
                "Usage: model rename-property <Entity> <Old> <New> [--workspace <path>]");
        }
    
        var entityName = commandArgs[2];
        var oldPropertyName = commandArgs[3];
        var newPropertyName = commandArgs[4];
        var options = ParseMutatingCommonOptions(commandArgs, startIndex: 5);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }
    
        var operation = new WorkspaceOp
        {
            Type = WorkspaceOpTypes.RenameProperty,
            EntityName = entityName,
            PropertyName = oldPropertyName,
            NewPropertyName = newPropertyName,
        };
    
        return await ExecuteOperationAsync(
                options.WorkspacePath,
                operation,
                "model rename-property",
                "property renamed",
                ("Entity", entityName),
                ("From", oldPropertyName),
                ("To", newPropertyName))
            .ConfigureAwait(false);
    }
}
