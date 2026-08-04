internal sealed partial class CliRuntime
{
    async Task<int> ModelRenameEntityAsync(string[] commandArgs)
    {
        var options = ReadModelRenameEntityOptions(commandArgs, startIndex: 2);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        try
        {
            var workspace = await OpenXmlWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.ContractVersion);
            var fkFieldsRenamed = workspace.Model.Entities
                .SelectMany(entity => entity.Relationships)
                .Count(relationship =>
                    MetaName.Comparer.Equals(
                        relationship.Entity,
                        options.OldEntityName) &&
                    string.IsNullOrWhiteSpace(relationship.Role));
            var operation = new Operation.RenameEntity(
                options.OldEntityName,
                options.NewEntityName);

            return await ExecuteOperationsAgainstOpenedXmlWorkspaceAsync(
                    workspace,
                    new[] { operation },
                    "model rename-entity",
                    "entity renamed",
                    buildSuccessDetails: results =>
                    {
                        var result = (RenameEntityResult)results.Single();
                        return new (string Key, string Value)[]
                        {
                            ("Workspace", workspace.RootPath),
                            ("Model", workspace.Model.Name),
                            ("From", result.OldName),
                            ("To", result.NewName),
                            ("Relationships updated", result.RelationshipCount.ToString()),
                            ("FK fields renamed", fkFieldsRenamed.ToString()),
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

    (bool Ok, string OldEntityName, string NewEntityName, string WorkspacePath, string ErrorMessage)
        ReadModelRenameEntityOptions(string[] commandArgs, int startIndex)
    {
        var oldEntityName = RequiredValue("Old").Trim();
        var newEntityName = RequiredValue("New").Trim();
        var workspacePath = WorkspacePath();
        if (string.IsNullOrWhiteSpace(oldEntityName) || string.IsNullOrWhiteSpace(newEntityName))
        {
            return (false, string.Empty, string.Empty, string.Empty, "Error: missing required arguments <Old> <New>.");
        }

        if (!ModelNamePattern.IsMatch(newEntityName))
        {
            return (false, string.Empty, string.Empty, string.Empty, "Error: <New> must use identifier pattern [A-Za-z_][A-Za-z0-9_]*.");
        }

        return (true, oldEntityName, newEntityName, workspacePath, string.Empty);
    }
}
