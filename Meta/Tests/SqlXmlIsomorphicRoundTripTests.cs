using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Meta.Adapters;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Serialization;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class SqlXmlIsomorphicRoundTripTests
{
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
                "dbo",
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
            await using (var promotedSource = await SqlWorkspaceSource.OpenAsync(
                             connectionString,
                             "dbo"))
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
                "dbo",
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
    public async Task SqlWorkspaceSource_CountAndQueryMatchInMemorySource()
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
            await using var actualSource = await SqlWorkspaceSource.OpenAsync(
                connectionString,
                "dbo");

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

            SqlOperations.Apply(connectionString, "dbo", promote);
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
            SqlOperations.Apply(connectionString, "dbo", demote);
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
                    "dbo",
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
            var source = MetaXmlCodecTests.BuildState();
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
                "dbo",
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
            var source = MetaXmlCodecTests.BuildState();
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
                "dbo",
                operations);

            var actual = await MetaSqlReader.ReadAsync(
                databaseConnectionString,
                "dbo");
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(
                expected.Workspace,
                actual));
            Assert.Equal(expected.Results, actualResults);

            await using var sourceReader = await SqlWorkspaceSource.OpenAsync(
                databaseConnectionString,
                "dbo");
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
    public async Task SqlOperations_RenameModel_RenamesTheDatabase()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL operation verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var originalName = "MetaOld" + Guid.NewGuid().ToString("N")[..20];
        var renamedName = "MetaNew" + Guid.NewGuid().ToString("N")[..20];
        try
        {
            var source = MetaXmlCodecTests.BuildState();
            source.Model.Name = originalName;
            source.Instance.ModelName = originalName;
            var sql = MetaSqlWriter.Write(source);
            await RecreateDatabaseFromSqlAsync(
                baseConnectionString,
                originalName,
                sql.Schema,
                sql.Data);

            var originalConnectionString =
                new SqlConnectionStringBuilder(baseConnectionString)
                {
                    InitialCatalog = originalName,
                }.ConnectionString;
            var results = SqlOperations.Execute(
                originalConnectionString,
                "dbo",
                new Operation.RenameModel(originalName, renamedName));
            Assert.Equal(
                new RenameModelResult(originalName, renamedName),
                Assert.Single(results));

            var renamedConnectionString =
                new SqlConnectionStringBuilder(baseConnectionString)
                {
                    InitialCatalog = renamedName,
                }.ConnectionString;
            var renamed = await MetaSqlReader.ReadAsync(
                renamedConnectionString,
                "dbo");

            Assert.Equal(renamedName, renamed.Model.Name);
            Assert.Equal(renamedName, renamed.Instance.ModelName);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropDatabaseIfExistsAsync(baseConnectionString, originalName);
            await DropDatabaseIfExistsAsync(baseConnectionString, renamedName);
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
                () => SqlWorkspaceSource.OpenAsync(
                    connectionString,
                    "dbo"));

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
                () => SqlWorkspaceSource.OpenAsync(
                    connectionString,
                    "dbo"));

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
                () => SqlWorkspaceSource.OpenAsync(
                    connectionString,
                    "dbo"));

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
                () => SqlWorkspaceSource.OpenAsync(
                    connectionString,
                    "dbo"));

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
                () => SqlWorkspaceSource.OpenAsync(
                    connectionString,
                    "dbo"));

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
            var workspace = MetaXmlCodecTests.BuildState();
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
                    "dbo",
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
            var source = MetaXmlCodecTests.BuildState();
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
                    "dbo",
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

    [Fact]
    public async Task XmlSqlXml_RoundTrip_IsByteIdentical_ForCanonicalMetadata()
    {
        var baseConnectionString = await ResolveSqlTestConnectionStringAsync();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "SQL round-trip verification requires SQL Server. Set Meta_SQL_TEST_CONNECTION or make the local '.' SQL Server endpoint available.");
        }

        var repoRoot = FindRepositoryRoot();
        var sourceInputRoot = Path.Combine(
            repoRoot,
            "Samples",
            "Demos",
            "EnterpriseBIPlatformTooling",
            "Workspace");
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-sql-roundtrip", Guid.NewGuid().ToString("N"));
        var leftWorkspaceRoot = Path.Combine(tempRoot, "left");
        var rightWorkspaceRoot = Path.Combine(tempRoot, "right");
        var sqlOutRoot = Path.Combine(tempRoot, "sql");
        var databaseName = "MetaRt" + Guid.NewGuid().ToString("N")[..20];

        try
        {
            Directory.CreateDirectory(tempRoot);

            var services = new ServiceCollection();
            var openedSource = await XmlWorkspaceReader.OpenAsync(sourceInputRoot);
            var sourceWorkspace = openedSource.State.Clone();

            // Keep database name and model name aligned so SQL import produces the same model name.
            sourceWorkspace.Model.Name = databaseName;
            sourceWorkspace.Instance.ModelName = databaseName;
            AddOptionalCubePredecessor(sourceWorkspace);
            await XmlWorkspaceWriter.WriteNewAsync(sourceWorkspace, leftWorkspaceRoot);

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
            await services.ExportService.ExportXmlAsync(
                importedWorkspace,
                rightWorkspaceRoot);

            AssertMetadataTreesAreByteIdentical(
                leftWorkspaceRoot,
                rightWorkspaceRoot);
        }
        finally
        {
            await DropDatabaseIfExistsAsync(baseConnectionString, databaseName);
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private static void AddOptionalCubePredecessor(InMemoryWorkspace workspace)
    {
        var cube = workspace.Model.FindEntity("Cube")
                   ?? throw new InvalidOperationException("Round-trip fixture does not contain the Cube entity.");
        cube.Relationships.Add(new GenericRelationship
        {
            Entity = "Cube",
            Role = "PreviousCube",
            IsNullable = true,
        });

        var cubes = workspace.Instance.GetOrCreateEntityRecords("Cube");
        if (cubes.Count < 2)
        {
            throw new InvalidOperationException("Round-trip fixture requires at least two Cube rows.");
        }

        cubes[1].RelationshipIds["PreviousCubeId"] = cubes[0].Id;
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

    private static async Task RecreateDatabaseFromScriptsAsync(
        string baseConnectionString,
        string databaseName,
        string schemaScriptPath,
        string dataScriptPath)
    {
        var schemaScript = await File.ReadAllTextAsync(schemaScriptPath).ConfigureAwait(false);
        var dataScript = await File.ReadAllTextAsync(dataScriptPath).ConfigureAwait(false);
        await RecreateDatabaseFromSqlAsync(
            baseConnectionString,
            databaseName,
            schemaScript,
            dataScript);
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

