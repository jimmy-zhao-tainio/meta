using System.Globalization;

namespace MetaCli.Core;

public delegate int MetaCliCommandHandler(MetaCliInvocation invocation);

public sealed class MetaCliRuntimeBuilder
{
    private readonly MetaCliModel model;
    private readonly Dictionary<string, MetaCliCommandHandler> handlers = new(StringComparer.Ordinal);
    private string? applicationId;

    public MetaCliRuntimeBuilder(MetaCliModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        this.model = model;
    }

    public MetaCliRuntimeBuilder ForApplication(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Application id is required.", nameof(id));
        }

        applicationId = id.Trim();
        return this;
    }

    public MetaCliRuntimeBuilder Bind(string executableCommandId, MetaCliCommandHandler handler)
    {
        if (string.IsNullOrWhiteSpace(executableCommandId))
        {
            throw new ArgumentException("Executable command id is required.", nameof(executableCommandId));
        }

        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = handler;
        return this;
    }

    public MetaCliRuntime Build() => new(model, applicationId, handlers);
}

public sealed class MetaCliRuntime
{
    private readonly MetaCliModel model;
    private readonly string? applicationId;
    private readonly IReadOnlyDictionary<string, MetaCliCommandHandler> handlers;

    public MetaCliRuntime(
        MetaCliModel model,
        string? applicationId,
        IReadOnlyDictionary<string, MetaCliCommandHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(handlers);
        this.model = model;
        this.applicationId = string.IsNullOrWhiteSpace(applicationId) ? null : applicationId.Trim();
        this.handlers = new Dictionary<string, MetaCliCommandHandler>(handlers, StringComparer.Ordinal);
    }

    public MetaCliRuntimeResult Run(params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (!TryResolveApplication(out var application, out var applicationError))
        {
            return MetaCliRuntimeResult.Failure(2, applicationError);
        }

        var policy = ResolveParserPolicy(application);
        var commandMatch = MatchExecutableCommand(application, arguments);
        if (!commandMatch.Succeeded)
        {
            return MetaCliRuntimeResult.Failure(2, commandMatch.Error);
        }

        var bindResult = BindParameters(application, commandMatch.ExecutableCommand, policy, arguments, commandMatch.ConsumedTokenCount);
        if (!bindResult.Succeeded)
        {
            return MetaCliRuntimeResult.Failure(2, bindResult.Error);
        }

        var invocation = new MetaCliInvocation(
            application,
            commandMatch.ExecutableCommand,
            DisplayRoute(commandMatch.ExecutableCommand.Command),
            arguments,
            bindResult.Bindings);

        if (!handlers.TryGetValue(commandMatch.ExecutableCommand.Id, out var handler))
        {
            return MetaCliRuntimeResult.Failure(
                3,
                $"Command '{invocation.CommandRoute}' has no registered implementation (executable command: {commandMatch.ExecutableCommand.Id}).",
                invocation);
        }

        var exitCode = handler(invocation);
        return MetaCliRuntimeResult.Success(exitCode, invocation);
    }

    private bool TryResolveApplication(out Application application, out string error)
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

    private ParserPolicyRuntime ResolveParserPolicy(Application application)
    {
        var policy = model.ParserPolicyList.SingleOrDefault(item => ReferenceEquals(item.Application, application));
        if (policy is null)
        {
            return ParserPolicyRuntime.Default;
        }

        return new ParserPolicyRuntime(
            EmptyToNull(policy.StopParsingToken),
            ParseBool(policy.AllowsEqualsValueSyntax),
            ParseBool(policy.AllowsOptionsAfterPositionals),
            ParseBool(policy.AllowsShortOptionClusters));
    }

