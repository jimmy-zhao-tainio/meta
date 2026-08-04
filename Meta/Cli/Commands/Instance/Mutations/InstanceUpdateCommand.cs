internal sealed partial class CliRuntime
{
    async Task<int> InstanceUpdateAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var id = RequiredValue("Id");
        var options = ReadMutatingEntityOptions(commandArgs, startIndex: 4);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        if (ContainsIdSetAssignment(options.SetValues))
        {
            return PrintArgumentError("Error: do not use --set Id. Instance id must be positional <Id>.");
        }

        if (options.SetValues.Count == 0)
        {
            return PrintArgumentError("Error: instance update requires at least one --set Field=Value.");
        }

        try
        {
            var resolvedEntityName = await ResolveEntityNameAsync(CurrentWorkspace, entityName)
                .ConfigureAwait(false);
            var properties = new HashSet<string>(MetaName.Comparer);
            await foreach (var property in CurrentWorkspace.ReadPropertiesAsync(resolvedEntityName))
            {
                properties.Add(property.Name);
            }

            var relationshipAliases = new Dictionary<string, string>(MetaName.Comparer);
            await foreach (var relationship in CurrentWorkspace.ReadRelationshipsAsync(resolvedEntityName))
            {
                relationshipAliases[relationship.GetColumnName()] = relationship.GetColumnName();
                relationshipAliases[relationship.GetRoleOrDefault()] = relationship.GetColumnName();
            }

            var operations = new List<Operation>(options.SetValues.Count);
            foreach (var pair in options.SetValues)
            {
                if (properties.Contains(pair.Key))
                {
                    operations.Add(new Operation.SetProperty(
                        resolvedEntityName,
                        id,
                        pair.Key,
                        pair.Value));
                }
                else if (relationshipAliases.TryGetValue(pair.Key, out var relationshipName))
                {
                    var targetId = NormalizeRelationshipInputValue(pair.Value);
                    operations.Add(string.IsNullOrWhiteSpace(targetId)
                        ? new Operation.ClearRelationship(resolvedEntityName, id, relationshipName)
                        : new Operation.SetRelationship(resolvedEntityName, id, relationshipName, targetId));
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Field '{pair.Key}' is not a property or relationship on entity '{resolvedEntityName}'.");
                }
            }

            return await ExecuteOperationsAsync(
                    operations,
                    commandName: "instance.update",
                    successMessage: $"updated {BuildEntityInstanceAddress(resolvedEntityName, id)}")
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}


