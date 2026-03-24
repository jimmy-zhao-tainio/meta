internal sealed partial class CliRuntime
{
    async Task<int> ListRelationshipsAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 3)
        {
            return PrintUsageError("Usage: list relationships <Entity> [--workspace <path>]");
        }
    
        var entityName = commandArgs[2];
        var options = ParseWorkspaceOnlyOptions(commandArgs, startIndex: 3);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }
    
        var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
        PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
        var entity = workspace.Model.FindEntity(entityName);
        if (entity == null)
        {
            return PrintDataError("E_ENTITY_NOT_FOUND", $"Entity '{entityName}' does not exist.");
        }
    
        var refs = entity.Relationships
            .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase)
            .Select(relationship => new
            {
                Name = relationship.GetColumnName(),
                Target = relationship.Entity,
            })
            .ToList();

        presenter.WriteInfo($"Relationships: {entity.Name} ({refs.Count})");
        presenter.WriteInfo("Required: (n/a)");
        presenter.WriteTable(
            new[] { "Name", "Target" },
            refs.Select(relationship => (IReadOnlyList<string>)new[]
            {
                relationship.Name,
                relationship.Target,
            }).ToList());
    
        return 0;
    }
}

