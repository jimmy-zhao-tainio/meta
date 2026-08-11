using Meta.Core.Connections;
using Meta.Operations;
using MetaCli.Core;

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
                    await CurrentWorkspaces.CreateAsync("output", importedFromSql).ConfigureAwait(false);
                    presenter.WriteOk(
                        "imported sql",
                        ("Workspace", OutputLocation()));

                    return 0;
                case "csv":
                    var entityName = RequiredValue("entity").Trim();
                    var outputLocation = MetaCliWorkspace.OptionalOutputLocation(Invocation);
                    var csvFile = RequiredValue("csvFile");
                    if (outputLocation is not null)
                    {
                        var importedFromCsv = await services.ImportService
                            .ImportCsvAsync(csvFile, entityName)
                            .ConfigureAwait(false);
                        var importedEntity = importedFromCsv.Model.Entities.Single();
                        var importedRows = importedFromCsv.Instance.RecordsByEntity[importedEntity.Name];

                        var csvDiagnostics = WorkspaceValidator.Validate(
                            importedFromCsv.Model,
                            importedFromCsv.Instance);
                        if (csvDiagnostics.HasErrors || (globalStrict && csvDiagnostics.WarningCount > 0))
                        {
                            return PrintOperationValidationFailure("import", Array.Empty<Operation>(), csvDiagnostics);
                        }

                        await CurrentWorkspaces.CreateAsync("output", importedFromCsv).ConfigureAwait(false);
                        presenter.WriteOk(
                            "imported csv",
                            ("Workspace", outputLocation),
                            ("Entity", importedEntity.Name),
                            ("Rows", importedRows.Count.ToString()));

                        return 0;
                    }

                    var importedForMerge = await services.ImportService
                        .ImportCsvAsync(csvFile, entityName)
                        .ConfigureAwait(false);
                    var workspaceForCsv = await WorkspaceComposition.MaterializeAsync(CurrentWorkspace)
                        .ConfigureAwait(false);
                    var csvImportPlan = services.ImportService.PlanCsvImport(
                        workspaceForCsv,
                        importedForMerge);
                    return await ExecuteOperationsAsync(
                            csvImportPlan.Operations,
                            commandName: "import csv",
                            successMessage: "imported csv",
                            successDetails: new[]
                            {
                                ("Workspace", WorkspacePath()),
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

    (bool Ok, string ConnectionEnvironmentVariableName, string Schema, string ErrorMessage)
        ReadImportSqlOptions(string[] commandArgs, int startIndex)
    {
        return (true, RequiredValue("connection-env"), RequiredValue("schema"), string.Empty);
    }

    private string OutputLocation() =>
        MetaCliWorkspace.OutputLocation(Invocation, "output-xml", "output-csharp", "output-sql");

}

