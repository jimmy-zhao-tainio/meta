using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Operations.Tests;

public sealed class OperationTests
{
    [Fact]
    public void Construction_RejectsNamesOutsideTheSharedLanguage()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new Operation.AddEntity(
                new string('x', MetaName.MaximumLength + 1)));

        Assert.Throws<InvalidOperationException>(() =>
            new Operation.SetProperty(
                "Item",
                "item-1",
                "not-a-name",
                "value"));
    }

    [Fact]
    public void Construction_RejectsIdentitiesOutsideTheSharedLanguage()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new Operation.InsertRecord(
                "Item",
                new string('x', MetaIdentity.MaximumLength + 1)));

        Assert.Throws<InvalidOperationException>(() =>
            new Operation.SetRelationship(
                "Item",
                "item-1",
                "Parent",
                " padded"));
    }

    [Fact]
    public void InsertRecord_CapturesItsArgumentsAtConstruction()
    {
        var values = new Dictionary<string, string>
        {
            ["Name"] = "Original",
        };
        var relationships = new Dictionary<string, string>
        {
            ["Parent"] = "item-1",
        };

        var operation = new Operation.InsertRecord(
            "Item",
            "item-2",
            values,
            relationships);
        values["Name"] = "Changed";
        relationships["Parent"] = "item-3";

        Assert.Equal("Original", operation.Values["Name"]);
        Assert.Equal("item-1", operation.RelationshipIds["Parent"]);
    }

    [Fact]
    public void RenameOperations_ReturnSemanticOutcomes()
    {
        var model = new GenericModel { Name = "Original" };
        var parent = new GenericEntity { Name = "Parent" };
        var child = new GenericEntity { Name = "Child" };
        child.Relationships.Add(new GenericRelationship
        {
            Entity = "Parent",
            IsNullable = false,
        });
        model.Entities.AddRange([parent, child]);
        var instance = new GenericInstance { ModelName = model.Name };
        instance.GetOrCreateEntityRecords("Parent").Add(
            new GenericRecord { Id = "parent" });
        var childRecord = new GenericRecord { Id = "child" };
        childRecord.RelationshipIds.Add("ParentId", "parent");
        instance.GetOrCreateEntityRecords("Child").Add(childRecord);

        var execution = InMemoryOperations.Execute(
            new InMemoryWorkspace(model, instance),
            new Operation.RenameModel("Original", "Renamed"),
            new Operation.RenameRecord("Parent", "parent", "parent-new"),
            new Operation.RenameRelationship("Child", "Parent", "Owner"),
            new Operation.RenameEntity("Parent", "Party"),
            new Operation.RenameRecord("Party", "parent-new", "parent-final"));

        Assert.Equal(
            new RenameModelResult("Original", "Renamed"),
            execution.Results[0]);
        Assert.Equal(
            new RenameRecordResult("Parent", "parent", "parent-new", 1),
            execution.Results[1]);
        Assert.Equal(
            new RenameRelationshipResult(
                "Child",
                "Parent",
                "ParentId",
                "OwnerId",
                1),
            execution.Results[2]);
        Assert.Equal(
            new RenameEntityResult("Parent", "Party", 1, 1, 0),
            execution.Results[3]);
        Assert.Equal(
            new RenameRecordResult("Party", "parent-new", "parent-final", 1),
            execution.Results[4]);
    }

    [Fact]
    public void RenameModel_RejectsTheWrongCurrentModel()
    {
        var workspace = new InMemoryWorkspace(
            new GenericModel { Name = "Actual" },
            new GenericInstance { ModelName = "Actual" });

        var exception = Assert.Throws<MetaOperationException>(() =>
            InMemoryOperations.Apply(
                workspace,
                new Operation.RenameModel("Expected", "Renamed")));

        Assert.Contains(
            "Workspace model is 'Actual', not 'Expected'.",
            exception.InnerException!.Message);
        Assert.Equal("Actual", workspace.Model.Name);
        Assert.Equal("Actual", workspace.Instance.ModelName);
    }

    [Fact]
    public void DeleteRecord_ReportsReferentialDiagnosticsWithoutChangingSource()
    {
        var workspace = BuildReferenceWorkspace();
        var before = workspace.Clone();

        var exception = Assert.Throws<MetaOperationException>(() =>
            InMemoryOperations.Apply(
                workspace,
                new Operation.DeleteRecord("Parent", "parent")));

        Assert.NotNull(exception.Diagnostics);
        Assert.Contains(exception.Diagnostics!.Issues, issue =>
            issue.Code == "instance.relationship.orphan" &&
            issue.Location == "instance/Child/child/relationship/Parent/parent");
        Assert.Null(InMemoryWorkspaceComparer.FindDifference(before, workspace));
    }

    private static InMemoryWorkspace BuildReferenceWorkspace()
    {
        var model = new GenericModel { Name = "ReferenceModel" };
        var parent = new GenericEntity { Name = "Parent" };
        var child = new GenericEntity { Name = "Child" };
        child.Relationships.Add(new GenericRelationship
        {
            Entity = "Parent",
            IsNullable = false,
        });
        model.Entities.AddRange([parent, child]);

        var instance = new GenericInstance { ModelName = model.Name };
        instance.GetOrCreateEntityRecords("Parent").Add(
            new GenericRecord { Id = "parent" });
        var childRecord = new GenericRecord { Id = "child" };
        childRecord.RelationshipIds.Add("ParentId", "parent");
        instance.GetOrCreateEntityRecords("Child").Add(childRecord);
        return new InMemoryWorkspace(model, instance);
    }
}
