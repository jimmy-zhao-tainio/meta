internal sealed partial class CliRuntime
{
    async Task<int> ModelDropPropertyAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var propertyName = RequiredValue("Property");
        var options = ReadMutatingCommonOptions(commandArgs, startIndex: 4);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        var operation = new RemovePropertyOperation(
            entityName,
            propertyName);

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
