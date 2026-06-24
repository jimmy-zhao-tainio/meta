using System.Globalization;
using Meta.Core.Presentation;
using MetaCli.Core;

internal static class Program
{
    private static readonly ConsolePresenter Presenter = new();
    private static readonly MetaCliWorkspaceService Service = new();

    private static int Main(string[] args)
    {
        if (args.Length == 0 || IsHelpToken(args[0]))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            if (string.Equals(args[0], "--new-workspace", StringComparison.OrdinalIgnoreCase))
            {
                return RunCreate(args);
            }

            return args[0].ToLowerInvariant() switch
            {
                "show" => RunWithHelp(args, "show", RunShow),
                "add-application" => RunWithHelp(args, "add-application", RunAddApplication),
                "add-command" => RunWithHelp(args, "add-command", RunAddCommand),
                "add-executable-command" => RunWithHelp(args, "add-executable-command", RunAddExecutableCommand),
                "set-default-command" => RunWithHelp(args, "set-default-command", RunSetDefaultCommand),
                "add-value-arity" => RunWithHelp(args, "add-value-arity", RunAddValueArity),
                "add-value-shape" => RunWithHelp(args, "add-value-shape", RunAddValueShape),
                "add-allowed-value" => RunWithHelp(args, "add-allowed-value", RunAddAllowedValue),
                "add-option" => RunWithHelp(args, "add-option", RunAddOption),
                "add-option-token" => RunWithHelp(args, "add-option-token", RunAddOptionToken),
                "add-positional" => RunWithHelp(args, "add-positional", RunAddPositional),
                "add-parameter-group" => RunWithHelp(args, "add-parameter-group", RunAddParameterGroup),
                "add-parameter-group-member" => RunWithHelp(args, "add-parameter-group-member", RunAddParameterGroupMember),
                "add-duplicate-option-behavior" => RunWithHelp(args, "add-duplicate-option-behavior", RunAddDuplicateOptionBehavior),
                "add-unknown-token-behavior" => RunWithHelp(args, "add-unknown-token-behavior", RunAddUnknownTokenBehavior),
                "add-parser-policy" => RunWithHelp(args, "add-parser-policy", RunAddParserPolicy),
                "add-output-format" => RunWithHelp(args, "add-output-format", RunAddOutputFormat),
                "add-output-stream" => RunWithHelp(args, "add-output-stream", RunAddOutputStream),
                "add-output" => RunWithHelp(args, "add-output", RunAddOutput),
                "add-exit-code" => RunWithHelp(args, "add-exit-code", RunAddExitCode),
                "help" => ReturnHelp(),
                _ => Fail($"unknown command '{args[0]}'.", "meta-cli help"),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return Fail(
                "Command failed.",
                NextAfterFailure(args),
                exitCode: 4,
                details: new[] { ex.Message });
        }
    }

