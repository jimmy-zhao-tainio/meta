using MetaCli.Core;

internal sealed partial class CliRuntime
{
    async Task<int> InstanceDiffAsync(string[] commandArgs)
    {
        var leftSource = CurrentWorkspaces.Required("leftWorkspace");
        var rightSource = CurrentWorkspaces.Required("rightWorkspace");
        var leftWorkspace = await WorkspaceComposition.MaterializeAsync(leftSource).ConfigureAwait(false);
        var rightWorkspace = await WorkspaceComposition.MaterializeAsync(rightSource).ConfigureAwait(false);

        var rightDiagnostics = WorkspaceValidator.Validate(
            rightWorkspace.Model,
            rightWorkspace.Instance);
        if (rightDiagnostics.HasErrors || (globalStrict && rightDiagnostics.WarningCount > 0))
        {
            return PrintOperationValidationFailure("instance diff right workspace", Array.Empty<Operation>(), rightDiagnostics);
        }

        if (!string.Equals(
                leftWorkspace.Model.ComputeContractSignature(),
                rightWorkspace.Model.ComputeContractSignature(),
                StringComparison.Ordinal))
        {
            return PrintFormattedError(
                "E_OPERATION",
                "instance diff requires matching model contracts in left and right workspaces.",
                exitCode: 4,
                hints: new[]
                {
                    "Next: align models first, or run meta instance diff-aligned <leftWorkspace> <rightWorkspace> <alignmentWorkspace>",
                });
        }

        Meta.Core.Services.InstanceDiffBuildResult diff;
        try
        {
            diff = services.InstanceDiffService.BuildEqualDiffWorkspace(
                leftWorkspace,
                rightWorkspace);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }

        var diagnostics = WorkspaceValidator.Validate(
            diff.DiffWorkspace.Model,
            diff.DiffWorkspace.Instance);
        if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
        {
            return PrintOperationValidationFailure("instance diff", Array.Empty<Operation>(), diagnostics);
        }

        await CurrentWorkspaces.CreateAsync("output", diff.DiffWorkspace).ConfigureAwait(false);
        presenter.WriteInfo(diff.HasDifferences
            ? "Instance diff: differences found."
            : "Instance diff: no differences.");
        presenter.WriteInfo($"DiffWorkspace: {MetaCliWorkspace.OutputLocation(Invocation, "output-xml", "output-csharp", "output-sql")}");
        presenter.WriteInfo(
            $"Rows: left={diff.LeftRowCount}, right={diff.RightRowCount}  Properties: left={diff.LeftPropertyCount}, right={diff.RightPropertyCount}");
        presenter.WriteInfo(
            $"NotIn: left-not-in-right={diff.LeftNotInRightCount}, right-not-in-left={diff.RightNotInLeftCount}");

        return diff.HasDifferences ? 1 : 0;
    }
}