    private CommandMatch MatchExecutableCommand(Application application, IReadOnlyList<string> arguments)
    {
        var rootCommands = model.CommandList
            .Where(command => ReferenceEquals(command.Application, application) && command.ParentCommand is null && !string.IsNullOrWhiteSpace(command.Token))
            .ToArray();

        Command? current = null;
        ExecutableCommand? executable = null;
        var consumed = 0;
        while (consumed < arguments.Count)
        {
            var token = arguments[consumed];
            var candidates = model.CommandList
                .Where(command => ReferenceEquals(command.Application, application))
                .Where(command => ReferenceEquals(command.ParentCommand, current))
                .Where(command => string.Equals(command.Token, token, StringComparison.Ordinal))
                .ToArray();

            if (candidates.Length == 0)
            {
                break;
            }

            if (candidates.Length > 1)
            {
                return CommandMatch.Failure($"Command token '{token}' is ambiguous.");
            }

            current = candidates[0];
            consumed++;
            executable = FindExecutableCommand(current);
        }

        if (executable is not null)
        {
            return CommandMatch.Success(executable, consumed);
        }

        if (consumed == 0)
        {
            var defaultCommand = model.ApplicationDefaultCommandList
                .SingleOrDefault(item => ReferenceEquals(item.Application, application));
            if (defaultCommand is not null)
            {
                return CommandMatch.Success(defaultCommand.ExecutableCommand, 0);
            }

            if (arguments.Count == 0)
            {
                return CommandMatch.Failure("No command was provided.");
            }

            var first = arguments[0];
            if (rootCommands.Length == 0)
            {
                return CommandMatch.Failure($"Command '{first}' is not modeled for application '{application.Name}'.");
            }

            return CommandMatch.Failure($"Command '{first}' is not modeled for application '{application.Name}'.");
        }

        var route = current is null ? string.Empty : DisplayRoute(current);
        return CommandMatch.Failure($"Command '{route}' is not runnable.");
    }

    private ExecutableCommand? FindExecutableCommand(Command command) =>
        model.ExecutableCommandList.SingleOrDefault(item => ReferenceEquals(item.Command, command));

    private ParameterBindResult BindParameters(
        Application application,
        ExecutableCommand executableCommand,
        ParserPolicyRuntime policy,
        IReadOnlyList<string> arguments,
        int startIndex)
    {
        var parameters = model.ParameterList
            .Where(parameter => ReferenceEquals(parameter.ExecutableCommand, executableCommand))
            .ToArray();
        var states = parameters.ToDictionary(
            parameter => parameter,
            parameter => new BoundParameterBuilder(parameter));

        var optionByToken = BuildOptionTokenMap(executableCommand);
        if (!optionByToken.Succeeded)
        {
            return ParameterBindResult.Failure(optionByToken.Error);
        }

        var orderedPositionals = OrderPositionals(executableCommand);
        if (!orderedPositionals.Succeeded)
        {
            return ParameterBindResult.Failure(orderedPositionals.Error);
        }

        var positionalIndex = 0;
        var seenPositional = false;
        var positionalMode = false;
        for (var index = startIndex; index < arguments.Count; index++)
        {
            var token = arguments[index];
            if (!positionalMode && policy.StopParsingToken is not null && string.Equals(token, policy.StopParsingToken, StringComparison.Ordinal))
            {
                positionalMode = true;
                continue;
            }

            if (!positionalMode && TrySplitEqualsOption(token, policy, optionByToken.Values, out var optionToken, out var inlineValue, out var inlineError))
            {
                if (inlineError is not null)
                {
                    return ParameterBindResult.Failure(inlineError);
                }

                if (!policy.AllowsOptionsAfterPositionals && seenPositional)
                {
                    return ParameterBindResult.Failure($"Option '{optionToken.Token}' cannot appear after positional arguments.");
                }

                var optionBind = BindOptionValue(states, optionToken, inlineValue, arguments, ref index);
                if (!optionBind.Succeeded)
                {
                    return ParameterBindResult.Failure(optionBind.Error);
                }

                continue;
            }

            if (!positionalMode && optionByToken.Values.TryGetValue(token, out var exactOptionToken))
            {
                if (!policy.AllowsOptionsAfterPositionals && seenPositional)
                {
                    return ParameterBindResult.Failure($"Option '{exactOptionToken.Token}' cannot appear after positional arguments.");
                }

                var optionBind = BindOptionValue(states, exactOptionToken, null, arguments, ref index);
                if (!optionBind.Succeeded)
                {
                    return ParameterBindResult.Failure(optionBind.Error);
                }

                continue;
            }

            if (!positionalMode && LooksLikeOption(token) && !NextPositionalAllowsOptionLikeValue(orderedPositionals.Values, positionalIndex))
            {
                return ParameterBindResult.Failure($"Option '{token}' is not recognized.");
            }

            var positionalBind = BindPositionalValue(states, orderedPositionals.Values, ref positionalIndex, token);
            if (!positionalBind.Succeeded)
            {
                return ParameterBindResult.Failure(positionalBind.Error);
            }

            seenPositional = true;
        }

        var completion = CompleteBindings(executableCommand, states);
        if (!completion.Succeeded)
        {
            return ParameterBindResult.Failure(completion.Error);
        }

        return ParameterBindResult.Success(states.Values.Select(static state => state.ToBinding()).ToArray());
    }

