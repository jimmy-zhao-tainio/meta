using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Meta.Adapters;
using Meta.Core.Domain;
using Meta.Core.Serialization;
using Meta.Core.Services;
using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace Meta.Core.Tests;

public sealed class DeterminismGoldenTests
{

    [Fact]
    public async Task XmlCanonicalOutput_IsDeterministic()
    {
        var services = new ServiceCollection();
        var workspace = await LoadCanonicalSampleWorkspaceAsync(services);
        var outputA = Path.Combine(Path.GetTempPath(), "metadata-golden-tests", Guid.NewGuid().ToString("N"), "a");
        var outputB = Path.Combine(Path.GetTempPath(), "metadata-golden-tests", Guid.NewGuid().ToString("N"), "b");

        try
        {
            await services.ExportService.ExportXmlAsync(workspace, outputA);
            await services.ExportService.ExportXmlAsync(workspace, outputB);

            var manifestA = BuildWorkspaceXmlManifest(outputA);
            var manifestB = BuildWorkspaceXmlManifest(outputB);

            AssertManifestEqual(manifestA, manifestB);
            Assert.NotEmpty(manifestA.FileHashes);
        }
        finally
        {
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputA)!);
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputB)!);
        }
    }

    [Fact]
    public async Task SqlGeneration_IsDeterministic()
    {
        var services = new ServiceCollection();
        var workspace = await LoadCanonicalSampleWorkspaceAsync(services);
        var outputA = Path.Combine(Path.GetTempPath(), "metadata-golden-tests", Guid.NewGuid().ToString("N"), "sql-a");
        var outputB = Path.Combine(Path.GetTempPath(), "metadata-golden-tests", Guid.NewGuid().ToString("N"), "sql-b");

        try
        {
            GenerationService.GenerateSql(workspace, outputA);
            GenerationService.GenerateSql(workspace, outputB);

            var manifestA = BuildDirectoryManifest(outputA);
            var manifestB = BuildDirectoryManifest(outputB);

            AssertManifestEqual(manifestA, manifestB);
            Assert.NotEmpty(manifestA.FileHashes);
        }
        finally
        {
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputA)!);
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputB)!);
        }
    }

    [Fact]
    public async Task CSharpGeneration_IsDeterministic()
    {
        var services = new ServiceCollection();
        var workspace = await LoadCanonicalSampleWorkspaceAsync(services);
        var outputA = Path.Combine(Path.GetTempPath(), "metadata-golden-tests", Guid.NewGuid().ToString("N"), "cs-a");
        var outputB = Path.Combine(Path.GetTempPath(), "metadata-golden-tests", Guid.NewGuid().ToString("N"), "cs-b");

        try
        {
            GenerationService.GenerateCSharp(workspace, outputA);
            GenerationService.GenerateCSharp(workspace, outputB);

            var manifestA = BuildDirectoryManifest(outputA);
            var manifestB = BuildDirectoryManifest(outputB);

            AssertManifestEqual(manifestA, manifestB);
            Assert.NotEmpty(manifestA.FileHashes);
        }
        finally
        {
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputA)!);
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputB)!);
        }
    }

    private static void AssertManifestEqual(DirectoryManifest expected, DirectoryManifest actual)
    {
        AssertManifestEqual(expected.FileHashes, actual.FileHashes);
        Assert.Equal(expected.CombinedHash, actual.CombinedHash);
    }

    private static void AssertManifestEqual(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var item in expected.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            Assert.True(actual.TryGetValue(item.Key, out var actualHash), $"Missing output file '{item.Key}'.");
            Assert.Equal(item.Value, actualHash);
        }
    }

    private static DirectoryManifest BuildDirectoryManifest(string rootPath)
    {
        var root = Path.GetFullPath(rootPath);
        var fileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = Path.GetRelativePath(root, filePath).Replace('\\', '/');
            fileHashes[relativePath] = ComputeFileHash(filePath);
        }

        return new DirectoryManifest
        {
            FileHashes = fileHashes,
            CombinedHash = ComputeCombinedHash(fileHashes),
        };
    }

    private static DirectoryManifest BuildWorkspaceXmlManifest(string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var fileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var workspaceXmlPath = Path.Combine(root, "workspace.xml");
        fileHashes["workspace.xml"] = ComputeFileHash(workspaceXmlPath);

        var metadataRoot = root;
        foreach (var filePath in Directory.GetFiles(metadataRoot, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = Path.GetRelativePath(metadataRoot, filePath).Replace('\\', '/');
            fileHashes[relativePath] = ComputeFileHash(filePath);
        }

        return new DirectoryManifest
        {
            FileHashes = fileHashes,
            CombinedHash = ComputeCombinedHash(fileHashes),
        };
    }

    private static string ComputeFileHash(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string ComputeCombinedHash(IReadOnlyDictionary<string, string> fileHashes)
    {
        var payload = string.Join(
            "\n",
            fileHashes
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"{item.Key}:{item.Value}"));
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
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

    private static Task<Workspace> LoadCanonicalSampleWorkspaceAsync(ServiceCollection services)
    {
        _ = services;
        return Task.FromResult(LoadWorkspaceFromContractFiles(
            Path.Combine(FindRepositoryRoot(), "Meta", "Tests", "TestData", "SampleModel.xml"),
            Path.Combine(FindRepositoryRoot(), "Meta", "Tests", "TestData", "SampleInstance.xml")));
    }

    private static Workspace LoadWorkspaceFromContractFiles(string modelPath, string instancePath)
    {
        var model = ModelXmlCodec.LoadFromPath(modelPath);
        var instance = InstanceXmlCodec.LoadFromPath(instancePath, model, sourceShardFileName: string.Empty);
        return new Workspace
        {
            WorkspaceRootPath = "memory",
            MetadataRootPath = "memory",
            WorkspaceConfig = MetaWorkspaceConfig.CreateDefault(),
            Model = model,
            Instance = instance,
            IsDirty = false,
        };
    }

    private sealed class DirectoryManifest
    {
        public IReadOnlyDictionary<string, string> FileHashes { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string CombinedHash { get; set; } = string.Empty;
    }
}

