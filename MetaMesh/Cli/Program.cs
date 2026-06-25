using Meta.Core.Presentation;
using MetaCli;
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

        if (TryHandleHelp(args))
        {
            return 0;
        }

        Environment.ExitCode = 0;
        var runtime = new MetaCliRuntime<MetaMeshModel>(CommandWorkspacePath, ApplicationId)
            .Bind("exec-help", _ => PrintHelp())
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

    private static void PrintHelp()
    {
        var model = LoadCommandSurface();
        var application = FindApplication(model);
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-mesh <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        foreach (var command in model.CommandList
                     .Where(command => ReferenceEquals(command.Application, application))
                     .Where(command => command.ParentCommand is null)
                     .Where(command => !string.Equals(command.Token, "help", StringComparison.Ordinal))
                     .OrderBy(command => command.Token, StringComparer.Ordinal))
        {
            var description = string.IsNullOrWhiteSpace(command.Description) ? string.Empty : command.Description;
            Console.WriteLine($"  {command.Token,-18}{description}");
        }

        Console.WriteLine($"  {"help",-18}Show help.");
        Console.WriteLine();
        Console.WriteLine("Next: meta-mesh help <command>");
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
        Console.Write($"  meta-mesh {BuildRoute(command)}");
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

        foreach (var positional in positionals)
        {
            Console.Write($" <{positional.Parameter.Name}>");
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
            foreach (var option in options)
            {
                var parameter = option.Parameter;
                var optionToken = option.Token!;
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
}
