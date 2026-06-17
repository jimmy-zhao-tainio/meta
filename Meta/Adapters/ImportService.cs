using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Meta.Core.Domain;
using Meta.Core.Services;
using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace Meta.Adapters;

public sealed class ImportService : IImportService
{
    private const int MaxIdentifierLength = 128;
    private static readonly Regex IdentifierPattern = new(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

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

        var effectiveSchema = string.IsNullOrWhiteSpace(schema) ? "dbo" : schema.Trim();
        ValidateIdentifier(effectiveSchema, "Schema name");

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

        workspace.Model.Name = connection.Database ?? "MetadataModel";
        ValidateIdentifier(workspace.Model.Name, "Database name");
        workspace.Instance.ModelName = workspace.Model.Name;

        var entityLookup = new Dictionary<string, GenericEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var tableName in await SqlServerImportReader.LoadTableNamesAsync(connection, effectiveSchema, cancellationToken).ConfigureAwait(false))
        {
            ValidateIdentifier(tableName, "Table name");
            if (entityLookup.ContainsKey(tableName))
            {
                throw new InvalidOperationException($"Duplicate table name '{tableName}' in schema '{effectiveSchema}'.");
            }

            var entity = new GenericEntity
            {
                Name = tableName,
            };

            workspace.Model.Entities.Add(entity);
            entityLookup[tableName] = entity;
        }

        foreach (var entity in workspace.Model.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var columns = await SqlServerImportReader.LoadColumnsAsync(connection, effectiveSchema, entity.Name, cancellationToken).ConfigureAwait(false);
            ApplyEntityColumns(entity, columns);
        }

        var relationships = await SqlServerImportReader.LoadRelationshipsAsync(connection, effectiveSchema, cancellationToken).ConfigureAwait(false);
        foreach (var relationship in relationships)
        {
            if (!entityLookup.TryGetValue(relationship.SourceTable, out var sourceEntity) ||
                !entityLookup.TryGetValue(relationship.TargetTable, out var targetEntity))
            {
                continue;
            }

            if (!string.Equals(relationship.TargetColumn, "Id", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Foreign key '{relationship.ConstraintName}' on '{sourceEntity.Name}.{relationship.SourceColumn}' must reference '{targetEntity.Name}.Id'.");
            }

            var sourceColumnName = relationship.SourceColumn.Trim();
            ValidateIdentifier(sourceColumnName, $"Foreign key column on table '{sourceEntity.Name}'");
            if (!sourceColumnName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) || sourceColumnName.Length <= 2)
            {
                throw new InvalidOperationException(
                    $"Foreign key '{relationship.ConstraintName}' on '{sourceEntity.Name}.{relationship.SourceColumn}' must use an '<Role>Id' column name.");
            }

            var role = sourceColumnName[..^2];
            if (sourceEntity.Relationships.Any(item =>
                    string.Equals(item.GetRoleOrDefault(), role, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Table '{sourceEntity.Name}' has duplicate relationship role '{role}'.");
            }

            sourceEntity.Relationships.Add(new GenericRelationship
            {
                Entity = targetEntity.Name,
                Role = string.Equals(role, targetEntity.Name, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : role,
            });
        }

        foreach (var entity in workspace.Model.Entities)
        {
            NormalizeRelationshipProperties(entity);
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
            var inferredDataType = CsvImportSupport.InferDataType(values);

            entity.Properties.Add(new GenericProperty
            {
                Name = plan.PropertyName,
                DataType = inferredDataType,
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

    private static void ApplyEntityColumns(GenericEntity entity, IReadOnlyCollection<SqlServerColumnRow> columns)
    {
        var properties = new List<GenericProperty>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            ValidateIdentifier(column.Name, $"Column name on table '{entity.Name}'");
            if (!seen.Add(column.Name))
            {
                throw new InvalidOperationException($"Duplicate column '{column.Name}' on table '{entity.Name}'.");
            }

            if (string.Equals(column.Name, "Id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            properties.Add(new GenericProperty
            {
                Name = column.Name,
                DataType = "string",
                IsNullable = column.IsNullable,
            });
        }

        if (!seen.Contains("Id"))
        {
            throw new InvalidOperationException($"Table '{entity.Name}' must contain required column 'Id'.");
        }

        entity.Properties.Clear();
        entity.Properties.AddRange(properties);
    }

    private static void NormalizeRelationshipProperties(GenericEntity entity)
    {
        if (entity.Relationships.Count == 0 || entity.Properties.Count == 0)
        {
            return;
        }

        var relationshipColumns = entity.Relationships
            .Where(item => !string.IsNullOrWhiteSpace(item.Entity))
            .Select(item => item.GetColumnName())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        entity.Properties.RemoveAll(property =>
            relationshipColumns.Contains(property.Name));
    }

    private static void ValidateIdentifier(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }

        if (value.Length > MaxIdentifierLength)
        {
            throw new InvalidOperationException($"{label} '{value}' exceeds max length {MaxIdentifierLength}.");
        }

        if (!IdentifierPattern.IsMatch(value))
        {
            throw new InvalidOperationException(
                $"{label} '{value}' is invalid. Use [A-Za-z_][A-Za-z0-9_]* and max length {MaxIdentifierLength}.");
        }
    }

    private static string NormalizeIdentity(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

}





