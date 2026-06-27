using System.Globalization;
using Meta.Core.Presentation;
using MetaCli;
using MetaCli.Core;

internal static class Program
{
    private const string ApplicationId = "app-meta-cli";
    private const string CommandWorkspaceDirectoryName = "meta-cli.MetaCli";
    private static readonly ConsolePresenter Presenter = new();
    private static readonly MetaCliWorkspaceService Service = new();

    private static int Main(string[] args)
    {
        var exitCode = 0;
        var runtime = new MetaCliRuntime<MetaCliModel>(
                CommandWorkspacePath,
                ApplicationId,
                setExitCode: code => exitCode = code)
            .UseDefaultHelp()
            .Bind("exec-new-workspace", RunCreate)
            .Bind("exec-show", RunShow)
            .Bind("exec-add-application", RunAddApplication)
            .Bind("exec-add-command", RunAddCommand)
            .Bind("exec-add-executable-command", RunAddExecutableCommand)
            .Bind("exec-set-default-command", RunSetDefaultCommand)
            .Bind("exec-add-value-arity", RunAddValueArity)
            .Bind("exec-add-value-shape", RunAddValueShape)
            .Bind("exec-add-allowed-value", RunAddAllowedValue)
            .Bind("exec-add-application-option", RunAddApplicationOption)
            .Bind("exec-add-option", RunAddOption)
            .Bind("exec-add-option-token", RunAddOptionToken)
            .Bind("exec-add-positional", RunAddPositional)
            .Bind("exec-add-parameter-group", RunAddParameterGroup)
            .Bind("exec-add-parameter-group-member", RunAddParameterGroupMember);

        runtime.Run(args);
        return exitCode;
    }

    private static string CommandWorkspacePath =>
        Path.Combine(AppContext.BaseDirectory, CommandWorkspaceDirectoryName);

    private static void RunCreate(MetaCliInvocation invocation)
    {
        var result = Service.CreateWorkspace(
            invocation.Required("Path"),
            invocation.Optional("application"),
            invocation.Flag("standard-cli-shapes"),
            invocation.Flag("default-help"));
        Presenter.WriteInfo($"Created MetaCli workspace: {result.WorkspacePath}");
        if (result.ApplicationCount > 0)
        {
            Presenter.WriteInfo($"  applications: {Count(result.ApplicationCount)}");
        }

        if (result.ValueShapeCount > 0)
        {
            Presenter.WriteInfo($"  value shapes: {Count(result.ValueShapeCount)}");
        }

        if (result.CommandCount > 0)
        {
            Presenter.WriteInfo($"  commands: {Count(result.CommandCount)} ({Count(result.ExecutableCommandCount)} runnable)");
        }
    }

