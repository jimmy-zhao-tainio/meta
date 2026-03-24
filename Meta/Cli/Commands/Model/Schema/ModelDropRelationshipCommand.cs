internal sealed partial class CliRuntime
{
    async Task<int> ModelDropRelationshipAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 4)
        {
            return PrintUsageError(
                "Usage: model drop-relationship <FromEntity> <ToEntity> [--workspace <path>]");
        }
    
        var fromEntityName = commandArgs[2];
        var toEntityName = commandArgs[3];
        var options = ParseMutatingCommonOptions(commandArgs, startIndex: 4);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }
    
        try
        {
            var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
            var fromEntity = RequireEntity(workspace, fromEntityName);
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

            var relationshipName = relationship.GetColumnName();
            var targetEntityName = relationship.Entity;
    
            var operation = new WorkspaceOp
            {
                Type = WorkspaceOpTypes.DeleteRelationship,
                EntityName = fromEntityName,
                RelatedEntity = relationshipName,
            };
            return await ExecuteOperationAsync(
                    options.WorkspacePath,
                    operation,
                    "model drop-relationship",
                    "relationship removed",
                    ("From", fromEntityName),
                    ("To", targetEntityName),
                    ("Name", relationshipName))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}


