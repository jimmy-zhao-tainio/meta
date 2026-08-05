internal sealed partial class CliRuntime
{
    string DefaultWorkspacePath()
    {
        return string.IsNullOrWhiteSpace(globalWorkspacePath) ? Environment.CurrentDirectory : globalWorkspacePath;
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

    string ResolveWorkspacePathForHints()
    {
        if (!string.IsNullOrWhiteSpace(globalWorkspacePath))
        {
            return globalWorkspacePath;
        }

        if (currentInvocation is not null)
        {
            return OptionalValue("workspace", DefaultWorkspacePath());
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

    IReadOnlyList<GenericRecord> GetEntityRows(InMemoryWorkspace? workspace, string entityName)
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

