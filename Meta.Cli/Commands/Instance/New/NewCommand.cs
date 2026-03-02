internal sealed partial class CliRuntime
{
    async Task<int> InsertAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 2)
        {
            return PrintUsageError("Usage: insert <Entity> [<Id>|--auto-id] --set Field=Value [--set Field=Value ...] [--workspace <path>]");
        }
    
        var entityName = commandArgs[1];
        string? explicitId = null;
        var optionsStartIndex = 2;

        if (commandArgs.Length > 2 && !commandArgs[2].StartsWith("--", StringComparison.Ordinal))
        {
            explicitId = commandArgs[2];
            optionsStartIndex = 3;
        }

        var parseResult = ParseMutatingEntityOptions(commandArgs, startIndex: optionsStartIndex, allowAutoId: true);
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
            var workspace = await LoadWorkspaceForCommandAsync(parseResult.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
            var entity = RequireEntity(workspace, entityName);
            var resolvedId = parseResult.AutoId
                ? GenerateNextAutoId(workspace, entityName)
                : explicitId!;
    
            var rowPatch = BuildRowPatchForCreate(workspace, entity, parseResult.SetValues, resolvedId);
            var operation = new WorkspaceOp
            {
                Type = WorkspaceOpTypes.BulkUpsertRows,
                EntityName = entityName,
                RowPatches = { rowPatch },
            };
    
            BulkRelationshipResolver.ResolveRelationshipIds(workspace, operation);
            return await ExecuteOperationsAgainstLoadedWorkspaceAsync(
                    workspace,
                    new[] { operation },
                    commandName: "insert",
                    successMessage: $"created {BuildEntityInstanceAddress(entityName, rowPatch.Id)}",
                    successDetails: BuildRowPreviewDetails(entity, rowPatch))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }

    string GenerateNextAutoId(Workspace workspace, string entityName)
    {
        var rows = workspace.Instance.GetOrCreateEntityRecords(entityName);
        var numericIds = new List<long>();

        foreach (var row in rows)
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


