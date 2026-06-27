internal sealed partial class CliRuntime
{
    async Task<int> ModelAddPropertyAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var propertyName = RequiredValue("Property");
        var required = !IsPresent("required") || bool.Parse(RequiredValue("required"));
        string? defaultValue = IsPresent("default-value") ? RequiredValue("default-value") : null;
        var workspacePath = WorkspacePath();

        var operation = new WorkspaceOp
        {
            Type = WorkspaceOpTypes.AddProperty,
            EntityName = entityName,
            Property = new GenericProperty
            {
                Name = propertyName,
                DataType = "string",
                IsNullable = !required,
            },
            PropertyDefaultValue = defaultValue,
        };

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
                workspacePath,
                operation,
                "model add-property",
                "property added",
                successDetails.ToArray())
            .ConfigureAwait(false);
    }
}

