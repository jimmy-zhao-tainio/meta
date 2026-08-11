using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Operations.Tests;

public sealed class RefactorOperationTests
{
    [Fact]
    public void PropertyToRelationship_UsesTargetLookupAndReturnsOutcome()
    {
        var source = BuildLookupWorkspace(optionalSource: false);

        var execution = InMemoryOperations.Execute(
            source,
            new Operation.PropertyToRelationship(
                "Order",
                "WarehouseCode",
                "Warehouse",
                "Code"));

        var outcome = Assert.IsType<PropertyToRelationshipResult>(
            Assert.Single(execution.Results));
        Assert.Equal(2, outcome.SourceRecordCount);
        Assert.Equal(2, outcome.RelationshipValueCount);
        Assert.True(outcome.PropertyRemoved);
        Assert.Equal("WarehouseId", outcome.RelationshipName);

        var order = execution.Workspace.Model.FindEntity("Order");
        Assert.NotNull(order);
        Assert.DoesNotContain(order!.Properties, property =>
            property.Name == "WarehouseCode");
        var relationship = Assert.Single(order.Relationships);
        Assert.False(relationship.IsNullable);
        Assert.Equal(
            "warehouse-a",
            execution.Workspace.Instance.RecordsByEntity["Order"][0]
                .RelationshipIds["WarehouseId"]);

        Assert.Contains(
            source.Model.FindEntity("Order")!.Properties,
            property => property.Name == "WarehouseCode");
    }

    [Fact]
    public void RelationshipToProperty_PreservesOptionalityAndMissingValues()
    {
        var source = BuildLookupWorkspace(optionalSource: true);
        var promoted = InMemoryOperations.Apply(
            source,
            new Operation.PropertyToRelationship(
                "Order",
                "WarehouseCode",
                "Warehouse",
                "Code"));

        var execution = InMemoryOperations.Execute(
            promoted,
            new Operation.RelationshipToProperty(
                "Order",
                "Warehouse",
                PropertyName: "WarehouseKey"));

        var outcome = Assert.IsType<RelationshipToPropertyResult>(
            Assert.Single(execution.Results));
        Assert.False(outcome.IsRequired);
        Assert.Equal(1, outcome.PropertyValueCount);
        var order = execution.Workspace.Model.FindEntity("Order");
        var property = Assert.Single(order!.Properties, item =>
            item.Name == "WarehouseKey");
        Assert.True(property.IsNullable);
        var records = execution.Workspace.Instance.RecordsByEntity["Order"];
        Assert.Equal("warehouse-a", records[0].Values["WarehouseKey"]);
        Assert.False(records[1].Values.ContainsKey("WarehouseKey"));
    }

    [Fact]
    public void PropertyToRelationship_DuplicateLookupFailsWithoutChangingSource()
    {
        var source = BuildLookupWorkspace(optionalSource: false);
        source.Instance.RecordsByEntity["Warehouse"][1].Values["Code"] = "A";
        var before = source.Clone();

        var exception = Assert.Throws<MetaOperationException>(() =>
            InMemoryOperations.Apply(
                source,
                new Operation.PropertyToRelationship(
                    "Order",
                    "WarehouseCode",
                    "Warehouse",
                    "Code")));

        Assert.Contains("duplicate value", exception.InnerException!.Message);
        Assert.Null(InMemoryWorkspaceComparer.FindDifference(before, source));
    }

    [Fact]
    public void RelationshipToProperty_RejectsAnotherRelationshipStorageName()
    {
        var source = BuildLookupWorkspace(optionalSource: false);
        var supplier = new GenericEntity { Name = "Supplier" };
        source.Model.Entities.Add(supplier);
        source.Instance.GetOrCreateEntityRecords("Supplier").Add(
            new GenericRecord { Id = "supplier-a" });
        var order = source.Model.FindEntity("Order")!;
        order.Properties.Add(new GenericProperty
        {
            Name = "SupplierId",
            IsNullable = false,
        });
        foreach (var record in source.Instance.RecordsByEntity["Order"])
        {
            record.Values.Add("SupplierId", "supplier-a");
        }

        var promoted = InMemoryOperations.Apply(
            source,
            new Operation.PropertyToRelationship(
                "Order",
                "WarehouseCode",
                "Warehouse",
                "Code"),
            new Operation.PropertyToRelationship(
                "Order",
                "SupplierId",
                "Supplier",
                "Id"));

        var exception = Assert.Throws<MetaOperationException>(() =>
            InMemoryOperations.Apply(
                promoted,
                new Operation.RelationshipToProperty(
                    "Order",
                    "Warehouse",
                    PropertyName: "SupplierId")));

        Assert.Equal(
            "Property 'Order.SupplierId' already exists.",
            exception.InnerException!.Message);
    }

    private static InMemoryWorkspace BuildLookupWorkspace(bool optionalSource)
    {
        var model = new GenericModel { Name = "LookupDemo" };
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
            IsNullable = optionalSource,
        });
        model.Entities.Add(order);

        var instance = new GenericInstance { ModelName = model.Name };
        instance.GetOrCreateEntityRecords("Warehouse").AddRange(
        [
            Record("warehouse-a", "Code", "A"),
            Record("warehouse-b", "Code", "B"),
        ]);
        instance.GetOrCreateEntityRecords("Order").AddRange(
        [
            Record("order-a", "WarehouseCode", "A"),
            optionalSource
                ? new GenericRecord { Id = "order-b" }
                : Record("order-b", "WarehouseCode", "B"),
        ]);
        return new InMemoryWorkspace(model, instance);
    }

    private static GenericRecord Record(
        string id,
        string property,
        string value)
    {
        var record = new GenericRecord { Id = id };
        record.Values.Add(property, value);
        return record;
    }
}
