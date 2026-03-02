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
    private static readonly IReadOnlyDictionary<string, string> ExpectedXmlMetadataHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["instance/Cube.xml"] = "38e2870cc216605b3864a8937d2db7f203cf8efdfa244f1cc69f92edb8a64dc4",
            ["instance/Dimension.xml"] = "1d57f85733c3c88804a9709bfbb9ee4db50fb786022a5c9aa532646d55539e75",
            ["instance/Fact.xml"] = "71274cc995b3a9205e068c64c3a24780c03e0ae3cd29bae53af3fc41fbbc8aeb",
            ["instance/Measure.xml"] = "d5a07951a904f842a85cf9b582b07bbba29c3a030f46bf2d7b6160a50208406b",
            ["instance/System.xml"] = "68e51da68ae30019c92eaf03e1faac85600198b5e1b01eb5b8651830c535ec83",
            ["instance/SystemCube.xml"] = "c99e0e66d48be557b784a872db25d44cfb9097b22b278c21af0c291ab346685f",
            ["instance/SystemDimension.xml"] = "c6be74169de98a90be91cb9f111eef2781908878b3615765e93cba597a6ac63b",
            ["instance/SystemFact.xml"] = "6791fd3221d121de46492e1a8bd6431c8dc833c11b3efb3141600231f49413ff",
            ["instance/SystemType.xml"] = "fdb6db2b2b03c595fcd682803aa09ca11e8d21d752551e797c75a999a9f40f2d",
            ["model.xml"] = "36ae3183a2ef6d9a045ead05c7a0f21751d74facd0dd9c8333bf99cc7fc3e153",
            ["workspace.xml"] = "53b13bbb57febb1ba3082fd0cd712581f5bb57832f0ad1d889c717ff08ee978c",
        };

    private const string ExpectedXmlMetadataCombinedHash = "600c40b8d52bcd146f216e0ae55adb6e524c90ea74c4eeaa6aab8c2b50d796aa";

    private static readonly IReadOnlyDictionary<string, string> ExpectedSqlHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["data.sql"] = "810c919903112a9e1caaa9bd1c77f1fde915e41430261303615cd81fc272765a",
            ["schema.sql"] = "e7d5206e17a433c3621e829ee6736de8ea76bab015674eb335b4ef25867a0974",
        };

    private const string ExpectedSqlCombinedHash = "cc068c77d683d5c9461bcda2138e236c5da72fad35d6a3d414fe5b0601e07d22";

    private static readonly IReadOnlyDictionary<string, string> ExpectedCSharpHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Cube.cs"] = "104a7e96a4a39449b8c36910b4b6f797294dd59cc68048444a7c621bd77002dc",
            ["Dimension.cs"] = "ed7690d13eeb3b04d382dabca4cce880252cb3fce28aac211e7a0059be49428a",
            ["EnterpriseBIPlatform.cs"] = "00772ffd2d046bd2fc0f3f6fe2772a70247c28920aae6c4d40398f48fe6dc0f5",
            ["Fact.cs"] = "d0c027e86e831b5eaf4726cb63e8f87b740a2b2151c857f25562e983870c9369",
            ["Measure.cs"] = "b742056a6fd8da73e94a844d13781c21a01066da8f5e7cfc9eede041a7065825",
            ["System.cs"] = "bee9f521db049ba82a239474f0d3d5eedc65425378423a557e764632acc21680",
            ["SystemCube.cs"] = "97623acb4a0880e323c64fe8e878812d6e7e5da12431a43339f68274ae1c3e7c",
            ["SystemDimension.cs"] = "2052823cadd5914f41be95e5dc663a9922cb13eae90318a6cbde156ae393c7d8",
            ["SystemFact.cs"] = "707388c0154a1e56130b7e13b6d35f51db69a16c74f5e6eef8dc58872b4630e6",
            ["SystemType.cs"] = "98f63c550d044451a04f9322072c15e35915520585242bf0ab22bbe9cbee95bf",
        };

    private const string ExpectedCSharpCombinedHash = "d26efbddfa1683a18b3ccd10538e4b09d6e3b21522913793e42270f8910f162e";

    [Fact]
    public async Task XmlCanonicalOutput_MatchesGoldenHashes()
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
            AssertManifestEqual(ExpectedXmlMetadataHashes, manifestA.FileHashes);
            Assert.Equal(ExpectedXmlMetadataCombinedHash, manifestA.CombinedHash);
        }
        finally
        {
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputA)!);
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputB)!);
        }
    }

    [Fact]
    public async Task SqlGeneration_MatchesGoldenHashes()
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
            AssertManifestEqual(ExpectedSqlHashes, manifestA.FileHashes);
            Assert.Equal(ExpectedSqlCombinedHash, manifestA.CombinedHash);
        }
        finally
        {
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputA)!);
            DeleteDirectoryIfExists(Path.GetDirectoryName(outputB)!);
        }
    }

    [Fact]
    public async Task CSharpGeneration_MatchesGoldenHashes()
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
            AssertManifestEqual(ExpectedCSharpHashes, manifestA.FileHashes);
            Assert.Equal(ExpectedCSharpCombinedHash, manifestA.CombinedHash);
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

        var metadataRoot = Path.Combine(root, "metadata");
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
            Path.Combine(FindRepositoryRoot(), "Meta.Core.Tests", "TestData", "SampleModel.xml"),
            Path.Combine(FindRepositoryRoot(), "Meta.Core.Tests", "TestData", "SampleInstance.xml")));
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

    private sealed class DirectoryManifest
    {
        public IReadOnlyDictionary<string, string> FileHashes { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string CombinedHash { get; set; } = string.Empty;
    }
}

