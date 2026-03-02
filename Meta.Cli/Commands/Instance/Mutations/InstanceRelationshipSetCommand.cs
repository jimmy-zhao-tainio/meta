internal sealed partial class CliRuntime
{
    async Task<int> InstanceRelationshipSetAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 7)
        {
            return PrintUsageError(
                "Usage: instance relationship set <FromEntity> <FromId> --to <RelationshipSelector> <ToId> [--workspace <path>]");
        }
    
        var fromEntityName = commandArgs[3];
        var fromId = commandArgs[4];
        var options = ParseInstanceRelationshipSetOptions(commandArgs, startIndex: 5);
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
            var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
            var fromEntity = RequireEntity(workspace, fromEntityName);
            var fromRow = ResolveRowById(workspace, fromEntityName, fromId);

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
            RequireEntity(workspace, toTargetEntityName);
            var targetExists = workspace.Instance.GetOrCreateEntityRecords(toTargetEntityName)
                .Any(row => string.Equals(row.Id, toId, StringComparison.OrdinalIgnoreCase));
            if (!targetExists)
            {
                return PrintDataError(
                    "E_ROW_NOT_FOUND",
                    $"Instance with Id '{toId}' does not exist in entity '{toTargetEntityName}'.");
            }
    
            var operation = new WorkspaceOp
            {
                Type = WorkspaceOpTypes.BulkUpsertRows,
                EntityName = fromEntityName,
                RowPatches =
                {
                    BuildRelationshipUsageRewritePatch(fromRow, toRelationshipName, toId),
                },
            };
    
            return await ExecuteOperationsAgainstLoadedWorkspaceAsync(
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


