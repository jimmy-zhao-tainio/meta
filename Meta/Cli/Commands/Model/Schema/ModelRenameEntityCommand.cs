using Meta.Core.Operations;
using Meta.Core.Services;

internal sealed partial class CliRuntime
{
    async Task<int> ModelRenameEntityAsync(string[] commandArgs)
    {
        var options = ReadModelRenameEntityOptions(commandArgs, startIndex: 2);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        var commandOptions = options.Options;

        Workspace? workspace = null;
        WorkspaceSnapshot? before = null;
        try
        {
            workspace = await LoadWorkspaceForCommandAsync(commandOptions.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
            before = WorkspaceSnapshotCloner.Capture(workspace);

            var result = services.ModelRefactorService.RenameEntity(workspace, commandOptions.Refactor);
            ApplyImplicitNormalization(workspace);

            var diagnostics = services.ValidationService.Validate(workspace);
            workspace.Diagnostics = diagnostics;
            if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
            {
                WorkspaceSnapshotCloner.Restore(workspace, before);
                return PrintOperationValidationFailure(
                    "model rename-entity",
                    Array.Empty<WorkspaceOp>(),
                    diagnostics);
            }

            await services.WorkspaceService.SaveAsync(workspace).ConfigureAwait(false);

            presenter.WriteOk(
                "entity renamed",
                ("Workspace", Path.GetFullPath(workspace.WorkspaceRootPath)),
                ("Model", workspace.Model.Name),
                ("From", result.OldEntityName),
                ("To", result.NewEntityName),
                ("Relationships updated", result.RelationshipsUpdated.ToString()),
                ("FK fields renamed", result.FkFieldsRenamed.ToString()),
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

    (bool Ok, RenameEntityCommandOptions Options, string ErrorMessage)
        ReadModelRenameEntityOptions(string[] commandArgs, int startIndex)
    {
        var oldEntityName = RequiredValue("Old").Trim();
        var newEntityName = RequiredValue("New").Trim();
        var workspacePath = WorkspacePath();
        if (string.IsNullOrWhiteSpace(oldEntityName) || string.IsNullOrWhiteSpace(newEntityName))
        {
            return (false, default, "Error: missing required arguments <Old> <New>.");
        }

        if (!ModelNamePattern.IsMatch(newEntityName))
        {
            return (false, default, "Error: <New> must use identifier pattern [A-Za-z_][A-Za-z0-9_]*.");
        }

        return (true, new RenameEntityCommandOptions(
            WorkspacePath: workspacePath,
            Refactor: new RenameEntityRefactorOptions(oldEntityName, newEntityName)), string.Empty);
    }

    readonly record struct RenameEntityCommandOptions(
        string WorkspacePath,
        RenameEntityRefactorOptions Refactor);
}
