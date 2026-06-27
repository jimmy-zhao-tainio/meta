namespace MetaCli.Core;

public sealed class MetaCliHelpService
{
    private readonly TextWriter output;
    private readonly TextWriter error;

    public MetaCliHelpService(TextWriter? output = null, TextWriter? error = null)
    {
        this.output = output ?? Console.Out;
        this.error = error ?? Console.Error;
    }

    public bool TryWriteHelp(
        MetaCliModel model,
        string? applicationId,
        IReadOnlyList<string> arguments,
        out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(arguments);

        exitCode = 0;
        if (!TryBuildHelpRoute(arguments, out var route))
        {
            return false;
        }

        var surface = new MetaCliCommandSurface(model, applicationId);
        if (!surface.TryResolveApplication(out var application, out var applicationError))
        {
            error.WriteLine(applicationError);
            exitCode = 2;
            return true;
        }

        if (route.Count == 0)
        {
            WriteApplicationHelp(surface, application);
            return true;
        }

        var command = surface.FindCommand(application, route);
        if (command is null)
        {
            error.WriteLine($"Unknown command '{string.Join(" ", route)}'.");
            exitCode = 2;
            return true;
        }

        var executableCommand = surface.FindExecutableCommand(command);
        if (executableCommand is not null)
        {
            WriteCommandHelp(surface, application, executableCommand);
            return true;
        }

        var children = surface.ChildCommands(command);
        if (children.Count > 0)
        {
            WriteCommandGroupHelp(surface, application, command, children);
            return true;
        }

        error.WriteLine($"Command '{surface.Route(command)}' is not runnable.");
        exitCode = 2;
        return true;
    }

    public bool TryBuildUsage(
        MetaCliModel model,
        string? applicationId,
        IReadOnlyList<string> arguments,
        out string usage)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(arguments);

        usage = string.Empty;
        var surface = new MetaCliCommandSurface(model, applicationId);
        if (!surface.TryResolveApplication(out var application, out _))
        {
            return false;
        }

        var command = ResolveCommandFromArguments(surface, application, arguments);
        if (command is null)
        {
            usage = surface.BuildApplicationUsage(application);
            return true;
        }

        var executableCommand = surface.FindExecutableCommand(command);
        if (executableCommand is not null)
        {
            usage = surface.BuildCommandUsage(application, executableCommand);
            return true;
        }

        if (surface.ChildCommands(command).Count > 0)
        {
            usage = surface.BuildCommandGroupUsage(application, command);
            return true;
        }

