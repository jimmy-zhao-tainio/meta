using Meta.Core.Domain;
using Meta.Core.Presentation;
using Meta.Core.Presentation.Cli;
using Meta.Core.Services;
using MetaWeave.Core;

internal static class Program
{
    private const int PersistRetryCount = 3;
    private static readonly TimeSpan PersistRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly ConsolePresenter Presenter = new();
    private static readonly CliAppDefinition Cli = new(
        "meta-weave",
        new[] { "meta-weave <command> [options]" },
        new[]
        {
            new CliCommandDefinition(
                "help",
                "Show this help.",
                new[] { "meta-weave help" }),
            new CliCommandDefinition(
                "init",
                "Create a new MetaWeave workspace.",
                new[] { "meta-weave init --new-workspace <path>" },
                new[]
                {
                    new CliOptionDefinition("--new-workspace <path>", "Required. Empty target directory for the new MetaWeave workspace.")
                },
                new[] { "Creates a new workspace with the MetaWeave model and validates it." }),
            new CliCommandDefinition(
                "add-model",
                "Add a referenced model workspace.",
                new[] { "meta-weave add-model --workspace <path> --alias <alias> --model <modelName> --workspace-path <path>" },
                new[]
                {
                    new CliOptionDefinition("--workspace <path>", "Required. MetaWeave workspace to update."),
                    new CliOptionDefinition("--alias <alias>", "Required. Local model alias used by bindings."),
                    new CliOptionDefinition("--model <modelName>", "Required. Referenced model name."),
                    new CliOptionDefinition("--workspace-path <path>", "Required. Referenced model workspace path.")
                }),
            new CliCommandDefinition(
                "add-binding",
                "Add a property binding between two model references.",
                new[] { "meta-weave add-binding --workspace <path> --name <bindingName> --source-model <alias> --source-entity <entity> --source-property <property> --target-model <alias> --target-entity <entity> --target-property <property>" },
                new[]
                {
                    new CliOptionDefinition("--workspace <path>", "Required. MetaWeave workspace to update."),
                    new CliOptionDefinition("--name <bindingName>", "Required. Binding identity."),
                    new CliOptionDefinition("--source-model <alias>", "Required. Source model alias."),
                    new CliOptionDefinition("--source-entity <entity>", "Required. Source entity name."),
                    new CliOptionDefinition("--source-property <property>", "Required. Source property name."),
                    new CliOptionDefinition("--target-model <alias>", "Required. Target model alias."),
                    new CliOptionDefinition("--target-entity <entity>", "Required. Target entity name."),
                    new CliOptionDefinition("--target-property <property>", "Required. Target property name.")
                }),
            new CliCommandDefinition(
                "suggest",
                "Suggest missing property bindings only when the source values resolve uniquely and completely in a target key.",
                new[] { "meta-weave suggest --workspace <path>" },
                new[]
                {
                    new CliOptionDefinition("--workspace <path>", "Required. MetaWeave workspace to inspect.")
                },
                new[]
                {
                    "Strong suggestions require exact property-name alignment plus complete and unique resolution.",
                    "Weak suggestions cover role-style suffix matches and cases where one source property resolves to more than one eligible target."
                }),
            new CliCommandDefinition(
                "check",
                "Validate property bindings across referenced workspaces.",
                new[] { "meta-weave check --workspace <path>" },
                new[]
                {
                    new CliOptionDefinition("--workspace <path>", "Required. MetaWeave workspace to validate.")
                },
                new[] { "Loads referenced workspaces and validates that every bound source property resolves exactly once in the target model." }),
            new CliCommandDefinition(
                "materialize",
                "Materialize a new workspace from a valid weave.",
                new[] { "meta-weave materialize --workspace <path> --new-workspace <path> --model <name>" },
                new[]
                {
                    new CliOptionDefinition("--workspace <path>", "Required. MetaWeave workspace to materialize from."),
                    new CliOptionDefinition("--new-workspace <path>", "Required. Empty target directory for the materialized workspace."),
                    new CliOptionDefinition("--model <name>", "Required. Materialized model name.")
                },
                new[] { "Checks the weave, calls core workspace merge on the referenced workspaces, and materializes weave bindings as in-workspace relationships." })
        },
        Next: "meta-weave suggest --help",
        Examples: new[]
        {
            "meta-weave suggest --workspace MetaWeave\\Workspaces\\Weave-Mapping-ReferenceType",
            "meta-weave check --workspace MetaWeave\\Workspaces\\Weave-Mapping-ReferenceType"
        });

