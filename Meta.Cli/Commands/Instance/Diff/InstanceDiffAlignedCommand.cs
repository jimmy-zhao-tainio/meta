internal sealed partial class CliRuntime
{
    async Task<int> InstanceDiffAlignedAsync(string[] commandArgs)
    {
        if (commandArgs.Length != 5)
        {
            return PrintUsageError("Usage: instance diff-aligned <leftWorkspace> <rightWorkspace> <alignmentWorkspace>");
        }

        var leftPath = Path.GetFullPath(commandArgs[2]);
        var rightPath = Path.GetFullPath(commandArgs[3]);
        var alignmentPath = Path.GetFullPath(commandArgs[4]);

        var leftWorkspace = await services.WorkspaceService.LoadAsync(leftPath, searchUpward: false).ConfigureAwait(false);
        var rightWorkspace = await services.WorkspaceService.LoadAsync(rightPath, searchUpward: false).ConfigureAwait(false);
        var alignmentWorkspace = await services.WorkspaceService.LoadAsync(alignmentPath, searchUpward: false).ConfigureAwait(false);
        PrintContractCompatibilityWarning(leftWorkspace.WorkspaceConfig);
        PrintContractCompatibilityWarning(rightWorkspace.WorkspaceConfig);
        PrintContractCompatibilityWarning(alignmentWorkspace.WorkspaceConfig);

        var rightDiagnostics = services.ValidationService.Validate(rightWorkspace);
        if (rightDiagnostics.HasErrors || (globalStrict && rightDiagnostics.WarningCount > 0))
        {
            return PrintOperationValidationFailure("instance diff-aligned right workspace", Array.Empty<WorkspaceOp>(), rightDiagnostics);
        }

        Meta.Core.Services.InstanceDiffBuildResult diff;
        try
        {
            diff = services.InstanceDiffService.BuildAlignedDiffWorkspace(
                leftWorkspace,
                rightWorkspace,
                alignmentWorkspace,
                rightPath);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
        if (Directory.Exists(diff.DiffWorkspacePath))
        {
            Directory.Delete(diff.DiffWorkspacePath, recursive: true);
        }

        ApplyImplicitNormalization(diff.DiffWorkspace);
        var diagnostics = services.ValidationService.Validate(diff.DiffWorkspace);
        diff.DiffWorkspace.Diagnostics = diagnostics;
        if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
        {
            return PrintOperationValidationFailure("instance diff-aligned", Array.Empty<WorkspaceOp>(), diagnostics);
        }

        await services.WorkspaceService.SaveAsync(diff.DiffWorkspace).ConfigureAwait(false);
        var diffPath = Path.GetFullPath(diff.DiffWorkspacePath);
        presenter.WriteInfo(diff.HasDifferences
            ? "Instance diff-aligned: differences found."
            : "Instance diff-aligned: no differences.");
        presenter.WriteInfo($"DiffWorkspace: {diffPath}");
        presenter.WriteInfo(
            $"Rows: left={diff.LeftRowCount}, right={diff.RightRowCount}  Properties: left={diff.LeftPropertyCount}, right={diff.RightPropertyCount}");
        presenter.WriteInfo(
            $"NotIn: left-not-in-right={diff.LeftNotInRightCount}, right-not-in-left={diff.RightNotInLeftCount}");

        return diff.HasDifferences ? 1 : 0;
    }
}

