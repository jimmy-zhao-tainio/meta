using System.Linq;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Serialization;
using MetaWeave.Core;
using MetaWeaveModel = global::MetaWeave.MetaWeaveModel;

namespace MetaWeave.Tests;

public sealed class SuggestServiceTests
{
    [Fact]
    public async Task SuggestAsync_OmitsBindingsThatAlreadyExist()
    {
        var workspacePath = GetFixtureWorkspacePath("Weave-Mapping-ReferenceType");
        var workspace = MetaWeaveModel.LoadFromXmlWorkspace(workspacePath, searchUpward: false);

        var result = await new MetaWeaveSuggestService().SuggestAsync(workspace, workspacePath);

        Assert.Empty(result.Suggestions);
        Assert.Empty(result.WeakSuggestions);
    }

    [Fact]
    public async Task SuggestAsync_FindsStrictlyResolvableExactIdBindings()
    {
        var root = CreateTempRoot("metaweave-suggest-exact");
        try
        {
            var referencePath = CreateReferenceWorkspace(root, "Reference");
            var sourcePath = CreateSourceWorkspace(
                root,
                "Source",
                "SampleReferenceBindingCatalog",
                ("ReferenceTypeId", new[] { "type:string", "type:int", "type:string" }));

            var weaveWorkspacePath = Path.Combine(root, "Weave");
            var weaveWorkspace = MetaWeaveModel.CreateEmpty();
            var authoringService = new MetaWeaveAuthoringService();
            await authoringService.AddModelReferenceAsync(weaveWorkspace, weaveWorkspacePath, "Source", "SampleReferenceBindingCatalog", sourcePath);
            await authoringService.AddModelReferenceAsync(weaveWorkspace, weaveWorkspacePath, "Reference", "SampleReferenceCatalog", referencePath);

            var result = await new MetaWeaveSuggestService().SuggestAsync(weaveWorkspace, weaveWorkspacePath);

            Assert.Single(result.Suggestions);
            Assert.Empty(result.WeakSuggestions);
            var suggestion = Assert.Single(result.Suggestions);
            Assert.Equal("ReferenceTypeId", suggestion.SourceProperty);
            Assert.Equal("ReferenceType", suggestion.TargetEntity);
            Assert.Equal("Id", suggestion.TargetProperty);
            Assert.True(string.IsNullOrWhiteSpace(suggestion.InferredRole));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SuggestAsync_ReportsRoleStyleIdMatchesAsWeakSuggestions()
    {
        var root = CreateTempRoot("metaweave-suggest-weak-role");
        try
        {
            var referencePath = CreateReferenceWorkspace(root, "Reference");
            var sourcePath = CreateSourceWorkspace(
                root,
                "Source",
                "SampleReferenceBindingCatalog",
                ("SourceReferenceTypeId", new[] { "type:string", "type:int", "type:string" }),
                ("TargetReferenceTypeId", new[] { "type:int", "type:decimal", "type:int" }));

            var weaveWorkspacePath = Path.Combine(root, "Weave");
            var weaveWorkspace = MetaWeaveModel.CreateEmpty();
            var authoringService = new MetaWeaveAuthoringService();
            await authoringService.AddModelReferenceAsync(weaveWorkspace, weaveWorkspacePath, "Source", "SampleReferenceBindingCatalog", sourcePath);
            await authoringService.AddModelReferenceAsync(weaveWorkspace, weaveWorkspacePath, "Reference", "SampleReferenceCatalog", referencePath);

            var result = await new MetaWeaveSuggestService().SuggestAsync(weaveWorkspace, weaveWorkspacePath);

            Assert.Empty(result.Suggestions);
            Assert.Equal(2, result.WeakSuggestionCount);

            var sourceWeak = Assert.Single(result.WeakSuggestions, item => string.Equals(item.SourceProperty, "SourceReferenceTypeId", StringComparison.Ordinal));
            var sourceCandidate = Assert.Single(sourceWeak.Candidates);
            Assert.Equal("ReferenceType", sourceCandidate.TargetEntity);
            Assert.Equal("Id", sourceCandidate.TargetProperty);
            Assert.Equal("SourceReferenceType", sourceCandidate.InferredRole);

            var targetWeak = Assert.Single(result.WeakSuggestions, item => string.Equals(item.SourceProperty, "TargetReferenceTypeId", StringComparison.Ordinal));
            var targetCandidate = Assert.Single(targetWeak.Candidates);
            Assert.Equal("ReferenceType", targetCandidate.TargetEntity);
            Assert.Equal("Id", targetCandidate.TargetProperty);
            Assert.Equal("TargetReferenceType", targetCandidate.InferredRole);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SuggestAsync_ReportsAmbiguousExactMatchesAsWeakSuggestions()
    {
        var root = CreateTempRoot("metaweave-suggest-ambiguous");
        try
        {
            var referenceAPath = CreateReferenceWorkspace(root, "ReferenceA");
            var referenceBPath = CreateReferenceWorkspace(root, "ReferenceB");
            var sourcePath = CreateSourceWorkspace(
                root,
                "Source",
                "SampleReferenceBindingCatalog",
                ("ReferenceTypeId", new[] { "type:string", "type:int", "type:string" }));

            var weaveWorkspacePath = Path.Combine(root, "Weave");
            var weaveWorkspace = MetaWeaveModel.CreateEmpty();
            var authoringService = new MetaWeaveAuthoringService();
            await authoringService.AddModelReferenceAsync(weaveWorkspace, weaveWorkspacePath, "Source", "SampleReferenceBindingCatalog", sourcePath);
            await authoringService.AddModelReferenceAsync(weaveWorkspace, weaveWorkspacePath, "ReferenceA", "SampleReferenceCatalog", referenceAPath);
            await authoringService.AddModelReferenceAsync(weaveWorkspace, weaveWorkspacePath, "ReferenceB", "SampleReferenceCatalog", referenceBPath);

            var result = await new MetaWeaveSuggestService().SuggestAsync(weaveWorkspace, weaveWorkspacePath);

            Assert.Empty(result.Suggestions);
            var weak = Assert.Single(result.WeakSuggestions);
            Assert.Equal("ReferenceTypeId", weak.SourceProperty);
            Assert.Equal(2, weak.Candidates.Count);
            Assert.All(weak.Candidates, candidate => Assert.True(string.IsNullOrWhiteSpace(candidate.InferredRole)));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    private static string GetFixtureWorkspacePath(string name)
    {
        return Path.Combine(FindRepositoryRoot(), "MetaWeave", "Workspaces", name);
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "Metadata.Framework.sln")))
            {
                return directory;
            }

            var parent = Directory.GetParent(directory);
            if (parent == null)
            {
                break;
            }

            directory = parent.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }

    private static string CreateTempRoot(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateReferenceWorkspace(string root, string folderName)
    {
        var path = Path.Combine(root, folderName);
        var model = new GenericModel
        {
            Name = "SampleReferenceCatalog",
            Entities =
            {
                new GenericEntity
                {
                    Name = "ReferenceType",
                    Properties =
                    {
                        new GenericProperty { Name = "Name", IsNullable = false },
                    },
                },
            },
        };
        var instance = new GenericInstance
        {
            ModelName = "SampleReferenceCatalog",
        };
        AddRow(instance, "ReferenceType", "type:decimal", ("Name", "decimal"));
        AddRow(instance, "ReferenceType", "type:int", ("Name", "int"));
        AddRow(instance, "ReferenceType", "type:string", ("Name", "string"));
        XmlWorkspaceWriter.WriteNewAsync(
                new InMemoryWorkspace(model, instance),
                path)
            .GetAwaiter()
            .GetResult();
        return path;
    }

    private static string CreateSourceWorkspace(string root, string folderName, string modelName, params (string PropertyName, string[] Values)[] propertySets)
    {
        var path = Path.Combine(root, folderName);
        var entity = new GenericEntity
        {
            Name = "Mapping",
        };
        entity.Properties.Add(new GenericProperty { Name = "Name", IsNullable = false });
        foreach (var propertySet in propertySets)
        {
            entity.Properties.Add(new GenericProperty { Name = propertySet.PropertyName, IsNullable = false });
        }

        var model = new GenericModel
        {
            Name = modelName,
            Entities = { entity },
        };
        var instance = new GenericInstance
        {
            ModelName = modelName,
        };

        var rowCount = propertySets.Max(item => item.Values.Length);
        for (var index = 0; index < rowCount; index++)
        {
            var values = new List<(string Key, string Value)>
            {
                ("Name", $"Mapping{index + 1}")
            };
            foreach (var propertySet in propertySets)
            {
                values.Add((propertySet.PropertyName, propertySet.Values[index]));
            }

            AddRow(instance, "Mapping", $"mapping:{index + 1}", values.ToArray());
        }

        XmlWorkspaceWriter.WriteNewAsync(
                new InMemoryWorkspace(model, instance),
                path)
            .GetAwaiter()
            .GetResult();
        return path;
    }

    private static void AddRow(GenericInstance instance, string entityName, string id, params (string Key, string Value)[] values)
    {
        var row = new GenericRecord
        {
            Id = id,
        };

        foreach (var (key, value) in values)
        {
            row.Values[key] = value;
        }

        instance.GetOrCreateEntityRecords(entityName).Add(row);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
