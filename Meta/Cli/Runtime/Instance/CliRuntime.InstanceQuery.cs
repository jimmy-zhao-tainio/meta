internal sealed partial class CliRuntime
{
    static IMetaWorkspaceSource CreateWorkspaceSource(InMemoryWorkspace workspace) =>
        new InMemoryWorkspaceSource(workspace);

    static async Task<string> ResolveEntityNameAsync(
        IMetaWorkspaceSource source,
        string entityName)
    {
        await foreach (var candidate in source.ReadEntityNamesAsync())
        {
            if (MetaName.Comparer.Equals(candidate, entityName))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Entity '{entityName}' does not exist.");
    }

    void PrintSelectedRecord(string entityName, RecordData record)
    {
        presenter.WriteInfo($"Instance: {BuildEntityInstanceAddress(entityName, record.Id)}");
        var rows = new List<IReadOnlyList<string>>();
        foreach (var value in record.Values
                     .OrderBy(item => string.Equals(item.Key, "Id", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new[] { value.Key, value.Value });
        }

        foreach (var relationship in record.RelationshipIds
                     .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new[] { relationship.Key, relationship.Value });
        }

        presenter.WriteTable(new[] { "Field", "Value" }, rows);
    }

    void PrintQueryResult(
        string entityName,
        string whereExpression,
        RecordQueryResult result,
        IReadOnlyCollection<PropertyDefinition> properties)
    {
        presenter.WriteInfo($"Query: {entityName}");
        presenter.WriteInfo($"Filter: {whereExpression}");
        presenter.WriteInfo($"Matches: {result.TotalCount.ToString(CultureInfo.InvariantCulture)}");

        var previewColumns = new List<string> { "Id" };
        previewColumns.AddRange(properties
            .OrderBy(property => property.IsRequired ? 0 : 1)
            .ThenBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Select(property => property.Name));
        var previewRows = new List<IReadOnlyList<string>>();
        foreach (var row in result.Records)
        {
            var cells = new List<string>();
            foreach (var column in previewColumns)
            {
                if (string.Equals(column, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    cells.Add(row.Id);
                    continue;
                }

                cells.Add(row.Values.TryGetValue(column, out var value) ? value : string.Empty);
            }

            previewRows.Add(cells);
        }

        presenter.WriteTable(previewColumns, previewRows);

        if (result.TotalCount > result.Records.Count)
        {
            presenter.WriteInfo(
                $"InstancesTruncated: {(result.TotalCount - result.Records.Count).ToString(CultureInfo.InvariantCulture)}");
        }
    }

    string BuildFilterSummary(IReadOnlyList<(string Mode, string Field, string Value)> filters)
    {
        if (filters == null || filters.Count == 0)
        {
            return "(none)";
        }

        return string.Join(
            " AND ",
            filters.Select(filter =>
            {
                if (string.Equals(filter.Mode, "contains", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{filter.Field} contains {QuoteInstanceId(filter.Value)}";
                }

                return $"{filter.Field} = {QuoteInstanceId(filter.Value)}";
            }));
    }

    void PrintGraphStats(GenericModel model, GraphStatsReport stats, int topN)
    {
        presenter.WriteInfo($"Graph: {model.Name}");
        presenter.WriteInfo($"Nodes: {stats.NodeCount.ToString(CultureInfo.InvariantCulture)}");
        presenter.WriteInfo($"Edges: {stats.EdgeCount.ToString(CultureInfo.InvariantCulture)}");
        presenter.WriteInfo(
            $"Components: {stats.WeaklyConnectedComponents.ToString(CultureInfo.InvariantCulture)}  Roots: {stats.RootCount.ToString(CultureInfo.InvariantCulture)}  Sinks: {stats.SinkCount.ToString(CultureInfo.InvariantCulture)}  Isolated: {stats.IsolatedCount.ToString(CultureInfo.InvariantCulture)}");
        presenter.WriteInfo(
            $"Cycles: {(stats.HasCycles ? "yes" : "no")}  MaxDepth: {(stats.DagMaxDepth.HasValue ? stats.DagMaxDepth.Value.ToString(CultureInfo.InvariantCulture) : "n/a")}");
        presenter.WriteInfo(
            $"AvgDegree: in={stats.AverageInDegree.ToString("F3", CultureInfo.InvariantCulture)} out={stats.AverageOutDegree.ToString("F3", CultureInfo.InvariantCulture)}");

        presenter.WriteInfo($"Top out-degree ({topN.ToString(CultureInfo.InvariantCulture)}):");
        presenter.WriteTable(
            new[] { "Entity", "OutDegree" },
            stats.TopOutDegree
                .Select(hub => (IReadOnlyList<string>)new[]
                {
                    hub.Entity,
                    hub.Degree.ToString(CultureInfo.InvariantCulture),
                })
                .ToList());

        presenter.WriteInfo($"Top in-degree ({topN.ToString(CultureInfo.InvariantCulture)}):");
        presenter.WriteTable(
            new[] { "Entity", "InDegree" },
            stats.TopInDegree
                .Select(hub => (IReadOnlyList<string>)new[]
                {
                    hub.Entity,
                    hub.Degree.ToString(CultureInfo.InvariantCulture),
                })
                .ToList());

        if (stats.CycleSamples.Count > 0)
        {
            presenter.WriteInfo($"Cycle samples ({stats.CycleSamples.Count.ToString(CultureInfo.InvariantCulture)}):");
            foreach (var sample in stats.CycleSamples)
            {
                presenter.WriteInfo($"  {sample}");
            }
        }
    }

}
