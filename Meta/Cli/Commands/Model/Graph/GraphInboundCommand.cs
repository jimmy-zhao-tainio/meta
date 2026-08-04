internal sealed partial class CliRuntime
{
    async Task<int> GraphInboundAsync(string[] commandArgs)
    {
        var targetEntityName = RequiredValue("Entity");
        var options = ReadGraphInboundOptions(commandArgs, startIndex: 3);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        try
        {
            var resolvedTargetEntityName = await ResolveEntityNameAsync(CurrentWorkspace, targetEntityName)
                .ConfigureAwait(false);
            var inboundAll = new List<(string FromEntity, string ToEntity)>();
            await foreach (var fromEntity in CurrentWorkspace.ReadEntityNamesAsync())
            {
                await foreach (var relationship in CurrentWorkspace.ReadRelationshipsAsync(fromEntity))
                {
                    if (MetaName.Comparer.Equals(relationship.TargetEntityName, resolvedTargetEntityName))
                    {
                        inboundAll.Add((fromEntity, resolvedTargetEntityName));
                    }
                }
            }

            inboundAll = inboundAll
                .OrderBy(item => item.FromEntity, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ToEntity, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var inbound = inboundAll.Take(options.Top).ToList();

            presenter.WriteInfo($"Inbound relationships: {resolvedTargetEntityName} ({inboundAll.Count.ToString(CultureInfo.InvariantCulture)})");
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

