using Meta.Integration;
using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Core.Tests;

public sealed class ImportServiceTests
{
    [Fact]
    public void PlanCsvImport_ReturnsOperationsWithoutMutatingTarget()
    {
        var target = CreateWorkspace("TargetModel", "Alpha");
        var imported = CreateWorkspace("ImportedModel", "Alpha updated");
        var added = new GenericRecord { Id = "2" };
        added.Values["Name"] = "Beta";
        imported.Instance.GetOrCreateEntityRecords("Item").Add(added);

        var plan = new ImportService().PlanCsvImport(target, imported);

        Assert.Equal("Alpha", target.Instance.GetOrCreateEntityRecords("Item").Single().Values["Name"]);
        Assert.Collection(
            plan.Operations,
            operation => Assert.IsType<Operation.SetProperty>(operation),
            operation => Assert.IsType<Operation.InsertRecord>(operation));

        var applied = InMemoryOperations.Apply(
            new InMemoryWorkspace(target.Model, target.Instance),
            plan.Operations);
        var records = applied.Instance.GetOrCreateEntityRecords("Item")
            .OrderBy(record => record.Id, MetaIdentity.Comparer)
            .ToList();
        Assert.Equal(2, records.Count);
        Assert.Equal("Alpha updated", records[0].Values["Name"]);
        Assert.Equal("Beta", records[1].Values["Name"]);
    }

    private static InMemoryWorkspace CreateWorkspace(string modelName, string value)
    {
        var entity = new GenericEntity { Name = "Item" };
        entity.Properties.Add(new GenericProperty
        {
            Name = "Name",
            IsNullable = false,
        });

        var model = new GenericModel { Name = modelName };
        model.Entities.Add(entity);
        var instance = new GenericInstance { ModelName = modelName };
        var record = new GenericRecord { Id = "1" };
        record.Values["Name"] = value;
        instance.GetOrCreateEntityRecords(entity.Name).Add(record);

        return new InMemoryWorkspace(model, instance);
    }
}
