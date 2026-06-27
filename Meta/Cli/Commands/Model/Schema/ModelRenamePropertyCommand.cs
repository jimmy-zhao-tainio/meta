internal sealed partial class CliRuntime
{
    async Task<int> ModelRenamePropertyAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var oldPropertyName = RequiredValue("Old");
        var newPropertyName = RequiredValue("New");
        var options = ReadMutatingCommonOptions(commandArgs, startIndex: 5);
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
