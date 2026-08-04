using MetaCli.Core;

internal sealed partial class CliRuntime
{
    async Task<int> BulkInsertAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var parseResult = ReadUpsertOptions(commandArgs, startIndex: 2);
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
            var model = await WorkspaceComposition.MaterializeModelAsync(CurrentWorkspace)
                .ConfigureAwait(false);
            var entity = model.FindEntity(entityName);
            if (entity == null)
            {
                return PrintDataError("E_ENTITY_NOT_FOUND", $"entity '{entityName}' does not exist.");
            }

            var existingRecords = new List<RecordData>();
            await foreach (var record in CurrentWorkspace.ReadRecordsAsync(entity.Name))
            {
                existingRecords.Add(record);
            }

            var rows = ParseBulkInputRows(input, parseResult.Format);
            var plan = BuildUpsertOperationsFromRows(
                entity,
                existingRecords,
                rows,
                parseResult.KeyFields,
                autoId: parseResult.AutoId);
            var existingIds = existingRecords
                .Select(record => record.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return await ExecuteOperationsAsync(
                    plan.Operations,
                    commandName: "bulk-insert",
                    successMessage: $"bulk insert {entityName}",
                    successDetails: BuildUpsertSuccessDetails(
                        existingIds,
                        plan.RowIds))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}

