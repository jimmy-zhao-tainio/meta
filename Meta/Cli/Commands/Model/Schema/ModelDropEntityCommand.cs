internal sealed partial class CliRuntime
{
    async Task<int> ModelDropEntityAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 3)
        {
            return PrintUsageError(
                "Usage: model drop-entity <Entity> [--workspace <path>]");
        }
    
        var entityName = commandArgs[2];
        var options = ParseMutatingCommonOptions(commandArgs, startIndex: 3);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }
    
        try
        {
            var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
            RequireEntity(workspace, entityName);
    
            var rows = workspace.Instance.GetOrCreateEntityRecords(entityName);
            if (rows.Count > 0)
            {
                var firstRow = rows
                    .OrderBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
                    .First();
                return PrintFormattedError(
                    "E_ENTITY_NOT_EMPTY",
                    $"Cannot drop entity {entityName}",
                    exitCode: 4,
                    where: BuildWhere(
                        ("entity", entityName),
                        ("rows", rows.Count.ToString(CultureInfo.InvariantCulture))),
                    hints: new[]
                    {
                        $"{entityName} has {rows.Count.ToString(CultureInfo.InvariantCulture)} instances.",
                        $"Next: meta view instance {entityName} {QuoteInstanceId(firstRow.Id)}",
                    });
            }
    
            var inboundRelationships = workspace.Model.Entities
                .SelectMany(fromEntity => fromEntity.Relationships
                    .Where(relationship => string.Equals(relationship.Entity, entityName, StringComparison.OrdinalIgnoreCase))
                    .Select(_ => new
                    {
                        FromEntity = fromEntity.Name,
                        ToEntity = entityName,
                    }))
                .OrderBy(item => item.FromEntity, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ToEntity, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (inboundRelationships.Count > 0)
            {
                return PrintFormattedErrorWithTable(
                    code: "E_ENTITY_HAS_INBOUND_RELATIONSHIPS",
                    message: $"Entity '{entityName}' has inbound relationships.",
                    exitCode: 4,
                    where: new[]
                    {
                        ("entity", entityName),
                        ("inboundRelationships", inboundRelationships.Count.ToString(CultureInfo.InvariantCulture)),
                    },
                    hints: new[]
                    {
                        $"Inbound relationships: {inboundRelationships.Count.ToString(CultureInfo.InvariantCulture)}.",
                        $"Next: meta graph inbound {entityName}",
                    },
                    tableTitle: "Inbound relationships",
                    headers: new[] { "FromEntity", "ToEntity" },
                    rows: inboundRelationships
                        .Take(20)
                        .Select(item => (IReadOnlyList<string>)new[]
                        {
                            item.FromEntity,
                            item.ToEntity,
                        })
                        .ToList());
            }
    
            var operation = new WorkspaceOp
            {
                Type = WorkspaceOpTypes.DeleteEntity,
                EntityName = entityName,
            };
            return await ExecuteOperationAsync(
                    options.WorkspacePath,
                    operation,
                    "model drop-entity",
                    "entity removed",
                    ("Entity", entityName))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}


