using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Surfaces.Sql;

internal static class SqlWorkspaceModelReader
{
    public static GenericModel Read(
        SqlConnection connection,
        SqlTransaction? transaction,
        string schema)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var schemaName = MetaName.Require(schema, "Schema name.");
        EnsureRepresentableBehavior(connection, transaction, schemaName);
        var columns = ReadColumns(connection, transaction, schemaName);
        var relationships = ReadRelationships(
            connection,
            transaction,
            schemaName);
        EnsureSingleColumnRelationships(relationships);

        var model = new GenericModel
        {
            Name = SqlWorkspaceModelMetadata.Read(connection, transaction),
        };
        foreach (var tableColumns in columns.GroupBy(
                     column => column.TableName,
                     MetaName.Comparer))
        {
            var entity = new GenericEntity
            {
                Name = MetaName.Require(tableColumns.Key, "Table name."),
            };
            var entityColumns = tableColumns.ToArray();
            EnsureColumnContract(entity.Name, entityColumns, relationships);
            var relationshipColumns = relationships
                .Where(relationship => MetaName.Comparer.Equals(
                    relationship.SourceTable,
                    entity.Name))
                .Select(relationship => relationship.SourceColumn)
                .ToHashSet(MetaName.Comparer);
            foreach (var column in entityColumns)
            {
                if (MetaName.Comparer.Equals(column.Name, "Id") ||
                    relationshipColumns.Contains(column.Name))
                {
                    continue;
                }

                entity.Properties.Add(new GenericProperty
                {
                    Name = MetaName.Require(column.Name, "Property name."),
                    IsNullable = column.IsNullable,
                });
            }

            foreach (var relationship in relationships.Where(relationship =>
                         MetaName.Comparer.Equals(
                             relationship.SourceTable,
                             entity.Name)))
            {
                EnsureRelationshipContract(relationship);
                var role = relationship.SourceColumn[..^2];
                entity.Relationships.Add(new GenericRelationship
                {
                    Entity = MetaName.Require(
                        relationship.TargetTable,
                        "Relationship target entity name."),
                    Role = MetaName.Comparer.Equals(
                        role,
                        relationship.TargetTable)
                        ? string.Empty
                        : MetaName.Require(role, "Relationship role."),
                    IsNullable = relationship.IsNullable,
                });
            }

            model.Entities.Add(entity);
        }

