internal sealed partial class CliRuntime
{
    async Task<int> DeleteAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var id = RequiredValue("Id");
        var options = ReadMutatingCommonOptions(commandArgs, startIndex: 3);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        try
        {
            var workspace = await OpenXmlWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.ContractVersion);
            RequireEntity(workspace.Model, entityName);
            ResolveRowById(workspace.State, entityName, id);

            var operation = new Operation.DeleteRecord(entityName, id);

            return await ExecuteOperationsAgainstOpenedXmlWorkspaceAsync(
                    workspace,
                    new[] { operation },
                    commandName: "delete",
                    successMessage: $"deleted {BuildEntityInstanceAddress(entityName, id)}")
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}


