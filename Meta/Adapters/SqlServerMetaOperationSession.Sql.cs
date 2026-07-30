using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Meta.Core.Ddl;

namespace Meta.Adapters;

public sealed partial class SqlServerMetaOperationSession
{
    private async Task<string> RequireActualIdAsync(
        string entityName,
        string suppliedId,
        CancellationToken cancellationToken)
    {
        var id = RequireIdentity(suppliedId, nameof(suppliedId));
        return await FindActualIdAsync(
                   entityName,
                   id,
                   cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException(
                   $"Entity '{entityName}' does not contain record '{id}'.");
    }

    private async Task<string?> FindActualIdAsync(
        string entityName,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            "SELECT TOP (2) [Id] " +
            $"FROM {QualifiedTable(entityName)} WITH (UPDLOCK, HOLDLOCK) " +
            $"WHERE [Id] COLLATE {MetaSqlStorageContract.IdentityCollation} = " +
            $"@id COLLATE {MetaSqlStorageContract.IdentityCollation};");
        command.Parameters.Add(
            new SqlParameter("@id", SqlDbType.NVarChar, 128)
            {
                Value = id,
            });

        var matches = new List<string>(2);
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            matches.Add(reader.GetString(0));
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Entity '{entityName}' contains more than one case-insensitive match for Id '{id}'."),
        };
    }

    private async Task<bool> HasRowsAsync(
        string entityName,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            $"SELECT CASE WHEN EXISTS (" +
            $"SELECT 1 FROM {QualifiedTable(entityName)}) " +
            "THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;");
        return Convert.ToBoolean(
            await command.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private async Task<long> CountNullsAsync(
        string entityName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            $"SELECT COUNT_BIG(*) FROM {QualifiedTable(entityName)} " +
            $"WHERE {QuoteIdentifier(columnName)} IS NULL;");
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private Task AlterPropertyNullabilityAsync(
        string entityName,
        string propertyName,
        bool isRequired,
        CancellationToken cancellationToken)
    {
        return ExecuteNonQueryAsync(
            $"ALTER TABLE {QualifiedTable(entityName)} ALTER COLUMN " +
            $"{QuoteIdentifier(propertyName)} NVARCHAR(MAX) " +
            $"{(isRequired ? "NOT NULL" : "NULL")};",
            cancellationToken);
    }

    private Task AlterRelationshipNullabilityAsync(
        string entityName,
        string columnName,
        bool isRequired,
        CancellationToken cancellationToken)
    {
        return ExecuteNonQueryAsync(
            $"ALTER TABLE {QualifiedTable(entityName)} ALTER COLUMN " +
            $"{QuoteIdentifier(columnName)} NVARCHAR(128) COLLATE " +
            $"{MetaSqlStorageContract.IdentityCollation} " +
            $"{(isRequired ? "NOT NULL" : "NULL")};",
            cancellationToken);
    }

    private async Task<string> RequireForeignKeyConstraintNameAsync(
        string sourceEntityName,
        string sourceColumnName,
        string targetEntityName,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            """
            SELECT foreignKey.name
            FROM sys.foreign_keys AS foreignKey
            INNER JOIN sys.foreign_key_columns AS foreignKeyColumn
                ON foreignKeyColumn.constraint_object_id = foreignKey.object_id
            INNER JOIN sys.tables AS sourceTable
                ON sourceTable.object_id = foreignKey.parent_object_id
            INNER JOIN sys.schemas AS sourceSchema
                ON sourceSchema.schema_id = sourceTable.schema_id
            INNER JOIN sys.columns AS sourceColumn
                ON sourceColumn.object_id = foreignKeyColumn.parent_object_id
                AND sourceColumn.column_id = foreignKeyColumn.parent_column_id
            INNER JOIN sys.tables AS targetTable
                ON targetTable.object_id = foreignKey.referenced_object_id
            INNER JOIN sys.schemas AS targetSchema
                ON targetSchema.schema_id = targetTable.schema_id
            INNER JOIN sys.columns AS targetColumn
                ON targetColumn.object_id = foreignKeyColumn.referenced_object_id
                AND targetColumn.column_id = foreignKeyColumn.referenced_column_id
            WHERE sourceSchema.name = @schema
              AND targetSchema.name = @schema
              AND sourceTable.name = @sourceTable
              AND sourceColumn.name = @sourceColumn
              AND targetTable.name = @targetTable
              AND targetColumn.name = N'Id';
            """);
        command.Parameters.Add(
            new SqlParameter("@schema", SqlDbType.NVarChar, 128)
            {
                Value = _schema,
            });
        command.Parameters.Add(
            new SqlParameter("@sourceTable", SqlDbType.NVarChar, 128)
            {
                Value = sourceEntityName,
            });
        command.Parameters.Add(
            new SqlParameter("@sourceColumn", SqlDbType.NVarChar, 128)
            {
                Value = sourceColumnName,
            });
        command.Parameters.Add(
            new SqlParameter("@targetTable", SqlDbType.NVarChar, 128)
            {
                Value = targetEntityName,
            });

        var matches = new List<string>(2);
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            matches.Add(reader.GetString(0));
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"Relationship constraint for '{sourceEntityName}.{sourceColumnName}' does not exist."),
            _ => throw new InvalidOperationException(
                $"Relationship '{sourceEntityName}.{sourceColumnName}' has more than one foreign key constraint."),
        };
    }

    private SqlCommand CreateCommand(string commandText)
    {
        var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = commandText;
        command.CommandTimeout = 300;
        return command;
    }

    private async Task ExecuteNonQueryAsync(
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(commandText);
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private string QualifiedTable(string entityName)
    {
        return $"{QuoteIdentifier(_schema)}.{QuoteIdentifier(entityName)}";
    }

    private static void EnsureSingleAffectedRow(
        int affected,
        string entityName,
        string id)
    {
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Expected one '{entityName}' record for Id '{id}', but changed {affected}.");
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static string NormalizeIdentifier(string identifier)
    {
        const int maxIdentifierLength = 128;
        if (identifier.Length <= maxIdentifierLength)
        {
            return identifier;
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(identifier));
        var hash = Convert.ToHexString(hashBytes)[..16];
        var prefixLength = maxIdentifierLength - 1 - hash.Length;
        return identifier[..prefixLength] + "_" + hash;
    }
}
