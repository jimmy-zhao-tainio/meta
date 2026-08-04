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

        return await ExecuteOperationAsync(
                options.WorkspacePath,
                () => new Operation.AddEntity(entityName),
                "model add-entity",
                "entity created",
                ("Entity", entityName))
            .ConfigureAwait(false);
    }
}
