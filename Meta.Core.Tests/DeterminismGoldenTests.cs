using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Meta.Adapters;
using Meta.Core.Domain;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class DeterminismGoldenTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedXmlMetadataHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["instance/Cube.xml"] = "7e1cdd9d9e3ef20bf50dda876e4ce435809019c4d6d3dbbd2f2fc885c3af1858",
            ["instance/Dimension.xml"] = "0465171623420221211b6a4aceb597227fcfb06d79dc489d495994faf9b635d7",
            ["instance/Fact.xml"] = "03482418a41841181a954ab3bd23d93a78f6f7afef652eebf585e4270737de1f",
            ["instance/Measure.xml"] = "9805a5fe77a8952bb1bed8f274e8f37a52ec55b3409c6da6706e39141e69796f",
            ["instance/System.xml"] = "da7a807995ff53dfc337f344f8b7173c011c718f5b64ee880c91554781dfce12",
            ["instance/SystemCube.xml"] = "0d012bd20081ed6ae31d2a38cd8b687ba4381f13cae1a5aabe264c04366b702d",
            ["instance/SystemDimension.xml"] = "c8c495f17a6db14cccf0e000097b73cbdccdb1e0be29e21e057fa6e414831439",
            ["instance/SystemFact.xml"] = "33b7bba7b37768b09b8e3b19122fe29ad063473835ed9155561aa53f5ed5d583",
            ["instance/SystemType.xml"] = "61bd50d754f2a26b860ba877eb5429174ea26e567766dc634b78ed3f5848fb4e",
            ["model.xml"] = "6e473c65afd30cac887e822980f4ba541760da99e87ea4ec9c70c89f75b16c09",
            ["workspace.xml"] = "6035029f2d8f54ac86d296a2cb6458ebdda100e41ed7a6eee46e0540624885f3",
        };

    private const string ExpectedXmlMetadataCombinedHash = "99d54f139556cbce6afec096e0dfb4024dd9ffd06ddfcd28af2d3b2b7652f59e";

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
            ["EnterpriseBIPlatform.cs"] = "62ec8ba6105d0154b1a4a222f6eba837657fdec8a5171f57e719c86b685b65c8",
            ["Fact.cs"] = "d0c027e86e831b5eaf4726cb63e8f87b740a2b2151c857f25562e983870c9369",
            ["Measure.cs"] = "b742056a6fd8da73e94a844d13781c21a01066da8f5e7cfc9eede041a7065825",
            ["System.cs"] = "bee9f521db049ba82a239474f0d3d5eedc65425378423a557e764632acc21680",
            ["SystemCube.cs"] = "97623acb4a0880e323c64fe8e878812d6e7e5da12431a43339f68274ae1c3e7c",
            ["SystemDimension.cs"] = "2052823cadd5914f41be95e5dc663a9922cb13eae90318a6cbde156ae393c7d8",
            ["SystemFact.cs"] = "707388c0154a1e56130b7e13b6d35f51db69a16c74f5e6eef8dc58872b4630e6",
            ["SystemType.cs"] = "98f63c550d044451a04f9322072c15e35915520585242bf0ab22bbe9cbee95bf",
        };

    private const string ExpectedCSharpCombinedHash = "58abdfb7991abfebba8e455c682dc7f9a5c773a4dde5224fd66d06b38f3b6dba";

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
        var (modelPath, instancePath, rootPath) = TestWorkspaceFactory.CreateCanonicalSampleContractFiles();
        return LoadAndCleanupAsync();

        async Task<Workspace> LoadAndCleanupAsync()
        {
            try
            {
                return await services.ImportService.ImportXmlAsync(modelPath, instancePath).ConfigureAwait(false);
            }
            finally
            {
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            }
        }
    }

    private sealed class DirectoryManifest
    {
        public IReadOnlyDictionary<string, string> FileHashes { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string CombinedHash { get; set; } = string.Empty;
    }
}

