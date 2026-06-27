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
            .Bind("exec-scan", RunScan)
            .Bind("exec-suggest", RunSuggest)
            .Bind("exec-show", RunShow)
            .Bind("exec-check", RunCheck)
            .Bind("exec-impact", RunImpact)
            .Bind("exec-mount", RunMount)
            .Bind("exec-link", RunLink);

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

        Service.CreateEmpty(invocation.Optional("name") ?? "Mesh").SaveToXmlWorkspace(workspacePath);
        Presenter.WriteOk(
            "mesh workspace created",
            ("Workspace", workspacePath),
            ("Model", "MetaMesh"));
    }

    private static void RunScan(MetaCliInvocation invocation)
    {
        var root = invocation.Required("root");
        var workspacePath = Path.GetFullPath(invocation.Required("new-workspace"));
        if (Directory.Exists(workspacePath) && Directory.EnumerateFileSystemEntries(workspacePath).Any())
        {
            throw new InvalidOperationException($"Target directory '{workspacePath}' must be empty.");
        }

        var result = Service.ScanToWorkspace(root, workspacePath, invocation.Optional("name") ?? "Mesh");
        Presenter.WriteOk("mesh scan", ("Workspace", workspacePath), ("Root", Path.GetFullPath(root)));
        WriteWorkspaces(result.Workspaces);
        WriteSuggestions(result.Suggestions);
    }

    private static void RunSuggest(MetaCliInvocation invocation)
    {
        var result = Service.SuggestFromRoot(invocation.Required("root"));
        WriteSuggestions(result.Suggestions);
    }

    private static void RunShow(MetaCliInvocation invocation, MetaMeshModel model)
    {
        var result = Service.Show(model);
        Presenter.WriteKeyValueBlock("MetaMesh", new[]
        {
            ("Name", result.MeshName),
            ("Root", result.RootPath),
            ("Workspaces", result.Workspaces.Count.ToString()),
            ("Links", result.Links.Count.ToString()),
            ("Suggestions", result.Suggestions.Count.ToString()),
        });
        WriteWorkspaces(result.Workspaces);
        WriteLinks(result.Links);
        WriteSuggestions(result.Suggestions);
    }

    private static void RunCheck(MetaCliInvocation invocation, MetaMeshModel model)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        var result = Service.Check(model, workspacePath);
        if (result.Issues.Count == 0)
        {
            Presenter.WriteInfo("MetaMesh check: Ok");
            Presenter.WriteKeyValueBlock("Summary", new[]
            {
                ("Workspaces", model.WorkspaceInstanceList.Count.ToString()),
                ("Links", model.WorkspaceLinkList.Count.ToString()),
                ("Warnings", "0"),
                ("Errors", "0"),
            });
            return;
        }

        Presenter.WriteInfo("MetaMesh check: issues");
        foreach (var issue in result.Issues)
        {
            var handle = string.IsNullOrWhiteSpace(issue.WorkspaceHandle)
                ? string.Empty
                : $" [{issue.WorkspaceHandle}]";
            Presenter.WriteInfo($"  {issue.Severity} {issue.Code}{handle}: {issue.Message}");
        }

        if (result.HasErrors)
        {
            throw new InvalidOperationException("MetaMesh check found errors.");
        }
    }

    private static void RunImpact(MetaCliInvocation invocation, MetaMeshModel model)
    {
        var result = Service.Impact(model, invocation.Required("handle"));
        Presenter.WriteKeyValueBlock("Impact", new[]
        {
            ("Workspace", result.WorkspaceHandle),
            ("AffectedWorkspaces", result.AffectedHandles.Count.ToString()),
        });

        if (result.AffectedHandles.Count > 0)
        {
            Presenter.WriteInfo("Affected handles:");
            foreach (var affected in result.AffectedHandles)
            {
                Presenter.WriteInfo("  " + affected);
            }
        }

        WriteLinks(result.AffectedLinks);
    }

    private static void RunMount(MetaCliInvocation invocation, MetaMeshModel model)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        var summary = Service.Mount(
            model,
            invocation.Required("handle"),
            invocation.Required("path"));
        model.SaveToXmlWorkspace(workspacePath);

        Presenter.WriteOk("mesh mount", ("Workspace", workspacePath));
        WriteWorkspaces(new[] { summary });
    }

    private static void RunLink(MetaCliInvocation invocation, MetaMeshModel model)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        var summary = Service.Link(
            model,
            invocation.Required("from"),
            invocation.Required("to"),
            invocation.Required("kind"));
        model.SaveToXmlWorkspace(workspacePath);

        Presenter.WriteOk("mesh link", ("Workspace", workspacePath));
        WriteLinks(new[] { summary });
    }

    private static string ResolveWorkspacePath(MetaCliInvocation invocation)
    {
        var workspace = invocation.Optional("workspace");
        return Path.GetFullPath(string.IsNullOrWhiteSpace(workspace)
            ? Directory.GetCurrentDirectory()
            : workspace);
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
            var model = string.IsNullOrWhiteSpace(workspace.ModelName) ? "(unknown model)" : workspace.ModelName;
            Presenter.WriteInfo($"  {workspace.Handle}  {model}");
        }
    }

    private static void WriteLinks(IReadOnlyList<MetaMeshLinkSummary> links)
    {
        if (links.Count == 0)
        {
            return;
        }

        Presenter.WriteInfo("Links:");
        foreach (var link in links)
        {
            var description = string.IsNullOrWhiteSpace(link.Description) ? string.Empty : "  " + link.Description;
            Presenter.WriteInfo($"  {link.FromHandle} --{link.Kind}--> {link.ToHandle}{description}");
        }
    }

    private static void WriteSuggestions(IReadOnlyList<MetaMeshSuggestionSummary> suggestions)
    {
        Presenter.WriteInfo("Suggestions:");
        if (suggestions.Count == 0)
        {
            Presenter.WriteInfo("  (none)");
            return;
        }

        foreach (var suggestion in suggestions)
        {
            var handle = string.IsNullOrWhiteSpace(suggestion.WorkspaceHandle)
                ? string.Empty
                : $" [{suggestion.WorkspaceHandle}]";
            Presenter.WriteInfo($"  {suggestion.Severity}{handle}: {suggestion.Message}");
        }
    }
}
