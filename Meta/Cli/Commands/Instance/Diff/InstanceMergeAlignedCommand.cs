using MetaCli.Core;

internal sealed partial class CliRuntime
{
    async Task<int> InstanceMergeAlignedAsync(string[] commandArgs)
    {
        var targetWorkspace = CurrentWorkspaces.Required("targetWorkspace");
        var diffWorkspace = await WorkspaceComposition.MaterializeAsync(
            CurrentWorkspaces.Required("diffWorkspace"))
            .ConfigureAwait(false);

        try
        {
            var targetState = await WorkspaceComposition.MaterializeAsync(targetWorkspace)
                .ConfigureAwait(false);
            var operations = services.InstanceDiffService.PlanAlignedDiffMerge(
                targetState,
                diffWorkspace);
            return await ExecuteOperationsOnWorkspaceAsync(
                targetWorkspace,
                operations,
                "instance merge-aligned",
                "instance merge-aligned applied",
                new[] { ("Target", RequiredValue("targetWorkspace")) })
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
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

