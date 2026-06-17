using System;
using System.IO;

namespace Meta.Core.Services;

internal static class WorkspacePathResolver
{
    public static string ResolvePathFromWorkspaceRoot(string workspaceRootPath, string path)
    {
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidDataException(
                $"Workspace config path '{path}' must be relative to the workspace root.");
        }

        var resolvedPath = Path.GetFullPath(Path.Combine(workspaceRootPath, normalized));
        var workspaceRoot = Path.GetFullPath(workspaceRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!IsPathWithinRoot(resolvedPath, workspaceRoot))
        {
            throw new InvalidDataException(
                $"Workspace config path '{path}' resolves outside workspace root '{workspaceRoot}'.");
        }

        return resolvedPath;
    }

    public static void EnsurePathUnderWorkspaceRoot(string path, string workspaceRootPath, string workspaceConfigFieldName)
    {
        if (!IsPathWithinRoot(path, workspaceRootPath))
        {
            throw new InvalidDataException(
                $"Workspace config '{workspaceConfigFieldName}' must resolve under the workspace root. Resolved path '{path}' is outside '{workspaceRootPath}'.");
        }
    }

    private static bool IsPathWithinRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(path, root, comparison))
        {
            return true;
        }

        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSeparator, comparison);
    }
}
