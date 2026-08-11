using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Operations.Tests;

public sealed class WorkspaceSynchronizationTests
{
    [Fact]
    public void PlanCreation_ComposesOrdinaryOperationsFromAnEmptyModel()
    {
        var desired = BuildWorkspace();
        var empty = new InMemoryWorkspace(
            new GenericModel { Name = desired.Model.Name },
            new GenericInstance { ModelName = desired.Model.Name });

        var operations = WorkspaceSynchronization.PlanCreation(
            desired,
            desired.Model.Name);
        var result = InMemoryOperations.Apply(empty, operations);

        Assert.Contains(operations, operation => operation is Operation.AddEntity);
        Assert.Contains(operations, operation => operation is Operation.AddProperty);
        Assert.Contains(operations, operation => operation is Operation.AddRelationship);
        Assert.Contains(operations, operation => operation is Operation.InsertRecord);
        Assert.Null(InMemoryWorkspaceComparer.FindDifference(desired, result));
    }

    [Fact]
    public void PlanInstanceChanges_ComposesAReferentiallyValidTransition()
    {
        var current = BuildWorkspace();
        var desired = current.Clone();
        var teams = desired.Instance.RecordsByEntity["Team"];
        teams.Add(Record("team-c", "Gamma"));

        var people = desired.Instance.RecordsByEntity["Person"];
        var personA = people.Single(record => record.Id == "person-a");
        personA.Values["Name"] = "Alicia";
        personA.RelationshipIds["TeamId"] = "team-c";
        personA.RelationshipIds.Remove("PreviousPersonId");
        people.RemoveAll(record => record.Id == "person-b");
        teams.RemoveAll(record => record.Id == "team-b");

        var operations = WorkspaceSynchronization.PlanInstanceChanges(
            current,
            desired);

        Assert.Contains(operations, operation => operation is Operation.InsertRecord);
        Assert.Contains(operations, operation => operation is Operation.SetProperty);
        Assert.Contains(operations, operation => operation is Operation.SetRelationship);
        Assert.Contains(operations, operation => operation is Operation.ClearRelationship);
        Assert.Contains(operations, operation => operation is Operation.DeleteRecord);
        var result = InMemoryOperations.Apply(current, operations);
        Assert.Null(InMemoryWorkspaceComparer.FindDifference(desired, result));
        Assert.Null(InMemoryWorkspaceComparer.FindDifference(current, BuildWorkspace()));
    }

    [Fact]
    public void PlanInstanceChanges_InsertsRequiredTargetsBeforeDependents()
    {
        var current = BuildEmptyWorkspace();
        var desired = BuildWorkspace();

        var operations = WorkspaceSynchronization.PlanInstanceChanges(
            current,
            desired);

        var teamInsert = operations
            .Select((operation, index) => (operation, index))
            .Single(item => item.operation is Operation.InsertRecord insert &&
                            insert.EntityName == "Team" &&
                            insert.Id == "team-a");
        var personInsert = operations
            .Select((operation, index) => (operation, index))
            .Single(item => item.operation is Operation.InsertRecord insert &&
                            insert.EntityName == "Person" &&
                            insert.Id == "person-a");
        Assert.True(teamInsert.index < personInsert.index);
    }

    private static InMemoryWorkspace BuildEmptyWorkspace()
    {
        var workspace = BuildWorkspace();
        workspace.Instance.RecordsByEntity.Clear();
        return workspace;
    }

    private static InMemoryWorkspace BuildWorkspace()
    {
        var model = new GenericModel { Name = "People" };
        var team = new GenericEntity { Name = "Team" };
        team.Properties.Add(new GenericProperty
        {
            Name = "Name",
            IsNullable = false,
        });
        model.Entities.Add(team);

        var person = new GenericEntity { Name = "Person" };
        person.Properties.Add(new GenericProperty
        {
            Name = "Name",
            IsNullable = false,
        });
        person.Relationships.Add(new GenericRelationship
        {
            Entity = "Team",
            IsNullable = false,
        });
        person.Relationships.Add(new GenericRelationship
        {
            Entity = "Person",
            Role = "PreviousPerson",
            IsNullable = true,
        });
        model.Entities.Add(person);

        var instance = new GenericInstance { ModelName = model.Name };
        instance.GetOrCreateEntityRecords("Team").AddRange(
        [
            Record("team-a", "Alpha"),
            Record("team-b", "Beta"),
        ]);
        instance.GetOrCreateEntityRecords("Person").AddRange(
        [
            Record(
                "person-a",
                "Alice",
                new Dictionary<string, string>
                {
                    ["TeamId"] = "team-a",
                    ["PreviousPersonId"] = "person-b",
                }),
            Record(
                "person-b",
                "Bob",
                new Dictionary<string, string>
                {
                    ["TeamId"] = "team-b",
                }),
        ]);
        return new InMemoryWorkspace(model, instance);
    }

    private static GenericRecord Record(
        string id,
        string name,
        IReadOnlyDictionary<string, string>? relationships = null)
    {
        var record = new GenericRecord { Id = id };
        record.Values["Name"] = name;
        if (relationships != null)
        {
            foreach (var relationship in relationships)
            {
                record.RelationshipIds.Add(relationship.Key, relationship.Value);
            }
        }

        return record;
    }
}
