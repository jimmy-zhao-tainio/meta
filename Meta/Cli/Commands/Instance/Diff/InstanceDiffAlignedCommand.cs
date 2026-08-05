using MetaCli.Core;

internal sealed partial class CliRuntime
{
    async Task<int> InstanceDiffAlignedAsync(string[] commandArgs)
    {
        var leftSource = CurrentWorkspaces.Required("leftWorkspace");
        var rightSource = CurrentWorkspaces.Required("rightWorkspace");
        var alignmentSource = CurrentWorkspaces.Required("alignmentWorkspace");
        var leftWorkspace = await WorkspaceComposition.MaterializeAsync(leftSource).ConfigureAwait(false);
        var rightWorkspace = await WorkspaceComposition.MaterializeAsync(rightSource).ConfigureAwait(false);
        var alignmentWorkspace = await WorkspaceComposition.MaterializeAsync(alignmentSource).ConfigureAwait(false);

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
                leftWorkspace,
                rightWorkspace,
                alignmentWorkspace);
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
            return PrintOperationValidationFailure("instance diff-aligned", Array.Empty<Operation>(), diagnostics);
        }

        await CurrentWorkspaces.CreateAsync("output", diff.DiffWorkspace).ConfigureAwait(false);
        presenter.WriteInfo(diff.HasDifferences
            ? "Instance diff-aligned: differences found."
            : "Instance diff-aligned: no differences.");
        presenter.WriteInfo($"DiffWorkspace: {MetaCliWorkspace.OutputLocation(Invocation, "output-xml", "output-csharp", "output-sql")}");
        presenter.WriteInfo(
            $"Rows: left={diff.LeftRowCount}, right={diff.RightRowCount}  Properties: left={diff.LeftPropertyCount}, right={diff.RightPropertyCount}");
        presenter.WriteInfo(
            $"NotIn: left-not-in-right={diff.LeftNotInRightCount}, right-not-in-left={diff.RightNotInLeftCount}");

        return diff.HasDifferences ? 1 : 0;
    }
}

