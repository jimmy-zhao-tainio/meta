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

    private static string NormalizeIdentity(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

}





