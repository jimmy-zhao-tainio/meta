internal sealed partial class CliRuntime
{
    async Task<int> ListPropertiesAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var options = ReadWorkspaceOnlyOptions(commandArgs, startIndex: 3);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        var source = CurrentWorkspace;
        var resolvedEntityName = await ResolveEntityNameAsync(source, entityName).ConfigureAwait(false);
        var properties = new List<(string Name, bool Required)>
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

        presenter.WriteInfo($"Properties: {resolvedEntityName}");
        presenter.WriteTable(
            new[] { "Name", "Required" },
            properties
                .Select(property => (IReadOnlyList<string>)new[]
                {
                    property.Name,
                    property.Required ? "yes" : "no",
                })
                .ToList());

        return 0;
    }
}

