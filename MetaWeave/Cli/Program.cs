using Meta.Core.Domain;
using Meta.Core.Presentation;
using Meta.Core.Services;
using MetaCli;
using MetaCli.Core;
using MetaWeave.Core;
using MetaWeaveModel = global::MetaWeave.MetaWeaveModel;

internal static class Program
{
    private const string ApplicationId = "app-meta-weave";
    private const string CommandWorkspaceDirectoryName = "meta-weave.MetaCli";
    private static readonly ConsolePresenter Presenter = new();

    static int Main(string[] args)
    {
        if (Meta.Core.Presentation.Cli.CliVersion.TryWriteVersion(Presenter, "meta-weave", args, out var versionExitCode))
        {
            return versionExitCode;
        }

        if (TryHandleHelp(args))
        {
            return 0;
        }

        Environment.ExitCode = 0;
        var runtime = new MetaCliRuntime<MetaWeaveModel>(CommandWorkspacePath, ApplicationId)
            .Bind("exec-help", _ => PrintHelp())
            .Bind("exec-new-workspace", RunNewWorkspace)
            .Bind("exec-add-model", RunAddModel)
            .Bind("exec-add-binding", RunAddBinding)
            .Bind("exec-suggest", RunSuggest)
            .Bind("exec-check", RunCheck)
            .Bind("exec-materialize", RunMaterialize);

        runtime.Run(args);
        return Environment.ExitCode;
    }

    private static string CommandWorkspacePath =>
        Path.Combine(AppContext.BaseDirectory, CommandWorkspaceDirectoryName);

    private static bool TryHandleHelp(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            PrintHelp();
            return true;
        }

        if (IsHelpToken(args[0]))
        {
            if (args.Count > 1)
            {
                PrintCommandHelp(args.Skip(1).ToArray());
            }
            else
            {
                PrintHelp();
            }

            return true;
        }

        if (args.Count > 1 && IsHelpToken(args[1]))
        {
            PrintCommandHelp(new[] { args[0] });
            return true;
        }

