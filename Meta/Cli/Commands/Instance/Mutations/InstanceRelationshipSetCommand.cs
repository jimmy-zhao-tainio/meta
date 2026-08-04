internal sealed partial class CliRuntime
{
    async Task<int> InstanceRelationshipSetAsync(string[] commandArgs)
    {
        var fromEntityName = RequiredValue("FromEntity");
        var fromId = RequiredValue("FromId");
        var options = ReadInstanceRelationshipSetOptions(commandArgs, startIndex: 5);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        if (string.IsNullOrWhiteSpace(options.RelationshipSelector) || string.IsNullOrWhiteSpace(options.ToId))
        {
            return PrintArgumentError("Error: instance relationship set requires --to <RelationshipSelector> <ToId>.");
        }
        try
        {
            var resolvedFromEntityName = await ResolveEntityNameAsync(
                    CurrentWorkspace,
                    fromEntityName)
                .ConfigureAwait(false);
            var fromRow = await CurrentWorkspace.ReadRecordAsync(
                    resolvedFromEntityName,
                    fromId)
                .ConfigureAwait(false) ?? throw new InvalidOperationException(
                    $"Instance with Id '{fromId}' does not exist in entity '{resolvedFromEntityName}'.");

            var selector = options.RelationshipSelector;
            var toId = options.ToId;
            var matches = new List<RelationshipDefinition>();
            await foreach (var candidate in CurrentWorkspace.ReadRelationshipsAsync(resolvedFromEntityName))
            {
                if (MetaName.Comparer.Equals(candidate.TargetEntityName, selector) ||
                    MetaName.Comparer.Equals(candidate.GetRoleOrDefault(), selector) ||
                    MetaName.Comparer.Equals(candidate.GetColumnName(), selector))
                {
                    matches.Add(candidate);
                }
            }

            if (matches.Count > 1)
            {
                return PrintDataError(
                    "E_RELATIONSHIP_AMBIGUOUS",
                    $"Relationship selector '{selector}' is ambiguous on entity '{resolvedFromEntityName}'. Use relationship role or column.");
            }

            if (matches.Count == 0)
            {
                return PrintDataError(
                    "E_RELATIONSHIP_NOT_FOUND",
                    $"Relationship '{resolvedFromEntityName}->{selector}' does not exist.");
            }

            var relationship = matches.Single();
            var toRelationshipName = relationship.GetColumnName();
            var toTargetEntityName = relationship.TargetEntityName;
            if (await CurrentWorkspace.ReadRecordAsync(toTargetEntityName, toId).ConfigureAwait(false) is null)
            {
                return PrintDataError(
                    "E_ROW_NOT_FOUND",
                    $"Instance with Id '{toId}' does not exist in entity '{toTargetEntityName}'.");
            }

            var operation = new Operation.SetRelationship(
                resolvedFromEntityName,
                fromRow.Id,
                toRelationshipName,
                toId);

            return await ExecuteOperationsAsync(
                    [operation],
                    commandName: "instance.relationship.set",
                    successMessage: "relationship usage updated",
                    successDetails: new[]
                    {
                        ("FromInstance", BuildEntityInstanceAddress(fromEntityName, fromRow.Id)),
                        ("ToInstance", BuildEntityInstanceAddress(toTargetEntityName, toId)),
                    })
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}


