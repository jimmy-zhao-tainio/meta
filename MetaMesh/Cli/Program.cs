using Meta.Core.Presentation;
using MetaCli.Core;
using MetaMesh.Core;
using MetaMeshModel = global::MetaMesh.MetaMeshModel;

internal static class Program
{
    private const string ApplicationId = "app-meta-mesh";
    private const string CommandWorkspaceDirectoryName = "meta-mesh.MetaCli";
    private static readonly ConsolePresenter Presenter = new();
    private static readonly MetaMeshWorkspaceService Service = new();

    private static int Main(string[] args)
    {
        if (Meta.Core.Presentation.Cli.CliVersion.TryWriteVersion(Presenter, "meta-mesh", args, out var versionExitCode))
        {
            return versionExitCode;
        }

        Environment.ExitCode = 0;
        var runtime = new MetaCliRuntime<MetaMeshModel>(CommandWorkspacePath, ApplicationId)
            .UseDefaultHelp()
            .Bind("exec-new-workspace", RunNewWorkspace)
            .Bind("exec-show", RunShow)
            .Bind("exec-workspaces", RunWorkspaces)
            .Bind("exec-operations", RunOperations)
            .Bind("exec-steps", RunSteps)
            .Bind("exec-validate", RunValidateOperation)
            .Bind("exec-add-workspace", RunAddWorkspace)
            .Bind("exec-add-operation", RunAddOperation)
            .Bind("exec-add-step", RunAddStep)
            .Bind("exec-run", RunOperation);

        runtime.Run(args);
        return Environment.ExitCode;
    }

    private static string CommandWorkspacePath =>
        Path.Combine(AppContext.BaseDirectory, CommandWorkspaceDirectoryName);

    private static void RunNewWorkspace(MetaCliInvocation invocation)
    {
        var workspacePath = Path.GetFullPath(invocation.Required("path"));
        if (Directory.Exists(workspacePath) && Directory.EnumerateFileSystemEntries(workspacePath).Any())
        {
            throw new InvalidOperationException($"Target directory '{workspacePath}' must be empty.");
        }

        Service.CreateEmpty(
                invocation.Optional("name") ?? "Mesh",
                invocation.Optional("root"),
                invocation.Optional("description"))
            .SaveToXmlWorkspace(workspacePath);

        Presenter.WriteOk();
        Presenter.WriteInfo($"Created MetaMesh workspace: {workspacePath}");
    }

