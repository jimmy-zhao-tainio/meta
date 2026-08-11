using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class WorkspaceMergeServiceTests
{
    [Fact]
    public async Task Merge_MergesDistinctWorkspaces()
    {
        var service = new WorkspaceMergeService();
        var left = CreateWorkspace("Left", "Alpha", "1");
        var right = CreateWorkspace("Right", "Beta", "2");
        var plan = await service.MergeAsync(
            new IMetaWorkspaceSource[]
            {
                new InMemoryWorkspaceSource(left),
                new InMemoryWorkspaceSource(right),
            },
            new WorkspaceMergeOptions("MergedModel"));
        var result = plan.Result;

        Assert.Equal(2, result.SourceWorkspaceCount);
        Assert.Equal(2, result.EntitiesMerged);
        Assert.Equal(2, result.RowsMerged);
        Assert.Equal("MergedModel", plan.Workspace.Model.Name);
        Assert.NotNull(plan.Workspace.Model.FindEntity("Alpha"));
        Assert.NotNull(plan.Workspace.Model.FindEntity("Beta"));
        Assert.Single(plan.Workspace.Instance.GetOrCreateEntityRecords("Alpha"));
        Assert.Single(plan.Workspace.Instance.GetOrCreateEntityRecords("Beta"));
    }

    [Fact]
    public async Task Merge_Fails_WhenEntityNamesCollide()
    {
        var service = new WorkspaceMergeService();
        var left = CreateWorkspace("Left", "Thing", "1");
        var right = CreateWorkspace("Right", "Thing", "2");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MergeAsync(
                new IMetaWorkspaceSource[]
                {
                    new InMemoryWorkspaceSource(left),
                    new InMemoryWorkspaceSource(right),
                },
                new WorkspaceMergeOptions("MergedModel")));

        Assert.Contains("entity 'Thing' already exists", error.Message);
    }

    private static InMemoryWorkspace CreateWorkspace(string rootName, string entityName, string rowId)
    {
        var model = new GenericModel
        {
            Name = rootName + "Model",
        };
        model.Entities.Add(new GenericEntity
        {
            Name = entityName,
            Properties =
            {
                new GenericProperty { Name = "Name", IsNullable = false },
            },
        });

        var instance = new GenericInstance
        {
            ModelName = rootName + "Model",
        };
        var row = new GenericRecord
        {
            Id = rowId,
        };
        row.Values["Name"] = entityName + rowId;
        instance.GetOrCreateEntityRecords(entityName).Add(row);

        return new InMemoryWorkspace(model, instance);
    }
}
