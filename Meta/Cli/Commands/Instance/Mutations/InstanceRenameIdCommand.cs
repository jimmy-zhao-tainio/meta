internal sealed partial class CliRuntime
{
    async Task<int> InstanceRenameIdAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var oldId = RequiredValue("OldId");
        var newId = RequiredValue("NewId");
        return await ExecuteOperationsAsync(
                [new Operation.RenameRecord(entityName, oldId, newId)],
                "instance rename-id",
                "instance id renamed",
                buildSuccessDetails: results =>
                {
                    var result = (RenameRecordResult)results.Single();
                    return new (string Key, string Value)[]
                    {
                        ("Entity", result.EntityName),
                        ("From", result.OldId),
                        ("To", result.NewId),
                        ("Relationships updated", result.RelationshipValueCount.ToString()),
                    };
                })
            .ConfigureAwait(false);
    }
}
