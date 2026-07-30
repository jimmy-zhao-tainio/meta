using MetaCli.Core;

internal sealed partial class CliRuntime
{
    async Task<int> BulkInsertAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var parseResult = ReadBulkInsertOptions(commandArgs, startIndex: 2);
        if (!parseResult.Ok)
        {
            return PrintArgumentError(parseResult.ErrorMessage);
        }

        if (!string.IsNullOrWhiteSpace(parseResult.Format) &&
            !string.Equals(parseResult.Format, "tsv", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parseResult.Format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            return PrintDataError("E_FORMAT",
                $"unsupported --from '{parseResult.Format}'. Supported values are tsv or csv.");
        }

        var hasFile = !string.IsNullOrWhiteSpace(parseResult.FilePath);
        if ((hasFile && parseResult.UseStdin) || (!hasFile && !parseResult.UseStdin))
        {
            return PrintArgumentError("Error: provide exactly one of --file or --stdin.");
        }

        string input;
        if (parseResult.UseStdin)
        {
            input = MetaCliStandardInput.ReadToEnd();
        }
        else
        {
            if (!File.Exists(parseResult.FilePath))
            {
                return PrintDataError("E_FILE_NOT_FOUND", $"input file '{parseResult.FilePath}' was not found.");
            }

            input = await File.ReadAllTextAsync(parseResult.FilePath).ConfigureAwait(false);
        }

        try
        {
            var workspace = await LoadWorkspaceForCommandAsync(parseResult.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.WorkspaceConfig);

            var entity = workspace.Model.FindEntity(entityName);
            if (entity == null)
            {
                return PrintDataError("E_ENTITY_NOT_FOUND", $"entity '{entityName}' does not exist.");
            }

            var rows = ParseBulkInputRows(input, parseResult.Format);
            var (plan, ids) = BuildBulkInsertPlan(
                workspace,
                entity,
                rows,
                parseResult.AutoId);
            return await ExecuteOperationsAgainstLoadedWorkspaceAsync(
                    workspace,
                    plan,
                    commandName: "bulk-insert",
                    successMessage: $"bulk insert {entityName}",
                    successDetails: BuildBulkInsertSuccessDetails(ids.Count))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}

