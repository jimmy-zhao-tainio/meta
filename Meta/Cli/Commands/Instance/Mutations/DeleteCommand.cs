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
            var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
            RequireEntity(workspace, entityName);
            ResolveRowById(workspace, entityName, id);

            var operation = new WorkspaceOp
            {
                Type = WorkspaceOpTypes.DeleteRows,
                EntityName = entityName,
                Ids = new List<string> { id },
            };

            return await ExecuteOperationsAgainstLoadedWorkspaceAsync(
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


