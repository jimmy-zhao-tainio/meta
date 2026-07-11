using System.Xml.Serialization;
using Meta.Core.Serialization;

namespace Meta.Core.Tests;

public sealed class TypedWorkspaceXmlSerializerTests
{
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

            TypedWorkspaceXmlSerializer.Save(model, workspacePath);

            var modelXmlPath = Path.Combine(workspacePath, "model.xml");
            var alphaShardPath = Path.Combine(workspacePath, "instances", "Alpha.xml");
            var betaShardPath = Path.Combine(workspacePath, "instances", "Beta.xml");
            var oldTimestamp = new DateTime(2001, 02, 03, 04, 05, 06, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(modelXmlPath, oldTimestamp);
            File.SetLastWriteTimeUtc(alphaShardPath, oldTimestamp);
            File.SetLastWriteTimeUtc(betaShardPath, oldTimestamp);

            TypedWorkspaceXmlSerializer.Save(model, workspacePath);

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

            TypedWorkspaceXmlSerializer.Save(model, workspacePath);
            var instancePath = Path.Combine(workspacePath, "instances");
            File.WriteAllText(Path.Combine(instancePath, "Stale.xml"), "<TestTypedModel />");
            model.BetaList.Clear();

            TypedWorkspaceXmlSerializer.Save(model, workspacePath);

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

            TypedWorkspaceXmlSerializer.Save(model, workspacePath);
            var instancePath = Path.Combine(workspacePath, "instances");
            var canonicalShardPath = Path.Combine(instancePath, "Alpha.xml");
            var splitShardPath = Path.Combine(instancePath, "Alpha.part-a.xml");
            File.Move(canonicalShardPath, splitShardPath);

            var loaded = TypedWorkspaceXmlSerializer.Load<TestTypedModel>(workspacePath);

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
                () => TypedWorkspaceXmlSerializer.Save(model, workspacePath));

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
            TypedWorkspaceXmlSerializer.Save(new TestTypedModel(), workspacePath);
            var childPath = Path.Combine(workspacePath, "child");
            Directory.CreateDirectory(childPath);

            var exception = Assert.Throws<NotSupportedException>(
                () => TypedWorkspaceXmlSerializer.Load<TestTypedModel>(childPath, searchUpward: true));

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
                () => TypedWorkspaceXmlSerializer.Load<TestTypedModel>(workspacePath));

            Assert.Contains("workspace.xml", exception.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(workspacePath));
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
            TypedWorkspaceXmlSerializer.Save(new TestTypedModel(), workspacePath);
            Assert.True(TypedWorkspaceXmlSerializer.IsWorkspace<TestTypedModel>(workspacePath));

            var emptyPath = Path.Combine(tempRoot, "empty");
            Directory.CreateDirectory(emptyPath);
            Assert.False(TypedWorkspaceXmlSerializer.IsWorkspace<TestTypedModel>(emptyPath));

            File.WriteAllText(Path.Combine(workspacePath, "model.xml"), "<model name=\"OtherModel\" />");
            Assert.False(TypedWorkspaceXmlSerializer.IsWorkspace<TestTypedModel>(workspacePath));
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

            var createdPath = TypedWorkspaceXmlSerializer.CreateWorkspace<TestTypedModel>(workspacePath);

            Assert.Equal(Path.GetFullPath(workspacePath), createdPath);
            Assert.True(File.Exists(Path.Combine(workspacePath, "workspace.xml")));
            Assert.True(File.Exists(Path.Combine(workspacePath, "model.xml")));
            Assert.True(TypedWorkspaceXmlSerializer.IsWorkspace<TestTypedModel>(workspacePath));
            var loaded = TypedWorkspaceXmlSerializer.Load<TestTypedModel>(workspacePath);
            Assert.Empty(loaded.AlphaList);
            Assert.Empty(loaded.BetaList);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

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

    [XmlRoot("TestTypedModel")]
    public sealed class TestTypedModel
    {
        [XmlArray("AlphaList")]
        [XmlArrayItem("Alpha")]
        public List<Alpha> AlphaList { get; set; } = new();

        public bool ShouldSerializeAlphaList() => AlphaList.Count > 0;

        [XmlArray("BetaList")]
        [XmlArrayItem("Beta")]
        public List<Beta> BetaList { get; set; } = new();

        public bool ShouldSerializeBetaList() => BetaList.Count > 0;
    }

    public sealed class Alpha
    {
        [XmlAttribute]
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }

    public sealed class Beta
    {
        [XmlAttribute]
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }
}
