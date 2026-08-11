using Meta.Operations;
using Meta.Operations.Domain;

namespace Meta.Operations.Tests;

public sealed class GenericInstanceTests
{
    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var instance = new GenericInstance
        {
            ModelName = "People",
        };
        var record = new GenericRecord
        {
            Id = "person-1",
        };
        record.Values.Add("Name", "Original");
        record.RelationshipIds.Add("TeamId", "team-1");
        instance.GetOrCreateEntityRecords("Person").Add(record);

        var clone = instance.Clone();
        var clonedRecord = Assert.Single(
            clone.RecordsByEntity["Person"]);
        clone.ModelName = "PeopleClone";
        clonedRecord.Id = "person-2";
        clonedRecord.Values["Name"] = "Changed";
        clonedRecord.RelationshipIds["TeamId"] = "team-2";

        Assert.Equal("People", instance.ModelName);
        var original = Assert.Single(
            instance.RecordsByEntity["Person"]);
        Assert.Equal("person-1", original.Id);
        Assert.Equal("Original", original.Values["Name"]);
        Assert.Equal("team-1", original.RelationshipIds["TeamId"]);
    }
}
