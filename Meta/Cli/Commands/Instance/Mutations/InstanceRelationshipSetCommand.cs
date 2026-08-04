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
            var workspace = await OpenXmlWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.ContractVersion);
            var fromEntity = RequireEntity(workspace.Model, fromEntityName);
            var fromRow = ResolveRowById(workspace.State, fromEntityName, fromId);

            var toEntityName = options.RelationshipSelector;
            var toId = options.ToId;
            var relationship = ResolveRelationshipDefinition(fromEntity, toEntityName, out var isAmbiguous);
            if (isAmbiguous)
            {
                return PrintDataError(
                    "E_RELATIONSHIP_AMBIGUOUS",
                    $"Relationship selector '{toEntityName}' is ambiguous on entity '{fromEntityName}'. Use relationship role or column.");
            }

            if (relationship == null)
            {
                return PrintDataError(
                    "E_RELATIONSHIP_NOT_FOUND",
                    $"Relationship '{fromEntityName}->{toEntityName}' does not exist.");
            }

            var toRelationshipName = relationship.GetColumnName();
            var toTargetEntityName = relationship.Entity;
            RequireEntity(workspace.Model, toTargetEntityName);
            var targetExists = workspace.Instance.GetOrCreateEntityRecords(toTargetEntityName)
                .Any(row => string.Equals(row.Id, toId, StringComparison.OrdinalIgnoreCase));
            if (!targetExists)
            {
                return PrintDataError(
                    "E_ROW_NOT_FOUND",
                    $"Instance with Id '{toId}' does not exist in entity '{toTargetEntityName}'.");
            }

            var operation = new Operation.SetRelationship(
                fromEntityName,
                fromRow.Id,
                toRelationshipName,
                toId);

            return await ExecuteOperationsAgainstOpenedXmlWorkspaceAsync(
                    workspace,
                    new[] { operation },
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


