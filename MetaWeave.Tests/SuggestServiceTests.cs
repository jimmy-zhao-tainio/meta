using Meta.Core.Domain;
using Meta.Core.Services;
using MetaWeave.Core;

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
    public async Task SuggestAsync_FindsStrictlyResolvableRepeatedIdBindings()
    {
        var root = Path.Combine(Path.GetTempPath(), "metaweave-suggest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "Source");
            var referencePath = Path.Combine(root, "Reference");
            CopyDirectory(GetFixtureWorkspacePath("SampleSourceCatalog"), sourcePath);
            CopyDirectory(GetFixtureWorkspacePath("SampleReferenceCatalog"), referencePath);

            var workspaceService = new WorkspaceService();
            var sourceWorkspace = await workspaceService.LoadAsync(sourcePath, searchUpward: false);
            sourceWorkspace.Instance.GetOrCreateEntityRecords("Mapping").Add(new GenericRecord
            {
                Id = "mapping:string-to-decimal:again",
                Values =
                {
                    ["Name"] = "StringToDecimalAgain",
                    ["SourceTypeId"] = "type:string",
                    ["TargetTypeId"] = "type:decimal",
                },
            });
            sourceWorkspace.IsDirty = true;
            await workspaceService.SaveAsync(sourceWorkspace);

            var weaveWorkspace = MetaWeaveWorkspaces.CreateEmptyMetaWeaveWorkspace(Path.Combine(root, "Weave"));
            var authoringService = new MetaWeaveAuthoringService(workspaceService);
            await authoringService.AddModelReferenceAsync(weaveWorkspace, "Source", "SampleSourceCatalog", sourcePath);
            await authoringService.AddModelReferenceAsync(weaveWorkspace, "Reference", "SampleReferenceCatalog", referencePath);

            var result = await new MetaWeaveSuggestService(workspaceService).SuggestAsync(weaveWorkspace);

            Assert.Equal(2, result.SuggestionCount);
            Assert.Empty(result.WeakSuggestions);
            Assert.Contains(result.Suggestions, item =>
                string.Equals(item.SourceModelAlias, "Source", StringComparison.Ordinal) &&
                string.Equals(item.SourceEntity, "Mapping", StringComparison.Ordinal) &&
                string.Equals(item.SourceProperty, "SourceTypeId", StringComparison.Ordinal) &&
                string.Equals(item.TargetModelAlias, "Reference", StringComparison.Ordinal) &&
                string.Equals(item.TargetEntity, "ReferenceType", StringComparison.Ordinal) &&
                string.Equals(item.TargetProperty, "Id", StringComparison.Ordinal));
            Assert.Contains(result.Suggestions, item =>
                string.Equals(item.SourceModelAlias, "Source", StringComparison.Ordinal) &&
                string.Equals(item.SourceEntity, "Mapping", StringComparison.Ordinal) &&
                string.Equals(item.SourceProperty, "TargetTypeId", StringComparison.Ordinal) &&
                string.Equals(item.TargetModelAlias, "Reference", StringComparison.Ordinal) &&
                string.Equals(item.TargetEntity, "ReferenceType", StringComparison.Ordinal) &&
                string.Equals(item.TargetProperty, "Id", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SuggestAsync_ReportsAmbiguousCrossWorkspaceMatchesAsWeakSuggestions()
    {
        var root = Path.Combine(Path.GetTempPath(), "metaweave-suggest-ambiguous", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "Source");
            var referenceAPath = Path.Combine(root, "ReferenceA");
            var referenceBPath = Path.Combine(root, "ReferenceB");
            CopyDirectory(GetFixtureWorkspacePath("SampleSourceCatalog"), sourcePath);
            CopyDirectory(GetFixtureWorkspacePath("SampleReferenceCatalog"), referenceAPath);
            CopyDirectory(GetFixtureWorkspacePath("SampleReferenceCatalog"), referenceBPath);

            var workspaceService = new WorkspaceService();
            var sourceWorkspace = await workspaceService.LoadAsync(sourcePath, searchUpward: false);
            sourceWorkspace.Instance.GetOrCreateEntityRecords("Mapping").Add(new GenericRecord
            {
                Id = "mapping:string-to-decimal:again",
                Values =
                {
                    ["Name"] = "StringToDecimalAgain",
                    ["SourceTypeId"] = "type:string",
                    ["TargetTypeId"] = "type:decimal",
                },
            });
            sourceWorkspace.IsDirty = true;
            await workspaceService.SaveAsync(sourceWorkspace);

            var weaveWorkspace = MetaWeaveWorkspaces.CreateEmptyMetaWeaveWorkspace(Path.Combine(root, "Weave"));
            var authoringService = new MetaWeaveAuthoringService(workspaceService);
            await authoringService.AddModelReferenceAsync(weaveWorkspace, "Source", "SampleSourceCatalog", sourcePath);
            await authoringService.AddModelReferenceAsync(weaveWorkspace, "ReferenceA", "SampleReferenceCatalog", referenceAPath);
            await authoringService.AddModelReferenceAsync(weaveWorkspace, "ReferenceB", "SampleReferenceCatalog", referenceBPath);

            var result = await new MetaWeaveSuggestService(workspaceService).SuggestAsync(weaveWorkspace);

            Assert.Empty(result.Suggestions);
            Assert.Equal(2, result.WeakSuggestionCount);
            Assert.Contains(result.WeakSuggestions, item =>
                string.Equals(item.SourceModelAlias, "Source", StringComparison.Ordinal) &&
                string.Equals(item.SourceEntity, "Mapping", StringComparison.Ordinal) &&
                string.Equals(item.SourceProperty, "SourceTypeId", StringComparison.Ordinal) &&
                item.Candidates.Count == 2);
            Assert.Contains(result.WeakSuggestions, item =>
                string.Equals(item.SourceModelAlias, "Source", StringComparison.Ordinal) &&
                string.Equals(item.SourceEntity, "Mapping", StringComparison.Ordinal) &&
                string.Equals(item.SourceProperty, "TargetTypeId", StringComparison.Ordinal) &&
                item.Candidates.Count == 2);
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

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);
        foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(targetPath, relative));
        }

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, file);
            var targetFile = Path.Combine(targetPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
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
