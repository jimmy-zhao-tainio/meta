internal sealed partial class CliRuntime
{
    async Task<int> ExportAsync(string[] commandArgs)
    {
        var mode = CommandToken().Trim().ToLowerInvariant();
        switch (mode)
        {
            case "csv":
                var options = ReadExportCsvOptions(commandArgs, startIndex: 3);
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

                    var entityName = RequiredValue("Entity");
                    await services.ExportService.ExportCsvAsync(workspace, entityName, options.OutputPath).ConfigureAwait(false);
                    presenter.WriteOk(
                        "exported csv",
                        ("Workspace", Path.GetFullPath(workspace.WorkspaceRootPath)),
                        ("Entity", entityName),
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
        ReadExportCsvOptions(string[] commandArgs, int startIndex)
    {
        return (true, RequiredValue("out"), WorkspacePath(), string.Empty);
    }
}
