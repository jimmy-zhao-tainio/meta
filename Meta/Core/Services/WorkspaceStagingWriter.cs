using System;
using System.IO;

namespace Meta.Core.Services;

internal static class WorkspaceStagingWriter
{
    public static void SaveByStagingConfiguredPaths(
        string workspaceRoot,
        string modelPath,
        string instanceDirectoryPath,
        Action<string> writeModel,
        Action<string> writeInstances)
    {
        var stagingRootPath = Path.Combine(
            workspaceRoot,
            ".__workspace-staging." + Guid.NewGuid().ToString("N"));
        var backupRootPath = Path.Combine(
            workspaceRoot,
            ".__workspace-backup." + Guid.NewGuid().ToString("N"));
        var stagedModelPath = MapPathToStagingRoot(workspaceRoot, stagingRootPath, modelPath);
        var stagedInstanceDirectoryPath = MapPathToStagingRoot(workspaceRoot, stagingRootPath, instanceDirectoryPath);
        var backupModelPath = MapPathToStagingRoot(workspaceRoot, backupRootPath, modelPath);
        var backupInstanceDirectoryPath = MapPathToStagingRoot(workspaceRoot, backupRootPath, instanceDirectoryPath);

        Directory.CreateDirectory(stagingRootPath);
        try
        {
            writeModel(stagedModelPath);
            writeInstances(stagedInstanceDirectoryPath);
            ReplaceFileFromStaging(modelPath, stagedModelPath, backupModelPath);
            ReplaceDirectoryFromStaging(instanceDirectoryPath, stagedInstanceDirectoryPath, backupInstanceDirectoryPath);
        }
        catch
        {
            RestoreFileFromBackup(modelPath, backupModelPath);
            RestoreDirectoryFromBackup(instanceDirectoryPath, backupInstanceDirectoryPath);
            throw;
        }
        finally
        {
            DeleteDirectoryIfExists(stagingRootPath);
            DeleteDirectoryIfExists(backupRootPath);
        }
    }

    private static string MapPathToStagingRoot(
        string rootPath,
        string stagingRootPath,
        string resolvedFinalPath)
    {
        var relative = Path.GetRelativePath(rootPath, resolvedFinalPath);
        return Path.GetFullPath(Path.Combine(stagingRootPath, relative));
    }

    private static void ReplaceFileFromStaging(string finalPath, string stagedPath, string backupPath)
    {
        DeleteDirectoryIfExists(finalPath);
        DeleteIfExists(backupPath);

        if (File.Exists(finalPath))
        {
            var backupDirectory = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrWhiteSpace(backupDirectory))
            {
                Directory.CreateDirectory(backupDirectory);
            }

            File.Move(finalPath, backupPath);
        }

        if (!File.Exists(stagedPath))
        {
            DeleteIfExists(finalPath);
            return;
        }

        var finalDirectory = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrWhiteSpace(finalDirectory))
        {
            Directory.CreateDirectory(finalDirectory);
        }

        File.Move(stagedPath, finalPath);
    }

    private static void ReplaceDirectoryFromStaging(string finalPath, string stagedPath, string backupPath)
    {
        DeleteIfExists(finalPath);
        DeleteDirectoryIfExists(backupPath);

        if (Directory.Exists(finalPath))
        {
            var backupDirectory = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrWhiteSpace(backupDirectory))
            {
                Directory.CreateDirectory(backupDirectory);
            }

            Directory.Move(finalPath, backupPath);
        }
        else if (File.Exists(finalPath))
        {
            File.Delete(finalPath);
        }

        if (!Directory.Exists(stagedPath))
        {
            DeleteDirectoryIfExists(finalPath);
            return;
        }

        var finalDirectory = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrWhiteSpace(finalDirectory))
        {
            Directory.CreateDirectory(finalDirectory);
        }

        Directory.Move(stagedPath, finalPath);
    }

    private static void RestoreFileFromBackup(string finalPath, string backupPath)
    {
        if (File.Exists(finalPath))
        {
            File.Delete(finalPath);
        }

        if (!File.Exists(backupPath))
        {
            return;
        }

        var finalDirectory = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrWhiteSpace(finalDirectory))
        {
            Directory.CreateDirectory(finalDirectory);
        }

        File.Move(backupPath, finalPath);
    }

    private static void RestoreDirectoryFromBackup(string finalPath, string backupPath)
    {
        if (Directory.Exists(finalPath))
        {
            Directory.Delete(finalPath, recursive: true);
        }

        if (!Directory.Exists(backupPath))
        {
            return;
        }

        var finalDirectory = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrWhiteSpace(finalDirectory))
        {
            Directory.CreateDirectory(finalDirectory);
        }

        Directory.Move(backupPath, finalPath);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
