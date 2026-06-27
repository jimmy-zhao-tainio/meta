using System.Runtime.CompilerServices;

namespace MetaCli.Core;

public sealed class MetaCliCommandSurface
{
    private readonly MetaCliModel model;
    private readonly string? applicationId;

    public MetaCliCommandSurface(MetaCliModel model, string? applicationId = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        this.model = model;
        this.applicationId = string.IsNullOrWhiteSpace(applicationId) ? null : applicationId.Trim();
    }

    public bool TryResolveApplication(out Application application, out string error)
    {
        application = null!;
        error = string.Empty;

        if (applicationId is not null)
        {
            application = model.ApplicationList.FirstOrDefault(item => string.Equals(item.Id, applicationId, StringComparison.Ordinal))!;
            if (application is null)
            {
                error = $"Application '{applicationId}' does not exist.";
                return false;
            }

            return true;
        }

        if (model.ApplicationList.Count == 1)
        {
            application = model.ApplicationList[0];
            return true;
        }

        if (model.ApplicationList.Count == 0)
        {
            error = "The MetaCli model has no application.";
            return false;
        }

        error = "The MetaCli model has more than one application; select one before running.";
        return false;
    }

    public IReadOnlyList<Command> RootCommands(Application application) =>
        model.CommandList
            .Where(command => ReferenceEquals(command.Application, application))
            .Where(command => command.ParentCommand is null)
            .OrderBy(static command => command.Token, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static command => command.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static command => command.Id, StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<Command> ChildCommands(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return model.CommandList
            .Where(candidate => ReferenceEquals(candidate.ParentCommand, command))
            .OrderBy(static candidate => candidate.Token, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public Command? FindCommand(Application application, IReadOnlyList<string> route)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(route);

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

    public ExecutableCommand? FindExecutableCommand(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return model.ExecutableCommandList.SingleOrDefault(item => ReferenceEquals(item.Command, command));
    }

    public IReadOnlyList<Parameter> EffectiveParameters(ExecutableCommand executableCommand)
    {
        ArgumentNullException.ThrowIfNull(executableCommand);

        var parameters = new List<Parameter>();
        parameters.AddRange(CommandParameters(executableCommand));
        parameters.AddRange(model.ApplicationParameterList
            .Where(item => ReferenceEquals(item.Application, executableCommand.Command.Application))
            .Select(static item => item.Parameter));
        return parameters
            .Distinct(ReferenceComparer<Parameter>.Instance)
            .ToArray();
    }

    public IReadOnlyList<Parameter> CommandParameters(ExecutableCommand executableCommand)
    {
        ArgumentNullException.ThrowIfNull(executableCommand);

        return model.ExecutableCommandParameterList
            .Where(item => ReferenceEquals(item.ExecutableCommand, executableCommand))
            .Select(static item => item.Parameter)
            .ToArray();
    }

    public IReadOnlyList<OptionToken> OptionTokens(Parameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        var tokens = model.OptionTokenList
            .Where(token => ReferenceEquals(token.Option.Parameter, parameter))
            .ToArray();
        return TryOrderChain(tokens, static token => token.PreviousToken, out var ordered)
            ? ordered
            : tokens
                .OrderBy(static token => token.Token, StringComparer.Ordinal)
                .ThenBy(static token => token.Id, StringComparer.Ordinal)
                .ToArray();
    }

    public OptionToken? PrimaryOptionToken(Parameter parameter) =>
        OptionTokens(parameter).FirstOrDefault();

    public IReadOnlyList<PositionalArgument> OrderedPositionals(ExecutableCommand executableCommand)
    {
        ArgumentNullException.ThrowIfNull(executableCommand);

        var commandParameters = CommandParameters(executableCommand).ToHashSet(ReferenceComparer<Parameter>.Instance);
        var positionals = model.PositionalArgumentList
            .Where(argument => commandParameters.Contains(argument.Parameter))
            .ToArray();
        return TryOrderChain(positionals, static argument => argument.PreviousArgument, out var ordered)
            ? ordered
            : positionals
                .OrderBy(static argument => argument.Parameter.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static argument => argument.Id, StringComparer.Ordinal)
                .ToArray();
    }

    public string Route(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var route = MetaCliWorkspaceService.BuildRoute(command);
        return string.IsNullOrWhiteSpace(route) ? command.Name : route;
    }

    public string ExecutableName(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (!string.IsNullOrWhiteSpace(application.ExecutableName))
        {
            return application.ExecutableName!;
        }

        return string.IsNullOrWhiteSpace(application.Name) ? "cli" : application.Name;
    }

    public string BuildApplicationUsage(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return $"{ExecutableName(application)} <command> [options]";
    }

    public string BuildCommandGroupUsage(Application application, Command command)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(command);

        return $"{ExecutableName(application)} {Route(command)} <command> [options]";
    }

    public string BuildCommandUsage(Application application, ExecutableCommand executableCommand)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(executableCommand);

        var parts = new List<string> { ExecutableName(application), Route(executableCommand.Command) };
        foreach (var positional in OrderedPositionals(executableCommand))
        {
            var parameter = positional.Parameter;
            parts.Add(IsTrue(parameter.IsRequired)
                ? $"<{parameter.Name}>"
                : $"[<{parameter.Name}>]");
        }

        foreach (var option in EffectiveParameters(executableCommand)
            .Select(parameter => (Parameter: parameter, Token: PrimaryOptionToken(parameter)))
            .Where(option => option.Token is not null))
        {
            var label = option.Token!.Token + ValueLabel(option.Parameter);
            parts.Add(IsTrue(option.Parameter.IsRequired) ? label : $"[{label}]");
        }

        return string.Join(" ", parts);
    }

    public static string ValueLabel(Parameter parameter)
    {
        var valueName = ValueName(parameter);
        return string.IsNullOrWhiteSpace(valueName) ? string.Empty : " " + valueName;
    }

    public static string ValueName(Parameter parameter, string fallback = "<value>")
    {
        ArgumentNullException.ThrowIfNull(parameter);

        var arity = parameter.ValueShape.ValueArity;
        if (string.Equals(arity.MaxValueCount, "0", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(parameter.ValueShape.ValueLabel)
            ? fallback
            : parameter.ValueShape.ValueLabel;
    }

    private static bool IsTrue(string? value) =>
        bool.TryParse(value, out var parsed) && parsed;

    private static bool TryOrderChain<T>(
        IReadOnlyList<T> items,
        Func<T, T?> previous,
        out IReadOnlyList<T> ordered)
        where T : class
    {
        ordered = Array.Empty<T>();
        if (items.Count == 0)
        {
            return true;
        }

        var heads = items
            .Where(item => previous(item) is null)
            .Take(2)
            .ToArray();
        if (heads.Length != 1)
        {
            return false;
        }

        var result = new List<T>();
        var visited = new HashSet<T>(ReferenceComparer<T>.Instance);
        var current = heads[0];
        while (visited.Add(current))
        {
            result.Add(current);
            var next = items
                .Where(item => ReferenceEquals(previous(item), current))
                .Take(2)
                .ToArray();
            if (next.Length > 1)
            {
                return false;
            }

            if (next.Length == 0)
            {
                ordered = result.Count == items.Count ? result : Array.Empty<T>();
                return result.Count == items.Count;
            }

            current = next[0];
        }

        return false;
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class?
    {
        public static readonly ReferenceComparer<T> Instance = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
