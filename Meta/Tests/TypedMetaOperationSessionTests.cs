using System.Xml.Serialization;
using Meta.Core.Operations;
using Meta.Core.Serialization;

namespace Meta.Core.Tests;

public sealed class TypedMetaOperationSessionTests
{
    [Fact]
    public void Apply_ExecutesOrderedTypedInsertsThroughGenericSemantics()
    {
        var model = TypedOperationModel.CreateEmpty();
        var team = new TypedTeam
        {
            Id = "team-a",
            Name = "Team A",
        };
        var person = new TypedPerson
        {
            Id = "person-a",
            Name = "Person A",
            Team = team,
        };
        var session = new TypedMetaOperationSession<TypedOperationModel>(model);

        var result = session.Apply(plan => plan
            .Insert(team)
            .Insert(person));

        Assert.Equal(2, result.AppliedOperationCount);
        Assert.Same(team, Assert.Single(model.TeamList));
        Assert.Same(person, Assert.Single(model.PersonList));
        Assert.Same(team, person.Team);
    }

    [Fact]
    public void Apply_SetsAndClearsTypedPropertiesAndRelationships()
    {
        var firstTeam = new TypedTeam { Id = "team-a", Name = "Team A" };
        var secondTeam = new TypedTeam { Id = "team-b", Name = "Team B" };
        var mentor = new TypedPerson
        {
            Id = "person-mentor",
            Name = "Mentor",
            Team = firstTeam,
        };
        var person = new TypedPerson
        {
            Id = "person-a",
            Name = "Person A",
            Team = firstTeam,
        };
        var model = new TypedOperationModel
        {
            TeamList = { firstTeam, secondTeam },
            PersonList = { mentor, person },
        };
        var session = new TypedMetaOperationSession<TypedOperationModel>(model);

        session.Apply(plan => plan
            .SetProperty(person, item => item.Name, "Renamed")
            .SetProperty(person, item => item.Note, "review")
            .SetRelationship(person, item => item.Team, secondTeam)
            .SetRelationship(person, item => item.Mentor, mentor));

        Assert.Equal("Renamed", person.Name);
        Assert.Equal("review", person.Note);
        Assert.Same(secondTeam, person.Team);
        Assert.Same(mentor, person.Mentor);

        session.Apply(plan => plan
            .ClearProperty(person, item => item.Note)
            .ClearRelationship<TypedPerson, TypedPerson>(
                person,
                item => item.Mentor));

        Assert.Null(person.Note);
        Assert.Null(person.Mentor);
    }

    [Fact]
    public void Apply_RejectedLaterOperationRestoresTheWholeTypedPlan()
    {
        var team = new TypedTeam { Id = "team-a", Name = "Team A" };
        var model = new TypedOperationModel
        {
            TeamList = { team },
        };
        var person = new TypedPerson
        {
            Id = "person-a",
            Name = "Person A",
            Team = team,
        };
        var duplicateTeam = new TypedTeam
        {
            Id = "TEAM-A",
            Name = "Duplicate",
        };
        var session = new TypedMetaOperationSession<TypedOperationModel>(model);

        var exception = Assert.Throws<MetaOperationException>(
            () => session.Apply(plan => plan
                .Insert(person)
                .Insert(duplicateTeam)));

        Assert.Equal(1, exception.OperationIndex);
        Assert.Same(team, Assert.Single(model.TeamList));
        Assert.Empty(model.PersonList);

        session.Apply(plan => plan.Insert(person));
        Assert.Same(person, Assert.Single(model.PersonList));
    }

    [Fact]
    public void Apply_UsesGenericReferenceIntegrityForTypedDeletes()
    {
        var team = new TypedTeam { Id = "team-a", Name = "Team A" };
        var person = new TypedPerson
        {
            Id = "person-a",
            Name = "Person A",
            Team = team,
        };
        var model = new TypedOperationModel
        {
            TeamList = { team },
            PersonList = { person },
        };
        var session = new TypedMetaOperationSession<TypedOperationModel>(model);

        Assert.Throws<MetaOperationException>(
            () => session.Apply(plan => plan.Delete(team)));
        Assert.Same(team, Assert.Single(model.TeamList));
        Assert.Same(person, Assert.Single(model.PersonList));

        session.Apply(plan => plan
            .Delete(person)
            .Delete(team));

        Assert.Empty(model.PersonList);
        Assert.Empty(model.TeamList);
    }

