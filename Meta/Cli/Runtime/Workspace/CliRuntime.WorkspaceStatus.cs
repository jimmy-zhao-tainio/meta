using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

internal sealed partial class CliRuntime
{
    void PrintWorkspaceSummary(Workspace workspace)
    {
        var entityCount = workspace.Model.Entities.Count;
        var rowCount = workspace.Instance.RecordsByEntity.Values.Sum(records => records.Count);
        var dataSizes = CalculateWorkspaceDataSizes(workspace);
        presenter.WriteInfo("Status: ok");
        presenter.WriteKeyValueBlock(
            "Workspace",
            new[]
            {
                ("Path", workspace.WorkspaceRootPath),
                ("Metadata", workspace.MetadataRootPath),
            });
        presenter.WriteKeyValueBlock(
            "Model",
            new[]
            {
                ("Name", workspace.Model.Name),
                ("Entities", entityCount.ToString(CultureInfo.InvariantCulture)),
                ("Rows", rowCount.ToString(CultureInfo.InvariantCulture)),
            });
        presenter.WriteKeyValueBlock(
            "Data",
            new[]
            {
                ("Model", FormatByteSizeWithBytes(dataSizes.ModelBytes)),
                ("Instance", FormatByteSizeWithBytes(dataSizes.InstanceBytes)),
            });
        presenter.WriteKeyValueBlock(
            "Contract",
            new[]
            {
                ("Version", MetaWorkspaceConfig.GetContractVersion(workspace.WorkspaceConfig)),
            });
    }

    (long ModelBytes, long InstanceBytes) CalculateWorkspaceDataSizes(Workspace workspace)
    {
        var modelPath = ResolveFirstExistingPath(new[]
        {
            ResolveWorkspaceConfigPathFromWorkspaceRoot(
                workspace,
                MetaWorkspaceConfig.GetModelFile(workspace.WorkspaceConfig),
                "model.xml"),
            Path.Combine(workspace.MetadataRootPath, "model.xml"),
            Path.Combine(workspace.WorkspaceRootPath, "model.xml"),
        });

        var modelBytes = GetFileSize(modelPath);

        var instanceBytes = 0L;
        var shardDirectory = ResolveWorkspaceConfigPathFromWorkspaceRoot(
            workspace,
            MetaWorkspaceConfig.GetInstanceDir(workspace.WorkspaceConfig),
            "instances");
        if (Directory.Exists(shardDirectory))
        {
            var shardFiles = Directory.GetFiles(shardDirectory, "*.xml");
            if (shardFiles.Length > 0)
            {
                instanceBytes = shardFiles.Sum(GetFileSize);
            }
        }

        if (instanceBytes == 0)
        {
            instanceBytes = GetDirectorySize(shardDirectory);
        }

        return (modelBytes, instanceBytes);
    }

    string ResolveWorkspaceConfigPathFromWorkspaceRoot(Workspace workspace, string? configuredPath, string fallbackRelativePath)
    {
        var value = string.IsNullOrWhiteSpace(configuredPath) ? fallbackRelativePath : configuredPath.Trim();
        var normalized = value.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(workspace.WorkspaceRootPath, normalized));
    }

    string ResolveFirstExistingPath(IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    long GetFileSize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return 0L;
        }

        return new FileInfo(path).Length;
    }

    long GetDirectorySize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return 0L;
        }

        return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
            .Select(file => new FileInfo(file).Length)
            .Sum();
    }

    string FormatByteSizeWithBytes(long bytes)
    {
        var human = FormatByteSize(bytes);
        return string.Equals(human, $"{bytes} B", StringComparison.Ordinal)
            ? human
            : $"{human} ({bytes} B)";
    }

    string FormatByteSize(long bytes)
    {
        const double Kb = 1024d;
        const double Mb = Kb * 1024d;
        const double Gb = Mb * 1024d;

        if (bytes < Kb)
        {
            return $"{bytes} B";
        }

        if (bytes < Mb)
        {
            return (bytes / Kb).ToString("0.##", CultureInfo.InvariantCulture) + " KB";
        }

        if (bytes < Gb)
        {
            return (bytes / Mb).ToString("0.##", CultureInfo.InvariantCulture) + " MB";
        }

        return (bytes / Gb).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
    }

    void PrintContractCompatibilityWarning(Meta.Core.WorkspaceConfig.Generated.MetaWorkspace workspaceConfig)
    {
        var contractVersion = MetaWorkspaceConfig.GetContractVersion(workspaceConfig);
        if (!MetaWorkspaceConfig.TryParseContractVersion(contractVersion, out var major, out var minor))
        {
            return;
        }

        if (major == SupportedContractMajorVersion && minor > SupportedContractMinorVersion)
        {
            presenter.WriteWarning(
                $"workspace contractVersion '{contractVersion}' is newer than tool baseline '{SupportedContractMajorVersion}.{SupportedContractMinorVersion}'.");
        }
    }

    bool WorkspaceLooksInitialized(string workspaceRoot, string metadataRoot)
    {
        return File.Exists(Path.Combine(workspaceRoot, "workspace.xml")) ||
               File.Exists(Path.Combine(metadataRoot, "model.xml")) ||
               Directory.Exists(Path.Combine(metadataRoot, "instances"));
    }

    (string WorkspaceRootPath, string MetadataRootPath) ResolveWorkspaceFilesystemContext(string workspacePath)
    {
        var absolutePath = Path.GetFullPath(workspacePath);
        return HasWorkspaceOverrideInInvocation()
            ? ResolveWorkspaceFilesystemContextWithoutSearch(absolutePath)
            : DiscoverWorkspaceFilesystemContext(absolutePath);
    }

    (string WorkspaceRootPath, string MetadataRootPath) DiscoverWorkspaceFilesystemContext(string startPath)
    {
        var current = Directory.Exists(startPath)
            ? Path.GetFullPath(startPath)
            : Path.GetFullPath(Path.GetDirectoryName(startPath) ?? startPath);

        while (!string.IsNullOrWhiteSpace(current))
        {
            var metadataRoot = current;
            var workspaceXml = Path.Combine(current, "workspace.xml");
            if (File.Exists(workspaceXml) || IsWorkspaceMetadataCandidate(metadataRoot))
            {
                return (current, metadataRoot);
            }

            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new FileNotFoundException($"Could not find workspace starting from '{startPath}'.");
    }

    (string WorkspaceRootPath, string MetadataRootPath) ResolveWorkspaceFilesystemContextWithoutSearch(string workspacePath)
    {
        if (string.Equals(Path.GetFileName(workspacePath), "instances", StringComparison.OrdinalIgnoreCase))
        {
            var workspaceRoot = Directory.GetParent(workspacePath)?.FullName ?? workspacePath;
            if (IsWorkspaceMetadataCandidate(workspacePath) || Directory.Exists(workspacePath))
            {
                return (workspaceRoot, workspaceRoot);
            }
        }

        var metadataRoot = workspacePath;
        if (IsWorkspaceMetadataCandidate(metadataRoot) || Directory.Exists(metadataRoot))
        {
            return (workspacePath, metadataRoot);
        }

        throw new FileNotFoundException($"Could not find workspace under '{workspacePath}'.");
    }

    bool IsWorkspaceMetadataCandidate(string metadataRootPath)
    {
        var workspaceRootPath = Directory.GetParent(metadataRootPath)?.FullName ?? metadataRootPath;
        return File.Exists(Path.Combine(workspaceRootPath, "workspace.xml")) ||
               File.Exists(Path.Combine(metadataRootPath, "model.xml")) ||
               Directory.Exists(Path.Combine(metadataRootPath, "instances"));
    }
}
