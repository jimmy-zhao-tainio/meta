using System.Text.RegularExpressions;
using Meta.Core.Operations;
using Meta.Core.Services;

internal sealed partial class CliRuntime
{
    private static readonly Regex RenameModelNamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    async Task<int> ModelRenameModelAsync(string[] commandArgs)
    {
        var options = ReadModelRenameModelOptions(commandArgs, startIndex: 2);
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

            var result = services.ModelRefactorService.RenameModel(
                workspace,
                new RenameModelRefactorOptions(options.OldModelName, options.NewModelName));

            ApplyImplicitNormalization(workspace);

            var diagnostics = services.ValidationService.Validate(workspace);
            workspace.Diagnostics = diagnostics;
            if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
            {
                WorkspaceSnapshotCloner.Restore(workspace, before);
                return PrintOperationValidationFailure(
                    "model rename-model",
                    Array.Empty<WorkspaceOp>(),
                    diagnostics);
            }

            await services.WorkspaceService.SaveAsync(workspace).ConfigureAwait(false);

            presenter.WriteOk(
                "model renamed",
                ("Workspace", Path.GetFullPath(workspace.WorkspaceRootPath)),
                ("From", result.OldModelName),
                ("To", result.NewModelName));
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

    (bool Ok, string OldModelName, string NewModelName, string WorkspacePath, string ErrorMessage)
        ReadModelRenameModelOptions(string[] commandArgs, int startIndex)
    {
        var oldModelName = RequiredValue("Old").Trim();
        var newModelName = RequiredValue("New").Trim();
        var workspacePath = WorkspacePath();
        if (string.IsNullOrWhiteSpace(oldModelName) || string.IsNullOrWhiteSpace(newModelName))
        {
            return (false, string.Empty, string.Empty, string.Empty, "Error: missing required arguments <Old> <New>.");
        }

        if (!RenameModelNamePattern.IsMatch(newModelName))
        {
            return (false, string.Empty, string.Empty, string.Empty, "Error: <New> must use identifier pattern [A-Za-z_][A-Za-z0-9_]*.");
        }

        return (true, oldModelName, newModelName, workspacePath, string.Empty);
    }
}
