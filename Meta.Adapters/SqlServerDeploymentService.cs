using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Meta.Adapters;

public sealed class SqlServerDeploymentService
{
    private static readonly Regex GoBatchPattern = new(@"^\s*GO\s*(?:--.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreateTablePattern = new(@"CREATE\s+TABLE\s+\[(?<schema>[^\]]+)\]\.\[(?<name>[^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReferencesPattern = new(@"REFERENCES\s+\[(?<schema>[^\]]+)\]\.\[(?<name>[^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InsertIntoPattern = new(@"INSERT\s+INTO\s+\[(?<schema>[^\]]+)\]\.\[(?<name>[^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        var scriptFiles = ResolveScriptFiles(scriptsDirectory);
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

    static string[] ResolveScriptFiles(string scriptsDirectory)
    {
        var allFiles = Directory.GetFiles(scriptsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var descriptors = allFiles.Select(path => SqlScriptDescriptor.Create(path, File.ReadAllText(path))).ToList();
        var providersByTable = descriptors
            .SelectMany(descriptor => descriptor.ProvidedTables.Select(table => (table, descriptor)))
            .GroupBy(item => item.table, item => item.descriptor, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase).First(), StringComparer.OrdinalIgnoreCase);

        var dependencies = descriptors.ToDictionary(
            descriptor => descriptor.FilePath,
            descriptor => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptors)
        {
            foreach (var requiredTable in descriptor.RequiredTables)
            {
                if (!providersByTable.TryGetValue(requiredTable, out var provider))
                {
                    continue;
                }

                if (!string.Equals(provider.FilePath, descriptor.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    dependencies[descriptor.FilePath].Add(provider.FilePath);
                }
            }
        }

        return TopologicallySort(descriptors, dependencies).Select(item => item.FilePath).ToArray();
    }

    static IReadOnlyList<SqlScriptDescriptor> TopologicallySort(
        IReadOnlyList<SqlScriptDescriptor> descriptors,
        IReadOnlyDictionary<string, HashSet<string>> dependencies)
    {
        var remaining = descriptors.ToDictionary(item => item.FilePath, item => new HashSet<string>(dependencies[item.FilePath], StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var queue = new List<SqlScriptDescriptor>();
        var ordered = new List<SqlScriptDescriptor>();

        void RebuildQueue()
        {
            queue.Clear();
            queue.AddRange(descriptors.Where(item => remaining.TryGetValue(item.FilePath, out var deps) && deps.Count == 0));
            queue.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.FileName, right.FileName));
        }

        RebuildQueue();
        while (queue.Count > 0)
        {
            var next = queue[0];
            queue.RemoveAt(0);
            ordered.Add(next);
            remaining.Remove(next.FilePath);

            foreach (var deps in remaining.Values)
            {
                deps.Remove(next.FilePath);
            }

            RebuildQueue();
        }

        if (remaining.Count > 0)
        {
            var cycleFiles = remaining.Keys
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
            throw new InvalidOperationException($"Could not determine SQL deployment order because of cyclic or unresolved dependencies among: {string.Join(", ", cycleFiles)}.");
        }

        return ordered;
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
        return GoBatchPattern.Split(scriptText)
            .Select(batch => batch.Trim())
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();
    }

    private sealed record SqlScriptDescriptor(
        string FilePath,
        string FileName,
        IReadOnlyCollection<string> ProvidedTables,
        IReadOnlyCollection<string> RequiredTables)
    {
        public static SqlScriptDescriptor Create(string filePath, string scriptText)
        {
            var fileName = Path.GetFileName(filePath);
            var providedTables = CreateTablePattern.Matches(scriptText)
                .Select(match => $"{match.Groups["schema"].Value}.{match.Groups["name"].Value}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var referencedTables = ReferencesPattern.Matches(scriptText)
                .Select(match => $"{match.Groups["schema"].Value}.{match.Groups["name"].Value}");
            var insertedTables = InsertIntoPattern.Matches(scriptText)
                .Select(match => $"{match.Groups["schema"].Value}.{match.Groups["name"].Value}");
            var requiredTables = referencedTables
                .Concat(insertedTables)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new SqlScriptDescriptor(filePath, fileName, providedTables, requiredTables);
        }
    }
}

public readonly record struct SqlServerDeploymentResult(int ScriptFileCount, int BatchCount, string DatabaseName);
