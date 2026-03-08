using Meta.Core.Domain;
using Meta.Core.Services;
using MetaFabric.Core;

namespace MetaFabric.Tests;

public sealed class MetaFabricServiceTests
{
    [Fact]
    public async Task CheckAsync_SanctionedScopedFabric_Passes()
    {
        var workspace = await new WorkspaceService().LoadAsync(GetFixtureWorkspacePath("Fabric-Scoped-Group-CategoryItem"), searchUpward: false);

        var result = await new MetaFabricService().CheckAsync(workspace);

        Assert.False(result.HasErrors);
        Assert.Equal(2, result.WeaveCount);
        Assert.Equal(2, result.BindingCount);
        Assert.Equal(5, result.ResolvedRowCount);
    }

    [Fact]
    public async Task SuggestAsync_SanctionedUnscopedFabric_SuggestsParentScope()
    {
        var workspace = await new WorkspaceService().LoadAsync(GetFixtureWorkspacePath("Fabric-Suggest-Scoped-Group-CategoryItem"), searchUpward: false);

        var result = await new MetaFabricSuggestService().SuggestAsync(workspace);

        Assert.Equal(1, result.SuggestionCount);
        Assert.Equal(0, result.WeakSuggestionCount);

        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal("ChildItem", suggestion.ChildBindingReferenceName);
        Assert.Equal("Item.Name -> CategoryItem.Name", suggestion.ChildBindingName);
        Assert.Equal("ParentGroup", suggestion.ParentBindingReferenceName);
        Assert.Equal("Group.Name -> Category.Name", suggestion.ParentBindingName);
        Assert.Equal("GroupId", suggestion.SourceParentReferenceName);
        Assert.Equal("CategoryId", suggestion.TargetParentReferenceName);
    }

    [Fact]
    public async Task SuggestAsync_SanctionedScopedFabric_ReturnsNoSuggestions()
    {
        var workspace = await new WorkspaceService().LoadAsync(GetFixtureWorkspacePath("Fabric-Scoped-Group-CategoryItem"), searchUpward: false);

        var result = await new MetaFabricSuggestService().SuggestAsync(workspace);

        Assert.Equal(0, result.SuggestionCount);
        Assert.Equal(0, result.WeakSuggestionCount);
    }

    [Fact]
    public async Task CheckAsync_Fails_WhenScopeRequirementCreatesCycle()
    {
        var root = CreateTempRoot("metafabric-cycle");
        try
        {
            var workspace = MetaFabricWorkspaces.CreateEmptyMetaFabricWorkspace(Path.Combine(root, "Fabric"));

            var weaveReference = new GenericRecord { Id = "1" };
            weaveReference.Values["Alias"] = "Scoped";
            weaveReference.Values["WorkspacePath"] = GetWeaveWorkspacePath("Weave-Scoped-Group-Category");
            workspace.Instance.GetOrCreateEntityRecords("WeaveReference").Add(weaveReference);

            var parentBinding = new GenericRecord { Id = "1" };
            parentBinding.Values["Name"] = "Parent";
            parentBinding.Values["BindingName"] = "Group.Name -> Category.Name";
            parentBinding.RelationshipIds["WeaveReferenceId"] = weaveReference.Id;
            workspace.Instance.GetOrCreateEntityRecords("BindingReference").Add(parentBinding);

            var childBinding = new GenericRecord { Id = "2" };
            childBinding.Values["Name"] = "Child";
            childBinding.Values["BindingName"] = "Group.Name -> Category.Name";
            childBinding.RelationshipIds["WeaveReferenceId"] = weaveReference.Id;
            workspace.Instance.GetOrCreateEntityRecords("BindingReference").Add(childBinding);

            var scopeA = new GenericRecord { Id = "1" };
            scopeA.Values["SourceParentReferenceName"] = "Id";
            scopeA.Values["TargetParentReferenceName"] = "Id";
            scopeA.RelationshipIds["BindingId"] = childBinding.Id;
            scopeA.RelationshipIds["ParentBindingId"] = parentBinding.Id;
            workspace.Instance.GetOrCreateEntityRecords("BindingScopeRequirement").Add(scopeA);

            var scopeB = new GenericRecord { Id = "2" };
            scopeB.Values["SourceParentReferenceName"] = "Id";
            scopeB.Values["TargetParentReferenceName"] = "Id";
            scopeB.RelationshipIds["BindingId"] = parentBinding.Id;
            scopeB.RelationshipIds["ParentBindingId"] = childBinding.Id;
            workspace.Instance.GetOrCreateEntityRecords("BindingScopeRequirement").Add(scopeB);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new MetaFabricService().CheckAsync(workspace));
            Assert.Contains("contains a cycle", ex.Message);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    private static string GetFixtureWorkspacePath(string name)
    {
        return Path.Combine(FindRepositoryRoot(), "MetaFabric.Workspaces", name);
    }

    private static string GetWeaveWorkspacePath(string name)
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

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
