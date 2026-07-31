using System.Security.Cryptography;
using System.Text;

namespace Meta.Adapters;

internal static class CSharpMetaWorkspaceFiles
{
    private const string WorkspaceMarker = "// <meta-workspace>";

    public static string RequireOwnedWorkspace(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new ArgumentException(
                "C# workspace path is required.",
                nameof(workspacePath));
        }

        var root = Path.GetFullPath(workspacePath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"C# workspace '{root}' was not found.");
        }

        var files = Directory.GetFiles(
            root,
            "*",
            SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            throw new InvalidDataException(
                $"C# workspace '{root}' is empty.");
        }

        var unsupported = files.FirstOrDefault(path =>
            !string.Equals(
                Path.GetExtension(path),
                ".cs",
                StringComparison.OrdinalIgnoreCase));
        if (unsupported != null)
        {
            throw new InvalidDataException(
                $"C# operation sessions require an owned source directory containing only C# workspace files. Unsupported file: {Path.GetRelativePath(root, unsupported)}.");
        }

        var unmarked = files.FirstOrDefault(path =>
            !File.ReadLines(path)
                .Take(5)
                .Any(line => string.Equals(
                    line,
                    WorkspaceMarker,
                    StringComparison.Ordinal)));
        if (unmarked != null)
        {
            throw new InvalidDataException(
                $"C# operation sessions cannot rewrite unowned source file '{Path.GetRelativePath(root, unmarked)}'.");
        }

        return root;
    }

    public static string ComputeFingerprint(string workspacePath)
    {
        var root = Path.GetFullPath(workspacePath);
        var files = Directory.GetFiles(
                root,
                "*",
                SearchOption.AllDirectories)
            .OrderBy(
                path => Path.GetRelativePath(root, path),
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                path => Path.GetRelativePath(root, path),
                StringComparer.Ordinal)
            .ToArray();

        using var hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        foreach (var path in files)
        {
            var relativePath = Path.GetRelativePath(root, path)
                .Replace('\\', '/');
            Append(hash, Encoding.UTF8.GetBytes(relativePath));
            Append(hash, File.ReadAllBytes(path));
        }

        return Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant();
    }

    public static string CreateSiblingPath(
        string workspacePath,
        string purpose)
    {
        var root = Path.GetFullPath(workspacePath);
        var parent = Directory.GetParent(root)?.FullName
                     ?? throw new InvalidOperationException(
                         $"C# workspace '{root}' has no parent directory.");
        var leafName = Path.GetFileName(root);
        return Path.Combine(
            parent,
            $".{leafName}.meta-{purpose}-{Guid.NewGuid():N}");
    }

    public static void PublishDirectory(
        string stagedPath,
        string workspacePath)
    {
        var stage = Path.GetFullPath(stagedPath);
        var root = Path.GetFullPath(workspacePath);
        var stageParent = Directory.GetParent(stage)?.FullName;
        var rootParent = Directory.GetParent(root)?.FullName;
        if (stageParent == null ||
            rootParent == null ||
            !string.Equals(
                stageParent,
                rootParent,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "C# workspace staging and publication paths must be siblings.");
        }

        var backup = CreateSiblingPath(root, "backup");
        Directory.Move(root, backup);
        try
        {
            Directory.Move(stage, root);
        }
        catch
        {
            if (!Directory.Exists(root) &&
                Directory.Exists(backup))
            {
                Directory.Move(backup, root);
            }

            throw;
        }

        try
        {
            Directory.Delete(backup, recursive: true);
        }
        catch (IOException)
        {
            // Publication has completed. A retained backup is safer than
            // reporting a failed commit after the new workspace is visible.
        }
        catch (UnauthorizedAccessException)
        {
            // Publication has completed. A retained backup is safer than
            // reporting a failed commit after the new workspace is visible.
        }
    }

    private static void Append(
        IncrementalHash hash,
        ReadOnlySpan<byte> value)
    {
        hash.AppendData(BitConverter.GetBytes(value.Length));
        hash.AppendData(value);
    }
}
