using Meta.Adapters;
using Meta.Core.Domain;
using MetaWorkspace = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

public sealed class InstanceDiffServiceTests
{
    [Fact]
    public void BuildEqualDiffWorkspace_AndApplyEqualDiffWorkspace_RoundTripsToRightSnapshot()
    {
        var left = CreateWorkspace(
            modelName: "PeopleModel",
            new[]
            {
                ("1", new Dictionary<string, string> { ["Name"] = "Alice", ["Age"] = "30" }),
            });
        var right = CreateWorkspace(
            modelName: "PeopleModel",
            new[]
            {
                ("1", new Dictionary<string, string> { ["Name"] = "Alice", ["Age"] = "31" }),
                ("2", new Dictionary<string, string> { ["Name"] = "Bob", ["Age"] = "40" }),
            });

        var services = new ServiceCollection();
        var diff = services.InstanceDiffService.BuildEqualDiffWorkspace(left, right, @".\RightWorkspace");

        Assert.True(diff.HasDifferences);
        Assert.Equal(1, diff.LeftRowCount);
        Assert.Equal(2, diff.RightRowCount);

        var target = CreateWorkspace(
            modelName: "PeopleModel",
            new[]
            {
                ("1", new Dictionary<string, string> { ["Name"] = "Alice", ["Age"] = "30" }),
            });

        services.InstanceDiffService.ApplyEqualDiffWorkspace(target, diff.DiffWorkspace);

        var targetRows = target.Instance.GetOrCreateEntityRecords("Person")
            .OrderBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.Equal(2, targetRows.Count);
        Assert.Equal("1", targetRows[0].Id);
        Assert.Equal("Alice", targetRows[0].Values["Name"]);
        Assert.Equal("31", targetRows[0].Values["Age"]);
        Assert.Equal("2", targetRows[1].Id);
        Assert.Equal("Bob", targetRows[1].Values["Name"]);
        Assert.Equal("40", targetRows[1].Values["Age"]);
    }

    private static Workspace CreateWorkspace(
        string modelName,
        IEnumerable<(string Id, Dictionary<string, string> Values)> rows)
    {
        var workspace = new Workspace
        {
            WorkspaceRootPath = @"C:\temp\meta-instance-diff-test",
            MetadataRootPath = @"C:\temp\meta-instance-diff-test\metadata",
            WorkspaceConfig = MetaWorkspace.CreateDefault(),
            Model = new GenericModel
            {
                Name = modelName,
                Entities =
                {
                    new GenericEntity
                    {
                        Name = "Person",
                        Properties =
                        {
                            new GenericProperty { Name = "Name", DataType = "string", IsNullable = false },
                            new GenericProperty { Name = "Age", DataType = "string", IsNullable = false },
                        },
                    },
                },
            },
            Instance = new GenericInstance
            {
                ModelName = modelName,
            },
            IsDirty = true,
        };

        var entityRows = workspace.Instance.GetOrCreateEntityRecords("Person");
        foreach (var row in rows)
        {
            var record = new GenericRecord
            {
                Id = row.Id,
            };

            foreach (var pair in row.Values)
            {
                record.Values[pair.Key] = pair.Value;
            }

            entityRows.Add(record);
        }

        return workspace;
    }
}
