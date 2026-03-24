internal sealed partial class CliRuntime
{
    async Task<int> InstanceMergeAsync(string[] commandArgs)
    {
        if (commandArgs.Length != 4)
        {
            return PrintUsageError("Usage: instance merge <targetWorkspace> <diffWorkspace>");
        }

        var targetPath = Path.GetFullPath(commandArgs[2]);
        var diffWorkspacePath = Path.GetFullPath(commandArgs[3]);

        var targetWorkspace = await services.WorkspaceService.LoadAsync(targetPath, searchUpward: false).ConfigureAwait(false);
        var diffWorkspace = await services.WorkspaceService.LoadAsync(diffWorkspacePath, searchUpward: false).ConfigureAwait(false);
        PrintContractCompatibilityWarning(targetWorkspace.WorkspaceConfig);
        PrintContractCompatibilityWarning(diffWorkspace.WorkspaceConfig);

        var before = WorkspaceSnapshotCloner.Capture(targetWorkspace);
        try
        {
            services.InstanceDiffService.ApplyEqualDiffWorkspace(targetWorkspace, diffWorkspace);
            ApplyImplicitNormalization(targetWorkspace);

            var diagnostics = services.ValidationService.Validate(targetWorkspace);
            targetWorkspace.Diagnostics = diagnostics;
            if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
            {
                WorkspaceSnapshotCloner.Restore(targetWorkspace, before);
                return PrintOperationValidationFailure("instance merge", Array.Empty<WorkspaceOp>(), diagnostics);
            }

            await services.WorkspaceService.SaveAsync(targetWorkspace).ConfigureAwait(false);
            presenter.WriteOk(
                "instance merge applied",
                ("Target", targetPath));

            return 0;
        }
        catch (InvalidOperationException exception)
        {
            WorkspaceSnapshotCloner.Restore(targetWorkspace, before);
            if (string.Equals(
                    exception.Message,
                    "instance merge precondition failed: target does not match the diff left snapshot.",
                    StringComparison.Ordinal))
            {
                return PrintFormattedError(
                    "E_CONFLICT",
                    exception.Message,
                    exitCode: 1,
                    hints: new[]
                    {
                        "Next: re-run meta instance diff on the current target and intended right workspace.",
                    });
            }

            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}

