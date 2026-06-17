using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Meta.Core.Domain;

namespace Meta.Adapters;

internal static class SqlServerImportReader
{
    public static async Task<List<string>> LoadTableNamesAsync(
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

    public static async Task<List<SqlServerColumnRow>> LoadColumnsAsync(
        SqlConnection connection,
        string schema,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new List<SqlServerColumnRow>();
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
            columns.Add(new SqlServerColumnRow
            {
                Name = reader.GetString(0),
                IsNullable = string.Equals(reader.GetString(1), "YES", StringComparison.OrdinalIgnoreCase),
            });
        }

        return columns;
    }

    public static async Task<List<SqlServerRelationshipRow>> LoadRelationshipsAsync(
        SqlConnection connection,
        string schema,
        CancellationToken cancellationToken)
    {
        var relationships = new List<SqlServerRelationshipRow>();
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
            relationships.Add(new SqlServerRelationshipRow
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
        var normalized = new List<SqlServerRelationshipRow>(grouped.Count);
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

    public static async Task<List<GenericRecord>> LoadRowsAsync(
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

    private static string EscapeSqlIdentifier(string value)
    {
        return value.Replace("]", "]]", StringComparison.Ordinal);
    }

    private static string NormalizeIdentity(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}

internal sealed class SqlServerColumnRow
{
    public string Name { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
}

internal sealed class SqlServerRelationshipRow
{
    public string ConstraintName { get; set; } = string.Empty;
    public string SourceTable { get; set; } = string.Empty;
    public string SourceColumn { get; set; } = string.Empty;
    public string TargetTable { get; set; } = string.Empty;
    public string TargetColumn { get; set; } = string.Empty;
    public int ConstraintColumnId { get; set; }
}
