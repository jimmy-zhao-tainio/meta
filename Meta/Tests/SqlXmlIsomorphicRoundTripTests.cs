using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Meta.Adapters;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class SqlXmlIsomorphicRoundTripTests
{
    [Fact]
    public async Task XmlSqlXml_RoundTrip_IsByteIdentical_ForCanonicalMetadata()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            return;
        }

        var repoRoot = FindRepositoryRoot();
        var sourceInputRoot = Path.Combine(repoRoot, "Samples", "MainWorkspace");
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-sql-roundtrip", Guid.NewGuid().ToString("N"));
        var leftWorkspaceRoot = Path.Combine(tempRoot, "left");
        var rightWorkspaceRoot = Path.Combine(tempRoot, "right");
        var sqlOutRoot = Path.Combine(tempRoot, "sql");
        var databaseName = "MetaRt" + Guid.NewGuid().ToString("N")[..20];

        try
        {
            Directory.CreateDirectory(tempRoot);

            var services = new ServiceCollection();
            var sourceWorkspace = await services.WorkspaceService.LoadAsync(sourceInputRoot);

            // Keep database name and model name aligned so SQL import produces the same model name.
            sourceWorkspace.Model.Name = databaseName;
            sourceWorkspace.Instance.ModelName = databaseName;
            sourceWorkspace.WorkspaceRootPath = leftWorkspaceRoot;
            sourceWorkspace.MetadataRootPath = string.Empty;
            sourceWorkspace.IsDirty = true;
            await services.WorkspaceService.SaveAsync(sourceWorkspace);

            GenerationService.GenerateSql(sourceWorkspace, sqlOutRoot);
            await RecreateDatabaseFromScriptsAsync(
                baseConnectionString,
                databaseName,
                Path.Combine(sqlOutRoot, "schema.sql"),
                Path.Combine(sqlOutRoot, "data.sql"));

            var databaseConnectionString = new SqlConnectionStringBuilder(baseConnectionString)
            {
                InitialCatalog = databaseName,
            }.ConnectionString;

            var importedWorkspace = await services.ImportService
                .ImportSqlAsync(databaseConnectionString, "dbo");
            importedWorkspace.WorkspaceRootPath = rightWorkspaceRoot;
            importedWorkspace.MetadataRootPath = string.Empty;
            importedWorkspace.IsDirty = true;
            await services.WorkspaceService.SaveAsync(importedWorkspace);

            AssertMetadataTreesAreByteIdentical(
                Path.Combine(leftWorkspaceRoot, "metadata"),
                Path.Combine(rightWorkspaceRoot, "metadata"));
        }
        finally
        {
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private static async Task<string?> ResolveSqlTestConnectionStringAsync()
    {
        var candidates = new List<string>();
        var envOverride = Environment.GetEnvironmentVariable("Meta_SQL_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            candidates.Add(envOverride.Trim());
        }

        candidates.Add("Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=True;Encrypt=False");

        foreach (var candidate in candidates)
        {
            if (await CanOpenMasterAsync(candidate).ConfigureAwait(false))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<bool> CanOpenMasterAsync(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "master",
            };

            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task RecreateDatabaseFromScriptsAsync(
        string baseConnectionString,
        string databaseName,
        string schemaScriptPath,
        string dataScriptPath)
    {
        var schemaScript = await File.ReadAllTextAsync(schemaScriptPath).ConfigureAwait(false);
        var dataScript = await File.ReadAllTextAsync(dataScriptPath).ConfigureAwait(false);
        var escapedLiteral = databaseName.Replace("'", "''", StringComparison.Ordinal);
        var escapedIdentifier = databaseName.Replace("]", "]]", StringComparison.Ordinal);

        var masterBuilder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = "master",
        };

        await using (var masterConnection = new SqlConnection(masterBuilder.ConnectionString))
        {
            await masterConnection.OpenAsync().ConfigureAwait(false);
            var sql =
                $"IF DB_ID(N'{escapedLiteral}') IS NOT NULL BEGIN ALTER DATABASE [{escapedIdentifier}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{escapedIdentifier}]; END; CREATE DATABASE [{escapedIdentifier}];";
            await using var command = new SqlCommand(sql, masterConnection)
            {
                CommandTimeout = 300,
            };
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var databaseBuilder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = databaseName,
        };

        await using var databaseConnection = new SqlConnection(databaseBuilder.ConnectionString);
        await databaseConnection.OpenAsync().ConfigureAwait(false);
        foreach (var batch in SplitSqlBatches(schemaScript))
        {
            await using var command = new SqlCommand(batch, databaseConnection)
            {
                CommandTimeout = 300,
            };
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        foreach (var batch in SplitSqlBatches(dataScript))
        {
            await using var command = new SqlCommand(batch, databaseConnection)
            {
                CommandTimeout = 300,
            };
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static async Task DropDatabaseIfExistsAsync(string baseConnectionString, string databaseName)
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString) || string.IsNullOrWhiteSpace(databaseName))
        {
            return;
        }

        try
        {
            var escapedLiteral = databaseName.Replace("'", "''", StringComparison.Ordinal);
            var escapedIdentifier = databaseName.Replace("]", "]]", StringComparison.Ordinal);
            var masterBuilder = new SqlConnectionStringBuilder(baseConnectionString)
            {
                InitialCatalog = "master",
            };

            await using var masterConnection = new SqlConnection(masterBuilder.ConnectionString);
            await masterConnection.OpenAsync().ConfigureAwait(false);
            var dropSql =
                $"IF DB_ID(N'{escapedLiteral}') IS NOT NULL BEGIN ALTER DATABASE [{escapedIdentifier}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{escapedIdentifier}]; END;";
            await using var dropCommand = new SqlCommand(dropSql, masterConnection)
            {
                CommandTimeout = 300,
            };
            await dropCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup for test resources.
        }
    }

    private static IReadOnlyList<string> SplitSqlBatches(string script)
    {
        var batches = new List<string>();
        using var reader = new StringReader(script ?? string.Empty);
        var current = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (string.Equals(line.Trim(), "GO", StringComparison.OrdinalIgnoreCase))
            {
                var batch = string.Join('\n', current).Trim();
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    batches.Add(batch);
                }

                current.Clear();
                continue;
            }

            current.Add(line);
        }

        var finalBatch = string.Join('\n', current).Trim();
        if (!string.IsNullOrWhiteSpace(finalBatch))
        {
            batches.Add(finalBatch);
        }

        return batches;
    }

    private static void AssertMetadataTreesAreByteIdentical(string expectedMetadataRoot, string actualMetadataRoot)
    {
        var expected = ReadMetadataFileBytes(expectedMetadataRoot);
        var actual = ReadMetadataFileBytes(actualMetadataRoot);

        var expectedPaths = expected.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var actualPaths = actual.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedPaths, actualPaths);

        foreach (var path in expectedPaths)
        {
            var expectedBytes = expected[path];
            var actualBytes = actual[path];
            Assert.True(
                expectedBytes.AsSpan().SequenceEqual(actualBytes),
                $"Metadata file bytes differ for '{path}'.");
        }
    }

    private static Dictionary<string, byte[]> ReadMetadataFileBytes(string metadataRoot)
    {
        var root = Path.GetFullPath(metadataRoot);
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "Metadata.Framework.sln")))
            {
                return directory;
            }

            var parent = Directory.GetParent(directory);
            if (parent == null)
            {
                break;
            }

            directory = parent.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

