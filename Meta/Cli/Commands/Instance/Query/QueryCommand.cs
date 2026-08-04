internal sealed partial class CliRuntime
{
    async Task<int> QueryAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var options = ReadQueryCommandOptions(commandArgs, startIndex: 2);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        try
        {
            var source = CurrentWorkspace;
            var resolvedEntityName = await ResolveEntityNameAsync(source, entityName).ConfigureAwait(false);
            var maximumRecords = options.Top <= 0 ? 200 : options.Top;
            var conditions = options.Filters
                .Select(filter => string.Equals(filter.Mode, "contains", StringComparison.OrdinalIgnoreCase)
                    ? (RecordCondition)new RecordCondition.Contains(filter.Field, filter.Value)
                    : new RecordCondition.Equal(filter.Field, filter.Value))
                .ToArray();
            var result = await source.QueryRecordsAsync(
                    resolvedEntityName,
                    new RecordQuery(maximumRecords, conditions))
                .ConfigureAwait(false);
            var properties = new List<PropertyDefinition>();
            await foreach (var property in source.ReadPropertiesAsync(resolvedEntityName))
            {
                properties.Add(property);
            }

            var renderedFilter = BuildFilterSummary(options.Filters);
            PrintQueryResult(
                resolvedEntityName,
                renderedFilter,
                result,
                properties);

            return 0;
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}

