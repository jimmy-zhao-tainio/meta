using Meta.Adapters;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class CSharpMetaOperationSessionTests
{
    [Fact]
    public void ConformancePlans_CoverEveryConcreteMetaOperation()
    {
        var covered = MetaOperationInterpreterTests.BuildPlan().Operations
            .Concat(
                MetaOperationInterpreterTests.BuildSchemaRefactorPlan()
                    .Operations)
            .Select(operation => operation.GetType())
            .ToHashSet();
        var operationTypes = typeof(MetaOperation).Assembly.GetTypes()
            .Where(type =>
                !type.IsAbstract &&
                typeof(MetaOperation).IsAssignableFrom(type))
            .ToArray();

        Assert.Equal(
            operationTypes.OrderBy(type => type.FullName),
            covered.OrderBy(type => type.FullName));
    }

    [Fact]
    public void Reader_DecodesGeneratedModelAndInstances()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = MetaOperationInterpreterTests.BuildState();
            GenerationService.GenerateCSharp(
                new Workspace
                {
                    Model = source.Model,
                    Instance = source.Instance,
                },
                root);

            var actual = new CSharpMetaWorkspaceReader().Read(root);

            Assert.Equal(
                MetaOperationInterpreterTests.Canonicalize(source),
                MetaOperationInterpreterTests.Canonicalize(actual));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void WorkspaceGeneration_UsesDirectObjectReferences()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = MetaOperationInterpreterTests.BuildState();

            GenerationService.GenerateCSharpWorkspace(
                source.Model,
                source.Instance,
                root);

            var modelSource = File.ReadAllText(
                Path.Combine(root, "OperationProof.cs"));
            Assert.Contains(
                "personList[0].Team = teamList[0];",
                modelSource,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "RequireTarget",
                modelSource,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "ById",
                modelSource,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Session_ProducesReferenceInterpreterState(
        bool schemaRefactors)
    {
        var root = CreateTempDirectory();
        try
        {
            var source = MetaOperationInterpreterTests.BuildState();
            var plan = schemaRefactors
                ? MetaOperationInterpreterTests.BuildSchemaRefactorPlan()
                : MetaOperationInterpreterTests.BuildPlan();
            var expected = new MetaOperationInterpreter()
                .Apply(source, plan)
                .State;
            var session = CSharpMetaOperationSession.Create(
                root,
                source);

            session.Apply(plan);
            session.Commit();

            var actual = new CSharpMetaWorkspaceReader().Read(root);
            Assert.Equal(
                MetaOperationInterpreterTests.Canonicalize(expected),
                MetaOperationInterpreterTests.Canonicalize(actual));
            Assert.All(
                Directory.GetFiles(root, "*.cs"),
                path => Assert.StartsWith(
                    "// <meta-workspace>",
                    File.ReadAllText(path),
                    StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Session_RejectedPlanLeavesSessionAndFilesUnchanged()
    {
        var root = CreateTempDirectory();
        try
        {
            var session = CSharpMetaOperationSession.Create(
                root,
                MetaOperationInterpreterTests.BuildState());
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

            Assert.Throws<MetaOperationException>(
                () => session.Apply(rejected));

            Assert.Equal(
                stateBefore,
                MetaOperationInterpreterTests.Canonicalize(
                    session.Snapshot()));
            AssertFilesEqual(filesBefore, ReadFiles(root));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Session_RejectsStaleCommitBeforeReplacingSource()
    {
        var root = CreateTempDirectory();
        try
        {
            CSharpMetaOperationSession.Create(
                root,
                MetaOperationInterpreterTests.BuildState());
            var first = CSharpMetaOperationSession.OpenExisting(root);
            var stale = CSharpMetaOperationSession.OpenExisting(root);

            first.Apply(MetaOperationPlan.Create(
                new SetPropertyOperation(
                    "Person",
                    "person-a",
                    "LegacyName",
                    "First writer")));
            first.Commit();

            stale.Apply(MetaOperationPlan.Create(
                new SetPropertyOperation(
                    "Person",
                    "person-a",
                    "LegacyName",
                    "Stale writer")));
            Assert.Throws<WorkspaceConflictException>(
                () => stale.Commit());

            var actual = new CSharpMetaWorkspaceReader().Read(root);
            var person = Assert.Single(
                actual.Instance.RecordsByEntity["Person"]);
            Assert.Equal("First writer", person.Values["LegacyName"]);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Reader_PreservesMissingEmptyWhitespaceAndEscapedText()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = MetaOperationInterpreterTests.BuildState();
            var people = source.Instance.RecordsByEntity["Person"];
            people[0].Values.Remove("Note");
            people[0].Values["LegacyName"] = string.Empty;
            people.AddRange(
            [
                new GenericRecord
                {
                    Id = "person-b",
                    Values =
                    {
                        ["LegacyName"] = "Second",
                        ["Note"] = string.Empty,
                    },
                },
                new GenericRecord
                {
                    Id = "person-c",
                    Values =
                    {
                        ["LegacyName"] = "Third",
                        ["Note"] = "  slash\\quote\"line\nnext\t\u0001",
                    },
                },
            ]);
            GenerationService.GenerateCSharpWorkspace(
                source.Model,
                source.Instance,
                root);

            var actual = new CSharpMetaWorkspaceReader().Read(root);

            Assert.Equal(
                MetaOperationInterpreterTests.Canonicalize(source),
                MetaOperationInterpreterTests.Canonicalize(actual));
            var actualPeople = actual.Instance.RecordsByEntity["Person"];
            Assert.False(actualPeople[0].Values.ContainsKey("Note"));
            Assert.Equal(string.Empty, actualPeople[1].Values["Note"]);
            Assert.Equal(
                "  slash\\quote\"line\nnext\t\u0001",
                actualPeople[2].Values["Note"]);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Reader_DecodesGeneratedRequiredObjectReference()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = MetaOperationInterpreterTests.BuildState();
            source.Model.FindEntity("Person")!
                .Relationships.Single()
                .IsNullable = false;
            GenerationService.GenerateCSharpWorkspace(
                source.Model,
                source.Instance,
                root);

            var actual = new CSharpMetaWorkspaceReader().Read(root);

            Assert.Equal(
                MetaOperationInterpreterTests.Canonicalize(source),
                MetaOperationInterpreterTests.Canonicalize(actual));
            Assert.Contains(
                "= null!;",
                File.ReadAllText(Path.Combine(root, "Person.cs")),
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Reader_UsesRoslynConstantSemantics()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = MetaOperationInterpreterTests.BuildState();
            GenerationService.GenerateCSharpWorkspace(
                source.Model,
                source.Instance,
                root);
            var modelPath = Path.Combine(root, "OperationProof.cs");
            var text = File.ReadAllText(modelPath)
                .Replace(
                    "LegacyName = \"Original\",",
                    "LegacyName = \"Orig\" + \"inal\",",
                    StringComparison.Ordinal);
            File.WriteAllText(modelPath, text);

            var actual = new CSharpMetaWorkspaceReader().Read(root);

            Assert.Equal(
                MetaOperationInterpreterTests.Canonicalize(source),
                MetaOperationInterpreterTests.Canonicalize(actual));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Reader_UsesReferencedObjectsCanonicalIdentity()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = MetaOperationInterpreterTests.BuildState();
            GenerationService.GenerateCSharp(
                new Workspace
                {
                    Model = source.Model,
                    Instance = source.Instance,
                },
                root);
            var modelPath = Path.Combine(root, "OperationProof.cs");
            var original = File.ReadAllText(modelPath);
            var edited = original.Replace(
                    """
                                personList[0].Team = RequireTarget(
                                    teamListById,
                                    "team-a",
                    """,
                    """
                                personList[0].Team = RequireTarget(
                                    teamListById,
                                    "TEAM-A",
                    """,
                    StringComparison.Ordinal);
            Assert.NotEqual(original, edited);
            File.WriteAllText(modelPath, edited);

            var actual = new CSharpMetaWorkspaceReader().Read(root);

            var person = Assert.Single(
                actual.Instance.RecordsByEntity["Person"]);
            Assert.Equal("team-a", person.RelationshipIds["TeamId"]);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Reader_DecodesAuthoredPropertyAndInstanceValue()
    {
        var root = CreateTempDirectory();
        try
        {
            var expected = MetaOperationInterpreterTests.BuildState();
            expected.Model.FindEntity("Person")!.Properties.Add(
                new GenericProperty
                {
                    Name = "Alias",
                    IsNullable = true,
                });
            expected.Instance.RecordsByEntity["Person"][0]
                .Values["Alias"] = "Primary";
            GenerationService.GenerateCSharpWorkspace(
                MetaOperationInterpreterTests.BuildState().Model,
                MetaOperationInterpreterTests.BuildState().Instance,
                root);

            var entityPath = Path.Combine(root, "Person.cs");
            File.WriteAllText(
                entityPath,
                File.ReadAllText(entityPath).Replace(
                    "        public string LegacyName { get; set; } = string.Empty;",
                    """
                            public string LegacyName { get; set; } = string.Empty;

                            public string? Alias { get; set; }
                    """,
                    StringComparison.Ordinal));
            var modelPath = Path.Combine(root, "OperationProof.cs");
            File.WriteAllText(
                modelPath,
                File.ReadAllText(modelPath).Replace(
                    "                    LegacyName = \"Original\",",
                    """
                                        LegacyName = "Original",
                                        Alias = "Primary",
                    """,
                    StringComparison.Ordinal));

            var actual = new CSharpMetaWorkspaceReader().Read(root);

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
    public void Reader_UsesAutomaticPropertyDefaults()
    {
        var root = CreateTempDirectory();
        try
        {
            var expected = MetaOperationInterpreterTests.BuildState();
            var person = expected.Instance
                .RecordsByEntity["Person"][0];
            person.Values["LegacyName"] = string.Empty;
            person.Values["Note"] = "Default note";
            GenerationService.GenerateCSharpWorkspace(
                MetaOperationInterpreterTests.BuildState().Model,
                MetaOperationInterpreterTests.BuildState().Instance,
                root);

            var entityPath = Path.Combine(root, "Person.cs");
            File.WriteAllText(
                entityPath,
                File.ReadAllText(entityPath).Replace(
                    "public string? Note { get; set; }",
                    "public string? Note { get; set; } = \"Default note\";",
                    StringComparison.Ordinal));
            var modelPath = Path.Combine(root, "OperationProof.cs");
            File.WriteAllText(
                modelPath,
                File.ReadAllText(modelPath)
                    .Replace(
                        "                    LegacyName = \"Original\",\n",
                        string.Empty,
                        StringComparison.Ordinal)
                    .Replace(
                        "                    Note = \"Remove me\",\n",
                        string.Empty,
                        StringComparison.Ordinal));

            var actual = new CSharpMetaWorkspaceReader().Read(root);

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
    public void Reader_RejectsBuiltInPropertyDisconnectedFromModeledFactory()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = MetaOperationInterpreterTests.BuildState();
            GenerationService.GenerateCSharpWorkspace(
                source.Model,
                source.Instance,
                root);
            var modelPath = Path.Combine(root, "OperationProof.cs");
            var text = File.ReadAllText(modelPath)
                .Replace(
                    "OperationProofInstanceFactory.CreateBuiltIn();",
                    "OperationProofInstanceFactory.CreateOther();",
                    StringComparison.Ordinal)
                .Replace(
                    "        internal static OperationProofInstance CreateBuiltIn()",
                    """
                            internal static OperationProofInstance CreateOther() =>
                                CreateBuiltIn();

                            internal static OperationProofInstance CreateBuiltIn()
                    """,
                    StringComparison.Ordinal);
            File.WriteAllText(modelPath, text);

            Assert.Throws<InvalidDataException>(
                () => new CSharpMetaWorkspaceReader().Read(root));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Reader_RejectsReturnedCollectionDisconnectedFromEntityList()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = MetaOperationInterpreterTests.BuildState();
            GenerationService.GenerateCSharpWorkspace(
                source.Model,
                source.Instance,
                root);
            var modelPath = Path.Combine(root, "OperationProof.cs");
            File.WriteAllText(
                modelPath,
                File.ReadAllText(modelPath).Replace(
                    "new ReadOnlyCollection<Person>(personList)",
                    "new ReadOnlyCollection<Person>(new List<Person>())",
                    StringComparison.Ordinal));

            Assert.Throws<InvalidDataException>(
                () => new CSharpMetaWorkspaceReader().Read(root));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Reader_RejectsUnmodeledFactoryMutation()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = MetaOperationInterpreterTests.BuildState();
            GenerationService.GenerateCSharpWorkspace(
                source.Model,
                source.Instance,
                root);
            var modelPath = Path.Combine(root, "OperationProof.cs");
            File.WriteAllText(
                modelPath,
                File.ReadAllText(modelPath).Replace(
                    "            return new OperationProofInstance(",
                    """
                                personList.Clear();

                                return new OperationProofInstance(
                    """,
                    StringComparison.Ordinal));

            Assert.Throws<InvalidDataException>(
                () => new CSharpMetaWorkspaceReader().Read(root));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Session_RejectsCSharpMemberCollisionBeforeChangingPendingState()
    {
        var root = CreateTempDirectory();
        try
        {
            var session = CSharpMetaOperationSession.Create(
                root,
                MetaOperationInterpreterTests.BuildState());
            var stateBefore = MetaOperationInterpreterTests.Canonicalize(
                session.Snapshot());
            var filesBefore = ReadFiles(root);

            var exception = Assert.Throws<MetaOperationException>(
                () => session.Apply(MetaOperationPlan.Create(
                    new AddPropertyOperation(
                        "Person",
                        "Team",
                        isRequired: false))));

            Assert.Contains(
                "conflicts",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                stateBefore,
                MetaOperationInterpreterTests.Canonicalize(
                    session.Snapshot()));
            AssertFilesEqual(filesBefore, ReadFiles(root));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Create_RejectedCSharpShapeLeavesEmptyTargetUnchanged()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = MetaOperationInterpreterTests.BuildState();
            source.Model.FindEntity("Person")!.Properties.Add(
                new GenericProperty
                {
                    Name = "Team",
                    IsNullable = true,
                });

            Assert.Throws<NotSupportedException>(
                () => CSharpMetaOperationSession.Create(
                    root,
                    source));

            Assert.Empty(
                Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Session_RejectsUnrepresentableCSharpNameBeforeChangingPendingState()
    {
        var root = CreateTempDirectory();
        try
        {
            var session = CSharpMetaOperationSession.Create(
                root,
                MetaOperationInterpreterTests.BuildState());
            var stateBefore = MetaOperationInterpreterTests.Canonicalize(
                session.Snapshot());
            var filesBefore = ReadFiles(root);

            var exception = Assert.Throws<MetaOperationException>(
                () => session.Apply(MetaOperationPlan.Create(
                    new AddEntityOperation("class"))));

            Assert.Contains(
                "cannot represent",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                stateBefore,
                MetaOperationInterpreterTests.Canonicalize(
                    session.Snapshot()));
            AssertFilesEqual(filesBefore, ReadFiles(root));
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
            "meta-operation-csharp",
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
}
