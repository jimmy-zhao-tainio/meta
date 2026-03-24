internal sealed partial class CliRuntime
{
    async Task<int> InstanceRelationshipListAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 5)
        {
            return PrintUsageError("Usage: instance relationship list <FromEntity> <FromId> [--workspace <path>]");
        }
    
        var fromEntityName = commandArgs[3];
        var fromId = commandArgs[4];
        var options = ParseWorkspaceOnlyOptions(commandArgs, startIndex: 5);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }
    
        try
        {
            var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
            var fromEntity = RequireEntity(workspace, fromEntityName);
            var row = ResolveRowById(workspace, fromEntityName, fromId);
            var relationshipRows = fromEntity.Relationships
                .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase)
                .Where(relationship =>
                    row.RelationshipIds.TryGetValue(relationship.GetColumnName(), out var relationshipId) &&
                    !string.IsNullOrWhiteSpace(relationshipId))
                .Select(item => new
                {
                    Relationship = item.GetColumnName(),
                    ToEntity = item.Entity,
                    ToInstance = BuildEntityInstanceAddress(item.Entity, row.RelationshipIds[item.GetColumnName()]),
                })
                .ToList();

            if (relationshipRows.Count == 0)
            {
                presenter.WriteOk("no relationship usage", ("Instance", BuildEntityInstanceAddress(fromEntityName, row.Id)));
                return 0;
            }
    
            presenter.WriteInfo("Relationships:");
            presenter.WriteInfo($"  FromInstance: {BuildEntityInstanceAddress(fromEntityName, row.Id)}");
            presenter.WriteTable(
                new[] { "Relationship", "ToEntity", "ToInstance" },
                relationshipRows
                    .Select(item => (IReadOnlyList<string>)new[]
                    {
                        item.Relationship,
                        item.ToEntity,
                        item.ToInstance,
                    })
                    .ToList());
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}


