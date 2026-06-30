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
        WriteWorkspaces(new[] { summary });
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
        Presenter.WriteKeyValueBlock("MetaMesh", new[]
        {
            ("Name", result.MeshName),
            ("Root", result.RootPath),
            ("ResolvedRoot", result.ResolvedRootPath),
            ("Workspaces", result.Workspaces.Count.ToString()),
            ("Operations", result.Operations.Count.ToString()),
        });

        if (verbose)
        {
            WriteWorkspaces(result.Workspaces);
            WriteOperations(result.Operations, verbose: true);
            return;
        }

        WriteOperations(result.Operations, verbose: false);
        Presenter.WriteInfo("Use --verbose to list workspace paths and step commands.");
    }

    private static void WriteWorkspaces(IReadOnlyList<MetaMeshWorkspaceSummary> workspaces)
    {
        Presenter.WriteInfo("Workspaces:");
        if (workspaces.Count == 0)
        {
            Presenter.WriteInfo("  (none)");
            return;
        }

        foreach (var workspace in workspaces)
        {
            Presenter.WriteInfo($"  {workspace.Name}");
            if (!string.IsNullOrWhiteSpace(workspace.ModelName))
            {
                Presenter.WriteInfo($"    model: {workspace.ModelName}");
            }

            Presenter.WriteInfo($"    path: {workspace.Path}");
            Presenter.WriteInfo($"    resolved: {workspace.ResolvedPath}");
            if (!string.IsNullOrWhiteSpace(workspace.Description))
            {
                Presenter.WriteInfo($"    description: {workspace.Description}");
            }
        }
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
                Presenter.WriteInfo($"    steps: {operation.Steps.Count}");
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
}
