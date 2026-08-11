using Meta.Core.Services;
using Meta.Operations.Domain;
using Meta.Operations;

public sealed class InstanceDiffServiceTests
{
    [Fact]
    public void BuildEqualDiffWorkspace_AndPlanEqualDiffMerge_RoundTripsToRightSnapshot()
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

        var service = new InstanceDiffService();
        var diff = service.BuildEqualDiffWorkspace(left, right);

        Assert.True(diff.HasDifferences);
        Assert.Equal(1, diff.LeftRowCount);
        Assert.Equal(2, diff.RightRowCount);

        var target = CreateWorkspace(
            modelName: "PeopleModel",
            new[]
            {
                ("1", new Dictionary<string, string> { ["Name"] = "Alice", ["Age"] = "30" }),
            });

        var operations = service.PlanEqualDiffMerge(
            target,
            diff.DiffWorkspace);
        var applied = InMemoryOperations.Apply(
            target,
            operations);

        var targetRows = applied.Instance.GetOrCreateEntityRecords("Person")
            .OrderBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.Equal(2, targetRows.Count);
        Assert.Equal("1", targetRows[0].Id);
        Assert.Equal("Alice", targetRows[0].Values["Name"]);
        Assert.Equal("31", targetRows[0].Values["Age"]);
        Assert.Equal("2", targetRows[1].Id);
        Assert.Equal("Bob", targetRows[1].Values["Name"]);
        Assert.Equal("40", targetRows[1].Values["Age"]);
        Assert.Single(target.Instance.GetOrCreateEntityRecords("Person"));
    }

    private static InMemoryWorkspace CreateWorkspace(
        string modelName,
        IEnumerable<(string Id, Dictionary<string, string> Values)> rows)
    {
        var model = new GenericModel
        {
            Name = modelName,
            Entities =
            {
                new GenericEntity
                {
                    Name = "Person",
                    Properties =
                    {
                        new GenericProperty { Name = "Name", IsNullable = false },
                        new GenericProperty { Name = "Age", IsNullable = false },
                    },
                },
            },
        };
        var workspace = new InMemoryWorkspace(
            model,
            new GenericInstance
            {
                ModelName = modelName,
            });

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
