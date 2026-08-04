internal sealed partial class CliRuntime
{
    async Task<int> InstanceUpdateAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var id = RequiredValue("Id");
        var options = ReadMutatingEntityOptions(commandArgs, startIndex: 4);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        if (ContainsIdSetAssignment(options.SetValues))
        {
            return PrintArgumentError("Error: do not use --set Id. Instance id must be positional <Id>.");
        }

        if (options.SetValues.Count == 0)
        {
            return PrintArgumentError("Error: instance update requires at least one --set Field=Value.");
        }

        try
        {
            var workspace = await OpenXmlWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.ContractVersion);
            var entity = RequireEntity(workspace.Model, entityName);
            ResolveRowById(workspace.State, entityName, id);
            var operations = BuildUpdateOperations(
                entity,
                id,
                options.SetValues);
            return await ExecuteOperationsAgainstOpenedXmlWorkspaceAsync(
                    workspace,
                    operations,
                    commandName: "instance.update",
                    successMessage: $"updated {BuildEntityInstanceAddress(entityName, id)}")
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}


