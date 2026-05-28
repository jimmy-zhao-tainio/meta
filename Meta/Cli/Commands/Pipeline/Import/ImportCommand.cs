using Meta.Core.Connections;

internal sealed partial class CliRuntime
{
    async Task<int> ImportAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 2)
        {
            return PrintUsageError("Usage: import <sql|csv> ...");
        }
    
        var mode = commandArgs[1].Trim().ToLowerInvariant();
        try
        {
            switch (mode)
            {
                case "sql":
                    if (commandArgs.Length < 5)
                    {
                        return PrintUsageError("Usage: import sql --connection-env <name> <schema> --new-workspace <path>");
                    }

                    var sqlOptions = ParseImportSqlOptions(commandArgs, startIndex: 2);
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
                    ApplyImplicitNormalization(importedFromSql);
                    var sqlDiagnostics = services.ValidationService.Validate(importedFromSql);
                    importedFromSql.Diagnostics = sqlDiagnostics;
                    if (sqlDiagnostics.HasErrors || (globalStrict && sqlDiagnostics.WarningCount > 0))
                    {
                        return PrintOperationValidationFailure("import", Array.Empty<WorkspaceOp>(), sqlDiagnostics);
                    }
                    await services.ExportService.ExportXmlAsync(importedFromSql, workspacePath).ConfigureAwait(false);
                    presenter.WriteOk(
                        "imported sql",
                        ("Workspace", Path.GetFullPath(workspacePath)));
    
                    return 0;
                case "csv":
                    if (commandArgs.Length < 3)
                    {
                        return PrintUsageError(
                            "Usage: import csv <csvFile> --entity <EntityName> [--workspace <path> | --new-workspace <path>]");
                    }

                    var csvOptions = ParseImportCsvOptions(commandArgs, startIndex: 3);
                    if (!csvOptions.Ok)
                    {
                        return PrintArgumentError(csvOptions.ErrorMessage);
                    }

                    if (csvOptions.UseNewWorkspace)
                    {
                        var importedFromCsv = await services.ImportService
                            .ImportCsvAsync(commandArgs[2], csvOptions.EntityName)
                            .ConfigureAwait(false);
                        var importedEntity = importedFromCsv.Model.Entities.Single();
                        var importedRows = importedFromCsv.Instance.RecordsByEntity[importedEntity.Name];

                        workspacePath = csvOptions.NewWorkspacePath;
                        targetValidation = ValidateNewWorkspaceTarget(workspacePath);
                        if (targetValidation != 0)
                        {
                            return targetValidation;
                        }

                        ApplyImplicitNormalization(importedFromCsv);
                        var csvDiagnostics = services.ValidationService.Validate(importedFromCsv);
                        importedFromCsv.Diagnostics = csvDiagnostics;
                        if (csvDiagnostics.HasErrors || (globalStrict && csvDiagnostics.WarningCount > 0))
                        {
                            return PrintOperationValidationFailure("import", Array.Empty<WorkspaceOp>(), csvDiagnostics);
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
                    var importedForMerge = await services.ImportService
                        .ImportCsvAsync(commandArgs[2], csvOptions.EntityName)
                        .ConfigureAwait(false);
                    var importedEntityForMerge = importedForMerge.Model.Entities.Single();
                    var importedRowsForMerge = importedForMerge.Instance.RecordsByEntity[importedEntityForMerge.Name];
                    var existingEntity = workspaceForCsv.Model.FindEntity(importedEntityForMerge.Name);

                    if (existingEntity == null)
                    {
                        workspaceForCsv.Model.Entities.Add(importedEntityForMerge);
                        workspaceForCsv.Instance.RecordsByEntity[importedEntityForMerge.Name] = importedRowsForMerge;
                    }
                    else
                    {
                        MergeCsvImportIntoExistingEntity(existingEntity, workspaceForCsv, importedEntityForMerge, importedRowsForMerge);
                    }
                    ApplyImplicitNormalization(workspaceForCsv);

                    var workspaceCsvDiagnostics = services.ValidationService.Validate(workspaceForCsv);
                    workspaceForCsv.Diagnostics = workspaceCsvDiagnostics;
                    if (workspaceCsvDiagnostics.HasErrors || (globalStrict && workspaceCsvDiagnostics.WarningCount > 0))
                    {
                        return PrintOperationValidationFailure("import", Array.Empty<WorkspaceOp>(), workspaceCsvDiagnostics);
                    }

                    await services.WorkspaceService.SaveAsync(workspaceForCsv).ConfigureAwait(false);
                    presenter.WriteOk(
                        "imported csv",
                        ("Workspace", Path.GetFullPath(workspaceForCsv.WorkspaceRootPath)),
                        ("Entity", importedEntityForMerge.Name),
                        ("Rows", importedRowsForMerge.Count.ToString()));

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
        ParseImportSqlOptions(string[] commandArgs, int startIndex)
    {
        var connectionEnvironmentVariableName = string.Empty;
        var schema = string.Empty;
        var newWorkspacePath = string.Empty;

        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--connection-env", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, connectionEnvironmentVariableName, schema, newWorkspacePath, "Error: --connection-env requires a name.");
                }

                if (!string.IsNullOrWhiteSpace(connectionEnvironmentVariableName))
                {
                    return (false, connectionEnvironmentVariableName, schema, newWorkspacePath, "Error: --connection-env can only be provided once.");
                }

                connectionEnvironmentVariableName = commandArgs[++i];
                continue;
            }

            if (string.Equals(arg, "--new-workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, connectionEnvironmentVariableName, schema, newWorkspacePath, "Error: --new-workspace requires a path.");
                }

                if (!string.IsNullOrWhiteSpace(newWorkspacePath))
                {
                    return (false, connectionEnvironmentVariableName, schema, newWorkspacePath, "Error: --new-workspace can only be provided once.");
                }

                newWorkspacePath = commandArgs[++i];
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                return (false, connectionEnvironmentVariableName, schema, newWorkspacePath, $"Error: unknown option '{arg}'.");
            }

            if (!string.IsNullOrWhiteSpace(schema))
            {
                return (false, connectionEnvironmentVariableName, schema, newWorkspacePath, $"Error: unexpected argument '{arg}'.");
            }

            schema = arg;
        }

        if (string.IsNullOrWhiteSpace(connectionEnvironmentVariableName))
        {
            return (false, connectionEnvironmentVariableName, schema, newWorkspacePath, "Error: import sql requires --connection-env <name>.");
        }

        if (string.IsNullOrWhiteSpace(schema))
        {
            return (false, connectionEnvironmentVariableName, schema, newWorkspacePath, "Error: import sql requires <schema>.");
        }

        if (string.IsNullOrWhiteSpace(newWorkspacePath))
        {
            return (false, connectionEnvironmentVariableName, schema, newWorkspacePath, "Error: import sql requires --new-workspace <path>.");
        }

        return (true, connectionEnvironmentVariableName, schema, newWorkspacePath, string.Empty);
    }

    private static void MergeCsvImportIntoExistingEntity(
        GenericEntity existingEntity,
        Workspace workspace,
        GenericEntity importedEntity,
        IReadOnlyList<GenericRecord> importedRows)
    {
        var existingPropertyNames = existingEntity.Properties
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingRelationshipNames = existingEntity.Relationships
            .Select(item => item.GetColumnName())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var importedProperty in importedEntity.Properties)
        {
            var name = importedProperty.Name;
            if (existingPropertyNames.Contains(name) || existingRelationshipNames.Contains(name))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"CSV column '{name}' does not match existing property or relationship on entity '{existingEntity.Name}'.");
        }

        var existingRows = workspace.Instance.GetOrCreateEntityRecords(existingEntity.Name);
        var rowsById = existingRows.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        ValidateCsvImportPreflight(existingEntity, importedEntity, importedRows, rowsById);

        foreach (var importedRow in importedRows
                     .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!rowsById.TryGetValue(importedRow.Id, out var targetRow))
            {
                targetRow = new GenericRecord
                {
                    Id = importedRow.Id,
                };
                existingRows.Add(targetRow);
                rowsById[targetRow.Id] = targetRow;
            }

            foreach (var importedProperty in importedEntity.Properties)
            {
                var name = importedProperty.Name;
                var hasValue = importedRow.Values.TryGetValue(name, out var value);

                if (existingRelationshipNames.Contains(name))
                {
                    if (!hasValue || string.IsNullOrWhiteSpace(value))
                    {
                        targetRow.RelationshipIds.Remove(name);
                    }
                    else
                    {
                        targetRow.RelationshipIds[name] = value;
                    }

                    continue;
                }

                if (!hasValue || string.IsNullOrWhiteSpace(value))
                {
                    targetRow.Values.Remove(name);
                }
                else
                {
                    targetRow.Values[name] = value;
                }
            }
        }
    }

    private static void ValidateCsvImportPreflight(
        GenericEntity existingEntity,
        GenericEntity importedEntity,
        IReadOnlyList<GenericRecord> importedRows,
        IReadOnlyDictionary<string, GenericRecord> rowsById)
    {
        var existingPropertiesByName = existingEntity.Properties
            .ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var existingRelationshipNames = existingEntity.Relationships
            .Select(item => item.GetColumnName())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var importedColumnNames = importedEntity.Properties
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var importedRow in importedRows
                     .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            var isExistingRow = rowsById.ContainsKey(importedRow.Id);

            foreach (var importedProperty in importedEntity.Properties)
            {
                var name = importedProperty.Name;
                var hasValue = importedRow.Values.TryGetValue(name, out var value);
                var isBlank = !hasValue || string.IsNullOrWhiteSpace(value);

                if (existingRelationshipNames.Contains(name))
                {
                    if (isBlank)
                    {
                        throw new InvalidOperationException(
                            $"CSV row '{importedRow.Id}' leaves required relationship '{name}' blank on entity '{existingEntity.Name}'.");
                    }

                    continue;
                }

                if (existingPropertiesByName.TryGetValue(name, out var existingProperty) &&
                    !existingProperty.IsNullable &&
                    isBlank)
                {
                    throw new InvalidOperationException(
                        $"CSV row '{importedRow.Id}' leaves required property '{name}' blank on entity '{existingEntity.Name}'.");
                }
            }

            if (isExistingRow)
            {
                continue;
            }

            foreach (var requiredProperty in existingEntity.Properties
                         .Where(item => !item.IsNullable)
                         .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!importedColumnNames.Contains(requiredProperty.Name))
                {
                    throw new InvalidOperationException(
                        $"CSV row '{importedRow.Id}' cannot create new '{existingEntity.Name}' because required property '{requiredProperty.Name}' is missing from the import columns.");
                }

                if (!importedRow.Values.TryGetValue(requiredProperty.Name, out var propertyValue) ||
                    string.IsNullOrWhiteSpace(propertyValue))
                {
                    throw new InvalidOperationException(
                        $"CSV row '{importedRow.Id}' leaves required property '{requiredProperty.Name}' blank on entity '{existingEntity.Name}'.");
                }
            }

            foreach (var requiredRelationshipName in existingEntity.Relationships
                         .Select(item => item.GetColumnName())
                         .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                if (!importedColumnNames.Contains(requiredRelationshipName))
                {
                    throw new InvalidOperationException(
                        $"CSV row '{importedRow.Id}' cannot create new '{existingEntity.Name}' because required relationship '{requiredRelationshipName}' is missing from the import columns.");
                }

                if (!importedRow.Values.TryGetValue(requiredRelationshipName, out var relationshipValue) ||
                    string.IsNullOrWhiteSpace(relationshipValue))
                {
                    throw new InvalidOperationException(
                        $"CSV row '{importedRow.Id}' leaves required relationship '{requiredRelationshipName}' blank on entity '{existingEntity.Name}'.");
                }
            }
        }
    }
}

