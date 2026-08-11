using Meta.Core.Services;
using Meta.Operations;
using Meta.Operations.Domain;
using Meta.Surfaces.Xml;

namespace Meta.Surfaces.Xml.Tests;

public sealed class WorkspaceMergeXmlTests
{
    [Fact]
    public async Task MergeXml_ComposesSemanticsAndPreservesXmlShardLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-merge-tests", Guid.NewGuid().ToString("N"));
        var leftPath = Path.Combine(root, "left");
        var rightPath = Path.Combine(root, "right");
        var mergedPath = Path.Combine(root, "merged");

        try
        {
            var left = CreateWorkspace("Left", "Alpha", "1");
            var right = CreateWorkspace("Right", "Beta", "2");
            await XmlWorkspaceWriter.WriteNewAsync(left, leftPath);
            await XmlWorkspaceWriter.WriteNewAsync(right, rightPath);

            var openedLeft = await XmlWorkspaceReader.OpenAsync(leftPath);
            var openedRight = await XmlWorkspaceReader.OpenAsync(rightPath);
            var plan = await new WorkspaceMergeService().MergeAsync(
                new IMetaWorkspaceSource[]
                {
                    new InMemoryWorkspaceSource(openedLeft.State),
                    new InMemoryWorkspaceSource(openedRight.State),
                },
                new WorkspaceMergeOptions("MergedModel"));

            await XmlWorkspaceWriter.WriteMergedAsync(plan.Workspace, mergedPath, new[] { openedLeft, openedRight });

            var merged = await XmlWorkspaceReader.OpenAsync(mergedPath);
            Assert.Equal("MergedModel", merged.Model.Name);
            Assert.NotNull(merged.Model.FindEntity("Alpha"));
            Assert.NotNull(merged.Model.FindEntity("Beta"));
            Assert.Single(merged.Instance.GetOrCreateEntityRecords("Alpha"));
            Assert.Single(merged.Instance.GetOrCreateEntityRecords("Beta"));
            Assert.True(File.Exists(Path.Combine(mergedPath, "instances", "Alpha.xml")));
            Assert.True(File.Exists(Path.Combine(mergedPath, "instances", "Beta.xml")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static InMemoryWorkspace CreateWorkspace(string rootName, string entityName, string rowId)
    {
        var model = new GenericModel { Name = rootName + "Model" };
        model.Entities.Add(new GenericEntity
        {
            Name = entityName,
            Properties = { new GenericProperty { Name = "Name", IsNullable = false } },
        });

        var instance = new GenericInstance { ModelName = rootName + "Model" };
        var row = new GenericRecord { Id = rowId };
        row.Values["Name"] = entityName + rowId;
        instance.GetOrCreateEntityRecords(entityName).Add(row);
        return new InMemoryWorkspace(model, instance);
    }
}
