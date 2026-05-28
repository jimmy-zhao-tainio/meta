using Meta.Adapters;
using Meta.Core.Domain;
using Meta.Core.Services;
using MetaWorkspaceGenerated = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace Meta.Core.Tests;

public sealed class WorkspaceMergeServiceTests
{
    [Fact]
    public void MergeInto_MergesDistinctWorkspaces()
    {
        var service = new WorkspaceMergeService();
        var left = CreateWorkspace("Left", "Alpha", "1");
        var right = CreateWorkspace("Right", "Beta", "2");
        var target = CreateTargetWorkspace("Merged", "MergedModel");

        var result = service.MergeInto(
            target,
            new[] { left, right },
            new WorkspaceMergeOptions("MergedModel"));

        Assert.Equal(2, result.SourceWorkspaceCount);
        Assert.Equal(2, result.EntitiesMerged);
        Assert.Equal(2, result.RowsMerged);
        Assert.Equal("MergedModel", target.Model.Name);
        Assert.NotNull(target.Model.FindEntity("Alpha"));
        Assert.NotNull(target.Model.FindEntity("Beta"));
        Assert.Single(target.Instance.GetOrCreateEntityRecords("Alpha"));
        Assert.Single(target.Instance.GetOrCreateEntityRecords("Beta"));
    }

    [Fact]
    public void MergeInto_Fails_WhenEntityNamesCollide()
    {
        var service = new WorkspaceMergeService();
        var left = CreateWorkspace("Left", "Thing", "1");
        var right = CreateWorkspace("Right", "Thing", "2");
        var target = CreateTargetWorkspace("Merged", "MergedModel");

        var error = Assert.Throws<InvalidOperationException>(() =>
            service.MergeInto(
                target,
                new[] { left, right },
                new WorkspaceMergeOptions("MergedModel")));

        Assert.Contains("entity 'Thing' already exists", error.Message);
    }

    private static Workspace CreateWorkspace(string rootName, string entityName, string rowId)
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-merge-tests", Guid.NewGuid().ToString("N"), rootName);
        var model = new GenericModel
        {
            Name = rootName + "Model",
        };
        model.Entities.Add(new GenericEntity
        {
            Name = entityName,
            Properties =
            {
                new GenericProperty { Name = "Name", DataType = "string", IsNullable = false },
            },
        });

        var instance = new GenericInstance
        {
            ModelName = rootName + "Model",
        };
        var row = new GenericRecord
        {
            Id = rowId,
            SourceShardFileName = entityName + ".xml",
        };
        row.Values["Name"] = entityName + rowId;
        instance.GetOrCreateEntityRecords(entityName).Add(row);

        return new Workspace
        {
            WorkspaceRootPath = root,
            MetadataRootPath = Path.Combine(root, "metadata"),
            WorkspaceConfig = MetaWorkspaceGenerated.CreateDefault(),
            Model = model,
            Instance = instance,
            IsDirty = true,
        };
    }

    private static Workspace CreateTargetWorkspace(string rootName, string modelName)
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-merge-tests", Guid.NewGuid().ToString("N"), rootName);
        return new Workspace
        {
            WorkspaceRootPath = root,
            MetadataRootPath = Path.Combine(root, "metadata"),
            WorkspaceConfig = MetaWorkspaceGenerated.CreateDefault(),
            Model = new GenericModel { Name = modelName },
            Instance = new GenericInstance { ModelName = modelName },
            IsDirty = true,
        };
    }
}