        return false;
    }

    private static void RunNewWorkspace(MetaCliInvocation invocation)
    {
        var workspacePath = Path.GetFullPath(invocation.Required("path"));
        if (Directory.Exists(workspacePath) && Directory.EnumerateFileSystemEntries(workspacePath).Any())
        {
            throw new InvalidOperationException($"Target directory '{workspacePath}' must be empty.");
        }

        Directory.CreateDirectory(workspacePath);
        MetaWeaveModel.CreateEmpty().SaveToXmlWorkspace(workspacePath);

        Presenter.WriteOk(
            "weave workspace created",
            ("Workspace", workspacePath),
            ("Model", "MetaWeave"));
    }

    private static void RunAddModel(MetaCliInvocation invocation, MetaWeaveModel weaveModel)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        new MetaWeaveAuthoringService().AddModelReferenceAsync(
            weaveModel,
            workspacePath,
            invocation.Required("alias"),
            invocation.Required("model"),
            invocation.Required("workspace-path")).GetAwaiter().GetResult();
        weaveModel.SaveToXmlWorkspace(workspacePath);

        Presenter.WriteOk(
            "weave model reference added",
            ("Workspace", workspacePath),
            ("Alias", invocation.Required("alias")),
            ("Model", invocation.Required("model")),
            ("WorkspacePath", invocation.Required("workspace-path")));
    }

    private static void RunAddBinding(MetaCliInvocation invocation, MetaWeaveModel weaveModel)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        new MetaWeaveAuthoringService().AddPropertyBindingAsync(
            weaveModel,
            workspacePath,
            invocation.Required("name"),
            invocation.Required("source-model"),
            invocation.Required("source-entity"),
            invocation.Required("source-property"),
            invocation.Required("target-model"),
            invocation.Required("target-entity"),
            invocation.Required("target-property")).GetAwaiter().GetResult();
        weaveModel.SaveToXmlWorkspace(workspacePath);

        Presenter.WriteOk(
            "weave property binding added",
            ("Workspace", workspacePath),
            ("Binding", invocation.Required("name")));
    }

    private static void RunSuggest(MetaCliInvocation invocation, MetaWeaveModel weaveModel)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        var result = new MetaWeaveSuggestService().SuggestAsync(weaveModel, workspacePath).GetAwaiter().GetResult();

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
    }

    private static void RunCheck(MetaCliInvocation invocation, MetaWeaveModel weaveModel)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        var result = new MetaWeaveService().CheckAsync(weaveModel, workspacePath).GetAwaiter().GetResult();
        if (result.HasErrors)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                result.Bindings.SelectMany(binding => binding.Errors.Select(error => $"{binding.BindingName}: {error}"))));
        }

        Presenter.WriteOk(
            "weave check",
            ("Workspace", workspacePath),
            ("Bindings", result.BindingCount.ToString()),
            ("ResolvedRows", result.ResolvedRowCount.ToString()),
            ("Errors", result.ErrorCount.ToString()));
    }

    private static void RunMaterialize(MetaCliInvocation invocation, MetaWeaveModel weaveModel)
    {
        var weaveWorkspacePath = ResolveWorkspacePath(invocation);
        var materializedWorkspacePath = Path.GetFullPath(invocation.Required("new-workspace"));
        if (Directory.Exists(materializedWorkspacePath) && Directory.EnumerateFileSystemEntries(materializedWorkspacePath).Any())
        {
            throw new InvalidOperationException($"Target directory '{materializedWorkspacePath}' must be empty.");
        }

        var workspaceService = new WorkspaceService();
        var materializedWorkspace = new MetaWeaveService(workspaceService, new WorkspaceMergeService())
            .MaterializeAsync(
                weaveModel,
                weaveWorkspacePath,
                materializedWorkspacePath,
                invocation.Required("model"))
            .GetAwaiter()
            .GetResult();

        Directory.CreateDirectory(materializedWorkspacePath);
        workspaceService.SaveAsync(materializedWorkspace).GetAwaiter().GetResult();

        Presenter.WriteOk(
            "weave materialize",
            ("Weave", weaveWorkspacePath),
            ("Workspace", materializedWorkspacePath),
            ("Model", materializedWorkspace.Model.Name),
            ("Entities", materializedWorkspace.Model.Entities.Count.ToString()),
            ("BindingsMaterialized", weaveModel.PropertyBindingList.Count.ToString()));
    }

    private static string ResolveWorkspacePath(MetaCliInvocation invocation)
    {
        var workspace = invocation.Optional("workspace");
        return Path.GetFullPath(string.IsNullOrWhiteSpace(workspace)
            ? Directory.GetCurrentDirectory()
            : workspace);
    }

    private static void PrintHelp()
    {
        var model = LoadCommandSurface();
        var application = FindApplication(model);
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-weave <command> [--workspace <path>] [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        foreach (var command in model.CommandList
                     .Where(command => ReferenceEquals(command.Application, application))
                     .Where(command => command.ParentCommand is null)
                     .Where(command => !string.Equals(command.Token, "help", StringComparison.Ordinal))
                     .OrderBy(command => command.Token, StringComparer.Ordinal))
        {
            var description = string.IsNullOrWhiteSpace(command.Description) ? string.Empty : "  " + command.Description;
            Console.WriteLine($"  {command.Token,-18}{description}");
        }

        Console.WriteLine($"  {"help",-18}Show help.");
        Console.WriteLine();
        Console.WriteLine("Next: meta-weave help <command>");
    }

    private static void PrintCommandHelp(IReadOnlyList<string> route)
    {
        var model = LoadCommandSurface();
        var application = FindApplication(model);
        var command = FindCommand(model, application, route);
        if (command is null)
        {
            Console.Error.WriteLine($"Command '{string.Join(" ", route)}' is not modeled.");
            return;
        }

        var executableCommand = model.ExecutableCommandList.SingleOrDefault(item => ReferenceEquals(item.Command, command));
        if (executableCommand is null)
        {
            Console.Error.WriteLine($"Command '{BuildRoute(command)}' is not runnable.");
            return;
        }

        var parameters = EffectiveParameters(model, application, executableCommand).ToList();
        var positionals = OrderPositionals(model, executableCommand).ToList();
        Console.WriteLine("Usage:");
        Console.Write($"  meta-weave {BuildRoute(command)}");
        foreach (var positional in positionals)
        {
            Console.Write($" <{positional.Parameter.Name}>");
        }

        var options = parameters
            .Select(parameter => (Parameter: parameter, Token: model.OptionTokenList.FirstOrDefault(token => ReferenceEquals(token.Option.Parameter, parameter))))
            .Where(item => item.Token is not null)
            .ToList();
        foreach (var option in options)
        {
            var token = option.Token!;
            var valueLabel = ValueLabel(option.Parameter);
            var required = IsTrue(option.Parameter.IsRequired);
            Console.Write(required ? $" {token.Token}{valueLabel}" : $" [{token.Token}{valueLabel}]");
        }

        Console.WriteLine();
        if (!string.IsNullOrWhiteSpace(command.Description))
        {
            Console.WriteLine();
            Console.WriteLine(command.Description);
        }

        if (options.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Options:");
            foreach (var parameter in parameters)
            {
                var optionToken = model.OptionTokenList.FirstOrDefault(token => ReferenceEquals(token.Option.Parameter, parameter));
                if (optionToken is null)
                {
                    continue;
                }

                var label = optionToken.Token + ValueLabel(parameter);
                var description = string.IsNullOrWhiteSpace(parameter.Description) ? string.Empty : "  " + parameter.Description;
                Console.WriteLine($"  {label,-28}{description}");
            }
        }
    }

    private static MetaCliModel LoadCommandSurface() =>
        MetaCliModel.LoadFromXmlWorkspace(CommandWorkspacePath, searchUpward: false);

    private static Application FindApplication(MetaCliModel model) =>
        model.ApplicationList.Single(application => string.Equals(application.Id, ApplicationId, StringComparison.Ordinal));

    private static Command? FindCommand(MetaCliModel model, Application application, IReadOnlyList<string> route)
    {
        Command? current = null;
        foreach (var token in route)
        {
            current = model.CommandList.SingleOrDefault(command =>
                ReferenceEquals(command.Application, application) &&
                ReferenceEquals(command.ParentCommand, current) &&
                string.Equals(command.Token, token, StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private static IEnumerable<Parameter> EffectiveParameters(
        MetaCliModel model,
        Application application,
        ExecutableCommand executableCommand)
    {
        foreach (var parameter in model.ApplicationParameterList
                     .Where(item => ReferenceEquals(item.Application, application))
                     .Select(item => item.Parameter))
        {
            yield return parameter;
        }

        foreach (var parameter in model.ExecutableCommandParameterList
                     .Where(item => ReferenceEquals(item.ExecutableCommand, executableCommand))
                     .Select(item => item.Parameter))
        {
            yield return parameter;
        }
    }

    private static IEnumerable<PositionalArgument> OrderPositionals(MetaCliModel model, ExecutableCommand executableCommand)
    {
        var commandParameters = model.ExecutableCommandParameterList
            .Where(item => ReferenceEquals(item.ExecutableCommand, executableCommand))
            .Select(item => item.Parameter)
            .ToHashSet();
        var positionals = model.PositionalArgumentList
            .Where(argument => commandParameters.Contains(argument.Parameter))
            .ToList();
        var current = positionals.SingleOrDefault(argument => argument.PreviousArgument is null);
        while (current is not null)
        {
            yield return current;
            var previous = current;
            current = positionals.SingleOrDefault(argument => ReferenceEquals(argument.PreviousArgument, previous));
        }
    }

    private static string BuildRoute(Command command)
    {
        var parts = new Stack<string>();
        for (var current = command; current is not null; current = current.ParentCommand!)
        {
            if (!string.IsNullOrWhiteSpace(current.Token))
            {
                parts.Push(current.Token);
            }
        }

        return string.Join(" ", parts);
    }

    private static string ValueLabel(Parameter parameter)
    {
        var arity = parameter.ValueShape.ValueArity;
        if (string.Equals(arity.MaxValueCount, "0", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var label = string.IsNullOrWhiteSpace(parameter.ValueShape.ValueLabel)
            ? "<value>"
            : parameter.ValueShape.ValueLabel;
        return " " + label;
    }

    private static bool IsTrue(string? value) =>
        bool.TryParse(value, out var parsed) && parsed;

    private static bool IsHelpToken(string value)
    {
        return string.Equals(value, "help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
    }

    private static void Line(string message)
    {
        Presenter.WriteInfo(message);
    }
}
