using System.Data;
using Microsoft.Data.SqlClient;
using Meta.Core.Domain;
using MetaSchema.Core;

namespace MetaSchema.Extractors.SqlServer;

public sealed class SqlServerSchemaExtractor
{
    public Workspace ExtractMetaSchemaWorkspace(SqlServerExtractRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.NewWorkspacePath))
        {
            throw new InvalidOperationException("extract sqlserver requires --new-workspace <path>.");
        }

        if (string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            throw new InvalidOperationException("extract sqlserver requires --connection <connectionString>.");
        }

        if (string.IsNullOrWhiteSpace(request.SystemName))
        {
            throw new InvalidOperationException("extract sqlserver requires --system <name>.");
        }

        if (string.IsNullOrWhiteSpace(request.SchemaName) && !request.AllSchemas)
        {
            throw new InvalidOperationException("extract sqlserver requires --schema <name> or --all-schemas.");
        }

        if (!string.IsNullOrWhiteSpace(request.SchemaName) && request.AllSchemas)
        {
            throw new InvalidOperationException("extract sqlserver does not allow --schema with --all-schemas.");
        }

        if (string.IsNullOrWhiteSpace(request.TableName) && !request.AllTables)
        {
            throw new InvalidOperationException("extract sqlserver requires --table <name> or --all-tables.");
        }

        if (!string.IsNullOrWhiteSpace(request.TableName) && request.AllTables)
        {
            throw new InvalidOperationException("extract sqlserver does not allow --table with --all-tables.");
        }

        var workspace = MetaSchemaWorkspaces.CreateEmptyMetaSchemaWorkspace(request.NewWorkspacePath);

        using var connection = new SqlConnection(request.ConnectionString);
        connection.Open();

        var databaseName = string.IsNullOrWhiteSpace(connection.Database)
            ? "(default)"
            : connection.Database;
        var dataSource = connection.DataSource ?? string.Empty;
        var systemName = request.SystemName.Trim();
        var schemaFilter = request.AllSchemas ? null : request.SchemaName.Trim();
        var tableFilter = request.AllTables ? null : request.TableName.Trim();
        var tableRows = LoadTables(connection, schemaFilter, tableFilter)
            .OrderBy(row => row.SchemaName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.SchemaName, StringComparer.Ordinal)
            .ThenBy(row => row.TableName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.TableName, StringComparer.Ordinal)
            .ToList();
        if (tableRows.Count == 0)
        {
            var filterDescription = (schemaFilter, tableFilter) switch
            {
                (null, null) => "any schema/table filter",
                (not null, null) => $"schema '{schemaFilter}'",
                (null, not null) => $"table '{tableFilter}'",
                (not null, not null) => $"table '{schemaFilter}.{tableFilter}'",
            };
            throw new InvalidOperationException(
                $"No SQL Server tables matched {filterDescription} in database '{connection.Database}'.");
        }

        var systemId = BuildSystemId(systemName);

        AddRecord(
            workspace,
            "System",
            systemId,
            values =>
            {
                values["Name"] = systemName;
                if (!string.IsNullOrWhiteSpace(dataSource))
                {
                    values["Description"] = databaseName + " @ " + dataSource;
                }
            });

        var schemaNames = tableRows.Select(row => row.SchemaName).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal);
        foreach (var schemaName in schemaNames)
        {
            var schemaId = BuildSchemaId(databaseName, schemaName);
            AddRecord(
                workspace,
                "Schema",
                schemaId,
                values => values["Name"] = schemaName,
                relationships => relationships["SystemId"] = systemId);
        }

        foreach (var tableRow in tableRows)
        {
            var schemaId = BuildSchemaId(databaseName, tableRow.SchemaName);
            var columnRows = LoadColumns(connection, tableRow.SchemaName, tableRow.TableName);
            var foreignKeys = LoadForeignKeys(connection, tableRow.SchemaName, tableRow.TableName);
            var foreignKeyColumns = LoadForeignKeyColumns(connection, tableRow.SchemaName, tableRow.TableName);

            var tableId = BuildTableId(databaseName, tableRow.SchemaName, tableRow.TableName);
            AddRecord(
                workspace,
                "Table",
                tableId,
                values =>
                {
                    values["Name"] = tableRow.TableName;
                    if (!string.IsNullOrWhiteSpace(tableRow.ObjectType))
                    {
                        values["ObjectType"] = tableRow.ObjectType;
                    }
                },
                relationships => relationships["SchemaId"] = schemaId);

            var sourceFieldIdByColumnName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var columnRow in columnRows
                         .OrderBy(row => row.TableName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(row => row.TableName, StringComparer.Ordinal)
                         .ThenBy(row => row.OrdinalPosition))
            {
                var fieldId = BuildFieldId(databaseName, columnRow.SchemaName, columnRow.TableName, columnRow.ColumnName);
                sourceFieldIdByColumnName[columnRow.ColumnName] = fieldId;
                AddRecord(
                    workspace,
                    "Field",
                    fieldId,
                    values =>
                    {
                        values["Name"] = columnRow.ColumnName;
                        values["TypeId"] = BuildTypeId(columnRow.DataTypeName);
                        values["Ordinal"] = columnRow.OrdinalPosition.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        values["IsNullable"] = columnRow.IsNullable ? "true" : "false";
                        if (columnRow.Length.HasValue)
                        {
                            values["Length"] = columnRow.Length.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        }

                        if (columnRow.NumericPrecision.HasValue)
                        {
                            values["NumericPrecision"] = columnRow.NumericPrecision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        }

                        if (columnRow.Scale.HasValue)
                        {
                            values["Scale"] = columnRow.Scale.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        }
                    },
                    relationships => relationships["TableId"] = tableId);
            }

            var foreignKeyColumnsByName = foreignKeyColumns
                .GroupBy(row => row.ForeignKeyName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group
                    .OrderBy(item => item.Ordinal)
                    .ToList(), StringComparer.Ordinal);

            foreach (var foreignKey in foreignKeys
                         .OrderBy(row => row.Name, StringComparer.Ordinal))
            {
                var relationshipId = BuildRelationshipId(databaseName, tableRow.SchemaName, tableRow.TableName, foreignKey.Name);
                AddRecord(
                    workspace,
                    "TableRelationship",
                    relationshipId,
                    values =>
                    {
                        values["Name"] = foreignKey.Name;
                        values["TargetSchemaName"] = foreignKey.TargetSchemaName;
                        values["TargetTableName"] = foreignKey.TargetTableName;
                    },
                    relationships => relationships["SourceTableId"] = tableId);

                if (!foreignKeyColumnsByName.TryGetValue(foreignKey.Name, out var fkColumns))
                {
                    continue;
                }

                foreach (var fkColumn in fkColumns)
                {
                    if (!sourceFieldIdByColumnName.TryGetValue(fkColumn.SourceColumnName, out var sourceFieldId))
                    {
                        throw new InvalidOperationException(
                            $"SQL Server foreign key '{tableRow.SchemaName}.{tableRow.TableName}.{foreignKey.Name}' referenced source column '{fkColumn.SourceColumnName}' that was not extracted.");
                    }

                    var relationshipFieldId = BuildRelationshipFieldId(
                        databaseName,
                        tableRow.SchemaName,
                        tableRow.TableName,
                        foreignKey.Name,
                        fkColumn.Ordinal);
                    AddRecord(
                        workspace,
                        "TableRelationshipField",
                        relationshipFieldId,
                        values =>
                        {
                            values["Ordinal"] = fkColumn.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            values["SourceFieldName"] = fkColumn.SourceColumnName;
                            values["TargetFieldName"] = fkColumn.TargetColumnName;
                        },
                        relationships =>
                        {
                            relationships["TableRelationshipId"] = relationshipId;
                            relationships["SourceFieldId"] = sourceFieldId;
                        });
                }
            }
        }

        workspace.IsDirty = true;
        return workspace;
    }

    private static List<TableRow> LoadTables(SqlConnection connection, string? schemaName, string? tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            select
                TABLE_SCHEMA,
                TABLE_NAME,
                TABLE_TYPE
            from INFORMATION_SCHEMA.TABLES
            where TABLE_TYPE in ('BASE TABLE', 'VIEW')
              and (@schemaName is null or TABLE_SCHEMA = @schemaName)
              and (@tableName is null or TABLE_NAME = @tableName)
            order by TABLE_SCHEMA, TABLE_NAME
            """;
        command.Parameters.Add(new SqlParameter("@schemaName", SqlDbType.NVarChar, 128) { Value = string.IsNullOrWhiteSpace(schemaName) ? DBNull.Value : schemaName });
        command.Parameters.Add(new SqlParameter("@tableName", SqlDbType.NVarChar, 128) { Value = string.IsNullOrWhiteSpace(tableName) ? DBNull.Value : tableName });

        var rows = new List<TableRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new TableRow(
                SchemaName: reader.GetString(0),
                TableName: reader.GetString(1),
                ObjectType: NormalizeTableType(reader.GetString(2))));
        }

        return rows;
    }

    private static List<ColumnRow> LoadColumns(SqlConnection connection, string schemaName, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            select
                TABLE_SCHEMA,
                TABLE_NAME,
                COLUMN_NAME,
                ORDINAL_POSITION,
                IS_NULLABLE,
                DATA_TYPE,
                CHARACTER_MAXIMUM_LENGTH,
                NUMERIC_PRECISION,
                NUMERIC_SCALE
            from INFORMATION_SCHEMA.COLUMNS
            where TABLE_SCHEMA = @schemaName
              and TABLE_NAME = @tableName
            order by TABLE_NAME, ORDINAL_POSITION
            """;
        command.Parameters.Add(new SqlParameter("@schemaName", SqlDbType.NVarChar, 128) { Value = schemaName });
        command.Parameters.Add(new SqlParameter("@tableName", SqlDbType.NVarChar, 128) { Value = tableName });

        var rows = new List<ColumnRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new ColumnRow(
                SchemaName: reader.GetString(0),
                TableName: reader.GetString(1),
                ColumnName: reader.GetString(2),
                OrdinalPosition: reader.GetInt32(3),
                IsNullable: string.Equals(reader.GetString(4), "YES", StringComparison.OrdinalIgnoreCase),
                DataTypeName: reader.GetString(5),
                Length: ReadNullableInt(reader, 6),
                NumericPrecision: ReadNullableInt(reader, 7),
                Scale: ReadNullableInt(reader, 8)));
        }

        return rows;
    }

    private static List<ForeignKeyRow> LoadForeignKeys(SqlConnection connection, string schemaName, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            select
                fk.name as ForeignKeyName,
                refSchema.name as TargetSchemaName,
                refTable.name as TargetTableName
            from sys.foreign_keys fk
            join sys.tables srcTable on srcTable.object_id = fk.parent_object_id
            join sys.schemas srcSchema on srcSchema.schema_id = srcTable.schema_id
            join sys.tables refTable on refTable.object_id = fk.referenced_object_id
            join sys.schemas refSchema on refSchema.schema_id = refTable.schema_id
            where srcSchema.name = @schemaName
              and srcTable.name = @tableName
              and fk.is_disabled = 0
              and fk.is_not_trusted = 0
            order by fk.name
            """;
        command.Parameters.Add(new SqlParameter("@schemaName", SqlDbType.NVarChar, 128) { Value = schemaName });
        command.Parameters.Add(new SqlParameter("@tableName", SqlDbType.NVarChar, 128) { Value = tableName });

        var rows = new List<ForeignKeyRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new ForeignKeyRow(
                Name: reader.GetString(0),
                TargetSchemaName: reader.GetString(1),
                TargetTableName: reader.GetString(2)));
        }

        return rows;
    }

    private static List<ForeignKeyColumnRow> LoadForeignKeyColumns(SqlConnection connection, string schemaName, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            select
                fk.name as ForeignKeyName,
                fkc.constraint_column_id as Ordinal,
                srcColumn.name as SourceColumnName,
                refColumn.name as TargetColumnName
            from sys.foreign_keys fk
            join sys.tables srcTable on srcTable.object_id = fk.parent_object_id
            join sys.schemas srcSchema on srcSchema.schema_id = srcTable.schema_id
            join sys.foreign_key_columns fkc on fkc.constraint_object_id = fk.object_id
            join sys.columns srcColumn on srcColumn.object_id = fkc.parent_object_id and srcColumn.column_id = fkc.parent_column_id
            join sys.columns refColumn on refColumn.object_id = fkc.referenced_object_id and refColumn.column_id = fkc.referenced_column_id
            where srcSchema.name = @schemaName
              and srcTable.name = @tableName
            order by fk.name, fkc.constraint_column_id
            """;
        command.Parameters.Add(new SqlParameter("@schemaName", SqlDbType.NVarChar, 128) { Value = schemaName });
        command.Parameters.Add(new SqlParameter("@tableName", SqlDbType.NVarChar, 128) { Value = tableName });

        var rows = new List<ForeignKeyColumnRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new ForeignKeyColumnRow(
                ForeignKeyName: reader.GetString(0),
                Ordinal: reader.GetInt32(1),
                SourceColumnName: reader.GetString(2),
                TargetColumnName: reader.GetString(3)));
        }

        return rows;
    }

    private static int? ReadNullableInt(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            byte byteValue => byteValue,
            short shortValue => shortValue,
            int intValue => intValue,
            long longValue => checked((int)longValue),
            decimal decimalValue => decimal.ToInt32(decimalValue),
            _ => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private static string NormalizeTableType(string tableType)
    {
        return tableType switch
        {
            "BASE TABLE" => "Table",
            "VIEW" => "View",
            _ => tableType,
        };
    }

    private static void AddRecord(
        Workspace workspace,
        string entityName,
        string id,
        Action<Dictionary<string, string>>? populateValues = null,
        Action<Dictionary<string, string>>? populateRelationships = null)
    {
        var record = new GenericRecord
        {
            Id = id,
        };
        populateValues?.Invoke(record.Values);
        populateRelationships?.Invoke(record.RelationshipIds);
        workspace.Instance.GetOrCreateEntityRecords(entityName).Add(record);
    }

    private static string BuildSystemId(string databaseName)
    {
        return "sqlserver:system:" + databaseName;
    }

    private static string BuildSchemaId(string databaseName, string schemaName)
    {
        return "sqlserver:" + databaseName + ":schema:" + schemaName;
    }

    private static string BuildTableId(string databaseName, string schemaName, string tableName)
    {
        return "sqlserver:" + databaseName + ":schema:" + schemaName + ":table:" + tableName;
    }

    private static string BuildTypeId(string dataTypeName)
    {
        return "sqlserver:type:" + dataTypeName;
    }

    private static string BuildFieldId(string databaseName, string schemaName, string tableName, string columnName)
    {
        return "sqlserver:" + databaseName + ":schema:" + schemaName + ":table:" + tableName + ":field:" + columnName;
    }

    private static string BuildRelationshipId(string databaseName, string schemaName, string tableName, string relationshipName)
    {
        return "sqlserver:" + databaseName + ":schema:" + schemaName + ":table:" + tableName + ":relationship:" + relationshipName;
    }

    private static string BuildRelationshipFieldId(string databaseName, string schemaName, string tableName, string relationshipName, int ordinal)
    {
        return BuildRelationshipId(databaseName, schemaName, tableName, relationshipName) +
               ":field:" +
               ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private readonly record struct TableRow(
        string SchemaName,
        string TableName,
        string ObjectType);

    private readonly record struct ColumnRow(
        string SchemaName,
        string TableName,
        string ColumnName,
        int OrdinalPosition,
        bool IsNullable,
        string DataTypeName,
        int? Length,
        int? NumericPrecision,
        int? Scale);

    private readonly record struct ForeignKeyRow(
        string Name,
        string TargetSchemaName,
        string TargetTableName);

    private readonly record struct ForeignKeyColumnRow(
        string ForeignKeyName,
        int Ordinal,
        string SourceColumnName,
        string TargetColumnName);
}

public sealed class SqlServerExtractRequest
{
    public string NewWorkspacePath { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string SystemName { get; set; } = string.Empty;
    public string SchemaName { get; set; } = string.Empty;
    public bool AllSchemas { get; set; }
    public string TableName { get; set; } = string.Empty;
    public bool AllTables { get; set; }
}
