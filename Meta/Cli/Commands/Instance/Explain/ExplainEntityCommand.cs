internal sealed partial class CliRuntime
{
    async Task<int> ViewEntityAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 3)
        {
            return PrintUsageError("Usage: view entity <Entity> [--workspace <path>]");
        }
    
        var entityName = commandArgs[2];
        var options = ParseWorkspaceOnlyOptions(commandArgs, startIndex: 3);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }
    
        var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
        PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
        var entity = RequireEntity(workspace, entityName);
        var rowCount = workspace.Instance.RecordsByEntity.TryGetValue(entity.Name, out var rows) ? rows.Count : 0;
        var properties = entity.Properties
            .Where(item => !string.Equals(item.Name, "Id", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new
            {
                name = item.Name,
                isRequired = !item.IsNullable,
                dataType = item.DataType,
            })
            .ToList();
        properties.Insert(0, new
        {
            name = "Id",
            isRequired = true,
            dataType = "string",
        });

        presenter.WriteInfo($"Entity: {entity.Name}");
        presenter.WriteInfo($"Rows: {rowCount.ToString(CultureInfo.InvariantCulture)}");

        presenter.WriteInfo("Properties:");
        presenter.WriteTable(
            new[] { "Name", "Type", "Required" },
            properties
                .Select(property => (IReadOnlyList<string>)new[]
                {
                    property.name,
                    property.dataType,
                    property.isRequired ? "required" : "optional",
                })
                .ToList());
    
        var relationships = entity.Relationships
            .OrderBy(item => item.Entity, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Entity)
            .ToList();
        presenter.WriteInfo($"Relationships: {relationships.Count.ToString(CultureInfo.InvariantCulture)}");
        presenter.WriteInfo("RelationshipTargets:");
        if (relationships.Count == 0)
        {
            presenter.WriteInfo("  (none)");
        }
        else
        {
            presenter.WriteTable(
                new[] { "Target" },
                relationships.Select(relationship => (IReadOnlyList<string>)new[] { relationship }).ToList());
        }
    
        return 0;
    }
}

