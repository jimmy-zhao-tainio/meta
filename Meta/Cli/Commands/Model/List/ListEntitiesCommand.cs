internal sealed partial class CliRuntime
{
    async Task<int> ListEntitiesAsync(string[] commandArgs)
    {
        var options = ParseWorkspaceOnlyOptions(commandArgs, startIndex: 2);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }
    
        var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
        PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
        var rowsByEntity = workspace.Instance.RecordsByEntity;
    
        var entities = workspace.Model.Entities
            .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entity => new
            {
                Name = entity.Name,
                Properties = entity.Properties.Count,
                Relationships = entity.Relationships.Count,
                Rows = rowsByEntity.TryGetValue(entity.Name, out var rows) ? rows.Count : 0,
            })
            .ToList();

        presenter.WriteInfo($"Entities ({entities.Count}):");
        presenter.WriteTable(
            new[] { "Name", "Rows", "Properties", "Relationships" },
            entities
                .Select(entity => (IReadOnlyList<string>)new[]
                {
                    entity.Name,
                    entity.Rows.ToString(CultureInfo.InvariantCulture),
                    entity.Properties.ToString(CultureInfo.InvariantCulture),
                    entity.Relationships.ToString(CultureInfo.InvariantCulture),
                })
                .ToList());
    
        return 0;
    }
}

