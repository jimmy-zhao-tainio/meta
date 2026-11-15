using Meta.Operations.Domain;
using Meta.Integration;
using Meta.Surfaces.Xml;
using TestTyped;

namespace Meta.Core.Tests;

public sealed class TypedWorkspaceXmlSerializerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData(" \t\r\n ")]
    [InlineData(" leading and trailing ")]
    [InlineData("first\r\nsecond")]
    [InlineData("\u00c5sa \u00e4ter r\u00e4ksm\u00f6rg\u00e5s.")]
    public void XmlWorkspace_RoundTripPreservesExactTextValues(string? value)
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workspacePath = Path.Combine(tempRoot, "workspace");
            var model = new TestTypedModel
            {
                AlphaList = { new Alpha { Id = "one", Name = "One", Note = value } },
            };
            TypedWorkspaceModelMapper.Create(model, FixtureContract(), Binding, workspacePath, "xml");

            var restored = TypedWorkspaceModelMapper.Load<TestTypedModel>(workspacePath, Binding);
            Assert.Equal(value, Assert.Single(restored.AlphaList).Note);

            var shardPath = Path.Combine(workspacePath, "instances", "Alpha.xml");
            var semanticModel = Binding.Write(model, FixtureContract()).Model;
            foreach (var instance in new[]
                     {
                         InstanceXmlCodec.LoadFromPath(shardPath, semanticModel),
                         InstanceXmlCodec.LoadFromPaths(new[] { shardPath }, semanticModel),
                     })
            {
                var row = Assert.Single(instance.RecordsByEntity["Alpha"]);
                if (value is null)
                {
                    Assert.False(row.Values.ContainsKey("Note"));
                }
                else
                {
                    Assert.Equal(value, row.Values["Note"]);
                }
            }

            var originalBytes = File.ReadAllBytes(shardPath);
            TypedWorkspaceXmlSerializer.Save(restored, workspacePath, FixtureContract(), Binding);
            Assert.Equal(originalBytes, File.ReadAllBytes(shardPath));
            Assert.Equal(value, Assert.Single(
                TypedWorkspaceXmlSerializer.Load<TestTypedModel>(workspacePath, Binding).AlphaList).Note);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact]
    public void TypedWorkspaceModelMapper_RoundTripsScalarsAndReferences()
    {
        var target = new Beta { Id = "target", Name = "Target" };
        var typed = new TestTypedModel
        {
            AlphaList =
            {
                new Alpha
                {
                    Id = "source",
                    Name = "Source",
                    Note = string.Empty,
                    Parent = target,
                },
                new Alpha
                {
                    Id = "missing",
                    Name = "Missing optional value",
                },
            },
            BetaList = { target },
        };

        var semantic = Binding.Write(typed, FixtureContract());
        var source = Assert.Single(
            semantic.Instance.RecordsByEntity["Alpha"],
            row => row.Id == "source");
        Assert.Equal(string.Empty, source.Values["Note"]);
        Assert.Equal("target", source.RelationshipIds["ParentId"]);
        var missing = Assert.Single(
            semantic.Instance.RecordsByEntity["Alpha"],
            row => row.Id == "missing");
        Assert.False(missing.Values.ContainsKey("Note"));

        var restored = Binding.Read(semantic);
        var restoredSource = Assert.Single(restored.AlphaList, row => row.Id == "source");
        var restoredMissing = Assert.Single(restored.AlphaList, row => row.Id == "missing");
        Assert.Same(Assert.Single(restored.BetaList), restoredSource.Parent);
        Assert.Equal(string.Empty, restoredSource.Note);
        Assert.Null(restoredMissing.Note);

        var roundTripped = Binding.Write(restored, FixtureContract());
        Assert.Null(InMemoryWorkspaceComparer.FindDifference(
            semantic,
            roundTripped));
    }

    [Fact]
    public void TypedWorkspaceModelMapper_CSharpLifecycleUsesNeutralMapping()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workspacePath = Path.Combine(tempRoot, "typed-csharp");
            var model = new TestTypedModel
            {
                AlphaList = { new Alpha { Id = "one", Name = "One", Note = string.Empty } },
            };

            TypedWorkspaceModelMapper.Create(model, FixtureContract(), Binding, workspacePath, "csharp");
            var restored = TypedWorkspaceModelMapper.Load<TestTypedModel>(workspacePath, Binding);

            Assert.Null(InMemoryWorkspaceComparer.FindDifference(
                Binding.Write(model, FixtureContract()),
                Binding.Write(restored, FixtureContract())));
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact]
    public void Save_RejectsIdentityBeyondTheSharedBoundary()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var model = new TestTypedModel
            {
                AlphaList =
                {
                    new Alpha
                    {
                        Id = new string('x', Meta.Operations.Domain.MetaIdentity.MaximumLength + 1),
                        Name = "Too long",
                    },
                },
            };

            var exception = Assert.Throws<InvalidOperationException>(
                () => TypedWorkspaceXmlSerializer.Save(model, Path.Combine(tempRoot, "workspace"), FixtureContract(), Binding));

            Assert.Contains(
                Meta.Operations.Domain.MetaIdentity.MaximumLength.ToString(),
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact]
    public void Save_UnchangedWorkspace_DoesNotRewriteModelOrShards()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workspacePath = Path.Combine(tempRoot, "workspace");
            var model = new TestTypedModel
            {
                AlphaList =
                {
                    new Alpha { Id = "1", Name = "One" },
                },
                BetaList =
                {
                    new Beta { Id = "2", Name = "Two" },
                },
            };

            TypedWorkspaceXmlSerializer.Save(model, workspacePath, FixtureContract(), Binding);

            var modelXmlPath = Path.Combine(workspacePath, "model.xml");
            var alphaShardPath = Path.Combine(workspacePath, "instances", "Alpha.xml");
            var betaShardPath = Path.Combine(workspacePath, "instances", "Beta.xml");
            var oldTimestamp = new DateTime(2001, 02, 03, 04, 05, 06, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(modelXmlPath, oldTimestamp);
            File.SetLastWriteTimeUtc(alphaShardPath, oldTimestamp);
            File.SetLastWriteTimeUtc(betaShardPath, oldTimestamp);

            TypedWorkspaceXmlSerializer.Save(model, workspacePath, FixtureContract(), Binding);

            Assert.Equal(oldTimestamp, File.GetLastWriteTimeUtc(modelXmlPath));
            Assert.Equal(oldTimestamp, File.GetLastWriteTimeUtc(alphaShardPath));
            Assert.Equal(oldTimestamp, File.GetLastWriteTimeUtc(betaShardPath));
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact]
    public void Save_RemovesEmptyAndStaleShards()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workspacePath = Path.Combine(tempRoot, "workspace");
            var model = new TestTypedModel
            {
                AlphaList =
                {
                    new Alpha { Id = "1", Name = "One" },
                },
                BetaList =
                {
                    new Beta { Id = "2", Name = "Two" },
                },
            };

            TypedWorkspaceXmlSerializer.Save(model, workspacePath, FixtureContract(), Binding);
            var instancePath = Path.Combine(workspacePath, "instances");
            File.WriteAllText(Path.Combine(instancePath, "Stale.xml"), "<TestTypedModel />");
            model.BetaList.Clear();

            TypedWorkspaceXmlSerializer.Save(model, workspacePath, FixtureContract(), Binding);

            Assert.True(File.Exists(Path.Combine(instancePath, "Alpha.xml")));
            Assert.False(File.Exists(Path.Combine(instancePath, "Beta.xml")));
            Assert.False(File.Exists(Path.Combine(instancePath, "Stale.xml")));
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact]
    public void Load_FallsBackForNonCanonicalShardFileNames()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workspacePath = Path.Combine(tempRoot, "workspace");
            var model = new TestTypedModel
            {
                AlphaList =
                {
                    new Alpha { Id = "1", Name = "One" },
                },
            };

            TypedWorkspaceXmlSerializer.Save(model, workspacePath, FixtureContract(), Binding);
            var instancePath = Path.Combine(workspacePath, "instances");
            var canonicalShardPath = Path.Combine(instancePath, "Alpha.xml");
            var splitShardPath = Path.Combine(instancePath, "Alpha.part-a.xml");
            File.Move(canonicalShardPath, splitShardPath);

            var loaded = TypedWorkspaceXmlSerializer.Load<TestTypedModel>(workspacePath, Binding);

            var row = Assert.Single(loaded.AlphaList);
            Assert.Equal("1", row.Id);
            Assert.Equal("One", row.Name);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact]
    public void Save_WritesLfTerminatedXml()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workspacePath = Path.Combine(tempRoot, "workspace");
            TypedWorkspaceXmlSerializer.Save(new TestTypedModel
            {
                AlphaList =
                {
                    new Alpha { Id = "1", Name = "One" },
                },
            }, workspacePath, FixtureContract(), Binding);

            var xmlPaths = Directory.GetFiles(workspacePath, "*.xml", SearchOption.AllDirectories);
            Assert.NotEmpty(xmlPaths);
            Assert.All(xmlPaths, AssertLfTerminated);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact]
    public void Save_RejectsIdsThatDifferOnlyByCase()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workspacePath = Path.Combine(tempRoot, "workspace");
            var model = new TestTypedModel
            {
                AlphaList =
                {
                    new Alpha { Id = "DUP", Name = "One" },
                    new Alpha { Id = "dup", Name = "Two" },
                },
            };

            var exception = Assert.Throws<InvalidOperationException>(
                () => TypedWorkspaceXmlSerializer.Save(model, workspacePath, FixtureContract(), Binding));

            Assert.Contains("duplicate Id 'dup'", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact]
    public void Load_DoesNotSearchUpward()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workspacePath = Path.Combine(tempRoot, "workspace");
            TypedWorkspaceXmlSerializer.Save(new TestTypedModel(), workspacePath, FixtureContract(), Binding);
            var childPath = Path.Combine(workspacePath, "child");
            Directory.CreateDirectory(childPath);

            var exception = Assert.Throws<NotSupportedException>(
                () => TypedWorkspaceXmlSerializer.Load<TestTypedModel>(childPath, Binding, searchUpward: true));

            Assert.Contains("does not search parent directories", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact]
    public void Load_RequiresExistingWorkspace()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workspacePath = Path.Combine(tempRoot, "not-a-workspace");
            Directory.CreateDirectory(workspacePath);

            var exception = Assert.Throws<InvalidDataException>(
                () => TypedWorkspaceXmlSerializer.Load<TestTypedModel>(workspacePath, Binding));

            Assert.Contains("workspace.meta", exception.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(workspacePath));
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact]
    public void TypedWorkspaceModelMapper_SaveDoesNotInventXmlForNewWorkspace()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workspacePath = Path.Combine(tempRoot, "workspace");
            var model = new TestTypedModel();

            var exception = Assert.Throws<InvalidOperationException>(
                () => TypedWorkspaceModelMapper.Save(model, workspacePath, Binding));

            Assert.Contains("explicit representation", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(workspacePath));

            TypedWorkspaceModelMapper.Create(model, FixtureContract(), Binding, workspacePath, "xml");
            Assert.True(File.Exists(Path.Combine(workspacePath, "workspace.meta")));
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact]
    public void IsWorkspace_RequiresMatchingModel()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workspacePath = Path.Combine(tempRoot, "workspace");
            TypedWorkspaceXmlSerializer.Save(new TestTypedModel(), workspacePath, FixtureContract(), Binding);
            Assert.True(TypedWorkspaceXmlSerializer.IsWorkspace<TestTypedModel>(workspacePath, Binding));

            var emptyPath = Path.Combine(tempRoot, "empty");
            Directory.CreateDirectory(emptyPath);
            Assert.False(TypedWorkspaceXmlSerializer.IsWorkspace<TestTypedModel>(emptyPath, Binding));

            File.WriteAllText(Path.Combine(workspacePath, "model.xml"), "<model name=\"OtherModel\" />");
            Assert.False(TypedWorkspaceXmlSerializer.IsWorkspace<TestTypedModel>(workspacePath, Binding));
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Fact]
    public void CreateWorkspace_CreatesEmptyWorkspace()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workspacePath = Path.Combine(tempRoot, "workspace");

            var createdPath = TypedWorkspaceXmlSerializer.CreateWorkspace<TestTypedModel>(workspacePath, FixtureContract(), Binding);

            Assert.Equal(Path.GetFullPath(workspacePath), createdPath);
            Assert.True(File.Exists(Path.Combine(workspacePath, "workspace.meta")));
            Assert.True(File.Exists(Path.Combine(workspacePath, "model.xml")));
            Assert.True(TypedWorkspaceXmlSerializer.IsWorkspace<TestTypedModel>(workspacePath, Binding));
            var loaded = TypedWorkspaceXmlSerializer.Load<TestTypedModel>(workspacePath, Binding);
            Assert.Empty(loaded.AlphaList);
            Assert.Empty(loaded.BetaList);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private static readonly TestTypedWorkspaceBinding Binding = new();

    private static GenericModel FixtureContract() =>
        TypedWorkspaceModelMapper.LoadStateAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestTyped"))
            .GetAwaiter().GetResult().Model;

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "meta-typed-xml-tests", Guid.NewGuid().ToString("N"));
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

    private static void AssertLfTerminated(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.NotEmpty(bytes);
        Assert.False(HasUtf8Bom(bytes));
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal((byte)'\n', bytes[^1]);
    }

    private static bool HasUtf8Bom(IReadOnlyList<byte> bytes)
    {
        return bytes.Count >= 3 &&
               bytes[0] == 0xef &&
               bytes[1] == 0xbb &&
               bytes[2] == 0xbf;
    }

}
