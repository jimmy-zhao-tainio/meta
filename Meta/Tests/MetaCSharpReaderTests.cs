using Meta.Surfaces;
using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Surfaces.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Meta.Core.Tests;

public sealed class MetaCSharpReaderTests
{
    [Fact]
    public async Task CSharpWorkspace_ExecutesAndPersistsOperations()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meta-csharp-workspace-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            await WriteSourcesAsync(
                root,
                MetaCSharpWriter.Write(WorkspaceTestData.BuildState()));
            await using (var workspace = await CSharpWorkspace.OpenAsync(root))
            {
                await workspace.ExecuteAsync([
                    new Operation.SetProperty(
                        "Node",
                        "Root",
                        "RequiredText",
                        "Changed"),
                ]);
            }

            await using var reopened = await CSharpWorkspace.OpenAsync(root);
            var record = await reopened.ReadRecordAsync("Node", "Root");
            Assert.NotNull(record);
            Assert.Equal("Changed", record.Values["RequiredText"]);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CSharpWorkspace_RejectsAStaleWrite()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meta-csharp-workspace-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            await WriteSourcesAsync(
                root,
                MetaCSharpWriter.Write(WorkspaceTestData.BuildState()));
            await using var workspace = await CSharpWorkspace.OpenAsync(root);
            var sourcePath = Directory.GetFiles(root, "*.cs").First();
            await File.AppendAllTextAsync(sourcePath, "\n");

            await Assert.ThrowsAsync<Meta.Surfaces.WorkspaceConflictException>(
                async () => await workspace.ExecuteAsync([
                    new Operation.SetProperty(
                        "Node",
                        "Root",
                        "RequiredText",
                        "Changed"),
                ]));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ReadWrite_PreservesSemanticState()
    {
        var state = WorkspaceTestData.BuildState();

        var csharp = MetaCSharpWriter.Write(state);
        var roundTripped = MetaCSharpReader.Read(csharp);

        Assert.Null(InMemoryWorkspaceComparer.FindDifference(
            state,
            roundTripped));
    }

    [Fact]
    public void Write_PreservesMissingAndExplicitlyEmptyOptionalText()
    {
        var state = WorkspaceTestData.BuildState();

        var source = Assert.Single(
            MetaCSharpWriter.Write(state).Sources,
            item => item.Key == "RoundTrip.meta.cs").Value;

        Assert.Equal(1, Count(
            source,
            "OptionalText = \"\","));
    }

    [Fact]
    public void OperationLaw_HoldsForCSharp()
    {
        var source = WorkspaceTestData.BuildState();
        var operation = new Operation.RenameRecord(
            "Node",
            "Root",
            "ROOT");
        var expected = InMemoryOperations.Apply(
            source,
            operation);

        var decoded = MetaCSharpReader.Read(
            MetaCSharpWriter.Write(source));
        var applied = InMemoryOperations.Apply(
            decoded,
            operation);
        var reloaded = MetaCSharpReader.Read(
            MetaCSharpWriter.Write(applied));

        Assert.Null(InMemoryWorkspaceComparer.FindDifference(
            expected,
            reloaded));
    }

    [Fact]
    public void ReadWrite_PreservesNamesThatAreCSharpKeywords()
    {
        var model = new GenericModel
        {
            Name = "namespace",
        };
        var entity = new GenericEntity
        {
            Name = "class",
        };
        entity.Properties.Add(new GenericProperty
        {
            Name = "event",
            IsNullable = false,
        });
        entity.Relationships.Add(new GenericRelationship
        {
            Entity = "class",
            Role = "parent",
            IsNullable = true,
        });
        model.Entities.Add(entity);
        var instance = new GenericInstance
        {
            ModelName = model.Name,
        };
        var record = new GenericRecord
        {
            Id = "one",
        };
        record.Values.Add("event", "value");
        record.RelationshipIds.Add("parentId", "one");
        instance.GetOrCreateEntityRecords("class").Add(record);
        var state = new InMemoryWorkspace(model, instance);

        var csharp = MetaCSharpWriter.Write(state);
        var roundTripped = MetaCSharpReader.Read(csharp);

        AssertCompiles(csharp);
        Assert.Contains(
            csharp.Sources.Values,
            source => source.Contains(
                "class @class",
                StringComparison.Ordinal));
        Assert.Null(InMemoryWorkspaceComparer.FindDifference(
            state,
            roundTripped));
    }

    [Fact]
    public void Read_RejectsACollectionThatIsNotReturned()
    {
        var csharp = MetaCSharpWriter.Write(
            WorkspaceTestData.BuildState());
        var source = csharp.Sources["RoundTrip.meta.cs"];
        source = source.Replace(
            "        var model = RoundTripModel.CreateEmpty();",
            """
                        var model = RoundTripModel.CreateEmpty();
                        var ignored = new Node { Id = "ignored", RequiredText = "Ignored" };
            """,
            StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidDataException>(() =>
            MetaCSharpReader.Read(WithSource(csharp, source)));

        Assert.Contains("declares a record that is not added", exception.Message);
    }

    [Fact]
    public void Read_RejectsAnUnmodeledCollectionMutation()
    {
        var csharp = MetaCSharpWriter.Write(
            WorkspaceTestData.BuildState());
        var source = csharp.Sources["RoundTrip.meta.cs"];
        source = source.Replace(
            "        return model;",
            """
                model.NodeList.Add(new Node { Id = "added", RequiredText = "Added" });
                return model;
            """,
            StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidDataException>(() =>
            MetaCSharpReader.Read(WithSource(csharp, source)));

        Assert.Contains("unsupported expression", exception.Message);
    }

    [Fact]
    public void Read_RequiresCompilableCSharp()
    {
        var csharp = MetaCSharpWriter.Write(
            WorkspaceTestData.BuildState());
        var source = csharp.Sources["RoundTrip.meta.cs"] +
                     Environment.NewLine +
                     "internal sealed class Broken { MissingType Value { get; set; } }";

        var exception = Assert.Throws<InvalidDataException>(() =>
            MetaCSharpReader.Read(WithSource(csharp, source)));

        Assert.Contains("does not compile", exception.Message);
    }

    private static MetaCSharp WithSource(
        MetaCSharp csharp,
        string source)
    {
        return new MetaCSharp(
            csharp.Sources.ToDictionary(
                item => item.Key,
                item => item.Key == "RoundTrip.meta.cs"
                    ? source
                    : item.Value,
                StringComparer.OrdinalIgnoreCase));
    }

    private static async Task WriteSourcesAsync(
        string root,
        MetaCSharp csharp)
    {
        Directory.CreateDirectory(root);
        foreach (var source in csharp.Sources)
        {
            var path = Path.Combine(root, source.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, source.Value);
        }

        WorkspaceMetaFile.WriteCSharp(root, csharp.Sources.Keys.ToArray());
    }

    private static void DeleteDirectory(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertCompiles(MetaCSharp csharp)
    {
        var platformAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        var references = platformAssemblies
            .Split(Path.PathSeparator)
            .Select(path =>
                MetadataReference.CreateFromFile(path));
        var syntaxTrees = csharp.Sources
            .Select(source => CSharpSyntaxTree.ParseText(
                source.Value,
                path: source.Key));
        var compilation = CSharpCompilation.Create(
            "GeneratedMetadata",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            string.Join(
                Environment.NewLine,
                errors.Select(error => error.ToString())));
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(
                   value,
                   index,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
