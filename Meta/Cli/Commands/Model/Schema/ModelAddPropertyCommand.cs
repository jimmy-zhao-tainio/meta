internal sealed partial class CliRuntime
{
    async Task<int> ModelAddPropertyAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 4)
        {
            return PrintUsageError(
                "Usage: model add-property <Entity> <Property> [--required true|false] [--default-value <Value>] [--workspace <path>]");
        }
    
        var entityName = commandArgs[2];
        var propertyName = commandArgs[3];
        var required = true;
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
    
                if (!bool.TryParse(commandArgs[++i], out required))
                {
                    return PrintArgumentError("Error: --required must be true or false.");
                }
    
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

