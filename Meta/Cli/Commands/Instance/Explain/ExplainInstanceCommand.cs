internal sealed partial class CliRuntime
{
    async Task<int> ViewInstanceAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var id = RequiredValue("Id");
        var options = ReadWorkspaceOnlyOptions(commandArgs, startIndex: 4);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        var workspace = await OpenXmlWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
        PrintContractCompatibilityWarning(workspace.ContractVersion);
        var source = CreateWorkspaceSource(workspace.State);
        var resolvedEntityName = await ResolveEntityNameAsync(source, entityName).ConfigureAwait(false);
        var row = await source.ReadRecordAsync(resolvedEntityName, id).ConfigureAwait(false) ??
                  throw new InvalidOperationException(
                      $"Instance with Id '{id}' does not exist in entity '{resolvedEntityName}'.");

        PrintSelectedRecord(resolvedEntityName, row);
        return 0;
    }
}


