using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Surfaces.CSharp;
using Meta.Surfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Meta.Core.Tests;

public sealed class CSharpWorkspaceOwnershipTests
{
    private static readonly SemaphoreSlim PublicationTestGate = new(1, 1);

    [Fact]
    public async Task OpeningANonexistentWorkspaceCreatesNothing()
    {
        using var fixture = ProjectFixture.CreateEmpty();
        Directory.Delete(fixture.Root, recursive: true);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
            await CSharpWorkspace.OpenAsync(fixture.Root));

        Assert.False(Directory.Exists(fixture.Root));
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".meta.lock")));
    }

    [Fact]
    public async Task OpeningACSharpWorkspaceDoesNotCreateAWriteLock()
    {
        using var fixture = await ProjectFixture.CreateAsync();
        var lockPath = Path.Combine(fixture.Root, ".meta.lock");

        Assert.False(File.Exists(lockPath));
        await using var workspace = await CSharpWorkspace.OpenAsync(fixture.Root);

        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public async Task IndependentReadersDoNotUseAnExclusiveReaderLock()
    {
        using var fixture = await ProjectFixture.CreateAsync();

        var readers = await Task.WhenAll(
            CSharpWorkspace.OpenAsync(fixture.Root),
            CSharpWorkspace.OpenAsync(fixture.Root));

        try
        {
            Assert.Equal("Demo", await readers[0].ReadModelNameAsync());
            Assert.Equal("Demo", await readers[1].ReadModelNameAsync());
            Assert.False(File.Exists(Path.Combine(fixture.Root, ".meta.lock")));
        }
        finally
        {
            await readers[0].DisposeAsync();
            await readers[1].DisposeAsync();
        }
    }

    [Fact]
    public async Task CreationInsideProjectReadsOnlyDeclaredSourceAndPreservesProjectFiles()
    {
        using var fixture = await ProjectFixture.CreateAsync();
        var before = fixture.ReadUnownedFiles();

        await using var workspace = await CSharpWorkspace.OpenAsync(fixture.Root);

        Assert.Equal(["Demo.meta.cs"], WorkspaceMetaFile.Read(fixture.Root).Sources);
        Assert.Equal("Demo", await workspace.ReadModelNameAsync());
        Assert.Equal(before, fixture.ReadUnownedFiles());
    }

    [Fact]
    public async Task CreationRejectsExistingDescriptorOrOwnedTarget()
    {
        using var descriptorFixture = ProjectFixture.CreateEmpty();
        await File.WriteAllTextAsync(
            Path.Combine(descriptorFixture.Root, WorkspaceMetaFile.FileName),
            "representation xml\n");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CSharpWorkspace.CreateAsync(DemoState(), descriptorFixture.Root));

        using var targetFixture = ProjectFixture.CreateEmpty();
        var targetPath = Path.Combine(targetFixture.Root, "Demo.meta.cs");
        await File.WriteAllTextAsync(targetPath, "user-owned source");

        await Assert.ThrowsAsync<IOException>(async () =>
            await CSharpWorkspace.CreateAsync(DemoState(), targetFixture.Root));

        Assert.Equal("user-owned source", await File.ReadAllTextAsync(targetPath));
        Assert.False(File.Exists(
            Path.Combine(targetFixture.Root, WorkspaceMetaFile.FileName)));
    }

    [Fact]
    public async Task MutationPreservesUnownedFilesAndIgnoresConflictingSources()
    {
        using var fixture = await ProjectFixture.CreateAsync();
        var before = fixture.ReadUnownedFiles();

        await using (var workspace = await CSharpWorkspace.OpenAsync(fixture.Root))
        {
            await workspace.ExecuteAsync([
                new Operation.SetProperty("Node", "Root", "RequiredText", "Changed"),
            ]);
        }

        Assert.Equal(before, fixture.ReadUnownedFiles());
        Assert.Contains(
            "Changed",
            await File.ReadAllTextAsync(Path.Combine(fixture.Root, "Demo.meta.cs")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditingAnUnownedFileDoesNotConflict()
    {
        using var fixture = await ProjectFixture.CreateAsync();
        await using var workspace = await CSharpWorkspace.OpenAsync(fixture.Root);
        var programPath = Path.Combine(fixture.Root, "Program.cs");
        await File.AppendAllTextAsync(programPath, "\n// user change\n");

        await workspace.ExecuteAsync([
            new Operation.SetProperty("Node", "Root", "RequiredText", "Changed"),
        ]);

        Assert.Contains(
            "user change",
            await File.ReadAllTextAsync(programPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditingAnOwnedSourceConflicts()
    {
        using var fixture = await ProjectFixture.CreateAsync();
        await using var workspace = await CSharpWorkspace.OpenAsync(fixture.Root);
        await File.AppendAllTextAsync(
            Path.Combine(fixture.Root, "Demo.meta.cs"),
            "\n// ownership conflict\n");

        await Assert.ThrowsAsync<WorkspaceConflictException>(async () =>
            await workspace.ExecuteAsync([
                new Operation.SetProperty("Node", "Root", "RequiredText", "Changed"),
            ]));
    }

    [Fact]
    public async Task EditingTheOwnershipDeclarationConflicts()
    {
        using var fixture = await ProjectFixture.CreateAsync();
        await using var workspace = await CSharpWorkspace.OpenAsync(fixture.Root);
        await File.AppendAllTextAsync(
            Path.Combine(fixture.Root, WorkspaceMetaFile.FileName),
            "source Missing.meta.cs\n");

        await Assert.ThrowsAsync<WorkspaceConflictException>(async () =>
            await workspace.ExecuteAsync([
                new Operation.SetProperty("Node", "Root", "RequiredText", "Changed"),
            ]));
    }

    [Fact]
    public async Task ModelRenameChangesDescriptorAndOwnedFilenameAtomically()
    {
        using var fixture = await ProjectFixture.CreateAsync();
        var oldSource = await File.ReadAllBytesAsync(
            Path.Combine(fixture.Root, "Demo.meta.cs"));

        await using (var workspace = await CSharpWorkspace.OpenAsync(fixture.Root))
        {
            await workspace.ExecuteAsync([
                new Operation.RenameModel("Demo", "Renamed"),
            ]);
        }

        Assert.False(File.Exists(Path.Combine(fixture.Root, "Demo.meta.cs")));
        Assert.True(File.Exists(Path.Combine(fixture.Root, "Renamed.meta.cs")));
        Assert.Equal(
            ["Renamed.meta.cs"],
            WorkspaceMetaFile.Read(fixture.Root).Sources);
        Assert.NotEqual(
            oldSource,
            await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "Renamed.meta.cs")));
    }

    [Fact]
    public async Task OpenDuringPublicationGetsAnExplicitWorkspaceLockConflict()
    {
        await PublicationTestGate.WaitAsync();
        using var fixture = await ProjectFixture.CreateAsync();
        await using var workspace = await CSharpWorkspace.OpenAsync(fixture.Root);
        var published = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CSharpWorkspacePublicationTestHooks.Checkpoint = (path, checkpoint) =>
        {
            if (path == fixture.Root &&
                checkpoint == CSharpWorkspacePublicationCheckpoint.AfterNewStatePublished)
            {
                published.TrySetResult(true);
                release.Task.GetAwaiter().GetResult();
            }
        };

        try
        {
            var publication = workspace.ExecuteAsync([
                new Operation.RenameModel("Demo", "Renamed"),
            ]).AsTask();
            await published.Task;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await CSharpWorkspace.OpenAsync(fixture.Root));

            Assert.Contains("locked", exception.Message, StringComparison.OrdinalIgnoreCase);
            release.SetResult(true);
            await publication;
        }
        finally
        {
            release.TrySetResult(true);
            CSharpWorkspacePublicationTestHooks.Checkpoint = null;
            PublicationTestGate.Release();
        }
    }

    [Fact]
    public async Task PublicationFailureRollsBackWhileTheWorkspaceRemainsProtected()
    {
        await PublicationTestGate.WaitAsync();
        using var fixture = await ProjectFixture.CreateAsync();
        var oldDescriptor = await File.ReadAllBytesAsync(
            Path.Combine(fixture.Root, WorkspaceMetaFile.FileName));
        var oldSource = await File.ReadAllBytesAsync(
            Path.Combine(fixture.Root, "Demo.meta.cs"));
        var oldUnowned = fixture.ReadUnownedFiles();
        var rollbackWasProtected = false;
        CSharpWorkspacePublicationTestHooks.Checkpoint = (path, checkpoint) =>
        {
            if (path != fixture.Root)
            {
                return;
            }

            if (checkpoint == CSharpWorkspacePublicationCheckpoint.AfterNewStatePublished)
            {
                throw new InvalidOperationException("injected publication failure");
            }

            if (checkpoint == CSharpWorkspacePublicationCheckpoint.BeforeRollback)
            {
                try
                {
                    CSharpWorkspace.OpenAsync(fixture.Root).GetAwaiter().GetResult();
                }
                catch (InvalidOperationException exception) when (
                    exception.Message.Contains("locked", StringComparison.OrdinalIgnoreCase))
                {
                    rollbackWasProtected = true;
                }
            }
        };

        try
        {
            await using var workspace = await CSharpWorkspace.OpenAsync(fixture.Root);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await workspace.ExecuteAsync([
                    new Operation.RenameModel("Demo", "Renamed"),
                ]));

            Assert.True(rollbackWasProtected);
            Assert.Equal(oldDescriptor, await File.ReadAllBytesAsync(
                Path.Combine(fixture.Root, WorkspaceMetaFile.FileName)));
            Assert.Equal(oldSource, await File.ReadAllBytesAsync(
                Path.Combine(fixture.Root, "Demo.meta.cs")));
            Assert.Equal(oldUnowned, fixture.ReadUnownedFiles());
            Assert.False(File.Exists(Path.Combine(fixture.Root, "Renamed.meta.cs")));
        }
        finally
        {
            CSharpWorkspacePublicationTestHooks.Checkpoint = null;
            PublicationTestGate.Release();
        }
    }

    [Fact]
    public async Task RestorationFailurePreservesBackupAndReportsRecoveryPath()
    {
        await PublicationTestGate.WaitAsync();
        using var fixture = await ProjectFixture.CreateAsync();
        var oldDescriptor = await File.ReadAllBytesAsync(
            Path.Combine(fixture.Root, WorkspaceMetaFile.FileName));
        var oldSource = await File.ReadAllBytesAsync(
            Path.Combine(fixture.Root, "Demo.meta.cs"));
        var oldUnowned = fixture.ReadUnownedFiles();
        CSharpWorkspacePublicationTestHooks.Checkpoint = (path, checkpoint) =>
        {
            if (path == fixture.Root &&
                checkpoint == CSharpWorkspacePublicationCheckpoint.AfterNewStatePublished)
            {
                throw new InvalidOperationException("injected publication failure");
            }

            if (path == fixture.Root &&
                checkpoint == CSharpWorkspacePublicationCheckpoint.BeforeRestore)
            {
                throw new IOException("injected restoration failure");
            }
        };

        string? recoveryPath = null;
        try
        {
            await using var workspace = await CSharpWorkspace.OpenAsync(fixture.Root);
            var exception = await Assert.ThrowsAsync<WorkspacePublicationException>(async () =>
                await workspace.ExecuteAsync([
                    new Operation.RenameModel("Demo", "Renamed"),
                ]));

            recoveryPath = exception.RecoveryPath;
            Assert.True(Directory.Exists(recoveryPath));
            Assert.Same(exception.PublicationFailure, exception.InnerException!.InnerException);
            Assert.Equal(oldDescriptor, await File.ReadAllBytesAsync(
                Path.Combine(recoveryPath, WorkspaceMetaFile.FileName)));
            Assert.Equal(oldSource, await File.ReadAllBytesAsync(
                Path.Combine(recoveryPath, "Demo.meta.cs")));
            Assert.Equal(oldUnowned, fixture.ReadUnownedFiles());
        }
        finally
        {
            CSharpWorkspacePublicationTestHooks.Checkpoint = null;
            if (recoveryPath != null && Directory.Exists(recoveryPath))
            {
                Directory.Delete(recoveryPath, recursive: true);
            }

            PublicationTestGate.Release();
        }
    }

    [Fact]
    public async Task CreationFailureAfterSourceMoveCleansUpWhileTheWorkspaceRemainsProtected()
    {
        await PublicationTestGate.WaitAsync();
        using var fixture = ProjectFixture.CreateEmpty();
        fixture.WriteUnownedProjectFiles();
        var before = fixture.ReadUnownedFiles();
        var lockObserved = false;
        CSharpWorkspacePublicationTestHooks.Checkpoint = (path, checkpoint) =>
        {
            if (path == fixture.Root &&
                checkpoint == CSharpWorkspacePublicationCheckpoint.AfterCreationSourcesMoved)
            {
                lockObserved = WorkspaceWriteLock.IsActive(path);
                throw new InvalidOperationException("injected creation failure");
            }
        };

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await CSharpWorkspace.CreateAsync(DemoState(), fixture.Root));

            Assert.True(lockObserved);
            Assert.False(File.Exists(Path.Combine(fixture.Root, WorkspaceMetaFile.FileName)));
            Assert.False(File.Exists(Path.Combine(fixture.Root, "Demo.meta.cs")));
            Assert.False(File.Exists(Path.Combine(fixture.Root, ".meta.lock")));
            Assert.Equal(before, fixture.ReadUnownedFiles());
        }
        finally
        {
            CSharpWorkspacePublicationTestHooks.Checkpoint = null;
            PublicationTestGate.Release();
        }
    }

    [Fact]
    public async Task CreationCleanupNeverDeletesAnExternallyChangedSource()
    {
        await PublicationTestGate.WaitAsync();
        using var fixture = ProjectFixture.CreateEmpty();
        fixture.WriteUnownedProjectFiles();
        var before = fixture.ReadUnownedFiles();
        string? stagingPath = null;
        CSharpWorkspacePublicationTestHooks.Checkpoint = (path, checkpoint) =>
        {
            if (path == fixture.Root &&
                checkpoint == CSharpWorkspacePublicationCheckpoint.AfterCreationSourcesMoved)
            {
                Assert.True(WorkspaceWriteLock.IsActive(path));
                File.WriteAllText(
                    Path.Combine(path, "Demo.meta.cs"),
                    "external replacement");
                throw new InvalidOperationException("injected creation failure");
            }
        };

        try
        {
            var exception = await Assert.ThrowsAsync<WorkspaceCreationException>(async () =>
                await CSharpWorkspace.CreateAsync(DemoState(), fixture.Root));

            stagingPath = exception.StagingPath;
            Assert.True(Directory.Exists(stagingPath));
            Assert.Equal(
                "external replacement",
                await File.ReadAllTextAsync(Path.Combine(fixture.Root, "Demo.meta.cs")));
            Assert.False(File.Exists(Path.Combine(fixture.Root, WorkspaceMetaFile.FileName)));
            Assert.False(File.Exists(Path.Combine(fixture.Root, ".meta.lock")));
            Assert.Equal(before, fixture.ReadUnownedFiles());
        }
        finally
        {
            CSharpWorkspacePublicationTestHooks.Checkpoint = null;
            if (stagingPath != null && Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, recursive: true);
            }

            PublicationTestGate.Release();
        }
    }

    [Fact]
    public async Task UnownedTargetCollisionFailsWithoutChangingWorkspace()
    {
        using var fixture = await ProjectFixture.CreateAsync();
        var oldDescriptor = await File.ReadAllBytesAsync(
            Path.Combine(fixture.Root, WorkspaceMetaFile.FileName));
        var oldSource = await File.ReadAllBytesAsync(
            Path.Combine(fixture.Root, "Demo.meta.cs"));
        var collisionPath = Path.Combine(fixture.Root, "Renamed.meta.cs");
        await File.WriteAllTextAsync(collisionPath, "user-owned target");

        await using var workspace = await CSharpWorkspace.OpenAsync(fixture.Root);
        await Assert.ThrowsAsync<IOException>(async () =>
            await workspace.ExecuteAsync([
                new Operation.RenameModel("Demo", "Renamed"),
            ]));

        Assert.Equal(oldDescriptor, await File.ReadAllBytesAsync(
            Path.Combine(fixture.Root, WorkspaceMetaFile.FileName)));
        Assert.Equal(oldSource, await File.ReadAllBytesAsync(
            Path.Combine(fixture.Root, "Demo.meta.cs")));
        Assert.Equal("user-owned target", await File.ReadAllTextAsync(collisionPath));
    }

    [Fact]
    public async Task LegacySingleSourceMigratesOnFirstMutation()
    {
        using var fixture = await ProjectFixture.CreateLegacyAsync();
        await using (var workspace = await CSharpWorkspace.OpenAsync(fixture.Root))
        {
            await workspace.ExecuteAsync([
                new Operation.SetProperty("Node", "Root", "RequiredText", "Migrated"),
            ]);
        }

        Assert.False(File.Exists(Path.Combine(fixture.Root, "Legacy.cs")));
        Assert.True(File.Exists(Path.Combine(fixture.Root, "Demo.meta.cs")));
        Assert.Equal(
            ["Demo.meta.cs"],
            WorkspaceMetaFile.Read(fixture.Root).Sources);
    }

    [Fact]
    public async Task AmbiguousLegacySourcesAreRejected()
    {
        using var fixture = await ProjectFixture.CreateAmbiguousLegacyAsync();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await CSharpWorkspace.OpenAsync(fixture.Root));

        Assert.Contains("exactly one unambiguous source file", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/absolute.cs")]
    [InlineData("../escape.cs")]
    [InlineData("folder/../escape.cs")]
    [InlineData("not-csharp.txt")]
    public void UnsafeSourceDeclarationsAreRejected(string source)
    {
        using var fixture = ProjectFixture.CreateEmpty();
        File.WriteAllText(
            Path.Combine(fixture.Root, WorkspaceMetaFile.FileName),
            $"representation csharp\nsource {source}\n");

        var exception = Assert.Throws<InvalidDataException>(() =>
            WorkspaceMetaFile.Read(fixture.Root));

        Assert.Contains("source", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CaseEquivalentSourceDeclarationsAreRejected()
    {
        using var fixture = ProjectFixture.CreateEmpty();
        File.WriteAllText(
            Path.Combine(fixture.Root, WorkspaceMetaFile.FileName),
            "representation csharp\nsource Demo.meta.cs\nsource demo.META.CS\n");

        var exception = Assert.Throws<InvalidDataException>(() =>
            WorkspaceMetaFile.Read(fixture.Root));

        Assert.Contains("case-equivalent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void XmlAndSqlDescriptorsRejectSourceDeclarations()
    {
        using var fixture = ProjectFixture.CreateEmpty();
        var descriptorPath = Path.Combine(fixture.Root, WorkspaceMetaFile.FileName);
        File.WriteAllText(descriptorPath, "representation xml\nsource Demo.meta.cs\n");

        Assert.Throws<InvalidDataException>(() => WorkspaceMetaFile.Read(fixture.Root));

        File.WriteAllText(
            descriptorPath,
            "representation sql\nlocation SQL_ENV\nsource Demo.meta.cs\n");
        Assert.Throws<InvalidDataException>(() => WorkspaceMetaFile.Read(fixture.Root));
    }

    [Fact]
    public void ReparsePointSourcePathsAreRejected()
    {
        using var fixture = ProjectFixture.CreateEmpty();
        var target = Path.Combine(fixture.Root, "outside");
        Directory.CreateDirectory(target);
        var link = Path.Combine(fixture.Root, "linked");
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (IOException)
        {
            return;
        }

        File.WriteAllText(
            Path.Combine(fixture.Root, WorkspaceMetaFile.FileName),
            "representation csharp\nsource linked/Demo.meta.cs\n");

        var exception = Assert.Throws<InvalidDataException>(() =>
            WorkspaceMetaFile.Read(fixture.Root));

        Assert.Contains("symbolic link or reparse point", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedPartialTypesCompileWithUnownedExtension()
    {
        var state = DemoState();
        var output = MetaCSharpWriter.Write(state);
        var source = Assert.Single(output.Sources.Values);
        var extension = "namespace Demo; public sealed partial class DemoModel { public string Extension => \"ok\"; }";
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "DemoConsumer",
            [
                CSharpSyntaxTree.ParseText(source),
                CSharpSyntaxTree.ParseText(extension),
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Empty(errors);
    }

    private static InMemoryWorkspace DemoState()
    {
        var state = WorkspaceTestData.BuildState();
        state.Model.Name = "Demo";
        state.Instance.ModelName = "Demo";
        return state;
    }

    private sealed class ProjectFixture : IDisposable
    {
        private ProjectFixture(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static ProjectFixture CreateEmpty()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "meta-csharp-ownership-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new ProjectFixture(root);
        }

        public static async Task<ProjectFixture> CreateAsync()
        {
            var fixture = CreateEmpty();
            fixture.WriteUnownedProjectFiles();
            await CSharpWorkspace.CreateAsync(DemoState(), fixture.Root);
            return fixture;
        }

        public static async Task<ProjectFixture> CreateLegacyAsync()
        {
            var fixture = CreateEmpty();
            var output = MetaCSharpWriter.Write(DemoState());
            await File.WriteAllTextAsync(
                Path.Combine(fixture.Root, "Legacy.cs"),
                Assert.Single(output.Sources.Values));
            await File.WriteAllTextAsync(
                Path.Combine(fixture.Root, WorkspaceMetaFile.FileName),
                "representation csharp\n");
            return fixture;
        }

        public static async Task<ProjectFixture> CreateAmbiguousLegacyAsync()
        {
            var fixture = CreateEmpty();
            var source = Assert.Single(MetaCSharpWriter.Write(DemoState()).Sources.Values);
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "One.cs"), source);
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "Two.cs"), source);
            await File.WriteAllTextAsync(
                Path.Combine(fixture.Root, WorkspaceMetaFile.FileName),
                "representation csharp\n");
            return fixture;
        }

        public Dictionary<string, byte[]> ReadUnownedFiles()
        {
            return new[]
                {
                    "Demo.csproj",
                    "Program.cs",
                    "Customer.Extensions.cs",
                    "bin/Debug/generated.cs",
                    "obj/Debug/generated.cs",
                }
                .ToDictionary(
                    path => path,
                    path => File.ReadAllBytes(Path.Combine(Root, path)),
                    StringComparer.Ordinal);
        }

        public void WriteUnownedProjectFiles()
        {
            Write("Demo.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            Write("Program.cs", "not metadata state");
            Write("Customer.Extensions.cs", "not metadata state");
            Write("bin/Debug/generated.cs", "conflicting generated source");
            Write("obj/Debug/generated.cs", "conflicting generated source");
        }

        private void Write(string relativePath, string contents)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
