internal sealed partial class CliRuntime
{
    async Task<int> ModelAddPropertyAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var propertyName = RequiredValue("Property");
        var required = !IsPresent("required") || bool.Parse(RequiredValue("required"));
        string? defaultValue = IsPresent("default-value") ? RequiredValue("default-value") : null;

        var requiredText = required ? "required" : "optional";
        var successDetails = new List<(string Key, string Value)>
        {
            ("Entity", entityName),
            ("Property", $"{propertyName} ({requiredText})"),
        };
        if (defaultValue != null)
        {
            successDetails.Add(("DefaultValue", defaultValue.Length == 0 ? "(empty)" : defaultValue));
        }

        return await ExecuteOperationAsync(
                new Operation.AddProperty(
                    entityName,
                    propertyName,
                    required,
                    defaultValue),
                "model add-property",
                "property added",
                successDetails.ToArray())
            .ConfigureAwait(false);
    }
}

