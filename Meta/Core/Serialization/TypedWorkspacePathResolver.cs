using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Meta.Core.Serialization;

internal static class TypedWorkspacePathResolver
{
    private const string WorkspaceXmlFileName = "workspace.xml";
    private const string DefaultModelFileRelativePath = "model.xml";
    private const string DefaultInstanceDirectoryRelativePath = "instances";

    public static string DiscoverWorkspaceRoot(string inputPath)
    {
        var initialDirectory = Directory.Exists(inputPath)
            ? inputPath
            : Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Path.GetFullPath(inputPath);
        var current = Path.GetFullPath(initialDirectory);

        while (!string.IsNullOrWhiteSpace(current))
        {
            if (LooksLikeWorkspaceRoot(current))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        return Path.GetFullPath(initialDirectory);
    }

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

    private static bool LooksLikeWorkspaceRoot(string workspaceRootPath)
    {
        if (File.Exists(Path.Combine(workspaceRootPath, WorkspaceXmlFileName)))
        {
            return true;
        }

        return File.Exists(Path.Combine(workspaceRootPath, Path.GetFileName(DefaultModelFileRelativePath))) ||
               Directory.Exists(Path.Combine(workspaceRootPath, DefaultInstanceDirectoryRelativePath));
    }

    private static string ReadInstanceDirectoryRelativePath(string workspaceRootPath)
    {
        var workspaceXmlPath = Path.Combine(workspaceRootPath, WorkspaceXmlFileName);
        if (!File.Exists(workspaceXmlPath))
        {
            return DefaultInstanceDirectoryRelativePath;
        }

        var document = XDocument.Load(workspaceXmlPath, LoadOptions.None);
        var instanceDirPath = document.Root?
            .Element("WorkspaceLayoutList")?
            .Elements("WorkspaceLayout")
            .Elements("InstanceDirPath")
            .Select(element => element.Value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return string.IsNullOrWhiteSpace(instanceDirPath)
            ? DefaultInstanceDirectoryRelativePath
            : instanceDirPath!;
    }

    private static string ReadModelFileRelativePath(string workspaceRootPath)
    {
        var workspaceXmlPath = Path.Combine(workspaceRootPath, WorkspaceXmlFileName);
        if (!File.Exists(workspaceXmlPath))
        {
            return DefaultModelFileRelativePath;
        }

        var document = XDocument.Load(workspaceXmlPath, LoadOptions.None);
        var modelFilePath = document.Root?
            .Element("WorkspaceLayoutList")?
            .Elements("WorkspaceLayout")
            .Elements("ModelFilePath")
            .Select(element => element.Value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return string.IsNullOrWhiteSpace(modelFilePath)
            ? DefaultModelFileRelativePath
            : modelFilePath!;
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
