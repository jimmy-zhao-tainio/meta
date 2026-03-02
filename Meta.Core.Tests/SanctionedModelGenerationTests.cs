using System;
using System.IO;
using System.Threading.Tasks;
using Meta.Adapters;
using Meta.Core.Domain;
using Meta.Core.Serialization;
using Meta.Core.Services;
using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace Meta.Core.Tests;

public sealed class SanctionedModelGenerationTests
{
    [Fact]
    public void MetaWorkspaceReferenceModel_GeneratesCSharpWithTooling()
    {
        var repoRoot = FindRepositoryRoot();
        var services = new ServiceCollection();
        var modelPath = Path.Combine(repoRoot, "Meta.Core", "WorkspaceConfig", "Models", "MetaWorkspace.model.xml");
        var instancePath = Path.Combine(repoRoot, "Meta.Core", "WorkspaceConfig", "Models", "MetaWorkspace.instance.empty.xml");
        var outputPath = Path.Combine(Path.GetTempPath(), "metadata-sanctioned-tests", Guid.NewGuid().ToString("N"), "meta-workspace");

        try
        {
            var workspace = LoadWorkspaceFromContractFiles(modelPath, instancePath);
            var manifest = GenerationService.GenerateCSharp(workspace, outputPath, includeTooling: true);

            Assert.True(manifest.FileHashes.ContainsKey("MetaWorkspace.Tooling.cs"));
            Assert.True(File.Exists(Path.Combine(outputPath, "MetaWorkspace.Tooling.cs")));
        }
        finally
        {
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputPath)!);
        }
    }

    [Fact]
    public void SchemaCatalogReferenceModel_GeneratesCSharpWithTooling()
    {
        var repoRoot = FindRepositoryRoot();
        var services = new ServiceCollection();
        var modelPath = Path.Combine(repoRoot, "MetaSchema.Core", "Models", "SchemaCatalog.model.xml");
        var instancePath = Path.Combine(repoRoot, "MetaSchema.Core", "Models", "SchemaCatalog.instance.empty.xml");
        var outputPath = Path.Combine(Path.GetTempPath(), "metadata-sanctioned-tests", Guid.NewGuid().ToString("N"), "schema-catalog");

        try
        {
            var workspace = LoadWorkspaceFromContractFiles(modelPath, instancePath);
            var manifest = GenerationService.GenerateCSharp(workspace, outputPath, includeTooling: true);

            Assert.True(manifest.FileHashes.ContainsKey("SchemaCatalog.Tooling.cs"));
            Assert.True(File.Exists(Path.Combine(outputPath, "SchemaCatalog.Tooling.cs")));
        }
        finally
        {
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputPath)!);
        }
    }

    [Fact]
    public async Task TypeConversionCatalogWorkspace_GeneratesCSharpWithTooling()
    {
        var repoRoot = FindRepositoryRoot();
        var services = new ServiceCollection();
        var workspacePath = Path.Combine(repoRoot, "MetaSchema.Catalogs", "TypeConversionCatalog");
        var outputPath = Path.Combine(Path.GetTempPath(), "metadata-sanctioned-tests", Guid.NewGuid().ToString("N"), "type-conversion");

        try
        {
            var workspace = await services.WorkspaceService.LoadAsync(workspacePath);
            var manifest = GenerationService.GenerateCSharp(workspace, outputPath, includeTooling: true);

            Assert.True(manifest.FileHashes.ContainsKey("TypeConversionCatalog.Tooling.cs"));
            Assert.True(File.Exists(Path.Combine(outputPath, "TypeConversionCatalog.Tooling.cs")));
        }
        finally
        {
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputPath)!);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
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

    private static Workspace LoadWorkspaceFromContractFiles(string modelPath, string instancePath)
    {
        var model = ModelXmlCodec.LoadFromPath(modelPath);
        var instance = InstanceXmlCodec.LoadFromPath(instancePath, model, sourceShardFileName: string.Empty);
        return new Workspace
        {
            WorkspaceRootPath = "memory",
            MetadataRootPath = "memory/metadata",
            WorkspaceConfig = MetaWorkspaceConfig.CreateDefault(),
            Model = model,
            Instance = instance,
            IsDirty = false,
        };
    }
}
