using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Meta.Core.Domain;
using Meta.Core.Services;
using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace Meta.Adapters;

public sealed class ImportService : IImportService
{
    private readonly IWorkspaceService _workspaceService;

    public ImportService(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
    }

    public async Task<Workspace> ImportSqlAsync(string connectionString, string schema, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        var effectiveSchema = SqlServerMetaModelReader.NormalizeSchema(schema);

        var workspace = new Workspace
        {
            WorkspaceRootPath = Path.Combine(Path.GetTempPath(), "metadata-studio-import", Guid.NewGuid().ToString("N")),
            MetadataRootPath = string.Empty,
            WorkspaceConfig = MetaWorkspaceConfig.CreateDefault(),
            Model = new GenericModel(),
            Instance = new GenericInstance(),
            IsDirty = true,
        };

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        workspace.Model = await SqlServerMetaModelReader.LoadAsync(
                connection,
                effectiveSchema,
                cancellationToken)
            .ConfigureAwait(false);
        workspace.Instance.ModelName = workspace.Model.Name;

        foreach (var entity in workspace.Model.Entities)
        {
            var rows = await SqlServerImportReader.LoadRowsAsync(connection, effectiveSchema, entity, cancellationToken).ConfigureAwait(false);
            workspace.Instance.RecordsByEntity[entity.Name] = rows;
        }

        return workspace;
    }

    public async Task<Workspace> ImportCsvAsync(
        string csvPath,
        string entityName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(csvPath))
        {
            throw new ArgumentException("CSV file path is required.", nameof(csvPath));
        }

        if (string.IsNullOrWhiteSpace(entityName))
        {
            throw new ArgumentException("Entity name is required.", nameof(entityName));
        }

        var fullCsvPath = Path.GetFullPath(csvPath);
        if (!File.Exists(fullCsvPath))
        {
            throw new FileNotFoundException($"CSV file '{fullCsvPath}' was not found.", fullCsvPath);
        }

        var csvText = await File.ReadAllTextAsync(fullCsvPath, cancellationToken).ConfigureAwait(false);
        var parsedRows = CsvImportSupport.ParseRows(csvText);
        if (parsedRows.Count == 0)
        {
            throw new InvalidOperationException("CSV file must include a header row.");
        }

        var header = parsedRows[0];
        if (header.Count == 0)
        {
            throw new InvalidOperationException("CSV header row is empty.");
        }

        const string idColumn = "Id";
        var idColumnIndex = CsvImportSupport.ResolveIdColumnIndex(header, idColumn);

        var entityIdentifier = CsvImportSupport.NormalizeIdentifier(entityName, fallback: "Entity");
        var modelName = CsvImportSupport.NormalizeIdentifier(entityIdentifier + "Model", fallback: "ImportedModel");
        var columnPlans = CsvImportSupport.BuildColumnPlans(header, idColumnIndex);

        var dataRows = new List<IReadOnlyList<string>>();
        for (var index = 1; index < parsedRows.Count; index++)
        {
            var row = parsedRows[index];
            if (row.Count > header.Count)
            {
                throw new InvalidOperationException(
                    $"CSV row {index + 1} has {row.Count.ToString(CultureInfo.InvariantCulture)} values but header has {header.Count.ToString(CultureInfo.InvariantCulture)} columns.");
            }

            if (CsvImportSupport.IsRowCompletelyEmpty(row))
            {
                continue;
            }

            dataRows.Add(row);
        }

        var workspace = new Workspace
        {
            WorkspaceRootPath = Path.Combine(Path.GetTempPath(), "metadata-studio-import", Guid.NewGuid().ToString("N")),
            MetadataRootPath = string.Empty,
            WorkspaceConfig = MetaWorkspaceConfig.CreateDefault(),
            Model = new GenericModel
            {
                Name = modelName,
            },
            Instance = new GenericInstance
            {
                ModelName = modelName,
            },
            IsDirty = true,
        };

        var entity = new GenericEntity
        {
            Name = entityIdentifier,
        };
        workspace.Model.Entities.Add(entity);

        foreach (var plan in columnPlans)
        {
            var values = dataRows.Select(row => CsvImportSupport.GetCellValue(row, plan.ColumnIndex)).ToList();
            var hasEmpty = values.Any(value => string.IsNullOrWhiteSpace(value));

            entity.Properties.Add(new GenericProperty
            {
                Name = plan.PropertyName,
                IsNullable = hasEmpty,
            });
        }

        var records = workspace.Instance.GetOrCreateEntityRecords(entity.Name);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var rowIndex = 0; rowIndex < dataRows.Count; rowIndex++)
        {
            var dataRow = dataRows[rowIndex];
            var recordId = NormalizeIdentity(CsvImportSupport.GetCellValue(dataRow, idColumnIndex));
            if (string.IsNullOrWhiteSpace(recordId))
            {
                throw new InvalidOperationException(
                    $"CSV row {rowIndex + 2} is missing required Id value from column '{header[idColumnIndex]}'.");
            }

            if (!ids.Add(recordId))
            {
                throw new InvalidOperationException(
                    $"CSV contains duplicate Id '{recordId}' in column '{header[idColumnIndex]}'.");
            }

            var record = new GenericRecord
            {
                Id = recordId,
            };

            foreach (var plan in columnPlans)
            {
                var cellValue = CsvImportSupport.GetCellValue(dataRow, plan.ColumnIndex);
                if (string.IsNullOrWhiteSpace(cellValue))
                {
                    continue;
                }

                record.Values[plan.PropertyName] = cellValue;
            }

            records.Add(record);
        }

        return workspace;
    }

    public async Task<CsvWorkspaceImportResult> ImportCsvIntoWorkspaceAsync(
        Workspace workspace,
        string csvPath,
        string entityName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var importedWorkspace = await ImportCsvAsync(csvPath, entityName, cancellationToken).ConfigureAwait(false);
        var importedEntity = importedWorkspace.Model.Entities.Single();
        var importedRows = importedWorkspace.Instance.RecordsByEntity[importedEntity.Name];
        var existingEntity = workspace.Model.FindEntity(importedEntity.Name);

        if (existingEntity == null)
        {
            workspace.Model.Entities.Add(importedEntity);
            workspace.Instance.RecordsByEntity[importedEntity.Name] = importedRows;
        }
        else
        {
            MergeCsvImportIntoExistingEntity(existingEntity, workspace, importedEntity, importedRows);
        }

        workspace.IsDirty = true;
        return new CsvWorkspaceImportResult(importedEntity.Name, importedRows.Count);
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
        var existingRelationshipsByName = existingEntity.Relationships
            .ToDictionary(item => item.GetColumnName(), StringComparer.OrdinalIgnoreCase);
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

                if (existingRelationshipsByName.TryGetValue(name, out var existingRelationship))
                {
                    if (!existingRelationship.IsNullable && isBlank)
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
                         .Where(item => !item.IsNullable)
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

    private static string NormalizeIdentity(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}

