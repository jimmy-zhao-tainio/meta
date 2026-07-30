using System.Security.Cryptography;
using System.Text;
using Meta.Core.Services;

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
        return CaptureSnapshot(workspacePath).Fingerprint;
    }

    public static CSharpMetaWorkspaceSnapshot CaptureSnapshot(
        string workspacePath)
    {
        var root = Path.GetFullPath(workspacePath);
        var paths = Directory.GetFiles(
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

        var files = new List<CSharpMetaWorkspaceFile>(paths.Length);
        using var hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        foreach (var path in paths)
        {
            var relativePath = Path.GetRelativePath(root, path)
                .Replace('\\', '/');
            var contents = File.ReadAllBytes(path);
            Append(hash, Encoding.UTF8.GetBytes(relativePath));
            Append(hash, contents);
            files.Add(
                new CSharpMetaWorkspaceFile(
                    path,
                    contents));
        }

        return new CSharpMetaWorkspaceSnapshot(
            files,
            Convert.ToHexString(hash.GetHashAndReset())
                .ToLowerInvariant());
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
        string workspacePath,
        string expectedFingerprint)
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
        try
        {
            Directory.Move(root, backup);
        }
        catch (DirectoryNotFoundException)
        {
            throw new WorkspaceConflictException(
                $"C# workspace '{root}' disappeared while its commit was being prepared.",
                expectedFingerprint,
                "<missing>");
        }
        catch (IOException) when (!Directory.Exists(root))
        {
            throw new WorkspaceConflictException(
                $"C# workspace '{root}' was replaced while its commit was being prepared.",
                expectedFingerprint,
                File.Exists(root)
                    ? "<replaced by file>"
                    : "<missing>");
        }

        string actualFingerprint;
        try
        {
            actualFingerprint = ComputeFingerprint(backup);
        }
        catch
        {
            TryRestoreBackup(backup, root);
            throw;
        }

        if (!string.Equals(
                actualFingerprint,
                expectedFingerprint,
                StringComparison.Ordinal))
        {
            var restored = TryRestoreBackup(backup, root);
            throw new WorkspaceConflictException(
                $"C# workspace '{root}' changed while its commit was being prepared." +
                (restored
                    ? string.Empty
                    : $" The prior workspace remains at '{backup}'."),
                expectedFingerprint,
                actualFingerprint);
        }

        try
        {
            Directory.Move(stage, root);
        }
        catch
        {
            if (Directory.Exists(root))
            {
                throw new WorkspaceConflictException(
                    $"C# workspace '{root}' was recreated while its commit was being published. The prior workspace remains at '{backup}'.",
                    expectedFingerprint,
                    "<workspace path recreated>");
            }

            TryRestoreBackup(backup, root);
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

    private static bool TryRestoreBackup(
        string backupPath,
        string workspacePath)
    {
        if (Directory.Exists(workspacePath) ||
            !Directory.Exists(backupPath))
        {
            return false;
        }

        try
        {
            Directory.Move(backupPath, workspacePath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
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

internal sealed record CSharpMetaWorkspaceSnapshot(
    IReadOnlyList<CSharpMetaWorkspaceFile> Files,
    string Fingerprint);

internal sealed record CSharpMetaWorkspaceFile(
    string FullPath,
    byte[] Contents);
