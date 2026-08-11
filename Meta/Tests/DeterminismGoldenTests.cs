using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Meta.Operations.Domain;
using Meta.Integration;
using Meta.Surfaces.CSharp;
using Meta.Surfaces.Sql;
using Meta.Surfaces.Xml;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class DeterminismGoldenTests
{

    [Fact]
    public async Task XmlCanonicalOutput_IsDeterministic()
    {
        var workspace = LoadCanonicalSampleWorkspace();
        var outputA = Path.Combine(Path.GetTempPath(), "metadata-golden-tests", Guid.NewGuid().ToString("N"), "a");
        var outputB = Path.Combine(Path.GetTempPath(), "metadata-golden-tests", Guid.NewGuid().ToString("N"), "b");

        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(workspace, outputA);
            await XmlWorkspaceWriter.WriteNewAsync(workspace, outputB);

            var manifestA = BuildWorkspaceManifest(outputA);
            var manifestB = BuildWorkspaceManifest(outputB);

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

    private static DirectoryManifest BuildWorkspaceManifest(string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var fileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var workspaceMetaPath = Path.Combine(root, "workspace.meta");
        fileHashes["workspace.meta"] = ComputeFileHash(workspaceMetaPath);

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

    private static InMemoryWorkspace LoadCanonicalSampleWorkspace()
    {
        return LoadWorkspaceFromContractFiles(
            Path.Combine(FindRepositoryRoot(), "Meta", "Tests", "TestData", "SampleModel.xml"),
            Path.Combine(FindRepositoryRoot(), "Meta", "Tests", "TestData", "SampleInstance.xml"));
    }

    private static InMemoryWorkspace LoadWorkspaceFromContractFiles(string modelPath, string instancePath)
    {
        var model = ModelXmlCodec.LoadFromPath(modelPath);
        var instance = InstanceXmlCodec.LoadFromPath(instancePath, model);
        return new InMemoryWorkspace(model, instance);
    }

    private sealed class DirectoryManifest
    {
        public IReadOnlyDictionary<string, string> FileHashes { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string CombinedHash { get; set; } = string.Empty;
    }
}