        return false;
    }

    public void WriteApplicationHelp(MetaCliCommandSurface surface, Application application)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(application);

        var executableName = surface.ExecutableName(application);
        output.WriteLine("Usage:");
        output.WriteLine($"  {surface.BuildApplicationUsage(application)}");
        output.WriteLine();

        var commands = surface.RootCommands(application).ToArray();
        if (commands.Length > 0)
        {
            output.WriteLine("Commands:");
            var commandWidth = Math.Max(18, commands.Max(command => (command.Token ?? command.Name).Length) + 2);
            foreach (var command in commands)
            {
                var token = string.IsNullOrWhiteSpace(command.Token) ? command.Name : command.Token;
                var description = string.IsNullOrWhiteSpace(command.Description)
                    ? string.Empty
                    : command.Description;
                output.WriteLine($"  {token.PadRight(commandWidth)}{description}");
            }

            output.WriteLine();
        }

        output.WriteLine($"Next: {executableName} help <command>");
    }

    public void WriteCommandGroupHelp(
        MetaCliCommandSurface surface,
        Application application,
        Command command,
        IReadOnlyList<Command>? children = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(command);

        var executableName = surface.ExecutableName(application);
        var route = surface.Route(command);
        var childCommands = children ?? surface.ChildCommands(command);

        output.WriteLine("Usage:");
        output.WriteLine($"  {surface.BuildCommandGroupUsage(application, command)}");

        if (!string.IsNullOrWhiteSpace(command.Description))
        {
            output.WriteLine();
            output.WriteLine(command.Description);
        }

        if (childCommands.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Commands:");
            var commandWidth = Math.Max(18, childCommands.Max(child => (child.Token ?? child.Name).Length) + 2);
            foreach (var child in childCommands)
            {
                var token = string.IsNullOrWhiteSpace(child.Token) ? child.Name : child.Token;
                var description = string.IsNullOrWhiteSpace(child.Description)
                    ? string.Empty
                    : child.Description;
                output.WriteLine($"  {token.PadRight(commandWidth)}{description}");
            }
        }

        output.WriteLine();
        output.WriteLine($"Next: {executableName} help {route} <command>");
    }

    public void WriteCommandHelp(
        MetaCliCommandSurface surface,
        Application application,
        ExecutableCommand executableCommand)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(executableCommand);

        var parameters = surface.EffectiveParameters(executableCommand);
        var options = parameters
            .Select(parameter => new HelpOption(parameter, surface.PrimaryOptionToken(parameter)))
            .Where(option => option.Token is not null)
            .ToArray();
        var positionals = surface.OrderedPositionals(executableCommand);

        output.WriteLine("Usage:");
        output.WriteLine($"  {surface.BuildCommandUsage(application, executableCommand)}");

        if (!string.IsNullOrWhiteSpace(executableCommand.Command.Description))
        {
            output.WriteLine();
            output.WriteLine(executableCommand.Command.Description);
        }

        if (positionals.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Arguments:");
            foreach (var positional in positionals)
            {
                var parameter = positional.Parameter;
                var label = "<" + parameter.Name + ">";
                var description = string.IsNullOrWhiteSpace(parameter.Description)
                    ? string.Empty
                    : "  " + parameter.Description;
                output.WriteLine(string.IsNullOrEmpty(description)
                    ? $"  {label}"
                    : $"  {label,-28}{description}");
            }
        }

        if (options.Length > 0)
        {
            output.WriteLine();
            output.WriteLine("Options:");
            foreach (var option in options)
            {
                var parameter = option.Parameter;
                var label = option.Token!.Token + MetaCliCommandSurface.ValueLabel(parameter);
                var description = string.IsNullOrWhiteSpace(parameter.Description)
                    ? string.Empty
                    : "  " + parameter.Description;
                output.WriteLine(string.IsNullOrEmpty(description)
                    ? $"  {label}"
                    : $"  {label,-28}{description}");
            }
        }

        var children = surface.ChildCommands(executableCommand.Command);
        if (children.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Commands:");
            var commandWidth = Math.Max(18, children.Max(child => (child.Token ?? child.Name).Length) + 2);
            foreach (var child in children)
            {
                var token = string.IsNullOrWhiteSpace(child.Token) ? child.Name : child.Token;
                var description = string.IsNullOrWhiteSpace(child.Description)
                    ? string.Empty
                    : child.Description;
                output.WriteLine($"  {token.PadRight(commandWidth)}{description}");
            }
        }
    }

    private static bool TryBuildHelpRoute(
        IReadOnlyList<string> arguments,
        out IReadOnlyList<string> route)
    {
        route = Array.Empty<string>();
        if (arguments.Count == 0)
        {
            return true;
        }

        if (IsHelpToken(arguments[0]))
        {
            route = arguments
                .Skip(1)
                .Where(static argument => !IsHelpToken(argument))
                .ToArray();
            return true;
        }

        if (arguments.Count > 1 && IsHelpToken(arguments[^1]))
        {
            route = arguments
                .Take(arguments.Count - 1)
                .ToArray();
            return true;
        }

        return false;
    }

    public static bool IsHelpToken(string value) =>
        string.Equals(value, "help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);

    private static Command? ResolveCommandFromArguments(
        MetaCliCommandSurface surface,
        Application application,
        IReadOnlyList<string> arguments)
    {
        var route = NormalizeRoute(arguments);
        if (route.Count == 0)
        {
            return null;
        }

        Command? current = null;
        var matched = false;
        foreach (var token in route)
        {
            if (token.StartsWith("-", StringComparison.Ordinal))
            {
                break;
            }

            var next = current is null
                ? surface.RootCommands(application).SingleOrDefault(command => TokenEquals(command, token))
                : surface.ChildCommands(current).SingleOrDefault(command => TokenEquals(command, token));
            if (next is null)
            {
                break;
            }

            current = next;
            matched = true;
        }

        return matched ? current : null;
    }

    private static IReadOnlyList<string> NormalizeRoute(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return Array.Empty<string>();
        }

        if (IsHelpToken(arguments[0]))
        {
            return arguments
                .Skip(1)
                .Where(static argument => !IsHelpToken(argument))
                .ToArray();
        }

        if (arguments.Count > 1 && IsHelpToken(arguments[^1]))
        {
            return arguments
                .Take(arguments.Count - 1)
                .ToArray();
        }

        return arguments.ToArray();
    }

    private static bool TokenEquals(Command command, string token)
    {
        var commandToken = string.IsNullOrWhiteSpace(command.Token) ? command.Name : command.Token;
        return string.Equals(commandToken, token, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record HelpOption(Parameter Parameter, OptionToken? Token);
}
