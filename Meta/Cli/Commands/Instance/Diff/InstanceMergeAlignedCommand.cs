internal sealed partial class CliRuntime
{
    async Task<int> InstanceMergeAlignedAsync(string[] commandArgs)
    {
        if (commandArgs.Length != 4)
        {
            return PrintUsageError("Usage: instance merge-aligned <targetWorkspace> <diffWorkspace>");
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
            services.InstanceDiffService.ApplyAlignedDiffWorkspace(targetWorkspace, diffWorkspace);
            ApplyImplicitNormalization(targetWorkspace);

            var diagnostics = services.ValidationService.Validate(targetWorkspace);
            targetWorkspace.Diagnostics = diagnostics;
            if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
            {
                WorkspaceSnapshotCloner.Restore(targetWorkspace, before);
                return PrintOperationValidationFailure("instance merge-aligned", Array.Empty<WorkspaceOp>(), diagnostics);
            }

            await services.WorkspaceService.SaveAsync(targetWorkspace).ConfigureAwait(false);
            presenter.WriteOk(
                "instance merge-aligned applied",
                ("Target", targetPath));

            return 0;
        }
        catch (InvalidOperationException exception)
        {
            WorkspaceSnapshotCloner.Restore(targetWorkspace, before);
            if (string.Equals(
                    exception.Message,
                    "instance merge-aligned precondition failed: target does not match the diff left snapshot.",
                    StringComparison.Ordinal))
            {
                return PrintFormattedError(
                    "E_CONFLICT",
                    exception.Message,
                    exitCode: 1,
                    hints: new[]
                    {
                        "Next: re-run meta instance diff-aligned on the current target, intended right workspace, and alignment workspace.",
                    });
            }

            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}

