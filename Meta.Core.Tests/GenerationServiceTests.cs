using System;
using System.IO;
using Meta.Adapters;
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

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

