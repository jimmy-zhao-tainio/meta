using System.Linq;
using Meta.Core.Domain;
using Meta.Core.Services;
using MetaWeave.Core;
using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace MetaWeave.Tests;

public sealed class SuggestServiceTests
{
    [Fact]
    public async Task SuggestAsync_OmitsBindingsThatAlreadyExist()
    {
        var workspace = await new WorkspaceService().LoadAsync(GetFixtureWorkspacePath("Weave-Mapping-ReferenceType"), searchUpward: false);

        var result = await new MetaWeaveSuggestService().SuggestAsync(workspace);

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

            var workspaceService = new WorkspaceService();
            var weaveWorkspace = MetaWeaveWorkspaces.CreateEmptyMetaWeaveWorkspace(Path.Combine(root, "Weave"));
            var authoringService = new MetaWeaveAuthoringService(workspaceService);
            await authoringService.AddModelReferenceAsync(weaveWorkspace, "Source", "SampleReferenceBindingCatalog", sourcePath);
            await authoringService.AddModelReferenceAsync(weaveWorkspace, "Reference", "SampleReferenceCatalog", referencePath);

            var result = await new MetaWeaveSuggestService(workspaceService).SuggestAsync(weaveWorkspace);

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

            var workspaceService = new WorkspaceService();
            var weaveWorkspace = MetaWeaveWorkspaces.CreateEmptyMetaWeaveWorkspace(Path.Combine(root, "Weave"));
            var authoringService = new MetaWeaveAuthoringService(workspaceService);
            await authoringService.AddModelReferenceAsync(weaveWorkspace, "Source", "SampleReferenceBindingCatalog", sourcePath);
            await authoringService.AddModelReferenceAsync(weaveWorkspace, "Reference", "SampleReferenceCatalog", referencePath);

            var result = await new MetaWeaveSuggestService(workspaceService).SuggestAsync(weaveWorkspace);

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

            var workspaceService = new WorkspaceService();
            var weaveWorkspace = MetaWeaveWorkspaces.CreateEmptyMetaWeaveWorkspace(Path.Combine(root, "Weave"));
            var authoringService = new MetaWeaveAuthoringService(workspaceService);
            await authoringService.AddModelReferenceAsync(weaveWorkspace, "Source", "SampleReferenceBindingCatalog", sourcePath);
            await authoringService.AddModelReferenceAsync(weaveWorkspace, "ReferenceA", "SampleReferenceCatalog", referenceAPath);
            await authoringService.AddModelReferenceAsync(weaveWorkspace, "ReferenceB", "SampleReferenceCatalog", referenceBPath);

            var result = await new MetaWeaveSuggestService(workspaceService).SuggestAsync(weaveWorkspace);

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
        return Path.Combine(FindRepositoryRoot(), "MetaWeave.Workspaces", name);
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
        var workspace = new Workspace
        {
            WorkspaceRootPath = path,
            MetadataRootPath = Path.Combine(path, "metadata"),
            WorkspaceConfig = MetaWorkspaceConfig.CreateDefault(),
            Model = new GenericModel
            {
                Name = "SampleReferenceCatalog",
                Entities =
                {
                    new GenericEntity
                    {
                        Name = "ReferenceType",
                        Properties =
                        {
                            new GenericProperty { Name = "Name", DataType = "string", IsNullable = false },
                        },
                    },
                },
            },
            Instance = new GenericInstance
            {
                ModelName = "SampleReferenceCatalog",
            },
            IsDirty = true,
        };
        AddRow(workspace.Instance, "ReferenceType", "type:decimal", ("Name", "decimal"));
        AddRow(workspace.Instance, "ReferenceType", "type:int", ("Name", "int"));
        AddRow(workspace.Instance, "ReferenceType", "type:string", ("Name", "string"));
        new WorkspaceService().SaveAsync(workspace).GetAwaiter().GetResult();
        return path;
    }

    private static string CreateSourceWorkspace(string root, string folderName, string modelName, params (string PropertyName, string[] Values)[] propertySets)
    {
        var path = Path.Combine(root, folderName);
        var entity = new GenericEntity
        {
            Name = "Mapping",
        };
        entity.Properties.Add(new GenericProperty { Name = "Name", DataType = "string", IsNullable = false });
        foreach (var propertySet in propertySets)
        {
            entity.Properties.Add(new GenericProperty { Name = propertySet.PropertyName, DataType = "string", IsNullable = false });
        }

        var workspace = new Workspace
        {
            WorkspaceRootPath = path,
            MetadataRootPath = Path.Combine(path, "metadata"),
            WorkspaceConfig = MetaWorkspaceConfig.CreateDefault(),
            Model = new GenericModel
            {
                Name = modelName,
                Entities = { entity },
            },
            Instance = new GenericInstance
            {
                ModelName = modelName,
            },
            IsDirty = true,
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

            AddRow(workspace.Instance, "Mapping", $"mapping:{index + 1}", values.ToArray());
        }

        new WorkspaceService().SaveAsync(workspace).GetAwaiter().GetResult();
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
