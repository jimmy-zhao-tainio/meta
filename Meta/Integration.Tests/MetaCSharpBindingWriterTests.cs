using System.Diagnostics;
using Meta.Operations.Domain;
using Meta.Surfaces.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Meta.Integration.Tests;

public sealed class MetaCSharpBindingWriterTests
{
    [Fact]
    public void BindingDependsOnTheContractAndIsDeterministicForReorderedMetadata()
    {
        var state = CreateState();
        var generated = MetaCSharpBindingWriter.Write(state.Model);
        Assert.Equal(2, generated.Sources.Count);
        var source = new KeyValuePair<string, string>("namespace.bindings.cs", generated.Sources["namespace.bindings.cs"]);
        Assert.Contains("namespace @namespace;", source.Value);
        Assert.Contains("public sealed class namespaceWorkspaceBinding", source.Value);
        Assert.Contains("ITypedWorkspaceBinding<global::@namespace.namespaceModel>", source.Value);
        Assert.DoesNotContain("sentinel instance text", source.Value);
        Assert.DoesNotContain("BuiltIn", source.Value);
        Assert.DoesNotContain("Microsoft.CodeAnalysis", source.Value);
        Assert.DoesNotContain("System.Reflection", source.Value);
        Assert.DoesNotContain("Activator", source.Value);

        var reordered = state.Model.Clone();
        reordered.Entities.Reverse();
        foreach (var entity in reordered.Entities)
        {
            entity.Properties.Reverse();
            entity.Relationships.Reverse();
        }
        var regenerated = MetaCSharpBindingWriter.Write(reordered);
        foreach (var artifact in generated.Sources)
            Assert.Equal(artifact.Value, regenerated.Sources[artifact.Key]);
        var access = generated.Sources["namespace.access.cs"];
        Assert.Contains("public static class namespaceModelAccess", access);
        Assert.Contains("VisitRelationships", access);
        Assert.DoesNotContain("Meta.TypedModels", access);
        Assert.DoesNotContain("System.Reflection", access);
        Assert.DoesNotContain("sentinel instance text", access);
    }

