internal sealed partial class CliRuntime
{
    async Task<int> InstanceRelationshipListAsync(string[] commandArgs)
    {
        var fromEntityName = RequiredValue("FromEntity");
        var fromId = RequiredValue("FromId");
        var options = ReadWorkspaceOnlyOptions(commandArgs, startIndex: 5);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        try
        {
            var source = CurrentWorkspace;
            var resolvedEntityName = await ResolveEntityNameAsync(source, fromEntityName).ConfigureAwait(false);
            var row = await source.ReadRecordAsync(resolvedEntityName, fromId).ConfigureAwait(false) ??
                      throw new InvalidOperationException(
                          $"Instance with Id '{fromId}' does not exist in entity '{resolvedEntityName}'.");
            var relationships = new List<RelationshipDefinition>();
            await foreach (var relationship in source.ReadRelationshipsAsync(resolvedEntityName))
            {
                relationships.Add(relationship);
            }

            var relationshipRows = relationships
                .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(relationship => relationship.TargetEntityName, StringComparer.OrdinalIgnoreCase)
                .Where(relationship =>
                    row.RelationshipIds.TryGetValue(relationship.GetColumnName(), out var relationshipId) &&
                    !string.IsNullOrWhiteSpace(relationshipId))
                .Select(item => new
                {
                    Relationship = item.GetColumnName(),
                    ToEntity = item.TargetEntityName,
                    ToInstance = BuildEntityInstanceAddress(item.TargetEntityName, row.RelationshipIds[item.GetColumnName()]),
                })
                .ToList();

            if (relationshipRows.Count == 0)
            {
                presenter.WriteInfo("Relationships");
                presenter.WriteInfo("  (none)");
                return 0;
            }

            presenter.WriteInfo("Relationships");
            presenter.WriteInfo($"  FromInstance: {BuildEntityInstanceAddress(resolvedEntityName, row.Id)}");
            presenter.WriteTable(
                new[] { "Relationship", "ToEntity", "ToInstance" },
                relationshipRows
                    .Select(item => (IReadOnlyList<string>)new[]
                    {
                        item.Relationship,
                        item.ToEntity,
                        item.ToInstance,
                    })
                    .ToList());
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}


