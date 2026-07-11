using System.Globalization;
using System.Runtime.CompilerServices;

namespace MetaCli.Core;

public sealed class MetaCliParser
{
    private readonly MetaCliModel model;
    private readonly string? applicationId;

    public MetaCliParser(MetaCliModel model, string? applicationId = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        this.model = model;
        this.applicationId = string.IsNullOrWhiteSpace(applicationId) ? null : applicationId.Trim();
    }

    public MetaCliParseResult Parse(params string[] arguments) =>
        Parse((IReadOnlyList<string>)arguments);

    public MetaCliParseResult Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (!TryResolveApplication(out var application, out var applicationErrorCode, out var applicationError))
        {
            return MetaCliParseResult.Failure(applicationErrorCode, applicationError);
        }

        var rules = ParserRuntimeRules.Default;
        var commandMatch = MatchExecutableCommand(application, rules, arguments);
        if (!commandMatch.Succeeded)
        {
            return MetaCliParseResult.Failure(commandMatch.ErrorCode, commandMatch.Error);
        }

        var bindResult = BindParameters(application, commandMatch.ExecutableCommand, rules, arguments, commandMatch.CommandTokenIndexes);
        if (!bindResult.Succeeded)
        {
            return MetaCliParseResult.Failure(bindResult.ErrorCode, bindResult.Error);
        }

        var invocation = new MetaCliInvocation(
            application,
            commandMatch.ExecutableCommand,
            DisplayRoute(commandMatch.ExecutableCommand.Command),
            arguments,
            bindResult.Bindings);

