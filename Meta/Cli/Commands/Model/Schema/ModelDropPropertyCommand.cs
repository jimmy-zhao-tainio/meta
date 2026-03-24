internal sealed partial class CliRuntime
{
    async Task<int> ModelDropPropertyAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 4)
        {
            return PrintUsageError(
                "Usage: model drop-property <Entity> <Property> [--workspace <path>]");
        }
    
        var entityName = commandArgs[2];
        var propertyName = commandArgs[3];
        var options = ParseMutatingCommonOptions(commandArgs, startIndex: 4);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }
    
        var operation = new WorkspaceOp
        {
            Type = WorkspaceOpTypes.DeleteProperty,
            EntityName = entityName,
            PropertyName = propertyName,
        };
    
        return await ExecuteOperationAsync(
                options.WorkspacePath,
                operation,
                "model drop-property",
                "property removed",
                ("Entity", entityName),
                ("Property", propertyName))
            .ConfigureAwait(false);
    }
}
