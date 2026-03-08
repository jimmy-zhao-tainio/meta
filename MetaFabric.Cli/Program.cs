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

        if (string.Equals(args[0], "add-weave", StringComparison.OrdinalIgnoreCase))
        {
            return await RunAddWeaveAsync(args).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "add-binding", StringComparison.OrdinalIgnoreCase))
        {
            return await RunAddBindingAsync(args).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "add-scope", StringComparison.OrdinalIgnoreCase))
        {
            return await RunAddScopeAsync(args).ConfigureAwait(false);
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

    private static async Task<int> RunAddWeaveAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintAddWeaveHelp();
            return 0;
        }

        var parseResult = ParseAddWeaveArgs(args, 1);
        if (!parseResult.Ok)
        {
            Presenter.WriteFailure(parseResult.ErrorMessage, new[] { "Next: meta-fabric add-weave --help" });
            return 1;
        }

        var workspacePath = Path.GetFullPath(parseResult.WorkspacePath);
        try
        {
            var workspaceService = new WorkspaceService();
            var workspace = await workspaceService.LoadAsync(workspacePath, searchUpward: false).ConfigureAwait(false);
            await new MetaFabricAuthoringService(workspaceService)
                .AddWeaveReferenceAsync(workspace, parseResult.Alias, Path.GetFullPath(parseResult.WeaveWorkspacePath))
                .ConfigureAwait(false);

            var validation = new ValidationService().Validate(workspace);
            if (validation.HasErrors)
            {
                Presenter.WriteFailure("fabric workspace is invalid after add-weave.", validation.Issues
                    .Where(issue => issue.Severity == IssueSeverity.Error)
                    .Select(issue => $"  - {issue.Code}: {issue.Message}")
                    .Concat(new[] { "Next: fix the fabric workspace and retry add-weave." }));
                return 4;
            }

            await workspaceService.SaveAsync(workspace).ConfigureAwait(false);
            Presenter.WriteOk(
                "fabric weave reference added",
                ("Workspace", workspacePath),
                ("Alias", parseResult.Alias),
                ("WeaveWorkspace", Path.GetFullPath(parseResult.WeaveWorkspacePath)));
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Presenter.WriteFailure(ex.Message, new[] { "Next: meta-fabric add-weave --help" });
            return 4;
        }
    }

    private static async Task<int> RunAddBindingAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintAddBindingHelp();
            return 0;
        }

        var parseResult = ParseAddBindingArgs(args, 1);
        if (!parseResult.Ok)
        {
            Presenter.WriteFailure(parseResult.ErrorMessage, new[] { "Next: meta-fabric add-binding --help" });
            return 1;
        }

        var workspacePath = Path.GetFullPath(parseResult.WorkspacePath);
        try
        {
            var workspaceService = new WorkspaceService();
            var workspace = await workspaceService.LoadAsync(workspacePath, searchUpward: false).ConfigureAwait(false);
            await new MetaFabricAuthoringService(workspaceService)
                .AddBindingReferenceAsync(
                    workspace,
                    parseResult.Name,
                    parseResult.WeaveAlias,
                    parseResult.SourceEntity,
                    parseResult.SourceProperty,
                    parseResult.TargetEntity,
                    parseResult.TargetProperty)
                .ConfigureAwait(false);

            var validation = new ValidationService().Validate(workspace);
            if (validation.HasErrors)
            {
                Presenter.WriteFailure("fabric workspace is invalid after add-binding.", validation.Issues
                    .Where(issue => issue.Severity == IssueSeverity.Error)
                    .Select(issue => $"  - {issue.Code}: {issue.Message}")
                    .Concat(new[] { "Next: fix the fabric workspace and retry add-binding." }));
                return 4;
            }

            await workspaceService.SaveAsync(workspace).ConfigureAwait(false);
            Presenter.WriteOk(
                "fabric binding reference added",
                ("Workspace", workspacePath),
                ("Binding", parseResult.Name),
                ("Weave", parseResult.WeaveAlias));
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Presenter.WriteFailure(ex.Message, new[] { "Next: meta-fabric add-binding --help" });
            return 4;
        }
    }

    private static async Task<int> RunAddScopeAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintAddScopeHelp();
            return 0;
        }

        var parseResult = ParseAddScopeArgs(args, 1);
        if (!parseResult.Ok)
        {
            Presenter.WriteFailure(parseResult.ErrorMessage, new[] { "Next: meta-fabric add-scope --help" });
            return 1;
        }

        var workspacePath = Path.GetFullPath(parseResult.WorkspacePath);
        try
        {
            var workspaceService = new WorkspaceService();
            var workspace = await workspaceService.LoadAsync(workspacePath, searchUpward: false).ConfigureAwait(false);
            await new MetaFabricAuthoringService(workspaceService)
                .AddScopeRequirementAsync(
                    workspace,
                    parseResult.BindingReferenceName,
                    parseResult.ParentBindingReferenceName,
                    parseResult.SourceParentReferenceName,
                    parseResult.TargetParentReferenceName)
                .ConfigureAwait(false);

            var validation = new ValidationService().Validate(workspace);
            if (validation.HasErrors)
            {
                Presenter.WriteFailure("fabric workspace is invalid after add-scope.", validation.Issues
                    .Where(issue => issue.Severity == IssueSeverity.Error)
                    .Select(issue => $"  - {issue.Code}: {issue.Message}")
                    .Concat(new[] { "Next: fix the fabric workspace and retry add-scope." }));
                return 4;
            }

            await workspaceService.SaveAsync(workspace).ConfigureAwait(false);
            Presenter.WriteOk(
                "fabric scope requirement added",
                ("Workspace", workspacePath),
                ("Binding", parseResult.BindingReferenceName),
                ("ParentBinding", parseResult.ParentBindingReferenceName),
                ("SourceParentReference", parseResult.SourceParentReferenceName),
                ("TargetParentReference", parseResult.TargetParentReferenceName));
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Presenter.WriteFailure(ex.Message, new[] { "Next: meta-fabric add-scope --help" });
            return 4;
        }
    }

    private static async Task<int> RunSuggestAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintSuggestHelp();
            return 0;
        }

        var parseResult = ParseSuggestArgs(args, 1);
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

            if (parseResult.PrintCommands)
            {
                Presenter.WriteInfo(string.Empty);
                Presenter.WriteInfo("Commands");
                if (result.Suggestions.Count == 0)
                {
                    Presenter.WriteInfo("  (none)");
                }
                else
                {
                    foreach (var suggestion in result.Suggestions)
                    {
                        Presenter.WriteInfo($"  meta-fabric add-scope --workspace \"{workspacePath}\" --binding {suggestion.ChildBindingReferenceName} --parent-binding {suggestion.ParentBindingReferenceName} --source-parent-reference {suggestion.SourceParentReferenceName} --target-parent-reference {suggestion.TargetParentReferenceName}");
                    }
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

    private static (bool Ok, string WorkspacePath, bool PrintCommands, string ErrorMessage) ParseSuggestArgs(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        var printCommands = false;
        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--workspace":
                    if (i + 1 >= args.Length)
                    {
                        return (false, workspacePath, printCommands, "missing value for --workspace.");
                    }

                    if (!string.IsNullOrWhiteSpace(workspacePath))
                    {
                        return (false, workspacePath, printCommands, "--workspace can only be provided once.");
                    }

                    workspacePath = args[++i];
                    break;
                case "--print-commands":
                    printCommands = true;
                    break;
                default:
                    return (false, workspacePath, printCommands, $"unknown option '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return (false, string.Empty, printCommands, "missing required option --workspace <path>.");
        }

        return (true, workspacePath, printCommands, string.Empty);
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

    private static (bool Ok, string WorkspacePath, string Alias, string WeaveWorkspacePath, string ErrorMessage) ParseAddWeaveArgs(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        var alias = string.Empty;
        var weaveWorkspacePath = string.Empty;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (i + 1 >= args.Length)
            {
                return (false, workspacePath, alias, weaveWorkspacePath, $"missing value for {arg}.");
            }

            switch (arg.ToLowerInvariant())
            {
                case "--workspace":
                    workspacePath = EnsureUnsetThenAssign(workspacePath, args[++i], "--workspace");
                    break;
                case "--alias":
                    alias = EnsureUnsetThenAssign(alias, args[++i], "--alias");
                    break;
                case "--workspace-path":
                    weaveWorkspacePath = EnsureUnsetThenAssign(weaveWorkspacePath, args[++i], "--workspace-path");
                    break;
                default:
                    return (false, workspacePath, alias, weaveWorkspacePath, $"unknown option '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return (false, workspacePath, alias, weaveWorkspacePath, "missing required option --workspace <path>.");
        }

        if (string.IsNullOrWhiteSpace(alias))
        {
            return (false, workspacePath, alias, weaveWorkspacePath, "missing required option --alias <alias>.");
        }

        if (string.IsNullOrWhiteSpace(weaveWorkspacePath))
        {
            return (false, workspacePath, alias, weaveWorkspacePath, "missing required option --workspace-path <path>.");
        }

        return (true, workspacePath, alias, weaveWorkspacePath, string.Empty);
    }

    private static (bool Ok, string WorkspacePath, string Name, string WeaveAlias, string SourceEntity, string SourceProperty, string TargetEntity, string TargetProperty, string ErrorMessage) ParseAddBindingArgs(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        var name = string.Empty;
        var weaveAlias = string.Empty;
        var sourceEntity = string.Empty;
        var sourceProperty = string.Empty;
        var targetEntity = string.Empty;
        var targetProperty = string.Empty;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (i + 1 >= args.Length)
            {
                return (false, workspacePath, name, weaveAlias, sourceEntity, sourceProperty, targetEntity, targetProperty, $"missing value for {arg}.");
            }

            switch (arg.ToLowerInvariant())
            {
                case "--workspace":
                    workspacePath = EnsureUnsetThenAssign(workspacePath, args[++i], "--workspace");
                    break;
                case "--name":
                    name = EnsureUnsetThenAssign(name, args[++i], "--name");
                    break;
                case "--weave":
                    weaveAlias = EnsureUnsetThenAssign(weaveAlias, args[++i], "--weave");
                    break;
                case "--source-entity":
                    sourceEntity = EnsureUnsetThenAssign(sourceEntity, args[++i], "--source-entity");
                    break;
                case "--source-property":
                    sourceProperty = EnsureUnsetThenAssign(sourceProperty, args[++i], "--source-property");
                    break;
                case "--target-entity":
                    targetEntity = EnsureUnsetThenAssign(targetEntity, args[++i], "--target-entity");
                    break;
                case "--target-property":
                    targetProperty = EnsureUnsetThenAssign(targetProperty, args[++i], "--target-property");
                    break;
                default:
                    return (false, workspacePath, name, weaveAlias, sourceEntity, sourceProperty, targetEntity, targetProperty, $"unknown option '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return (false, workspacePath, name, weaveAlias, sourceEntity, sourceProperty, targetEntity, targetProperty, "missing required option --workspace <path>.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, workspacePath, name, weaveAlias, sourceEntity, sourceProperty, targetEntity, targetProperty, "missing required option --name <bindingReferenceName>.");
        }

        if (string.IsNullOrWhiteSpace(weaveAlias))
        {
            return (false, workspacePath, name, weaveAlias, sourceEntity, sourceProperty, targetEntity, targetProperty, "missing required option --weave <alias>.");
        }

        if (string.IsNullOrWhiteSpace(sourceEntity))
        {
            return (false, workspacePath, name, weaveAlias, sourceEntity, sourceProperty, targetEntity, targetProperty, "missing required option --source-entity <entity>.");
        }

        if (string.IsNullOrWhiteSpace(sourceProperty))
        {
            return (false, workspacePath, name, weaveAlias, sourceEntity, sourceProperty, targetEntity, targetProperty, "missing required option --source-property <property>.");
        }

        if (string.IsNullOrWhiteSpace(targetEntity))
        {
            return (false, workspacePath, name, weaveAlias, sourceEntity, sourceProperty, targetEntity, targetProperty, "missing required option --target-entity <entity>.");
        }

        if (string.IsNullOrWhiteSpace(targetProperty))
        {
            return (false, workspacePath, name, weaveAlias, sourceEntity, sourceProperty, targetEntity, targetProperty, "missing required option --target-property <property>.");
        }

        return (true, workspacePath, name, weaveAlias, sourceEntity, sourceProperty, targetEntity, targetProperty, string.Empty);
    }

    private static (bool Ok, string WorkspacePath, string BindingReferenceName, string ParentBindingReferenceName, string SourceParentReferenceName, string TargetParentReferenceName, string ErrorMessage) ParseAddScopeArgs(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        var bindingReferenceName = string.Empty;
        var parentBindingReferenceName = string.Empty;
        var sourceParentReferenceName = string.Empty;
        var targetParentReferenceName = string.Empty;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (i + 1 >= args.Length)
            {
                return (false, workspacePath, bindingReferenceName, parentBindingReferenceName, sourceParentReferenceName, targetParentReferenceName, $"missing value for {arg}.");
            }

            switch (arg.ToLowerInvariant())
            {
                case "--workspace":
                    workspacePath = EnsureUnsetThenAssign(workspacePath, args[++i], "--workspace");
                    break;
                case "--binding":
                    bindingReferenceName = EnsureUnsetThenAssign(bindingReferenceName, args[++i], "--binding");
                    break;
                case "--parent-binding":
                    parentBindingReferenceName = EnsureUnsetThenAssign(parentBindingReferenceName, args[++i], "--parent-binding");
                    break;
                case "--source-parent-reference":
                    sourceParentReferenceName = EnsureUnsetThenAssign(sourceParentReferenceName, args[++i], "--source-parent-reference");
                    break;
                case "--target-parent-reference":
                    targetParentReferenceName = EnsureUnsetThenAssign(targetParentReferenceName, args[++i], "--target-parent-reference");
                    break;
                default:
                    return (false, workspacePath, bindingReferenceName, parentBindingReferenceName, sourceParentReferenceName, targetParentReferenceName, $"unknown option '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return (false, workspacePath, bindingReferenceName, parentBindingReferenceName, sourceParentReferenceName, targetParentReferenceName, "missing required option --workspace <path>.");
        }

        if (string.IsNullOrWhiteSpace(bindingReferenceName))
        {
            return (false, workspacePath, bindingReferenceName, parentBindingReferenceName, sourceParentReferenceName, targetParentReferenceName, "missing required option --binding <name>.");
        }

        if (string.IsNullOrWhiteSpace(parentBindingReferenceName))
        {
            return (false, workspacePath, bindingReferenceName, parentBindingReferenceName, sourceParentReferenceName, targetParentReferenceName, "missing required option --parent-binding <name>.");
        }

        if (string.IsNullOrWhiteSpace(sourceParentReferenceName))
        {
            return (false, workspacePath, bindingReferenceName, parentBindingReferenceName, sourceParentReferenceName, targetParentReferenceName, "missing required option --source-parent-reference <name>.");
        }

        if (string.IsNullOrWhiteSpace(targetParentReferenceName))
        {
            return (false, workspacePath, bindingReferenceName, parentBindingReferenceName, sourceParentReferenceName, targetParentReferenceName, "missing required option --target-parent-reference <name>.");
        }

        return (true, workspacePath, bindingReferenceName, parentBindingReferenceName, sourceParentReferenceName, targetParentReferenceName, string.Empty);
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
            ("add-weave", "Add a referenced MetaWeave workspace."),
            ("add-binding", "Add a binding reference from a weave binding."),
            ("add-scope", "Add a parent-scoped binding requirement."),
            ("suggest", "Suggest parent-scoped requirements over referenced weave workspaces."),
            ("check", "Validate scoped binding over referenced weave workspaces."),
        });
        Presenter.WriteInfo(string.Empty);
        Presenter.WriteExamples(new[]
        {
            @"meta-fabric add-weave --workspace MetaFabric.Workspace --alias Parent --workspace-path MetaWeave.Workspaces\Weave-Scoped-Group-Category",
            @"meta-fabric add-binding --workspace MetaFabric.Workspace --name ParentGroup --weave Parent --source-entity Group --source-property Name --target-entity Category --target-property Name",
            @"meta-fabric suggest --workspace MetaFabric.Workspaces\Fabric-Suggest-Scoped-Group-CategoryItem --print-commands",
            @"meta-fabric check --workspace MetaFabric.Workspaces\Fabric-Scoped-Group-CategoryItem"
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

    private static void PrintAddWeaveHelp()
    {
        Presenter.WriteInfo("Command: add-weave");
        Presenter.WriteUsage("meta-fabric add-weave --workspace <path> --alias <alias> --workspace-path <path>");
        Presenter.WriteInfo(string.Empty);
        Presenter.WriteInfo("Notes:");
        Presenter.WriteInfo("  Adds a referenced MetaWeave workspace and stores the weave path relative to the fabric workspace when possible.");
    }

    private static void PrintAddBindingHelp()
    {
        Presenter.WriteInfo("Command: add-binding");
        Presenter.WriteUsage("meta-fabric add-binding --workspace <path> --name <referenceName> --weave <alias> --source-entity <entity> --source-property <property> --target-entity <entity> --target-property <property>");
        Presenter.WriteInfo(string.Empty);
        Presenter.WriteInfo("Notes:");
        Presenter.WriteInfo("  Resolves one PropertyBinding inside the referenced weave workspace by its source and target endpoints.");
    }

    private static void PrintAddScopeHelp()
    {
        Presenter.WriteInfo("Command: add-scope");
        Presenter.WriteUsage("meta-fabric add-scope --workspace <path> --binding <name> --parent-binding <name> --source-parent-reference <name> --target-parent-reference <name>");
        Presenter.WriteInfo(string.Empty);
        Presenter.WriteInfo("Notes:");
        Presenter.WriteInfo("  Adds a parent-scoped requirement that constrains one binding by another binding.");
    }

    private static void PrintSuggestHelp()
    {
        Presenter.WriteInfo("Command: suggest");
        Presenter.WriteUsage("meta-fabric suggest --workspace <path> [--print-commands]");
        Presenter.WriteInfo(string.Empty);
        Presenter.WriteInfo("Notes:");
        Presenter.WriteInfo("  Suggests parent-scoped binding requirements only when one scope pattern makes an ambiguous child weave deterministic.");
        Presenter.WriteInfo("  Weak suggestions are printed when more than one scope pattern would work.");
        Presenter.WriteInfo("  --print-commands prints matching meta-fabric add-scope commands for strong suggestions.");
    }

    private static void PrintCheckHelp()
    {
        Presenter.WriteInfo("Command: check");
        Presenter.WriteUsage("meta-fabric check --workspace <path>");
        Presenter.WriteInfo(string.Empty);
        Presenter.WriteInfo("Notes:");
        Presenter.WriteInfo("  Loads referenced weave workspaces and validates scoped parent-child binding requirements.");
    }

    private static string EnsureUnsetThenAssign(string currentValue, string nextValue, string optionName)
    {
        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            throw new InvalidOperationException($"{optionName} can only be provided once.");
        }

        return nextValue;
    }
}

