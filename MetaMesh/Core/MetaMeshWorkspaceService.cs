using System.Diagnostics;
using Meta.Surfaces;
using SurfaceCSharpWorkspace = global::Meta.Surfaces.CSharp.CSharpWorkspace;
using SurfaceSqlWorkspace = global::Meta.Surfaces.Sql.SqlWorkspace;
using Meta.Core.Connections;

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

    public MetaMeshShowResult Show(
        MetaMesh.MetaMeshModel model,
        MetaMeshWorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(context);

        var mesh = RequireMesh(model);
        var resolvedRootPath = ResolveMeshRootPath(mesh, context);
        var workspaces = ResolveWorkspaces(model, resolvedRootPath);
        return new MetaMeshShowResult(
            mesh.Name,
            mesh.RootPath ?? string.Empty,
            resolvedRootPath,
            workspaces
                .OrderBy(static item => item.Workspace.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToWorkspaceSummary)
                .ToArray(),
            CollectWorkspaceIssues(workspaces),
            model.OperationList
                .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(operation => ToOperationSummary(model, operation))
                .ToArray());
    }

    public MetaMeshWorkspaceSummary AddWorkspace(
        MetaMesh.MetaMeshModel model,
        string name,
        string? xmlPath,
        string? csharpPath,
        string? sqlConnectionEnvironmentVariable,
        string? modelName,
        string? description,
        MetaMeshWorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(context);
        var mesh = RequireMesh(model);
        var normalizedName = RequiredName(name);
        var locations = new[]
        {
            (Surface: "xml", Location: NormalizeOptional(xmlPath)),
            (Surface: "csharp", Location: NormalizeOptional(csharpPath)),
            (Surface: "sql", Location: NormalizeOptional(sqlConnectionEnvironmentVariable))
        }.Where(static item => item.Location is not null).ToArray();
        if (locations.Length != 1)
        {
            throw new InvalidOperationException(
                "A workspace requires exactly one of --xml-path, --csharp-path, or --sql-connection-env.");
        }

        if (model.WorkspaceList.Any(item => string.Equals(item.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Workspace '{normalizedName}' already exists.");
        }

        var workspace = new MetaMesh.Workspace
        {
            Id = "workspace:" + NormalizeToken(normalizedName),
            Mesh = mesh,
            Name = normalizedName,
            ModelName = NormalizeOptional(modelName),
            Description = NormalizeOptional(description)
        };

        RequireUniqueId(model.WorkspaceList, workspace.Id, "Workspace");
        model.WorkspaceList.Add(workspace);
        switch (locations[0].Surface)
        {
            case "xml":
                var xmlWorkspace = new MetaMesh.XmlWorkspace
                {
                    Id = "xml-workspace:" + NormalizeToken(normalizedName),
                    Workspace = workspace,
                    Path = locations[0].Location!
                };
                RequireUniqueId(model.XmlWorkspaceList, xmlWorkspace.Id, "XmlWorkspace");
                model.XmlWorkspaceList.Add(xmlWorkspace);
                break;
            case "csharp":
                var csharpWorkspace = new MetaMesh.CSharpWorkspace
                {
                    Id = "csharp-workspace:" + NormalizeToken(normalizedName),
                    Workspace = workspace,
                    Path = locations[0].Location!
                };
                RequireUniqueId(model.CSharpWorkspaceList, csharpWorkspace.Id, "CSharpWorkspace");
                model.CSharpWorkspaceList.Add(csharpWorkspace);
                break;
            case "sql":
                var sqlWorkspace = new MetaMesh.SqlWorkspace
                {
                    Id = "sql-workspace:" + NormalizeToken(normalizedName),
                    Workspace = workspace,
                    ConnectionEnvironmentVariable = locations[0].Location!
                };
                RequireUniqueId(model.SqlWorkspaceList, sqlWorkspace.Id, "SqlWorkspace");
                model.SqlWorkspaceList.Add(sqlWorkspace);
                break;
        }

        var resolvedRootPath = ResolveMeshRootPath(mesh, context);
        return ToWorkspaceSummary(ResolveWorkspace(model, workspace, resolvedRootPath));
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

    public MetaMeshOperationSummary UpdateStep(
        MetaMesh.MetaMeshModel model,
        string operationName,
        string stepName,
        MetaMeshStepUpdate update)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(update);
        var operation = RequireOperation(model, operationName);
        var step = RequireOperationStep(model, operation, stepName);

        if (!update.UpdateExecutable &&
            !update.UpdateArguments &&
            !update.UpdateWorkingDirectory &&
            !update.UpdatePreviousStep &&
            !update.UpdateExpectedExitCode &&
            !update.UpdateDescription)
        {
            throw new InvalidOperationException("At least one step field must be selected for update.");
        }

        if (update.UpdateExecutable)
        {
            step.Executable = RequiredName(update.Executable);
        }

        if (update.UpdateArguments)
        {
            step.Arguments = NormalizeOptional(update.Arguments);
        }

        if (update.UpdateWorkingDirectory)
        {
            step.WorkingDirectory = NormalizeOptional(update.WorkingDirectory);
        }

        if (update.UpdatePreviousStep)
        {
            var previousStep = string.IsNullOrWhiteSpace(update.PreviousStepName)
                ? null
                : RequireOperationStep(model, operation, update.PreviousStepName);
            if (ReferenceEquals(previousStep, step))
            {
                throw new InvalidOperationException($"Step '{step.Name}' cannot be its own previous step.");
            }

            var originalPreviousStep = step.PreviousStep;
            step.PreviousStep = previousStep;
            try
            {
                OrderOperationSteps(model, operation, strict: true);
            }
            catch
            {
                step.PreviousStep = originalPreviousStep;
                throw;
            }
        }

        if (update.UpdateExpectedExitCode)
        {
            step.ExpectedExitCode = NormalizeExpectedExitCode(update.ExpectedExitCode);
        }

        if (update.UpdateDescription)
        {
            step.Description = NormalizeOptional(update.Description);
        }

        return ToOperationSummary(model, operation);
    }

    public MetaMeshOperationSummary RemoveStep(
        MetaMesh.MetaMeshModel model,
        string operationName,
        string stepName)
    {
        ArgumentNullException.ThrowIfNull(model);
        var operation = RequireOperation(model, operationName);
        var step = RequireOperationStep(model, operation, stepName);
        var followingSteps = model.OperationStepList
            .Where(item => ReferenceEquals(item.Operation, operation) && ReferenceEquals(item.PreviousStep, step))
            .ToArray();
        if (followingSteps.Length > 1)
        {
            throw new InvalidOperationException(
                $"Cannot remove step '{step.Name}' because multiple steps refer to it as their predecessor.");
        }

        if (followingSteps.Length == 1)
        {
            followingSteps[0].PreviousStep = step.PreviousStep;
        }

        model.OperationStepList.Remove(step);
        return ToOperationSummary(model, operation);
    }

    public MetaMeshRunResult RunOperation(
        MetaMesh.MetaMeshModel model,
        string operationName,
        MetaMeshWorkspaceContext context,
        IMetaMeshRunObserver? observer = null,
        bool attachToConsole = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(context);
        var mesh = RequireMesh(model);
        var operation = RequireOperation(model, operationName);
        var resolvedRootPath = ResolveMeshRootPath(mesh, context);
        var workspaceTokens = BuildWorkspaceTokens(model, resolvedRootPath);
        var stepResults = new List<MetaMeshRunStepResult>();
        var plan = BuildRunPlan(model, operation, context, resolvedRootPath, workspaceTokens);

        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            observer?.StepStarted(new MetaMeshRunStepStart(
                i + 1,
                plan.Steps.Count,
                step.Name,
                FormatCommand(step.Executable, step.Arguments),
                step.WorkingDirectory));
            var result = RunProcess(
                step.Name,
                step.Executable,
                step.Arguments,
                step.WorkingDirectory,
                step.ExpectedExitCode,
                attachToConsole);
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
        MetaMeshWorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(context);
        var mesh = RequireMesh(model);
        var operation = RequireOperation(model, operationName);
        var resolvedRootPath = ResolveMeshRootPath(mesh, context);
        var workspaceTokens = BuildWorkspaceTokens(model, resolvedRootPath);
        var plan = BuildRunPlan(model, operation, context, resolvedRootPath, workspaceTokens);
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
        MetaMeshWorkspaceContext context,
        string resolvedRootPath,
        IReadOnlyDictionary<string, ResolvedWorkspace> workspaceTokens)
    {
        RequireDirectory(resolvedRootPath, "Mesh root");

        var steps = OrderOperationSteps(model, operation, strict: true);
        if (steps.Count == 0)
        {
            throw new InvalidOperationException($"Operation '{operation.Name}' has no steps.");
        }

        foreach (var step in steps)
        {
            ValidateTokens(step.Executable, workspaceTokens);
            ValidateTokens(step.Arguments, workspaceTokens);
            ValidateTokens(step.WorkingDirectory, workspaceTokens, requireFileSystemLocation: true);
        }

        var workspaceIssues = CollectOperationWorkspaceIssues(steps, workspaceTokens);
        if (workspaceIssues.Count > 0)
        {
            throw new MetaMeshWorkspaceIssueException(workspaceIssues);
        }

        var plannedSteps = new List<MetaMeshPlannedStep>();
        foreach (var step in steps)
        {
            var executable = ExpandTokens(step.Executable, context.WorkspaceLocation, resolvedRootPath, workspaceTokens);
            var arguments = ExpandTokens(step.Arguments ?? string.Empty, context.WorkspaceLocation, resolvedRootPath, workspaceTokens);
            var workingDirectory = ResolveWorkingDirectory(
                ExpandTokens(step.WorkingDirectory ?? string.Empty, context.WorkspaceLocation, resolvedRootPath, workspaceTokens),
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
        int expectedExitCode,
        bool attachToConsole)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = !attachToConsole,
            RedirectStandardError = !attachToConsole,
            UseShellExecute = false,
            CreateNoWindow = !attachToConsole,
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"Could not start operation step '{stepName}'.");
        var stdout = attachToConsole ? null : process.StandardOutput.ReadToEndAsync();
        var stderr = attachToConsole ? null : process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        var output = attachToConsole
            ? string.Empty
            : stdout!.GetAwaiter().GetResult() + stderr!.GetAwaiter().GetResult();
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
        ResolvedWorkspace workspace)
    {
        return new MetaMeshWorkspaceSummary(
            workspace.Workspace.Name,
            workspace.Surface,
            workspace.Location,
            workspace.ResolvedLocation,
            workspace.Workspace.ModelName ?? string.Empty,
            workspace.Workspace.Description ?? string.Empty);
    }

    private static IReadOnlyList<MetaMeshWorkspaceIssue> CollectWorkspaceIssues(
        IReadOnlyList<ResolvedWorkspace> workspaces)
    {
        var issues = new List<MetaMeshWorkspaceIssue>();
        foreach (var workspace in workspaces.OrderBy(static item => item.Workspace.Name, StringComparer.OrdinalIgnoreCase))
        {
            var reason = GetWorkspaceIssueReason(workspace);
            if (reason is null)
            {
                continue;
            }

            issues.Add(new MetaMeshWorkspaceIssue(
                workspace.Workspace.Name,
                workspace.Surface,
                workspace.Location,
                workspace.ResolvedLocation,
                workspace.Workspace.ModelName ?? string.Empty,
                reason));
        }

        return issues;
    }

    private static string? GetWorkspaceIssueReason(ResolvedWorkspace workspace)
    {
        if (workspace is ResolvedXmlWorkspace xml)
        {
            return GetXmlWorkspaceIssueReason(xml.ResolvedLocation);
        }

        try
        {
            if (workspace is ResolvedCSharpWorkspace csharp)
            {
                var opened = SurfaceCSharpWorkspace.OpenAsync(csharp.ResolvedLocation)
                    .GetAwaiter()
                    .GetResult();
                opened.DisposeAsync().AsTask().GetAwaiter().GetResult();
                return null;
            }

            if (workspace is ResolvedSqlWorkspace sql)
            {
                var connectionString = ConnectionEnvironmentVariableResolver.ResolveRequired(sql.Location);
                var opened = SurfaceSqlWorkspace.OpenAsync(connectionString)
                    .GetAwaiter()
                    .GetResult();
                opened.DisposeAsync().AsTask().GetAwaiter().GetResult();
                return null;
            }
        }
        catch (Exception exception)
        {
            return "workspace could not be opened: " + exception.Message;
        }

        throw new InvalidOperationException(
            $"Unsupported workspace representation '{workspace.GetType().Name}'.");
    }

    private static string? GetXmlWorkspaceIssueReason(string path)
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

        return File.Exists(Path.Combine(path, "workspace.meta"))
            ? null
            : "workspace.meta is missing";
    }

    private static IReadOnlyList<MetaMeshWorkspaceIssue> CollectOperationWorkspaceIssues(
        IReadOnlyList<MetaMesh.OperationStep> steps,
        IReadOnlyDictionary<string, ResolvedWorkspace> workspaces)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            AddWorkspaceTokenNames(names, step.Executable);
            AddWorkspaceTokenNames(names, step.Arguments);
            AddWorkspaceTokenNames(names, step.WorkingDirectory);
        }

        var issues = new List<MetaMeshWorkspaceIssue>();
        foreach (var name in names.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
        {
            if (!workspaces.TryGetValue(name, out var workspace))
            {
                continue;
            }

            var reason = GetWorkspaceIssueReason(workspace);
            if (reason is null)
            {
                continue;
            }

            issues.Add(new MetaMeshWorkspaceIssue(
                workspace.Workspace.Name,
                workspace.Surface,
                workspace.Location,
                workspace.ResolvedLocation,
                workspace.Workspace.ModelName ?? string.Empty,
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

        foreach (var token in FindWorkspaceTokens(value))
        {
            names.Add(token.Name);
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

    private static IReadOnlyDictionary<string, ResolvedWorkspace> BuildWorkspaceTokens(
        MetaMesh.MetaMeshModel model,
        string resolvedRootPath)
    {
        var result = new Dictionary<string, ResolvedWorkspace>(StringComparer.OrdinalIgnoreCase);
        foreach (var workspace in ResolveWorkspaces(model, resolvedRootPath))
        {
            if (!result.TryAdd(workspace.Workspace.Name, workspace))
            {
                throw new InvalidOperationException(
                    $"Workspace name '{workspace.Workspace.Name}' is declared more than once.");
            }
        }

        return result;
    }

    private static IReadOnlyList<ResolvedWorkspace> ResolveWorkspaces(
        MetaMesh.MetaMeshModel model,
        string resolvedRootPath) =>
        model.WorkspaceList
            .Select(workspace => ResolveWorkspace(model, workspace, resolvedRootPath))
            .ToArray();

    private static ResolvedWorkspace ResolveWorkspace(
        MetaMesh.MetaMeshModel model,
        MetaMesh.Workspace workspace,
        string resolvedRootPath)
    {
        var xml = model.XmlWorkspaceList
            .Where(item => ReferenceEquals(item.Workspace, workspace))
            .ToArray();
        var csharp = model.CSharpWorkspaceList
            .Where(item => ReferenceEquals(item.Workspace, workspace))
            .ToArray();
        var sql = model.SqlWorkspaceList
            .Where(item => ReferenceEquals(item.Workspace, workspace))
            .ToArray();
        var count = xml.Length + csharp.Length + sql.Length;
        if (count != 1)
        {
            throw new InvalidOperationException(
                $"Workspace '{workspace.Name}' must have exactly one XML, C#, or SQL representation; found {count}.");
        }

        if (xml.Length == 1)
        {
            return new ResolvedXmlWorkspace(
                workspace,
                xml[0].Path,
                ResolveFileWorkspacePath(xml[0].Path, resolvedRootPath));
        }

        if (csharp.Length == 1)
        {
            return new ResolvedCSharpWorkspace(
                workspace,
                csharp[0].Path,
                ResolveFileWorkspacePath(csharp[0].Path, resolvedRootPath));
        }

        return new ResolvedSqlWorkspace(
            workspace,
            sql[0].ConnectionEnvironmentVariable);
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

    private static string ResolveMeshRootPath(
        MetaMesh.Mesh mesh,
        MetaMeshWorkspaceContext context)
    {
        var basePath = !string.IsNullOrWhiteSpace(context.WorkspaceDirectory)
            ? Path.GetFullPath(context.WorkspaceDirectory)
            : Path.GetFullPath(context.CurrentDirectory);
        if (string.IsNullOrWhiteSpace(mesh.RootPath))
        {
            return basePath;
        }

        return Path.IsPathRooted(mesh.RootPath)
            ? Path.GetFullPath(mesh.RootPath)
            : Path.GetFullPath(Path.Combine(basePath, mesh.RootPath));
    }

    private static string ResolveFileWorkspacePath(string path, string resolvedRootPath) =>
        Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(resolvedRootPath, path));

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
        string meshWorkspaceLocation,
        string resolvedRootPath,
        IReadOnlyDictionary<string, ResolvedWorkspace> workspaces)
    {
        var result = ReplaceOrdinalIgnoreCase(value, "{mesh.workspace}", meshWorkspaceLocation);
        result = ReplaceOrdinalIgnoreCase(result, "{mesh.root}", resolvedRootPath);
        foreach (var token in FindWorkspaceTokens(result).ToArray())
        {
            var workspace = workspaces[token.Name];
            var replacement = token.Member switch
            {
                "location" => workspace.ResolvedLocation,
                "surface" => workspace.Surface,
                _ => throw new InvalidOperationException(
                    $"Workspace token '{token.Text}' has unsupported member '{token.Member}'.")
            };
            result = ReplaceOrdinalIgnoreCase(result, token.Text, replacement);
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
        IReadOnlyDictionary<string, ResolvedWorkspace> workspaces,
        bool requireFileSystemLocation = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var token in FindWorkspaceTokens(value))
        {
            if (!workspaces.TryGetValue(token.Name, out var workspace))
            {
                throw new InvalidOperationException(
                    $"Workspace token '{token.Text}' references undeclared workspace '{token.Name}'.");
            }

            if (token.Member is not ("location" or "surface"))
            {
                throw new InvalidOperationException(
                    $"Workspace token '{token.Text}' must select .location or .surface.");
            }

            if (requireFileSystemLocation &&
                token.Member == "location" &&
                workspace is ResolvedSqlWorkspace)
            {
                throw new InvalidOperationException(
                    $"SQL workspace '{token.Name}' cannot be used as an operation working directory.");
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

    private static IEnumerable<WorkspaceToken> FindWorkspaceTokens(string value)
    {
        const string prefix = "{workspace:";
        var startIndex = 0;
        while (true)
        {
            var index = value.IndexOf(prefix, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                yield break;
            }

            var nameStart = index + prefix.Length;
            var endIndex = value.IndexOf('}', nameStart);
            if (endIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Workspace token starting at character {index} is not closed.");
            }

            var selector = value[nameStart..endIndex].Trim();
            var memberSeparator = selector.LastIndexOf('.');
            var name = memberSeparator < 0 ? selector : selector[..memberSeparator].Trim();
            var member = memberSeparator < 0
                ? string.Empty
                : selector[(memberSeparator + 1)..].Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return new WorkspaceToken(
                    name,
                    member,
                    value[index..(endIndex + 1)]);
            }

            startIndex = endIndex + 1;
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

    private abstract record ResolvedWorkspace(
        MetaMesh.Workspace Workspace,
        string Location)
    {
        public abstract string Surface { get; }

        public abstract string ResolvedLocation { get; }
    }

    private sealed record ResolvedXmlWorkspace(
        MetaMesh.Workspace Workspace,
        string Location,
        string Path)
        : ResolvedWorkspace(Workspace, Location)
    {
        public override string Surface => "xml";

        public override string ResolvedLocation => Path;
    }

    private sealed record ResolvedCSharpWorkspace(
        MetaMesh.Workspace Workspace,
        string Location,
        string Path)
        : ResolvedWorkspace(Workspace, Location)
    {
        public override string Surface => "csharp";

        public override string ResolvedLocation => Path;
    }

    private sealed record ResolvedSqlWorkspace(
        MetaMesh.Workspace Workspace,
        string Location)
        : ResolvedWorkspace(Workspace, Location)
    {
        public override string Surface => "sql";

        public override string ResolvedLocation => Location;
    }

    private sealed record WorkspaceToken(
        string Name,
        string Member,
        string Text);
}
