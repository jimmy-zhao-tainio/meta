internal sealed partial class CliRuntime
{
    async Task<int> ViewEntityAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var options = ReadWorkspaceOnlyOptions(commandArgs, startIndex: 3);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        var source = CurrentWorkspace;
        var resolvedEntityName = await ResolveEntityNameAsync(source, entityName).ConfigureAwait(false);
        var rowCount = await source.CountRecordsAsync(resolvedEntityName).ConfigureAwait(false);
        var properties = new List<(string Name, bool IsRequired)>
        {
            ("Id", true),
        };
        await foreach (var property in source.ReadPropertiesAsync(resolvedEntityName))
        {
            properties.Add((property.Name, property.IsRequired));
        }

        properties = properties
            .OrderBy(property => MetaName.Comparer.Equals(property.Name, "Id") ? 0 : 1)
            .ThenBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        presenter.WriteInfo($"Entity: {resolvedEntityName}");
        presenter.WriteInfo($"Rows: {rowCount.ToString(CultureInfo.InvariantCulture)}");

        presenter.WriteInfo("Properties:");
        presenter.WriteTable(
            new[] { "Name", "Required" },
            properties
                .Select(property => (IReadOnlyList<string>)new[]
                {
                    property.Name,
                    property.IsRequired ? "required" : "optional",
                })
                .ToList());

        var relationships = new List<string>();
        await foreach (var relationship in source.ReadRelationshipsAsync(resolvedEntityName))
        {
            relationships.Add(relationship.TargetEntityName);
        }

        relationships.Sort(StringComparer.OrdinalIgnoreCase);
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

