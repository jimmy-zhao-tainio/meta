internal sealed partial class CliRuntime
{
    async Task<int> ModelAddEntityAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Name");
        var options = ReadMutatingCommonOptions(commandArgs, startIndex: 3);
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