    internal static CliAppDefinition CreateAppDefinition() => Cli;

    static async Task<int> Main(string[] args)
    {
        if (Meta.Core.Presentation.Cli.CliVersion.TryWriteVersion(Presenter, Cli.Name, args, out var versionExitCode))
        {
            return versionExitCode;
        }

        if (args.Length == 0 || IsHelpToken(args[0]))
        {
            PrintHelp();
            return 0;
        }

        if (string.Equals(args[0], "init", StringComparison.OrdinalIgnoreCase))
        {
            return await RunInitAsync(args).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "check", StringComparison.OrdinalIgnoreCase))
        {
            return await RunCheckAsync(args).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "materialize", StringComparison.OrdinalIgnoreCase))
        {
            return await RunMaterializeAsync(args).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "add-model", StringComparison.OrdinalIgnoreCase))
        {
            return await RunAddModelAsync(args).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "add-binding", StringComparison.OrdinalIgnoreCase))
        {
            return await RunAddBindingAsync(args).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "suggest", StringComparison.OrdinalIgnoreCase))
        {
            return await RunSuggestAsync(args).ConfigureAwait(false);
        }

        Presenter.WriteFailure(
            "Cannot run meta-weave.",
            new[]
            {
                $"Unknown command '{args[0]}'.",
                "Next: meta-weave help"
            });
        return 1;
    }

    private static async Task<int> RunInitAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintInitHelp();
            return 0;
        }

        var parseResult = ParseNewWorkspaceOnly(args, startIndex: 1);
        if (!parseResult.Ok)
        {
            Presenter.WriteFailure(
                "Cannot initialize MetaWeave workspace.",
                new[] { parseResult.ErrorMessage, "Next: meta-weave init --help" });
            return 1;
        }

        var workspacePath = Path.GetFullPath(parseResult.NewWorkspacePath);
        if (Directory.Exists(workspacePath) && Directory.EnumerateFileSystemEntries(workspacePath).Any())
        {
            Presenter.WriteFailure(
                "Cannot initialize MetaWeave workspace.",
                new[]
                {
                    $"Target directory '{workspacePath}' must be empty.",
                    "Next: choose a new folder or empty the target directory and retry."
                });
            return 4;
        }

        Directory.CreateDirectory(workspacePath);

        var workspace = MetaWeaveWorkspaces.CreateEmptyMetaWeaveWorkspace(workspacePath);
        var validation = new ValidationService().Validate(workspace);
        if (validation.HasErrors)
        {
            Presenter.WriteFailure(
                "Cannot initialize MetaWeave workspace.",
                validation.Issues.Where(item => item.Severity == IssueSeverity.Error)
                    .Select(item => $"- {item.Code}: {item.Message}")
                    .Concat(new[] { "Next: fix the sanctioned model and retry init." }));
            return 4;
        }

        await new WorkspaceService().SaveAsync(workspace).ConfigureAwait(false);
        await WaitForWorkspaceReadyAsync(workspacePath).ConfigureAwait(false);

        Presenter.WriteOk("weave init", ("Workspace", workspacePath), ("Model", workspace.Model.Name));
        return 0;
    }

    private static async Task<int> RunSuggestAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintSuggestHelp();
            return 0;
        }

        var parseResult = ParseWorkspaceOnly(args, startIndex: 1);
        if (!parseResult.Ok)
        {
            Presenter.WriteFailure(
                "Cannot suggest weave bindings.",
                new[] { parseResult.ErrorMessage, "Next: meta-weave suggest --help" });
            return 1;
        }

        var workspacePath = Path.GetFullPath(parseResult.WorkspacePath);
        try
        {
            var workspace = await new WorkspaceService().LoadAsync(workspacePath, searchUpward: false).ConfigureAwait(false);
            var result = await new MetaWeaveSuggestService().SuggestAsync(workspace).ConfigureAwait(false);

            Line("Binding suggestions");
            if (result.Suggestions.Count == 0)
            {
                Line("  (none)");
            }
            else
            {
                for (var index = 0; index < result.Suggestions.Count; index++)
                {
                    var suggestion = result.Suggestions[index];
                    var roleSuffix = string.IsNullOrWhiteSpace(suggestion.InferredRole)
                        ? string.Empty
                        : $" (role: {suggestion.InferredRole})";
                    Line($"  {index + 1}) {suggestion.SourceModelAlias}.{suggestion.SourceEntity}.{suggestion.SourceProperty} -> {suggestion.TargetModelAlias}.{suggestion.TargetEntity}.{suggestion.TargetProperty}{roleSuffix}");
                }
            }

            if (result.WeakSuggestions.Count > 0)
            {
                Line(string.Empty);
                Line("Weak binding suggestions");
                var weakIndex = 1;
                foreach (var weakSuggestion in result.WeakSuggestions)
                {
                    foreach (var candidate in weakSuggestion.Candidates)
                    {
                        var roleSuffix = string.IsNullOrWhiteSpace(candidate.InferredRole)
                            ? string.Empty
                            : $" (role: {candidate.InferredRole})";
                        Line($"  {weakIndex}) {weakSuggestion.SourceModelAlias}.{weakSuggestion.SourceEntity}.{weakSuggestion.SourceProperty} -> {candidate.TargetModelAlias}.{candidate.TargetEntity}.{candidate.TargetProperty}{roleSuffix}");
                        weakIndex++;
                    }
                }
            }

            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Presenter.WriteFailure(
                "Cannot suggest weave bindings.",
                new[] { ex.Message, "Next: fix the weave workspace or referenced workspaces and retry." });
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

        var parseResult = ParseWorkspaceOnly(args, startIndex: 1);
        if (!parseResult.Ok)
        {
            Presenter.WriteFailure(
                "Cannot check weave bindings.",
                new[] { parseResult.ErrorMessage, "Next: meta-weave check --help" });
            return 1;
        }

        var workspacePath = Path.GetFullPath(parseResult.WorkspacePath);
        try
        {
            var workspace = await new WorkspaceService().LoadAsync(workspacePath, searchUpward: false).ConfigureAwait(false);
            var result = await new MetaWeaveService().CheckAsync(workspace).ConfigureAwait(false);
            if (result.HasErrors)
            {
                Presenter.WriteFailure(
                    "Cannot check weave bindings.",
                    result.Bindings.SelectMany(binding => binding.Errors.Select(error => $"- {binding.BindingName}: {error}"))
                        .Concat(new[] { "Next: fix the reported bindings and retry meta-weave check." }));
                return 2;
            }

            Presenter.WriteOk(
                "weave check",
                ("Workspace", workspacePath),
                ("Bindings", result.BindingCount.ToString()),
                ("ResolvedRows", result.ResolvedRowCount.ToString()),
                ("Errors", result.ErrorCount.ToString()));
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Presenter.WriteFailure(
                "Cannot check weave bindings.",
                new[] { ex.Message, "Next: fix the weave workspace or referenced workspaces and retry." });
            return 4;
        }
    }

    private static async Task<int> RunMaterializeAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintMaterializeHelp();
            return 0;
        }

        var parseResult = ParseWorkspaceAndNewWorkspaceAndModel(args, startIndex: 1);
        if (!parseResult.Ok)
        {
            Presenter.WriteFailure(
                "Cannot materialize weave workspace.",
                new[] { parseResult.ErrorMessage, "Next: meta-weave materialize --help" });
            return 1;
        }

        var weaveWorkspacePath = Path.GetFullPath(parseResult.WorkspacePath);
        var materializedWorkspacePath = Path.GetFullPath(parseResult.NewWorkspacePath);
        if (Directory.Exists(materializedWorkspacePath) && Directory.EnumerateFileSystemEntries(materializedWorkspacePath).Any())
        {
            Presenter.WriteFailure(
                "Cannot materialize weave workspace.",
                new[]
                {
                    $"Target directory '{materializedWorkspacePath}' must be empty.",
                    "Next: choose a new folder or empty the target directory and retry."
                });
            return 4;
        }

        try
        {
            var workspaceService = new WorkspaceService();
            var weaveWorkspace = await workspaceService.LoadAsync(weaveWorkspacePath, searchUpward: false).ConfigureAwait(false);
            var materializedWorkspace = await new MetaWeaveService(workspaceService, new WorkspaceMergeService())
                .MaterializeAsync(weaveWorkspace, materializedWorkspacePath, parseResult.ModelName)
                .ConfigureAwait(false);

            Directory.CreateDirectory(materializedWorkspacePath);
            await workspaceService.SaveAsync(materializedWorkspace).ConfigureAwait(false);
            await WaitForWorkspaceReadyAsync(materializedWorkspacePath).ConfigureAwait(false);

            Presenter.WriteOk(
                "weave materialize",
                ("Weave", weaveWorkspacePath),
                ("Workspace", materializedWorkspacePath),
                ("Model", materializedWorkspace.Model.Name),
                ("Entities", materializedWorkspace.Model.Entities.Count.ToString()),
                ("BindingsMaterialized", weaveWorkspace.Instance.GetOrCreateEntityRecords("PropertyBinding").Count.ToString()));
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Presenter.WriteFailure(
                "Cannot materialize weave workspace.",
                new[] { ex.Message, "Next: run meta-weave check and resolve all reported issues before materialize." });
            return 4;
        }
    }
    private static async Task<int> RunAddModelAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintAddModelHelp();
            return 0;
        }

        (bool Ok, string WorkspacePath, string Alias, string ModelName, string ModelWorkspacePath, string ErrorMessage) parse;
        try
        {
            parse = ParseAddModelArgs(args, 1);
        }
        catch (InvalidOperationException ex)
        {
            Presenter.WriteFailure(
                "Cannot add model reference.",
                new[] { ex.Message, "Next: meta-weave add-model --help" });
            return 1;
        }
        if (!parse.Ok)
        {
            Presenter.WriteFailure(
                "Cannot add model reference.",
                new[] { parse.ErrorMessage, "Next: meta-weave add-model --help" });
            return 1;
        }

        var workspacePath = Path.GetFullPath(parse.WorkspacePath);
        var workspaceService = new WorkspaceService();
        var workspace = await workspaceService.LoadAsync(workspacePath, searchUpward: false).ConfigureAwait(false);
        try
        {
            var resolvedWorkspacePath = Path.GetFullPath(parse.ModelWorkspacePath);
            await new MetaWeaveAuthoringService().AddModelReferenceAsync(workspace, parse.Alias, parse.ModelName, resolvedWorkspacePath).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Presenter.WriteFailure(
                "Cannot add model reference.",
                new[] { ex.Message, "Next: meta-weave add-model --help" });
            return 4;
        }

        var validation = new ValidationService().Validate(workspace);
        if (validation.HasErrors)
        {
            Presenter.WriteFailure(
                "Cannot add model reference.",
                validation.Issues.Where(item => item.Severity == IssueSeverity.Error)
                    .Select(item => $"- {item.Code}: {item.Message}")
                    .Concat(new[] { "Next: fix the weave workspace and retry add-model." }));
            return 4;
        }

        await workspaceService.SaveAsync(workspace).ConfigureAwait(false);
        await WaitForPersistedModelReferenceAsync(workspacePath, parse.Alias).ConfigureAwait(false);
        Presenter.WriteOk(
            "weave model reference added",
            ("Workspace", workspacePath),
            ("Alias", parse.Alias),
            ("Model", parse.ModelName),
            ("WorkspacePath", parse.ModelWorkspacePath));
        return 0;
    }

    private static async Task<int> RunAddBindingAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintAddBindingHelp();
            return 0;
        }

        (bool Ok, string WorkspacePath, string Name, string SourceModelAlias, string SourceEntity, string SourceProperty, string TargetModelAlias, string TargetEntity, string TargetProperty, string ErrorMessage) parse;
        try
        {
            parse = ParseAddBindingArgs(args, 1);
        }
        catch (InvalidOperationException ex)
        {
            Presenter.WriteFailure(
                "Cannot add property binding.",
                new[] { ex.Message, "Next: meta-weave add-binding --help" });
            return 1;
        }
        if (!parse.Ok)
        {
            Presenter.WriteFailure(
                "Cannot add property binding.",
                new[] { parse.ErrorMessage, "Next: meta-weave add-binding --help" });
            return 1;
        }

        var workspacePath = Path.GetFullPath(parse.WorkspacePath);
        var workspaceService = new WorkspaceService();
        var workspace = await workspaceService.LoadAsync(workspacePath, searchUpward: false).ConfigureAwait(false);
        try
        {
            await new MetaWeaveAuthoringService().AddPropertyBindingAsync(
                workspace,
                parse.Name,
                parse.SourceModelAlias,
                parse.SourceEntity,
                parse.SourceProperty,
                parse.TargetModelAlias,
                parse.TargetEntity,
                parse.TargetProperty).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Presenter.WriteFailure(
                "Cannot add property binding.",
                new[] { ex.Message, "Next: meta-weave add-binding --help" });
            return 4;
        }

        var validation = new ValidationService().Validate(workspace);
        if (validation.HasErrors)
        {
            Presenter.WriteFailure(
                "Cannot add property binding.",
                validation.Issues.Where(item => item.Severity == IssueSeverity.Error)
                    .Select(item => $"- {item.Code}: {item.Message}")
                    .Concat(new[] { "Next: fix the weave workspace and retry add-binding." }));
            return 4;
        }

        await workspaceService.SaveAsync(workspace).ConfigureAwait(false);
        await WaitForPersistedBindingAsync(workspacePath, parse.Name).ConfigureAwait(false);
        Presenter.WriteOk(
            "weave property binding added",
            ("Workspace", workspacePath),
            ("Binding", parse.Name));
        return 0;
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

    private static (bool Ok, string WorkspacePath, string NewWorkspacePath, string ModelName, string ErrorMessage) ParseWorkspaceAndNewWorkspaceAndModel(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        var newWorkspacePath = string.Empty;
        var modelName = string.Empty;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (i + 1 >= args.Length)
            {
                return (false, workspacePath, newWorkspacePath, modelName, $"missing value for {arg}.");
            }

            switch (arg.ToLowerInvariant())
            {
                case "--workspace":
                    workspacePath = EnsureUnsetThenAssign(workspacePath, args[++i], "--workspace");
                    break;
                case "--new-workspace":
                    newWorkspacePath = EnsureUnsetThenAssign(newWorkspacePath, args[++i], "--new-workspace");
                    break;
                case "--model":
                    modelName = EnsureUnsetThenAssign(modelName, args[++i], "--model");
                    break;
                default:
                    return (false, workspacePath, newWorkspacePath, modelName, $"unknown option '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return (false, workspacePath, newWorkspacePath, modelName, "missing required option --workspace <path>.");
        }

        if (string.IsNullOrWhiteSpace(newWorkspacePath))
        {
            return (false, workspacePath, newWorkspacePath, modelName, "missing required option --new-workspace <path>.");
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            return (false, workspacePath, newWorkspacePath, modelName, "missing required option --model <name>.");
        }

        return (true, workspacePath, newWorkspacePath, modelName, string.Empty);
    }

    private static bool IsHelpToken(string value)
    {
        return string.Equals(value, "help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintHelp()
    {
        CliHelpRenderer.WriteAppHelp(Presenter, Cli);
    }

    private static void PrintInitHelp()
    {
        PrintCommandHelp("init");
    }

    private static void PrintSuggestHelp()
    {
        PrintCommandHelp("suggest");
    }

    private static void PrintCheckHelp()
    {
        PrintCommandHelp("check");
    }

    private static void PrintMaterializeHelp()
    {
        PrintCommandHelp("materialize");
    }

    private static void PrintAddModelHelp()
    {
        PrintCommandHelp("add-model");
    }

    private static void PrintAddBindingHelp()
    {
        PrintCommandHelp("add-binding");
    }

    private static void PrintCommandHelp(string commandName)
    {
        CliHelpRenderer.WriteCommandHelp(Presenter, Cli, Cli.GetCommand(commandName));
    }

    private static (bool Ok, string WorkspacePath, string Alias, string ModelName, string ModelWorkspacePath, string ErrorMessage) ParseAddModelArgs(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        var alias = string.Empty;
        var modelName = string.Empty;
        var modelWorkspacePath = string.Empty;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (i + 1 >= args.Length)
            {
                return (false, workspacePath, alias, modelName, modelWorkspacePath, $"missing value for {arg}.");
            }

            switch (arg.ToLowerInvariant())
            {
                case "--workspace":
                    workspacePath = EnsureUnsetThenAssign(workspacePath, args[++i], "--workspace");
                    break;
                case "--alias":
                    alias = EnsureUnsetThenAssign(alias, args[++i], "--alias");
                    break;
                case "--model":
                    modelName = EnsureUnsetThenAssign(modelName, args[++i], "--model");
                    break;
                case "--workspace-path":
                    modelWorkspacePath = EnsureUnsetThenAssign(modelWorkspacePath, args[++i], "--workspace-path");
                    break;
                default:
                    return (false, workspacePath, alias, modelName, modelWorkspacePath, $"unknown option '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return (false, workspacePath, alias, modelName, modelWorkspacePath, "missing required option --workspace <path>.");
        }

        if (string.IsNullOrWhiteSpace(alias))
        {
            return (false, workspacePath, alias, modelName, modelWorkspacePath, "missing required option --alias <alias>.");
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            return (false, workspacePath, alias, modelName, modelWorkspacePath, "missing required option --model <modelName>.");
        }

        if (string.IsNullOrWhiteSpace(modelWorkspacePath))
        {
            return (false, workspacePath, alias, modelName, modelWorkspacePath, "missing required option --workspace-path <path>.");
        }

        return (true, workspacePath, alias, modelName, modelWorkspacePath, string.Empty);
    }

    private static (bool Ok, string WorkspacePath, string Name, string SourceModelAlias, string SourceEntity, string SourceProperty, string TargetModelAlias, string TargetEntity, string TargetProperty, string ErrorMessage) ParseAddBindingArgs(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        var name = string.Empty;
        var sourceModelAlias = string.Empty;
        var sourceEntity = string.Empty;
        var sourceProperty = string.Empty;
        var targetModelAlias = string.Empty;
        var targetEntity = string.Empty;
        var targetProperty = string.Empty;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (i + 1 >= args.Length)
            {
                return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, $"missing value for {arg}.");
            }

            switch (arg.ToLowerInvariant())
            {
                case "--workspace":
                    workspacePath = EnsureUnsetThenAssign(workspacePath, args[++i], "--workspace");
                    break;
                case "--name":
                    name = EnsureUnsetThenAssign(name, args[++i], "--name");
                    break;
                case "--source-model":
                    sourceModelAlias = EnsureUnsetThenAssign(sourceModelAlias, args[++i], "--source-model");
                    break;
                case "--source-entity":
                    sourceEntity = EnsureUnsetThenAssign(sourceEntity, args[++i], "--source-entity");
                    break;
                case "--source-property":
                    sourceProperty = EnsureUnsetThenAssign(sourceProperty, args[++i], "--source-property");
                    break;
                case "--target-model":
                    targetModelAlias = EnsureUnsetThenAssign(targetModelAlias, args[++i], "--target-model");
                    break;
                case "--target-entity":
                    targetEntity = EnsureUnsetThenAssign(targetEntity, args[++i], "--target-entity");
                    break;
                case "--target-property":
                    targetProperty = EnsureUnsetThenAssign(targetProperty, args[++i], "--target-property");
                    break;
                default:
                    return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, $"unknown option '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, "missing required option --workspace <path>.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, "missing required option --name <bindingName>.");
        }

        if (string.IsNullOrWhiteSpace(sourceModelAlias))
        {
            return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, "missing required option --source-model <alias>.");
        }

        if (string.IsNullOrWhiteSpace(sourceEntity))
        {
            return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, "missing required option --source-entity <entity>.");
        }

        if (string.IsNullOrWhiteSpace(sourceProperty))
        {
            return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, "missing required option --source-property <property>.");
        }

        if (string.IsNullOrWhiteSpace(targetModelAlias))
        {
            return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, "missing required option --target-model <alias>.");
        }

        if (string.IsNullOrWhiteSpace(targetEntity))
        {
            return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, "missing required option --target-entity <entity>.");
        }

        if (string.IsNullOrWhiteSpace(targetProperty))
        {
            return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, "missing required option --target-property <property>.");
        }

        return (true, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, string.Empty);
    }

    private static int CountWeakSuggestions(IReadOnlyList<WeaveWeakBindingSuggestion> suggestions)
    {
        return suggestions.Sum(item => item.Candidates.Count);
    }

    private static string EnsureUnsetThenAssign(string currentValue, string nextValue, string optionName)
    {
        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            throw new InvalidOperationException($"{optionName} can only be provided once.");
        }

        return nextValue;
    }

    private static async Task WaitForWorkspaceReadyAsync(string workspacePath)
    {
        await WaitForWorkspaceStateAsync(
            workspacePath,
            workspace => !string.IsNullOrWhiteSpace(workspace.Model?.Name),
            "workspace initialization").ConfigureAwait(false);
    }

    private static async Task WaitForPersistedModelReferenceAsync(string workspacePath, string alias)
    {
        await WaitForWorkspaceStateAsync(
            workspacePath,
            workspace => workspace.Instance.GetOrCreateEntityRecords("ModelReference")
                .Any(record =>
                    record.Values.TryGetValue("Alias", out var value) &&
                    string.Equals(value, alias, StringComparison.Ordinal)),
            $"model reference '{alias}'").ConfigureAwait(false);
    }

    private static async Task WaitForPersistedBindingAsync(string workspacePath, string bindingName)
    {
        await WaitForWorkspaceStateAsync(
            workspacePath,
            workspace => workspace.Instance.GetOrCreateEntityRecords("PropertyBinding")
                .Any(record =>
                    record.Values.TryGetValue("Name", out var value) &&
                    string.Equals(value, bindingName, StringComparison.Ordinal)),
            $"property binding '{bindingName}'").ConfigureAwait(false);
    }

    private static async Task WaitForWorkspaceStateAsync(string workspacePath, Func<Workspace, bool> predicate, string description)
    {
        var workspaceService = new WorkspaceService();
        var lockPath = Path.Combine(workspacePath, ".meta.lock");
        for (var attempt = 0; attempt < PersistRetryCount; attempt++)
        {
            try
            {
                if (File.Exists(lockPath))
                {
                    throw new IOException($"Workspace lock '{lockPath}' is still present.");
                }

                var loaded = await workspaceService.LoadAsync(workspacePath, searchUpward: false).ConfigureAwait(false);
                if (predicate(loaded))
                {
                    return;
                }
            }
            catch (Exception) when (attempt < PersistRetryCount - 1)
            {
                // Retry below.
            }

            if (attempt < PersistRetryCount - 1)
            {
                await Task.Delay(PersistRetryDelay).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException($"Workspace save did not persist expected {description}.");
    }


    private static void Line(string message)
    {
        Presenter.WriteInfo(message);
    }
}





