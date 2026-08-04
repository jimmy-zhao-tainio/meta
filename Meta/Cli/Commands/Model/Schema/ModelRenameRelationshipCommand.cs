internal sealed partial class CliRuntime
{
    async Task<int> ModelRenameRelationshipAsync(string[] commandArgs)
    {
        var options = ReadModelRenameRelationshipOptions(commandArgs, startIndex: 2);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        var commandOptions = options.Options;

        try
        {
            var sourceEntityName = await ResolveEntityNameAsync(
                    CurrentWorkspace,
                    commandOptions.SourceEntityName)
                .ConfigureAwait(false);
            var matchingRelationships = new List<RelationshipDefinition>();
            await foreach (var candidate in CurrentWorkspace.ReadRelationshipsAsync(sourceEntityName))
            {
                if (MetaName.Comparer.Equals(
                        candidate.TargetEntityName,
                        commandOptions.TargetEntityName))
                {
                    matchingRelationships.Add(candidate);
                }
            }

            if (matchingRelationships.Count > 1)
            {
                return PrintDataError(
                    "E_RELATIONSHIP_AMBIGUOUS",
                    $"Relationship '{commandOptions.SourceEntityName}->{commandOptions.TargetEntityName}' is ambiguous because multiple relationships target '{commandOptions.TargetEntityName}'.");
            }

            var relationship = matchingRelationships.SingleOrDefault();
            if (relationship == null)
            {
                return PrintDataError(
                    "E_RELATIONSHIP_NOT_FOUND",
                    $"Relationship '{commandOptions.SourceEntityName}->{commandOptions.TargetEntityName}' does not exist.");
            }

            var currentRole = relationship.Role ?? string.Empty;
            var operation = new Operation.RenameRelationship(
                sourceEntityName,
                relationship.GetColumnName(),
                commandOptions.NewRole);

            return await ExecuteOperationsAsync(
                    [operation],
                    "model rename-relationship",
                    "relationship renamed",
                    buildSuccessDetails: results =>
                    {
                        var result = (RenameRelationshipResult)results.Single();
                        return new (string Key, string Value)[]
                        {
                            ("From", result.SourceEntityName + "." + result.OldName),
                            ("To", result.SourceEntityName + "." + result.NewName),
                            ("Target", result.TargetEntityName),
                            ("OldRole", string.IsNullOrWhiteSpace(currentRole) ? "(none)" : currentRole),
                            ("NewRole", string.Equals(result.NewName, result.TargetEntityName + "Id", StringComparison.OrdinalIgnoreCase)
                                ? "(none)"
                                : commandOptions.NewRole),
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

    (bool Ok, RenameRelationshipCommandOptions Options, string ErrorMessage)
        ReadModelRenameRelationshipOptions(string[] commandArgs, int startIndex)
    {
        var sourceEntityName = RequiredValue("FromEntity").Trim();
        var targetEntityName = RequiredValue("ToEntity").Trim();
        var newRole = OptionalValue("role").Trim();
        if (string.IsNullOrWhiteSpace(sourceEntityName) || string.IsNullOrWhiteSpace(targetEntityName))
        {
            return (false, default, "Error: missing required arguments <FromEntity> <ToEntity>.");
        }

        if (!string.IsNullOrWhiteSpace(newRole) && !ModelNamePattern.IsMatch(newRole))
        {
            return (false, default, "Error: --role must use identifier pattern [A-Za-z_][A-Za-z0-9_]*.");
        }

        return (true, new RenameRelationshipCommandOptions(
            SourceEntityName: sourceEntityName,
            TargetEntityName: targetEntityName,
            NewRole: newRole), string.Empty);
    }

    readonly record struct RenameRelationshipCommandOptions(
        string SourceEntityName,
        string TargetEntityName,
        string NewRole);
}
