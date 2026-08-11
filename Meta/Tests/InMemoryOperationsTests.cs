using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Core.Services;
using System.Xml.Linq;

namespace Meta.Core.Tests;

public sealed class InMemoryOperationsTests
{
    [Fact]
    public void Apply_UsesConcreteOperationsAndLeavesTheSourceUnchanged()
    {
        var source = BuildState();

        var result = InMemoryOperations.Apply(
            source,
            new Operation.AddEntity("Audit"),
            new Operation.AddProperty(
                "Audit",
                "Message",
                IsRequired: true),
            new Operation.InsertRecord(
                "Audit",
                "audit-1",
                new Dictionary<string, string>
                {
                    ["Message"] = "Created",
                }),
            new Operation.SetProperty(
                "Person",
                "person-a",
                "Name",
                "Updated"),
            new Operation.ClearProperty(
                "Person",
                "person-a",
                "Note"),
            new Operation.SetRelationship(
                "Person",
                "person-a",
                "Team",
                "TEAM-B"));

        Assert.Null(source.Model.FindEntity("Audit"));
        var sourcePerson = Assert.Single(
            source.Instance.RecordsByEntity["Person"]);
        Assert.Equal("Original", sourcePerson.Values["Name"]);
        Assert.Equal("Original note", sourcePerson.Values["Note"]);
        Assert.Equal("team-a", sourcePerson.RelationshipIds["TeamId"]);

        var audit = Assert.Single(
            result.Instance.RecordsByEntity["Audit"]);
        Assert.Equal("Created", audit.Values["Message"]);

        var person = Assert.Single(
            result.Instance.RecordsByEntity["Person"]);
        Assert.Equal("Updated", person.Values["Name"]);
        Assert.False(person.Values.ContainsKey("Note"));
        Assert.Equal("team-b", person.RelationshipIds["TeamId"]);
        Assert.False(WorkspaceValidator.Validate(
                result.Model,
                result.Instance)
            .HasErrors);
    }

    [Fact]
    public void ApplyBatch_ValidatesTheFinalCandidateAndLeavesTheSourceUnchanged()
    {
        var source = BuildState();

        var result = InMemoryOperations.ApplyBatch(
            source,
            [
                new Operation.AddEntity("Audit"),
                new Operation.AddProperty(
                    "Audit",
                    "Message",
                    IsRequired: true),
                new Operation.InsertRecord(
                    "Audit",
                    "audit-1",
                    new Dictionary<string, string>
                    {
                        ["Message"] = "Created",
                    }),
            ]);

        Assert.Equal("Created", Assert.Single(
            result.Instance.RecordsByEntity["Audit"]).Values["Message"]);
        Assert.Null(source.Model.FindEntity("Audit"));
        Assert.False(WorkspaceValidator.Validate(
                result.Model,
                result.Instance)
            .HasErrors);
    }

    [Fact]
    public void Apply_RejectsCaseInsensitiveCollisionWithoutChangingSource()
    {
        var source = BuildState();

        var exception = Assert.Throws<MetaOperationException>(
            () => InMemoryOperations.Apply(
                source,
                new Operation.InsertRecord(
                    "Person",
                    "PERSON-A",
                    new Dictionary<string, string>
                    {
                        ["Name"] = "Duplicate",
                    },
                    new Dictionary<string, string>
                    {
                        ["Team"] = "team-a",
                    })));

        Assert.Equal(0, exception.OperationIndex);
        Assert.IsType<Operation.InsertRecord>(exception.Operation);
        Assert.Single(source.Instance.RecordsByEntity["Person"]);
    }

    [Fact]
    public void RenameRecord_PreservesAuthoredCasingAndUpdatesReferences()
    {
        var source = BuildState();

        var result = InMemoryOperations.Apply(
            source,
            new Operation.RenameRecord(
                "Team",
                "team-a",
                "Team-A"));

        var team = Assert.Single(
            result.Instance.RecordsByEntity["Team"],
            record => MetaIdentity.Comparer.Equals(
                record.Id,
                "team-a"));
        Assert.Equal("Team-A", team.Id);
        var person = Assert.Single(
            result.Instance.RecordsByEntity["Person"]);
        Assert.Equal("Team-A", person.RelationshipIds["TeamId"]);
    }

    [Fact]
    public void DeleteRecord_AllowsAnOptionalSelfReferenceToTheDeletedRecord()
    {
        var source = BuildState();

        var result = InMemoryOperations.Apply(
            source,
            new Operation.AddRelationship(
                "Person",
                "Person",
                "Manager",
                IsRequired: false),
            new Operation.SetRelationship(
                "Person",
                "person-a",
                "Manager",
                "person-a"),
            new Operation.DeleteRecord(
                "Person",
                "person-a"));

        Assert.Empty(result.Instance.RecordsByEntity["Person"]);
    }

    [Fact]
    public void RenameRelationship_ChangesTheRoleAndStoredUsages()
    {
        var source = BuildState();

        var result = InMemoryOperations.Apply(
            source,
            new Operation.RenameRelationship(
                "Person",
                "Team",
                "AssignedTeam"));

        var person = Assert.Single(result.Model.Entities, entity =>
            MetaName.Comparer.Equals(entity.Name, "Person"));
        var relationship = Assert.Single(person.Relationships);
        Assert.Equal("AssignedTeam", relationship.Role);
        var record = Assert.Single(
            result.Instance.RecordsByEntity["Person"]);
        Assert.False(record.RelationshipIds.ContainsKey("TeamId"));
        Assert.Equal(
            "team-a",
            record.RelationshipIds["AssignedTeamId"]);
    }