    private static void RunShow(MetaCliInvocation invocation, MetaMeshModel model)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        WriteShow(Service.Show(model, workspacePath), invocation.Flag("verbose"));
    }

    private static void RunWorkspaces(MetaCliInvocation invocation, MetaMeshModel model)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        var result = Service.Show(model, workspacePath);
        WriteWorkspaces(result.Workspaces, result.WorkspaceIssues);
    }

    private static void RunOperations(MetaCliInvocation invocation, MetaMeshModel model)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        var result = Service.Show(model, workspacePath);
        WriteOperations(result.Operations, verbose: false);
    }

    private static void RunSteps(MetaCliInvocation invocation, MetaMeshModel model)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        var operationName = invocation.Required("operation");
        var result = Service.Show(model, workspacePath);
        var operation = result.Operations.FirstOrDefault(item =>
            string.Equals(item.Name, operationName, StringComparison.OrdinalIgnoreCase));
        if (operation is null)
        {
            throw new MetaCliExitException(2, $"Operation '{operationName}' was not found.");
        }

        WriteOperationSteps(operation);
    }

    private static void RunValidateOperation(MetaCliInvocation invocation, MetaMeshModel model)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        var operationName = invocation.Required("operation");
        try
        {
            var result = Service.ValidateOperation(model, operationName, workspacePath);
            WriteValidation(result);
        }
        catch (MetaMeshWorkspaceIssueException exception)
        {
            WriteWorkspaceIssues(exception.Issues);
            throw new MetaCliExitException(2, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw new MetaCliExitException(2, exception.Message);
        }
        catch (ArgumentException exception)
        {
            throw new MetaCliExitException(2, exception.Message);
        }
    }

    private static void RunAddWorkspace(MetaCliInvocation invocation, MetaMeshModel model)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        var summary = Service.AddWorkspace(
            model,
            invocation.Required("name"),
            invocation.Required("path"),
            invocation.Optional("model"),
            invocation.Optional("description"),
            workspacePath);
        model.SaveToXmlWorkspace(workspacePath);

        Presenter.WriteOk();
        WriteWorkspaces(new[] { summary }, Array.Empty<MetaMeshWorkspaceIssue>());
    }

    private static void RunAddOperation(MetaCliInvocation invocation, MetaMeshModel model)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        var summary = Service.AddOperation(
            model,
            invocation.Required("name"),
            invocation.Optional("description"));
        model.SaveToXmlWorkspace(workspacePath);

        Presenter.WriteOk();
        WriteOperations(new[] { summary });
    }

    private static void RunAddStep(MetaCliInvocation invocation, MetaMeshModel model)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        var summary = Service.AddStep(
            model,
            invocation.Required("operation"),
            invocation.Required("name"),
            invocation.Required("executable"),
            invocation.Optional("arguments"),
            invocation.Optional("working-directory"),
            invocation.Optional("previous-step"),
            invocation.Optional("description"));
        model.SaveToXmlWorkspace(workspacePath);

        Presenter.WriteOk();
        WriteOperations(new[] { summary });
    }

    private static void RunOperation(MetaCliInvocation invocation, MetaMeshModel model)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        var operationName = invocation.Required("operation");
        Presenter.WriteInfo($"Operation: {operationName}");

        using var progress = MetaMeshRunProgressRenderer.TryCreate();
        try
        {
            IMetaMeshRunObserver observer = progress is null
                ? new ConsoleRunObserver()
                : new ProgressRunObserver(progress);
            var result = Service.RunOperation(model, operationName, workspacePath, observer);
            progress?.Complete(failed: !result.Succeeded);
            if (!result.Succeeded)
            {
                WriteFailedStepOutput(result);
                throw new MetaCliExitException(4, $"Operation '{result.OperationName}' failed.");
            }

            WriteRunSummary(result);
        }
        catch (MetaMeshWorkspaceIssueException exception)
        {
            progress?.Complete(failed: true);
            WriteWorkspaceIssues(exception.Issues);
            throw new MetaCliExitException(2, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            progress?.Complete(failed: true);
            throw new MetaCliExitException(2, exception.Message);
        }
        catch (ArgumentException exception)
        {
            progress?.Complete(failed: true);
            throw new MetaCliExitException(2, exception.Message);
        }
        catch
        {
            progress?.Complete(failed: true);
            throw;
        }
    }

    private static string ResolveWorkspacePath(MetaCliInvocation invocation)
    {
        var workspace = invocation.Optional("workspace");
        return Path.GetFullPath(string.IsNullOrWhiteSpace(workspace)
            ? Directory.GetCurrentDirectory()
            : workspace);
    }

    private static void WriteShow(MetaMeshShowResult result, bool verbose)
    {
        Presenter.WriteInfo($"MetaMesh: {result.MeshName}");
        Presenter.WriteInfo($"Root: {FormatRoot(result.RootPath, result.ResolvedRootPath)}");
        Presenter.WriteInfo($"Workspaces: {result.Workspaces.Count} ({result.WorkspaceIssues.Count} missing)");
        Presenter.WriteInfo($"Operations: {result.Operations.Count}");

        if (verbose)
        {
            WriteWorkspaces(result.Workspaces, result.WorkspaceIssues);
            WriteOperations(result.Operations, verbose: true);
            return;
        }

        if (result.WorkspaceIssues.Count > 0)
        {
            Presenter.WriteInfo("Run `meta-mesh workspaces` to inspect missing workspaces.");
        }

        if (result.Operations.Count > 0)
        {
            Presenter.WriteInfo("Run `meta-mesh operations` to list operation names.");
        }
    }

    private static void WriteWorkspaces(
        IReadOnlyList<MetaMeshWorkspaceSummary> workspaces,
        IReadOnlyList<MetaMeshWorkspaceIssue> issues)
    {
        var issueByName = issues.ToDictionary(static item => item.Name, StringComparer.OrdinalIgnoreCase);
        Presenter.WriteInfo("Workspaces:");
        if (workspaces.Count == 0)
        {
            Presenter.WriteInfo("  (none)");
            return;
        }

        foreach (var workspace in workspaces)
        {
            var status = issueByName.ContainsKey(workspace.Name) ? "missing" : "ok";
            var header = string.IsNullOrWhiteSpace(workspace.ModelName)
                ? workspace.Name
                : $"{workspace.Name} ({workspace.ModelName})";
            Presenter.WriteInfo($"  {header} - {status}");
            Presenter.WriteInfo($"    path: {workspace.Path}");
            Presenter.WriteInfo($"    resolved: {workspace.ResolvedPath}");
            if (issueByName.TryGetValue(workspace.Name, out var issue))
            {
                Presenter.WriteInfo($"    problem: {issue.Reason}");
            }

            if (!string.IsNullOrWhiteSpace(workspace.Description))
            {
                Presenter.WriteInfo($"    description: {workspace.Description}");
            }
        }
    }

    private static void WriteWorkspaceIssues(IReadOnlyList<MetaMeshWorkspaceIssue> issues)
    {
        if (issues.Count == 0)
        {
            return;
        }

        Presenter.WriteInfo("Missing workspaces:");
        foreach (var issue in issues)
        {
            Presenter.WriteInfo($"  {issue.Name}");
            if (!string.IsNullOrWhiteSpace(issue.ModelName))
            {
                Presenter.WriteInfo($"    model: {issue.ModelName}");
            }

            Presenter.WriteInfo($"    path: {issue.Path}");
            Presenter.WriteInfo($"    resolved: {issue.ResolvedPath}");
            Presenter.WriteInfo($"    reason: {issue.Reason}");
        }
    }

    private static void WriteOperationSteps(MetaMeshOperationSummary operation)
    {
        Presenter.WriteInfo($"Operation: {operation.Name}");
        if (!string.IsNullOrWhiteSpace(operation.Description))
        {
            Presenter.WriteInfo(operation.Description);
        }

        Presenter.WriteInfo("Steps:");
        if (operation.Steps.Count == 0)
        {
            Presenter.WriteInfo("  (none)");
            return;
        }

        for (var index = 0; index < operation.Steps.Count; index++)
        {
            var step = operation.Steps[index];
            var command = string.IsNullOrWhiteSpace(step.Arguments)
                ? step.Executable
                : step.Executable + " " + step.Arguments;
            Presenter.WriteInfo($"  {index + 1}. {step.Name}");
            Presenter.WriteInfo($"     {command}");
            if (!string.IsNullOrWhiteSpace(step.WorkingDirectory))
            {
                Presenter.WriteInfo($"     in {step.WorkingDirectory}");
            }

            if (!string.IsNullOrWhiteSpace(step.Description))
            {
                Presenter.WriteInfo($"     {step.Description}");
            }
        }
    }

    private static void WriteValidation(MetaMeshValidationResult result)
    {
        Presenter.WriteInfo($"Operation: {result.OperationName}");
        Presenter.WriteInfo("Validation: OK");
        Presenter.WriteInfo($"{result.Steps.Count} {Pluralize(result.Steps.Count, "step", "steps")} ready.");
    }

    private static void WriteOperations(IReadOnlyList<MetaMeshOperationSummary> operations, bool verbose = true)
    {
        Presenter.WriteInfo("Operations:");
        if (operations.Count == 0)
        {
            Presenter.WriteInfo("  (none)");
            return;
        }

        foreach (var operation in operations)
        {
            Presenter.WriteInfo($"  {operation.Name}");
            if (!string.IsNullOrWhiteSpace(operation.Description))
            {
                Presenter.WriteInfo($"    description: {operation.Description}");
            }

            if (!verbose)
            {
                Presenter.WriteInfo($"    {operation.Steps.Count} {Pluralize(operation.Steps.Count, "step", "steps")}");
                continue;
            }

            if (operation.Steps.Count > 0)
            {
                Presenter.WriteInfo("    steps:");
                foreach (var step in operation.Steps)
                {
                    var command = string.IsNullOrWhiteSpace(step.Arguments)
                        ? step.Executable
                        : step.Executable + " " + step.Arguments;
                    Presenter.WriteInfo($"      {step.Name}: {command}");
                    if (!string.IsNullOrWhiteSpace(step.WorkingDirectory))
                    {
                        Presenter.WriteInfo($"        working-directory: {step.WorkingDirectory}");
                    }
                }
            }
        }
    }

    private sealed class ConsoleRunObserver : IMetaMeshRunObserver
    {
        public void StepStarted(MetaMeshRunStepStart step)
        {
            Presenter.WriteInfo($"  {step.Name}");
        }

        public void StepCompleted(MetaMeshRunStepResult step)
        {
            if (!string.IsNullOrWhiteSpace(step.Output))
            {
                foreach (var line in step.Output.Replace("\r\n", "\n").Split('\n'))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        Presenter.WriteInfo("      " + line);
                    }
                }
            }
        }
    }

    private sealed class ProgressRunObserver(MetaMeshRunProgressRenderer progress) : IMetaMeshRunObserver
    {
        public void StepStarted(MetaMeshRunStepStart step)
        {
            progress.StepStarted(step.Index, step.Total, step.Name);
        }

        public void StepCompleted(MetaMeshRunStepResult step)
        {
            progress.StepCompleted(step.Name, step.ExitCode == 0);
        }
    }

    private static void WriteRunSummary(MetaMeshRunResult result)
    {
        Presenter.WriteInfo($"{result.Steps.Count} {Pluralize(result.Steps.Count, "step", "steps")} completed.");
    }

    private static void WriteFailedStepOutput(MetaMeshRunResult result)
    {
        var failed = result.Steps.FirstOrDefault(static step => step.ExitCode != 0);
        if (failed is null)
        {
            return;
        }

        Presenter.WriteInfo($"Failed step: {failed.Name}");
        if (string.IsNullOrWhiteSpace(failed.Output))
        {
            return;
        }

        foreach (var line in failed.Output.Replace("\r\n", "\n").Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                Presenter.WriteInfo("  " + line);
            }
        }
    }

    private static string Pluralize(int count, string singular, string plural) =>
        count == 1 ? singular : plural;

    private static string FormatRoot(string rootPath, string resolvedRootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) ||
            string.Equals(rootPath, resolvedRootPath, StringComparison.OrdinalIgnoreCase))
        {
            return resolvedRootPath;
        }

        return $"{rootPath} -> {resolvedRootPath}";
    }
}
