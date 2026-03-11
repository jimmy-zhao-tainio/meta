using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Meta.Adapters;

public sealed class SqlServerDeploymentService
{
    public async Task<SqlServerDeploymentResult> DeployAsync(string scriptsDirectory, string connectionString, string? databaseName = null)
    {
        if (string.IsNullOrWhiteSpace(scriptsDirectory))
        {
            throw new ArgumentException("scripts directory is required.", nameof(scriptsDirectory));
        }

        if (!Directory.Exists(scriptsDirectory))
        {
            throw new DirectoryNotFoundException($"Scripts directory '{scriptsDirectory}' was not found.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("connection string is required.", nameof(connectionString));
        }

        var scriptFiles = Directory.GetFiles(scriptsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (scriptFiles.Length == 0)
        {
            throw new InvalidOperationException($"No .sql files found in '{scriptsDirectory}'.");
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrWhiteSpace(databaseName))
        {
            await EnsureDatabaseExistsAsync(builder, databaseName).ConfigureAwait(false);
            builder.InitialCatalog = databaseName;
        }

        var executedBatches = 0;
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        foreach (var scriptFile in scriptFiles)
        {
            var scriptText = await File.ReadAllTextAsync(scriptFile).ConfigureAwait(false);
            foreach (var batch in SplitGoBatches(scriptText))
            {
                await using var command = connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = batch;
                command.CommandTimeout = 0;
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                executedBatches++;
            }
        }

        return new SqlServerDeploymentResult(scriptFiles.Length, executedBatches, builder.InitialCatalog);
    }

    static async Task EnsureDatabaseExistsAsync(SqlConnectionStringBuilder builder, string databaseName)
    {
        var adminBuilder = new SqlConnectionStringBuilder(builder.ConnectionString)
        {
            InitialCatalog = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "master" : builder.InitialCatalog,
        };

        await using var connection = new SqlConnection(adminBuilder.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 0;
        command.CommandText = @"
IF DB_ID(@databaseName) IS NULL
BEGIN
    DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@databaseName);
    EXEC(@sql);
END";
        command.Parameters.Add(new SqlParameter("@databaseName", SqlDbType.NVarChar, 128) { Value = databaseName });
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    static string[] SplitGoBatches(string scriptText)
    {
        return Regex.Split(scriptText, @"^\s*GO\s*(?:--.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Select(batch => batch.Trim())
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();
    }
}

public readonly record struct SqlServerDeploymentResult(int ScriptFileCount, int BatchCount, string DatabaseName);
