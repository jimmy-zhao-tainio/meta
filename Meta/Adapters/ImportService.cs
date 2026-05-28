using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
        foreach (var tableName in await LoadTableNamesAsync(connection, effectiveSchema, cancellationToken).ConfigureAwait(false))
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
            var columns = await LoadColumnsAsync(connection, effectiveSchema, entity.Name, cancellationToken).ConfigureAwait(false);
            ApplyEntityColumns(entity, columns);
        }

        var relationships = await LoadRelationshipsAsync(connection, effectiveSchema, cancellationToken).ConfigureAwait(false);
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
            var rows = await LoadRowsAsync(connection, effectiveSchema, entity, cancellationToken).ConfigureAwait(false);
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
        var parsedRows = ParseCsvRows(csvText);
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
        var idColumnIndex = ResolveIdColumnIndex(header, idColumn);

        var entityIdentifier = NormalizeIdentifier(entityName, fallback: "Entity");
        var modelName = NormalizeIdentifier(entityIdentifier + "Model", fallback: "ImportedModel");
        var columnPlans = BuildCsvColumnPlans(header, idColumnIndex);

        var dataRows = new List<IReadOnlyList<string>>();
        for (var index = 1; index < parsedRows.Count; index++)
        {
            var row = parsedRows[index];
            if (row.Count > header.Count)
            {
                throw new InvalidOperationException(
                    $"CSV row {index + 1} has {row.Count.ToString(CultureInfo.InvariantCulture)} values but header has {header.Count.ToString(CultureInfo.InvariantCulture)} columns.");
            }

            if (IsCsvRowCompletelyEmpty(row))
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
            var values = dataRows.Select(row => GetCsvCellValue(row, plan.ColumnIndex)).ToList();
            var hasEmpty = values.Any(value => string.IsNullOrWhiteSpace(value));
            var inferredDataType = InferCsvDataType(values);

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
            var recordId = NormalizeIdentity(GetCsvCellValue(dataRow, idColumnIndex));
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
                var cellValue = GetCsvCellValue(dataRow, plan.ColumnIndex);
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

    private static async Task<List<string>> LoadTableNamesAsync(
        SqlConnection connection,
        string schema,
        CancellationToken cancellationToken)
    {
        var tables = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT TABLE_NAME
                              FROM INFORMATION_SCHEMA.TABLES
                              WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA = @schema
                              ORDER BY TABLE_NAME;
                              """;
        command.Parameters.Add(new SqlParameter("@schema", SqlDbType.NVarChar, 128) { Value = schema });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task<List<ColumnRow>> LoadColumnsAsync(
        SqlConnection connection,
        string schema,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new List<ColumnRow>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT COLUMN_NAME, IS_NULLABLE
                              FROM INFORMATION_SCHEMA.COLUMNS
                              WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
                              ORDER BY ORDINAL_POSITION;
                              """;
        command.Parameters.Add(new SqlParameter("@schema", SqlDbType.NVarChar, 128) { Value = schema });
        command.Parameters.Add(new SqlParameter("@table", SqlDbType.NVarChar, 128) { Value = tableName });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(new ColumnRow
            {
                Name = reader.GetString(0),
                IsNullable = string.Equals(reader.GetString(1), "YES", StringComparison.OrdinalIgnoreCase),
            });
        }

        return columns;
    }

    private static void ApplyEntityColumns(GenericEntity entity, IReadOnlyCollection<ColumnRow> columns)
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

    private static async Task<List<RelationshipRow>> LoadRelationshipsAsync(
        SqlConnection connection,
        string schema,
        CancellationToken cancellationToken)
    {
        var relationships = new List<RelationshipRow>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT
                                  fk.name AS ConstraintName,
                                  srcTable.name AS SourceTable,
                                  srcColumn.name AS SourceColumn,
                                  dstTable.name AS TargetTable,
                                  dstColumn.name AS TargetColumn,
                                  fkc.constraint_column_id AS ConstraintColumnId
                              FROM sys.foreign_keys fk
                              INNER JOIN sys.foreign_key_columns fkc
                                  ON fk.object_id = fkc.constraint_object_id
                              INNER JOIN sys.tables srcTable
                                  ON srcTable.object_id = fk.parent_object_id
                              INNER JOIN sys.schemas srcSchema
                                  ON srcSchema.schema_id = srcTable.schema_id
                              INNER JOIN sys.columns srcColumn
                                  ON srcColumn.object_id = fkc.parent_object_id
                                  AND srcColumn.column_id = fkc.parent_column_id
                              INNER JOIN sys.tables dstTable
                                  ON dstTable.object_id = fk.referenced_object_id
                              INNER JOIN sys.schemas dstSchema
                                  ON dstSchema.schema_id = dstTable.schema_id
                              INNER JOIN sys.columns dstColumn
                                  ON dstColumn.object_id = fkc.referenced_object_id
                                  AND dstColumn.column_id = fkc.referenced_column_id
                              WHERE srcSchema.name = @schema
                                AND dstSchema.name = @schema
                              ORDER BY fk.name, fkc.constraint_column_id;
                              """;
        command.Parameters.Add(new SqlParameter("@schema", SqlDbType.NVarChar, 128) { Value = schema });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            relationships.Add(new RelationshipRow
            {
                ConstraintName = reader.GetString(0),
                SourceTable = reader.GetString(1),
                SourceColumn = reader.GetString(2),
                TargetTable = reader.GetString(3),
                TargetColumn = reader.GetString(4),
                ConstraintColumnId = reader.GetInt32(5),
            });
        }

        var grouped = relationships
            .GroupBy(item => item.ConstraintName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var normalized = new List<RelationshipRow>(grouped.Count);
        foreach (var group in grouped)
        {
            if (group.Count() != 1)
            {
                var sample = group.First();
                throw new InvalidOperationException(
                    $"Composite foreign key '{group.Key}' on '{sample.SourceTable}' is not supported.");
            }

            normalized.Add(group.Single());
        }

        return normalized;
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

    private static async Task<List<GenericRecord>> LoadRowsAsync(
        SqlConnection connection,
        string schema,
        GenericEntity entity,
        CancellationToken cancellationToken)
    {
        var rows = new List<GenericRecord>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tableName = EscapeSqlIdentifier(entity.Name);
        var schemaName = EscapeSqlIdentifier(schema);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM [{schemaName}].[{tableName}] ORDER BY [Id];";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columnNames.Add(reader.GetName(i));
        }

        if (!columnNames.Contains("Id"))
        {
            throw new InvalidOperationException(
                $"Table '{schema}.{entity.Name}' does not include required column 'Id'.");
        }

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader["Id"] is DBNull)
            {
                throw new InvalidOperationException($"Table '{schema}.{entity.Name}' contains null Id values.");
            }

            var id = NormalizeIdentity(Convert.ToString(reader["Id"], CultureInfo.InvariantCulture));
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException($"Table '{schema}.{entity.Name}' contains empty Id values.");
            }

            if (!seenIds.Add(id))
            {
                throw new InvalidOperationException($"Table '{schema}.{entity.Name}' contains duplicate Id '{id}'.");
            }

            var record = new GenericRecord
            {
                Id = id,
            };

            foreach (var property in entity.Properties
                         .Where(item => !string.Equals(item.Name, "Id", StringComparison.OrdinalIgnoreCase)))
            {
                if (!columnNames.Contains(property.Name))
                {
                    continue;
                }

                var value = reader[property.Name];
                if (value is DBNull)
                {
                    continue;
                }

                var textValue = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (textValue == null)
                {
                    continue;
                }

                record.Values[property.Name] = textValue;
            }

            foreach (var relationship in entity.Relationships)
            {
                var columnName = relationship.GetColumnName();
                if (!columnNames.Contains(columnName))
                {
                    throw new InvalidOperationException(
                        $"Table '{schema}.{entity.Name}' is missing relationship column '{columnName}'.");
                }

                var relationshipValue = reader[columnName];
                if (relationshipValue is DBNull)
                {
                    throw new InvalidOperationException(
                        $"Table '{schema}.{entity.Name}' has null relationship value for '{columnName}' on row '{id}'.");
                }

                var relationshipId = NormalizeIdentity(Convert.ToString(relationshipValue, CultureInfo.InvariantCulture));
                if (string.IsNullOrWhiteSpace(relationshipId))
                {
                    throw new InvalidOperationException(
                        $"Table '{schema}.{entity.Name}' has empty relationship value for '{columnName}' on row '{id}'.");
                }

                record.RelationshipIds[relationship.GetColumnName()] = relationshipId;
            }

            rows.Add(record);
        }

        return rows;
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

    private static string EscapeSqlIdentifier(string value)
    {
        return value.Replace("]", "]]", StringComparison.Ordinal);
    }

    private static string NormalizeIdentity(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static int ResolveIdColumnIndex(IReadOnlyList<string> headerRow, string idColumn)
    {
        var matches = new List<int>();
        for (var index = 0; index < headerRow.Count; index++)
        {
            if (string.Equals(headerRow[index]?.Trim(), idColumn.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(index);
            }
        }

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"CSV file must include Id column '{idColumn}'.");
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"CSV file contains duplicate Id column '{idColumn}'.");
        }

        return matches[0];
    }

    private static List<CsvColumnPlan> BuildCsvColumnPlans(IReadOnlyList<string> headerRow, int idColumnIndex)
    {
        var usedPropertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<CsvColumnPlan>();
        for (var index = 0; index < headerRow.Count; index++)
        {
            if (index == idColumnIndex)
            {
                continue;
            }

            var rawHeader = index < headerRow.Count ? headerRow[index] : string.Empty;
            var fallback = "Column" + (index + 1).ToString(CultureInfo.InvariantCulture);
            var normalized = NormalizeIdentifier(rawHeader, fallback);
            if (string.Equals(normalized, "Id", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "IdValue";
            }

            var unique = MakeUniqueIdentifier(normalized, usedPropertyNames);
            plans.Add(new CsvColumnPlan(index, unique));
        }

        return plans;
    }

    private static List<List<string>> ParseCsvRows(string csvText)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var currentCell = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < csvText.Length; index++)
        {
            var ch = csvText[index];
            if (ch == '"')
            {
                if (inQuotes && index + 1 < csvText.Length && csvText[index + 1] == '"')
                {
                    currentCell.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (!inQuotes && ch == ',')
            {
                AppendCsvCell(currentRow, currentCell);
                continue;
            }

            if (!inQuotes && (ch == '\r' || ch == '\n'))
            {
                AppendCsvCell(currentRow, currentCell);
                if (!IsCsvRowCompletelyEmpty(currentRow))
                {
                    rows.Add(currentRow);
                }

                currentRow = new List<string>();
                if (ch == '\r' && index + 1 < csvText.Length && csvText[index + 1] == '\n')
                {
                    index++;
                }

                continue;
            }

            currentCell.Append(ch);
        }

        if (inQuotes)
        {
            throw new InvalidOperationException("CSV contains an unclosed quoted field.");
        }

        AppendCsvCell(currentRow, currentCell);
        if (!IsCsvRowCompletelyEmpty(currentRow) || rows.Count == 0)
        {
            rows.Add(currentRow);
        }

        return rows;
    }

    private static void AppendCsvCell(ICollection<string> row, StringBuilder currentCell)
    {
        var value = currentCell
            .ToString()
            .Trim()
            .TrimStart('\uFEFF');
        row.Add(value);
        currentCell.Clear();
    }

    private static string GetCsvCellValue(IReadOnlyList<string> row, int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= row.Count)
        {
            return string.Empty;
        }

        return row[columnIndex];
    }

    private static bool IsCsvRowCompletelyEmpty(IReadOnlyCollection<string> row)
    {
        return row.Count == 0 || row.All(cell => string.IsNullOrWhiteSpace(cell));
    }

    private static string InferCsvDataType(IReadOnlyCollection<string> values)
    {
        var nonEmptyValues = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (nonEmptyValues.Count == 0)
        {
            return "string";
        }

        if (nonEmptyValues.All(value => bool.TryParse(value, out _)))
        {
            return "bool";
        }

        if (nonEmptyValues.All(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            return "int";
        }

        if (nonEmptyValues.All(value => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            return "long";
        }

        if (nonEmptyValues.All(value => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)))
        {
            return "decimal";
        }

        if (nonEmptyValues.All(value => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out _)))
        {
            return "datetime";
        }

        return "string";
    }

    private static string NormalizeIdentifier(string value, string fallback)
    {
        var input = (value ?? string.Empty).Trim().TrimStart('\uFEFF');
        if (input.Length == 0)
        {
            input = fallback;
        }

        var builder = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }

        var normalized = builder.ToString().Trim('_');
        if (normalized.Length == 0)
        {
            normalized = fallback;
        }

        if (!char.IsLetter(normalized[0]) && normalized[0] != '_')
        {
            normalized = "_" + normalized;
        }

        normalized = CollapseUnderscores(normalized);
        if (normalized.Length > MaxIdentifierLength)
        {
            normalized = normalized[..MaxIdentifierLength];
        }

        if (!IdentifierPattern.IsMatch(normalized))
        {
            normalized = "_" + normalized.TrimStart('_');
            if (normalized.Length > MaxIdentifierLength)
            {
                normalized = normalized[..MaxIdentifierLength];
            }
        }

        ValidateIdentifier(normalized, "Identifier");
        return normalized;
    }

    private static string CollapseUnderscores(string value)
    {
        if (!value.Contains('_', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasUnderscore = false;
        foreach (var ch in value)
        {
            if (ch == '_')
            {
                if (previousWasUnderscore)
                {
                    continue;
                }

                previousWasUnderscore = true;
                builder.Append(ch);
                continue;
            }

            previousWasUnderscore = false;
            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string MakeUniqueIdentifier(string normalizedBase, ISet<string> usedNames)
    {
        if (usedNames.Add(normalizedBase))
        {
            return normalizedBase;
        }

        var suffix = 2;
        while (true)
        {
            var suffixText = "_" + suffix.ToString(CultureInfo.InvariantCulture);
            var maxBaseLength = MaxIdentifierLength - suffixText.Length;
            var baseName = normalizedBase.Length <= maxBaseLength
                ? normalizedBase
                : normalizedBase[..maxBaseLength];
            var candidate = baseName + suffixText;
            if (usedNames.Add(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private sealed class ColumnRow
    {
        public string Name { get; set; } = string.Empty;
        public bool IsNullable { get; set; }
    }

    private sealed class RelationshipRow
    {
        public string ConstraintName { get; set; } = string.Empty;
        public string SourceTable { get; set; } = string.Empty;
        public string SourceColumn { get; set; } = string.Empty;
        public string TargetTable { get; set; } = string.Empty;
        public string TargetColumn { get; set; } = string.Empty;
        public int ConstraintColumnId { get; set; }
    }

    private sealed class CsvColumnPlan
    {
        public CsvColumnPlan(int columnIndex, string propertyName)
        {
            ColumnIndex = columnIndex;
            PropertyName = propertyName;
        }

        public int ColumnIndex { get; }
        public string PropertyName { get; }
    }
}





