using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Meta.Surfaces;
using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Integration;
using Meta.Surfaces.CSharp;
using Meta.Surfaces.Sql;
using Meta.Surfaces.Xml;
using Meta.TypedModels;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class SqlXmlIsomorphicRoundTripTests
{
    private static readonly SemaphoreSlim TypedSqlWorkspaceGate = new(1, 1);

    [Fact]
    public async Task SqlWorkspace_CreateBuildsANewDatabaseThroughPrimitiveOperations()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL workspace creation verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaCreate" + Guid.NewGuid().ToString("N")[..16];
        try
        {
            var connectionString = new SqlConnectionStringBuilder(
                baseConnectionString)
            {
                InitialCatalog = databaseName,
            }.ConnectionString;
            var expected = BuildRefactorWorkspace(databaseName);

            await SqlWorkspace.CreateAsync(connectionString, expected);

            await using var created = await SqlWorkspace.OpenAsync(
                connectionString);
            var actual = await WorkspaceComposition.MaterializeAsync(created);
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(
                expected,
                actual));
        }
        finally
        {
            await DropDatabaseIfExistsAsync(
                baseConnectionString,
                databaseName);
        }
    }

    [Fact]
    public async Task SqlWorkspace_CreateRejectsAnExistingDatabase()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL workspace creation verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaExists" + Guid.NewGuid().ToString("N")[..16];
        try
        {
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                string.Empty,
                string.Empty);
            var connectionString = new SqlConnectionStringBuilder(
                baseConnectionString)
            {
                InitialCatalog = databaseName,
            }.ConnectionString;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SqlWorkspace.CreateAsync(
                    connectionString,
                    BuildRefactorWorkspace(databaseName)));

            Assert.Contains("already exists", exception.Message);
        }
        finally
        {
            await DropDatabaseIfExistsAsync(
                baseConnectionString,
                databaseName);
        }
    }

    [Fact]
    public async Task RefactorOperations_AreEquivalentInMemoryAndInSql()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL refactor verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaRefactor" + Guid.NewGuid().ToString("N")[..16];
        try
        {
            var source = BuildRefactorWorkspace(databaseName);
            var sql = MetaSqlWriter.Write(source);
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                sql.Schema,
                sql.Data);
            var connectionString = new SqlConnectionStringBuilder(
                baseConnectionString)
            {
                InitialCatalog = databaseName,
            }.ConnectionString;
            var promote = new Operation.PropertyToRelationship(
                "Order",
                "WarehouseCode",
                "Warehouse",
                "Code");

            var expectedPromotion = InMemoryOperations.Execute(
                source,
                promote);
            var sqlPromotion = SqlOperations.Execute(
                connectionString,
                promote);
            Assert.Equal(
                Assert.IsType<PropertyToRelationshipResult>(
                    Assert.Single(expectedPromotion.Results)),
                Assert.IsType<PropertyToRelationshipResult>(
                    Assert.Single(sqlPromotion)));
            var promotedFromSql = await MetaSqlReader.ReadAsync(
                connectionString,
                "dbo");
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(
                expectedPromotion.Workspace,
                promotedFromSql));
            await using (var promotedSource = await SqlWorkspace.OpenAsync(
                             connectionString))
            {
                var related = await promotedSource.QueryRecordsAsync(
                    "Order",
                    new RecordQuery(
                        10,
                        new RecordCondition.Equal(
                            "Warehouse",
                            "WAREHOUSE-A")));
                Assert.Equal(1, related.TotalCount);
                Assert.Equal("order-a", Assert.Single(related.Records).Id);
            }

            var demote = new Operation.RelationshipToProperty(
                "Order",
                "Warehouse",
                PropertyName: "WarehouseKey");
            var expectedDemotion = InMemoryOperations.Execute(
                expectedPromotion.Workspace,
                demote);
            var sqlDemotion = SqlOperations.Execute(
                connectionString,
                demote);
            Assert.Equal(
                Assert.IsType<RelationshipToPropertyResult>(
                    Assert.Single(expectedDemotion.Results)),
                Assert.IsType<RelationshipToPropertyResult>(
                    Assert.Single(sqlDemotion)));
            var demotedFromSql = await MetaSqlReader.ReadAsync(
                connectionString,
                "dbo");
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(
                expectedDemotion.Workspace,
                demotedFromSql));

            var order = demotedFromSql.Model.FindEntity("Order");
            var property = Assert.Single(order!.Properties);
            Assert.Equal("WarehouseKey", property.Name);
            Assert.True(property.IsNullable);
            var records = demotedFromSql.Instance.RecordsByEntity["Order"];
            Assert.Equal("warehouse-a", records[0].Values["WarehouseKey"]);
            Assert.False(records[1].Values.ContainsKey("WarehouseKey"));
        }
        finally
        {
            await DropDatabaseIfExistsAsync(
                baseConnectionString,
                databaseName);
        }
    }

    [Fact]
    public async Task SqlWorkspace_CountAndQueryMatchInMemorySource()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL read verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaQuery" + Guid.NewGuid().ToString("N")[..20];
        try
        {
            var workspace = BuildRefactorWorkspace(databaseName);
            workspace.Model.FindEntity("Order")!.Properties.Add(
                new GenericProperty
                {
                    Name = "__MetaTotalCount",
                    IsNullable = true,
                });
            workspace.Instance.RecordsByEntity["Order"][0]
                .Values.Add("__MetaTotalCount", "visible");
            var sql = MetaSqlWriter.Write(workspace);
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                sql.Schema,
                sql.Data);
            var connectionString = new SqlConnectionStringBuilder(
                baseConnectionString)
            {
                InitialCatalog = databaseName,
            }.ConnectionString;
            var expectedSource = new InMemoryWorkspaceSource(workspace);
            await using var actualSource = await SqlWorkspace.OpenAsync(
                connectionString);

            Assert.Equal(
                await expectedSource.CountRecordsAsync("Order"),
                await actualSource.CountRecordsAsync("Order"));

            var query = new RecordQuery(
                1,
                new RecordCondition.Contains("Id", "ORDER-"));
            var expected = await expectedSource.QueryRecordsAsync(
                "Order",
                query);
            var actual = await actualSource.QueryRecordsAsync(
                "Order",
                query);
            Assert.Equal(expected.TotalCount, actual.TotalCount);
            Assert.Equal(
                expected.Records.Select(record => record.Id),
                actual.Records.Select(record => record.Id));

            var missingValue = await actualSource.QueryRecordsAsync(
                "Order",
                new RecordQuery(
                    10,
                    new RecordCondition.Equal("WarehouseCode", string.Empty)));
            Assert.Equal(1, missingValue.TotalCount);
            Assert.Equal("order-b", Assert.Single(missingValue.Records).Id);

            var internalName = await actualSource.QueryRecordsAsync(
                "Order",
                new RecordQuery(
                    10,
                    new RecordCondition.Equal(
                        "__MetaTotalCount",
                        "VISIBLE")));
            Assert.Equal(1, internalName.TotalCount);
            Assert.Equal(
                "visible",
                Assert.Single(internalName.Records)
                    .Values["__MetaTotalCount"]);
        }
        finally
        {
            await DropDatabaseIfExistsAsync(
                baseConnectionString,
                databaseName);
        }
    }

    [Fact]
    public async Task RefactorOperations_ReuseTheNaturalSqlColumnForImplicitIdLookup()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL refactor verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaRefactorId" + Guid.NewGuid().ToString("N")[..14];
        try
        {
            var source = BuildIdentityRefactorWorkspace(databaseName);
            var sql = MetaSqlWriter.Write(source);
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                sql.Schema,
                sql.Data);
            var connectionString = new SqlConnectionStringBuilder(
                baseConnectionString)
            {
                InitialCatalog = databaseName,
            }.ConnectionString;
            var promote = new Operation.PropertyToRelationship(
                "Order",
                "WarehouseId",
                "Warehouse",
                "Id");
            var expected = InMemoryOperations.Apply(source, promote);

            SqlOperations.Apply(connectionString, promote);
            var actual = await MetaSqlReader.ReadAsync(
                connectionString,
                "dbo");
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(
                expected,
                actual));
            Assert.Equal(
                "warehouse-a",
                actual.Instance.RecordsByEntity["Order"][0]
                    .RelationshipIds["WarehouseId"]);

            var demote = new Operation.RelationshipToProperty(
                "Order",
                "Warehouse");
            expected = InMemoryOperations.Apply(expected, demote);
            SqlOperations.Apply(connectionString, demote);
            actual = await MetaSqlReader.ReadAsync(
                connectionString,
                "dbo");
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(
                expected,
                actual));
        }
        finally
        {
            await DropDatabaseIfExistsAsync(
                baseConnectionString,
                databaseName);
        }
    }

    [Fact]
    public async Task PropertyToRelationship_RejectsRequiredCycleWithoutChangingSql()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL refactor verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaRefactorCycle" + Guid.NewGuid().ToString("N")[..11];
        try
        {
            var source = BuildRequiredCycleCandidate(databaseName);
            var sql = MetaSqlWriter.Write(source);
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                sql.Schema,
                sql.Data);
            var connectionString = new SqlConnectionStringBuilder(
                baseConnectionString)
            {
                InitialCatalog = databaseName,
            }.ConnectionString;
            var operation = new Operation.PropertyToRelationship(
                "A",
                "BId",
                "B",
                "Id");

            var exception = Assert.Throws<MetaOperationException>(() =>
                SqlOperations.Apply(
                    connectionString,
                    operation));
            Assert.Contains("would create a cycle", exception.InnerException!.Message);
            var unchanged = await MetaSqlReader.ReadAsync(
                connectionString,
                "dbo");
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(
                source,
                unchanged));
        }
        finally
        {
            await DropDatabaseIfExistsAsync(
                baseConnectionString,
                databaseName);
        }
    }

    [Fact]
    public async Task InMemoryWorkspaceSql_RoundTrip_PreservesStateAndOperationLaw()
    {
        var baseConnectionString =
            await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL round-trip verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName =
            "MetaIso" + Guid.NewGuid().ToString("N")[..20];

        try
        {
            var source = WorkspaceTestData.BuildState();
            source.Model.Name = databaseName;
            source.Instance.ModelName = databaseName;

            var sql = MetaSqlWriter.Write(source);
            Assert.Equal(databaseName, sql.DatabaseName);
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                sql.Schema,
                sql.Data);

            var databaseConnectionString =
                new SqlConnectionStringBuilder(baseConnectionString)
                {
                    InitialCatalog = databaseName,
                }.ConnectionString;
            var roundTripped = await MetaSqlReader.ReadAsync(
                databaseConnectionString,
                "dbo");

            Assert.Null(InMemoryWorkspaceComparer.FindDifference(
                source,
                roundTripped));

            var operation = new Operation.SetProperty(
                "Node",
                "child",
                "OptionalText",
                string.Empty);
            var expected = InMemoryOperations.Apply(source, operation);
            SqlOperations.Apply(
                databaseConnectionString,
                operation);

            var reloaded = await MetaSqlReader.ReadAsync(
                databaseConnectionString,
                "dbo");

            Assert.Null(InMemoryWorkspaceComparer.FindDifference(
                expected,
                reloaded));
        }
        finally
        {
            await DropDatabaseIfExistsAsync(
                baseConnectionString,
                databaseName);
        }
    }

    [Fact]
    public async Task SqlOperations_ApplyCommonOperationLanguageWithoutMaterializingRows()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL operation verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaOps" + Guid.NewGuid().ToString("N")[..20];
        try
        {
            var source = WorkspaceTestData.BuildState();
            source.Model.Name = databaseName;
            source.Instance.ModelName = databaseName;
            var sql = MetaSqlWriter.Write(source);
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                sql.Schema,
                sql.Data);

            Operation[] operations =
            [
                new Operation.AddEntity("Tag"),
                new Operation.AddProperty("Tag", "Name", IsRequired: true),
                new Operation.InsertRecord(
                    "Tag",
                    "tag",
                    new Dictionary<string, string> { ["Name"] = "Tag" }),
                new Operation.AddEntity("NodeAlias"),
                new Operation.InsertRecord("NodeAlias", "Root"),
                new Operation.InsertRecord("NodeAlias", "child"),
                new Operation.AddRelationship(
                    "Tag",
                    "Node",
                    "Owner",
                    IsRequired: true,
                    ExistingRecordTargetId: "child"),
                new Operation.AddProperty(
                    "Node",
                    "Label",
                    IsRequired: true,
                    ExistingRecordValue: "seed"),
                new Operation.RenameProperty("Node", "Label", "Caption"),
                new Operation.SetPropertyRequired("Node", "Caption", IsRequired: false),
                new Operation.ClearProperty("Node", "child", "Caption"),
                new Operation.SetProperty("Node", "child", "Caption", string.Empty),
                new Operation.SetPropertyRequired("Node", "Caption", IsRequired: true),
                new Operation.SetPropertyRequired("Node", "Caption", IsRequired: false),
                new Operation.SetRelationship("Tag", "tag", "Owner", "Root"),
                new Operation.SetRelationship("Tag", "tag", "Owner", "child"),
                new Operation.RenameRelationship(
                    "Tag",
                    "Owner",
                    "OwnedNode"),
                new Operation.RenameRelationship(
                    "Tag",
                    "OwnedNode",
                    "Owner"),
                new Operation.RetargetRelationship(
                    "Tag",
                    "Owner",
                    "NodeAlias"),
                new Operation.RetargetRelationship(
                    "Tag",
                    "Owner",
                    "Node"),
                new Operation.AddRelationship(
                    "Tag",
                    "Node",
                    Role: null,
                    IsRequired: false,
                    ExistingRecordTargetId: "child"),
                new Operation.RetargetRelationship(
                    "Tag",
                    "Node",
                    "NodeAlias"),
                new Operation.RetargetRelationship(
                    "Tag",
                    "NodeAlias",
                    "Node"),
                new Operation.RemoveRelationship("Tag", "Node"),
                new Operation.SetRelationshipRequired(
                    "Tag",
                    "Owner",
                    IsRequired: false),
                new Operation.ClearRelationship(
                    "Tag",
                    "tag",
                    "Owner"),
                new Operation.SetRelationshipRequired(
                    "Tag",
                    "Owner",
                    IsRequired: true,
                    MissingRecordTargetId: "child"),
                new Operation.DeleteRecord("NodeAlias", "child"),
                new Operation.DeleteRecord("NodeAlias", "Root"),
                new Operation.RemoveEntity("NodeAlias"),
                new Operation.RenameRecord("Node", "child", "offspring"),
                new Operation.ClearRelationship("Node", "offspring", "Parent"),
                new Operation.AddRelationship(
                    "Node",
                    "Tag",
                    "LabelLink",
                    IsRequired: false,
                    ExistingRecordTargetId: "tag"),
                new Operation.ClearRelationship("Node", "offspring", "LabelLink"),
                new Operation.RemoveRelationship("Node", "LabelLink"),
                new Operation.RemoveRelationship("Tag", "Owner"),
                new Operation.DeleteRecord("Tag", "tag"),
                new Operation.RemoveProperty("Tag", "Name"),
                new Operation.RemoveEntity("Tag"),
                new Operation.RenameEntity("Node", "Item"),
                new Operation.AddRelationship(
                    "Item",
                    "Item",
                    Role: "Peer",
                    IsRequired: false),
                new Operation.SetRelationship("Item", "offspring", "Peer", "offspring"),
                new Operation.ClearRelationship("Item", "offspring", "Peer"),
                new Operation.RemoveRelationship("Item", "Peer"),
                new Operation.RemoveProperty("Item", "Caption"),
            ];

            var expected = InMemoryOperations.Execute(source, operations);
            var databaseConnectionString =
                new SqlConnectionStringBuilder(baseConnectionString)
                {
                    InitialCatalog = databaseName,
                }.ConnectionString;

            var actualResults = SqlOperations.Execute(
                databaseConnectionString,
                operations);

            var actual = await MetaSqlReader.ReadAsync(
                databaseConnectionString,
                "dbo");
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(
                expected.Workspace,
                actual));
            Assert.Equal(expected.Results, actualResults);

            await using var sourceReader = await SqlWorkspace.OpenAsync(
                databaseConnectionString);
            var record = await sourceReader.ReadRecordAsync(
                "Item",
                "OFFSPRING");
            Assert.NotNull(record);
            Assert.Equal("offspring", record!.Id);
            Assert.Null(await sourceReader.ReadRecordAsync(
                "Item",
                "missing"));
        }
        finally
        {
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
        }
    }

    [Fact]
    public async Task SqlWorkspace_RenameModel_PersistsLogicalIdentityThroughDescriptor()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL operation verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var originalName = "MetaOld" + Guid.NewGuid().ToString("N")[..20];
        var renamedName = "MetaNew" + Guid.NewGuid().ToString("N")[..20];
        var root = Path.Combine(
            Path.GetTempPath(),
            "MetaSqlRename-" + Guid.NewGuid().ToString("N"));
        var environmentVariable = "META_SQL_RENAME_" +
                                   Guid.NewGuid().ToString("N");
        var originalEnvironmentValue = Environment.GetEnvironmentVariable(
            environmentVariable);
        try
        {
            var source = WorkspaceTestData.BuildState();
            source.Model.Name = originalName;
            source.Instance.ModelName = originalName;
            var originalConnectionString = new SqlConnectionStringBuilder(
                baseConnectionString)
            {
                InitialCatalog = originalName,
            }.ConnectionString;
            Environment.SetEnvironmentVariable(
                environmentVariable,
                originalConnectionString);
            await WorkspaceSurface.CreateAsync(
                source,
                root,
                "sql",
                environmentVariable);

            await using (var workspace = await WorkspaceSurface.OpenAsync(root))
            {
                var results = await workspace.ExecuteAsync(
                    [new Operation.RenameModel(originalName, renamedName)]);
                Assert.Equal(
                    new RenameModelResult(originalName, renamedName),
                    Assert.Single(results));
            }

            Assert.Equal(
                originalConnectionString,
                Environment.GetEnvironmentVariable(environmentVariable));
            var expected = InMemoryOperations.Execute(
                source,
                new Operation.RenameModel(originalName, renamedName)).Workspace;
            await using var reopened = await WorkspaceSurface.OpenAsync(root);
            Assert.Equal(renamedName, await reopened.ReadModelNameAsync());
            var actual = await WorkspaceComposition.MaterializeAsync(reopened);
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(expected, actual));
            await reopened.ExecuteAsync([new Operation.AddEntity("AfterRename")]);

            await using var reopenedAgain = await WorkspaceSurface.OpenAsync(root);
            Assert.Contains(
                "AfterRename",
                await ReadEntityNamesAsync(reopenedAgain));

            await using var physicalConnection = new SqlConnection(
                originalConnectionString);
            await physicalConnection.OpenAsync();
            Assert.Equal(originalName, physicalConnection.Database);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                environmentVariable,
                originalEnvironmentValue);
            await DropDatabaseIfExistsAsync(baseConnectionString, originalName);
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task TypedWorkspaceModelMapper_SqlLifecycleUsesNeutralMapping()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL typed-model verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        const string databaseName = nameof(MetaTypedModelsSqlOrchestrationProof);
        var root = Path.Combine(
            Path.GetTempPath(),
            "MetaTypedSql-" + Guid.NewGuid().ToString("N"));
        var environmentVariable = "META_TYPED_SQL_" + Guid.NewGuid().ToString("N");
        var originalEnvironmentValue = Environment.GetEnvironmentVariable(environmentVariable);
        await TypedSqlWorkspaceGate.WaitAsync();
        try
        {
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
            var connectionString = new SqlConnectionStringBuilder(baseConnectionString)
            {
                InitialCatalog = databaseName,
            }.ConnectionString;
            Environment.SetEnvironmentVariable(environmentVariable, connectionString);
            var model = new MetaTypedModelsSqlOrchestrationProof
            {
                ItemList =
                {
                    new MetaTypedModelsSqlItem
                    {
                        Id = "one",
                        Note = string.Empty,
                    },
                    new MetaTypedModelsSqlItem { Id = "two" },
                },
            };

            await TypedWorkspaceModelMapper.CreateAsync(
                model,
                root,
                "sql",
                environmentVariable);
            var restored = await TypedWorkspaceModelMapper.LoadAsync<MetaTypedModelsSqlOrchestrationProof>(root);

            Assert.Null(InMemoryWorkspaceComparer.FindDifference(
                TypedModelMapper.ToWorkspace(model),
                TypedModelMapper.ToWorkspace(restored)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, originalEnvironmentValue);
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
            DeleteDirectoryIfExists(root);
            TypedSqlWorkspaceGate.Release();
        }
    }

    [Fact]
    public async Task SqlWorkspace_RenameModel_MigratesLegacyPhysicalNameFallback()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL operation verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var originalName = "MetaLegacy" + Guid.NewGuid().ToString("N")[..19];
        var renamedName = "MetaLogical" + Guid.NewGuid().ToString("N")[..17];
        var root = Path.Combine(
            Path.GetTempPath(),
            "MetaSqlLegacyRename-" + Guid.NewGuid().ToString("N"));
        var environmentVariable = "META_SQL_LEGACY_RENAME_" +
                                   Guid.NewGuid().ToString("N");
        var originalEnvironmentValue = Environment.GetEnvironmentVariable(
            environmentVariable);
        try
        {
            var source = WorkspaceTestData.BuildState();
            source.Model.Name = originalName;
            source.Instance.ModelName = originalName;
            var sql = MetaSqlWriter.Write(source);
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                originalName,
                sql.Schema,
                sql.Data);
            var connectionString = new SqlConnectionStringBuilder(
                baseConnectionString)
            {
                InitialCatalog = originalName,
            }.ConnectionString;
            Environment.SetEnvironmentVariable(
                environmentVariable,
                connectionString);
            WorkspaceMetaFile.WriteSql(root, environmentVariable);

            await using (var legacy = await WorkspaceSurface.OpenAsync(root))
            {
                Assert.Equal(originalName, await legacy.ReadModelNameAsync());
                await legacy.ExecuteAsync(
                    [new Operation.RenameModel(originalName, renamedName)]);
            }

            await using var reopened = await WorkspaceSurface.OpenAsync(root);
            Assert.Equal(renamedName, await reopened.ReadModelNameAsync());
            await using var physicalConnection = new SqlConnection(connectionString);
            await physicalConnection.OpenAsync();
            Assert.Equal(originalName, physicalConnection.Database);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                environmentVariable,
                originalEnvironmentValue);
            await DropDatabaseIfExistsAsync(baseConnectionString, originalName);
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SqlWorkspace_FailedBatchRollsBackLogicalModelRenameAndRejectsReuse()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL operation verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var originalName = "MetaBatchModel" + Guid.NewGuid().ToString("N")[..14];
        var renamedName = "MetaBatchRenamed" + Guid.NewGuid().ToString("N")[..12];
        var root = Path.Combine(
            Path.GetTempPath(),
            "MetaSqlBatchModel-" + Guid.NewGuid().ToString("N"));
        var environmentVariable = "META_SQL_BATCH_MODEL_" +
                                  Guid.NewGuid().ToString("N");
        var originalEnvironmentValue = Environment.GetEnvironmentVariable(
            environmentVariable);
        try
        {
            var source = BuildRelationshipRenameWorkspace(originalName);
            var connectionString = WithDatabase(
                baseConnectionString,
                originalName);
            Environment.SetEnvironmentVariable(
                environmentVariable,
                connectionString);
            await WorkspaceSurface.CreateAsync(
                source,
                root,
                "sql",
                environmentVariable);

            await using (var workspace = await WorkspaceSurface.OpenAsync(root))
            {
                var failure = await Assert.ThrowsAsync<MetaOperationException>(
                    () => workspace.ExecuteAsync(
                        [
                            new Operation.RenameModel(originalName, renamedName),
                            new Operation.RemoveEntity("Missing"),
                        ]).AsTask());
                Assert.Equal(1, failure.OperationIndex);
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => workspace.ExecuteAsync(
                        [new Operation.AddEntity("AfterFailure")]).AsTask());
            }

            await using var reopened = await WorkspaceSurface.OpenAsync(root);
            Assert.Equal(originalName, await reopened.ReadModelNameAsync());
            var actual = await WorkspaceComposition.MaterializeAsync(reopened);
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(source, actual));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                environmentVariable,
                originalEnvironmentValue);
            await DropDatabaseIfExistsAsync(baseConnectionString, originalName);
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SqlWorkspace_FailedBatchRollsBackEntityRenameCatalogAndData()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL operation verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaBatchEntity" + Guid.NewGuid().ToString("N")[..13];
        var root = Path.Combine(
            Path.GetTempPath(),
            "MetaSqlBatchEntity-" + Guid.NewGuid().ToString("N"));
        var environmentVariable = "META_SQL_BATCH_ENTITY_" +
                                  Guid.NewGuid().ToString("N");
        var originalEnvironmentValue = Environment.GetEnvironmentVariable(
            environmentVariable);
        try
        {
            var source = BuildRelationshipRenameWorkspace(databaseName);
            var connectionString = WithDatabase(
                baseConnectionString,
                databaseName);
            Environment.SetEnvironmentVariable(
                environmentVariable,
                connectionString);
            await WorkspaceSurface.CreateAsync(
                source,
                root,
                "sql",
                environmentVariable);
            var beforeConstraints = await ReadConstraintCatalogAsync(
                connectionString);

            await using (var workspace = await WorkspaceSurface.OpenAsync(root))
            {
                var failure = await Assert.ThrowsAsync<MetaOperationException>(
                    () => workspace.ExecuteAsync(
                        [
                            new Operation.RenameEntity("Child", "RenamedChild"),
                            new Operation.AddEntity("Parent"),
                        ]).AsTask());
                Assert.Equal(1, failure.OperationIndex);
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => workspace.ExecuteAsync(
                        [new Operation.AddEntity("AfterFailure")]).AsTask());
            }

            await using var reopened = await WorkspaceSurface.OpenAsync(root);
            var actual = await WorkspaceComposition.MaterializeAsync(reopened);
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(source, actual));
            Assert.Equal(
                beforeConstraints,
                await ReadConstraintCatalogAsync(connectionString));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                environmentVariable,
                originalEnvironmentValue);
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SqlWorkspace_RenameEntity_RefreshesAllAffectedConstraints()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL operation verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaEntityRename" + Guid.NewGuid().ToString("N")[..15];
        try
        {
            var source = BuildEntityRenameWorkspace(databaseName);
            var sql = MetaSqlWriter.Write(source);
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                sql.Schema,
                sql.Data);
            var connectionString = WithDatabase(
                baseConnectionString,
                databaseName);
            await ExecuteSqlAsync(
                connectionString,
                """
                EXEC sys.sp_rename N'[dbo].[PK_Node]', N'Legacy_Node_PK', N'OBJECT';
                EXEC sys.sp_rename N'[dbo].[FK_Source_Node_NodeId]', N'Legacy_Source_Node_FK', N'OBJECT';
                EXEC sys.sp_rename N'[dbo].[FK_Source_Node_OwnerId]', N'Legacy_Source_Owner_FK', N'OBJECT';
                """);
            var before = await ReadConstraintCatalogAsync(connectionString);

            SqlOperations.Execute(
                connectionString,
                new Operation.RenameEntity("Node", "RenamedNode"));

            var after = await ReadConstraintCatalogAsync(connectionString);
            var constraints = after.Select(constraint => constraint.Name).ToArray();
            Assert.Contains(
                SqlWorkspaceNames.PrimaryKey("RenamedNode"),
                constraints);
            Assert.Contains(
                SqlWorkspaceNames.ForeignKey(
                    "RenamedNode",
                    "Target",
                    "TargetId"),
                constraints);
            Assert.Contains(
                SqlWorkspaceNames.ForeignKey(
                    "RenamedNode",
                    "RenamedNode",
                    "ParentId"),
                constraints);
            Assert.Contains(
                SqlWorkspaceNames.ForeignKey(
                    "Source",
                    "RenamedNode",
                    "RenamedNodeId"),
                constraints);
            Assert.Contains(
                SqlWorkspaceNames.ForeignKey(
                    "Source",
                    "RenamedNode",
                    "OwnerId"),
                constraints);
            AssertConstraintPreserved(
                before,
                "Legacy_Node_PK",
                after,
                SqlWorkspaceNames.PrimaryKey("RenamedNode"));
            AssertConstraintPreserved(
                before,
                SqlWorkspaceNames.ForeignKey("Node", "Target", "TargetId"),
                after,
                SqlWorkspaceNames.ForeignKey(
                    "RenamedNode",
                    "Target",
                    "TargetId"));
            AssertConstraintPreserved(
                before,
                SqlWorkspaceNames.ForeignKey("Node", "Node", "ParentId"),
                after,
                SqlWorkspaceNames.ForeignKey(
                    "RenamedNode",
                    "RenamedNode",
                    "ParentId"));
            AssertConstraintPreserved(
                before,
                "Legacy_Source_Node_FK",
                after,
                SqlWorkspaceNames.ForeignKey(
                    "Source",
                    "RenamedNode",
                    "RenamedNodeId"));
            AssertConstraintPreserved(
                before,
                "Legacy_Source_Owner_FK",
                after,
                SqlWorkspaceNames.ForeignKey(
                    "Source",
                    "RenamedNode",
                    "OwnerId"));
            Assert.DoesNotContain(
                after,
                constraint => constraint.Name.StartsWith(
                    "MetaRename_",
                    StringComparison.OrdinalIgnoreCase));

            SqlOperations.Execute(
                connectionString,
                new Operation.AddEntity("Node"),
                new Operation.AddRelationship(
                    "RenamedNode",
                    "Node",
                    "Owner",
                    IsRequired: false));
            var afterReuse = await ReadConstraintNamesAsync(connectionString);
            Assert.Contains(
                SqlWorkspaceNames.PrimaryKey("Node"),
                afterReuse);
            Assert.Contains(
                SqlWorkspaceNames.ForeignKey(
                    "RenamedNode",
                    "Node",
                    "OwnerId"),
                afterReuse);
        }
        finally
        {
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
        }
    }

    [Fact]
    public async Task SqlWorkspace_RenameRelationship_RefreshesConstraintAndAllowsRoleReuse()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL operation verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaRelationshipRename" + Guid.NewGuid().ToString("N")[..12];
        try
        {
            var source = BuildRelationshipRenameWorkspace(databaseName);
            var sql = MetaSqlWriter.Write(source);
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                sql.Schema,
                sql.Data);
            var connectionString = WithDatabase(
                baseConnectionString,
                databaseName);
            var before = await ReadConstraintCatalogAsync(connectionString);
            var originalConstraintName = SqlWorkspaceNames.ForeignKey(
                "Child",
                "Parent",
                "OriginalId");

            SqlOperations.Execute(
                connectionString,
                new Operation.RenameRelationship(
                    "Child",
                    "Original",
                    "original"));
            var renamedCatalog = await ReadConstraintCatalogAsync(connectionString);
            var renamed = renamedCatalog.Select(constraint => constraint.Name).ToArray();
            var caseOnlyConstraintName = SqlWorkspaceNames.ForeignKey(
                "Child",
                "Parent",
                "originalId");
            Assert.Contains(
                caseOnlyConstraintName,
                renamed);
            Assert.DoesNotContain(
                originalConstraintName,
                renamed);
            AssertConstraintPreserved(
                before,
                originalConstraintName,
                renamedCatalog,
                caseOnlyConstraintName);

            SqlOperations.Execute(
                connectionString,
                new Operation.RenameRelationship(
                    "Child",
                    "original",
                    "Renamed"));
            var roleRenamedCatalog = await ReadConstraintCatalogAsync(
                connectionString);
            renamed = roleRenamedCatalog.Select(constraint => constraint.Name).ToArray();
            var renamedConstraintName = SqlWorkspaceNames.ForeignKey(
                "Child",
                "Parent",
                "RenamedId");
            Assert.Contains(
                renamedConstraintName,
                renamed);
            AssertConstraintPreserved(
                renamedCatalog,
                caseOnlyConstraintName,
                roleRenamedCatalog,
                renamedConstraintName);

            SqlOperations.Execute(
                connectionString,
                new Operation.AddRelationship(
                    "Child",
                    "Parent",
                    "Original",
                    IsRequired: false));
            var reused = await ReadConstraintNamesAsync(connectionString);
            Assert.Contains(
                SqlWorkspaceNames.ForeignKey(
                    "Child",
                    "Parent",
                    "OriginalId"),
                reused);
            Assert.Contains(
                SqlWorkspaceNames.ForeignKey(
                    "Child",
                    "Parent",
                    "RenamedId"),
                reused);
        }
        finally
        {
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
        }
    }

    [Fact]
    public async Task SqlWorkspace_RenameEntity_CollisionRollsBackWithoutChanges()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL operation verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaRenameRollback" + Guid.NewGuid().ToString("N")[..13];
        try
        {
            var source = BuildRelationshipRenameWorkspace(databaseName);
            var sql = MetaSqlWriter.Write(source);
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                sql.Schema,
                sql.Data);
            var connectionString = WithDatabase(
                baseConnectionString,
                databaseName);
            await ExecuteSqlAsync(
                connectionString,
                """
                CREATE TABLE [dbo].[PK_Target]
                (
                    [Id] NVARCHAR(450) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
                    CONSTRAINT [PK_PK_Target] PRIMARY KEY ([Id])
                );
                """);
            var before = await MetaSqlReader.ReadAsync(connectionString, "dbo");
            var beforeConstraints = await ReadConstraintCatalogAsync(connectionString);

            var exception = Assert.Throws<MetaOperationException>(() =>
                SqlOperations.Execute(
                    connectionString,
                    new Operation.RenameEntity("Child", "Target")));
            Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);

            var after = await MetaSqlReader.ReadAsync(connectionString, "dbo");
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(before, after));
            Assert.Equal(
                beforeConstraints,
                await ReadConstraintCatalogAsync(connectionString));
        }
        finally
        {
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
        }
    }

    [Fact]
    public async Task SqlWorkspace_RenameEntity_UsesDeterministicHashedConstraintNames()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL operation verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaLongRename" + Guid.NewGuid().ToString("N")[..13];
        var sourceName = "Source" + new string('S', 50);
        var oldTarget = "Old" + new string('T', 55);
        var newTarget = "New" + new string('T', 55);
        try
        {
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                $"""
                CREATE TABLE [{oldTarget}]
                (
                    [Id] NVARCHAR(450) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
                    CONSTRAINT [Legacy_Target_PK] PRIMARY KEY ([Id])
                );
                CREATE TABLE [{sourceName}]
                (
                    [Id] NVARCHAR(450) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
                    [RoleId] NVARCHAR(450) COLLATE Latin1_General_100_CI_AS_SC NULL,
                    CONSTRAINT [Legacy_Source_PK] PRIMARY KEY ([Id]),
                    CONSTRAINT [Legacy_Source_Role_FK]
                        FOREIGN KEY ([RoleId]) REFERENCES [dbo].[{oldTarget}] ([Id])
                );
                """,
                string.Empty);
            var connectionString = WithDatabase(
                baseConnectionString,
                databaseName);
            var before = await ReadConstraintCatalogAsync(connectionString);

            SqlOperations.Execute(
                connectionString,
                new Operation.RenameEntity(oldTarget, newTarget));
            SqlOperations.Execute(
                connectionString,
                new Operation.RenameRelationship(
                    sourceName,
                    "Role",
                    "AnotherRole"));

            var after = await ReadConstraintCatalogAsync(connectionString);
            var constraints = after.Select(constraint => constraint.Name).ToArray();
            Assert.Contains(SqlWorkspaceNames.PrimaryKey(newTarget), constraints);
            Assert.Contains(
                SqlWorkspaceNames.ForeignKey(
                    sourceName,
                    newTarget,
                    "AnotherRoleId"),
                constraints);
            Assert.DoesNotContain(
                SqlWorkspaceNames.PrimaryKey(oldTarget),
                constraints);
            var maximumName = new string('L', MetaName.MaximumLength);
            Assert.NotEqual(
                "PK_" + maximumName,
                SqlWorkspaceNames.PrimaryKey(maximumName));
            AssertConstraintPreserved(
                before,
                "Legacy_Target_PK",
                after,
                SqlWorkspaceNames.PrimaryKey(newTarget));
            AssertConstraintPreserved(
                before,
                "Legacy_Source_Role_FK",
                after,
                SqlWorkspaceNames.ForeignKey(
                    sourceName,
                    newTarget,
                    "AnotherRoleId"));
        }
        finally
        {
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
        }
    }

    [Fact]
    public async Task SqlWorkspaceOpen_ValidatesTheCompleteModelContract()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL source verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaModel" + Guid.NewGuid().ToString("N")[..19];
        try
        {
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                """
                CREATE TABLE [dbo].[Parent]
                (
                    [Id] NVARCHAR(450) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
                    CONSTRAINT [PK_Parent] PRIMARY KEY ([Id])
                );
                CREATE TABLE [dbo].[Child]
                (
                    [Id] NVARCHAR(450) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
                    [Parent] NVARCHAR(MAX) NULL,
                    [ParentId] NVARCHAR(450) COLLATE Latin1_General_100_CI_AS_SC NULL,
                    CONSTRAINT [PK_Child] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_Child_Parent]
                        FOREIGN KEY ([ParentId]) REFERENCES [dbo].[Parent] ([Id])
                );
                """,
                string.Empty);
            var connectionString =
                new SqlConnectionStringBuilder(baseConnectionString)
                {
                    InitialCatalog = databaseName,
                }.ConnectionString;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SqlWorkspace.OpenAsync(
                    connectionString));

            Assert.Contains("entity.member.collision", exception.Message);
        }
        finally
        {
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
        }
    }

    [Theory]
    [MemberData(nameof(UnmodeledSqlBehaviorCases))]
    public async Task SqlWorkspaceOpen_RejectsActiveUnmodeledSqlBehavior(
        string schemaSql,
        string expectedMessage)
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL source verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaBehavior" + Guid.NewGuid().ToString("N")[..15];
        try
        {
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                schemaSql,
                string.Empty);
            var connectionString =
                new SqlConnectionStringBuilder(baseConnectionString)
                {
                    InitialCatalog = databaseName,
                }.ConnectionString;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SqlWorkspace.OpenAsync(
                    connectionString));

            Assert.Contains(expectedMessage, exception.Message);
            Assert.Contains(
                "Meta does not model this SQL behavior.",
                exception.Message);
        }
        finally
        {
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
        }
    }

    public static IEnumerable<object[]> UnmodeledSqlBehaviorCases()
    {
        const string identity =
            "NVARCHAR(450) COLLATE Latin1_General_100_CI_AS_SC";

        yield return
        [
            $"""
             CREATE TABLE [dbo].[Item]
             (
                 [Id] {identity} NOT NULL,
                 [Name] NVARCHAR(MAX) NULL
                     CONSTRAINT [DF_Item_Name] DEFAULT N'unknown',
                 CONSTRAINT [PK_Item] PRIMARY KEY ([Id])
             );
             """,
            "has a default constraint",
        ];
        yield return
        [
            $"""
             CREATE TABLE [dbo].[Item]
             (
                 [Id] {identity} NOT NULL,
                 [Name] NVARCHAR(MAX) NULL,
                 [DisplayName] AS CAST([Name] AS NVARCHAR(MAX)),
                 CONSTRAINT [PK_Item] PRIMARY KEY ([Id])
             );
             """,
            "is computed",
        ];
        yield return
        [
            $"""
             CREATE TABLE [dbo].[Item]
             (
                 [Id] {identity} NOT NULL,
                 [Name] NVARCHAR(MAX) NULL,
                 CONSTRAINT [PK_Item] PRIMARY KEY ([Id]),
                 CONSTRAINT [CK_Item_Name] CHECK ([Name] <> N'forbidden')
             );
             """,
            "Check constraint",
        ];
        yield return
        [
            $"""
             CREATE TABLE [dbo].[Item]
             (
                 [Id] {identity} NOT NULL,
                 [Name] NVARCHAR(MAX) NULL,
                 CONSTRAINT [PK_Item] PRIMARY KEY ([Id])
             );
             EXEC(N'CREATE TRIGGER [dbo].[TR_Item]
                 ON [dbo].[Item]
                 AFTER INSERT
             AS
             BEGIN
                 SET NOCOUNT ON;
             END;');
             """,
            "Trigger",
        ];
        yield return
        [
            $"""
             CREATE TABLE [dbo].[Item]
             (
                 [Id] {identity} NOT NULL,
                 [Name] NVARCHAR(450) COLLATE Latin1_General_100_CI_AS_SC NULL,
                 CONSTRAINT [PK_Item] PRIMARY KEY ([Id]),
                 CONSTRAINT [UQ_Item_Name] UNIQUE ([Name])
             );
             """,
            "Unique index",
        ];
        yield return
        [
            $"""
             CREATE TABLE [dbo].[Parent]
             (
                 [Id] {identity} NOT NULL,
                 CONSTRAINT [PK_Parent] PRIMARY KEY ([Id])
             );
             CREATE TABLE [dbo].[Child]
             (
                 [Id] {identity} NOT NULL,
                 [ParentId] {identity} NULL,
                 CONSTRAINT [PK_Child] PRIMARY KEY ([Id]),
                 CONSTRAINT [FK_Child_Parent]
                     FOREIGN KEY ([ParentId]) REFERENCES [dbo].[Parent] ([Id])
                     ON DELETE SET NULL
             );
             """,
            "cascading referential action",
        ];
        yield return
        [
            $"""
             EXEC(N'CREATE SCHEMA [outside]');
             CREATE TABLE [outside].[Parent]
             (
                 [Id] {identity} NOT NULL,
                 CONSTRAINT [PK_OutsideParent] PRIMARY KEY ([Id])
             );
             CREATE TABLE [dbo].[Child]
             (
                 [Id] {identity} NOT NULL,
                 [ParentId] {identity} NULL,
                 CONSTRAINT [PK_Child] PRIMARY KEY ([Id]),
                 CONSTRAINT [FK_Child_OutsideParent]
                     FOREIGN KEY ([ParentId]) REFERENCES [outside].[Parent] ([Id])
             );
             """,
            "crosses the SQL workspace schema boundary",
        ];
    }

    [Fact]
    public async Task SqlPropertyRead_RejectsAnUnrepresentableForeignKey()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL source verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaRead" + Guid.NewGuid().ToString("N")[..20];
        try
        {
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                """
                CREATE TABLE [dbo].[Parent]
                (
                    [Id] NVARCHAR(450) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
                    CONSTRAINT [PK_Parent] PRIMARY KEY ([Id])
                );
                CREATE TABLE [dbo].[Child]
                (
                    [Id] NVARCHAR(450) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
                    [ParentKey] NVARCHAR(450) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
                    CONSTRAINT [PK_Child] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_Child_Parent]
                        FOREIGN KEY ([ParentKey]) REFERENCES [dbo].[Parent] ([Id])
                );
                """,
                string.Empty);
            var connectionString =
                new SqlConnectionStringBuilder(baseConnectionString)
                {
                    InitialCatalog = databaseName,
                }.ConnectionString;
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SqlWorkspace.OpenAsync(
                    connectionString));

            Assert.Contains("<Role>Id", exception.Message);
        }
        finally
        {
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
        }
    }

    [Fact]
    public async Task SqlPropertyRead_RequiresTheTextRepresentationContract()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL source verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaText" + Guid.NewGuid().ToString("N")[..20];
        try
        {
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                """
                CREATE TABLE [dbo].[Item]
                (
                    [Id] NVARCHAR(450) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
                    [Quantity] INT NULL,
                    CONSTRAINT [PK_Item] PRIMARY KEY ([Id])
                );
                """,
                string.Empty);
            var connectionString =
                new SqlConnectionStringBuilder(baseConnectionString)
                {
                    InitialCatalog = databaseName,
                }.ConnectionString;
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SqlWorkspace.OpenAsync(
                    connectionString));

            Assert.Contains("must use NVARCHAR(MAX)", exception.Message);
        }
        finally
        {
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
        }
    }

    [Fact]
    public async Task SqlPropertyRead_RequiresDatabaseBackedRecordIdentity()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL source verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaRi" + Guid.NewGuid().ToString("N")[..20];
        try
        {
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                """
                CREATE TABLE [dbo].[Item]
                (
                    [Id] NVARCHAR(450) COLLATE Latin1_General_100_CI_AS_SC NOT NULL,
                    [Name] NVARCHAR(MAX) NULL
                );
                """,
                string.Empty);
            var connectionString =
                new SqlConnectionStringBuilder(baseConnectionString)
                {
                    InitialCatalog = databaseName,
                }.ConnectionString;
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SqlWorkspace.OpenAsync(
                    connectionString));

            Assert.Contains("single-column primary key", exception.Message);
        }
        finally
        {
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
        }
    }

    [Fact]
    public async Task SqlOperations_RollBackModelChangesRejectedByTheKernel()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL operation verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaModelOp" + Guid.NewGuid().ToString("N")[..17];
        try
        {
            var workspace = WorkspaceTestData.BuildState();
            workspace.Model.Name = databaseName;
            workspace.Instance.ModelName = databaseName;
            var sql = MetaSqlWriter.Write(workspace);
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                sql.Schema,
                sql.Data);
            var connectionString =
                new SqlConnectionStringBuilder(baseConnectionString)
                {
                    InitialCatalog = databaseName,
                }.ConnectionString;

            var exception = Assert.Throws<MetaOperationException>(() =>
                SqlOperations.Apply(
                    connectionString,
                    new Operation.AddRelationship(
                        "Node",
                        "Node",
                        "RequiredText",
                        IsRequired: false)));

            Assert.Contains("entity.member.collision", exception.Message);
            var actual = await MetaSqlReader.ReadAsync(
                connectionString,
                "dbo");
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(
                workspace,
                actual));
        }
        finally
        {
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
        }
    }

    [Fact]
    public async Task SqlOperations_RollBackTheBatchWhenSqlRejectsAnOperation()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL operation verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var databaseName = "MetaRollback" + Guid.NewGuid().ToString("N")[..16];
        try
        {
            var source = WorkspaceTestData.BuildState();
            source.Model.Name = databaseName;
            source.Instance.ModelName = databaseName;
            var sql = MetaSqlWriter.Write(source);
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                databaseName,
                sql.Schema,
                sql.Data);

            var databaseConnectionString =
                new SqlConnectionStringBuilder(baseConnectionString)
                {
                    InitialCatalog = databaseName,
                }.ConnectionString;
            Assert.Throws<MetaOperationException>(() =>
                SqlOperations.Apply(
                    databaseConnectionString,
                    new Operation.AddProperty(
                        "Node",
                        "Transient",
                        IsRequired: false),
                    new Operation.InsertRecord(
                        "Node",
                        "broken",
                        new Dictionary<string, string>
                        {
                            ["RequiredText"] = "Broken",
                        },
                        new Dictionary<string, string>
                        {
                            ["Parent"] = "missing",
                        })));

            var reloaded = await MetaSqlReader.ReadAsync(
                databaseConnectionString,
                "dbo");
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(
                source,
                reloaded));
        }
        finally
        {
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
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

        candidates.Add("Server=.;Integrated Security=true;TrustServerCertificate=True;Encrypt=False");
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

    private static async Task RecreateDatabaseFromSqlAsync(
        string baseConnectionString,
        string databaseName,
        string schemaScript,
        string dataScript)
    {
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

    private static string WithDatabase(
        string connectionString,
        string databaseName)
    {
        return new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;
    }

    private static async Task<IReadOnlyList<string>> ReadEntityNamesAsync(
        IMetaWorkspace workspace)
    {
        var names = new List<string>();
        await foreach (var name in workspace.ReadEntityNamesAsync())
        {
            names.Add(name);
        }

        return names;
    }

    private static async Task<IReadOnlyList<string>> ReadConstraintNamesAsync(
        string connectionString)
    {
        var names = new List<string>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new SqlCommand(
            """
            SELECT objectValue.name
            FROM sys.objects objectValue
            INNER JOIN sys.schemas schemaValue
                ON schemaValue.schema_id = objectValue.schema_id
            WHERE schemaValue.name = N'dbo'
              AND objectValue.type IN ('PK', 'F')
            ORDER BY objectValue.name;
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync()
            .ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<IReadOnlyList<SqlConstraintCatalogEntry>>
        ReadConstraintCatalogAsync(string connectionString)
    {
        var constraints = new List<SqlConstraintCatalogEntry>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new SqlCommand(
            """
            SELECT
                objectValue.name,
                objectValue.object_id,
                objectValue.type,
                CONVERT(bit, COALESCE(foreignKey.is_disabled, 0)),
                CONVERT(bit, COALESCE(foreignKey.is_not_trusted, 0)),
                CONVERT(bit, COALESCE(foreignKey.is_not_for_replication, 0))
            FROM sys.objects objectValue
            INNER JOIN sys.schemas schemaValue
                ON schemaValue.schema_id = objectValue.schema_id
            LEFT JOIN sys.foreign_keys foreignKey
                ON foreignKey.object_id = objectValue.object_id
            WHERE schemaValue.name = N'dbo'
              AND objectValue.type IN ('PK', 'F')
            ORDER BY objectValue.object_id;
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync()
            .ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            constraints.Add(new SqlConstraintCatalogEntry(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2).Trim(),
                reader.GetBoolean(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5)));
        }

        return constraints;
    }

    private static void AssertConstraintPreserved(
        IReadOnlyList<SqlConstraintCatalogEntry> before,
        string beforeName,
        IReadOnlyList<SqlConstraintCatalogEntry> after,
        string afterName)
    {
        var original = Assert.Single(before, constraint =>
            string.Equals(
                constraint.Name,
                beforeName,
                StringComparison.Ordinal));
        var renamed = Assert.Single(after, constraint =>
            string.Equals(
                constraint.Name,
                afterName,
                StringComparison.Ordinal));
        Assert.Equal(
            original with { Name = afterName },
            renamed);
    }

    private static async Task ExecuteSqlAsync(
        string connectionString,
        string script)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        foreach (var batch in SplitSqlBatches(script))
        {
            await using var command = new SqlCommand(batch, connection)
            {
                CommandTimeout = 300,
            };
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static InMemoryWorkspace BuildEntityRenameWorkspace(
        string modelName)
    {
        var model = new GenericModel { Name = modelName };
        var node = new GenericEntity { Name = "Node" };
        node.Relationships.Add(new GenericRelationship
        {
            Entity = "Node",
            Role = "Parent",
            IsNullable = true,
        });
        node.Relationships.Add(new GenericRelationship
        {
            Entity = "Target",
            IsNullable = true,
        });
        model.Entities.Add(node);
        var target = new GenericEntity { Name = "Target" };
        model.Entities.Add(target);
        var source = new GenericEntity { Name = "Source" };
        source.Relationships.Add(new GenericRelationship
        {
            Entity = "Node",
            IsNullable = true,
        });
        source.Relationships.Add(new GenericRelationship
        {
            Entity = "Node",
            Role = "Owner",
            IsNullable = true,
        });
        model.Entities.Add(source);
        return new InMemoryWorkspace(
            model,
            new GenericInstance { ModelName = modelName });
    }

    private static InMemoryWorkspace BuildRelationshipRenameWorkspace(
        string modelName)
    {
        var model = new GenericModel { Name = modelName };
        model.Entities.Add(new GenericEntity { Name = "Parent" });
        var child = new GenericEntity { Name = "Child" };
        child.Relationships.Add(new GenericRelationship
        {
            Entity = "Parent",
            Role = "Original",
            IsNullable = true,
        });
        model.Entities.Add(child);
        var instance = new GenericInstance { ModelName = modelName };
        instance.GetOrCreateEntityRecords("Child").Add(
            new GenericRecord { Id = "child-1" });
        return new InMemoryWorkspace(
            model,
            instance);
    }

    public sealed class MetaTypedModelsSqlOrchestrationProof
    {
        public List<MetaTypedModelsSqlItem> ItemList { get; set; } = new();
    }

    public sealed class MetaTypedModelsSqlItem
    {
        public string Id { get; set; } = string.Empty;

        public string? Note { get; set; }
    }

    private static InMemoryWorkspace BuildRefactorWorkspace(
        string modelName)
    {
        var model = new GenericModel { Name = modelName };
        var warehouse = new GenericEntity { Name = "Warehouse" };
        warehouse.Properties.Add(new GenericProperty
        {
            Name = "Code",
            IsNullable = false,
        });
        model.Entities.Add(warehouse);
        var order = new GenericEntity { Name = "Order" };
        order.Properties.Add(new GenericProperty
        {
            Name = "WarehouseCode",
            IsNullable = true,
        });
        model.Entities.Add(order);

        var instance = new GenericInstance { ModelName = modelName };
        var warehouseA = new GenericRecord { Id = "warehouse-a" };
        warehouseA.Values.Add("Code", "A");
        var warehouseB = new GenericRecord { Id = "warehouse-b" };
        warehouseB.Values.Add("Code", "B");
        instance.GetOrCreateEntityRecords("Warehouse").AddRange(
            [warehouseA, warehouseB]);
        var orderA = new GenericRecord { Id = "order-a" };
        orderA.Values.Add("WarehouseCode", "A");
        instance.GetOrCreateEntityRecords("Order").AddRange(
            [orderA, new GenericRecord { Id = "order-b" }]);
        return new InMemoryWorkspace(model, instance);
    }

    private static InMemoryWorkspace BuildIdentityRefactorWorkspace(
        string modelName)
    {
        var model = new GenericModel { Name = modelName };
        model.Entities.Add(new GenericEntity { Name = "Warehouse" });
        var order = new GenericEntity { Name = "Order" };
        order.Properties.Add(new GenericProperty
        {
            Name = "WarehouseId",
            IsNullable = false,
        });
        model.Entities.Add(order);

        var instance = new GenericInstance { ModelName = modelName };
        instance.GetOrCreateEntityRecords("Warehouse").Add(
            new GenericRecord { Id = "warehouse-a" });
        var orderRecord = new GenericRecord { Id = "order-a" };
        orderRecord.Values.Add("WarehouseId", "WAREHOUSE-A");
        instance.GetOrCreateEntityRecords("Order").Add(orderRecord);
        return new InMemoryWorkspace(model, instance);
    }

    private static InMemoryWorkspace BuildRequiredCycleCandidate(
        string modelName)
    {
        var model = new GenericModel { Name = modelName };
        var a = new GenericEntity { Name = "A" };
        a.Properties.Add(new GenericProperty
        {
            Name = "BId",
            IsNullable = false,
        });
        model.Entities.Add(a);
        var b = new GenericEntity { Name = "B" };
        b.Relationships.Add(new GenericRelationship
        {
            Entity = "A",
            IsNullable = false,
        });
        model.Entities.Add(b);

        var instance = new GenericInstance { ModelName = modelName };
        var aRecord = new GenericRecord { Id = "a" };
        aRecord.Values.Add("BId", "b");
        instance.GetOrCreateEntityRecords("A").Add(aRecord);
        var bRecord = new GenericRecord { Id = "b" };
        bRecord.RelationshipIds.Add("AId", "a");
        instance.GetOrCreateEntityRecords("B").Add(bRecord);
        return new InMemoryWorkspace(model, instance);
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

    private sealed record SqlConstraintCatalogEntry(
        string Name,
        int ObjectId,
        string Type,
        bool IsDisabled,
        bool IsNotTrusted,
        bool IsNotForReplication);

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

