using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Meta.Core.Services;

internal static class GenerationOutputWriter
{
    public static string PrepareDirectory(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            File.Delete(file);
        }

        foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            Directory.Delete(dir, recursive: false);
        }

        return root;
    }

    public static void WriteText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static GenerationManifest BuildManifest(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Root directory is required.", nameof(rootDirectory));
        }

        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Directory '{root}' was not found.");
        }

        var manifest = new GenerationManifest
        {
            RootPath = root,
        };

        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var path in files)
        {
            var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
            var hash = ComputeFileHash(path);
            manifest.FileHashes[relativePath] = hash;
        }

        manifest.CombinedHash = ComputeCombinedHash(manifest.FileHashes);
        return manifest;
    }

    private static string ComputeFileHash(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeCombinedHash(IReadOnlyDictionary<string, string> fileHashes)
    {
        var payload = string.Join(
            "\n",
            fileHashes
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"{item.Key}:{item.Value}"));
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
