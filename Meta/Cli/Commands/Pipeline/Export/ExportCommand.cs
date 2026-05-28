internal sealed partial class CliRuntime
{
    async Task<int> ExportAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 2)
        {
            return PrintUsageError("Usage: export <csv> ...");
        }

        var mode = commandArgs[1].Trim().ToLowerInvariant();
        switch (mode)
        {
            case "csv":
                if (commandArgs.Length < 3)
                {
                    return PrintUsageError("Usage: export csv <Entity> --out <file> [--workspace <path>]");
                }

                var options = ParseExportCsvOptions(commandArgs, startIndex: 3);
                if (!options.Ok)
                {
                    return PrintArgumentError(options.ErrorMessage);
                }

                if (string.IsNullOrWhiteSpace(options.OutputPath))
                {
                    return PrintArgumentError("Error: export csv requires --out <file>.");
                }

                try
                {
                    var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
                    PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
                    var diagnostics = services.ValidationService.Validate(workspace);
                    workspace.Diagnostics = diagnostics;
                    if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
                    {
                        return PrintOperationValidationFailure("export", Array.Empty<Meta.Core.Operations.WorkspaceOp>(), diagnostics);
                    }

                    await services.ExportService.ExportCsvAsync(workspace, commandArgs[2], options.OutputPath).ConfigureAwait(false);
                    presenter.WriteOk(
                        "exported csv",
                        ("Workspace", Path.GetFullPath(workspace.WorkspaceRootPath)),
                        ("Entity", commandArgs[2]),
                        ("Out", Path.GetFullPath(options.OutputPath)));
                    return 0;
                }
                catch (Exception exception)
                {
                    return PrintGenerationError("E_EXPORT", exception.Message);
                }

            default:
                return PrintArgumentError($"Error: unknown export mode '{mode}'.");
        }
    }

    (bool Ok, string OutputPath, string WorkspacePath, string ErrorMessage)
        ParseExportCsvOptions(string[] commandArgs, int startIndex)
    {
        var outputPath = string.Empty;
        var workspacePath = DefaultWorkspacePath();

        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--out", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, outputPath, workspacePath, "Error: --out requires a file path.");
                }

                outputPath = commandArgs[++i];
                continue;
            }

            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, outputPath, workspacePath, "Error: --workspace requires a path.");
                }

                workspacePath = commandArgs[++i];
                continue;
            }

            return (false, outputPath, workspacePath, $"Error: unknown option '{arg}'.");
        }

        return (true, outputPath, workspacePath, string.Empty);
    }
}
