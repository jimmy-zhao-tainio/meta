internal sealed partial class CliRuntime
{
    async Task<int> ModelAddRelationshipAsync(string[] commandArgs)
    {
        var fromEntity = RequiredValue("FromEntity");
        var toEntity = RequiredValue("ToEntity");
        var options = ReadModelAddRelationshipOptions(commandArgs, startIndex: 4);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        var operation = new WorkspaceOp
        {
            Type = WorkspaceOpTypes.AddRelationship,
            EntityName = fromEntity,
            RelatedEntity = toEntity,
            RelatedRole = options.Role,
            RelatedDefaultId = options.DefaultId,
        };

        var relationshipColumnName = (string.IsNullOrWhiteSpace(options.Role) ? toEntity : options.Role) + "Id";
        var successDetails = new List<(string Key, string Value)>
        {
            ("From", fromEntity),
            ("To", toEntity),
            ("Name", relationshipColumnName),
        };
        if (!string.IsNullOrWhiteSpace(options.DefaultId))
        {
            successDetails.Add(("DefaultId", options.DefaultId));
        }

        return await ExecuteOperationAsync(
                options.WorkspacePath,
                operation,
                "model add-relationship",
                "relationship added",
                successDetails.ToArray())
            .ConfigureAwait(false);
    }
}
