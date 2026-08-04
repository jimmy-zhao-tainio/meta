internal sealed partial class CliRuntime
{
    async Task<int> ModelAddEntityAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Name");
        return await ExecuteOperationAsync(
                new Operation.AddEntity(entityName),
                "model add-entity",
                "entity created",
                ("Entity", entityName))
            .ConfigureAwait(false);
    }
}
