namespace MetaCli.Core;

public sealed class MetaCliInvocation
{
    private readonly IReadOnlyList<MetaCliParameterBinding> bindings;

    internal MetaCliInvocation(
        Application application,
        ExecutableCommand executableCommand,
        string commandRoute,
        IReadOnlyList<string> rawArguments,
        IReadOnlyList<MetaCliParameterBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(executableCommand);
        ArgumentNullException.ThrowIfNull(rawArguments);
        ArgumentNullException.ThrowIfNull(bindings);

        Application = application;
        ExecutableCommand = executableCommand;
        Command = executableCommand.Command;
        CommandRoute = commandRoute;
        RawArguments = rawArguments.ToArray();
        this.bindings = bindings.ToArray();
    }

    public Application Application { get; }

    public Command Command { get; }

    public ExecutableCommand ExecutableCommand { get; }

    public string CommandRoute { get; }

    public IReadOnlyList<string> RawArguments { get; }

    public IReadOnlyList<MetaCliParameterBinding> Parameters => bindings;

    public bool IsPresent(string parameter) => Resolve(parameter).IsPresent;

    public bool Flag(string parameter)
    {
        var binding = Resolve(parameter);
        return binding.IsPresent && binding.Values.Count == 0;
    }

    public string Required(string parameter)
    {
        var values = Values(parameter);
        return values.Count == 0
            ? throw new InvalidOperationException($"Parameter '{parameter}' has no value.")
            : values[0];
    }

    public string? Optional(string parameter)
    {
        var values = Values(parameter);
        return values.Count == 0 ? null : values[0];
    }

    public IReadOnlyList<string> Values(string parameter) => Resolve(parameter).Values;

    private MetaCliParameterBinding Resolve(string parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter))
        {
            throw new ArgumentException("Parameter id or name is required.", nameof(parameter));
        }

        var normalized = parameter.Trim();
        var byId = bindings.FirstOrDefault(binding => string.Equals(binding.Parameter.Id, normalized, StringComparison.Ordinal));
        if (byId is not null)
        {
            return byId;
        }

        var byName = bindings
            .Where(binding => string.Equals(binding.Parameter.Name, normalized, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (byName.Length == 1)
        {
            return byName[0];
        }

        if (byName.Length > 1)
        {
            throw new InvalidOperationException($"Parameter name '{normalized}' is ambiguous for command '{CommandRoute}'.");
        }

        throw new KeyNotFoundException($"Parameter '{normalized}' is not part of command '{CommandRoute}'.");
    }
}

public sealed record MetaCliParameterBinding(
    Parameter Parameter,
    bool IsPresent,
    IReadOnlyList<string> Values);

public sealed record MetaCliRuntimeResult(
    int ExitCode,
    string? Message,
    MetaCliInvocation? Invocation)
{
    public bool Succeeded => ExitCode == 0;

    public static MetaCliRuntimeResult Success(int exitCode, MetaCliInvocation invocation) =>
        new(exitCode, null, invocation);

    public static MetaCliRuntimeResult Failure(
        int exitCode,
        string message,
        MetaCliInvocation? invocation = null) =>
        new(exitCode, message, invocation);
}
