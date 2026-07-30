using Meta.Core.Operations;
using Meta.Core.Services;
using TypedOperationModel =
    Meta.Core.Tests.TypedMetaOperationSessionTests.TypedOperationModel;
using TypedPerson =
    Meta.Core.Tests.TypedMetaOperationSessionTests.TypedPerson;
using TypedTeam =
    Meta.Core.Tests.TypedMetaOperationSessionTests.TypedTeam;

namespace Meta.Core.Tests;

public sealed class TypedXmlMetaOperationSessionTests
{
    [Fact]
    public async Task OpenExisting_PersistsTypedOperationsThroughXmlSession()
    {
        var root = CreateTempDirectory();
        var workspacePath = Path.Combine(root, "TypedOperation.Workspace");
        try
        {
            var firstTeam = new TypedTeam
            {
                Id = "team-a",
                Name = "Team A",
            };
            new TypedOperationModel
            {
                TeamList = { firstTeam },
                PersonList =
                {
                    new TypedPerson
                    {
                        Id = "person-a",
                        Name = "Person A",
                        Team = firstTeam,
                    },
                },
            }.SaveToXmlWorkspace(workspacePath);

            var session =
                await TypedXmlMetaOperationSession<TypedOperationModel>
                    .OpenExistingAsync(workspacePath);
            var person = Assert.Single(session.Model.PersonList);
            var secondTeam = new TypedTeam
            {
                Id = "team-b",
                Name = "Team B",
            };

            session.Apply(plan => plan
                .Insert(secondTeam)
                .SetProperty(person, item => item.Name, "Renamed")
                .SetRelationship(person, item => item.Team, secondTeam));
            await session.CommitAsync();

            var reloaded =
                TypedOperationModel.LoadFromXmlWorkspace(workspacePath);
            var reloadedPerson = Assert.Single(reloaded.PersonList);
            Assert.Equal("Renamed", reloadedPerson.Name);
            Assert.Equal("team-b", reloadedPerson.Team.Id);
            Assert.Equal(2, reloaded.TeamList.Count);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task OpenLoaded_RejectsGeneratedGraphThatDiffersFromXml()
    {
        var root = CreateTempDirectory();
        var workspacePath = Path.Combine(root, "TypedOperation.Workspace");
        try
        {
            var team = new TypedTeam
            {
                Id = "team-a",
                Name = "Team A",
            };
            new TypedOperationModel
            {
                TeamList = { team },
            }.SaveToXmlWorkspace(workspacePath);
            var loaded =
                TypedOperationModel.LoadFromXmlWorkspace(workspacePath);
            Assert.Single(loaded.TeamList).Name = "Changed after load";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => TypedXmlMetaOperationSession<TypedOperationModel>
                    .OpenLoadedAsync(loaded, workspacePath));

            Assert.Contains(
                "represent different metadata states",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task Commit_RejectsStaleTypedWriter()
    {
        var root = CreateTempDirectory();
        var workspacePath = Path.Combine(root, "TypedOperation.Workspace");
        try
        {
            var team = new TypedTeam
            {
                Id = "team-a",
                Name = "Team A",
            };
            new TypedOperationModel
            {
                TeamList = { team },
            }.SaveToXmlWorkspace(workspacePath);

            var first =
                await TypedXmlMetaOperationSession<TypedOperationModel>
                    .OpenExistingAsync(workspacePath);
            var stale =
                await TypedXmlMetaOperationSession<TypedOperationModel>
                    .OpenExistingAsync(workspacePath);

            first.Apply(plan => plan.SetProperty(
                Assert.Single(first.Model.TeamList),
                item => item.Name,
                "First writer"));
            await first.CommitAsync();

            stale.Apply(plan => plan.SetProperty(
                Assert.Single(stale.Model.TeamList),
                item => item.Name,
                "Stale writer"));
            await Assert.ThrowsAsync<WorkspaceConflictException>(
                () => stale.CommitAsync());

            var reloaded =
                TypedOperationModel.LoadFromXmlWorkspace(workspacePath);
            Assert.Equal(
                "First writer",
                Assert.Single(reloaded.TeamList).Name);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task RejectedPlan_PreservesEarlierAcceptedTypedPlan()
    {
        var root = CreateTempDirectory();
        var workspacePath = Path.Combine(root, "TypedOperation.Workspace");
        try
        {
            new TypedOperationModel
            {
                TeamList =
                {
                    new TypedTeam
                    {
                        Id = "team-a",
                        Name = "Team A",
                    },
                },
            }.SaveToXmlWorkspace(workspacePath);
            var session =
                await TypedXmlMetaOperationSession<TypedOperationModel>
                    .OpenExistingAsync(workspacePath);
            var team = Assert.Single(session.Model.TeamList);
            var firstPerson = new TypedPerson
            {
                Id = "person-a",
                Name = "Person A",
                Team = team,
            };

            session.Apply(plan => plan.Insert(firstPerson));

            var secondPerson = new TypedPerson
            {
                Id = "person-b",
                Name = "Person B",
                Team = team,
            };
            var duplicateTeam = new TypedTeam
            {
                Id = "TEAM-A",
                Name = "Duplicate",
            };
            Assert.Throws<MetaOperationException>(() => session.Apply(plan =>
                plan
                    .Insert(secondPerson)
                    .Insert(duplicateTeam)));

            Assert.Same(firstPerson, Assert.Single(session.Model.PersonList));
            await session.CommitAsync();

            var reloaded =
                TypedOperationModel.LoadFromXmlWorkspace(workspacePath);
            Assert.Equal(
                "person-a",
                Assert.Single(reloaded.PersonList).Id);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task Discard_RestoresGeneratedObjectsAndLeavesXmlUntouched()
    {
        var root = CreateTempDirectory();
        var workspacePath = Path.Combine(root, "TypedOperation.Workspace");
        try
        {
            new TypedOperationModel
            {
                TeamList =
                {
                    new TypedTeam
                    {
                        Id = "team-a",
                        Name = "Team A",
                    },
                },
            }.SaveToXmlWorkspace(workspacePath);
            var session =
                await TypedXmlMetaOperationSession<TypedOperationModel>
                    .OpenExistingAsync(workspacePath);
            var team = Assert.Single(session.Model.TeamList);

            session.Apply(plan => plan.SetProperty(
                team,
                item => item.Name,
                "Pending"));
            session.Discard();

            Assert.Same(team, Assert.Single(session.Model.TeamList));
            Assert.Equal("Team A", team.Name);
            var reloaded =
                TypedOperationModel.LoadFromXmlWorkspace(workspacePath);
            Assert.Equal("Team A", Assert.Single(reloaded.TeamList).Name);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task XmlSession_LowersAllSixTypedInstanceOperations()
    {
        var root = CreateTempDirectory();
        var workspacePath = Path.Combine(root, "TypedOperation.Workspace");
        try
        {
            var firstTeam = new TypedTeam
            {
                Id = "team-a",
                Name = "Team A",
            };
            var secondTeam = new TypedTeam
            {
                Id = "team-b",
                Name = "Team B",
            };
            var deletedTeam = new TypedTeam
            {
                Id = "team-delete",
                Name = "Delete",
            };
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
                Note = "clear me",
                Team = firstTeam,
                Mentor = mentor,
            };
            new TypedOperationModel
            {
                TeamList = { firstTeam, secondTeam, deletedTeam },
                PersonList = { mentor, person },
            }.SaveToXmlWorkspace(workspacePath);
            var session =
                await TypedXmlMetaOperationSession<TypedOperationModel>
                    .OpenExistingAsync(workspacePath);
            secondTeam = session.Model.TeamList.Single(item =>
                item.Id == "team-b");
            deletedTeam = session.Model.TeamList.Single(item =>
                item.Id == "team-delete");
            person = session.Model.PersonList.Single(item =>
                item.Id == "person-a");
            var insertedTeam = new TypedTeam
            {
                Id = "team-inserted",
                Name = "Inserted",
            };

            session.Apply(plan => plan
                .Insert(insertedTeam)
                .SetProperty(person, item => item.Name, "Renamed")
                .ClearProperty(person, item => item.Note)
                .SetRelationship(person, item => item.Team, secondTeam)
                .ClearRelationship<TypedPerson, TypedPerson>(
                    person,
                    item => item.Mentor)
                .Delete(deletedTeam));
            await session.CommitAsync();

            var reloaded =
                TypedOperationModel.LoadFromXmlWorkspace(workspacePath);
            var reloadedPerson = reloaded.PersonList.Single(item =>
                item.Id == "person-a");
            Assert.Equal("Renamed", reloadedPerson.Name);
            Assert.Null(reloadedPerson.Note);
            Assert.Equal("team-b", reloadedPerson.Team.Id);
            Assert.Null(reloadedPerson.Mentor);
            Assert.Contains(
                reloaded.TeamList,
                item => item.Id == "team-inserted");
            Assert.DoesNotContain(
                reloaded.TeamList,
                item => item.Id == "team-delete");
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "meta-typed-operation-xml",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
