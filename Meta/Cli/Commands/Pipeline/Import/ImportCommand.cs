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
                    var sqlDiagnostics = WorkspaceValidator.Validate(
                        importedFromSql.Model,
                        importedFromSql.Instance);
                    if (sqlDiagnostics.HasErrors || (globalStrict && sqlDiagnostics.WarningCount > 0))
                    {
                        return PrintOperationValidationFailure("import", Array.Empty<Operation>(), sqlDiagnostics);
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

                        var csvDiagnostics = WorkspaceValidator.Validate(
                            importedFromCsv.Model,
                            importedFromCsv.Instance);
                        if (csvDiagnostics.HasErrors || (globalStrict && csvDiagnostics.WarningCount > 0))
                        {
                            return PrintOperationValidationFailure("import", Array.Empty<Operation>(), csvDiagnostics);
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
                    var workspaceForCsv = await OpenXmlWorkspaceForCommandAsync(workspacePath).ConfigureAwait(false);
                    PrintContractCompatibilityWarning(workspaceForCsv.ContractVersion);
                    var importedForMerge = await services.ImportService
                        .ImportCsvAsync(csvFile, csvOptions.EntityName)
                        .ConfigureAwait(false);
                    var csvImportPlan = services.ImportService.PlanCsvImport(
                        workspaceForCsv.State,
                        importedForMerge);
                    return await ExecuteOperationsAgainstOpenedXmlWorkspaceAsync(
                            workspaceForCsv,
                            csvImportPlan.Operations,
                            commandName: "import csv",
                            successMessage: "imported csv",
                            successDetails: new[]
                            {
                                ("Workspace", workspaceForCsv.RootPath),
                                ("Entity", csvImportPlan.EntityName),
                                ("Rows", csvImportPlan.RowCount.ToString()),
                            })
                        .ConfigureAwait(false);
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

