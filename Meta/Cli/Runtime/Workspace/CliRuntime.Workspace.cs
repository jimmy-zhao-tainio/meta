internal sealed partial class CliRuntime
{
    string DefaultWorkspacePath()
    {
        return string.IsNullOrWhiteSpace(globalWorkspacePath) ? Environment.CurrentDirectory : globalWorkspacePath;
    }

    bool HasWorkspaceOverrideInInvocation()
    {
        if (!string.IsNullOrWhiteSpace(globalWorkspacePath))
        {
            return true;
        }
    
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
    
        return false;
    }

    async Task<Workspace> LoadWorkspaceForCommandAsync(string workspacePath)
    {
        return await services.WorkspaceService
            .LoadAsync(workspacePath, searchUpward: !HasWorkspaceOverrideInInvocation())
            .ConfigureAwait(false);
    }

    string ResolveCSharpOutputDirectory(string outputPath)
    {
        if (outputPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            var outputFilePath = Path.GetFullPath(outputPath);
            var directory = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return directory;
            }
    
            return Environment.CurrentDirectory;
        }
    
        return Path.GetFullPath(outputPath);
    }

    int ValidateNewWorkspaceTarget(string targetPath)
    {
        var fullPath = Path.GetFullPath(targetPath);
        if (File.Exists(fullPath))
        {
            return PrintFormattedError(
                "E_OPERATION",
                "new workspace target must be a directory path.",
                exitCode: 4,
                where: BuildWhere(("path", fullPath)),
                hints: new[]
                {
                    $"Target path points to a file: {fullPath}",
                    "Next: choose a new folder path, for example: --new-workspace .\\ImportedWorkspace2",
                });
        }
    
        if (!Directory.Exists(fullPath))
        {
            return 0;
        }
    
        var entryCount = Directory.EnumerateFileSystemEntries(fullPath).Count();
        if (entryCount > 0)
        {
            var sampleEntries = Directory.EnumerateFileSystemEntries(fullPath)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            return PrintFormattedError(
                "E_OPERATION",
                "new workspace target directory must be empty.",
                exitCode: 4,
                where: BuildWhere(
                    ("entries", entryCount.ToString(CultureInfo.InvariantCulture)),
                    ("path", fullPath),
                    ("sampleEntries", string.Join("|", sampleEntries))),
                hints: new[]
                {
                    $"Directory contains entries such as: {string.Join(", ", sampleEntries)}",
                    "Next: choose a new folder path, for example: --new-workspace .\\ImportedWorkspace2",
                });
        }
    
        return 0;
    }

    string ResolveWorkspacePathForHints()
    {
        if (!string.IsNullOrWhiteSpace(globalWorkspacePath))
        {
            return globalWorkspacePath;
        }
    
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
    
        return DefaultWorkspacePath();
    }

    string ResolveWorkspaceFileForHint(string workspacePath, string fileRelativePath)
    {
        try
        {
            var context = ResolveWorkspaceFilesystemContext(workspacePath);
            var relative = fileRelativePath.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relative))
            {
                return relative;
            }
    
            return Path.GetFullPath(Path.Combine(context.WorkspaceRootPath, relative));
        }
        catch
        {
            return string.Empty;
        }
    }

    Workspace? TryLoadWorkspaceForHints()
    {
        try
        {
            var path = ResolveWorkspacePathForHints();
            return services.WorkspaceService
                .LoadAsync(path, searchUpward: !HasWorkspaceOverrideInInvocation())
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            return null;
        }
    }

    IReadOnlyList<GenericRecord> GetEntityRows(Workspace? workspace, string entityName)
    {
        if (workspace == null || string.IsNullOrWhiteSpace(entityName))
        {
            return Array.Empty<GenericRecord>();
        }
    
        if (workspace.Instance.RecordsByEntity.TryGetValue(entityName, out var directRows))
        {
            return directRows;
        }
    
        var candidate = workspace.Instance.RecordsByEntity
            .FirstOrDefault(pair => string.Equals(pair.Key, entityName, StringComparison.OrdinalIgnoreCase));
        return candidate.Value == null ? Array.Empty<GenericRecord>() : candidate.Value;
    }
}

