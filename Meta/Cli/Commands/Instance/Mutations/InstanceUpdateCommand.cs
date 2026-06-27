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
            var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
            var entity = RequireEntity(workspace, entityName);
            ResolveRowById(workspace, entityName, id);
            var patches = new List<RowPatch>
            {
                BuildRowPatchForUpdate(entity, id, options.SetValues),
            };
            var operation = new WorkspaceOp
            {
                Type = WorkspaceOpTypes.BulkUpsertRows,
                EntityName = entityName,
                RowPatches = patches,
            };

            BulkRelationshipResolver.ResolveRelationshipIds(workspace, operation);
            return await ExecuteOperationsAgainstLoadedWorkspaceAsync(
                    workspace,
                    new[] { operation },
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