    [Fact]
    public async Task GeneratedBindingExecutesExactProjectionAndIntegrityChecksInASeparateProcess()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "meta-explicit-binding-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            var state = CreateState();
            var modelSource = Assert.Single(MetaCSharpWriter.Write(state).Sources).Value;
            var generated = MetaCSharpBindingWriter.Write(state.Model);
            var sources = new[] { modelSource, HarnessSource }.Concat(generated.Sources.Values);
            var platformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
                ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
            var dependencyNames = new[] { "Meta.Operations.dll", "Meta.TypedModels.dll" };
            var references = platformAssemblies.Split(Path.PathSeparator)
                .Concat(dependencyNames.Select(name => Path.Combine(AppContext.BaseDirectory, name)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create(
                "BindingExecutionProof",
                sources.Select(source => CSharpSyntaxTree.ParseText(source)), references,
                new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
            var executablePath = Path.Combine(root, "BindingExecutionProof.dll");
            await using (var target = File.Create(executablePath))
            {
                var result = compilation.Emit(target);
                Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            }
            foreach (var name in dependencyNames)
            {
                File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(root, name));
            }
            await File.WriteAllTextAsync(Path.Combine(root, "BindingExecutionProof.runtimeconfig.json"),
                """{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}""");
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add(executablePath);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start generated binding proof.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
            Assert.True(process.ExitCode == 0, await output + Environment.NewLine + await error);
            Assert.Contains("PASS exact projection, fresh canonical graphs, and integrity rejection", await output);
        }
        finally
        {
            var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "meta-explicit-binding-tests")) + Path.DirectorySeparatorChar;
            if (root.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InvalidMetadataIsRejectedBeforeEmission()
    {
        var model = CreateState().Model;
        model.Entities[0].Relationships.Add(new GenericRelationship { Entity = "Missing", IsNullable = true });
        Assert.Throws<InvalidOperationException>(() => MetaCSharpBindingWriter.Write(model));
    }

    [Theory]
    [InlineData("namespaceWorkspaceBinding")]
    [InlineData("namespaceModel")]
    [InlineData("namespaceInstance")]
    [InlineData("namespaceModelAccess")]
    [InlineData("InamespaceCollectionVisitor")]
    [InlineData("InamespaceRelationshipVisitor")]
    public void EntityNamesCannotCollideWithGeneratedTypes(string entityName)
    {
        var model = CreateState().Model;
        model.Entities.Add(new GenericEntity { Name = entityName });

        var error = Assert.Throws<InvalidOperationException>(() => MetaCSharpBindingWriter.Write(model));

        Assert.Contains($"Entity '{entityName}'", error.Message);
        Assert.Contains("conflicts with a generated C# type in namespace 'namespace'", error.Message);
    }

    private static InMemoryWorkspace CreateState()
    {
        var model = new GenericModel { Name = "namespace" };
        var node = new GenericEntity { Name = "class" };
        node.Properties.Add(new GenericProperty { Name = "event" });
        node.Properties.Add(new GenericProperty { Name = "namespace", IsNullable = true });
        node.Relationships.Add(new GenericRelationship { Entity = "Target" });
        node.Relationships.Add(new GenericRelationship { Entity = "class", Role = "parent", IsNullable = true });
        var target = new GenericEntity { Name = "Target" };
        target.Properties.Add(new GenericProperty { Name = "Name" });
        model.Entities.Add(node);
        model.Entities.Add(target);
        var instance = new GenericInstance { ModelName = model.Name };
        var source = new GenericRecord { Id = "source" };
        source.Values.Add("event", "sentinel instance text");
        source.Values.Add("namespace", "");
        source.RelationshipIds.Add("TargetId", "target");
        source.RelationshipIds.Add("parentId", "source");
        instance.GetOrCreateEntityRecords("class").Add(source);
        var targetRow = new GenericRecord { Id = "target" };
        targetRow.Values.Add("Name", "Target");
        instance.GetOrCreateEntityRecords("Target").Add(targetRow);
        return new InMemoryWorkspace(model, instance);
    }

    private const string HarnessSource = """
        using System;
        using System.Collections.Generic;
        using Meta.Operations.Domain;
        using Meta.TypedModels;

        internal static class Program
        {
            private static void Main()
            {
                ITypedWorkspaceBinding<global::@namespace.namespaceModel> binding = new global::@namespace.namespaceWorkspaceBinding();
                foreach (var optional in new string?[] { null, "", " ", " \t\r\n ", "Åsa 漢字 🙂", "line1\r\nline2\n" })
                foreach (var required in new[] { "", " ", " \t\r\n ", "Åsa 漢字 🙂", "line1\r\nline2\n" })
                {
                    var state = State();
                    var record = state.Instance.RecordsByEntity["class"][0];
                    if (optional is null) record.Values.Remove("namespace");
                    else record.Values["namespace"] = optional;
                    record.Values["event"] = required;
                    var typed = binding.Read(state);
                    Check(typed.classList[0].@namespace == optional && typed.classList[0].@event == required, "exact string values");
                    var restored = binding.Write(typed, state.Model);
                    Check(InMemoryWorkspaceComparer.FindDifference(state, restored) is null, "full exact state");
                    Check(!ReferenceEquals(state.Model, restored.Model), "output owns cloned supplied contract");
                    var fresh = binding.Read(state);
                    Check(!ReferenceEquals(typed, fresh) && !ReferenceEquals(typed.classList[0], fresh.classList[0]), "fresh independent graph");
                    Check(ReferenceEquals(typed.classList[0].Target, typed.TargetList[0]), "canonical required reference");
                    Check(ReferenceEquals(typed.classList[0].parent, typed.classList[0]), "canonical nullable self reference");
                }
                var nullable = State();
                nullable.Instance.RecordsByEntity["class"][0].RelationshipIds.Remove("parentId");
                Check(binding.Read(nullable).classList[0].parent is null, "absent nullable relationship");
                Check(InMemoryWorkspaceComparer.FindDifference(nullable, binding.Write(binding.Read(nullable), nullable.Model)) is null, "nullable relationship roundtrip");

                var valid = State();
                var duplicate = binding.Read(valid);
                duplicate.classList.Add(new global::@namespace.@class { Id = "SOURCE" });
                Reject(() => binding.Write(duplicate, valid.Model), "duplicate typed IDs");
                var noId = binding.Read(valid);
                noId.classList[0].Id = null!;
                Reject(() => binding.Write(noId, valid.Model), "missing typed ID");
                var noValue = binding.Read(valid);
                noValue.classList[0].@event = null!;
                Reject(() => binding.Write(noValue, valid.Model), "missing required scalar");
                var noLink = binding.Read(valid);
                noLink.classList[0].Target = null!;
                Reject(() => binding.Write(noLink, valid.Model), "missing required relationship");
                var detached = binding.Read(valid);
                detached.classList[0].Target = new global::@namespace.Target { Id = "target", Name = "Target" };
                Reject(() => binding.Write(detached, valid.Model), "equal-ID noncanonical reference");
                detached.classList[0].Target.Id = "absent";
                Reject(() => binding.Write(detached, valid.Model), "dangling typed reference");
                var badGeneric = State();
                badGeneric.Instance.RecordsByEntity["class"][0].RelationshipIds["TargetId"] = "absent";
                Reject(() => binding.Read(badGeneric), "dangling generic reference");
                badGeneric = State();
                badGeneric.Instance.RecordsByEntity["class"][0].Values.Remove("event");
                Reject(() => binding.Read(badGeneric), "missing generic required scalar");
                badGeneric = State();
                badGeneric.Instance.RecordsByEntity["class"][0].RelationshipIds.Remove("TargetId");
                Reject(() => binding.Read(badGeneric), "missing generic required relationship");
                var stale = State();
                stale.Model.Entities[0].Properties.Add(new GenericProperty { Name = "Added", IsNullable = true });
                Reject(() => binding.Read(stale), "stale read schema");
                Reject(() => binding.Write(binding.Read(valid), stale.Model), "stale write schema");
                var graph = binding.Read(State());
                var collections = new Collections(graph);
                global::@namespace.namespaceModelAccess.VisitCollections(collections);
                Check(collections.Count == 2, "all generated collection delegates visited");
                var relationships = new Relationships(graph);
                global::@namespace.namespaceModelAccess.VisitRelationships(relationships);
                Check(relationships.Count == 2, "all generated relationship delegates visited");
                Check(ReferenceEquals(graph.classList[0].parent, graph.classList[0]), "visitation preserves canonical self reference");
                Console.WriteLine("PASS exact projection, fresh canonical graphs, and integrity rejection");
            }

            private sealed class Collections(global::@namespace.namespaceModel graph) : global::@namespace.InamespaceCollectionVisitor
            {
                public int Count;
                public void Visit<TEntity>(string entityName, Func<global::@namespace.namespaceModel, IList<TEntity>> rows,
                    Func<TEntity, string> readId, Action<TEntity, string> writeId) where TEntity : class
                {
                    Count++;
                    Check(rows(graph).Count == 1, "collection selects the authored row");
                    foreach (var row in rows(graph))
                    {
                        var id = readId(row);
                        writeId(row, "rewritten/" + id);
                        Check(readId(row) == "rewritten/" + id, "collection ID setter and getter");
                    }
                }
            }

            private sealed class Relationships(global::@namespace.namespaceModel graph) : global::@namespace.InamespaceRelationshipVisitor
            {
                public int Count;
                public void Visit<TRow, TTarget>(string entityName, string navigationName, string targetEntityName, bool isNullable,
                    Func<global::@namespace.namespaceModel, IList<TRow>> rows, Func<TRow, TTarget?> read,
                    Action<TRow, TTarget?> write, Func<TTarget, string> readTargetId) where TRow : class where TTarget : class
                {
                    Count++;
                    foreach (var row in rows(graph))
                    {
                        var target = read(row)!;
                        Check(readTargetId(target).StartsWith("rewritten/", StringComparison.Ordinal), "relationship reaches canonical mutated target");
                        if (isNullable)
                        {
                            write(row, null);
                            Check(read(row) is null, "nullable relationship setter");
                        }
                        write(row, target);
                        Check(ReferenceEquals(read(row), target), "relationship setter preserves identity");
                    }
                }
            }

            private static void Check(bool condition, string name)
            {
                if (!condition) throw new InvalidOperationException("FAILED " + name);
            }

            private static void Reject(Action action, string name)
            {
                try { action(); }
                catch (InvalidOperationException) { return; }
                throw new InvalidOperationException("FAILED rejection: " + name);
            }

            private static InMemoryWorkspace State()
            {
                var model = new GenericModel { Name = "namespace" };
                var node = new GenericEntity { Name = "class" };
                node.Properties.Add(new GenericProperty { Name = "event" });
                node.Properties.Add(new GenericProperty { Name = "namespace", IsNullable = true });
                node.Relationships.Add(new GenericRelationship { Entity = "Target" });
                node.Relationships.Add(new GenericRelationship { Entity = "class", Role = "parent", IsNullable = true });
                var target = new GenericEntity { Name = "Target" };
                target.Properties.Add(new GenericProperty { Name = "Name" });
                model.Entities.Add(node);
                model.Entities.Add(target);
                var instance = new GenericInstance { ModelName = model.Name };
                var source = new GenericRecord { Id = "source" };
                source.Values.Add("event", "sentinel instance text");
                source.Values.Add("namespace", "");
                source.RelationshipIds.Add("TargetId", "target");
                source.RelationshipIds.Add("parentId", "source");
                instance.GetOrCreateEntityRecords("class").Add(source);
                var targetRow = new GenericRecord { Id = "target" };
                targetRow.Values.Add("Name", "Target");
                instance.GetOrCreateEntityRecords("Target").Add(targetRow);
                return new InMemoryWorkspace(model, instance);
            }
        }
        """;
}
