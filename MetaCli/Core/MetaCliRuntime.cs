using System.Globalization;
using System.Runtime.CompilerServices;
using Meta.Core.Serialization;

namespace MetaCli.Core;

public delegate void MetaCliCommandHandler(MetaCliInvocation invocation);

public delegate void MetaCliModelCommandHandler<TModel>(
    MetaCliInvocation invocation,
    TModel model)
    where TModel : IMetaWorkspaceModel<TModel>;

public sealed class MetaCliRuntime<TModel>
    where TModel : IMetaWorkspaceModel<TModel>
{
    private readonly string commandWorkspacePath;
    private readonly string? applicationId;
    private readonly string workspaceParameter;
    private readonly TextWriter error;
    private readonly Action<int> setExitCode;
    private readonly Dictionary<string, HandlerBinding> handlers = new(StringComparer.Ordinal);
    private MetaCliModel model = MetaCliModel.CreateEmpty();

    public MetaCliRuntime(
        string commandWorkspacePath,
        string? applicationId = null,
        string workspaceParameter = "workspace",
        TextWriter? error = null,
        Action<int>? setExitCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandWorkspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceParameter);

        this.commandWorkspacePath = commandWorkspacePath;
        this.applicationId = string.IsNullOrWhiteSpace(applicationId) ? null : applicationId.Trim();
        this.workspaceParameter = workspaceParameter.Trim();
        this.error = error ?? Console.Error;
        this.setExitCode = setExitCode ?? (code => Environment.ExitCode = code);
    }

    public MetaCliRuntime<TModel> Bind(string executableCommandId, MetaCliCommandHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding.WithoutWorkspace(handler);
        return this;
    }

    public MetaCliRuntime<TModel> Bind(string executableCommandId, MetaCliModelCommandHandler<TModel> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding.WithWorkspace(handler);
        return this;
    }

    public void Run(params string[] arguments) =>
        Run((IReadOnlyList<string>)arguments);

    public void Run(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            model = MetaCliModel.LoadFromXmlWorkspace(commandWorkspacePath, searchUpward: false);
        }
        catch (Exception exception)
        {
            Fail(4, $"Cannot load command surface workspace '{Path.GetFullPath(commandWorkspacePath)}'. {exception.Message}");
            return;
        }

        var parse = Parse(arguments);
        if (!parse.Succeeded)
        {
            Fail(2, parse.Message ?? "Command line could not be parsed.");
            return;
        }

        var invocation = parse.RequireInvocation();
        if (!handlers.TryGetValue(invocation.ExecutableCommand.Id, out var handler))
        {
            Fail(4, $"Command '{invocation.CommandRoute}' is modeled but has no implementation.");
            return;
        }

        try
        {
            if (handler.WorkspaceHandler is not null)
            {
                var workspacePath = ResolveWorkspacePath(invocation);
                var domainModel = TModel.LoadFromXmlWorkspace(workspacePath, searchUpward: false);
                handler.WorkspaceHandler(invocation, domainModel);
            }
            else
            {
                handler.Handler!(invocation);
            }
        }
        catch (Exception exception)
        {
            Fail(4, $"Command '{invocation.CommandRoute}' failed. {exception.Message}");
            return;
        }

        setExitCode(0);
    }

    private MetaCliParseResult Parse(IReadOnlyList<string> arguments)
    {
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

        var bindResult = BindParameters(application, commandMatch.ExecutableCommand, rules, arguments, commandMatch.ConsumedTokenCount);
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

    private string ResolveWorkspacePath(MetaCliInvocation invocation)
    {
        try
        {
            var value = invocation.Optional(workspaceParameter);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return Path.GetFullPath(value);
            }
        }
        catch (KeyNotFoundException)
        {
        }

        return Directory.GetCurrentDirectory();
    }

    private void Fail(int exitCode, string message)
    {
        error.WriteLine(message);
        setExitCode(exitCode);
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
                return CommandMatch.Failure(MetaCliParseErrorCode.CommandAmbiguous, $"Command token '{token}' is ambiguous.");
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
            if (arguments.Count == 0)
            {
                var defaultCommand = model.ApplicationDefaultCommandList
                    .SingleOrDefault(item => ReferenceEquals(item.Application, application));
                return defaultCommand is null
                    ? CommandMatch.Failure(MetaCliParseErrorCode.CommandMissing, "No command was provided.")
                    : CommandMatch.Success(defaultCommand.ExecutableCommand, 0);
            }

            var first = arguments[0];
            if (LooksLikeOption(first))
            {
                return CommandMatch.Failure(MetaCliParseErrorCode.UnknownOption, $"Option '{first}' is not recognized.");
            }

            return CommandMatch.Failure(MetaCliParseErrorCode.CommandNotFound, $"Command '{first}' is not modeled for application '{application.Name}'.");
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
        int startIndex)
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
        var seenPositional = false;
        var positionalMode = false;
        for (var index = startIndex; index < arguments.Count; index++)
        {
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

                if (!rules.AllowsOptionsAfterPositionals && seenPositional)
                {
                    return ParameterBindResult.Failure(MetaCliParseErrorCode.OptionAfterPositional, $"Option '{optionToken.Token}' cannot appear after positional arguments.");
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
                if (!rules.AllowsOptionsAfterPositionals && seenPositional)
                {
                    return ParameterBindResult.Failure(MetaCliParseErrorCode.OptionAfterPositional, $"Option '{exactOptionToken.Token}' cannot appear after positional arguments.");
                }

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

            seenPositional = true;
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

            if (arity.Values.MaxValueCount is not null && state.Values.Count > arity.Values.MaxValueCount.Value)
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
            var members = model.ParameterGroupMemberList
                .Where(member => ReferenceEquals(member.ParameterGroup, group))
                .ToArray();
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

    private sealed record HandlerBinding(
        MetaCliCommandHandler? Handler,
        MetaCliModelCommandHandler<TModel>? WorkspaceHandler)
    {
        public static HandlerBinding WithoutWorkspace(MetaCliCommandHandler handler) =>
            new(handler, null);

        public static HandlerBinding WithWorkspace(MetaCliModelCommandHandler<TModel> handler) =>
            new(null, handler);
    }

    private sealed record ParserRuntimeRules(
        string? StopParsingToken,
        bool AllowsEqualsValueSyntax,
        bool AllowsOptionsAfterPositionals)
    {
        public static ParserRuntimeRules Default { get; } = new("--", true, false);
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
        int ConsumedTokenCount,
        MetaCliParseErrorCode ErrorCode,
        string Error)
    {
        public static CommandMatch Success(ExecutableCommand executableCommand, int consumedTokenCount) =>
            new(true, executableCommand, consumedTokenCount, MetaCliParseErrorCode.None, string.Empty);

        public static CommandMatch Failure(MetaCliParseErrorCode errorCode, string error) =>
            new(false, null!, 0, errorCode, error);
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