    private ValueResult<Dictionary<string, OptionToken>> BuildOptionTokenMap(ExecutableCommand executableCommand)
    {
        var tokens = new Dictionary<string, OptionToken>(StringComparer.Ordinal);
        foreach (var token in model.OptionTokenList.Where(token => ReferenceEquals(token.Option.Parameter.ExecutableCommand, executableCommand)))
        {
            if (!tokens.TryAdd(token.Token, token))
            {
                return ValueResult<Dictionary<string, OptionToken>>.Failure($"Option token '{token.Token}' is modeled more than once for command '{DisplayRoute(executableCommand.Command)}'.");
            }
        }

        return ValueResult<Dictionary<string, OptionToken>>.Success(tokens);
    }

    private ValueResult<IReadOnlyList<PositionalArgument>> OrderPositionals(ExecutableCommand executableCommand)
    {
        var positionals = model.PositionalArgumentList
            .Where(argument => ReferenceEquals(argument.Parameter.ExecutableCommand, executableCommand))
            .ToArray();
        if (positionals.Length == 0)
        {
            return ValueResult<IReadOnlyList<PositionalArgument>>.Success(Array.Empty<PositionalArgument>());
        }

        var head = positionals.Where(static argument => argument.PreviousArgument is null).Take(2).ToArray();
        if (head.Length != 1)
        {
            return ValueResult<IReadOnlyList<PositionalArgument>>.Failure($"Positional arguments for command '{DisplayRoute(executableCommand.Command)}' are not a single chain.");
        }

        var ordered = new List<PositionalArgument>();
        var current = head[0];
        var visited = new HashSet<PositionalArgument>();
        while (visited.Add(current))
        {
            ordered.Add(current);
            var next = positionals
                .Where(argument => ReferenceEquals(argument.PreviousArgument, current))
                .Take(2)
                .ToArray();
            if (next.Length > 1)
            {
                return ValueResult<IReadOnlyList<PositionalArgument>>.Failure($"Positional arguments for command '{DisplayRoute(executableCommand.Command)}' fork after '{current.Parameter.Name}'.");
            }

            if (next.Length == 0)
            {
                return ordered.Count == positionals.Length
                    ? ValueResult<IReadOnlyList<PositionalArgument>>.Success(ordered)
                    : ValueResult<IReadOnlyList<PositionalArgument>>.Failure($"Positional arguments for command '{DisplayRoute(executableCommand.Command)}' are disconnected.");
            }

            current = next[0];
        }

        return ValueResult<IReadOnlyList<PositionalArgument>>.Failure($"Positional arguments for command '{DisplayRoute(executableCommand.Command)}' contain a cycle.");
    }

    private OperationResult BindOptionValue(
        IReadOnlyDictionary<Parameter, BoundParameterBuilder> states,
        OptionToken optionToken,
        string? inlineValue,
        IReadOnlyList<string> arguments,
        ref int index)
    {
        var parameter = optionToken.Option.Parameter;
        var state = states[parameter];
        var arity = ReadArity(parameter.ValueShape);
        if (!arity.Succeeded)
        {
            return OperationResult.Failure(arity.Error);
        }

        if (!ParseBool(parameter.IsRepeatable) && state.IsPresent)
        {
            return OperationResult.Failure($"Option '{optionToken.Token}' was provided more than once.");
        }

        state.IsPresent = true;
        if (arity.Values.MaxValueCount == 0)
        {
            if (inlineValue is not null)
            {
                return OperationResult.Failure($"Option '{optionToken.Token}' does not accept a value.");
            }

            return OperationResult.Success();
        }

        if (inlineValue is not null)
        {
            state.Values.Add(inlineValue);
            return OperationResult.Success();
        }

        for (var valueIndex = 0; valueIndex < arity.Values.MinValueCount; valueIndex++)
        {
            var nextIndex = index + 1;
            if (nextIndex >= arguments.Count)
            {
                return OperationResult.Failure($"Option '{optionToken.Token}' requires a value.");
            }

            var value = arguments[nextIndex];
            if (LooksLikeOption(value) && !ParseBool(parameter.ValueShape.AllowsOptionLikeValue))
            {
                return OperationResult.Failure($"Option '{optionToken.Token}' requires a value before '{value}'.");
            }

            state.Values.Add(value);
            index = nextIndex;
        }

        return OperationResult.Success();
    }

