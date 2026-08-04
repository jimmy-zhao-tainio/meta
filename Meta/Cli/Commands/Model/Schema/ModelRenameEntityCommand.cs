internal sealed partial class CliRuntime
{
    async Task<int> ModelRenameEntityAsync(string[] commandArgs)
    {
        var oldEntityName = RequiredValue("Old").Trim();
        var newEntityName = RequiredValue("New").Trim();
        if (!ModelNamePattern.IsMatch(newEntityName))
        {
            return PrintArgumentError("Error: <New> must use identifier pattern [A-Za-z_][A-Za-z0-9_]*.");
        }

        try
        {
            return await ExecuteOperationsAsync(
                    [new Operation.RenameEntity(oldEntityName, newEntityName)],
                    "model rename-entity",
                    "entity renamed",
                    buildSuccessDetails: results =>
                    {
                        var result = (RenameEntityResult)results.Single();
                        return new (string Key, string Value)[]
                        {
                            ("From", result.OldName),
                            ("To", result.NewName),
                            ("Relationships updated", result.RelationshipCount.ToString()),
                            ("Rows touched", result.RelationshipValueCount.ToString()),
                        };
                    })
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }

}
