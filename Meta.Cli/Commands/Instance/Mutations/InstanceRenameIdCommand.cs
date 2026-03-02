using Meta.Core.Operations;
using Meta.Core.Services;

internal sealed partial class CliRuntime
{
    async Task<int> InstanceRenameIdAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 5)
        {
            return PrintUsageError("Usage: instance rename-id <Entity> <OldId> <NewId> [--workspace <path>]");
        }

        var entityName = commandArgs[2];
        var oldId = commandArgs[3];
        var newId = commandArgs[4];
        var options = ParseMutatingCommonOptions(commandArgs, startIndex: 5);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        Workspace? workspace = null;
        WorkspaceSnapshot? before = null;
        try
        {
            workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
            before = WorkspaceSnapshotCloner.Capture(workspace);

            var result = services.InstanceRefactorService.RenameInstanceId(
                workspace,
                new RenameInstanceIdRefactorOptions(entityName, oldId, newId));
            ApplyImplicitNormalization(workspace);

            var diagnostics = services.ValidationService.Validate(workspace);
            workspace.Diagnostics = diagnostics;
            if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
            {
                WorkspaceSnapshotCloner.Restore(workspace, before);
                return PrintOperationValidationFailure(
                    "instance rename-id",
                    Array.Empty<WorkspaceOp>(),
                    diagnostics);
            }

            await services.WorkspaceService.SaveAsync(workspace).ConfigureAwait(false);

            presenter.WriteOk(
                "instance id renamed",
                ("Workspace", Path.GetFullPath(workspace.WorkspaceRootPath)),
                ("Entity", result.EntityName),
                ("From", result.OldId),
                ("To", result.NewId),
                ("Relationships updated", result.RelationshipsUpdated.ToString()),
                ("Rows touched", result.RowsTouched.ToString()));
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            if (workspace != null && before != null)
            {
                WorkspaceSnapshotCloner.Restore(workspace, before);
            }

            return PrintDataError("E_OPERATION", exception.Message);
        }
        catch
        {
            if (workspace != null && before != null)
            {
                WorkspaceSnapshotCloner.Restore(workspace, before);
            }

            throw;
        }
    }
}