    private static int RunCreate(string[] args)
    {
        var parse = ParseOptions(args, 0, new[] { "--new-workspace" });
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, "meta-cli --new-workspace <path>");
        }

        var workspacePath = RequireOption(parse, "--new-workspace", "meta-cli --new-workspace <path>");
        var fullPath = Path.GetFullPath(workspacePath);
        if (Directory.Exists(fullPath) && Directory.EnumerateFileSystemEntries(fullPath).Any())
        {
            return Fail(
                "target directory must be empty.",
                "choose a new folder or empty the target directory and retry.",
                exitCode: 4,
                details: new[] { $"Target: {fullPath}" });
        }

        Service.CreateEmpty().SaveToXmlWorkspace(fullPath);
        Presenter.WriteInfo($"Created MetaCli workspace: {fullPath}");
        return 0;
    }

    private static int RunShow(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOnly());
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("show"));
        }

        var result = Service.Show(GetWorkspace(parse));
        Presenter.WriteInfo("MetaCli workspace");
        if (result.Applications.Count == 0)
        {
            Presenter.WriteInfo("  applications: none");
            return 0;
        }

        foreach (var application in result.Applications)
        {
            Presenter.WriteInfo($"  application: {application.Name} ({application.Id})");
            if (!string.IsNullOrWhiteSpace(application.ExecutableName))
            {
                Presenter.WriteInfo($"    executable: {application.ExecutableName}");
            }

            if (!string.IsNullOrWhiteSpace(application.Version))
            {
                Presenter.WriteInfo($"    version: {application.Version}");
            }

            if (!string.IsNullOrWhiteSpace(application.Description))
            {
                Presenter.WriteInfo($"    description: {application.Description}");
            }

            Presenter.WriteInfo($"    commands: {Count(application.CommandCount)} ({Count(application.ExecutableCommandCount)} runnable)");
            Presenter.WriteInfo($"    parameters: {Count(application.ParameterCount)} ({Plural(application.OptionCount, "option")}, {Plural(application.PositionalArgumentCount, "positional")}, {Plural(application.ParameterGroupCount, "group")})");

            if (application.Commands.Count == 0)
            {
                Presenter.WriteInfo("    command surface: none");
                continue;
            }

            Presenter.WriteInfo("    command surface:");
            foreach (var command in application.Commands)
            {
                var route = string.IsNullOrWhiteSpace(command.Route) ? "(default)" : command.Route;
                Presenter.WriteInfo($"      {route} {CommandTags(command)}");
                if (!string.IsNullOrWhiteSpace(command.Description))
                {
                    Presenter.WriteInfo($"        {command.Description}");
                }

                if (command.ParameterCount > 0)
                {
                    Presenter.WriteInfo($"        parameters: {Count(command.ParameterCount)} ({Plural(command.OptionCount, "option")}, {Plural(command.PositionalArgumentCount, "positional")})");
                }
            }
        }

        return 0;
    }

    private static int RunAddApplication(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--id", "--name", "--executable-name", "--version", "--description"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-application"));
        }

        var row = Service.AddApplication(
            GetWorkspace(parse),
            RequireOption(parse, "--id", HelpCommand("add-application")),
            RequireOption(parse, "--name", HelpCommand("add-application")),
            GetOption(parse, "--executable-name"),
            GetOption(parse, "--version"),
            GetOption(parse, "--description"));
        WriteDone($"Added application {row.Id} ({row.Name}).");
        return 0;
    }

    private static int RunAddCommand(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--id", "--application", "--name", "--token", "--parent-command", "--description"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-command"));
        }

        var row = Service.AddCommand(
            GetWorkspace(parse),
            RequireOption(parse, "--id", HelpCommand("add-command")),
            RequireOption(parse, "--application", HelpCommand("add-command")),
            RequireOption(parse, "--name", HelpCommand("add-command")),
            RequireOption(parse, "--token", HelpCommand("add-command")),
            GetOption(parse, "--parent-command"),
            GetOption(parse, "--description"));
        WriteDone($"Added command {MetaCliWorkspaceService.BuildRoute(row)} ({row.Id}).");
        return 0;
    }

    private static int RunAddExecutableCommand(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--id", "--command"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-executable-command"));
        }

        var row = Service.AddExecutableCommand(
            GetWorkspace(parse),
            RequireOption(parse, "--id", HelpCommand("add-executable-command")),
            RequireOption(parse, "--command", HelpCommand("add-executable-command")));
        WriteDone($"Made command runnable: {MetaCliWorkspaceService.BuildRoute(row.Command)} ({row.Id}).");
        return 0;
    }

    private static int RunSetDefaultCommand(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--application", "--command-id", "--executable-command-id", "--name", "--description"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("set-default-command"));
        }

        var row = Service.SetDefaultCommand(
            GetWorkspace(parse),
            RequireOption(parse, "--application", HelpCommand("set-default-command")),
            RequireOption(parse, "--command-id", HelpCommand("set-default-command")),
            RequireOption(parse, "--executable-command-id", HelpCommand("set-default-command")),
            RequireOption(parse, "--name", HelpCommand("set-default-command")),
            GetOption(parse, "--description"));
        WriteDone($"Set default command for {row.Application.Id}: {row.ExecutableCommand.Command.Name}.");
        return 0;
    }

    private static int RunAddValueArity(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--id", "--name", "--min-value-count", "--max-value-count", "--description"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-value-arity"));
        }

        var row = Service.AddValueArity(
            GetWorkspace(parse),
            RequireOption(parse, "--id", HelpCommand("add-value-arity")),
            RequireOption(parse, "--name", HelpCommand("add-value-arity")),
            RequireOption(parse, "--min-value-count", HelpCommand("add-value-arity")),
            GetOption(parse, "--max-value-count"),
            GetOption(parse, "--description"));
        WriteDone($"Added value arity {row.Id}: {row.Name} ({row.MinValueCount}..{(row.MaxValueCount ?? "*")}).");
        return 0;
    }

    private static int RunAddValueShape(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--id", "--name", "--value-arity", "--value-label", "--allows-option-like-value", "--description"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-value-shape"));
        }

        var row = Service.AddValueShape(
            GetWorkspace(parse),
            RequireOption(parse, "--id", HelpCommand("add-value-shape")),
            RequireOption(parse, "--name", HelpCommand("add-value-shape")),
            RequireOption(parse, "--value-arity", HelpCommand("add-value-shape")),
            GetOption(parse, "--value-label"),
            GetBoolOption(parse, "--allows-option-like-value"),
            GetOption(parse, "--description"));
        WriteDone($"Added value shape {row.Id}: {row.Name}.");
        return 0;
    }

    private static int RunAddAllowedValue(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--id", "--value-shape", "--value", "--previous-value", "--description"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-allowed-value"));
        }

        var row = Service.AddAllowedValue(
            GetWorkspace(parse),
            RequireOption(parse, "--id", HelpCommand("add-allowed-value")),
            RequireOption(parse, "--value-shape", HelpCommand("add-allowed-value")),
            RequireOption(parse, "--value", HelpCommand("add-allowed-value")),
            GetOption(parse, "--previous-value"),
            GetOption(parse, "--description"));
        WriteDone($"Added allowed value {row.Value} for {row.ValueShape.Name}.");
        return 0;
    }

    private static int RunAddOption(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--parameter-id", "--option-id", "--executable-command", "--name", "--value-shape", "--token-id", "--token", "--required", "--repeatable", "--default-value", "--description"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-option"));
        }

        var row = Service.AddOption(
            GetWorkspace(parse),
            RequireOption(parse, "--parameter-id", HelpCommand("add-option")),
            RequireOption(parse, "--option-id", HelpCommand("add-option")),
            RequireOption(parse, "--executable-command", HelpCommand("add-option")),
            RequireOption(parse, "--name", HelpCommand("add-option")),
            RequireOption(parse, "--value-shape", HelpCommand("add-option")),
            RequireOption(parse, "--token-id", HelpCommand("add-option")),
            RequireOption(parse, "--token", HelpCommand("add-option")),
            GetBoolOption(parse, "--required"),
            GetBoolOption(parse, "--repeatable"),
            GetOption(parse, "--default-value"),
            GetOption(parse, "--description"));
        WriteDone($"Added option {row.Parameter.Name} ({GetOption(parse, "--token")}).");
        return 0;
    }

    private static int RunAddOptionToken(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--id", "--option", "--token", "--previous-token"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-option-token"));
        }

        var row = Service.AddOptionToken(
            GetWorkspace(parse),
            RequireOption(parse, "--id", HelpCommand("add-option-token")),
            RequireOption(parse, "--option", HelpCommand("add-option-token")),
            RequireOption(parse, "--token", HelpCommand("add-option-token")),
            RequireOption(parse, "--previous-token", HelpCommand("add-option-token")));
        WriteDone($"Added option alias {row.Token}.");
        return 0;
    }

    private static int RunAddPositional(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--parameter-id", "--positional-id", "--executable-command", "--name", "--value-shape", "--previous-argument", "--required", "--repeatable", "--default-value", "--description"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-positional"));
        }

        var row = Service.AddPositional(
            GetWorkspace(parse),
            RequireOption(parse, "--parameter-id", HelpCommand("add-positional")),
            RequireOption(parse, "--positional-id", HelpCommand("add-positional")),
            RequireOption(parse, "--executable-command", HelpCommand("add-positional")),
            RequireOption(parse, "--name", HelpCommand("add-positional")),
            RequireOption(parse, "--value-shape", HelpCommand("add-positional")),
            GetOption(parse, "--previous-argument"),
            GetBoolOption(parse, "--required"),
            GetBoolOption(parse, "--repeatable"),
            GetOption(parse, "--default-value"),
            GetOption(parse, "--description"));
        WriteDone($"Added positional argument {row.Parameter.Name}.");
        return 0;
    }

    private static int RunAddParameterGroup(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--id", "--executable-command", "--name", "--member-id", "--parameter", "--required", "--allows-multiple", "--description"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-parameter-group"));
        }

        var row = Service.AddParameterGroup(
            GetWorkspace(parse),
            RequireOption(parse, "--id", HelpCommand("add-parameter-group")),
            RequireOption(parse, "--executable-command", HelpCommand("add-parameter-group")),
            RequireOption(parse, "--name", HelpCommand("add-parameter-group")),
            RequireOption(parse, "--member-id", HelpCommand("add-parameter-group")),
            RequireOption(parse, "--parameter", HelpCommand("add-parameter-group")),
            GetBoolOption(parse, "--required"),
            GetBoolOption(parse, "--allows-multiple"),
            GetOption(parse, "--description"));
        WriteDone($"Added parameter group {row.Name}.");
        return 0;
    }

    private static int RunAddParameterGroupMember(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--id", "--parameter-group", "--parameter", "--previous-member"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-parameter-group-member"));
        }

        var row = Service.AddParameterGroupMember(
            GetWorkspace(parse),
            RequireOption(parse, "--id", HelpCommand("add-parameter-group-member")),
            RequireOption(parse, "--parameter-group", HelpCommand("add-parameter-group-member")),
            RequireOption(parse, "--parameter", HelpCommand("add-parameter-group-member")),
            RequireOption(parse, "--previous-member", HelpCommand("add-parameter-group-member")));
        WriteDone($"Added {row.Parameter.Name} to group {row.ParameterGroup.Name}.");
        return 0;
    }

    private static int RunAddDuplicateOptionBehavior(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--id", "--name", "--description"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-duplicate-option-behavior"));
        }

        var row = Service.AddDuplicateOptionBehavior(
            GetWorkspace(parse),
            RequireOption(parse, "--id", HelpCommand("add-duplicate-option-behavior")),
            RequireOption(parse, "--name", HelpCommand("add-duplicate-option-behavior")),
            GetOption(parse, "--description"));
        WriteDone($"Added duplicate-option behavior {row.Name}.");
        return 0;
    }

    private static int RunAddUnknownTokenBehavior(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--id", "--name", "--description"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-unknown-token-behavior"));
        }

        var row = Service.AddUnknownTokenBehavior(
            GetWorkspace(parse),
            RequireOption(parse, "--id", HelpCommand("add-unknown-token-behavior")),
            RequireOption(parse, "--name", HelpCommand("add-unknown-token-behavior")),
            GetOption(parse, "--description"));
        WriteDone($"Added unknown-token behavior {row.Name}.");
        return 0;
    }

    private static int RunAddParserPolicy(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions(
            "--id",
            "--application",
            "--name",
            "--stop-parsing-token",
            "--allows-equals-value-syntax",
            "--allows-options-after-positionals",
            "--allows-short-option-clusters",
            "--duplicate-option-behavior",
            "--unknown-token-behavior",
            "--description"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-parser-policy"));
        }

        var row = Service.AddParserPolicy(
            GetWorkspace(parse),
            RequireOption(parse, "--id", HelpCommand("add-parser-policy")),
            RequireOption(parse, "--application", HelpCommand("add-parser-policy")),
            RequireOption(parse, "--name", HelpCommand("add-parser-policy")),
            GetOption(parse, "--stop-parsing-token"),
            GetBoolOption(parse, "--allows-equals-value-syntax"),
            GetBoolOption(parse, "--allows-options-after-positionals"),
            GetBoolOption(parse, "--allows-short-option-clusters"),
            GetOption(parse, "--duplicate-option-behavior"),
            GetOption(parse, "--unknown-token-behavior"),
            GetOption(parse, "--description"));
        WriteDone($"Added parser policy {row.Name} for {row.Application.Id}.");
        return 0;
    }

    private static int RunAddOutputFormat(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--id", "--name", "--content-type", "--description"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-output-format"));
        }

        var row = Service.AddOutputFormat(
            GetWorkspace(parse),
            RequireOption(parse, "--id", HelpCommand("add-output-format")),
            RequireOption(parse, "--name", HelpCommand("add-output-format")),
            GetOption(parse, "--content-type"),
            GetOption(parse, "--description"));
        WriteDone($"Added output format {row.Name}.");
        return 0;
    }

    private static int RunAddOutputStream(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--id", "--name", "--description"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-output-stream"));
        }

        var row = Service.AddOutputStream(
            GetWorkspace(parse),
            RequireOption(parse, "--id", HelpCommand("add-output-stream")),
            RequireOption(parse, "--name", HelpCommand("add-output-stream")),
            GetOption(parse, "--description"));
        WriteDone($"Added output stream {row.Name}.");
        return 0;
    }

    private static int RunAddOutput(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--id", "--executable-command", "--name", "--output-format", "--output-stream", "--description"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-output"));
        }

        var row = Service.AddOutput(
            GetWorkspace(parse),
            RequireOption(parse, "--id", HelpCommand("add-output")),
            RequireOption(parse, "--executable-command", HelpCommand("add-output")),
            RequireOption(parse, "--name", HelpCommand("add-output")),
            GetOption(parse, "--output-format"),
            GetOption(parse, "--output-stream"),
            GetOption(parse, "--description"));
        WriteDone($"Added output {row.Name} for {MetaCliWorkspaceService.BuildRoute(row.ExecutableCommand.Command)}.");
        return 0;
    }

    private static int RunAddExitCode(string[] args)
    {
        var parse = ParseOptions(args, 1, WorkspaceOptions("--id", "--application", "--code", "--name", "--executable-command", "--description"));
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("add-exit-code"));
        }

        var row = Service.AddExitCode(
            GetWorkspace(parse),
            RequireOption(parse, "--id", HelpCommand("add-exit-code")),
            RequireOption(parse, "--application", HelpCommand("add-exit-code")),
            RequireOption(parse, "--code", HelpCommand("add-exit-code")),
            RequireOption(parse, "--name", HelpCommand("add-exit-code")),
            GetOption(parse, "--executable-command"),
            GetOption(parse, "--description"));
        WriteDone($"Added exit code {row.Code}: {row.Name}.");
        return 0;
    }

    private static int RunWithHelp(string[] args, string commandName, Func<string[], int> run)
    {
        if (args.Length > 1 && IsHelpToken(args[1]))
        {
            PrintCommandHelp(commandName);
            return 0;
        }

        return run(args);
    }

    private static int ReturnHelp()
    {
        PrintHelp();
        return 0;
    }

    private static void PrintHelp()
    {
        Presenter.WriteUsage("meta-cli --new-workspace <path>");
        Presenter.WriteUsage("meta-cli <command> [--workspace <path>] [options]");
        Presenter.WriteInfo(string.Empty);
        Presenter.WriteCommandCatalog("Commands:", new[]
        {
            ("show", "Show the authored command surface."),
            ("add-application", "Add a CLI application."),
            ("add-command", "Add a command path segment."),
            ("add-executable-command", "Mark a command as runnable."),
            ("set-default-command", "Set the application-level default command."),
            ("add-value-arity", "Define how many values a parameter accepts."),
            ("add-value-shape", "Define a parameter value shape."),
            ("add-allowed-value", "Add one allowed value for a value shape."),
            ("add-option", "Add an option with its primary token."),
            ("add-option-token", "Add an additional token for an option."),
            ("add-positional", "Add a positional argument."),
            ("add-parameter-group", "Start a parameter choice group."),
            ("add-parameter-group-member", "Add another parameter to a group."),
            ("add-duplicate-option-behavior", "Define how duplicate options are handled."),
            ("add-unknown-token-behavior", "Define how unknown tokens are handled."),
            ("add-parser-policy", "Attach parser behavior to an application."),
            ("add-output-format", "Define an output format."),
            ("add-output-stream", "Define an output stream."),
            ("add-output", "Describe output from a runnable command."),
            ("add-exit-code", "Describe an exit code."),
            ("help", "Show help."),
        });
        Presenter.WriteInfo(string.Empty);
        Presenter.WriteNext("meta-cli --new-workspace <path>");
    }

    private static void PrintCommandHelp(string commandName)
    {
        switch (commandName)
        {
            case "show":
                WriteUsageAndOptions("meta-cli show [--workspace <path>]", WorkspaceHelp());
                break;
            case "add-application":
                WriteUsageAndOptions("meta-cli add-application [--workspace <path>] --id <id> --name <name> [--executable-name <name>] [--version <value>] [--description <text>]",
                    WorkspaceHelp(),
                    ("--id <id>", "Required. Application id."),
                    ("--name <name>", "Required. Application name."),
                    ("--executable-name <name>", "Optional executable name."),
                    ("--version <value>", "Optional version."),
                    ("--description <text>", "Optional description."));
                break;
            case "add-command":
                WriteUsageAndOptions("meta-cli add-command [--workspace <path>] --id <id> --application <id> --name <name> --token <token> [--parent-command <id>] [--description <text>]",
                    WorkspaceHelp(),
                    ("--id <id>", "Required. Command id."),
                    ("--application <id>", "Required. Application id."),
                    ("--name <name>", "Required. Command name."),
                    ("--token <token>", "Required. Command token."),
                    ("--parent-command <id>", "Optional parent command id."),
                    ("--description <text>", "Optional description."));
                break;
            case "add-executable-command":
                WriteUsageAndOptions("meta-cli add-executable-command [--workspace <path>] --id <id> --command <id>",
                    WorkspaceHelp(),
                    ("--id <id>", "Required. ExecutableCommand id."),
                    ("--command <id>", "Required. Command id."));
                break;
            case "set-default-command":
                WriteUsageAndOptions("meta-cli set-default-command [--workspace <path>] --application <id> --command-id <id> --executable-command-id <id> --name <name> [--description <text>]",
                    WorkspaceHelp(),
                    ("--application <id>", "Required. Application id."),
                    ("--command-id <id>", "Required. Tokenless default Command id to create."),
                    ("--executable-command-id <id>", "Required. ExecutableCommand id to create."),
                    ("--name <name>", "Required. Default command name."),
                    ("--description <text>", "Optional description."));
                break;
            case "add-value-arity":
                WriteUsageAndOptions("meta-cli add-value-arity [--workspace <path>] --id <id> --name <name> --min-value-count <count> [--max-value-count <count>] [--description <text>]",
                    WorkspaceHelp(),
                    ("--id <id>", "Required. ValueArity id."),
                    ("--name <name>", "Required. ValueArity name."),
                    ("--min-value-count <count>", "Required. Minimum cardinality."),
                    ("--max-value-count <count>", "Optional maximum cardinality. Omit for unbounded."),
                    ("--description <text>", "Optional description."));
                break;
            case "add-value-shape":
                WriteUsageAndOptions("meta-cli add-value-shape [--workspace <path>] --id <id> --name <name> --value-arity <id> [--value-label <label>] [--allows-option-like-value true|false] [--description <text>]",
                    WorkspaceHelp(),
                    ("--id <id>", "Required. ValueShape id."),
                    ("--name <name>", "Required. ValueShape name."),
                    ("--value-arity <id>", "Required. ValueArity id."),
                    ("--value-label <label>", "Optional display label."),
                    ("--allows-option-like-value true|false", "Optional boolean."),
                    ("--description <text>", "Optional description."));
                break;
            case "add-allowed-value":
                WriteUsageAndOptions("meta-cli add-allowed-value [--workspace <path>] --id <id> --value-shape <id> --value <value> [--previous-value <id>] [--description <text>]",
                    WorkspaceHelp(),
                    ("--id <id>", "Required. AllowedValue id."),
                    ("--value-shape <id>", "Required. ValueShape id."),
                    ("--value <value>", "Required. Allowed value."),
                    ("--previous-value <id>", "Optional previous AllowedValue id."),
                    ("--description <text>", "Optional description."));
                break;
            case "add-option":
                WriteUsageAndOptions("meta-cli add-option [--workspace <path>] --parameter-id <id> --option-id <id> --executable-command <id> --name <name> --value-shape <id> --token-id <id> --token <token> [--required true|false] [--repeatable true|false] [--default-value <value>] [--description <text>]",
                    WorkspaceHelp(),
                    ("--parameter-id <id>", "Required. Parameter id to create."),
                    ("--option-id <id>", "Required. Option id to create."),
                    ("--executable-command <id>", "Required. ExecutableCommand id."),
                    ("--name <name>", "Required. Parameter name."),
                    ("--value-shape <id>", "Required. ValueShape id."),
                    ("--token-id <id>", "Required. Primary OptionToken id to create."),
                    ("--token <token>", "Required. Primary option token text."),
                    ("--required true|false", "Optional boolean."),
                    ("--repeatable true|false", "Optional boolean."),
                    ("--default-value <value>", "Optional default value."),
                    ("--description <text>", "Optional description."));
                break;
            case "add-option-token":
                WriteUsageAndOptions("meta-cli add-option-token [--workspace <path>] --id <id> --option <id> --token <token> --previous-token <id>",
                    WorkspaceHelp(),
                    ("--id <id>", "Required. Alias OptionToken id."),
                    ("--option <id>", "Required. Option id."),
                    ("--token <token>", "Required. Alias token text."),
                    ("--previous-token <id>", "Required. Previous OptionToken id in the option token chain."));
                break;
            case "add-positional":
                WriteUsageAndOptions("meta-cli add-positional [--workspace <path>] --parameter-id <id> --positional-id <id> --executable-command <id> --name <name> --value-shape <id> [--previous-argument <id>] [--required true|false] [--repeatable true|false] [--default-value <value>] [--description <text>]",
                    WorkspaceHelp(),
                    ("--parameter-id <id>", "Required. Parameter id to create."),
                    ("--positional-id <id>", "Required. PositionalArgument id to create."),
                    ("--executable-command <id>", "Required. ExecutableCommand id."),
                    ("--name <name>", "Required. Parameter name."),
                    ("--value-shape <id>", "Required. ValueShape id."),
                    ("--previous-argument <id>", "Optional previous PositionalArgument id."),
                    ("--required true|false", "Optional boolean."),
                    ("--repeatable true|false", "Optional boolean."),
                    ("--default-value <value>", "Optional default value."),
                    ("--description <text>", "Optional description."));
                break;
            case "add-parameter-group":
                WriteUsageAndOptions("meta-cli add-parameter-group [--workspace <path>] --id <id> --executable-command <id> --name <name> --member-id <id> --parameter <id> [--required true|false] [--allows-multiple true|false] [--description <text>]",
                    WorkspaceHelp(),
                    ("--id <id>", "Required. ParameterGroup id."),
                    ("--executable-command <id>", "Required. ExecutableCommand id."),
                    ("--name <name>", "Required. Group name."),
                    ("--member-id <id>", "Required. First ParameterGroupMember id to create."),
                    ("--parameter <id>", "Required. First member Parameter id."),
                    ("--required true|false", "Optional boolean."),
                    ("--allows-multiple true|false", "Optional boolean."),
                    ("--description <text>", "Optional description."));
                break;
            case "add-parameter-group-member":
                WriteUsageAndOptions("meta-cli add-parameter-group-member [--workspace <path>] --id <id> --parameter-group <id> --parameter <id> --previous-member <id>",
                    WorkspaceHelp(),
                    ("--id <id>", "Required. ParameterGroupMember id."),
                    ("--parameter-group <id>", "Required. ParameterGroup id."),
                    ("--parameter <id>", "Required. Parameter id."),
                    ("--previous-member <id>", "Required. Previous ParameterGroupMember id in the group member chain."));
                break;
            case "add-duplicate-option-behavior":
                WriteUsageAndOptions("meta-cli add-duplicate-option-behavior [--workspace <path>] --id <id> --name <name> [--description <text>]",
                    WorkspaceHelp(),
                    ("--id <id>", "Required. DuplicateOptionBehavior id."),
                    ("--name <name>", "Required. Behavior name."),
                    ("--description <text>", "Optional description."));
                break;
            case "add-unknown-token-behavior":
                WriteUsageAndOptions("meta-cli add-unknown-token-behavior [--workspace <path>] --id <id> --name <name> [--description <text>]",
                    WorkspaceHelp(),
                    ("--id <id>", "Required. UnknownTokenBehavior id."),
                    ("--name <name>", "Required. Behavior name."),
                    ("--description <text>", "Optional description."));
                break;
            case "add-parser-policy":
                WriteUsageAndOptions("meta-cli add-parser-policy [--workspace <path>] --id <id> --application <id> --name <name> [--stop-parsing-token <token>] [--allows-equals-value-syntax true|false] [--allows-options-after-positionals true|false] [--allows-short-option-clusters true|false] [--duplicate-option-behavior <id>] [--unknown-token-behavior <id>] [--description <text>]",
                    WorkspaceHelp(),
                    ("--id <id>", "Required. ParserPolicy id."),
                    ("--application <id>", "Required. Application id."),
                    ("--name <name>", "Required. Parser policy name."),
                    ("--stop-parsing-token <token>", "Optional token that stops option parsing."),
                    ("--allows-equals-value-syntax true|false", "Optional boolean. Defaults to false when unset."),
                    ("--allows-options-after-positionals true|false", "Optional boolean. Defaults to false when unset."),
                    ("--allows-short-option-clusters true|false", "Optional boolean. Defaults to false when unset."),
                    ("--duplicate-option-behavior <id>", "Optional DuplicateOptionBehavior id."),
                    ("--unknown-token-behavior <id>", "Optional UnknownTokenBehavior id."),
                    ("--description <text>", "Optional description."));
                break;
            case "add-output-format":
                WriteUsageAndOptions("meta-cli add-output-format [--workspace <path>] --id <id> --name <name> [--content-type <value>] [--description <text>]",
                    WorkspaceHelp(),
                    ("--id <id>", "Required. OutputFormat id."),
                    ("--name <name>", "Required. Output format name."),
                    ("--content-type <value>", "Optional content type."),
                    ("--description <text>", "Optional description."));
                break;
            case "add-output-stream":
                WriteUsageAndOptions("meta-cli add-output-stream [--workspace <path>] --id <id> --name <name> [--description <text>]",
                    WorkspaceHelp(),
                    ("--id <id>", "Required. OutputStream id."),
                    ("--name <name>", "Required. Output stream name."),
                    ("--description <text>", "Optional description."));
                break;
            case "add-output":
                WriteUsageAndOptions("meta-cli add-output [--workspace <path>] --id <id> --executable-command <id> --name <name> [--output-format <id>] [--output-stream <id>] [--description <text>]",
                    WorkspaceHelp(),
                    ("--id <id>", "Required. Output id."),
                    ("--executable-command <id>", "Required. ExecutableCommand id."),
                    ("--name <name>", "Required. Output name."),
                    ("--output-format <id>", "Optional OutputFormat id."),
                    ("--output-stream <id>", "Optional OutputStream id."),
                    ("--description <text>", "Optional description."));
                break;
            case "add-exit-code":
                WriteUsageAndOptions("meta-cli add-exit-code [--workspace <path>] --id <id> --application <id> --code <code> --name <name> [--executable-command <id>] [--description <text>]",
                    WorkspaceHelp(),
                    ("--id <id>", "Required. ExitCode id."),
                    ("--application <id>", "Required. Application id."),
                    ("--code <code>", "Required. Exit code value."),
                    ("--name <name>", "Required. Exit code name."),
                    ("--executable-command <id>", "Optional ExecutableCommand id for command-specific exit codes."),
                    ("--description <text>", "Optional description."));
                break;
            default:
                PrintHelp();
                break;
        }
    }

    private static ParseResult ParseOptions(
        string[] args,
        int startIndex,
        IReadOnlyCollection<string> valueOptions)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var valueOptionSet = valueOptions.ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var index = startIndex; index < args.Length; index++)
        {
            var arg = args[index];
            if (!valueOptionSet.Contains(arg))
            {
                return ParseResult.Fail($"unknown option '{arg}'.");
            }

            if (index + 1 >= args.Length)
            {
                return ParseResult.Fail($"missing value for {arg}.");
            }

            if (values.ContainsKey(arg))
            {
                return ParseResult.Fail($"{arg} can only be provided once.");
            }

            values[arg] = args[++index];
        }

        return new ParseResult(true, values, string.Empty);
    }

    private static IReadOnlyCollection<string> WorkspaceOnly() => WorkspaceOptions();

    private static IReadOnlyCollection<string> WorkspaceOptions(params string[] options) =>
        new[] { "--workspace" }.Concat(options).ToArray();

    private static string GetWorkspace(ParseResult parse) =>
        GetOption(parse, "--workspace") ?? Environment.CurrentDirectory;

    private static string? GetOption(ParseResult parse, string name) =>
        parse.Values.TryGetValue(name, out var value) ? value : null;

    private static bool? GetBoolOption(ParseResult parse, string name)
    {
        var value = GetOption(parse, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"{name} must be true or false.");
    }

    private static string RequireOption(ParseResult parse, string name, string next)
    {
        var value = GetOption(parse, name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException($"missing required option {name}.");
    }

    private static string HelpCommand(string commandName) => $"meta-cli {commandName} --help";

    private static string NextAfterFailure(string[] args)
    {
        if (args.Length == 0)
        {
            return "meta-cli help";
        }

        if (string.Equals(args[0], "--new-workspace", StringComparison.OrdinalIgnoreCase))
        {
            return "meta-cli --new-workspace <path>";
        }

        if (args[0].StartsWith("-", StringComparison.Ordinal))
        {
            return "meta-cli help";
        }

        return HelpCommand(args[0]);
    }

    private static bool IsHelpToken(string value) =>
        string.Equals(value, "help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);

    private static int Fail(
        string message,
        string next,
        int exitCode = 2,
        IEnumerable<string>? details = null)
    {
        Presenter.WriteInfo(EnsureSentence(message));
        var renderedDetails = (details ?? Array.Empty<string>())
            .Where(detail => !string.IsNullOrWhiteSpace(detail))
            .ToArray();

        if (renderedDetails.Length > 0)
        {
            Presenter.WriteInfo(string.Empty);
            foreach (var detail in renderedDetails)
            {
                Presenter.WriteInfo(EnsureSentence(detail));
            }
        }

        Presenter.WriteInfo(string.Empty);
        Presenter.WriteNext(next);
        return exitCode;
    }

    private static void WriteUsageAndOptions(string usage, params (string Option, string Description)[] options)
    {
        Presenter.WriteUsage(usage);
        Presenter.WriteInfo(string.Empty);
        Presenter.WriteOptionCatalog(options);
    }

    private static (string Option, string Description) WorkspaceHelp() =>
        ("--workspace <path>", "Optional. MetaCli workspace. Defaults to the current directory.");

    private static void WriteDone(string message) => Presenter.WriteInfo(EnsureSentence(message));

    private static string CommandTags(MetaCliCommandSummary command)
    {
        var tags = new List<string>();
        if (command.IsDefault)
        {
            tags.Add("default");
        }

        tags.Add(command.IsExecutable ? "runnable" : "group");
        return $"[{string.Join(", ", tags)}]";
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Plural(int count, string singular)
    {
        var suffix = count == 1 ? string.Empty : "s";
        return $"{Count(count)} {singular}{suffix}";
    }

    private static IReadOnlyList<(string Key, string Value)> NonEmptyPairs(params (string Key, string Value)[] pairs) =>
        pairs.Where(static pair => !string.IsNullOrWhiteSpace(pair.Value)).ToArray();

    private static string EnsureSentence(string value)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return normalized.EndsWith(".", StringComparison.Ordinal) ||
               normalized.EndsWith("!", StringComparison.Ordinal) ||
               normalized.EndsWith("?", StringComparison.Ordinal)
            ? normalized
            : normalized + ".";
    }

    private sealed record ParseResult(
        bool Ok,
        IReadOnlyDictionary<string, string> Values,
        string ErrorMessage)
    {
        public static ParseResult Fail(string message) =>
            new(false, new Dictionary<string, string>(), message);
    }
}