        EnsureValid(model);
        return model;
    }

    private static void EnsureRepresentableBehavior(
        SqlConnection connection,
        SqlTransaction? transaction,
        string schema)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT TOP (1) violation.Description
            FROM
            (
                SELECT
                    10 AS SortOrder,
                    CONCAT(
                        N'Column ''', schemaValue.name, N'.', tableValue.name,
                        N'.', columnValue.name,
                        CASE
                            WHEN columnValue.is_computed = 1
                                THEN N''' is computed.'
                            ELSE N''' has a default constraint.'
                        END) AS Description
                FROM sys.tables tableValue
                INNER JOIN sys.schemas schemaValue
                    ON schemaValue.schema_id = tableValue.schema_id
                INNER JOIN sys.columns columnValue
                    ON columnValue.object_id = tableValue.object_id
                WHERE schemaValue.name = @schema
                  AND (columnValue.is_computed = 1 OR columnValue.default_object_id <> 0)

                UNION ALL

                SELECT
                    20,
                    CONCAT(N'Check constraint ''', schemaValue.name, N'.', checkValue.name, N''' is active.')
                FROM sys.check_constraints checkValue
                INNER JOIN sys.tables tableValue
                    ON tableValue.object_id = checkValue.parent_object_id
                INNER JOIN sys.schemas schemaValue
                    ON schemaValue.schema_id = tableValue.schema_id
                WHERE schemaValue.name = @schema
                  AND checkValue.is_disabled = 0

                UNION ALL

                SELECT
                    30,
                    CONCAT(N'Trigger ''', schemaValue.name, N'.', triggerValue.name, N''' is active.')
                FROM sys.triggers triggerValue
                INNER JOIN sys.tables tableValue
                    ON tableValue.object_id = triggerValue.parent_id
                INNER JOIN sys.schemas schemaValue
                    ON schemaValue.schema_id = tableValue.schema_id
                WHERE schemaValue.name = @schema
                  AND triggerValue.is_disabled = 0
                  AND triggerValue.is_ms_shipped = 0

                UNION ALL

                SELECT
                    40,
                    CONCAT(N'Unique index ''', schemaValue.name, N'.', indexValue.name, N''' is not the record identity primary key.')
                FROM sys.indexes indexValue
                INNER JOIN sys.tables tableValue
                    ON tableValue.object_id = indexValue.object_id
                INNER JOIN sys.schemas schemaValue
                    ON schemaValue.schema_id = tableValue.schema_id
                WHERE schemaValue.name = @schema
                  AND indexValue.is_unique = 1
                  AND indexValue.is_primary_key = 0
                  AND indexValue.is_disabled = 0

                UNION ALL

                SELECT
                    50,
                    CONCAT(N'Foreign key ''', foreignKey.name, N''' uses a cascading referential action.')
                FROM sys.foreign_keys foreignKey
                INNER JOIN sys.tables sourceTable
                    ON sourceTable.object_id = foreignKey.parent_object_id
                INNER JOIN sys.schemas sourceSchema
                    ON sourceSchema.schema_id = sourceTable.schema_id
                INNER JOIN sys.tables targetTable
                    ON targetTable.object_id = foreignKey.referenced_object_id
                INNER JOIN sys.schemas targetSchema
                    ON targetSchema.schema_id = targetTable.schema_id
                WHERE sourceSchema.name = @schema
                  AND targetSchema.name = @schema
                  AND foreignKey.is_disabled = 0
                  AND (foreignKey.delete_referential_action <> 0 OR foreignKey.update_referential_action <> 0)

                UNION ALL

                SELECT
                    60,
                    CONCAT(
                        N'Foreign key ''', foreignKey.name,
                        N''' crosses the SQL workspace schema boundary from ''',
                        sourceSchema.name, N''' to ''', targetSchema.name, N'''.')
                FROM sys.foreign_keys foreignKey
                INNER JOIN sys.tables sourceTable
                    ON sourceTable.object_id = foreignKey.parent_object_id
                INNER JOIN sys.schemas sourceSchema
                    ON sourceSchema.schema_id = sourceTable.schema_id
                INNER JOIN sys.tables targetTable
                    ON targetTable.object_id = foreignKey.referenced_object_id
                INNER JOIN sys.schemas targetSchema
                    ON targetSchema.schema_id = targetTable.schema_id
                WHERE (sourceSchema.name = @schema AND targetSchema.name <> @schema)
                   OR (sourceSchema.name <> @schema AND targetSchema.name = @schema)

                UNION ALL

                SELECT
                    70,
                    CONCAT(N'Table ''', schemaValue.name, N'.', tableValue.name, N''' is system-versioned.')
                FROM sys.tables tableValue
                INNER JOIN sys.schemas schemaValue
                    ON schemaValue.schema_id = tableValue.schema_id
                WHERE schemaValue.name = @schema
                  AND tableValue.temporal_type <> 0

                UNION ALL

                SELECT
                    80,
                    CONCAT(N'Security policy ''', policyValue.name, N''' is active for table ''', schemaValue.name, N'.', tableValue.name, N'''.')
                FROM sys.security_predicates predicateValue
                INNER JOIN sys.security_policies policyValue
                    ON policyValue.object_id = predicateValue.object_id
                INNER JOIN sys.tables tableValue
                    ON tableValue.object_id = predicateValue.target_object_id
                INNER JOIN sys.schemas schemaValue
                    ON schemaValue.schema_id = tableValue.schema_id
                WHERE schemaValue.name = @schema
                  AND policyValue.is_enabled = 1
            ) violation
            ORDER BY violation.SortOrder, violation.Description;
            """;
        command.Parameters.Add(NameParameter("@schema", schema));
        var description = command.ExecuteScalar() as string;
        if (description != null)
        {
            throw new InvalidOperationException(
                description + " Meta does not model this SQL behavior.");
        }
    }

    private static IReadOnlyList<SqlModelColumn> ReadColumns(
        SqlConnection connection,
        SqlTransaction? transaction,
        string schema)
    {
        var columns = new List<SqlModelColumn>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                tableValue.name,
                columnValue.name,
                columnValue.is_nullable,
                typeValue.name,
                columnValue.max_length,
                columnValue.collation_name,
                primaryKeyColumn.key_ordinal
            FROM sys.tables tableValue
            INNER JOIN sys.schemas schemaValue
                ON schemaValue.schema_id = tableValue.schema_id
            INNER JOIN sys.columns columnValue
                ON columnValue.object_id = tableValue.object_id
            INNER JOIN sys.types typeValue
                ON typeValue.user_type_id = columnValue.user_type_id
            LEFT JOIN sys.indexes primaryKey
                ON primaryKey.object_id = tableValue.object_id
               AND primaryKey.is_primary_key = 1
            LEFT JOIN sys.index_columns primaryKeyColumn
                ON primaryKeyColumn.object_id = columnValue.object_id
               AND primaryKeyColumn.index_id = primaryKey.index_id
               AND primaryKeyColumn.column_id = columnValue.column_id
            WHERE schemaValue.name = @schema
            ORDER BY tableValue.name, columnValue.column_id;
            """;
        command.Parameters.Add(NameParameter("@schema", schema));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var dataType = reader.GetString(3);
            var sqlLength = reader.GetInt16(4);
            var maximumLength = string.Equals(
                dataType,
                "nvarchar",
                StringComparison.OrdinalIgnoreCase)
                ? sqlLength < 0 ? -1 : sqlLength / 2
                : sqlLength;
            columns.Add(new SqlModelColumn(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                dataType,
                maximumLength,
                reader.IsDBNull(5) ? null : reader.GetString(5),
                !reader.IsDBNull(6) && reader.GetByte(6) > 0));
        }

        return columns;
    }

    private static IReadOnlyList<SqlModelRelationship> ReadRelationships(
        SqlConnection connection,
        SqlTransaction? transaction,
        string schema)
    {
        var relationships = new List<SqlModelRelationship>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                foreignKey.name,
                sourceTable.name,
                sourceColumn.name,
                targetTable.name,
                targetColumn.name,
                foreignKeyColumn.constraint_column_id,
                sourceColumn.is_nullable,
                foreignKey.is_disabled,
                foreignKey.is_not_trusted
            FROM sys.foreign_keys foreignKey
            INNER JOIN sys.foreign_key_columns foreignKeyColumn
                ON foreignKeyColumn.constraint_object_id = foreignKey.object_id
            INNER JOIN sys.tables sourceTable
                ON sourceTable.object_id = foreignKey.parent_object_id
            INNER JOIN sys.schemas sourceSchema
                ON sourceSchema.schema_id = sourceTable.schema_id
            INNER JOIN sys.columns sourceColumn
                ON sourceColumn.object_id = foreignKeyColumn.parent_object_id
               AND sourceColumn.column_id = foreignKeyColumn.parent_column_id
            INNER JOIN sys.tables targetTable
                ON targetTable.object_id = foreignKey.referenced_object_id
            INNER JOIN sys.schemas targetSchema
                ON targetSchema.schema_id = targetTable.schema_id
            INNER JOIN sys.columns targetColumn
                ON targetColumn.object_id = foreignKeyColumn.referenced_object_id
               AND targetColumn.column_id = foreignKeyColumn.referenced_column_id
            WHERE sourceSchema.name = @schema
              AND targetSchema.name = @schema
            ORDER BY sourceTable.name, foreignKey.name,
                     foreignKeyColumn.constraint_column_id;
            """;
        command.Parameters.Add(NameParameter("@schema", schema));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            relationships.Add(new SqlModelRelationship(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8)));
        }

        return relationships;
    }

    private static void EnsureColumnContract(
        string entityName,
        IReadOnlyCollection<SqlModelColumn> columns,
        IReadOnlyCollection<SqlModelRelationship> relationships)
    {
        var names = new HashSet<string>(MetaName.Comparer);
        foreach (var column in columns)
        {
            var name = MetaName.Require(column.Name, "Column name.");
            if (!names.Add(name))
            {
                throw new InvalidOperationException(
                    $"Table '{entityName}' contains case-insensitive duplicate column '{name}'.");
            }
        }

        var idColumn = columns.SingleOrDefault(column =>
            MetaName.Comparer.Equals(column.Name, "Id")) ??
            throw new InvalidOperationException(
                $"Table '{entityName}' must contain required column 'Id'.");
        if (idColumn.IsNullable)
        {
            throw new InvalidOperationException(
                $"Table '{entityName}.Id' must be non-nullable.");
        }

        RequireNVarChar(
            entityName,
            idColumn,
            MetaIdentity.MaximumLength,
            SqlWorkspaceContract.CaseInsensitiveCollation);
        var primaryKeyColumns = columns
            .Where(column => column.IsPrimaryKey)
            .ToArray();
        if (primaryKeyColumns.Length != 1 ||
            !MetaName.Comparer.Equals(primaryKeyColumns[0].Name, "Id"))
        {
            throw new InvalidOperationException(
                $"Table '{entityName}' must use 'Id' as its single-column primary key.");
        }

        var relationshipColumns = relationships
            .Where(relationship => MetaName.Comparer.Equals(
                relationship.SourceTable,
                entityName))
            .Select(relationship => relationship.SourceColumn)
            .ToHashSet(MetaName.Comparer);
        foreach (var column in columns.Where(column =>
                     !MetaName.Comparer.Equals(column.Name, "Id")))
        {
            RequireNVarChar(
                entityName,
                column,
                relationshipColumns.Contains(column.Name)
                    ? MetaIdentity.MaximumLength
                    : -1,
                relationshipColumns.Contains(column.Name)
                    ? SqlWorkspaceContract.CaseInsensitiveCollation
                    : null);
        }
    }

    private static void EnsureSingleColumnRelationships(
        IReadOnlyCollection<SqlModelRelationship> relationships)
    {
        foreach (var group in relationships.GroupBy(
                     relationship =>
                         relationship.SourceTable + "\0" +
                         relationship.ConstraintName,
                     MetaName.Comparer))
        {
            if (group.Count() != 1 || group.Single().ConstraintColumnId != 1)
            {
                var relationship = group.First();
                throw new InvalidOperationException(
                    $"Foreign key '{relationship.ConstraintName}' on '{relationship.SourceTable}' must use one column.");
            }
        }
    }

    private static void EnsureRelationshipContract(
        SqlModelRelationship relationship)
    {
        if (relationship.IsDisabled || relationship.IsNotTrusted)
        {
            throw new InvalidOperationException(
                $"Foreign key '{relationship.ConstraintName}' must be enabled and trusted.");
        }

        if (!MetaName.Comparer.Equals(relationship.TargetColumn, "Id"))
        {
            throw new InvalidOperationException(
                $"Foreign key '{relationship.ConstraintName}' on '{relationship.SourceTable}.{relationship.SourceColumn}' must reference '{relationship.TargetTable}.Id'.");
        }

        if (!relationship.SourceColumn.EndsWith(
                "Id",
                StringComparison.OrdinalIgnoreCase) ||
            relationship.SourceColumn.Length <= 2)
        {
            throw new InvalidOperationException(
                $"Foreign key '{relationship.ConstraintName}' on '{relationship.SourceTable}.{relationship.SourceColumn}' must use an '<Role>Id' column name.");
        }
    }

    private static void RequireNVarChar(
        string entityName,
        SqlModelColumn column,
        int maximumLength,
        string? requiredCollation)
    {
        if (!string.Equals(
                column.DataType,
                "nvarchar",
                StringComparison.OrdinalIgnoreCase) ||
            column.MaximumLength != maximumLength ||
            requiredCollation != null && !string.Equals(
                column.Collation,
                requiredCollation,
                StringComparison.OrdinalIgnoreCase))
        {
            var length = maximumLength < 0
                ? "MAX"
                : maximumLength.ToString(CultureInfo.InvariantCulture);
            var collation = requiredCollation == null
                ? string.Empty
                : $" COLLATE {requiredCollation}";
            throw new InvalidOperationException(
                $"Table '{entityName}.{column.Name}' must use NVARCHAR({length}){collation}.");
        }
    }

    private static void EnsureValid(GenericModel model)
    {
        var diagnostics = WorkspaceValidator.Validate(
            model,
            new GenericInstance { ModelName = model.Name });
        if (!diagnostics.HasErrors)
        {
            return;
        }

        var errors = diagnostics.Issues
            .Where(issue => issue.Severity == IssueSeverity.Error)
            .Take(5)
            .Select(issue =>
                $"{issue.Code} {issue.Location} - {issue.Message}");
        throw new InvalidOperationException(
            "SQL workspace model is invalid. " + string.Join(" | ", errors));
    }

    private static SqlParameter NameParameter(string name, string value) =>
        new(name, SqlDbType.NVarChar, MetaName.MaximumLength)
        {
            Value = value,
        };

    private sealed record SqlModelColumn(
        string TableName,
        string Name,
        bool IsNullable,
        string DataType,
        int MaximumLength,
        string? Collation,
        bool IsPrimaryKey);

    private sealed record SqlModelRelationship(
        string ConstraintName,
        string SourceTable,
        string SourceColumn,
        string TargetTable,
        string TargetColumn,
        int ConstraintColumnId,
        bool IsNullable,
        bool IsDisabled,
        bool IsNotTrusted);
}
