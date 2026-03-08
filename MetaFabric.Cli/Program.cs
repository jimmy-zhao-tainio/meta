using Meta.Core.Domain;
using Meta.Core.Presentation;
using Meta.Core.Services;
using MetaFabric.Core;

internal static class Program
{
    private static readonly ConsolePresenter Presenter = new();

    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || IsHelpToken(args[0]))
        {
            PrintHelp();
            return 0;
        }

        if (string.Equals(args[0], "init", StringComparison.OrdinalIgnoreCase))
        {
            return await RunInitAsync(args).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "suggest", StringComparison.OrdinalIgnoreCase))
        {
            return await RunSuggestAsync(args).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "check", StringComparison.OrdinalIgnoreCase))
        {
            return await RunCheckAsync(args).ConfigureAwait(false);
        }

        Presenter.WriteFailure($"unknown command '{args[0]}'.", new[] { "Next: meta-fabric help" });
        return 1;
    }

    private static async Task<int> RunInitAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintInitHelp();
            return 0;
        }

        var parseResult = ParseNewWorkspaceOnly(args, 1);
        if (!parseResult.Ok)
        {
            Presenter.WriteFailure(parseResult.ErrorMessage, new[] { "Next: meta-fabric init --help" });
            return 1;
        }

        var workspacePath = Path.GetFullPath(parseResult.NewWorkspacePath);
        if (Directory.Exists(workspacePath) && Directory.EnumerateFileSystemEntries(workspacePath).Any())
        {
            Presenter.WriteFailure($"target directory '{workspacePath}' must be empty.", new[] { "Next: choose a new folder or empty the target directory and retry." });
            return 4;
        }

        Directory.CreateDirectory(workspacePath);

        var workspace = MetaFabricWorkspaces.CreateEmptyMetaFabricWorkspace(workspacePath);
        var validation = new ValidationService().Validate(workspace);
        if (validation.HasErrors)
        {
            Presenter.WriteFailure("metafabric workspace is invalid.", validation.Issues
                .Where(issue => issue.Severity == IssueSeverity.Error)
                .Select(issue => $"  - {issue.Code}: {issue.Message}")
                .Concat(new[] { "Next: fix the sanctioned model and retry init." }));
            return 4;
        }

        await new WorkspaceService().SaveAsync(workspace).ConfigureAwait(false);

        Presenter.WriteOk("fabric init", ("Workspace", workspacePath), ("Model", workspace.Model.Name));
        return 0;
    }

    private static async Task<int> RunSuggestAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintSuggestHelp();
            return 0;
        }

        var parseResult = ParseWorkspaceOnly(args, 1);
        if (!parseResult.Ok)
        {
            Presenter.WriteFailure(parseResult.ErrorMessage, new[] { "Next: meta-fabric suggest --help" });
            return 1;
        }

        var workspacePath = Path.GetFullPath(parseResult.WorkspacePath);
        try
        {
            var workspaceService = new WorkspaceService();
            var workspace = await workspaceService.LoadAsync(workspacePath, searchUpward: false).ConfigureAwait(false);
            var result = await new MetaFabricSuggestService(workspaceService).SuggestAsync(workspace).ConfigureAwait(false);

            Presenter.WriteOk(
                "fabric suggest",
                ("Workspace", workspacePath),
                ("Suggestions", result.SuggestionCount.ToString()),
                ("WeakSuggestions", result.WeakSuggestionCount.ToString()));
            Presenter.WriteInfo(string.Empty);
            Presenter.WriteInfo("Scope suggestions");
            if (result.Suggestions.Count == 0)
            {
                Presenter.WriteInfo("  (none)");
            }
            else
            {
                for (var index = 0; index < result.Suggestions.Count; index++)
                {
                    var suggestion = result.Suggestions[index];
                    Presenter.WriteInfo($"  {index + 1}) {suggestion.ChildBindingReferenceName} -> {suggestion.ParentBindingReferenceName} (source parent: {suggestion.SourceParentReferenceName}, target parent: {suggestion.TargetParentReferenceName})");
                }
            }

            Presenter.WriteInfo(string.Empty);
            Presenter.WriteInfo("Weak scope suggestions");
            if (result.WeakSuggestions.Count == 0)
            {
                Presenter.WriteInfo("  (none)");
            }
            else
            {
                var weakIndex = 1;
                foreach (var weakSuggestion in result.WeakSuggestions)
                {
                    foreach (var candidate in weakSuggestion.Candidates)
                    {
                        Presenter.WriteInfo($"  {weakIndex}) {candidate.ChildBindingReferenceName} -> {candidate.ParentBindingReferenceName} (source parent: {candidate.SourceParentReferenceName}, target parent: {candidate.TargetParentReferenceName})");
                        weakIndex++;
                    }
                }
            }

            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Presenter.WriteFailure(ex.Message, new[] { "Next: fix the fabric workspace or referenced weave workspaces and retry." });
            return 4;
        }
    }

    private static async Task<int> RunCheckAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintCheckHelp();
            return 0;
        }

        var parseResult = ParseWorkspaceOnly(args, 1);
        if (!parseResult.Ok)
        {
            Presenter.WriteFailure(parseResult.ErrorMessage, new[] { "Next: meta-fabric check --help" });
            return 1;
        }

        var workspacePath = Path.GetFullPath(parseResult.WorkspacePath);
        try
        {
            var workspaceService = new WorkspaceService();
            var workspace = await workspaceService.LoadAsync(workspacePath, searchUpward: false).ConfigureAwait(false);
            var result = await new MetaFabricService(workspaceService).CheckAsync(workspace).ConfigureAwait(false);
            if (result.HasErrors)
            {
                Presenter.WriteFailure(
                    "fabric check failed.",
                    result.Bindings.SelectMany(binding => binding.Errors.Select(error => $"  - {binding.BindingName}: {error}"))
                        .Concat(new[] { "Next: fix the reported bindings or scope requirements and retry meta-fabric check." }));
                return 2;
            }

            Presenter.WriteOk(
                "fabric check",
                ("Workspace", workspacePath),
                ("Weaves", result.WeaveCount.ToString()),
                ("Bindings", result.BindingCount.ToString()),
                ("ResolvedRows", result.ResolvedRowCount.ToString()),
                ("Errors", result.ErrorCount.ToString()));
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Presenter.WriteFailure(ex.Message, new[] { "Next: fix the fabric workspace or referenced weave workspaces and retry." });
            return 4;
        }
    }

    private static (bool Ok, string WorkspacePath, string ErrorMessage) ParseWorkspaceOnly(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (!string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                return (false, workspacePath, $"unknown option '{arg}'.");
            }

            if (i + 1 >= args.Length)
            {
                return (false, workspacePath, "missing value for --workspace.");
            }

            if (!string.IsNullOrWhiteSpace(workspacePath))
            {
                return (false, workspacePath, "--workspace can only be provided once.");
            }

            workspacePath = args[++i];
        }

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return (false, string.Empty, "missing required option --workspace <path>.");
        }

        return (true, workspacePath, string.Empty);
    }

    private static (bool Ok, string NewWorkspacePath, string ErrorMessage) ParseNewWorkspaceOnly(string[] args, int startIndex)
    {
        var newWorkspacePath = string.Empty;
        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (!string.Equals(arg, "--new-workspace", StringComparison.OrdinalIgnoreCase))
            {
                return (false, newWorkspacePath, $"unknown option '{arg}'.");
            }

            if (i + 1 >= args.Length)
            {
                return (false, newWorkspacePath, "missing value for --new-workspace.");
            }

            if (!string.IsNullOrWhiteSpace(newWorkspacePath))
            {
                return (false, newWorkspacePath, "--new-workspace can only be provided once.");
            }

            newWorkspacePath = args[++i];
        }

        if (string.IsNullOrWhiteSpace(newWorkspacePath))
        {
            return (false, string.Empty, "missing required option --new-workspace <path>.");
        }

        return (true, newWorkspacePath, string.Empty);
    }

    private static bool IsHelpToken(string value)
    {
        return string.Equals(value, "help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintHelp()
    {
        Presenter.WriteUsage("meta-fabric <command> [options]");
        Presenter.WriteInfo(string.Empty);
        Presenter.WriteCommandCatalog("Commands:", new[]
        {
            ("help", "Show this help."),
            ("init", "Create a new MetaFabric workspace."),
            ("suggest", "Suggest parent-scoped requirements over referenced weave workspaces."),
            ("check", "Validate scoped binding over referenced weave workspaces."),
        });
        Presenter.WriteInfo(string.Empty);
        Presenter.WriteExamples(new[]
        {
            "meta-fabric suggest --workspace MetaFabric.Workspaces\\Fabric-Suggest-Scoped-Group-CategoryItem",
            "meta-fabric check --workspace MetaFabric.Workspaces\\Fabric-Scoped-Group-CategoryItem"
        });
        Presenter.WriteInfo(string.Empty);
        Presenter.WriteNext("meta-fabric suggest --help");
    }

    private static void PrintInitHelp()
    {
        Presenter.WriteInfo("Command: init");
        Presenter.WriteUsage("meta-fabric init --new-workspace <path>");
        Presenter.WriteInfo(string.Empty);
        Presenter.WriteInfo("Notes:");
        Presenter.WriteInfo("  Creates a new workspace with the MetaFabric model and validates it.");
    }

    private static void PrintSuggestHelp()
    {
        Presenter.WriteInfo("Command: suggest");
        Presenter.WriteUsage("meta-fabric suggest --workspace <path>");
        Presenter.WriteInfo(string.Empty);
        Presenter.WriteInfo("Notes:");
        Presenter.WriteInfo("  Suggests parent-scoped binding requirements only when one scope pattern makes an ambiguous child weave deterministic.");
        Presenter.WriteInfo("  Weak suggestions are printed when more than one scope pattern would work.");
    }

    private static void PrintCheckHelp()
    {
        Presenter.WriteInfo("Command: check");
        Presenter.WriteUsage("meta-fabric check --workspace <path>");
        Presenter.WriteInfo(string.Empty);
        Presenter.WriteInfo("Notes:");
        Presenter.WriteInfo("  Loads referenced weave workspaces and validates scoped parent-child binding requirements.");
    }
}
