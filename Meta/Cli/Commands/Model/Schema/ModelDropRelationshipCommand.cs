internal sealed partial class CliRuntime
{
    async Task<int> ModelDropRelationshipAsync(string[] commandArgs)
    {
        var fromEntityName = RequiredValue("FromEntity");
        var toEntityName = RequiredValue("ToEntity");
        var options = ReadMutatingCommonOptions(commandArgs, startIndex: 4);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        try
        {
            var workspace = await OpenXmlWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.ContractVersion);
            var fromEntity = workspace.Model.FindEntity(fromEntityName) ??
                throw new InvalidOperationException(
                    $"Entity '{fromEntityName}' does not exist.");
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

            return await ExecuteOperationAsync(
                    workspace,
                    new Operation.RemoveRelationship(
                        fromEntityName,
                        relationshipName),
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


