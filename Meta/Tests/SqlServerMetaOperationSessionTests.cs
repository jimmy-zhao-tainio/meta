using Microsoft.Data.SqlClient;
using Meta.Adapters;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Services;
using MetaWorkspace = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace Meta.Core.Tests;

public sealed class SqlServerMetaOperationSessionTests
{
    [Fact]
    public async Task SqlSession_RejectsDatabaseThatIsNotEncodedMetaWorkspace()
    {
        var baseConnectionString = await RequireSqlTestConnectionStringAsync();
        var databaseName = "MetaOps" + Guid.NewGuid().ToString("N")[..20];

        try
        {
            await CreateDatabaseAsync(baseConnectionString, databaseName);
            var databaseConnectionString = ForDatabase(
                baseConnectionString,
                databaseName);
            await using (var connection = new SqlConnection(
                             databaseConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "CREATE TABLE [dbo].[Thing] (" +
                    "[Id] INT NOT NULL PRIMARY KEY, " +
                    "[Name] NVARCHAR(50) NULL); " +
                    "INSERT INTO [dbo].[Thing] ([Id], [Name]) " +
                    "VALUES (1, N'One');";
                await command.ExecuteNonQueryAsync();
            }

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SqlServerMetaOperationSession.OpenExistingAsync(
                    databaseConnectionString));
            Assert.Contains(
                "encoded Meta workspace",
                exception.Message,
                StringComparison.Ordinal);

            var imported = await new ImportService(new WorkspaceService())
                .ImportSqlAsync(databaseConnectionString, "dbo");
            var thing = Assert.Single(
                imported.Instance.RecordsByEntity["Thing"]);
            Assert.Equal("1", thing.Id);
            Assert.Equal("One", thing.Values["Name"]);
        }
        finally
        {
            await DropDatabaseIfExistsAsync(
                baseConnectionString,
                databaseName);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqlSession_ProducesReferenceInterpreterState(
        bool schemaRefactors)
    {
        var baseConnectionString = await RequireSqlTestConnectionStringAsync();
        var databaseName = "MetaOps" + Guid.NewGuid().ToString("N")[..20];
        var outputRoot = CreateTempDirectory();

        try
        {
            var source = MetaOperationInterpreterTests.BuildState(databaseName);
            await DeployStateAsync(
                source,
                outputRoot,
                baseConnectionString,
                databaseName);
            var plan = schemaRefactors
                ? MetaOperationInterpreterTests.BuildSchemaRefactorPlan()
                : MetaOperationInterpreterTests.BuildPlan();
            var expected = new MetaOperationInterpreter()
                .Apply(source, plan)
                .State;
            var databaseConnectionString = ForDatabase(
                baseConnectionString,
                databaseName);

            await using (var session =
                         await SqlServerMetaOperationSession.OpenExistingAsync(
                             databaseConnectionString))
            {
                var result = await session.ApplyAsync(plan);
                Assert.Equal(
                    plan.Operations.Count,
                    result.AppliedOperationCount);
                await session.CommitAsync();
            }

            var imported = await new ImportService(new WorkspaceService())
                .ImportSqlAsync(databaseConnectionString, "dbo");
            Assert.Equal(
                MetaOperationInterpreterTests.Canonicalize(expected),
                MetaOperationInterpreterTests.Canonicalize(
                    GenericMetadataState.Capture(imported)));
        }
        finally
        {
            await DropDatabaseIfExistsAsync(
                baseConnectionString,
                databaseName);
            DeleteDirectoryIfExists(outputRoot);
        }
    }

    [Fact]
    public async Task SqlSession_RejectedPlanRollsBackToItsSavepoint()
    {
        var baseConnectionString = await RequireSqlTestConnectionStringAsync();
        var databaseName = "MetaOps" + Guid.NewGuid().ToString("N")[..20];
        var outputRoot = CreateTempDirectory();

        try
        {
            var source = MetaOperationInterpreterTests.BuildState(databaseName);
            await DeployStateAsync(
                source,
                outputRoot,
                baseConnectionString,
                databaseName);
            var databaseConnectionString = ForDatabase(
                baseConnectionString,
                databaseName);

            await using (var session =
                         await SqlServerMetaOperationSession.OpenExistingAsync(
                             databaseConnectionString))
            {
                await session.ApplyAsync(MetaOperationPlan.Create(
                    new SetPropertyOperation(
                        "Person",
                        "person-a",
                        "LegacyName",
                        "Accepted before failure")));

                var rejected = MetaOperationPlan.Create(
                    new SetPropertyOperation(
                        "Person",
                        "person-a",
                        "LegacyName",
                        "Must roll back"),
                    new InsertRecordOperation(
                        "Person",
                        "PERSON-A",
                        new Dictionary<string, string>
                        {
                            ["LegacyName"] = "Duplicate",
                        }));

                var exception = await Assert.ThrowsAsync<MetaOperationException>(
                    () => session.ApplyAsync(rejected));
                Assert.Equal(1, exception.OperationIndex);
                Assert.IsType<InsertRecordOperation>(exception.Operation);

                await session.CommitAsync();
            }

            var imported = await new ImportService(new WorkspaceService())
                .ImportSqlAsync(databaseConnectionString, "dbo");
            var person = Assert.Single(
                imported.Instance.RecordsByEntity["Person"]);
            Assert.Equal(
                "Accepted before failure",
                person.Values["LegacyName"]);
        }
        finally
        {
            await DropDatabaseIfExistsAsync(
                baseConnectionString,
                databaseName);
            DeleteDirectoryIfExists(outputRoot);
        }
    }

    [Fact]
    public async Task SqlSession_RejectedSchemaPlanRollsBackModelAndDatabase()
    {
        var baseConnectionString = await RequireSqlTestConnectionStringAsync();
        var databaseName = "MetaOps" + Guid.NewGuid().ToString("N")[..20];
        var outputRoot = CreateTempDirectory();

        try
        {
            var source = MetaOperationInterpreterTests.BuildState(databaseName);
            await DeployStateAsync(
                source,
                outputRoot,
                baseConnectionString,
                databaseName);
            var databaseConnectionString = ForDatabase(
                baseConnectionString,
                databaseName);

            await using (var session =
                         await SqlServerMetaOperationSession.OpenExistingAsync(
                             databaseConnectionString))
            {
                var rejected = MetaOperationPlan.Create(
                    new AddPropertyOperation(
                        "Person",
                        "Pending",
                        isRequired: false),
                    new AddPropertyOperation(
                        "Person",
                        "Pending",
                        isRequired: false));

                var exception = await Assert.ThrowsAsync<MetaOperationException>(
                    () => session.ApplyAsync(rejected));
                Assert.Equal(1, exception.OperationIndex);
                Assert.DoesNotContain(
                    session.SnapshotModel()
                        .FindEntity("Person")!
                        .Properties,
                    property => property.Name == "Pending");

                await session.CommitAsync();
            }

            var imported = await new ImportService(new WorkspaceService())
                .ImportSqlAsync(databaseConnectionString, "dbo");
            Assert.DoesNotContain(
                imported.Model.FindEntity("Person")!.Properties,
                property => property.Name == "Pending");
        }
        finally
        {
            await DropDatabaseIfExistsAsync(
                baseConnectionString,
                databaseName);
            DeleteDirectoryIfExists(outputRoot);
        }
    }

    [Fact]
    public async Task SqlSession_RejectsRequiredRelationshipCycleAndRollsBack()
    {
        var baseConnectionString = await RequireSqlTestConnectionStringAsync();
        var databaseName = "MetaOps" + Guid.NewGuid().ToString("N")[..20];
        var outputRoot = CreateTempDirectory();

        try
        {
            var source = MetaOperationInterpreterTests.BuildState(databaseName);
            await DeployStateAsync(
                source,
                outputRoot,
                baseConnectionString,
                databaseName);
            var databaseConnectionString = ForDatabase(
                baseConnectionString,
                databaseName);

            await using (var session =
                         await SqlServerMetaOperationSession.OpenExistingAsync(
                             databaseConnectionString))
            {
                var exception = await Assert.ThrowsAsync<MetaOperationException>(
                    () => session.ApplyAsync(MetaOperationPlan.Create(
                        new AddRelationshipOperation(
                            "Person",
                            "Person",
                            "Manager",
                            isRequired: true,
                            existingRecordTargetId: "person-a"))));
                Assert.Equal(-1, exception.OperationIndex);
                Assert.Null(exception.Operation);
                Assert.NotNull(exception.Diagnostics);
                Assert.Contains(
                    exception.Diagnostics.Issues,
                    issue => issue.Code == "relationship.cycle");
                Assert.DoesNotContain(
                    session.SnapshotModel()
                        .FindEntity("Person")!
                        .Relationships,
                    relationship =>
                        relationship.GetColumnName() == "ManagerId");

                await session.CommitAsync();
            }

            var imported = await new ImportService(new WorkspaceService())
                .ImportSqlAsync(databaseConnectionString, "dbo");
            Assert.DoesNotContain(
                imported.Model.FindEntity("Person")!.Relationships,
                relationship =>
                    relationship.GetColumnName() == "ManagerId");
        }
        finally
        {
            await DropDatabaseIfExistsAsync(
                baseConnectionString,
                databaseName);
            DeleteDirectoryIfExists(outputRoot);
        }
    }

    [Fact]
    public async Task SqlSession_RejectsIdentityOutsideSqlRepresentation()
    {
        var baseConnectionString = await RequireSqlTestConnectionStringAsync();
        var databaseName = "MetaOps" + Guid.NewGuid().ToString("N")[..20];
        var outputRoot = CreateTempDirectory();

        try
        {
            var source = MetaOperationInterpreterTests.BuildState(databaseName);
            await DeployStateAsync(
                source,
                outputRoot,
                baseConnectionString,
                databaseName);
            var databaseConnectionString = ForDatabase(
                baseConnectionString,
                databaseName);

            await using var session =
                await SqlServerMetaOperationSession.OpenExistingAsync(
                    databaseConnectionString);
            var exception = await Assert.ThrowsAsync<MetaOperationException>(
                () => session.ApplyAsync(MetaOperationPlan.Create(
                    new InsertRecordOperation(
                        "Person",
                        "fullwidth-\uFF21",
                        new Dictionary<string, string>
                        {
                            ["LegacyName"] = "Rejected",
                        }))));
            Assert.Contains(
                "printable ASCII",
                exception.Message,
                StringComparison.Ordinal);
            await session.CommitAsync();
        }
        finally
        {
            await DropDatabaseIfExistsAsync(
                baseConnectionString,
                databaseName);
            DeleteDirectoryIfExists(outputRoot);
        }
    }

    [Fact]
    public async Task SqlSession_RejectsMissingIdentityConstraint()
    {
        var baseConnectionString = await RequireSqlTestConnectionStringAsync();
        var databaseName = "MetaOps" + Guid.NewGuid().ToString("N")[..20];
        var outputRoot = CreateTempDirectory();

        try
        {
            var source = MetaOperationInterpreterTests.BuildState(databaseName);
            await DeployStateAsync(
                source,
                outputRoot,
                baseConnectionString,
                databaseName);
            var databaseConnectionString = ForDatabase(
                baseConnectionString,
                databaseName);
            await using (var connection = new SqlConnection(
                             databaseConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "ALTER TABLE [dbo].[Person] DROP CONSTRAINT " +
                    "[CK_Person_Id_MetaIdentity];";
                await command.ExecuteNonQueryAsync();
            }

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SqlServerMetaOperationSession.OpenExistingAsync(
                    databaseConnectionString));
            Assert.Contains(
                "required identity constraint",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            await DropDatabaseIfExistsAsync(
                baseConnectionString,
                databaseName);
            DeleteDirectoryIfExists(outputRoot);
        }
    }

    [Fact]
    public async Task SqlSession_RejectsDisabledForeignKey()
    {
        var baseConnectionString = await RequireSqlTestConnectionStringAsync();
        var databaseName = "MetaOps" + Guid.NewGuid().ToString("N")[..20];
        var outputRoot = CreateTempDirectory();

        try
        {
            var source = MetaOperationInterpreterTests.BuildState(databaseName);
            await DeployStateAsync(
                source,
                outputRoot,
                baseConnectionString,
                databaseName);
            var databaseConnectionString = ForDatabase(
                baseConnectionString,
                databaseName);
            await using (var connection = new SqlConnection(
                             databaseConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "ALTER TABLE [dbo].[Person] NOCHECK CONSTRAINT " +
                    "[FK_Person_Team_TeamId];";
                await command.ExecuteNonQueryAsync();
            }

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SqlServerMetaOperationSession.OpenExistingAsync(
                    databaseConnectionString));
            Assert.Contains(
                "foreign key must be enabled and trusted",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            await DropDatabaseIfExistsAsync(
                baseConnectionString,
                databaseName);
            DeleteDirectoryIfExists(outputRoot);
        }
    }

    [Fact]
    public async Task SqlSession_DiscardRollsBackSchemaAndDataOperations()
    {
        var baseConnectionString = await RequireSqlTestConnectionStringAsync();
        var databaseName = "MetaOps" + Guid.NewGuid().ToString("N")[..20];
        var outputRoot = CreateTempDirectory();

        try
        {
            var source = MetaOperationInterpreterTests.BuildState(databaseName);
            var sourceCanonical =
                MetaOperationInterpreterTests.Canonicalize(source);
            await DeployStateAsync(
                source,
                outputRoot,
                baseConnectionString,
                databaseName);
            var databaseConnectionString = ForDatabase(
                baseConnectionString,
                databaseName);

            await using (var session =
                         await SqlServerMetaOperationSession.OpenExistingAsync(
                             databaseConnectionString))
            {
                await session.ApplyAsync(
                    MetaOperationInterpreterTests.BuildPlan());
                await session.DiscardAsync();
            }

            var imported = await new ImportService(new WorkspaceService())
                .ImportSqlAsync(databaseConnectionString, "dbo");
            Assert.Equal(
                sourceCanonical,
                MetaOperationInterpreterTests.Canonicalize(
                    GenericMetadataState.Capture(imported)));
        }
        finally
        {
            await DropDatabaseIfExistsAsync(
                baseConnectionString,
                databaseName);
            DeleteDirectoryIfExists(outputRoot);
        }
    }

    private static async Task DeployStateAsync(
        GenericMetadataState state,
        string outputRoot,
        string baseConnectionString,
        string databaseName)
    {
        var workspace = new Workspace
        {
            WorkspaceRootPath = outputRoot,
            MetadataRootPath = outputRoot,
            WorkspaceConfig = MetaWorkspace.CreateDefault(),
            Model = state.Model.Clone(),
            Instance = WorkspaceSnapshotCloner.CloneInstance(state.Instance),
        };
        GenerationService.GenerateSql(workspace, outputRoot);
        await new SqlServerDeploymentService().DeployAsync(
            outputRoot,
            baseConnectionString,
            databaseName);
    }

    private static async Task<string> RequireSqlTestConnectionStringAsync()
    {
        var candidates = new List<string>();
        var configured = Environment.GetEnvironmentVariable(
            "Meta_SQL_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            candidates.Add(configured.Trim());
        }

        candidates.Add(
            "Server=.;Integrated Security=true;TrustServerCertificate=True;Encrypt=False");
        candidates.Add(
            "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=True;Encrypt=False");

        foreach (var candidate in candidates)
        {
            try
            {
                await using var connection = new SqlConnection(
                    ForDatabase(candidate, "master"));
                await connection.OpenAsync();
                return candidate;
            }
            catch
            {
                // Try the next sanctioned local SQL test endpoint.
            }
        }

        throw new InvalidOperationException(
            "SQL operation verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
    }

    private static string ForDatabase(
        string connectionString,
        string databaseName)
    {
        return new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;
    }

    private static async Task DropDatabaseIfExistsAsync(
        string baseConnectionString,
        string databaseName)
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            return;
        }

        var escapedLiteral = databaseName.Replace(
            "'",
            "''",
            StringComparison.Ordinal);
        var escapedIdentifier = databaseName.Replace(
            "]",
            "]]",
            StringComparison.Ordinal);

        try
        {
            await using var connection = new SqlConnection(
                ForDatabase(baseConnectionString, "master"));
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 300;
            command.CommandText =
                $"IF DB_ID(N'{escapedLiteral}') IS NOT NULL " +
                $"BEGIN ALTER DATABASE [{escapedIdentifier}] " +
                "SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{escapedIdentifier}]; END;";
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best-effort cleanup for test resources.
        }
    }

    private static async Task CreateDatabaseAsync(
        string baseConnectionString,
        string databaseName)
    {
        var escapedIdentifier = databaseName.Replace(
            "]",
            "]]",
            StringComparison.Ordinal);
        await using var connection = new SqlConnection(
            ForDatabase(baseConnectionString, "master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{escapedIdentifier}];";
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "meta-operation-sql",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