    private static void RunShow(MetaCliInvocation invocation)
    {
        var result = Service.Show(Workspace(invocation));
        Presenter.WriteInfo("MetaCli workspace");
        if (result.Applications.Count == 0)
        {
            Presenter.WriteInfo("  applications: none");
            return;
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
                var route = string.IsNullOrWhiteSpace(command.Route)
                    ? "(default)"
                    : command.Route;
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
    }

    private static void RunAddApplication(MetaCliInvocation invocation)
    {
        var row = Service.AddApplication(
            Workspace(invocation),
            invocation.Required("id"),
            invocation.Required("name"),
            invocation.Optional("executable-name"),
            invocation.Optional("version"),
            invocation.Optional("description"));
        WriteDone($"Added application {row.Id} ({row.Name}).");
    }

    private static void RunAddCommand(MetaCliInvocation invocation)
    {
        var row = Service.AddCommand(
            Workspace(invocation),
            invocation.Required("id"),
            invocation.Required("application"),
            invocation.Required("name"),
            invocation.Required("token"),
            invocation.Optional("parent-command"),
            invocation.Optional("description"));
        WriteDone($"Added command {MetaCliWorkspaceService.BuildRoute(row)} ({row.Id}).");
    }

    private static void RunAddExecutableCommand(MetaCliInvocation invocation)
    {
        var row = Service.AddExecutableCommand(
            Workspace(invocation),
            invocation.Required("id"),
            invocation.Required("command"));
        WriteDone($"Made command runnable: {MetaCliWorkspaceService.BuildRoute(row.Command)} ({row.Id}).");
    }

    private static void RunSetDefaultCommand(MetaCliInvocation invocation)
    {
        var row = Service.SetDefaultCommand(
            Workspace(invocation),
            invocation.Required("application"),
            invocation.Required("executable-command"));
        WriteDone($"Set default command for {row.Application.Id}: {row.ExecutableCommand.Command.Name}.");
    }

    private static void RunAddValueArity(MetaCliInvocation invocation)
    {
        var row = Service.AddValueArity(
            Workspace(invocation),
            invocation.Required("id"),
            invocation.Required("name"),
            invocation.Required("min-value-count"),
            invocation.Optional("max-value-count"),
            invocation.Optional("description"));
        WriteDone($"Added value arity {row.Id}: {row.Name} ({row.MinValueCount}..{(row.MaxValueCount ?? "*")}).");
    }

    private static void RunAddValueShape(MetaCliInvocation invocation)
    {
        var row = Service.AddValueShape(
            Workspace(invocation),
            invocation.Required("id"),
            invocation.Required("name"),
            invocation.Required("value-arity"),
            invocation.Optional("value-label"),
            OptionalBool(invocation, "allows-option-like-value"),
            invocation.Optional("description"));
        WriteDone($"Added value shape {row.Id}: {row.Name}.");
    }

    private static void RunAddAllowedValue(MetaCliInvocation invocation)
    {
        var row = Service.AddAllowedValue(
            Workspace(invocation),
            invocation.Required("id"),
            invocation.Required("value-shape"),
            invocation.Required("value"),
            invocation.Optional("description"));
        WriteDone($"Added allowed value {row.Value} for {row.ValueShape.Name}.");
    }

    private static void RunAddOption(MetaCliInvocation invocation)
    {
        var row = Service.AddOption(
            Workspace(invocation),
            invocation.Required("parameter-id"),
            invocation.Required("option-id"),
            invocation.Required("executable-command"),
            invocation.Required("name"),
            invocation.Required("value-shape"),
            invocation.Required("token-id"),
            invocation.Required("token"),
            OptionalBool(invocation, "required"),
            OptionalBool(invocation, "repeatable"),
            invocation.Optional("default-value"),
            invocation.Optional("description"));
        WriteDone($"Added option {row.Parameter.Name} ({invocation.Required("token")}).");
    }

    private static void RunAddApplicationOption(MetaCliInvocation invocation)
    {
        var row = Service.AddApplicationOption(
            Workspace(invocation),
            invocation.Required("parameter-id"),
            invocation.Required("option-id"),
            invocation.Required("application"),
            invocation.Required("name"),
            invocation.Required("value-shape"),
            invocation.Required("token-id"),
            invocation.Required("token"),
            OptionalBool(invocation, "required"),
            OptionalBool(invocation, "repeatable"),
            invocation.Optional("default-value"),
            invocation.Optional("description"));
        WriteDone($"Added application option {row.Parameter.Name} ({invocation.Required("token")}).");
    }

    private static void RunAddOptionToken(MetaCliInvocation invocation)
    {
        var row = Service.AddOptionToken(
            Workspace(invocation),
            invocation.Required("id"),
            invocation.Required("option"),
            invocation.Required("token"),
            invocation.Required("previous-token"));
        WriteDone($"Added option alias {row.Token}.");
    }

    private static void RunAddPositional(MetaCliInvocation invocation)
    {
        var row = Service.AddPositional(
            Workspace(invocation),
            invocation.Required("parameter-id"),
            invocation.Required("positional-id"),
            invocation.Required("executable-command"),
            invocation.Required("name"),
            invocation.Required("value-shape"),
            invocation.Optional("previous-argument"),
            OptionalBool(invocation, "required"),
            OptionalBool(invocation, "repeatable"),
            invocation.Optional("default-value"),
            invocation.Optional("description"));
        WriteDone($"Added positional argument {row.Parameter.Name}.");
    }

    private static void RunAddParameterGroup(MetaCliInvocation invocation)
    {
        var row = Service.AddParameterGroup(
            Workspace(invocation),
            invocation.Required("id"),
            invocation.Required("executable-command"),
            invocation.Required("name"),
            invocation.Required("member-id"),
            invocation.Required("parameter"),
            OptionalBool(invocation, "required"),
            OptionalBool(invocation, "allows-multiple"),
            invocation.Optional("description"));
        WriteDone($"Added parameter group {row.Name}.");
    }

    private static void RunAddParameterGroupMember(MetaCliInvocation invocation)
    {
        var row = Service.AddParameterGroupMember(
            Workspace(invocation),
            invocation.Required("id"),
            invocation.Required("parameter-group"),
            invocation.Required("parameter"),
            invocation.Required("previous-member"));
        WriteDone($"Added {row.Parameter.Name} to group {row.ParameterGroup.Name}.");
    }

    private static string Workspace(MetaCliInvocation invocation) =>
        invocation.Optional("workspace") ?? Environment.CurrentDirectory;

    private static bool? OptionalBool(MetaCliInvocation invocation, string parameter)
    {
        var value = invocation.Optional(parameter);
        return string.IsNullOrWhiteSpace(value) ? null : bool.Parse(value);
    }

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

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Plural(int count, string singular)
    {
        var suffix = count == 1 ? string.Empty : "s";
        return $"{Count(count)} {singular}{suffix}";
    }

    private static string EnsureSentence(string value)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var last = normalized[^1];
        return last is '.' or '!' or '?' ? normalized : normalized + ".";
    }
}
