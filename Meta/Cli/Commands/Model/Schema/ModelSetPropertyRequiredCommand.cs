internal sealed partial class CliRuntime
{
    async Task<int> ModelSetPropertyRequiredAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 6)
        {
            return PrintUsageError(
                "Usage: model set-property-required <Entity> <Property> --required true|false [--default-value <Value>] [--workspace <path>]");
        }

        var entityName = commandArgs[2];
        var propertyName = commandArgs[3];
        bool? required = null;
        string? defaultValue = null;
        var workspacePath = DefaultWorkspacePath();

        for (var i = 4; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--required", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return PrintArgumentError("Error: --required requires true or false.");
                }

                if (!bool.TryParse(commandArgs[++i], out var parsed))
                {
                    return PrintArgumentError("Error: --required must be true or false.");
                }

                required = parsed;
                continue;
            }

            if (string.Equals(arg, "--default-value", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return PrintArgumentError("Error: --default-value requires a value.");
                }

                defaultValue = commandArgs[++i];
                continue;
            }

            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return PrintArgumentError("Error: --workspace requires a path.");
                }

                workspacePath = commandArgs[++i];
                continue;
            }

            return PrintArgumentError($"Error: unknown option '{arg}'.");
        }

        if (required == null)
        {
            return PrintArgumentError("Error: --required true|false is required.");
        }

        if (!required.Value && defaultValue != null)
        {
            return PrintArgumentError("Error: --default-value is only valid with --required true.");
        }

        var operation = new WorkspaceOp
        {
            Type = WorkspaceOpTypes.ChangeNullability,
            EntityName = entityName,
            PropertyName = propertyName,
            IsNullable = !required.Value,
            PropertyDefaultValue = defaultValue,
        };

        var requiredText = required.Value ? "required" : "optional";
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
