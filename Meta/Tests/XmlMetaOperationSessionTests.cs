using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Services;
using MetaWorkspace = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace Meta.Core.Tests;

public sealed class XmlMetaOperationSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task XmlSession_ProducesReferenceInterpreterState(
        bool schemaRefactors)
    {
        var root = CreateTempDirectory();
        try
        {
            var workspaceService = new WorkspaceService();
            var source = MetaOperationInterpreterTests.BuildState();
            var plan = schemaRefactors
                ? MetaOperationInterpreterTests.BuildSchemaRefactorPlan()
                : MetaOperationInterpreterTests.BuildPlan();
            await SaveInitialWorkspaceAsync(root, source, workspaceService);
            var expected = new MetaOperationInterpreter()
                .Apply(source, plan)
                .State;

            var session = await XmlMetaOperationSession.OpenExistingAsync(
                root,
                workspaceService);
            session.Apply(plan);
            await session.CommitAsync();

            var reloaded = await workspaceService.LoadAsync(root);
            var actual = GenericMetadataState.Capture(reloaded);
            Assert.Equal(
                MetaOperationInterpreterTests.Canonicalize(expected),
                MetaOperationInterpreterTests.Canonicalize(actual));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task XmlSession_RejectedPlanLeavesSessionAndFilesUnchanged()
    {
        var root = CreateTempDirectory();
        try
        {
            var workspaceService = new WorkspaceService();
            await SaveInitialWorkspaceAsync(
                root,
                MetaOperationInterpreterTests.BuildState(),
                workspaceService);
            var session = await XmlMetaOperationSession.OpenExistingAsync(
                root,
                workspaceService);
            var stateBefore = MetaOperationInterpreterTests.Canonicalize(
                session.Snapshot());
            var filesBefore = ReadFiles(root);

            var rejected = MetaOperationPlan.Create(
                new SetPropertyOperation(
                    "Person",
                    "person-a",
                    "LegacyName",
                    "MustNotPublish"),
                new InsertRecordOperation(
                    "Person",
                    "PERSON-A",
                    new Dictionary<string, string>
                    {
                        ["LegacyName"] = "Duplicate",
                    }));

            Assert.Throws<MetaOperationException>(() => session.Apply(rejected));
            Assert.Equal(
                stateBefore,
                MetaOperationInterpreterTests.Canonicalize(session.Snapshot()));
            AssertFilesEqual(filesBefore, ReadFiles(root));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task XmlSession_RejectsStaleCommitBeforeOverwritingWorkspace()
    {
        var root = CreateTempDirectory();
        try
        {
            var workspaceService = new WorkspaceService();
            await SaveInitialWorkspaceAsync(
                root,
                MetaOperationInterpreterTests.BuildState(),
                workspaceService);

            var first = await XmlMetaOperationSession.OpenExistingAsync(
                root,
                workspaceService);
            var stale = await XmlMetaOperationSession.OpenExistingAsync(
                root,
                workspaceService);

            first.Apply(MetaOperationPlan.Create(
                new SetPropertyOperation(
                    "Person",
                    "person-a",
                    "LegacyName",
                    "First writer")));
            await first.CommitAsync();

            stale.Apply(MetaOperationPlan.Create(
                new SetPropertyOperation(
                    "Person",
                    "person-a",
                    "LegacyName",
                    "Stale writer")));
            await Assert.ThrowsAsync<WorkspaceConflictException>(
                () => stale.CommitAsync());

            var reloaded = await workspaceService.LoadAsync(root);
            var person = Assert.Single(
                reloaded.Instance.RecordsByEntity["Person"]);
            Assert.Equal("First writer", person.Values["LegacyName"]);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    private static async Task SaveInitialWorkspaceAsync(
        string root,
        GenericMetadataState state,
        IWorkspaceService workspaceService)
    {
        var workspace = new Workspace
        {
            WorkspaceRootPath = root,
            MetadataRootPath = root,
            WorkspaceConfig = MetaWorkspace.CreateDefault(),
            Model = state.Model.Clone(),
            Instance = WorkspaceSnapshotCloner.CloneInstance(state.Instance),
            IsDirty = true,
        };
        await workspaceService.SaveAsync(workspace);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "meta-operation-xml",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static IReadOnlyDictionary<string, byte[]> ReadFiles(string root)
    {
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                path => Path.GetRelativePath(root, path),
                File.ReadAllBytes,
                StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertFilesEqual(
        IReadOnlyDictionary<string, byte[]> expected,
        IReadOnlyDictionary<string, byte[]> actual)
    {
        Assert.Equal(
            expected.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            actual.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

        foreach (var path in expected.Keys)
        {
            Assert.True(
                expected[path].AsSpan().SequenceEqual(actual[path]),
                $"File '{path}' changed.");
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
