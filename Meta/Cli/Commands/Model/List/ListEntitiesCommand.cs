internal sealed partial class CliRuntime
{
    async Task<int> ListEntitiesAsync(string[] commandArgs)
    {
        var options = ReadWorkspaceOnlyOptions(commandArgs, startIndex: 2);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        var source = CurrentWorkspace;
        var entities = new List<(string Name, long Rows, int Properties, int Relationships)>();
        await foreach (var entityName in source.ReadEntityNamesAsync())
        {
            var propertyCount = 0;
            await foreach (var _ in source.ReadPropertiesAsync(entityName))
            {
                propertyCount++;
            }

            var relationshipCount = 0;
            await foreach (var _ in source.ReadRelationshipsAsync(entityName))
            {
                relationshipCount++;
            }

            entities.Add((
                entityName,
                await source.CountRecordsAsync(entityName).ConfigureAwait(false),
                propertyCount,
                relationshipCount));
        }

        entities.Sort((left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));

        presenter.WriteInfo($"Entities ({entities.Count}):");
        presenter.WriteTable(
            new[] { "Name", "Rows", "Properties", "Relationships" },
            entities
                .Select(entity => (IReadOnlyList<string>)new[]
                {
                    entity.Name,
                    entity.Rows.ToString(CultureInfo.InvariantCulture),
                    entity.Properties.ToString(CultureInfo.InvariantCulture),
                    entity.Relationships.ToString(CultureInfo.InvariantCulture),
                })
                .ToList());

        return 0;
    }
}

