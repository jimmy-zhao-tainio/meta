internal sealed partial class CliRuntime
{
    async Task<int> GraphInboundAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 3)
        {
            return PrintUsageError("Usage: graph inbound <Entity> [--workspace <path>] [--top <n>]");
        }
    
        var targetEntityName = commandArgs[2];
        var options = ParseGraphInboundOptions(commandArgs, startIndex: 3);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }
    
        try
        {
            var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
            RequireEntity(workspace, targetEntityName);
    
            var inboundAll = workspace.Model.Entities
                .SelectMany(fromEntity => fromEntity.Relationships
                    .Where(relationship => string.Equals(relationship.Entity, targetEntityName, StringComparison.OrdinalIgnoreCase))
                    .Select(_ => new
                    {
                        FromEntity = fromEntity.Name,
                        ToEntity = targetEntityName,
                    }))
                .OrderBy(item => item.FromEntity, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ToEntity, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var inbound = inboundAll.Take(options.Top).ToList();

            presenter.WriteInfo($"Inbound relationships: {targetEntityName} ({inboundAll.Count.ToString(CultureInfo.InvariantCulture)})");
            presenter.WriteTable(
                new[] { "FromEntity", "ToEntity" },
                inbound
                    .Select(item => (IReadOnlyList<string>)new[]
                    {
                        item.FromEntity,
                        item.ToEntity,
                    })
                    .ToList());
            if (inboundAll.Count > inbound.Count)
            {
                presenter.WriteInfo($"InstancesTruncated: {(inboundAll.Count - inbound.Count).ToString(CultureInfo.InvariantCulture)}");
            }
    
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}