    [Fact]
    public void RetargetRelationship_PreservesReferencesAndDefaultUsage()
    {
        var source = BuildState();

        var result = InMemoryOperations.Apply(
            source,
            new Operation.AddEntity("Department"),
            new Operation.InsertRecord("Department", "team-a"),
            new Operation.RetargetRelationship(
                "Person",
                "Team",
                "Department"));

        var person = Assert.Single(result.Model.Entities, entity =>
            MetaName.Comparer.Equals(entity.Name, "Person"));
        var relationship = Assert.Single(person.Relationships);
        Assert.Equal("Department", relationship.Entity);
        Assert.Equal(string.Empty, relationship.Role);
        var record = Assert.Single(
            result.Instance.RecordsByEntity["Person"]);
        Assert.False(record.RelationshipIds.ContainsKey("TeamId"));
        Assert.Equal(
            "team-a",
            record.RelationshipIds["DepartmentId"]);
    }

    [Fact]
    public void SetRelationshipRequired_FillsMissingReferences()
    {
        var source = BuildState();

        var result = InMemoryOperations.Apply(
            source,
            new Operation.SetRelationshipRequired(
                "Person",
                "Team",
                IsRequired: false),
            new Operation.ClearRelationship(
                "Person",
                "person-a",
                "Team"),
            new Operation.SetRelationshipRequired(
                "Person",
                "Team",
                IsRequired: true,
                MissingRecordTargetId: "team-b"));

        var person = Assert.Single(result.Model.Entities, entity =>
            MetaName.Comparer.Equals(entity.Name, "Person"));
        Assert.False(Assert.Single(person.Relationships).IsNullable);
        var record = Assert.Single(
            result.Instance.RecordsByEntity["Person"]);
        Assert.Equal("team-b", record.RelationshipIds["TeamId"]);
    }

    [Fact]
    public void AddEntity_DoesNotCreateStorageOrRewriteInstances()
    {
        var source = BuildState();
        var sourceInstance = InstanceXml(
            source.Model,
            source.Instance);

        var result = InMemoryOperations.Apply(
            source,
            new Operation.AddEntity("Audit"));

        Assert.False(result.Instance.RecordsByEntity.ContainsKey("Audit"));
        Assert.Equal(
            sourceInstance,
            InstanceXml(source.Model, result.Instance));
    }

    [Fact]
    public void StateComparer_DistinguishesMissingAndEmptyPropertyValues()
    {
        var missing = BuildState();
        var empty = BuildState();
        empty.Instance.RecordsByEntity["Person"][0].Values["Note"] =
            string.Empty;
        missing.Instance.RecordsByEntity["Person"][0].Values.Remove("Note");

        var difference = InMemoryWorkspaceComparer.FindDifference(missing, empty);

        Assert.NotNull(difference);
        Assert.Contains("properties differ", difference);
    }

    [Fact]
    public void StateComparer_TreatsRelationshipIdentitySpellingAsAReference()
    {
        var left = BuildState();
        var right = BuildState();
        right.Instance.RecordsByEntity["Person"][0]
            .RelationshipIds["TeamId"] = "TEAM-A";

        Assert.Null(InMemoryWorkspaceComparer.FindDifference(left, right));
    }

    private static InMemoryWorkspace BuildState()
    {
        var model = new GenericModel
        {
            Name = "People",
        };
        var team = new GenericEntity
        {
            Name = "Team",
        };
        team.Properties.Add(new GenericProperty
        {
            Name = "Name",
            IsNullable = false,
        });
        model.Entities.Add(team);

        var person = new GenericEntity
        {
            Name = "Person",
        };
        person.Properties.Add(new GenericProperty
        {
            Name = "Name",
            IsNullable = false,
        });
        person.Properties.Add(new GenericProperty
        {
            Name = "Note",
            IsNullable = true,
        });
        person.Relationships.Add(new GenericRelationship
        {
            Entity = "Team",
            IsNullable = false,
        });
        model.Entities.Add(person);

        var instance = new GenericInstance
        {
            ModelName = model.Name,
        };
        instance.GetOrCreateEntityRecords("Team").Add(
            Record(
                "team-a",
                ("Name", "Alpha")));
        instance.GetOrCreateEntityRecords("Team").Add(
            Record(
                "team-b",
                ("Name", "Beta")));

        var personRecord = Record(
            "person-a",
            ("Name", "Original"),
            ("Note", "Original note"));
        personRecord.RelationshipIds.Add("TeamId", "team-a");
        instance.GetOrCreateEntityRecords("Person").Add(personRecord);

        return new InMemoryWorkspace(model, instance);
    }

    private static GenericRecord Record(
        string id,
        params (string Name, string Value)[] values)
    {
        var record = new GenericRecord
        {
            Id = id,
        };
        foreach (var value in values)
        {
            record.Values.Add(value.Name, value.Value);
        }

        return record;
    }

    private static string InstanceXml(
        GenericModel model,
        GenericInstance instance)
    {
        return Meta.Surfaces.Xml.InstanceXmlCodec
            .BuildDocument(model, instance)
            .ToString(SaveOptions.DisableFormatting);
    }
}
