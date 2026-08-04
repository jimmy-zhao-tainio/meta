internal sealed partial class CliRuntime
{
    async Task<int> InstanceDiffAsync(string[] commandArgs)
    {
        var leftPath = Path.GetFullPath(RequiredValue("leftWorkspace"));
        var rightPath = Path.GetFullPath(RequiredValue("rightWorkspace"));

        var leftWorkspace = await OpenXmlWorkspaceForCommandAsync(leftPath).ConfigureAwait(false);
        var rightWorkspace = await OpenXmlWorkspaceForCommandAsync(rightPath).ConfigureAwait(false);
        PrintContractCompatibilityWarning(leftWorkspace.ContractVersion);
        PrintContractCompatibilityWarning(rightWorkspace.ContractVersion);

        var rightDiagnostics = WorkspaceValidator.Validate(
            rightWorkspace.Model,
            rightWorkspace.Instance);
        if (rightDiagnostics.HasErrors || (globalStrict && rightDiagnostics.WarningCount > 0))
        {
            return PrintOperationValidationFailure("instance diff right workspace", Array.Empty<Operation>(), rightDiagnostics);
        }

        if (!AreModelXmlFilesByteIdentical(leftPath, leftWorkspace, rightPath, rightWorkspace, out var leftModelPath, out var rightModelPath))
        {
            return PrintFormattedError(
                "E_OPERATION",
                "instance diff requires byte-identical model.xml in left and right workspaces.",
                exitCode: 4,
                hints: new[]
                {
                    $"LeftModel: {leftModelPath}",
                    $"RightModel: {rightModelPath}",
                    "Next: align models first, or run meta instance diff-aligned <leftWorkspace> <rightWorkspace> <alignmentWorkspace>",
                });
        }

        Meta.Core.Services.InstanceDiffBuildResult diff;
        try
        {
            diff = services.InstanceDiffService.BuildEqualDiffWorkspace(
                leftWorkspace.State,
                rightWorkspace.State);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }

        var diffPath = ResolveInstanceDiffOutputPath(rightPath, "instance-diff");
        if (Directory.Exists(diffPath))
        {
            Directory.Delete(diffPath, recursive: true);
        }

        var diagnostics = WorkspaceValidator.Validate(
            diff.DiffWorkspace.Model,
            diff.DiffWorkspace.Instance);
        if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
        {
            return PrintOperationValidationFailure("instance diff", Array.Empty<Operation>(), diagnostics);
        }

        await services.ExportService.ExportXmlAsync(diff.DiffWorkspace, diffPath).ConfigureAwait(false);
        MetaCli.Core.MetaCliWorkspace.DescribeXml(diffPath);
        presenter.WriteInfo(diff.HasDifferences
            ? "Instance diff: differences found."
            : "Instance diff: no differences.");
        presenter.WriteInfo($"DiffWorkspace: {diffPath}");
        presenter.WriteInfo(
            $"Rows: left={diff.LeftRowCount}, right={diff.RightRowCount}  Properties: left={diff.LeftPropertyCount}, right={diff.RightPropertyCount}");
        presenter.WriteInfo(
            $"NotIn: left-not-in-right={diff.LeftNotInRightCount}, right-not-in-left={diff.RightNotInLeftCount}");

        return diff.HasDifferences ? 1 : 0;
    }
}

