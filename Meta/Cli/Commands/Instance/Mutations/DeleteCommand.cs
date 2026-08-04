internal sealed partial class CliRuntime
{
    async Task<int> DeleteAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var id = RequiredValue("Id");
        return await ExecuteOperationAsync(
                new Operation.DeleteRecord(entityName, id),
                "delete",
                $"deleted {BuildEntityInstanceAddress(entityName, id)}")
            .ConfigureAwait(false);
    }
}


