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

        var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
        PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
        var entity = workspace.Model.FindEntity(entityName);
        if (entity == null)
        {
            return PrintDataError("E_ENTITY_NOT_FOUND", $"Entity '{entityName}' does not exist.");
        }

        var properties = entity.Properties
            .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .Select(property => new
            {
                Name = property.Name,
                Required = !property.IsNullable,
            })
            .ToList();
        properties.Insert(0, new
        {
            Name = "Id",
            Required = true,
        });

        presenter.WriteInfo($"Properties: {entity.Name}");
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

