internal sealed partial class CliRuntime
{
    async Task<int> BulkInsertAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 2)
        {
            return PrintUsageError(
                "Usage: bulk-insert <Entity> [--from tsv|csv] [--file <path>|--stdin] [--key Field[,Field2...]] [--auto-id] [--workspace <path>]");
        }
    
        var entityName = commandArgs[1];
        var parseResult = ParseUpsertOptions(commandArgs, startIndex: 2);
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

        if (parseResult.AutoId && parseResult.KeyFields.Count > 0)
        {
            return PrintArgumentError("Error: --auto-id cannot be combined with --key.");
        }
    
        string input;
        if (parseResult.UseStdin)
        {
            input = await Console.In.ReadToEndAsync().ConfigureAwait(false);
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
            var operation = BuildUpsertOperationFromRows(
                workspace,
                entity,
                rows,
                parseResult.KeyFields,
                autoEnsure: false,
                autoId: parseResult.AutoId);
            BulkRelationshipResolver.ResolveRelationshipIds(workspace, operation);
            return await ExecuteOperationsAgainstLoadedWorkspaceAsync(
                    workspace,
                    new[] { operation },
                    commandName: "bulk-insert",
                    successMessage: $"bulk insert {entityName}",
                    successDetails: BuildUpsertSuccessDetails(
                        workspace,
                        entityName,
                        operation.RowPatches.Select(patch => patch.Id).ToList()))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}