    [Fact]
    public void Apply_RejectsMutationOutsideTheTypedSession()
    {
        var team = new TypedTeam { Id = "team-a", Name = "Team A" };
        var model = new TypedOperationModel
        {
            TeamList = { team },
        };
        var session = new TypedMetaOperationSession<TypedOperationModel>(model);
        team.Name = "Changed directly";

        var exception = Assert.Throws<InvalidOperationException>(
            () => session.Apply(_ => { }));

        Assert.Contains(
            "changed outside this operation session",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal("Changed directly", team.Name);
    }

    [Fact]
    public void Apply_RejectsInsertRowChangedAfterPlanCreation()
    {
        var team = new TypedTeam { Id = "team-a", Name = "Team A" };
        var model = new TypedOperationModel
        {
            TeamList = { team },
        };
        var person = new TypedPerson
        {
            Id = "person-a",
            Name = "Original",
            Team = team,
        };
        var plan = TypedMetaOperationPlan<TypedOperationModel>.Create(
            builder => builder.Insert(person));
        person.Name = "Changed";
        var session = new TypedMetaOperationSession<TypedOperationModel>(model);

        var exception = Assert.Throws<InvalidOperationException>(
            () => session.Apply(plan));

        Assert.Contains(
            "changed after its typed operation plan was created",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(model.PersonList);
    }

    [Fact]
    public void Apply_RejectsExistingRowReidentifiedAfterPlanCreation()
    {
        var team = new TypedTeam { Id = "team-a", Name = "Team A" };
        var model = new TypedOperationModel
        {
            TeamList = { team },
        };
        var plan = TypedMetaOperationPlan<TypedOperationModel>.Create(
            builder => builder.SetProperty(
                team,
                item => item.Name,
                "Renamed"));
        team.Id = "team-b";
        var session = new TypedMetaOperationSession<TypedOperationModel>(model);

        var exception = Assert.Throws<InvalidOperationException>(
            () => session.Apply(plan));

        Assert.Contains(
            "changed identity after its typed operation plan was created",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal("Team A", team.Name);
    }

    [Fact]
    public void Apply_UsesExplicitClearAndRejectsClearingRequiredProperty()
    {
        var team = new TypedTeam { Id = "team-a", Name = "Team A" };
        var model = new TypedOperationModel
        {
            TeamList = { team },
        };
        var session = new TypedMetaOperationSession<TypedOperationModel>(model);

        Assert.Throws<MetaOperationException>(() => session.Apply(
            plan => plan.ClearProperty(team, item => item.Name)));

        Assert.Equal("Team A", team.Name);
    }

    [Fact]
    public void CommitAndDiscardMaintainTheTypedSessionBaseline()
    {
        var model = TypedOperationModel.CreateEmpty();
        var firstTeam = new TypedTeam { Id = "team-a", Name = "Team A" };
        var secondTeam = new TypedTeam { Id = "team-b", Name = "Team B" };
        var session = new TypedMetaOperationSession<TypedOperationModel>(model);

        session.Apply(plan => plan.Insert(firstTeam));
        session.Discard();
        Assert.Empty(model.TeamList);

        session.Apply(plan => plan.Insert(firstTeam));
        session.Commit();
        session.Apply(plan => plan.Insert(secondTeam));
        session.Discard();

        Assert.Same(firstTeam, Assert.Single(model.TeamList));
    }

    [XmlRoot("TypedOperation")]
    public sealed class TypedOperationModel :
        IMetaWorkspaceModel<TypedOperationModel>
    {
        [XmlArray("TeamList")]
        [XmlArrayItem("Team")]
        public List<TypedTeam> TeamList { get; set; } = [];

        [XmlArray("PersonList")]
        [XmlArrayItem("Person")]
        public List<TypedPerson> PersonList { get; set; } = [];

        public static TypedOperationModel CreateEmpty()
        {
            return new TypedOperationModel();
        }

        public static TypedOperationModel LoadFromXmlWorkspace(
            string workspacePath,
            bool searchUpward = false)
        {
            return TypedWorkspaceXmlSerializer.Load<TypedOperationModel>(
                workspacePath,
                searchUpward);
        }

        public static Task<TypedOperationModel> LoadFromXmlWorkspaceAsync(
            string workspacePath,
            bool searchUpward = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                LoadFromXmlWorkspace(workspacePath, searchUpward));
        }

        public void SaveToXmlWorkspace(string workspacePath)
        {
            TypedWorkspaceXmlSerializer.Save(this, workspacePath);
        }

        public Task SaveToXmlWorkspaceAsync(
            string workspacePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveToXmlWorkspace(workspacePath);
            return Task.CompletedTask;
        }
    }

    public sealed class TypedTeam
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public sealed class TypedPerson
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Note { get; set; }
        public TypedTeam Team { get; set; } = null!;
        public TypedPerson? Mentor { get; set; }
    }
}
