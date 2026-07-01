using System.Diagnostics;

namespace MetaMesh.Core;

public sealed class MetaMeshWorkspaceService
{
    private const string DefaultMeshName = "Mesh";

    public MetaMesh.MetaMeshModel CreateEmpty(
        string? meshName = null,
        string? rootPath = null,
        string? description = null)
    {
        var model = MetaMesh.MetaMeshModel.CreateEmpty();
        model.MeshList.Add(new MetaMesh.Mesh
        {
            Id = "mesh:default",
            Name = RequiredName(string.IsNullOrWhiteSpace(meshName) ? DefaultMeshName : meshName),
            RootPath = NormalizeOptional(rootPath),
            Description = NormalizeOptional(description)
        });
        return model;
    }

    public MetaMeshShowResult Show(MetaMesh.MetaMeshModel model, string meshWorkspacePath)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshWorkspacePath);

        var mesh = RequireMesh(model);
        var fullMeshWorkspacePath = Path.GetFullPath(meshWorkspacePath);
        var resolvedRootPath = ResolveMeshRootPath(mesh, fullMeshWorkspacePath);
        return new MetaMeshShowResult(
            mesh.Name,
            mesh.RootPath ?? string.Empty,
            resolvedRootPath,
            model.WorkspaceList
                .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(item => ToWorkspaceSummary(item, fullMeshWorkspacePath, resolvedRootPath))
                .ToArray(),
            CollectWorkspaceIssues(model, resolvedRootPath),
            model.OperationList
                .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(operation => ToOperationSummary(model, operation))
                .ToArray());
    }

    public MetaMeshWorkspaceSummary AddWorkspace(
        MetaMesh.MetaMeshModel model,
        string name,
        string path,
        string? modelName,
        string? description,
        string meshWorkspacePath)
    {
        ArgumentNullException.ThrowIfNull(model);
        var mesh = RequireMesh(model);
        var normalizedName = RequiredName(name);
        var normalizedPath = RequiredName(path);

        if (model.WorkspaceList.Any(item => string.Equals(item.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Workspace '{normalizedName}' already exists.");
        }

        var workspace = new MetaMesh.Workspace
        {
            Id = "workspace:" + NormalizeToken(normalizedName),
            Mesh = mesh,
            Name = normalizedName,
            Path = normalizedPath,
            ModelName = NormalizeOptional(modelName),
            Description = NormalizeOptional(description)
        };

        RequireUniqueId(model.WorkspaceList, workspace.Id, "Workspace");
        model.WorkspaceList.Add(workspace);
        var resolvedRootPath = ResolveMeshRootPath(mesh, Path.GetFullPath(meshWorkspacePath));
        return ToWorkspaceSummary(workspace, meshWorkspacePath, resolvedRootPath);
    }

    public MetaMeshOperationSummary AddOperation(
        MetaMesh.MetaMeshModel model,
        string name,
        string? description)
    {
        ArgumentNullException.ThrowIfNull(model);
        var mesh = RequireMesh(model);
        var normalizedName = RequiredName(name);

        if (model.OperationList.Any(item => string.Equals(item.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Operation '{normalizedName}' already exists.");
        }

        var operation = new MetaMesh.Operation
        {
            Id = "operation:" + NormalizeToken(normalizedName),
            Mesh = mesh,
            Name = normalizedName,
            Description = NormalizeOptional(description)
        };

        RequireUniqueId(model.OperationList, operation.Id, "Operation");
        model.OperationList.Add(operation);
        return ToOperationSummary(model, operation);
    }

    public MetaMeshOperationSummary AddStep(
        MetaMesh.MetaMeshModel model,
        string operationName,
        string name,
        string executable,
        string? arguments,
        string? workingDirectory,
        string? previousStepName,
        string? expectedExitCode,
        string? description)
    {
        ArgumentNullException.ThrowIfNull(model);
        var operation = RequireOperation(model, operationName);
        var normalizedName = RequiredName(name);
        var normalizedExecutable = RequiredName(executable);

        if (model.OperationStepList.Any(item =>
                ReferenceEquals(item.Operation, operation) &&
                string.Equals(item.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Operation '{operation.Name}' already has step '{normalizedName}'.");
        }

        var previousStep = string.IsNullOrWhiteSpace(previousStepName)
            ? null
            : RequireOperationStep(model, operation, previousStepName);

        var step = new MetaMesh.OperationStep
        {
            Id = "operation-step:" + NormalizeToken(operation.Name) + ":" + NormalizeToken(normalizedName),
            Operation = operation,
            Name = normalizedName,
            Executable = normalizedExecutable,
            Arguments = NormalizeOptional(arguments),
            WorkingDirectory = NormalizeOptional(workingDirectory),
            ExpectedExitCode = NormalizeExpectedExitCode(expectedExitCode),
            PreviousStep = previousStep,
            Description = NormalizeOptional(description)
        };

        RequireUniqueId(model.OperationStepList, step.Id, "OperationStep");
        model.OperationStepList.Add(step);
        return ToOperationSummary(model, operation);
    }

    public MetaMeshRunResult RunOperation(
        MetaMesh.MetaMeshModel model,
        string operationName,
        string meshWorkspacePath,
        IMetaMeshRunObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshWorkspacePath);
        var mesh = RequireMesh(model);
        var operation = RequireOperation(model, operationName);
        var fullMeshWorkspacePath = Path.GetFullPath(meshWorkspacePath);
        var resolvedRootPath = ResolveMeshRootPath(mesh, fullMeshWorkspacePath);
        var workspaceTokens = BuildWorkspaceTokens(model, resolvedRootPath);
        var stepResults = new List<MetaMeshRunStepResult>();
        var plan = BuildRunPlan(model, operation, fullMeshWorkspacePath, resolvedRootPath, workspaceTokens);

        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            observer?.StepStarted(new MetaMeshRunStepStart(
                i + 1,
                plan.Steps.Count,
                step.Name,
                FormatCommand(step.Executable, step.Arguments),
                step.WorkingDirectory));
            var result = RunProcess(step.Name, step.Executable, step.Arguments, step.WorkingDirectory, step.ExpectedExitCode);
            observer?.StepCompleted(result);
            stepResults.Add(result);
            if (!result.Succeeded)
            {
                break;
            }
        }

        return new MetaMeshRunResult(operation.Name, stepResults);
    }

    public MetaMeshValidationResult ValidateOperation(
        MetaMesh.MetaMeshModel model,
        string operationName,
        string meshWorkspacePath)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshWorkspacePath);
        var mesh = RequireMesh(model);
        var operation = RequireOperation(model, operationName);
        var fullMeshWorkspacePath = Path.GetFullPath(meshWorkspacePath);
        var resolvedRootPath = ResolveMeshRootPath(mesh, fullMeshWorkspacePath);
        var workspaceTokens = BuildWorkspaceTokens(model, resolvedRootPath);
        var plan = BuildRunPlan(model, operation, fullMeshWorkspacePath, resolvedRootPath, workspaceTokens);
        return new MetaMeshValidationResult(
            operation.Name,
            plan.Steps
                .Select(static step => new MetaMeshValidationStepSummary(
                    step.Name,
                    FormatCommand(step.Executable, step.Arguments),
                    step.WorkingDirectory,
                    step.ExpectedExitCode))
                .ToArray());
    }

    private static MetaMeshRunPlan BuildRunPlan(
        MetaMesh.MetaMeshModel model,
        MetaMesh.Operation operation,
        string meshWorkspacePath,
        string resolvedRootPath,
        IReadOnlyDictionary<string, string> workspaceTokens)
    {
        RequireWorkspaceDirectory(meshWorkspacePath, "MetaMesh workspace");
        RequireDirectoryReadable(meshWorkspacePath, "MetaMesh workspace");
        RequireDirectory(resolvedRootPath, "Mesh root");

        var steps = OrderOperationSteps(model, operation, strict: true);
        if (steps.Count == 0)
        {
            throw new InvalidOperationException($"Operation '{operation.Name}' has no steps.");
        }

        var workspaceIssues = CollectOperationWorkspaceIssues(model, steps, resolvedRootPath);
        if (workspaceIssues.Count > 0)
        {
            throw new MetaMeshWorkspaceIssueException(workspaceIssues);
        }

        var plannedSteps = new List<MetaMeshPlannedStep>();
        foreach (var step in steps)
        {
            ValidateTokens(step.Executable, workspaceTokens);
            ValidateTokens(step.Arguments, workspaceTokens);
            ValidateTokens(step.WorkingDirectory, workspaceTokens);

            var executable = ExpandTokens(step.Executable, meshWorkspacePath, resolvedRootPath, workspaceTokens);
            var arguments = ExpandTokens(step.Arguments ?? string.Empty, meshWorkspacePath, resolvedRootPath, workspaceTokens);
            var workingDirectory = ResolveWorkingDirectory(
                ExpandTokens(step.WorkingDirectory ?? string.Empty, meshWorkspacePath, resolvedRootPath, workspaceTokens),
                resolvedRootPath);

            RequireDirectory(workingDirectory, $"Working directory for step '{step.Name}'");
            RequireDirectoryReadable(workingDirectory, $"Working directory for step '{step.Name}'");
            RequireDirectoryWritable(workingDirectory, $"Working directory for step '{step.Name}'");

            var resolvedExecutable = ResolveExecutable(executable, workingDirectory)
                                     ?? throw new InvalidOperationException($"Executable '{executable}' for step '{step.Name}' was not found.");
            plannedSteps.Add(new MetaMeshPlannedStep(
                step.Name,
                resolvedExecutable,
                arguments,
                workingDirectory,
                ParseExpectedExitCode(step.ExpectedExitCode, step.Name)));
        }

        return new MetaMeshRunPlan(plannedSteps);
    }

    private static MetaMeshRunStepResult RunProcess(
        string stepName,
        string executable,
        string arguments,
        string workingDirectory,
        int expectedExitCode)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"Could not start operation step '{stepName}'.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        return new MetaMeshRunStepResult(
            stepName,
            FormatCommand(executable, arguments),
            workingDirectory,
            expectedExitCode,
            process.ExitCode,
            output);
    }

    private static string FormatCommand(string executable, string arguments) =>
        string.IsNullOrWhiteSpace(arguments) ? executable : executable + " " + arguments;

    private static MetaMeshOperationSummary ToOperationSummary(
        MetaMesh.MetaMeshModel model,
        MetaMesh.Operation operation)
    {
        return new MetaMeshOperationSummary(
            operation.Name,
            operation.Description ?? string.Empty,
            OrderOperationSteps(model, operation, strict: false)
                .Select(static item => new MetaMeshOperationStepSummary(
                    item.Name,
                    item.Executable,
                    item.Arguments ?? string.Empty,
                    item.WorkingDirectory ?? string.Empty,
                    ParseExpectedExitCode(item.ExpectedExitCode, item.Name),
                    item.Description ?? string.Empty))
                .ToArray());
    }

    private static MetaMeshWorkspaceSummary ToWorkspaceSummary(
        MetaMesh.Workspace workspace,
        string meshWorkspacePath,
        string resolvedRootPath)
    {
        return new MetaMeshWorkspaceSummary(
            workspace.Name,
            workspace.Path,
            ResolveWorkspacePath(workspace, resolvedRootPath),
            workspace.ModelName ?? string.Empty,
            workspace.Description ?? string.Empty);
    }

    private static IReadOnlyList<MetaMeshWorkspaceIssue> CollectWorkspaceIssues(
        MetaMesh.MetaMeshModel model,
        string resolvedRootPath)
    {
        var issues = new List<MetaMeshWorkspaceIssue>();
        foreach (var workspace in model.WorkspaceList.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var resolvedPath = ResolveWorkspacePath(workspace, resolvedRootPath);
            var reason = GetWorkspaceIssueReason(resolvedPath);
            if (reason is null)
            {
                continue;
            }

            issues.Add(new MetaMeshWorkspaceIssue(
                workspace.Name,
                workspace.Path,
                resolvedPath,
                workspace.ModelName ?? string.Empty,
                reason));
        }

        return issues;
    }

    private static string? GetWorkspaceIssueReason(string path)
    {
        if (!Directory.Exists(path))
        {
            return "directory does not exist";
        }

        try
        {
            Directory.EnumerateFileSystemEntries(path).FirstOrDefault();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return "directory is not readable";
        }

        return File.Exists(Path.Combine(path, "workspace.xml"))
            ? null
            : "workspace.xml is missing";
    }

    private static IReadOnlyList<MetaMeshWorkspaceIssue> CollectOperationWorkspaceIssues(
        MetaMesh.MetaMeshModel model,
        IReadOnlyList<MetaMesh.OperationStep> steps,
        string resolvedRootPath)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            AddWorkspaceTokenNames(names, step.Executable);
            AddWorkspaceTokenNames(names, step.WorkingDirectory);
        }

        var issues = new List<MetaMeshWorkspaceIssue>();
        foreach (var name in names.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
        {
            var workspace = model.WorkspaceList.FirstOrDefault(item =>
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (workspace is null)
            {
                continue;
            }

            var resolvedPath = ResolveWorkspacePath(workspace, resolvedRootPath);
            var reason = GetWorkspaceIssueReason(resolvedPath);
            if (reason is null)
            {
                continue;
            }

            issues.Add(new MetaMeshWorkspaceIssue(
                workspace.Name,
                workspace.Path,
                resolvedPath,
                workspace.ModelName ?? string.Empty,
                reason));
        }

        return issues;
    }

    private static void AddWorkspaceTokenNames(HashSet<string> names, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var name in FindWorkspacePathTokens(value))
        {
            names.Add(name);
        }
    }

    private static IReadOnlyList<MetaMesh.OperationStep> OrderOperationSteps(
        MetaMesh.MetaMeshModel model,
        MetaMesh.Operation operation,
        bool strict)
    {
        var steps = model.OperationStepList
            .Where(item => ReferenceEquals(item.Operation, operation))
            .ToArray();
        if (steps.Length == 0)
        {
            return steps;
        }

        if (!strict)
        {
            return OrderLooseChain(steps, static item => item.PreviousStep, static item => item.Name);
        }

        foreach (var step in steps)
        {
            if (step.PreviousStep is not null && !ReferenceEquals(step.PreviousStep.Operation, operation))
            {
                throw new InvalidOperationException($"Step '{step.Name}' points to a previous step from another operation.");
            }
        }

        var heads = steps.Where(static item => item.PreviousStep is null).ToArray();
        if (heads.Length != 1)
        {
            throw new InvalidOperationException($"Operation '{operation.Name}' must have one first step.");
        }

        var ordered = new List<MetaMesh.OperationStep>();
        var current = heads[0];
        while (current is not null)
        {
            if (ordered.Any(item => ReferenceEquals(item, current)))
            {
                throw new InvalidOperationException($"Operation '{operation.Name}' has a cycle in its step order.");
            }

            ordered.Add(current);
            var next = steps.Where(item => ReferenceEquals(item.PreviousStep, current)).ToArray();
            if (next.Length > 1)
            {
                throw new InvalidOperationException($"Operation '{operation.Name}' has multiple steps after '{current.Name}'.");
            }

            current = next.SingleOrDefault();
        }

        if (ordered.Count != steps.Length)
        {
            throw new InvalidOperationException($"Operation '{operation.Name}' has disconnected steps.");
        }

        return ordered;
    }

    private static IReadOnlyList<T> OrderLooseChain<T>(
        IReadOnlyList<T> items,
        Func<T, T?> previous,
        Func<T, string> name)
        where T : class
    {
        var ordered = new List<T>();
        var remaining = items.ToList();
        foreach (var head in items
                     .Where(item => previous(item) is null)
                     .OrderBy(name, StringComparer.OrdinalIgnoreCase))
        {
            AppendLooseChain(head, remaining, ordered, previous);
        }

        foreach (var item in remaining.OrderBy(name, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            AppendLooseChain(item, remaining, ordered, previous);
        }

        return ordered;
    }

    private static void AppendLooseChain<T>(
        T item,
        List<T> remaining,
        List<T> ordered,
        Func<T, T?> previous)
        where T : class
    {
        if (!remaining.Remove(item))
        {
            return;
        }

        ordered.Add(item);
        foreach (var next in remaining.Where(candidate => ReferenceEquals(previous(candidate), item)).ToArray())
        {
            AppendLooseChain(next, remaining, ordered, previous);
        }
    }

    private static IReadOnlyDictionary<string, string> BuildWorkspaceTokens(
        MetaMesh.MetaMeshModel model,
        string resolvedRootPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var workspace in model.WorkspaceList)
        {
            if (!result.TryAdd(workspace.Name, ResolveWorkspacePath(workspace, resolvedRootPath)))
            {
                throw new InvalidOperationException($"Workspace name '{workspace.Name}' is declared more than once.");
            }
        }

        return result;
    }

    private static MetaMesh.Mesh RequireMesh(MetaMesh.MetaMeshModel model)
    {
        if (model.MeshList.Count != 1)
        {
            throw new InvalidOperationException("MetaMesh workspace must contain exactly one Mesh row.");
        }

        return model.MeshList[0];
    }

    private static MetaMesh.Operation RequireOperation(MetaMesh.MetaMeshModel model, string name)
    {
        var normalizedName = RequiredName(name);
        return model.OperationList.FirstOrDefault(item => string.Equals(item.Name, normalizedName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Operation '{normalizedName}' was not found.");
    }

    private static MetaMesh.OperationStep RequireOperationStep(
        MetaMesh.MetaMeshModel model,
        MetaMesh.Operation operation,
        string stepName)
    {
        var normalizedName = RequiredName(stepName);
        return model.OperationStepList.FirstOrDefault(item =>
                   ReferenceEquals(item.Operation, operation) &&
                   string.Equals(item.Name, normalizedName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Operation '{operation.Name}' has no step '{normalizedName}'.");
    }

    private static string ResolveMeshRootPath(MetaMesh.Mesh mesh, string meshWorkspacePath)
    {
        if (string.IsNullOrWhiteSpace(mesh.RootPath))
        {
            return Path.GetFullPath(meshWorkspacePath);
        }

        return Path.IsPathRooted(mesh.RootPath)
            ? Path.GetFullPath(mesh.RootPath)
            : Path.GetFullPath(Path.Combine(meshWorkspacePath, mesh.RootPath));
    }

    private static string ResolveWorkspacePath(MetaMesh.Workspace workspace, string resolvedRootPath)
    {
        return Path.IsPathRooted(workspace.Path)
            ? Path.GetFullPath(workspace.Path)
            : Path.GetFullPath(Path.Combine(resolvedRootPath, workspace.Path));
    }

    private static string ResolveWorkingDirectory(string workingDirectory, string resolvedRootPath)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return resolvedRootPath;
        }

        return Path.IsPathRooted(workingDirectory)
            ? Path.GetFullPath(workingDirectory)
            : Path.GetFullPath(Path.Combine(resolvedRootPath, workingDirectory));
    }

    private static string ExpandTokens(
        string value,
        string meshWorkspacePath,
        string resolvedRootPath,
        IReadOnlyDictionary<string, string> workspacePaths)
    {
        var result = ReplaceOrdinalIgnoreCase(value, "{mesh.workspace}", meshWorkspacePath);
        result = ReplaceOrdinalIgnoreCase(result, "{mesh.root}", resolvedRootPath);
        foreach (var workspace in workspacePaths)
        {
            result = ReplaceOrdinalIgnoreCase(result, "{workspace:" + workspace.Key + ".path}", workspace.Value);
        }

        foreach (var environmentVariable in FindEnvironmentVariableTokens(result))
        {
            result = ReplaceOrdinalIgnoreCase(
                result,
                "{env:" + environmentVariable + "}",
                environmentVariable);
        }

        return result;
    }

    private static void ValidateTokens(
        string? value,
        IReadOnlyDictionary<string, string> workspacePaths)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var workspaceName in FindWorkspacePathTokens(value))
        {
            if (!workspacePaths.ContainsKey(workspaceName))
            {
                throw new InvalidOperationException($"Workspace token '{{workspace:{workspaceName}.path}}' references an undeclared workspace.");
            }
        }

        foreach (var environmentVariable in FindEnvironmentVariableTokens(value))
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariable)))
            {
                throw new InvalidOperationException($"Environment variable '{environmentVariable}' is not set or empty.");
            }
        }
    }

    private static IEnumerable<string> FindWorkspacePathTokens(string value)
    {
        const string prefix = "{workspace:";
        const string suffix = ".path}";
        var startIndex = 0;
        while (true)
        {
            var index = value.IndexOf(prefix, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                yield break;
            }

            var nameStart = index + prefix.Length;
            var endIndex = value.IndexOf(suffix, nameStart, StringComparison.OrdinalIgnoreCase);
            if (endIndex < 0)
            {
                startIndex = nameStart;
                continue;
            }

            var name = value[nameStart..endIndex].Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return name;
            }

            startIndex = endIndex + suffix.Length;
        }
    }

    private static IEnumerable<string> FindEnvironmentVariableTokens(string value)
    {
        const string prefix = "{env:";
        const string suffix = "}";
        var startIndex = 0;
        while (true)
        {
            var index = value.IndexOf(prefix, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                yield break;
            }

            var nameStart = index + prefix.Length;
            var endIndex = value.IndexOf(suffix, nameStart, StringComparison.Ordinal);
            if (endIndex < 0)
            {
                startIndex = nameStart;
                continue;
            }

            var name = value[nameStart..endIndex].Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return name;
            }

            startIndex = endIndex + suffix.Length;
        }
    }

    private static string? ResolveExecutable(string executable, string workingDirectory)
    {
        if (Path.IsPathFullyQualified(executable) || ContainsDirectorySeparator(executable))
        {
            var candidate = Path.IsPathRooted(executable)
                ? executable
                : Path.Combine(workingDirectory, executable);
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }

        foreach (var directory in EnumerateExecutableSearchPaths(workingDirectory))
        {
            foreach (var candidate in EnumerateExecutableCandidates(directory, executable))
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateExecutableSearchPaths(string workingDirectory)
    {
        yield return workingDirectory;

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(directory))
            {
                yield return directory.Trim();
            }
        }
    }

    private static IEnumerable<string> EnumerateExecutableCandidates(string directory, string executable)
    {
        yield return Path.Combine(directory, executable);

        if (!OperatingSystem.IsWindows() || Path.HasExtension(executable))
        {
            yield break;
        }

        var pathExtensions = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathExtensions))
        {
            pathExtensions = ".COM;.EXE;.BAT;.CMD";
        }

        foreach (var extension in pathExtensions.Split(';'))
        {
            if (!string.IsNullOrWhiteSpace(extension))
            {
                yield return Path.Combine(directory, executable + extension.Trim());
            }
        }
    }

    private static bool ContainsDirectorySeparator(string value) =>
        value.Contains(Path.DirectorySeparatorChar) ||
        value.Contains(Path.AltDirectorySeparatorChar);

    private static void RequireWorkspaceDirectory(string path, string label)
    {
        RequireDirectory(path, label);
        var workspaceFilePath = Path.Combine(path, "workspace.xml");
        if (!File.Exists(workspaceFilePath))
        {
            throw new InvalidOperationException($"{label} '{path}' does not contain workspace.xml.");
        }
    }

    private static void RequireDirectory(string path, string label)
    {
        if (!Directory.Exists(path))
        {
            throw new InvalidOperationException($"{label} '{path}' does not exist.");
        }
    }

    private static void RequireDirectoryReadable(string path, string label)
    {
        try
        {
            Directory.EnumerateFileSystemEntries(path).FirstOrDefault();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException($"{label} '{path}' is not readable.", exception);
        }
    }

    private static void RequireDirectoryWritable(string path, string label)
    {
        var probePath = Path.Combine(path, ".metamesh-write-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
            {
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException($"{label} '{path}' is not writable.", exception);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }

    private static string ReplaceOrdinalIgnoreCase(string text, string oldValue, string newValue)
    {
        var startIndex = 0;
        while (true)
        {
            var index = text.IndexOf(oldValue, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return text;
            }

            text = text[..index] + newValue + text[(index + oldValue.Length)..];
            startIndex = index + newValue.Length;
        }
    }

    private static void RequireUniqueId<T>(IEnumerable<T> rows, string id, string entityName)
        where T : class
    {
        var property = typeof(T).GetProperty("Id")
                       ?? throw new InvalidOperationException($"Entity '{entityName}' does not expose Id.");
        if (rows.Any(row => string.Equals((string?)property.GetValue(row), id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"{entityName} id '{id}' already exists.");
        }
    }

    private static string RequiredName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A required name value was empty.");
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeExpectedExitCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!int.TryParse(trimmed, out var exitCode) || exitCode < 0)
        {
            throw new ArgumentException($"Expected exit code '{trimmed}' is not a non-negative integer.");
        }

        return exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int ParseExpectedExitCode(string? value, string stepName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (int.TryParse(value.Trim(), out var exitCode) && exitCode >= 0)
        {
            return exitCode;
        }

        throw new InvalidOperationException($"Step '{stepName}' has invalid expected exit code '{value}'.");
    }

    private static string NormalizeToken(string value)
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

        return length == 0 ? "item" : new string(output, 0, length);
    }

    private sealed record MetaMeshRunPlan(IReadOnlyList<MetaMeshPlannedStep> Steps);

    private sealed record MetaMeshPlannedStep(
        string Name,
        string Executable,
        string Arguments,
        string WorkingDirectory,
        int ExpectedExitCode);
}
