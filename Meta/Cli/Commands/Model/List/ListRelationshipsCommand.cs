internal sealed partial class CliRuntime
{
    async Task<int> ListRelationshipsAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var options = ReadWorkspaceOnlyOptions(commandArgs, startIndex: 3);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        var source = CurrentWorkspace;
        var resolvedEntityName = await ResolveEntityNameAsync(source, entityName).ConfigureAwait(false);
        var relationships = new List<RelationshipDefinition>();
        await foreach (var relationship in source.ReadRelationshipsAsync(resolvedEntityName))
        {
            relationships.Add(relationship);
        }

        var refs = relationships
            .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(relationship => relationship.TargetEntityName, StringComparer.OrdinalIgnoreCase)
            .Select(relationship => new
            {
                Name = relationship.GetColumnName(),
                Target = relationship.TargetEntityName,
            })
            .ToList();

        presenter.WriteInfo($"Relationships: {resolvedEntityName} ({refs.Count})");
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

