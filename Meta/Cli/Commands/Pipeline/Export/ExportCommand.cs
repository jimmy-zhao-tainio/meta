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
                    var workspace = await OpenXmlWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
                    PrintContractCompatibilityWarning(workspace.ContractVersion);
                    var diagnostics = WorkspaceValidator.Validate(
                        workspace.Model,
                        workspace.Instance);
                    if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
                    {
                        return PrintOperationValidationFailure("export", Array.Empty<Operation>(), diagnostics);
                    }

                    var entityName = RequiredValue("Entity");
                    await services.ExportService.ExportCsvAsync(
                            new InMemoryWorkspaceSource(workspace.State),
                            entityName,
                            options.OutputPath)
                        .ConfigureAwait(false);
                    presenter.WriteOk(
                        "exported csv",
                        ("Workspace", workspace.RootPath),
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
