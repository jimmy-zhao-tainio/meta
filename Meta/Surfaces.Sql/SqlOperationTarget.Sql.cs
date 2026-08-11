using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Surfaces.Sql;

internal sealed partial class SqlOperationTarget
{
    private const string IdentityCollation =
        SqlWorkspaceContract.CaseInsensitiveCollation;

    private int Execute(
        string sql,
        params SqlParameter[] parameters)
    {
        using var command = CreateCommand(sql, parameters);
        return command.ExecuteNonQuery();
    }

    private object? Scalar(
        string sql,
        params SqlParameter[] parameters)
    {
        using var command = CreateCommand(sql, parameters);
        return command.ExecuteScalar();
    }

    private SqlCommand CreateCommand(
        string sql,
        IReadOnlyCollection<SqlParameter> parameters)
    {
        var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private bool HasRows(string entityName)
    {
        var value = Scalar(
            $"SELECT TOP (1) 1 FROM {Table(entityName)};");
        return value != null && value != DBNull.Value;
    }

    private bool RecordExists(string entityName, string id)
    {
        return Convert.ToInt32(
                   Scalar(
                       $"SELECT COUNT_BIG(1) FROM {Table(entityName)} WHERE [Id] = @id;",
                       IdentityParameter("@id", id)),
                   CultureInfo.InvariantCulture) == 1;
    }

    private IReadOnlyList<string> LoadColumnNames(string entityName)
    {
        var names = new List<string>();
        using var command = CreateCommand(
            """
            SELECT columnValue.name
            FROM sys.columns columnValue
            INNER JOIN sys.tables tableValue
                ON tableValue.object_id = columnValue.object_id
            INNER JOIN sys.schemas schemaValue
                ON schemaValue.schema_id = tableValue.schema_id
            WHERE schemaValue.name = @schema
              AND tableValue.name = @table
            ORDER BY columnValue.column_id;
            """,
            [NameParameter("@schema", _schema), NameParameter("@table", entityName)]);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        if (names.Count == 0)
        {
            throw new InvalidOperationException(
                $"Entity '{entityName}' does not exist.");
        }

        return names;
    }

    private bool TableExists(string entityName)
    {
        return Scalar(
            """
            SELECT TOP (1) 1
            FROM sys.tables tableValue
            INNER JOIN sys.schemas schemaValue
                ON schemaValue.schema_id = tableValue.schema_id
            WHERE schemaValue.name = @schema
              AND tableValue.name = @table;
            """,
            NameParameter("@schema", _schema),
            NameParameter("@table", entityName)) != null;
    }

    private string RequirePrimaryKeyConstraint(string entityName)
    {
        var value = Scalar(
            """
            SELECT TOP (1) keyConstraint.name
            FROM sys.key_constraints keyConstraint
            INNER JOIN sys.tables tableValue
                ON tableValue.object_id = keyConstraint.parent_object_id
            INNER JOIN sys.schemas schemaValue
                ON schemaValue.schema_id = tableValue.schema_id
            WHERE keyConstraint.type = 'PK'
              AND schemaValue.name = @schema
              AND tableValue.name = @table;
            """,
            NameParameter("@schema", _schema),
            NameParameter("@table", entityName));
        return value is string name
            ? name
            : throw new InvalidOperationException(
                $"Entity '{entityName}' does not have a primary key.");
    }

    private bool SchemaObjectExists(string objectName)
    {
        return Scalar(
            """
            SELECT TOP (1) 1
            FROM sys.objects objectValue
            INNER JOIN sys.schemas schemaValue
                ON schemaValue.schema_id = objectValue.schema_id
            WHERE schemaValue.name = @schema
              AND objectValue.name = @name;
            """,
            NameParameter("@schema", _schema),
            NameParameter("@name", objectName)) != null;
    }

    private IReadOnlyList<SqlConstraintRename> PlanConstraintRenames(
        params (string CurrentName, string FinalName)[] constraints)
    {
        var currentNames = constraints
            .Select(constraint => MetaName.Require(
                constraint.CurrentName,
                "Current constraint name."))
            .ToArray();
        var finalNames = new HashSet<string>(MetaName.Comparer);
        foreach (var constraint in constraints)
        {
            var finalName = MetaName.Require(
                constraint.FinalName,
                "Final constraint name.");
            if (!finalNames.Add(finalName))
            {
                throw new InvalidOperationException(
                    $"Constraint name '{finalName}' is required more than once.");
            }

            if (SchemaObjectExists(finalName) &&
                !currentNames.Contains(finalName, MetaName.Comparer))
            {
                throw new InvalidOperationException(
                    $"SQL object '{finalName}' already exists in schema '{_schema}'.");
            }
        }

        var reservedNames = new HashSet<string>(
            currentNames.Concat(finalNames),
            MetaName.Comparer);
        return constraints
            .Select(constraint => new SqlConstraintRename(
                constraint.CurrentName,
                constraint.FinalName,
                CreateTemporaryConstraintName(reservedNames)))
            .ToArray();
    }

    private string CreateTemporaryConstraintName(
        ISet<string> reservedNames)
    {
        while (true)
        {
            var name = "MetaRename_" + Guid.NewGuid().ToString("N");
            if (reservedNames.Add(name) && !SchemaObjectExists(name))
            {
                return name;
            }
        }
    }

    private void RenameConstraint(string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return;
        }

        Execute(
            "EXEC sys.sp_rename @objectName, @newName, N'OBJECT';",
            TextParameter(
                "@objectName",
                Quote(_schema) + "." + Quote(oldName)),
            NameParameter("@newName", newName));
    }

