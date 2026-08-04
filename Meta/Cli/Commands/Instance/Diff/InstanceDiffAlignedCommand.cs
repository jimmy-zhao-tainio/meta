internal sealed partial class CliRuntime
{
    async Task<int> InstanceDiffAlignedAsync(string[] commandArgs)
    {
        var leftPath = Path.GetFullPath(RequiredValue("leftWorkspace"));
        var rightPath = Path.GetFullPath(RequiredValue("rightWorkspace"));
        var alignmentPath = Path.GetFullPath(RequiredValue("alignmentWorkspace"));

        var leftWorkspace = await OpenXmlWorkspaceForCommandAsync(leftPath).ConfigureAwait(false);
        var rightWorkspace = await OpenXmlWorkspaceForCommandAsync(rightPath).ConfigureAwait(false);
        var alignmentWorkspace = await OpenXmlWorkspaceForCommandAsync(alignmentPath).ConfigureAwait(false);
        PrintContractCompatibilityWarning(leftWorkspace.ContractVersion);
        PrintContractCompatibilityWarning(rightWorkspace.ContractVersion);
        PrintContractCompatibilityWarning(alignmentWorkspace.ContractVersion);

        var rightDiagnostics = WorkspaceValidator.Validate(
            rightWorkspace.Model,
            rightWorkspace.Instance);
        if (rightDiagnostics.HasErrors || (globalStrict && rightDiagnostics.WarningCount > 0))
        {
            return PrintOperationValidationFailure("instance diff-aligned right workspace", Array.Empty<Operation>(), rightDiagnostics);
        }

        Meta.Core.Services.InstanceDiffBuildResult diff;
        try
        {
            diff = services.InstanceDiffService.BuildAlignedDiffWorkspace(
                leftWorkspace.State,
                rightWorkspace.State,
                alignmentWorkspace.State);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
        var diffPath = ResolveInstanceDiffOutputPath(rightPath, "instance-diff-aligned");
        if (Directory.Exists(diffPath))
        {
            Directory.Delete(diffPath, recursive: true);
        }

        var diagnostics = WorkspaceValidator.Validate(
            diff.DiffWorkspace.Model,
            diff.DiffWorkspace.Instance);
        if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
        {
            return PrintOperationValidationFailure("instance diff-aligned", Array.Empty<Operation>(), diagnostics);
        }

        await services.ExportService.ExportXmlAsync(diff.DiffWorkspace, diffPath).ConfigureAwait(false);
        MetaCli.Core.MetaCliWorkspace.DescribeXml(diffPath);
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

