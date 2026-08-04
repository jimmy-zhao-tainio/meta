internal sealed partial class CliRuntime
{
    async Task<int> ModelDropEntityAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        try
        {
            var resolvedEntityName = await ResolveEntityNameAsync(CurrentWorkspace, entityName)
                .ConfigureAwait(false);
            var rowCount = await CurrentWorkspace.CountRecordsAsync(resolvedEntityName)
                .ConfigureAwait(false);
            if (rowCount > 0)
            {
                var firstRow = await CurrentWorkspace.QueryRecordsAsync(
                        resolvedEntityName,
                        new RecordQuery(1))
                    .ConfigureAwait(false);
                return PrintFormattedError(
                    "E_ENTITY_NOT_EMPTY",
                    $"Cannot drop entity {resolvedEntityName}",
                    exitCode: 4,
                    where: BuildWhere(
                        ("entity", resolvedEntityName),
                        ("rows", rowCount.ToString(CultureInfo.InvariantCulture))),
                    hints: new[]
                    {
                        $"{resolvedEntityName} has {rowCount.ToString(CultureInfo.InvariantCulture)} instances.",
                        $"Next: meta view instance {resolvedEntityName} {QuoteInstanceId(firstRow.Records[0].Id)}",
                    });
            }

            var inboundRelationships = new List<(string FromEntity, string ToEntity)>();
            await foreach (var fromEntity in CurrentWorkspace.ReadEntityNamesAsync())
            {
                await foreach (var relationship in CurrentWorkspace.ReadRelationshipsAsync(fromEntity))
                {
                    if (MetaName.Comparer.Equals(relationship.TargetEntityName, resolvedEntityName))
                    {
                        inboundRelationships.Add((fromEntity, resolvedEntityName));
                    }
                }
            }

            inboundRelationships = inboundRelationships
                .OrderBy(item => item.FromEntity, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ToEntity, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (inboundRelationships.Count > 0)
            {
                return PrintFormattedErrorWithTable(
                    code: "E_ENTITY_HAS_INBOUND_RELATIONSHIPS",
                    message: $"Entity '{resolvedEntityName}' has inbound relationships.",
                    exitCode: 4,
                    where: new[]
                    {
                        ("entity", resolvedEntityName),
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

            return await ExecuteOperationAsync(
                    new Operation.RemoveEntity(resolvedEntityName),
                    "model drop-entity",
                    "entity removed",
                    ("Entity", resolvedEntityName))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}


