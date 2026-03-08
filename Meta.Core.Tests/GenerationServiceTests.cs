using System;
using System.IO;
using Meta.Adapters;
using Meta.Core.Domain;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class GenerationServiceTests
{
    [Fact]
    public async Task GenerateSql_IsDeterministicAcrossRuns()
    {
        var services = new ServiceCollection();
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync(services);
        var outputA = Path.Combine(Path.GetTempPath(), "metadata-gen-tests", Guid.NewGuid().ToString("N"), "a");
        var outputB = Path.Combine(Path.GetTempPath(), "metadata-gen-tests", Guid.NewGuid().ToString("N"), "b");

        try
        {
            var manifestA = GenerationService.GenerateSql(workspace, outputA);
            var manifestB = GenerationService.GenerateSql(workspace, outputB);

            Assert.True(GenerationService.AreEquivalent(manifestA, manifestB, out var message), message);
            Assert.True(File.Exists(Path.Combine(outputA, "schema.sql")));
            Assert.True(File.Exists(Path.Combine(outputA, "data.sql")));
            var schemaText = await File.ReadAllTextAsync(Path.Combine(outputA, "schema.sql"));
            Assert.Contains("NVARCHAR(MAX)", schemaText, StringComparison.Ordinal);
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputA)!);
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputB)!);
        }
    }

    [Fact]
    public async Task GenerateSsdt_WritesExpectedFiles()
    {
        var services = new ServiceCollection();
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync(services);
        var output = Path.Combine(Path.GetTempPath(), "metadata-gen-tests", Guid.NewGuid().ToString("N"), "ssdt");

        try
        {
            var manifest = GenerationService.GenerateSsdt(workspace, output);

            Assert.True(File.Exists(Path.Combine(output, "Schema.sql")));
            Assert.True(File.Exists(Path.Combine(output, "Data.sql")));
            Assert.True(File.Exists(Path.Combine(output, "PostDeploy.sql")));
            Assert.True(File.Exists(Path.Combine(output, "Metadata.sqlproj")));
            Assert.Equal(4, manifest.FileHashes.Count);
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            DeleteDirectoryIfExists(Path.GetDirectoryName(output)!);
        }
    }

    [Fact]
    public async Task GenerateCSharp_IsDeterministicAcrossRuns()
    {
        var services = new ServiceCollection();
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync(services);
        var outputA = Path.Combine(Path.GetTempPath(), "metadata-gen-tests", Guid.NewGuid().ToString("N"), "a");
        var outputB = Path.Combine(Path.GetTempPath(), "metadata-gen-tests", Guid.NewGuid().ToString("N"), "b");

        try
        {
            var manifestA = GenerationService.GenerateCSharp(workspace, outputA);
            var manifestB = GenerationService.GenerateCSharp(workspace, outputB);

            Assert.True(GenerationService.AreEquivalent(manifestA, manifestB, out var message), message);
            Assert.True(File.Exists(Path.Combine(outputA, workspace.Model.Name + ".cs")));
            Assert.True(File.Exists(Path.Combine(outputA, "Cube.cs")));
            var modelText = await File.ReadAllTextAsync(Path.Combine(outputA, workspace.Model.Name + ".cs"));
            Assert.Contains($"public static class {workspace.Model.Name}", modelText, StringComparison.Ordinal);
            Assert.Contains("public static IReadOnlyList<Measure> MeasureList", modelText, StringComparison.Ordinal);
            Assert.Contains("MeasureName = \"number_of_things\"", modelText, StringComparison.Ordinal);
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputA)!);
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputB)!);
        }
    }

    [Fact]
    public async Task GenerateCSharp_WithTooling_EmitsToolingFile()
    {
        var services = new ServiceCollection();
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync(services);
        var output = Path.Combine(Path.GetTempPath(), "metadata-gen-tests", Guid.NewGuid().ToString("N"), "tooling");

        try
        {
            var manifest = GenerationService.GenerateCSharp(workspace, output, includeTooling: true);
            var toolingFile = workspace.Model.Name + ".Tooling.cs";
            var toolingPath = Path.Combine(output, toolingFile);

            Assert.True(File.Exists(toolingPath));
            Assert.True(manifest.FileHashes.ContainsKey(toolingFile));
            Assert.Contains($"public static class {workspace.Model.Name}Tooling", File.ReadAllText(toolingPath), StringComparison.Ordinal);
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            DeleteDirectoryIfExists(Path.GetDirectoryName(output)!);
        }
    }

    [Fact]
    public void GenerateCSharp_ModelAndEntityNameCollision_UsesModelSuffixForFacade()
    {
        var workspace = new Workspace
        {
            Model =
            {
                Name = "Architecture",
                Entities =
                {
                    new GenericEntity
                    {
                        Name = "Architecture",
                        Properties =
                        {
                            new GenericProperty { Name = "Name" },
                        },
                    },
                },
            },
            Instance =
            {
                ModelName = "Architecture",
            },
        };
        workspace.Instance.GetOrCreateEntityRecords("Architecture").Add(new GenericRecord
        {
            Id = "1",
            Values =
            {
                ["Name"] = "FrameworkArchitecture",
            },
        });

        var output = Path.Combine(Path.GetTempPath(), "metadata-gen-tests", Guid.NewGuid().ToString("N"), "collision");

        try
        {
            var manifest = GenerationService.GenerateCSharp(workspace, output, includeTooling: true);
            var modelPath = Path.Combine(output, "ArchitectureModel.cs");
            var toolingPath = Path.Combine(output, "ArchitectureModel.Tooling.cs");
            var entityPath = Path.Combine(output, "Architecture.cs");

            Assert.True(File.Exists(modelPath));
            Assert.True(File.Exists(toolingPath));
            Assert.True(File.Exists(entityPath));
            Assert.True(manifest.FileHashes.ContainsKey("ArchitectureModel.cs"));
            Assert.True(manifest.FileHashes.ContainsKey("ArchitectureModel.Tooling.cs"));
            Assert.True(manifest.FileHashes.ContainsKey("Architecture.cs"));

            var modelCode = File.ReadAllText(modelPath);
            var toolingCode = File.ReadAllText(toolingPath);
            var entityCode = File.ReadAllText(entityPath);

            Assert.Contains("namespace Architecture", modelCode, StringComparison.Ordinal);
            Assert.Contains("public static class ArchitectureModel", modelCode, StringComparison.Ordinal);
            Assert.Contains("private static readonly ArchitectureModelInstance _builtIn", modelCode, StringComparison.Ordinal);
            Assert.Contains("public static IReadOnlyList<Architecture> ArchitectureList", modelCode, StringComparison.Ordinal);
            Assert.Contains("public static class ArchitectureModelTooling", toolingCode, StringComparison.Ordinal);
            Assert.Contains("namespace Architecture", entityCode, StringComparison.Ordinal);
            Assert.Contains("public sealed class Architecture", entityCode, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(Path.GetDirectoryName(output)!);
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

