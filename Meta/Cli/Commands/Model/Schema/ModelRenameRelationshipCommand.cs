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
            var loadOptions = string.IsNullOrWhiteSpace(commandOptions.ExistingColumnName)
                ? null
                : new WorkspaceLoadOptions(
                    new[]
                    {
                        new RelationshipColumnRecovery(
                            commandOptions.SourceEntityName,
                            commandOptions.TargetEntityName,
                            commandOptions.ExistingColumnName),
                    });
            var workspace = await OpenXmlWorkspaceForCommandAsync(commandOptions.WorkspacePath, loadOptions).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.ContractVersion);

            var fromEntity = workspace.Model.FindEntity(commandOptions.SourceEntityName) ??
                throw new InvalidOperationException(
                    $"Entity '{commandOptions.SourceEntityName}' does not exist.");
            var matchingRelationships = fromEntity.Relationships
                .Where(item => string.Equals(item.Entity, commandOptions.TargetEntityName, StringComparison.OrdinalIgnoreCase))
                .ToList();
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
                fromEntity.Name,
                relationship.GetColumnName(),
                commandOptions.NewRole);

            return await ExecuteOperationsAgainstOpenedXmlWorkspaceAsync(
                    workspace,
                    new[] { operation },
                    "model rename-relationship",
                    "relationship renamed",
                    buildSuccessDetails: results =>
                    {
                        var result = (RenameRelationshipResult)results.Single();
                        return new (string Key, string Value)[]
                        {
                            ("Workspace", workspace.RootPath),
                            ("Model", workspace.Model.Name),
                            ("From", result.SourceEntityName + "." + result.OldName),
                            ("To", result.SourceEntityName + "." + result.NewName),
                            ("Target", result.TargetEntityName),
                            ("OldRole", string.IsNullOrWhiteSpace(currentRole) ? "(none)" : currentRole),
                            ("NewRole", string.Equals(result.NewName, result.TargetEntityName + "Id", StringComparison.OrdinalIgnoreCase)
                                ? "(none)"
                                : commandOptions.NewRole),
                            ("Existing column", string.IsNullOrWhiteSpace(commandOptions.ExistingColumnName)
                                ? "(model)"
                                : commandOptions.ExistingColumnName),
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
        var workspacePath = WorkspacePath();
        var newRole = OptionalValue("role").Trim();
        var existingColumnName = OptionalValue("existing-column").Trim();
        if (string.IsNullOrWhiteSpace(sourceEntityName) || string.IsNullOrWhiteSpace(targetEntityName))
        {
            return (false, default, "Error: missing required arguments <FromEntity> <ToEntity>.");
        }

        if (!string.IsNullOrWhiteSpace(newRole) && !ModelNamePattern.IsMatch(newRole))
        {
            return (false, default, "Error: --role must use identifier pattern [A-Za-z_][A-Za-z0-9_]*.");
        }

        if (!string.IsNullOrWhiteSpace(existingColumnName) && !ModelNamePattern.IsMatch(existingColumnName))
        {
            return (false, default, "Error: --existing-column must use identifier pattern [A-Za-z_][A-Za-z0-9_]*.");
        }

        return (true, new RenameRelationshipCommandOptions(
            WorkspacePath: workspacePath,
            SourceEntityName: sourceEntityName,
            TargetEntityName: targetEntityName,
            NewRole: newRole,
            ExistingColumnName: existingColumnName), string.Empty);
    }

    readonly record struct RenameRelationshipCommandOptions(
        string WorkspacePath,
        string SourceEntityName,
        string TargetEntityName,
        string NewRole,
        string ExistingColumnName);
}
