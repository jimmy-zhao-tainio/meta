using System.Xml.Linq;

namespace MetaMesh.Core;

public sealed class MetaMeshWorkspaceService
{
    private const string DefaultMeshName = "Mesh";
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        "bin",
        "obj",
        "node_modules",
        "packages"
    };

    public MetaMesh.MetaMeshModel CreateEmpty(string meshName = DefaultMeshName, string rootPath = "")
    {
        var model = MetaMesh.MetaMeshModel.CreateEmpty();
        model.MeshList.Add(new MetaMesh.Mesh
        {
            Id = "mesh:default",
            Name = string.IsNullOrWhiteSpace(meshName) ? DefaultMeshName : meshName.Trim(),
            RootPath = rootPath.Trim(),
            Description = "Concrete map of mounted meta workspaces."
        });
        return model;
    }

    public MetaMeshScanResult ScanToWorkspace(string rootPath, string meshWorkspacePath, string meshName = DefaultMeshName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshWorkspacePath);

        var fullRoot = Path.GetFullPath(rootPath);
        var fullMeshPath = Path.GetFullPath(meshWorkspacePath);
        var model = BuildScanModel(fullRoot, meshName);
        model.SaveToXmlWorkspace(fullMeshPath);
        return BuildScanResult(model);
    }

    public MetaMeshScanResult SuggestFromRoot(string rootPath, string meshName = DefaultMeshName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return BuildScanResult(BuildScanModel(Path.GetFullPath(rootPath), meshName));
    }

    public MetaMeshShowResult Show(MetaMesh.MetaMeshModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return BuildShowResult(model);
    }

    public MetaMeshCheckResult Check(MetaMesh.MetaMeshModel model, string meshWorkspacePath)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshWorkspacePath);

        var fullMeshWorkspacePath = Path.GetFullPath(meshWorkspacePath);
        var issues = new List<MetaMeshIssue>();

        if (model.MeshList.Count == 0)
        {
            issues.Add(new MetaMeshIssue("Error", "MMESH001", "Mesh workspace contains no Mesh row."));
        }

        foreach (var duplicateHandle in model.WorkspaceInstanceList
                     .Where(static item => !string.IsNullOrWhiteSpace(item.Handle))
                     .GroupBy(static item => item.Handle, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1)
                     .Select(static group => group.Key))
        {
            issues.Add(new MetaMeshIssue("Error", "MMESH002", $"Workspace handle '{duplicateHandle}' is duplicated.", duplicateHandle));
        }

        foreach (var workspace in model.WorkspaceInstanceList.OrderBy(static item => item.Handle, StringComparer.OrdinalIgnoreCase))
        {
            var mounts = model.WorkspaceMountList
                .Where(item => ReferenceEquals(item.WorkspaceInstance, workspace) || string.Equals(item.WorkspaceInstance.Id, workspace.Id, StringComparison.Ordinal))
                .ToArray();

            if (mounts.Length == 0)
            {
                issues.Add(new MetaMeshIssue("Error", "MMESH003", $"Workspace '{workspace.Handle}' has no mount.", workspace.Handle));
                continue;
            }

            foreach (var mount in mounts)
            {
                var resolvedPath = ResolveMountPath(model, mount, fullMeshWorkspacePath);
                if (!Directory.Exists(resolvedPath))
                {
                    issues.Add(new MetaMeshIssue("Error", "MMESH004", $"Mounted path does not exist: {mount.PhysicalPath}", workspace.Handle));
                    continue;
                }

                if (!File.Exists(Path.Combine(resolvedPath, "model.xml")))
                {
                    issues.Add(new MetaMeshIssue("Warning", "MMESH005", $"Mounted path is not a normal meta workspace: {mount.PhysicalPath}", workspace.Handle));
                }
            }
        }

        foreach (var link in model.WorkspaceLinkList)
        {
            if (string.Equals(link.FromWorkspace?.Id, link.ToWorkspace?.Id, StringComparison.Ordinal))
            {
                issues.Add(new MetaMeshIssue("Warning", "MMESH006", $"Workspace link '{link.Id}' points from a workspace to itself.", link.FromWorkspace?.Handle ?? string.Empty));
            }
        }

        return new MetaMeshCheckResult(issues);
    }

    public MetaMeshImpactResult Impact(MetaMesh.MetaMeshModel model, string workspaceHandle)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceHandle);

        var start = ResolveWorkspaceByHandle(model, workspaceHandle);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start.Handle };
        var queue = new Queue<MetaMesh.WorkspaceInstance>();
        var affectedLinks = new List<MetaMeshLinkSummary>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var outgoing = model.WorkspaceLinkList
                .Where(link => string.Equals(link.FromWorkspace.Id, current.Id, StringComparison.Ordinal))
                .OrderBy(static link => link.ToWorkspace.Handle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static link => link.Kind, StringComparer.OrdinalIgnoreCase);

            foreach (var link in outgoing)
            {
                affectedLinks.Add(new MetaMeshLinkSummary(
                    link.FromWorkspace.Handle,
                    link.ToWorkspace.Handle,
                    link.Kind,
                    link.Description ?? string.Empty));

                if (visited.Add(link.ToWorkspace.Handle))
                {
                    queue.Enqueue(link.ToWorkspace);
                }
            }
        }

        return new MetaMeshImpactResult(
            start.Handle,
            affectedLinks,
            visited.Where(handle => !string.Equals(handle, start.Handle, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static handle => handle, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public MetaMeshWorkspaceSummary Mount(MetaMesh.MetaMeshModel model, string handle, string physicalPath)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalPath);

        var mesh = EnsureMesh(model);
        var normalizedHandle = NormalizeHandle(handle);
        var fullPath = Path.GetFullPath(physicalPath);
        var modelName = TryReadModelName(fullPath);
        var kind = InferWorkspaceKind(fullPath, modelName);
        var lifecycle = InferLifecycle(fullPath);

        var workspace = model.WorkspaceInstanceList
            .FirstOrDefault(item => string.Equals(item.Handle, normalizedHandle, StringComparison.OrdinalIgnoreCase));
        if (workspace is null)
        {
            workspace = new MetaMesh.WorkspaceInstance
            {
                Id = "workspace:" + normalizedHandle,
                Mesh = mesh,
                Handle = normalizedHandle,
            };
            model.WorkspaceInstanceList.Add(workspace);
        }

        workspace.DisplayName = string.IsNullOrWhiteSpace(workspace.DisplayName) ? normalizedHandle : workspace.DisplayName;
        workspace.ModelName = modelName;
        workspace.WorkspaceKind = kind;
        workspace.Lifecycle = lifecycle;

        var mount = model.WorkspaceMountList
            .FirstOrDefault(item => string.Equals(item.WorkspaceInstance.Id, workspace.Id, StringComparison.Ordinal));
        if (mount is null)
        {
            mount = new MetaMesh.WorkspaceMount
            {
                Id = "mount:" + normalizedHandle,
                WorkspaceInstance = workspace,
            };
            model.WorkspaceMountList.Add(mount);
        }

        mount.PhysicalPath = fullPath;
        mount.PathKind = "Absolute";

        return ToWorkspaceSummary(model, workspace);
    }

    public MetaMeshLinkSummary Link(MetaMesh.MetaMeshModel model, string fromHandle, string toHandle, string kind)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromHandle);
        ArgumentException.ThrowIfNullOrWhiteSpace(toHandle);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        var mesh = EnsureMesh(model);
        var from = ResolveWorkspaceByHandle(model, fromHandle);
        var to = ResolveWorkspaceByHandle(model, toHandle);
        var normalizedKind = NormalizeToken(kind);
        var id = $"link:{from.Handle}:{normalizedKind}:{to.Handle}";
        var existing = model.WorkspaceLinkList.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (existing is null)
        {
            existing = new MetaMesh.WorkspaceLink
            {
                Id = id,
                Mesh = mesh,
                FromWorkspace = from,
                ToWorkspace = to,
            };
            model.WorkspaceLinkList.Add(existing);
        }

        existing.Kind = normalizedKind;

        return new MetaMeshLinkSummary(from.Handle, to.Handle, existing.Kind, existing.Description ?? string.Empty);
    }

    private static MetaMesh.MetaMeshModel BuildScanModel(string fullRoot, string meshName)
    {
        var service = new MetaMeshWorkspaceService();
        var model = service.CreateEmpty(meshName, fullRoot);
        var mesh = model.MeshList.Single();
        var usedHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var workspacePath in FindWorkspaceRoots(fullRoot))
        {
            var modelName = TryReadModelName(workspacePath);
            var relativePath = Path.GetRelativePath(fullRoot, workspacePath);
            var handle = MakeUniqueHandle(DeriveHandle(relativePath, modelName), usedHandles);
            var workspace = new MetaMesh.WorkspaceInstance
            {
                Id = "workspace:" + handle,
                Mesh = mesh,
                Handle = handle,
                DisplayName = handle,
                ModelName = modelName,
                WorkspaceKind = InferWorkspaceKind(workspacePath, modelName),
                Lifecycle = InferLifecycle(workspacePath),
            };
            model.WorkspaceInstanceList.Add(workspace);
            model.WorkspaceMountList.Add(new MetaMesh.WorkspaceMount
            {
                Id = "mount:" + handle,
                WorkspaceInstance = workspace,
                PhysicalPath = relativePath,
                PathKind = "RelativeToMeshRoot",
            });

            foreach (var suggestion in BuildWorkspaceSuggestions(model, mesh, workspace, relativePath))
            {
                model.MeshSuggestionList.Add(suggestion);
            }
        }

        return model;
    }

    private static IEnumerable<MetaMesh.MeshSuggestion> BuildWorkspaceSuggestions(
        MetaMesh.MetaMeshModel model,
        MetaMesh.Mesh mesh,
        MetaMesh.WorkspaceInstance workspace,
        string relativePath)
    {
        var suggestions = new List<MetaMesh.MeshSuggestion>();
        if (relativePath.Contains("WS", StringComparison.OrdinalIgnoreCase) ||
            relativePath.Contains("Workspace", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add(new MetaMesh.MeshSuggestion
            {
                Id = "suggestion:" + workspace.Handle + ":clean-handle",
                Mesh = mesh,
                WorkspaceInstance = workspace,
                SuggestionKind = "CleanHandle",
                Severity = "Info",
                WorkspaceHandle = workspace.Handle,
                SuggestedHandle = workspace.Handle,
                SuggestedWorkspaceKind = workspace.WorkspaceKind,
                SuggestedLifecycle = workspace.Lifecycle,
                Message = "Physical workspace name looks tool-oriented; keep using the logical handle in commands."
            });
        }

        if (string.IsNullOrWhiteSpace(workspace.ModelName))
        {
            suggestions.Add(new MetaMesh.MeshSuggestion
            {
                Id = "suggestion:" + workspace.Handle + ":missing-model",
                Mesh = mesh,
                WorkspaceInstance = workspace,
                SuggestionKind = "MissingModelName",
                Severity = "Warning",
                WorkspaceHandle = workspace.Handle,
                Message = "Workspace model name could not be read."
            });
        }

        return suggestions;
    }

    private static IReadOnlyList<string> FindWorkspaceRoots(string fullRoot)
    {
        var results = new List<string>();
        if (!Directory.Exists(fullRoot))
        {
            return results;
        }

        var pending = new Stack<string>();
        pending.Push(fullRoot);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (File.Exists(Path.Combine(current, "workspace.xml")) && File.Exists(Path.Combine(current, "model.xml")))
            {
                results.Add(current);
                continue;
            }

            foreach (var child in Directory.EnumerateDirectories(current).OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                if (IgnoredDirectoryNames.Contains(Path.GetFileName(child)))
                {
                    continue;
                }

                pending.Push(child);
            }
        }

        return results.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static MetaMesh.Mesh EnsureMesh(MetaMesh.MetaMeshModel model)
    {
        if (model.MeshList.Count > 0)
        {
            return model.MeshList[0];
        }

        var mesh = new MetaMesh.Mesh
        {
            Id = "mesh:default",
            Name = DefaultMeshName,
        };
        model.MeshList.Add(mesh);
        return mesh;
    }

    private static MetaMesh.WorkspaceInstance ResolveWorkspaceByHandle(MetaMesh.MetaMeshModel model, string handle)
    {
        var normalizedHandle = NormalizeHandle(handle);
        return model.WorkspaceInstanceList.FirstOrDefault(item => string.Equals(item.Handle, normalizedHandle, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Workspace handle '{handle}' was not found.");
    }

    private static MetaMeshScanResult BuildScanResult(MetaMesh.MetaMeshModel model)
    {
        var show = BuildShowResult(model);
        return new MetaMeshScanResult(show.RootPath, show.Workspaces, show.Suggestions);
    }

    private static MetaMeshShowResult BuildShowResult(MetaMesh.MetaMeshModel model)
    {
        var mesh = model.MeshList.FirstOrDefault();
        return new MetaMeshShowResult(
            mesh?.Name ?? string.Empty,
            mesh?.RootPath ?? string.Empty,
            model.WorkspaceInstanceList
                .OrderBy(static item => item.Handle, StringComparer.OrdinalIgnoreCase)
                .Select(item => ToWorkspaceSummary(model, item))
                .ToArray(),
            model.WorkspaceLinkList
                .OrderBy(static item => item.FromWorkspace.Handle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.ToWorkspace.Handle, StringComparer.OrdinalIgnoreCase)
                .Select(static item => new MetaMeshLinkSummary(item.FromWorkspace.Handle, item.ToWorkspace.Handle, item.Kind, item.Description ?? string.Empty))
                .ToArray(),
            model.MeshSuggestionList
                .OrderBy(static item => item.WorkspaceHandle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.SuggestionKind, StringComparer.OrdinalIgnoreCase)
                .Select(static item => new MetaMeshSuggestionSummary(
                    item.SuggestionKind,
                    item.Severity,
                    item.WorkspaceHandle ?? string.Empty,
                    item.Message,
                    item.SuggestedHandle ?? string.Empty,
                    item.SuggestedWorkspaceKind ?? string.Empty,
                    item.SuggestedLifecycle ?? string.Empty,
                    item.SuggestedPath ?? string.Empty))
                .ToArray());
    }

    private static MetaMeshWorkspaceSummary ToWorkspaceSummary(MetaMesh.MetaMeshModel model, MetaMesh.WorkspaceInstance workspace)
    {
        var mount = model.WorkspaceMountList.FirstOrDefault(item => string.Equals(item.WorkspaceInstance.Id, workspace.Id, StringComparison.Ordinal));
        return new MetaMeshWorkspaceSummary(
            workspace.Handle,
            mount?.PhysicalPath ?? string.Empty,
            workspace.ModelName ?? string.Empty,
            workspace.WorkspaceKind ?? string.Empty,
            workspace.Lifecycle ?? string.Empty);
    }

    private static string ResolveMountPath(MetaMesh.MetaMeshModel model, MetaMesh.WorkspaceMount mount, string meshWorkspacePath)
    {
        if (Path.IsPathRooted(mount.PhysicalPath))
        {
            return Path.GetFullPath(mount.PhysicalPath);
        }

        var root = model.MeshList.FirstOrDefault()?.RootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = ".";
        }

        if (!Path.IsPathRooted(root))
        {
            root = Path.Combine(meshWorkspacePath, root);
        }

        return Path.GetFullPath(Path.Combine(root, mount.PhysicalPath));
    }

    private static string TryReadModelName(string workspacePath)
    {
        try
        {
            var modelPath = Path.Combine(workspacePath, "model.xml");
            if (!File.Exists(modelPath))
            {
                return string.Empty;
            }

            return XDocument.Load(modelPath).Root?.Attribute("name")?.Value?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string DeriveHandle(string relativePath, string modelName)
    {
        var segments = relativePath
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeHandle)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        var segmentSet = new HashSet<string>(segments, StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in new[]
                 {
                     "source", "conversion", "transform", "transforms", "binding", "pipeline", "orchestration",
                     "quality", "data-quality", "raw-vault", "business-vault", "vault", "warehouse", "analytics",
                     "tabular", "multidim", "multi-dimensional", "schema", "sql"
                 })
        {
            if (segmentSet.Contains(candidate))
            {
                return NormalizeKnownHandle(candidate);
            }
        }

        var modelHandle = NormalizeKnownHandle(ModelNameToHandle(modelName));
        if (!string.IsNullOrWhiteSpace(modelHandle))
        {
            return modelHandle;
        }

        return segments.LastOrDefault() ?? "workspace";
    }

    private static string ModelNameToHandle(string modelName) =>
        modelName.Trim() switch
        {
            "MetaSchema" => "schema",
            "MetaTransformScript" => "transform",
            "MetaTransformBinding" => "binding",
            "MetaDataQuality" => "quality",
            "MetaPipeline" => "pipeline",
            "MetaOrchestration" => "orchestration",
            "MetaSql" => "sql",
            "MetaSqlDeployManifest" => "deploy",
            "MetaRawDataVault" => "raw-vault",
            "MetaBusinessDataVault" => "business-vault",
            "MetaDataWarehouse" => "warehouse",
            "MetaAnalytics" => "analytics",
            "MetaTabular" => "tabular",
            "MetaMultiDimensional" => "multidim",
            _ => string.Empty
        };

    private static string NormalizeKnownHandle(string handle) =>
        handle switch
        {
            "transforms" => "transform",
            "data-quality" => "quality",
            "multi-dimensional" => "multidim",
            _ => handle
        };

    private static string MakeUniqueHandle(string baseHandle, HashSet<string> usedHandles)
    {
        var normalized = string.IsNullOrWhiteSpace(baseHandle) ? "workspace" : NormalizeHandle(baseHandle);
        var candidate = normalized;
        var suffix = 2;
        while (!usedHandles.Add(candidate))
        {
            candidate = normalized + "-" + suffix;
            suffix++;
        }

        return candidate;
    }

    private static string InferWorkspaceKind(string path, string modelName)
    {
        if (!string.IsNullOrWhiteSpace(modelName))
        {
            return modelName;
        }

        var normalizedPath = NormalizeHandle(path);
        if (normalizedPath.Contains("source", StringComparison.OrdinalIgnoreCase))
        {
            return "Source";
        }

        if (normalizedPath.Contains("pipeline", StringComparison.OrdinalIgnoreCase))
        {
            return "Pipeline";
        }

        return "Workspace";
    }

    private static string InferLifecycle(string path)
    {
        var normalizedPath = NormalizeHandle(path);
        if (normalizedPath.Contains("generated", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("current", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("sql", StringComparison.OrdinalIgnoreCase))
        {
            return "Generated";
        }

        if (normalizedPath.Contains("external", StringComparison.OrdinalIgnoreCase))
        {
            return "External";
        }

        if (normalizedPath.Contains("temp", StringComparison.OrdinalIgnoreCase))
        {
            return "Temporary";
        }

        return "Authored";
    }

    private static string NormalizeHandle(string value)
    {
        var output = new char[value.Length];
        var length = 0;
        var previousWasSeparator = false;
        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                output[length++] = char.ToLowerInvariant(character);
                previousWasSeparator = false;
                continue;
            }

            if (length > 0 && !previousWasSeparator)
            {
                output[length++] = '-';
                previousWasSeparator = true;
            }
        }

        while (length > 0 && output[length - 1] == '-')
        {
            length--;
        }

        return length == 0 ? string.Empty : new string(output, 0, length);
    }

    private static string NormalizeToken(string value)
    {
        var normalized = NormalizeHandle(value);
        return string.IsNullOrWhiteSpace(normalized) ? "depends-on" : normalized;
    }
}
