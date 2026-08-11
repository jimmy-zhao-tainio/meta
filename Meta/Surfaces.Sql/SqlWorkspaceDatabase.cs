using System.Data;
using Microsoft.Data.SqlClient;
using Meta.Operations.Domain;

namespace Meta.Surfaces.Sql;

internal static class SqlWorkspaceDatabase
{
    public static async Task<string> CreateAsync(
        string connectionString,
        string modelName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var expectedModelName = MetaName.Require(modelName, "Model name.");
        var target = new SqlConnectionStringBuilder(connectionString);
        var databaseName = MetaName.Require(
            target.InitialCatalog,
            "SQL workspace database name.");
        if (!MetaName.Comparer.Equals(databaseName, expectedModelName))
        {
            throw new InvalidOperationException(
                $"SQL workspace database '{databaseName}' does not match model '{expectedModelName}'.");
        }

        var admin = new SqlConnectionStringBuilder(target.ConnectionString)
        {
            InitialCatalog = "master",
        };
        await using var connection = new SqlConnection(admin.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (await ExistsAsync(connection, databaseName, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"SQL workspace database '{databaseName}' already exists.");
        }

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 300;
        command.CommandText = $"CREATE DATABASE {Quote(databaseName)};";
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        return databaseName;
    }

    public static async Task DropIfExistsAsync(
        string connectionString,
        string databaseName,
        CancellationToken cancellationToken)
    {
        var name = MetaName.Require(databaseName, "SQL workspace database name.");
        var target = new SqlConnectionStringBuilder(connectionString);
        using (var pooledConnection = new SqlConnection(target.ConnectionString))
        {
            SqlConnection.ClearPool(pooledConnection);
        }

        target.InitialCatalog = "master";
        await using var connection = new SqlConnection(target.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (!await ExistsAsync(connection, name, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 300;
        command.CommandText =
            $"ALTER DATABASE {Quote(name)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE {Quote(name)};";
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<bool> ExistsAsync(
        SqlConnection connection,
        string databaseName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN DB_ID(@databaseName) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.Add(new SqlParameter(
            "@databaseName",
            SqlDbType.NVarChar,
            MetaName.MaximumLength)
        {
            Value = databaseName,
        });
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false)) == 1;
    }

    private static string Quote(string name) =>
        "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
