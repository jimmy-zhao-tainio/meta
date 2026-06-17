internal sealed partial class CliRuntime
{
    void PrintSelectedRecord(string entityName, GenericRecord record)
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

    void PrintQueryResult(Workspace workspace, string entityName, string whereExpression, IReadOnlyList<GenericRecord> rows, int top)
    {
        presenter.WriteInfo($"Query: {entityName}");
        presenter.WriteInfo($"Filter: {whereExpression}");
        presenter.WriteInfo($"Matches: {rows.Count.ToString(CultureInfo.InvariantCulture)}");

        var limit = top <= 0 ? 200 : top;
        var previewColumns = ResolveQueryPreviewColumns(workspace, entityName);
        var previewRows = new List<IReadOnlyList<string>>();
        foreach (var row in rows.Take(limit))
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

        if (rows.Count > limit)
        {
            presenter.WriteInfo($"InstancesTruncated: {(rows.Count - limit).ToString(CultureInfo.InvariantCulture)}");
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

    IReadOnlyList<GenericRecord> QueryRows(Workspace workspace, string entityName, IReadOnlyList<(string Mode, string Field, string Value)> filters)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        var entity = RequireEntity(workspace, entityName);
        IEnumerable<GenericRecord> rows = workspace.Instance.GetOrCreateEntityRecords(entityName);
        if (filters is { Count: > 0 })
        {
            foreach (var filter in filters)
            {
                var resolvedField = ResolveQueryField(entity, filter.Field);
                rows = rows.Where(row => QueryFilterMatches(row, resolvedField, filter));
            }
        }

        return rows
            .OrderBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    string ResolveQueryField(GenericEntity entity, string fieldName)
    {
        if (string.Equals(fieldName, "Id", StringComparison.OrdinalIgnoreCase))
        {
            return "Id";
        }

        var property = entity.Properties
            .FirstOrDefault(item => string.Equals(item.Name, fieldName, StringComparison.OrdinalIgnoreCase));
        if (property != null)
        {
            return property.Name;
        }

        var relationship = entity.Relationships
            .FirstOrDefault(item =>
                string.Equals(item.GetRoleOrDefault(), fieldName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.GetColumnName(), fieldName, StringComparison.OrdinalIgnoreCase));
        if (relationship != null)
        {
            return relationship.GetColumnName();
        }

        throw new InvalidOperationException($"Field '{fieldName}' does not exist on entity '{entity.Name}'.");
    }

    bool QueryFilterMatches(GenericRecord row, string resolvedField, (string Mode, string Field, string Value) filter)
    {
        var fieldValue = GetQueryFieldValue(row, resolvedField);
        if (string.Equals(filter.Mode, "contains", StringComparison.OrdinalIgnoreCase))
        {
            return fieldValue.IndexOf(filter.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        return string.Equals(fieldValue, filter.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    string GetQueryFieldValue(GenericRecord row, string fieldName)
    {
        if (string.Equals(fieldName, "Id", StringComparison.OrdinalIgnoreCase))
        {
            return row.Id ?? string.Empty;
        }

        if (row.Values.TryGetValue(fieldName, out var value))
        {
            return value ?? string.Empty;
        }

        if (row.RelationshipIds.TryGetValue(fieldName, out var relationshipValue))
        {
            return relationshipValue ?? string.Empty;
        }

        return string.Empty;
    }

    void PrintGraphStats(Workspace workspace, GraphStatsReport stats, int topN)
    {
        presenter.WriteInfo($"Graph: {workspace.Model.Name}");
        presenter.WriteInfo($"Nodes: {stats.NodeCount.ToString(CultureInfo.InvariantCulture)}");
        presenter.WriteInfo(
            $"Edges: declared={stats.EdgeCount.ToString(CultureInfo.InvariantCulture)} unique={stats.UniqueEdgeCount.ToString(CultureInfo.InvariantCulture)} dup={stats.DuplicateEdgeCount.ToString(CultureInfo.InvariantCulture)} missingTarget={stats.MissingTargetEdgeCount.ToString(CultureInfo.InvariantCulture)}");
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

    IReadOnlyList<string> ResolveQueryPreviewColumns(Workspace workspace, string entityName)
    {
        var entity = workspace.Model.FindEntity(entityName);
        if (entity == null)
        {
            return new[] { "Id" };
        }

        var columns = new List<string> { "Id" };
        var additional = entity.Properties
            .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            .OrderBy(property => property.IsNullable ? 1 : 0)
            .ThenBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Select(property => property.Name)
            .ToList();
        columns.AddRange(additional);
        return columns;
    }
}
