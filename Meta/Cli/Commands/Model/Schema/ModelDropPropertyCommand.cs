internal sealed partial class CliRuntime
{
    async Task<int> ModelDropPropertyAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var propertyName = RequiredValue("Property");
        return await ExecuteOperationAsync(
                new Operation.RemoveProperty(entityName, propertyName),
                "model drop-property",
                "property removed",
                ("Entity", entityName),
                ("Property", propertyName))
            .ConfigureAwait(false);
    }
}