    private OperationResult BindPositionalValue(
        IReadOnlyDictionary<Parameter, BoundParameterBuilder> states,
        IReadOnlyList<PositionalArgument> positionals,
        ref int positionalIndex,
        string value)
    {
        if (positionalIndex >= positionals.Count)
        {
            return OperationResult.Failure($"Unexpected argument '{value}'.");
        }

        var positional = positionals[positionalIndex];
        var parameter = positional.Parameter;
        var state = states[parameter];
        var arity = ReadArity(parameter.ValueShape);
        if (!arity.Succeeded)
        {
            return OperationResult.Failure(arity.Error);
        }

        if (arity.Values.MaxValueCount == 0)
        {
            return OperationResult.Failure($"Positional argument '{parameter.Name}' does not accept a value.");
        }

        state.IsPresent = true;
        state.Values.Add(value);

        if (!ParseBool(parameter.IsRepeatable))
        {
            positionalIndex++;
        }

        return OperationResult.Success();
    }

    private OperationResult CompleteBindings(
        ExecutableCommand executableCommand,
        IReadOnlyDictionary<Parameter, BoundParameterBuilder> states)
    {
        foreach (var state in states.Values)
        {
            if (!state.IsPresent && !string.IsNullOrWhiteSpace(state.Parameter.DefaultValue))
            {
                state.Values.Add(state.Parameter.DefaultValue!);
            }

            if (ParseBool(state.Parameter.IsRequired) && state.Values.Count == 0 && !state.IsPresent)
            {
                return OperationResult.Failure($"Required parameter '{state.Parameter.Name}' was not provided.");
            }

            var arity = ReadArity(state.Parameter.ValueShape);
            if (!arity.Succeeded)
            {
                return OperationResult.Failure(arity.Error);
            }

            if (state.IsPresent && state.Values.Count < arity.Values.MinValueCount)
            {
                return OperationResult.Failure($"Parameter '{state.Parameter.Name}' expects at least {arity.Values.MinValueCount.ToString(CultureInfo.InvariantCulture)} value(s).");
            }

            if (arity.Values.MaxValueCount is not null && state.Values.Count > arity.Values.MaxValueCount.Value)
            {
                return OperationResult.Failure($"Parameter '{state.Parameter.Name}' expects at most {arity.Values.MaxValueCount.Value.ToString(CultureInfo.InvariantCulture)} value(s).");
            }

            var allowedValues = model.AllowedValueList
                .Where(value => ReferenceEquals(value.ValueShape, state.Parameter.ValueShape))
                .Select(static value => value.Value)
                .ToHashSet(StringComparer.Ordinal);
            if (allowedValues.Count != 0)
            {
                foreach (var value in state.Values)
                {
                    if (!allowedValues.Contains(value))
                    {
                        return OperationResult.Failure($"Parameter '{state.Parameter.Name}' does not allow value '{value}'.");
                    }
                }
            }
        }

        foreach (var group in model.ParameterGroupList.Where(group => ReferenceEquals(group.ExecutableCommand, executableCommand)))
        {
            var members = model.ParameterGroupMemberList
                .Where(member => ReferenceEquals(member.ParameterGroup, group))
                .ToArray();
            var presentMembers = members.Count(member => states.TryGetValue(member.Parameter, out var state) && state.IsPresent);
            if (ParseBool(group.IsRequired) && presentMembers == 0)
            {
                return OperationResult.Failure($"Parameter group '{group.Name}' requires one of: {string.Join(", ", members.Select(static member => member.Parameter.Name))}.");
            }

            if (!ParseBool(group.AllowsMultiple) && presentMembers > 1)
            {
                return OperationResult.Failure($"Parameter group '{group.Name}' accepts only one member.");
            }
        }

        return OperationResult.Success();
    }

