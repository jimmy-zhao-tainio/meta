internal sealed partial class CliRuntime
{
    async Task<int> StatusWorkspaceAsync(string[] commandArgs)
    {
        var options = ReadWorkspaceOnlyOptions(commandArgs, startIndex: 1);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        var workspace = await OpenXmlWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
        PrintContractCompatibilityWarning(workspace.ContractVersion);
        PrintWorkspaceSummary(workspace);

        return 0;
    }
}

