using Meta.Core.Connections;

internal sealed partial class CliRuntime
{
    async Task<int> ImportAsync(string[] commandArgs)
    {
        var mode = CommandToken().Trim().ToLowerInvariant();
        try
        {
            switch (mode)
            {
                case "sql":
                    var sqlOptions = ReadImportSqlOptions(commandArgs, startIndex: 2);
                    if (!sqlOptions.Ok)
                    {
                        return PrintArgumentError(sqlOptions.ErrorMessage);
                    }

                    var workspacePath = sqlOptions.NewWorkspacePath;
                    var targetValidation = ValidateNewWorkspaceTarget(workspacePath);
                    if (targetValidation != 0)
                    {
                        return targetValidation;
                    }

                    var connectionString = ConnectionEnvironmentVariableResolver.ResolveRequired(
                        sqlOptions.ConnectionEnvironmentVariableName);
                    var importedFromSql = await services.ImportService
                        .ImportSqlAsync(connectionString, sqlOptions.Schema)
                        .ConfigureAwait(false);
                    var sqlDiagnostics = services.ValidationService.Validate(importedFromSql);
                    importedFromSql.Diagnostics = sqlDiagnostics;
                    if (sqlDiagnostics.HasErrors || (globalStrict && sqlDiagnostics.WarningCount > 0))
                    {
                        return PrintOperationValidationFailure("import", MetaOperationPlan.Empty, sqlDiagnostics);
                    }
                    await services.ExportService.ExportXmlAsync(importedFromSql, workspacePath).ConfigureAwait(false);
                    presenter.WriteOk(
                        "imported sql",
                        ("Workspace", Path.GetFullPath(workspacePath)));

                    return 0;
                case "csv":
                    var csvOptions = ReadImportCsvOptions(commandArgs, startIndex: 3);
                    if (!csvOptions.Ok)
                    {
                        return PrintArgumentError(csvOptions.ErrorMessage);
                    }

                    var csvFile = RequiredValue("csvFile");
                    if (csvOptions.UseNewWorkspace)
                    {
                        var importedFromCsv = await services.ImportService
                            .ImportCsvAsync(csvFile, csvOptions.EntityName)
                            .ConfigureAwait(false);
                        var importedEntity = importedFromCsv.Model.Entities.Single();
                        var importedRows = importedFromCsv.Instance.RecordsByEntity[importedEntity.Name];

                        workspacePath = csvOptions.NewWorkspacePath;
                        targetValidation = ValidateNewWorkspaceTarget(workspacePath);
                        if (targetValidation != 0)
                        {
                            return targetValidation;
                        }

                        var csvDiagnostics = services.ValidationService.Validate(importedFromCsv);
                        importedFromCsv.Diagnostics = csvDiagnostics;
                        if (csvDiagnostics.HasErrors || (globalStrict && csvDiagnostics.WarningCount > 0))
                        {
                            return PrintOperationValidationFailure("import", MetaOperationPlan.Empty, csvDiagnostics);
                        }

                        await services.ExportService.ExportXmlAsync(importedFromCsv, workspacePath).ConfigureAwait(false);
                        presenter.WriteOk(
                            "imported csv",
                            ("Workspace", Path.GetFullPath(workspacePath)),
                            ("Entity", importedEntity.Name),
                            ("Rows", importedRows.Count.ToString()));

                        return 0;
                    }

                    workspacePath = csvOptions.WorkspacePath;
                    var workspaceForCsv = await LoadWorkspaceForCommandAsync(workspacePath).ConfigureAwait(false);
                    PrintContractCompatibilityWarning(workspaceForCsv.WorkspaceConfig);
                    var importResult = await services.ImportService
                        .ImportCsvIntoWorkspaceAsync(workspaceForCsv, csvFile, csvOptions.EntityName)
                        .ConfigureAwait(false);
                    var workspaceCsvDiagnostics = services.ValidationService.Validate(workspaceForCsv);
                    workspaceForCsv.Diagnostics = workspaceCsvDiagnostics;
                    if (workspaceCsvDiagnostics.HasErrors || (globalStrict && workspaceCsvDiagnostics.WarningCount > 0))
                    {
                        return PrintOperationValidationFailure("import", MetaOperationPlan.Empty, workspaceCsvDiagnostics);
                    }

                    await services.WorkspaceService.SaveAsync(workspaceForCsv).ConfigureAwait(false);
                    presenter.WriteOk(
                        "imported csv",
                        ("Workspace", Path.GetFullPath(workspaceForCsv.WorkspaceRootPath)),
                        ("Entity", importResult.EntityName),
                        ("Rows", importResult.RowsImported.ToString()));

                    return 0;
                default:
                    return PrintUsageError("Usage: import <sql|csv> ...");
            }
        }
        catch (ConnectionEnvironmentVariableException exception)
        {
            return PrintArgumentError(exception.Message);
        }
        catch (Exception exception)
        {
            return PrintDataError("E_IMPORT", exception.Message);
        }
    }

    (bool Ok, string ConnectionEnvironmentVariableName, string Schema, string NewWorkspacePath, string ErrorMessage)
        ReadImportSqlOptions(string[] commandArgs, int startIndex)
    {
        return (true, RequiredValue("connection-env"), RequiredValue("schema"), RequiredValue("new-workspace"), string.Empty);
    }
}

