using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Meta.Adapters;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class ModelRefactorServiceTests
{
    [Fact]
    public async Task RefactorPropertyToRelationship_RewritesLandingRowsInMemory()
    {
        var services = new ServiceCollection();
        var workspaceRoot = await TestWorkspaceFactory.CreateTempSuggestDemoWorkspaceAsync();
        try
        {
            var workspace = await services.WorkspaceService.LoadAsync(workspaceRoot);

            var result = services.ModelRefactorService.RefactorPropertyToRelationship(
                workspace,
                new PropertyToRelationshipRefactorOptions(
                    SourceEntityName: "Order",
                    SourcePropertyName: "WarehouseId",
                    TargetEntityName: "Warehouse",
                    LookupPropertyName: "Id",
                    Role: string.Empty,
                    DropSourceProperty: true));

            Assert.Equal(5, result.RowsRewritten);
            Assert.True(result.PropertyDropped);

            var orderEntity = workspace.Model.FindEntity("Order");
            Assert.NotNull(orderEntity);
            Assert.DoesNotContain(orderEntity!.Properties, item => string.Equals(item.Name, "WarehouseId", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(orderEntity.Relationships, item => string.Equals(item.Entity, "Warehouse", StringComparison.OrdinalIgnoreCase));

            var orderRows = workspace.Instance.GetOrCreateEntityRecords("Order");
            Assert.All(orderRows, row =>
            {
                Assert.False(row.Values.ContainsKey("WarehouseId"));
                Assert.True(row.RelationshipIds.TryGetValue("WarehouseId", out var fkValue));
                Assert.False(string.IsNullOrWhiteSpace(fkValue));
            });
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task RefactorPropertyToRelationship_KeepsOptionalBlankValuesUnrelatedInMemory()
    {
        var services = new ServiceCollection();
        var workspaceRoot = await TestWorkspaceFactory.CreateTempSuggestDemoWorkspaceAsync();
        try
        {
            var workspace = await services.WorkspaceService.LoadAsync(workspaceRoot);
            var orderEntity = workspace.Model.FindEntity("Order");
            Assert.NotNull(orderEntity);
            var warehouseIdProperty = orderEntity!.Properties.Single(item =>
                string.Equals(item.Name, "WarehouseId", StringComparison.OrdinalIgnoreCase));
            warehouseIdProperty.IsNullable = true;

            var blankOrder = workspace.Instance.GetOrCreateEntityRecords("Order").Single(item =>
                string.Equals(item.Id, "ORD-001", StringComparison.OrdinalIgnoreCase));
            blankOrder.Values["WarehouseId"] = string.Empty;

            var result = services.ModelRefactorService.RefactorPropertyToRelationship(
                workspace,
                new PropertyToRelationshipRefactorOptions(
                    SourceEntityName: "Order",
                    SourcePropertyName: "WarehouseId",
                    TargetEntityName: "Warehouse",
                    LookupPropertyName: "Id",
                    Role: string.Empty,
                    DropSourceProperty: true));

            Assert.Equal(5, result.RowsRewritten);
            Assert.True(result.PropertyDropped);

            Assert.DoesNotContain(orderEntity.Properties, item =>
                string.Equals(item.Name, "WarehouseId", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(orderEntity.Relationships, item =>
                string.Equals(item.Entity, "Warehouse", StringComparison.OrdinalIgnoreCase) &&
                item.IsNullable);

            Assert.False(blankOrder.Values.ContainsKey("WarehouseId"));
            Assert.False(blankOrder.RelationshipIds.ContainsKey("WarehouseId"));

            foreach (var orderRow in workspace.Instance.GetOrCreateEntityRecords("Order")
                         .Where(item => !string.Equals(item.Id, "ORD-001", StringComparison.OrdinalIgnoreCase)))
            {
                Assert.False(orderRow.Values.ContainsKey("WarehouseId"));
                Assert.True(orderRow.RelationshipIds.TryGetValue("WarehouseId", out var targetId));
                Assert.False(string.IsNullOrWhiteSpace(targetId));
            }
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task RelationshipToProperty_RoundTripsPropertyShapeInMemory()
    {
        var services = new ServiceCollection();
        var workspaceRoot = await TestWorkspaceFactory.CreateTempSuggestDemoWorkspaceAsync();
        try
        {
            var workspace = await services.WorkspaceService.LoadAsync(workspaceRoot);

            services.ModelRefactorService.RefactorPropertyToRelationship(
                workspace,
                new PropertyToRelationshipRefactorOptions(
                    SourceEntityName: "Order",
                    SourcePropertyName: "WarehouseId",
                    TargetEntityName: "Warehouse",
                    LookupPropertyName: "Id",
                    Role: string.Empty,
                    DropSourceProperty: true));

            var result = services.ModelRefactorService.RefactorRelationshipToProperty(
                workspace,
                new RelationshipToPropertyRefactorOptions(
                    SourceEntityName: "Order",
                    TargetEntityName: "Warehouse",
                    Role: string.Empty,
                    PropertyName: string.Empty));

            Assert.Equal(5, result.RowsRewritten);
            Assert.Equal("WarehouseId", result.PropertyName);

            var orderEntity = workspace.Model.FindEntity("Order");
            Assert.NotNull(orderEntity);
            Assert.Contains(orderEntity!.Properties, item => string.Equals(item.Name, "WarehouseId", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(orderEntity.Relationships, item => string.Equals(item.Entity, "Warehouse", StringComparison.OrdinalIgnoreCase));

            var orderRows = workspace.Instance.GetOrCreateEntityRecords("Order");
            Assert.All(orderRows, row =>
            {
                Assert.True(row.Values.TryGetValue("WarehouseId", out var propertyValue));
                Assert.False(string.IsNullOrWhiteSpace(propertyValue));
                Assert.False(row.RelationshipIds.ContainsKey("WarehouseId"));
            });
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    private static void DeleteDirectorySafe(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
