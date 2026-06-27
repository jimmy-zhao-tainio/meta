internal sealed partial class CliRuntime
{
    async Task<int> ModelSetPropertyRequiredAsync(string[] commandArgs)
    {
        var entityName = RequiredValue("Entity");
        var propertyName = RequiredValue("Property");
        var required = bool.Parse(RequiredValue("required"));
        string? defaultValue = IsPresent("default-value") ? RequiredValue("default-value") : null;
        var workspacePath = WorkspacePath();

        if (!required && defaultValue != null)
        {
            return PrintArgumentError("Error: --default-value is only valid with --required true.");
        }

        var operation = new WorkspaceOp
        {
            Type = WorkspaceOpTypes.ChangeNullability,
            EntityName = entityName,
            PropertyName = propertyName,
            IsNullable = !required,
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
                "model set-property-required",
                "property requiredness updated",
                successDetails.ToArray())
            .ConfigureAwait(false);
    }
}
