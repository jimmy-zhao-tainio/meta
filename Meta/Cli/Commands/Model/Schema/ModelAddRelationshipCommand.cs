internal sealed partial class CliRuntime
{
    async Task<int> ModelAddRelationshipAsync(string[] commandArgs)
    {
        var fromEntity = RequiredValue("FromEntity");
        var toEntity = RequiredValue("ToEntity");
        var role = OptionalValue("role");
        var defaultId = OptionalValue("default-id");
        var required = !IsPresent("required") || bool.Parse(RequiredValue("required"));

        var relationshipColumnName = (string.IsNullOrWhiteSpace(role) ? toEntity : role) + "Id";
        var requiredText = required ? "required" : "optional";
        var successDetails = new List<(string Key, string Value)>
        {
            ("From", fromEntity),
            ("To", toEntity),
            ("Name", $"{relationshipColumnName} ({requiredText})"),
        };
        if (!string.IsNullOrWhiteSpace(defaultId))
        {
            successDetails.Add(("DefaultId", defaultId));
        }

        return await ExecuteOperationAsync(
                new Operation.AddRelationship(
                    fromEntity,
                    toEntity,
                    string.IsNullOrWhiteSpace(role)
                        ? null
                        : role,
                    required,
                    string.IsNullOrWhiteSpace(defaultId)
                        ? null
                        : defaultId),
                "model add-relationship",
                "relationship added",
                successDetails.ToArray())
            .ConfigureAwait(false);
    }
}
