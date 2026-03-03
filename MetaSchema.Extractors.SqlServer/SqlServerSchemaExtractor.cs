using System.Data;
using Microsoft.Data.SqlClient;
using Meta.Core.Domain;
using MetaSchema.Core;

namespace MetaSchema.Extractors.SqlServer;

public sealed class SqlServerSchemaExtractor
{
    public Workspace ExtractSchemaCatalogWorkspace(SqlServerExtractRequest request)
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

        if (string.IsNullOrWhiteSpace(request.SchemaName))
        {
            throw new InvalidOperationException("extract sqlserver requires --schema <name>.");
        }

        if (string.IsNullOrWhiteSpace(request.TableName))
        {
            throw new InvalidOperationException("extract sqlserver requires --table <name>.");
        }

        var workspace = MetaSchemaCatalogWorkspaces.CreateEmptySchemaCatalogWorkspace(request.NewWorkspacePath);

        using var connection = new SqlConnection(request.ConnectionString);
        connection.Open();

        var databaseName = string.IsNullOrWhiteSpace(connection.Database)
            ? "(default)"
            : connection.Database;
        var dataSource = connection.DataSource ?? string.Empty;
        var systemName = request.SystemName.Trim();
        var schemaName = request.SchemaName.Trim();
        var tableName = request.TableName.Trim();

        EnsureTableExists(connection, schemaName, tableName);

        var systemId = BuildSystemId(systemName);
        var schemaId = BuildSchemaId(databaseName, schemaName);

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

        AddRecord(
            workspace,
            "Schema",
            schemaId,
            values => values["Name"] = schemaName,
            relationships => relationships["SystemId"] = systemId);

        var tableRow = LoadTable(connection, schemaName, tableName);
        var columnRows = LoadColumns(connection, schemaName, tableName);

        var fieldTypeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dataTypeName in columnRows
                     .Select(row => row.DataTypeName)
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(name => name, StringComparer.Ordinal))
        {
            var fieldTypeId = BuildFieldTypeId(databaseName, dataTypeName);
            fieldTypeIds[dataTypeName] = fieldTypeId;
            AddRecord(
                workspace,
                "FieldType",
                fieldTypeId,
                values =>
                {
                    values["Name"] = dataTypeName;
                    values["Family"] = ClassifyFamily(dataTypeName);
                    values["IsNative"] = "true";
                });
        }

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

        foreach (var columnRow in columnRows
                     .OrderBy(row => row.TableName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(row => row.TableName, StringComparer.Ordinal)
                     .ThenBy(row => row.OrdinalPosition))
        {
            var fieldTypeId = fieldTypeIds[columnRow.DataTypeName];
            var fieldId = BuildFieldId(databaseName, columnRow.SchemaName, columnRow.TableName, columnRow.ColumnName);
            AddRecord(
                workspace,
                "Field",
                fieldId,
                values =>
                {
                    values["Name"] = columnRow.ColumnName;
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
                relationships =>
                {
                    relationships["TableId"] = tableId;
                    relationships["FieldTypeId"] = fieldTypeId;
                });
        }

        workspace.IsDirty = true;
        return workspace;
    }

    private static void EnsureTableExists(SqlConnection connection, string schemaName, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            select 1
            from INFORMATION_SCHEMA.TABLES
            where TABLE_SCHEMA = @schemaName
              and TABLE_NAME = @tableName
              and TABLE_TYPE in ('BASE TABLE', 'VIEW')
            """;
        command.Parameters.Add(new SqlParameter("@schemaName", SqlDbType.NVarChar, 128) { Value = schemaName });
        command.Parameters.Add(new SqlParameter("@tableName", SqlDbType.NVarChar, 128) { Value = tableName });
        var exists = command.ExecuteScalar();
        if (exists == null)
        {
            throw new InvalidOperationException(
                $"SQL Server table '{schemaName}.{tableName}' was not found in database '{connection.Database}'.");
        }
    }

    private static TableRow LoadTable(SqlConnection connection, string schemaName, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            select
                TABLE_SCHEMA,
                TABLE_NAME,
                TABLE_TYPE
            from INFORMATION_SCHEMA.TABLES
            where TABLE_SCHEMA = @schemaName
              and TABLE_NAME = @tableName
              and TABLE_TYPE in ('BASE TABLE', 'VIEW')
            """;
        command.Parameters.Add(new SqlParameter("@schemaName", SqlDbType.NVarChar, 128) { Value = schemaName });
        command.Parameters.Add(new SqlParameter("@tableName", SqlDbType.NVarChar, 128) { Value = tableName });

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException(
                $"SQL Server table '{schemaName}.{tableName}' was not found in database '{connection.Database}'.");
        }

        return new TableRow(
            SchemaName: reader.GetString(0),
            TableName: reader.GetString(1),
            ObjectType: NormalizeTableType(reader.GetString(2)));
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

    private static string ClassifyFamily(string dataTypeName)
    {
        var typeName = dataTypeName.Trim().ToLowerInvariant();
        return typeName switch
        {
            "bit" => "Boolean",
            "tinyint" or "smallint" or "int" or "bigint" or "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real" => "Numeric",
            "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" or "time" => "Temporal",
            "char" or "nchar" or "varchar" or "nvarchar" or "text" or "ntext" or "xml" => "String",
            "binary" or "varbinary" or "image" or "rowversion" or "timestamp" => "Binary",
            "uniqueidentifier" => "Identifier",
            _ => "Other",
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

    private static string BuildFieldTypeId(string databaseName, string dataTypeName)
    {
        return "sqlserver:" + databaseName + ":fieldtype:" + dataTypeName;
    }

    private static string BuildFieldId(string databaseName, string schemaName, string tableName, string columnName)
    {
        return "sqlserver:" + databaseName + ":schema:" + schemaName + ":table:" + tableName + ":field:" + columnName;
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
}

public sealed class SqlServerExtractRequest
{
    public string NewWorkspacePath { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string SystemName { get; set; } = string.Empty;
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
}
