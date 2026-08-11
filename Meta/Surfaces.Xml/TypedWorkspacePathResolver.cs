using System;
using System.IO;
using Meta.Surfaces.Configuration;
using Meta.Surfaces;

namespace Meta.Surfaces.Xml;

internal static class TypedWorkspacePathResolver
{
    private const string DefaultInstanceDirectoryRelativePath = "instances";

    public static string ResolveWorkspaceRootFromPath(string inputPath)
    {
        var fullPath = Path.GetFullPath(inputPath);
        if (File.Exists(fullPath))
        {
            return Path.GetDirectoryName(fullPath) ?? fullPath;
        }

        if (string.Equals(
                Path.GetFileName(fullPath),
                DefaultInstanceDirectoryRelativePath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return Directory.GetParent(fullPath)?.FullName ?? fullPath;
        }

        return fullPath;
    }

    public static string ResolveInstanceDirectoryPath(string workspaceRootPath)
    {
        var relativePath = ReadInstanceDirectoryRelativePath(workspaceRootPath);
        var normalizedRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var instanceDirectoryPath = Path.GetFullPath(Path.Combine(workspaceRootPath, normalizedRelativePath));
        EnsurePathUnderWorkspaceRoot(workspaceRootPath, instanceDirectoryPath, "InstanceDirPath");
        return instanceDirectoryPath;
    }

    public static string ResolveModelFilePath(string workspaceRootPath)
    {
        var relativePath = ReadModelFileRelativePath(workspaceRootPath);
        var normalizedRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var modelFilePath = Path.GetFullPath(Path.Combine(workspaceRootPath, normalizedRelativePath));
        EnsurePathUnderWorkspaceRoot(workspaceRootPath, modelFilePath, "ModelFilePath");
        return modelFilePath;
    }

    private static string ReadInstanceDirectoryRelativePath(string workspaceRootPath)
    {
        var metadata = WorkspaceMetaFile.Read(workspaceRootPath);
        EnsureXmlSurface(metadata, workspaceRootPath);
        return MetaWorkspace.GetInstanceDir(metadata.Configuration);
    }

    private static string ReadModelFileRelativePath(string workspaceRootPath)
    {
        var metadata = WorkspaceMetaFile.Read(workspaceRootPath);
        EnsureXmlSurface(metadata, workspaceRootPath);
        return MetaWorkspace.GetModelFile(metadata.Configuration);
    }

    private static void EnsureXmlSurface(
        WorkspaceMetaDocument metadata,
        string workspaceRootPath)
    {
        if (!string.Equals(metadata.Representation, "xml", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Workspace '{workspaceRootPath}' selects '{metadata.Representation}', not the XML surface.");
        }
    }

    private static void EnsurePathUnderWorkspaceRoot(string workspaceRootPath, string path, string memberName)
    {
        var absoluteRootPath = Path.GetFullPath(workspaceRootPath);
        var absolutePath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootWithSeparator = absoluteRootPath.EndsWith(Path.DirectorySeparatorChar)
            ? absoluteRootPath
            : absoluteRootPath + Path.DirectorySeparatorChar;
        if (!absolutePath.StartsWith(rootWithSeparator, comparison) &&
            !string.Equals(absolutePath, absoluteRootPath, comparison))
        {
            throw new InvalidOperationException(
                $"Workspace {memberName} '{absolutePath}' must stay under workspace root '{absoluteRootPath}'.");
        }
    }
}
