internal sealed partial class CliRuntime
{
    async Task<int> InsertAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var explicitId = OptionalValue("Id");
        var parseResult = ReadMutatingEntityOptions(commandArgs, startIndex: 2, allowAutoId: true);
        if (!parseResult.Ok)
        {
            return PrintArgumentError(parseResult.ErrorMessage);
        }

        if (!string.IsNullOrWhiteSpace(explicitId) && parseResult.AutoId)
        {
            return PrintArgumentError("Error: --auto-id cannot be combined with positional <Id>.");
        }

        if (string.IsNullOrWhiteSpace(explicitId) && !parseResult.AutoId)
        {
            return PrintArgumentError("Error: insert requires either positional <Id> or --auto-id.");
        }

        if (ContainsIdSetAssignment(parseResult.SetValues))
        {
            return PrintArgumentError("Error: do not use --set Id. Use positional <Id> or --auto-id.");
        }

        if (parseResult.SetValues.Count == 0)
        {
            return PrintArgumentError("Error: insert requires at least one --set Field=Value.");
        }

        try
        {
            var model = await WorkspaceComposition.MaterializeModelAsync(CurrentWorkspace)
                .ConfigureAwait(false);
            var entity = RequireEntity(model, entityName);
            var resolvedId = parseResult.AutoId
                ? await GenerateNextAutoIdAsync(CurrentWorkspace, entity.Name).ConfigureAwait(false)
                : explicitId;

            var operation = BuildInsertRecordOperation(
                entity,
                parseResult.SetValues,
                resolvedId);
            return await ExecuteOperationsAsync(
                    [operation],
                    commandName: "insert",
                    successMessage: $"created {BuildEntityInstanceAddress(entityName, operation.Id)}",
                    successDetails: BuildRowPreviewDetails(entity, operation.Values))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }

    async Task<string> GenerateNextAutoIdAsync(
        IMetaWorkspaceSource workspace,
        string entityName)
    {
        var numericIds = new List<long>();

        await foreach (var row in workspace.ReadRecordsAsync(entityName))
        {
            var id = row.Id?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!long.TryParse(id, out var numericId))
            {
                throw new InvalidOperationException(
                    $"Cannot auto-generate Id for entity '{entityName}' because existing Id '{row.Id}' is not numeric. Use explicit <Id>.");
            }

            numericIds.Add(numericId);
        }

        var next = numericIds.Count == 0 ? 1L : numericIds.Max() + 1;
        return next.ToString();
    }
}