        return MetaCliParseResult.Success(invocation);
    }

    private bool TryResolveApplication(
        out Application application,
        out MetaCliParseErrorCode errorCode,
        out string error)
    {
        application = null!;
        errorCode = MetaCliParseErrorCode.None;
        error = string.Empty;

        if (applicationId is not null)
        {
            application = model.ApplicationList.FirstOrDefault(item => string.Equals(item.Id, applicationId, StringComparison.Ordinal))!;
            if (application is null)
            {
                errorCode = MetaCliParseErrorCode.ApplicationNotFound;
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
            errorCode = MetaCliParseErrorCode.ApplicationMissing;
            error = "The MetaCli model has no application.";
            return false;
        }

        errorCode = MetaCliParseErrorCode.ApplicationAmbiguous;
        error = "The MetaCli model has more than one application; select one before running.";
        return false;
    }

    private CommandMatch MatchExecutableCommand(
        Application application,
        ParserRuntimeRules rules,
        IReadOnlyList<string> arguments)
    {
        var applicationOptions = BuildApplicationOptionTokenMap(application);
        if (!applicationOptions.Succeeded)
        {
            return CommandMatch.Failure(applicationOptions.ErrorCode, applicationOptions.Error);
        }

        Command? current = null;
        ExecutableCommand? executable = null;
        var commandTokenIndexes = new HashSet<int>();
        for (var index = 0; index < arguments.Count;)
        {
            var token = arguments[index];
            if (TrySkipApplicationOption(applicationOptions.Values, rules, arguments, ref index, out var skipErrorCode, out var skipError))
            {
                if (skipError is not null)
                {
                    return CommandMatch.Failure(skipErrorCode, skipError);
                }

                continue;
            }

            var candidates = model.CommandList
                .Where(command => ReferenceEquals(command.Application, application))
                .Where(command => ReferenceEquals(command.ParentCommand, current))
                .Where(command => string.Equals(command.Token, token, StringComparison.Ordinal))
                .ToArray();

            if (candidates.Length == 0)
            {
                if (current is not null && executable is null && !LooksLikeOption(token))
                {
                    return CommandMatch.Failure(MetaCliParseErrorCode.CommandNotFound, $"Unknown command '{DisplayRoute(current)} {token}'.");
                }

                break;
            }

            if (candidates.Length > 1)
            {
                return CommandMatch.Failure(MetaCliParseErrorCode.CommandAmbiguous, $"Command token '{token}' is ambiguous.");
            }

            current = candidates[0];
            commandTokenIndexes.Add(index);
            index++;
            executable = FindExecutableCommand(current);
        }

        if (executable is not null)
        {
            return CommandMatch.Success(executable, commandTokenIndexes);
        }

        if (commandTokenIndexes.Count == 0)
        {
            if (arguments.Count == 0)
            {
                var defaultCommand = model.ApplicationDefaultCommandList
                    .SingleOrDefault(item => ReferenceEquals(item.Application, application));
                return defaultCommand is null
                    ? CommandMatch.Failure(MetaCliParseErrorCode.CommandMissing, "No command was provided.")
                    : CommandMatch.Success(defaultCommand.ExecutableCommand, Array.Empty<int>());
            }

            var first = arguments[0];
            if (LooksLikeOption(first))
            {
                return CommandMatch.Failure(MetaCliParseErrorCode.UnknownOption, $"Option '{first}' is not recognized.");
            }

            return CommandMatch.Failure(MetaCliParseErrorCode.CommandNotFound, $"Unknown command '{first}'.");
        }

        var route = current is null ? string.Empty : DisplayRoute(current);
        return CommandMatch.Failure(MetaCliParseErrorCode.CommandNotRunnable, $"Command '{route}' is not runnable.");
    }

    private ExecutableCommand? FindExecutableCommand(Command command) =>
        model.ExecutableCommandList.SingleOrDefault(item => ReferenceEquals(item.Command, command));

    private ParameterBindResult BindParameters(
        Application application,
        ExecutableCommand executableCommand,
        ParserRuntimeRules rules,
        IReadOnlyList<string> arguments,
        IReadOnlySet<int> commandTokenIndexes)
    {
        var parameters = EffectiveParametersForExecutable(application, executableCommand);
        var states = parameters.ToDictionary(
            parameter => parameter,
            parameter => new BoundParameterBuilder(parameter),
            ReferenceComparer<Parameter>.Instance);

        var optionByToken = BuildOptionTokenMap(executableCommand);
        if (!optionByToken.Succeeded)
        {
            return ParameterBindResult.Failure(optionByToken.ErrorCode, optionByToken.Error);
        }

        var orderedPositionals = OrderPositionals(executableCommand);
        if (!orderedPositionals.Succeeded)
        {
            return ParameterBindResult.Failure(orderedPositionals.ErrorCode, orderedPositionals.Error);
        }

        var positionalIndex = 0;
        var positionalMode = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (commandTokenIndexes.Contains(index))
            {
                continue;
            }

            var token = arguments[index];
            if (!positionalMode && rules.StopParsingToken is not null && string.Equals(token, rules.StopParsingToken, StringComparison.Ordinal))
            {
                positionalMode = true;
                continue;
            }

            if (!positionalMode && TrySplitEqualsOption(token, rules, optionByToken.Values, out var optionToken, out var inlineValue, out var inlineError))
            {
                if (inlineError is not null)
                {
                    return ParameterBindResult.Failure(MetaCliParseErrorCode.InlineValueSyntaxNotAllowed, inlineError);
                }

                var optionBind = BindOptionValue(states, optionToken, inlineValue, arguments, ref index);
                if (!optionBind.Succeeded)
                {
                    return ParameterBindResult.Failure(optionBind.ErrorCode, optionBind.Error);
                }

                continue;
            }

            if (!positionalMode && optionByToken.Values.TryGetValue(token, out var exactOptionToken))
            {
                var optionBind = BindOptionValue(states, exactOptionToken, null, arguments, ref index);
                if (!optionBind.Succeeded)
                {
                    return ParameterBindResult.Failure(optionBind.ErrorCode, optionBind.Error);
                }

                continue;
            }

            if (!positionalMode && LooksLikeOption(token) && !NextPositionalAllowsOptionLikeValue(orderedPositionals.Values, positionalIndex))
            {
                return ParameterBindResult.Failure(MetaCliParseErrorCode.UnknownOption, $"Option '{token}' is not recognized.");
            }

            var positionalBind = BindPositionalValue(states, orderedPositionals.Values, ref positionalIndex, token);
            if (!positionalBind.Succeeded)
            {
                return ParameterBindResult.Failure(positionalBind.ErrorCode, positionalBind.Error);
            }
        }

        var completion = CompleteBindings(executableCommand, states);
        if (!completion.Succeeded)
        {
            return ParameterBindResult.Failure(completion.ErrorCode, completion.Error);
        }

        return ParameterBindResult.Success(states.Values.Select(static state => state.ToBinding()).ToArray());
    }

    private ValueResult<Dictionary<string, OptionToken>> BuildOptionTokenMap(ExecutableCommand executableCommand)
    {
        var tokens = new Dictionary<string, OptionToken>(StringComparer.Ordinal);
        var parameters = EffectiveParametersForExecutable(executableCommand.Command.Application, executableCommand);
        foreach (var token in model.OptionTokenList.Where(token => parameters.Contains(token.Option.Parameter, ReferenceComparer<Parameter>.Instance)))
        {
            if (!tokens.TryAdd(token.Token, token))
            {
                return ValueResult<Dictionary<string, OptionToken>>.Failure(MetaCliParseErrorCode.InvalidModel, $"Option token '{token.Token}' is modeled more than once for command '{DisplayRoute(executableCommand.Command)}'.");
            }
        }

        return ValueResult<Dictionary<string, OptionToken>>.Success(tokens);
    }

    private ValueResult<Dictionary<string, OptionToken>> BuildApplicationOptionTokenMap(Application application)
    {
        var tokens = new Dictionary<string, OptionToken>(StringComparer.Ordinal);
        var parameters = model.ApplicationParameterList
            .Where(item => ReferenceEquals(item.Application, application))
            .Select(static item => item.Parameter)
            .ToArray();
        foreach (var token in model.OptionTokenList.Where(token => parameters.Contains(token.Option.Parameter, ReferenceComparer<Parameter>.Instance)))
        {
            if (!tokens.TryAdd(token.Token, token))
            {
                return ValueResult<Dictionary<string, OptionToken>>.Failure(MetaCliParseErrorCode.InvalidModel, $"Application option token '{token.Token}' is modeled more than once for application '{application.Name}'.");
            }
        }

        return ValueResult<Dictionary<string, OptionToken>>.Success(tokens);
    }

    private static bool TrySkipApplicationOption(
        IReadOnlyDictionary<string, OptionToken> applicationOptions,
        ParserRuntimeRules rules,
        IReadOnlyList<string> arguments,
        ref int index,
        out MetaCliParseErrorCode errorCode,
        out string? error)
    {
        errorCode = MetaCliParseErrorCode.None;
        error = null;

        var token = arguments[index];
        if (TrySplitEqualsOption(token, rules, applicationOptions, out var inlineToken, out var _, out var inlineError))
        {
            if (inlineError is not null)
            {
                errorCode = MetaCliParseErrorCode.InlineValueSyntaxNotAllowed;
                error = inlineError;
                return true;
            }

            index++;
            return true;
        }

        if (!applicationOptions.TryGetValue(token, out var optionToken))
        {
            return false;
        }

        var arity = ReadArity(optionToken.Option.Parameter.ValueShape);
        if (!arity.Succeeded)
        {
            errorCode = arity.ErrorCode;
            error = arity.Error;
            return true;
        }

        index += 1 + arity.Values.MinValueCount;
        return true;
    }

    private ValueResult<IReadOnlyList<PositionalArgument>> OrderPositionals(ExecutableCommand executableCommand)
    {
        var parameters = CommandParametersForExecutable(executableCommand).ToArray();
        var positionals = model.PositionalArgumentList
            .Where(argument => parameters.Contains(argument.Parameter, ReferenceComparer<Parameter>.Instance))
            .ToArray();
        if (positionals.Length == 0)
        {
            return ValueResult<IReadOnlyList<PositionalArgument>>.Success(Array.Empty<PositionalArgument>());
        }

        var head = positionals.Where(static argument => argument.PreviousArgument is null).Take(2).ToArray();
        if (head.Length != 1)
        {
            return ValueResult<IReadOnlyList<PositionalArgument>>.Failure(MetaCliParseErrorCode.InvalidModel, $"Positional arguments for command '{DisplayRoute(executableCommand.Command)}' are not a single chain.");
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
                return ValueResult<IReadOnlyList<PositionalArgument>>.Failure(MetaCliParseErrorCode.InvalidModel, $"Positional arguments for command '{DisplayRoute(executableCommand.Command)}' fork after '{current.Parameter.Name}'.");
            }

            if (next.Length == 0)
            {
                return ordered.Count == positionals.Length
                    ? ValueResult<IReadOnlyList<PositionalArgument>>.Success(ordered)
                    : ValueResult<IReadOnlyList<PositionalArgument>>.Failure(MetaCliParseErrorCode.InvalidModel, $"Positional arguments for command '{DisplayRoute(executableCommand.Command)}' are disconnected.");
            }

            current = next[0];
        }

        return ValueResult<IReadOnlyList<PositionalArgument>>.Failure(MetaCliParseErrorCode.InvalidModel, $"Positional arguments for command '{DisplayRoute(executableCommand.Command)}' contain a cycle.");
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
            return OperationResult.Failure(arity.ErrorCode, arity.Error);
        }

        if (!ParseBool(parameter.IsRepeatable) && state.IsPresent)
        {
            return OperationResult.Failure(MetaCliParseErrorCode.DuplicateOption, $"Option '{optionToken.Token}' was provided more than once.");
        }

        state.IsPresent = true;
        if (arity.Values.MaxValueCount == 0)
        {
            if (inlineValue is not null)
            {
                return OperationResult.Failure(MetaCliParseErrorCode.OptionDoesNotAcceptValue, $"Option '{optionToken.Token}' does not accept a value.");
            }

            state.AddOptionOccurrence(optionToken);
            return OperationResult.Success();
        }

        if (inlineValue is not null)
        {
            state.AddOptionValue(optionToken, inlineValue);
            return OperationResult.Success();
        }

        for (var valueIndex = 0; valueIndex < arity.Values.MinValueCount; valueIndex++)
        {
            var nextIndex = index + 1;
            if (nextIndex >= arguments.Count)
            {
                return OperationResult.Failure(MetaCliParseErrorCode.OptionRequiresValue, $"Option '{optionToken.Token}' requires a value.");
            }

            var value = arguments[nextIndex];
            if (LooksLikeOption(value) && !ParseBool(parameter.ValueShape.AllowsOptionLikeValue))
            {
                return OperationResult.Failure(MetaCliParseErrorCode.OptionRequiresValue, $"Option '{optionToken.Token}' requires a value before '{value}'.");
            }

            state.AddOptionValue(optionToken, value);
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
            return OperationResult.Failure(MetaCliParseErrorCode.UnexpectedArgument, $"Unexpected argument '{value}'.");
        }

        var positional = positionals[positionalIndex];
        var parameter = positional.Parameter;
        var state = states[parameter];
        var arity = ReadArity(parameter.ValueShape);
        if (!arity.Succeeded)
        {
            return OperationResult.Failure(arity.ErrorCode, arity.Error);
        }

        if (arity.Values.MaxValueCount == 0)
        {
            return OperationResult.Failure(MetaCliParseErrorCode.InvalidModel, $"Positional argument '{parameter.Name}' does not accept a value.");
        }

        state.IsPresent = true;
        state.AddPositionalValue(positional, value);

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
                state.AddDefaultValue(state.Parameter.DefaultValue!);
            }

            if (ParseBool(state.Parameter.IsRequired) && state.Values.Count == 0 && !state.IsPresent)
            {
                return OperationResult.Failure(MetaCliParseErrorCode.RequiredParameterMissing, $"Required parameter '{state.Parameter.Name}' was not provided.");
            }

            var arity = ReadArity(state.Parameter.ValueShape);
            if (!arity.Succeeded)
            {
                return OperationResult.Failure(arity.ErrorCode, arity.Error);
            }

            if (state.IsPresent && state.Values.Count < arity.Values.MinValueCount)
            {
                return OperationResult.Failure(MetaCliParseErrorCode.ParameterArityMismatch, $"Parameter '{state.Parameter.Name}' expects at least {arity.Values.MinValueCount.ToString(CultureInfo.InvariantCulture)} value(s).");
            }

            if (!ParseBool(state.Parameter.IsRepeatable) &&
                arity.Values.MaxValueCount is not null &&
                state.Values.Count > arity.Values.MaxValueCount.Value)
            {
                return OperationResult.Failure(MetaCliParseErrorCode.ParameterArityMismatch, $"Parameter '{state.Parameter.Name}' expects at most {arity.Values.MaxValueCount.Value.ToString(CultureInfo.InvariantCulture)} value(s).");
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
                        return OperationResult.Failure(MetaCliParseErrorCode.ValueNotAllowed, $"Parameter '{state.Parameter.Name}' does not allow value '{value}'.");
                    }
                }
            }
        }

        foreach (var group in model.ParameterGroupList.Where(group => ReferenceEquals(group.ExecutableCommand, executableCommand)))
        {
            IReadOnlyList<ParameterGroupMember> members = model.ParameterGroupMemberList
                .Where(member => ReferenceEquals(member.ParameterGroup, group))
                .ToArray();
            if (MetaCliOrdering.TryByPrevious(members, static member => member.PreviousMember, out var orderedMembers))
            {
                members = orderedMembers;
            }

            var presentMembers = members.Count(member => states.TryGetValue(member.Parameter, out var state) && state.IsPresent);
            if (ParseBool(group.IsRequired) && presentMembers == 0)
            {
                return OperationResult.Failure(MetaCliParseErrorCode.ParameterGroupRequired, $"Parameter group '{group.Name}' requires one of: {string.Join(", ", members.Select(static member => member.Parameter.Name))}.");
            }

            if (!ParseBool(group.AllowsMultiple) && presentMembers > 1)
            {
                return OperationResult.Failure(MetaCliParseErrorCode.ParameterGroupConflict, $"Parameter group '{group.Name}' accepts only one member.");
            }
        }

        return OperationResult.Success();
    }

    private static bool TrySplitEqualsOption(
        string token,
        ParserRuntimeRules rules,
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

        if (!rules.AllowsEqualsValueSyntax)
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
            return ValueResult<ValueArityRuntime>.Failure(MetaCliParseErrorCode.InvalidModel, $"Value shape '{valueShape.Name}' has invalid minimum value count.");
        }

        int? max = null;
        if (!string.IsNullOrWhiteSpace(valueShape.ValueArity.MaxValueCount))
        {
            if (!int.TryParse(valueShape.ValueArity.MaxValueCount, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedMax) || parsedMax < 0)
            {
                return ValueResult<ValueArityRuntime>.Failure(MetaCliParseErrorCode.InvalidModel, $"Value shape '{valueShape.Name}' has invalid maximum value count.");
            }

            max = parsedMax;
        }

        if (max is not null && max.Value < min)
        {
            return ValueResult<ValueArityRuntime>.Failure(MetaCliParseErrorCode.InvalidModel, $"Value shape '{valueShape.Name}' has maximum value count below minimum value count.");
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

    private IReadOnlyList<Parameter> EffectiveParametersForExecutable(
        Application application,
        ExecutableCommand executableCommand)
    {
        var parameters = new List<Parameter>();
        parameters.AddRange(model.ApplicationParameterList
            .Where(item => ReferenceEquals(item.Application, application))
            .Select(static item => item.Parameter));
        parameters.AddRange(CommandParametersForExecutable(executableCommand));
        return parameters
            .Distinct(ReferenceComparer<Parameter>.Instance)
            .ToArray();
    }

    private IEnumerable<Parameter> CommandParametersForExecutable(ExecutableCommand executableCommand) =>
        model.ExecutableCommandParameterList
            .Where(item => ReferenceEquals(item.ExecutableCommand, executableCommand))
            .Select(static item => item.Parameter);

    private sealed record ParserRuntimeRules(
        string? StopParsingToken,
        bool AllowsEqualsValueSyntax)
    {
        public static ParserRuntimeRules Default { get; } = new("--", true);
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

        public List<MetaCliParameterOccurrence> Occurrences { get; } = new();

        public void AddOptionOccurrence(OptionToken optionToken)
        {
            Occurrences.Add(new MetaCliParameterOccurrence(null, optionToken, null, false));
        }

        public void AddOptionValue(OptionToken optionToken, string value)
        {
            Values.Add(value);
            Occurrences.Add(new MetaCliParameterOccurrence(value, optionToken, null, false));
        }

        public void AddPositionalValue(PositionalArgument positionalArgument, string value)
        {
            Values.Add(value);
            Occurrences.Add(new MetaCliParameterOccurrence(value, null, positionalArgument, false));
        }

        public void AddDefaultValue(string value)
        {
            Values.Add(value);
            Occurrences.Add(new MetaCliParameterOccurrence(value, null, null, true));
        }

        public MetaCliParameterBinding ToBinding() =>
            new(Parameter, IsPresent, Values.ToArray(), Occurrences.ToArray());
    }

    private readonly record struct CommandMatch(
        bool Succeeded,
        ExecutableCommand ExecutableCommand,
        IReadOnlySet<int> CommandTokenIndexes,
        MetaCliParseErrorCode ErrorCode,
        string Error)
    {
        public static CommandMatch Success(ExecutableCommand executableCommand, IEnumerable<int> commandTokenIndexes) =>
            new(true, executableCommand, commandTokenIndexes.ToHashSet(), MetaCliParseErrorCode.None, string.Empty);

        public static CommandMatch Failure(MetaCliParseErrorCode errorCode, string error) =>
            new(false, null!, new HashSet<int>(), errorCode, error);
    }

    private readonly record struct ParameterBindResult(
        bool Succeeded,
        IReadOnlyList<MetaCliParameterBinding> Bindings,
        MetaCliParseErrorCode ErrorCode,
        string Error)
    {
        public static ParameterBindResult Success(IReadOnlyList<MetaCliParameterBinding> bindings) =>
            new(true, bindings, MetaCliParseErrorCode.None, string.Empty);

        public static ParameterBindResult Failure(MetaCliParseErrorCode errorCode, string error) =>
            new(false, Array.Empty<MetaCliParameterBinding>(), errorCode, error);
    }

    private readonly record struct OperationResult(
        bool Succeeded,
        MetaCliParseErrorCode ErrorCode,
        string Error)
    {
        public static OperationResult Success() => new(true, MetaCliParseErrorCode.None, string.Empty);

        public static OperationResult Failure(MetaCliParseErrorCode errorCode, string error) => new(false, errorCode, error);
    }

    private readonly record struct ValueResult<T>(
        bool Succeeded,
        T Values,
        MetaCliParseErrorCode ErrorCode,
        string Error)
    {
        public static ValueResult<T> Success(T values) => new(true, values, MetaCliParseErrorCode.None, string.Empty);

        public static ValueResult<T> Failure(MetaCliParseErrorCode errorCode, string error) => new(false, default!, errorCode, error);
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class?
    {
        public static readonly ReferenceComparer<T> Instance = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