    private static bool TrySplitEqualsOption(
        string token,
        ParserPolicyRuntime policy,
        IReadOnlyDictionary<string, OptionToken> options,
        out OptionToken optionToken,
        out string? inlineValue,
        out string? error)
    {
        optionToken = null!;
        inlineValue = null;
        error = null;

        var equalsIndex = token.IndexOf('=', StringComparison.Ordinal);
        if (equalsIndex <= 0)
        {
            return false;
        }

        var optionName = token[..equalsIndex];
        if (!options.TryGetValue(optionName, out optionToken!))
        {
            return false;
        }

        if (!policy.AllowsEqualsValueSyntax)
        {
            error = $"Option '{optionName}' does not accept inline value syntax.";
            return true;
        }

        inlineValue = token[(equalsIndex + 1)..];
        return true;
    }

    private static bool NextPositionalAllowsOptionLikeValue(
        IReadOnlyList<PositionalArgument> positionals,
        int positionalIndex) =>
        positionalIndex < positionals.Count &&
        ParseBool(positionals[positionalIndex].Parameter.ValueShape.AllowsOptionLikeValue);

    private static ValueResult<ValueArityRuntime> ReadArity(ValueShape valueShape)
    {
        if (!int.TryParse(valueShape.ValueArity.MinValueCount, NumberStyles.None, CultureInfo.InvariantCulture, out var min) || min < 0)
        {
            return ValueResult<ValueArityRuntime>.Failure($"Value shape '{valueShape.Name}' has invalid minimum value count.");
        }

        int? max = null;
        if (!string.IsNullOrWhiteSpace(valueShape.ValueArity.MaxValueCount))
        {
            if (!int.TryParse(valueShape.ValueArity.MaxValueCount, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedMax) || parsedMax < 0)
            {
                return ValueResult<ValueArityRuntime>.Failure($"Value shape '{valueShape.Name}' has invalid maximum value count.");
            }

            max = parsedMax;
        }

        if (max is not null && max.Value < min)
        {
            return ValueResult<ValueArityRuntime>.Failure($"Value shape '{valueShape.Name}' has maximum value count below minimum value count.");
        }

        return ValueResult<ValueArityRuntime>.Success(new ValueArityRuntime(min, max));
    }

    private static string DisplayRoute(Command command)
    {
        var route = MetaCliWorkspaceService.BuildRoute(command);
        return string.IsNullOrWhiteSpace(route) ? command.Name : route;
    }

    private static bool LooksLikeOption(string token) =>
        token.Length > 1 && token[0] == '-';

    private static bool ParseBool(string? value) =>
        bool.TryParse(value, out var parsed) && parsed;

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ParserPolicyRuntime(
        string? StopParsingToken,
        bool AllowsEqualsValueSyntax,
        bool AllowsOptionsAfterPositionals,
        bool AllowsShortOptionClusters)
    {
        public static ParserPolicyRuntime Default { get; } = new(null, false, false, false);
    }

    private sealed record ValueArityRuntime(int MinValueCount, int? MaxValueCount);

    private sealed class BoundParameterBuilder
    {
        public BoundParameterBuilder(Parameter parameter)
        {
            Parameter = parameter;
        }

        public Parameter Parameter { get; }

        public bool IsPresent { get; set; }

        public List<string> Values { get; } = new();

        public MetaCliParameterBinding ToBinding() =>
            new(Parameter, IsPresent, Values.ToArray());
    }

    private readonly record struct CommandMatch(
        bool Succeeded,
        ExecutableCommand ExecutableCommand,
        int ConsumedTokenCount,
        string Error)
    {
        public static CommandMatch Success(ExecutableCommand executableCommand, int consumedTokenCount) =>
            new(true, executableCommand, consumedTokenCount, string.Empty);

        public static CommandMatch Failure(string error) =>
            new(false, null!, 0, error);
    }

    private readonly record struct ParameterBindResult(
        bool Succeeded,
        IReadOnlyList<MetaCliParameterBinding> Bindings,
        string Error)
    {
        public static ParameterBindResult Success(IReadOnlyList<MetaCliParameterBinding> bindings) =>
            new(true, bindings, string.Empty);

        public static ParameterBindResult Failure(string error) =>
            new(false, Array.Empty<MetaCliParameterBinding>(), error);
    }

    private readonly record struct OperationResult(bool Succeeded, string Error)
    {
        public static OperationResult Success() => new(true, string.Empty);

        public static OperationResult Failure(string error) => new(false, error);
    }

    private readonly record struct ValueResult<T>(bool Succeeded, T Values, string Error)
    {
        public static ValueResult<T> Success(T values) => new(true, values, string.Empty);

        public static ValueResult<T> Failure(string error) => new(false, default!, error);
    }
}
