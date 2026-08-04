internal sealed partial class CliRuntime
{
    async Task<int> ModelDropRelationshipAsync(string[] commandArgs)
    {
        var fromEntityName = RequiredValue("FromEntity");
        var toEntityName = RequiredValue("ToEntity");
        try
        {
            var resolvedFromEntityName = await ResolveEntityNameAsync(CurrentWorkspace, fromEntityName)
                .ConfigureAwait(false);
            var matches = new List<RelationshipDefinition>();
            await foreach (var candidate in CurrentWorkspace.ReadRelationshipsAsync(resolvedFromEntityName))
            {
                if (MetaName.Comparer.Equals(candidate.TargetEntityName, toEntityName) ||
                    MetaName.Comparer.Equals(candidate.GetRoleOrDefault(), toEntityName) ||
                    MetaName.Comparer.Equals(candidate.GetColumnName(), toEntityName))
                {
                    matches.Add(candidate);
                }
            }

            if (matches.Count > 1)
            {
                return PrintDataError(
                    "E_RELATIONSHIP_AMBIGUOUS",
                    $"Relationship selector '{toEntityName}' is ambiguous on entity '{fromEntityName}'. Use relationship role or column.");
            }

            if (matches.Count == 0)
            {
                return PrintDataError(
                    "E_RELATIONSHIP_NOT_FOUND",
                    $"Relationship '{fromEntityName}->{toEntityName}' does not exist.");
            }

            var relationship = matches.Single();
            var relationshipName = relationship.GetColumnName();
            var targetEntityName = relationship.TargetEntityName;

            return await ExecuteOperationAsync(
                    new Operation.RemoveRelationship(
                        resolvedFromEntityName,
                        relationshipName),
                    "model drop-relationship",
                    "relationship removed",
                    ("From", resolvedFromEntityName),
                    ("To", targetEntityName),
                    ("Name", relationshipName))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
}


