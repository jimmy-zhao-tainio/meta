internal sealed partial class CliRuntime
{
    async Task<int> InstanceMergeAsync(string[] commandArgs)
    {
        var targetPath = Path.GetFullPath(RequiredValue("targetWorkspace"));
        var diffWorkspacePath = Path.GetFullPath(RequiredValue("diffWorkspace"));

        var targetWorkspace = await OpenXmlWorkspaceForCommandAsync(targetPath).ConfigureAwait(false);
        var diffWorkspace = await OpenXmlWorkspaceForCommandAsync(diffWorkspacePath).ConfigureAwait(false);
        PrintContractCompatibilityWarning(targetWorkspace.ContractVersion);
        PrintContractCompatibilityWarning(diffWorkspace.ContractVersion);

        try
        {
            var operations = services.InstanceDiffService.PlanEqualDiffMerge(
                targetWorkspace.State,
                diffWorkspace.State);
            return await ExecuteOperationsAgainstOpenedXmlWorkspaceAsync(
                    targetWorkspace,
                    operations,
                    "instance merge",
                    "instance merge applied",
                    new[] { ("Target", targetPath) })
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
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