    private IReadOnlyList<SqlRelationship> LoadRelationships(
        string? sourceEntityName = null,
        string? targetEntityName = null)
    {
        var relationships = new List<SqlRelationship>();
        var sourceFilter = sourceEntityName == null
            ? string.Empty
            : " AND sourceTable.name = @sourceTable";
        var targetFilter = targetEntityName == null
            ? string.Empty
            : " AND targetTable.name = @targetTable";
        var parameters = new List<SqlParameter>
        {
            NameParameter("@schema", _schema),
        };
        if (sourceEntityName != null)
        {
            parameters.Add(NameParameter("@sourceTable", sourceEntityName));
        }

        if (targetEntityName != null)
        {
            parameters.Add(NameParameter("@targetTable", targetEntityName));
        }

        using var command = CreateCommand(
            """
            SELECT
                foreignKey.name,
                sourceTable.name,
                sourceColumn.name,
                targetTable.name,
                targetColumn.name,
                sourceColumn.is_nullable
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
            """ + sourceFilter + targetFilter + " ORDER BY foreignKey.name;",
            parameters);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            relationships.Add(new SqlRelationship(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5)));
        }

        return relationships;
    }

    private SqlRelationship RequireRelationship(
        string sourceEntityName,
        string relationshipName)
    {
        var requiredSource = MetaName.Require(
            sourceEntityName,
            "Source entity name.");
        var requiredName = MetaName.Require(
            relationshipName,
            "Relationship name.");
        var matches = LoadRelationships(sourceEntityName: requiredSource)
            .Where(relationship =>
                MetaName.Comparer.Equals(
                    relationship.ColumnName,
                    requiredName) ||
                MetaName.Comparer.Equals(
                    RelationshipRole(relationship.ColumnName),
                    requiredName))
            .ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"Relationship '{requiredSource}.{requiredName}' does not exist."),
            _ => throw new InvalidOperationException(
                $"Relationship '{requiredSource}.{requiredName}' is ambiguous."),
        };
    }

    private void RequireProperty(
        string entityName,
        string propertyName)
    {
        var requiredEntity = MetaName.Require(entityName, "Entity name.");
        var requiredProperty = MetaName.Require(propertyName, "Property name.");
        if (MetaName.Comparer.Equals(requiredProperty, "Id"))
        {
            throw new InvalidOperationException(
                "Property 'Id' is implicit and cannot be changed as a property.");
        }

        if (!LoadColumnNames(requiredEntity).Any(column =>
                MetaName.Comparer.Equals(column, requiredProperty)))
        {
            throw new InvalidOperationException(
                $"Property '{requiredEntity}.{requiredProperty}' does not exist.");
        }

        if (LoadRelationships(sourceEntityName: requiredEntity).Any(relationship =>
                MetaName.Comparer.Equals(
                    relationship.ColumnName,
                    requiredProperty)))
        {
            throw new InvalidOperationException(
                $"'{requiredEntity}.{requiredProperty}' is a relationship, not a property.");
        }
    }

    private string Table(string entityName)
    {
        return Quote(_schema) + "." +
               Quote(MetaName.Require(entityName, "Entity name."));
    }

    private static string Quote(string name)
    {
        return "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    private static string RelationshipRole(string columnName)
    {
        return columnName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
               columnName.Length > 2
            ? columnName[..^2]
            : columnName;
    }

    private static SqlParameter NameParameter(string name, string value)
    {
        return new SqlParameter(name, SqlDbType.NVarChar, MetaName.MaximumLength)
        {
            Value = value,
        };
    }

    private static SqlParameter IdentityParameter(string name, string value)
    {
        return new SqlParameter(name, SqlDbType.NVarChar, MetaIdentity.MaximumLength)
        {
            Value = MetaIdentity.Require(value, "Record identity."),
        };
    }

    private static SqlParameter TextParameter(string name, string value)
    {
        return new SqlParameter(name, SqlDbType.NVarChar, -1)
        {
            Value = value,
        };
    }

    private sealed record SqlRelationship(
        string ConstraintName,
        string SourceEntityName,
        string ColumnName,
        string TargetEntityName,
        string TargetColumnName,
        bool IsNullable);

    private sealed record SqlConstraintRename(
        string CurrentName,
        string FinalName,
        string TemporaryName);
}
