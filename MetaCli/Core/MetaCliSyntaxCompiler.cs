namespace MetaCli.Core;

public sealed class MetaCliSyntaxCompiler
{
    public MetaCliModel CompileFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Syntax path is required.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        return Compile(File.ReadAllText(fullPath), fullPath);
    }

    public MetaCliModel Compile(string sourceText, string sourceName = "<syntax>")
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        var state = new CompilerState(sourceName);
        CommandContext? currentCommand = null;

        foreach (var line in ReadLines(sourceText))
        {
            var sourceLine = SourceLine.Parse(line.Text, line.Number);
            if (sourceLine.Tokens.Count == 0)
            {
                continue;
            }

            currentCommand = sourceLine.IsChild
                ? CompileChild(state, currentCommand, sourceLine)
                : CompileTopLevel(state, sourceLine);
        }

        if (state.Application is null)
        {
            throw state.Error(1, "Syntax must declare one application.");
        }

        return state.Model;
    }

    private static CommandContext? CompileTopLevel(CompilerState state, SourceLine line)
    {
        var keyword = line.Tokens[0];
        if (Is(keyword, "application"))
        {
            CompileApplication(state, line);
            return null;
        }

        if (Is(keyword, "arity"))
        {
            CompileArity(state, line);
            return null;
        }

        if (Is(keyword, "shape"))
        {
            CompileShape(state, line);
            return null;
        }

        if (Is(keyword, "command"))
        {
            return CompileCommand(state, line, executable: true);
        }

        if (Is(keyword, "option"))
        {
            CompileApplicationOption(state, line);
            return null;
        }

        if (Is(keyword, "group"))
        {
            CompileCommand(state, line, executable: false);
            return null;
        }

        throw state.Error(line.Number, $"Unknown top-level syntax '{keyword}'.");
    }

    private static CommandContext CompileChild(CompilerState state, CommandContext? currentCommand, SourceLine line)
    {
        if (currentCommand is null)
        {
            throw state.Error(line.Number, $"'{line.Tokens[0]}' must be placed under a command block.");
        }

        var keyword = line.Tokens[0];
        if (Is(keyword, "option"))
        {
            CompileOption(state, currentCommand, line);
            return currentCommand;
        }

        if (Is(keyword, "positional"))
        {
            CompilePositional(state, currentCommand, line);
            return currentCommand;
        }

        if (Is(keyword, "parameter-group"))
        {
            CompileParameterGroup(state, currentCommand, line);
            return currentCommand;
        }

        throw state.Error(line.Number, $"Unknown command syntax '{keyword}'.");
    }

    private static void CompileApplication(CompilerState state, SourceLine line)
    {
        if (state.Application is not null)
        {
            throw state.Error(line.Number, "Syntax can declare only one application.");
        }

        RequireTokenCount(state, line, 2, "application <name> [id <id>] [executable <name>] [version <value>] [description <text>]");
        var name = line.Tokens[1];
        var id = "app-" + NormalizeIdPart(name);
        var executableName = name;
        string? version = null;
        string? description = null;

        for (var index = 2; index < line.Tokens.Count;)
        {
            var token = line.Tokens[index++];
            if (Is(token, "id"))
            {
                id = RequireValue(state, line, ref index, token);
            }
            else if (Is(token, "executable"))
            {
                executableName = RequireValue(state, line, ref index, token);
            }
            else if (Is(token, "version"))
            {
                version = RequireValue(state, line, ref index, token);
            }
            else if (Is(token, "description"))
            {
                description = RequireValue(state, line, ref index, token);
            }
            else
            {
                throw state.Error(line.Number, $"Unknown application modifier '{token}'.");
            }
        }

        var application = new Application
        {
            Id = Required(id, "application id"),
            Name = Required(name, "application name"),
            ExecutableName = EmptyToNull(executableName),
            Version = EmptyToNull(version),
            Description = EmptyToNull(description),
        };
        state.Model.ApplicationList.Add(application);
        state.Application = application;
    }

    private static void CompileArity(CompilerState state, SourceLine line)
    {
        RequireTokenCount(state, line, 4, "arity <name> <min> <max|*> [id <id>] [description <text>]");
        var name = line.Tokens[1];
        var min = line.Tokens[2];
        var max = Is(line.Tokens[3], "*") ? null : line.Tokens[3];
        var id = "arity-" + NormalizeIdPart(name);
        string? description = null;

        for (var index = 4; index < line.Tokens.Count;)
        {
            var token = line.Tokens[index++];
            if (Is(token, "id"))
            {
                id = RequireValue(state, line, ref index, token);
            }
            else if (Is(token, "description"))
            {
                description = RequireValue(state, line, ref index, token);
            }
            else
            {
                throw state.Error(line.Number, $"Unknown arity modifier '{token}'.");
            }
        }

        var arity = new ValueArity
        {
            Id = Required(id, "value arity id"),
            Name = DisplayName(name),
            MinValueCount = Required(min, "min value count"),
            MaxValueCount = EmptyToNull(max),
            Description = EmptyToNull(description),
        };
        state.Model.ValueArityList.Add(arity);
        state.IndexArity(arity);
    }

    private static void CompileShape(CompilerState state, SourceLine line)
    {
        RequireTokenCount(state, line, 3, "shape <name> <arity> [label <label>] [option-like] [values <value>...]");
        var name = line.Tokens[1];
        var arity = state.ResolveArity(line.Number, line.Tokens[2]);
        var id = "shape-" + NormalizeIdPart(name);
        string? label = null;
        var optionLike = false;
        string? description = null;
        var values = new List<string>();

        for (var index = 3; index < line.Tokens.Count;)
        {
            var token = line.Tokens[index++];
            if (Is(token, "id"))
            {
                id = RequireValue(state, line, ref index, token);
            }
            else if (Is(token, "label"))
            {
                label = RequireValue(state, line, ref index, token);
            }
            else if (Is(token, "option-like"))
            {
                optionLike = true;
            }
            else if (Is(token, "description"))
            {
                description = RequireValue(state, line, ref index, token);
            }
            else if (Is(token, "values"))
            {
                while (index < line.Tokens.Count)
                {
                    values.Add(line.Tokens[index++]);
                }
            }
            else
            {
                throw state.Error(line.Number, $"Unknown shape modifier '{token}'.");
            }
        }

        var shape = new ValueShape
        {
            Id = Required(id, "value shape id"),
            Name = DisplayName(name),
            ValueArity = arity,
            ValueLabel = EmptyToNull(label),
            AllowsOptionLikeValue = optionLike ? "true" : null,
            Description = EmptyToNull(description),
        };
        state.Model.ValueShapeList.Add(shape);
        state.IndexShape(shape);

        foreach (var value in values)
        {
            state.Model.AllowedValueList.Add(new AllowedValue
            {
                Id = "allowed-" + NormalizeIdPart(name) + "-" + NormalizeIdPart(value),
                ValueShape = shape,
                Value = value,
            });
        }
    }

    private static CommandContext CompileCommand(CompilerState state, SourceLine line, bool executable)
    {
        var application = state.RequireApplication(line.Number);
        RequireTokenCount(state, line, 2, executable ? "command <route> [default]" : "group <route>");
        var routeTokens = line.Tokens.Skip(1).ToList();
        var isDefault = false;
        while (executable && routeTokens.Count > 0)
        {
            var last = routeTokens[^1];
            if (Is(last, "default"))
            {
                isDefault = true;
                routeTokens.RemoveAt(routeTokens.Count - 1);
                continue;
            }

            break;
        }

        if (routeTokens.Count == 0)
        {
            throw state.Error(line.Number, executable ? "Command route is empty." : "Group route is empty.");
        }

        if (!executable && isDefault)
        {
            throw state.Error(line.Number, "Only command lines can use default role modifiers.");
        }

        var context = state.EnsureCommandRoute(line.Number, routeTokens, executable);
        if (executable && isDefault)
        {
            if (state.Model.ApplicationDefaultCommandList.Any(defaultCommand => ReferenceEquals(defaultCommand.Application, application)))
            {
                throw state.Error(line.Number, $"Application '{application.Id}' already has a default command.");
            }

            state.Model.ApplicationDefaultCommandList.Add(new ApplicationDefaultCommand
            {
                Id = application.Id + ":default-command",
                Application = application,
                ExecutableCommand = context.RequireExecutable(state, line.Number),
            });
        }

        if (!executable)
        {
            return null!;
        }

        return context;
    }

    private static void CompileApplicationOption(CompilerState state, SourceLine line)
    {
        var application = state.RequireApplication(line.Number);
        var option = CompileOptionAggregate(state, null, line, idScope: "application");
        state.Model.ApplicationParameterList.Add(new ApplicationParameter
        {
            Id = application.Id + ":parameter:" + option.Parameter.Id,
            Application = application,
            Parameter = option.Parameter,
        });
        state.IndexApplicationParameter(line.Number, option.Parameter);
    }

    private static void CompileOption(CompilerState state, CommandContext context, SourceLine line)
    {
        var executableCommand = context.RequireExecutable(state, line.Number);
        var option = CompileOptionAggregate(state, context, line, idScope: context.RouteId);
        state.Model.ExecutableCommandParameterList.Add(new ExecutableCommandParameter
        {
            Id = executableCommand.Id + ":parameter:" + option.Parameter.Id,
            ExecutableCommand = executableCommand,
            Parameter = option.Parameter,
        });
        state.IndexParameter(line.Number, context, option.Parameter);
    }

    private static Option CompileOptionAggregate(
        CompilerState state,
        CommandContext? context,
        SourceLine line,
        string idScope)
    {
        RequireTokenCount(state, line, 3, "option <token> <shape> [required] [repeatable] [alias <token>] [name <name>]");
        var token = line.Tokens[1];
        var shape = state.ResolveShape(line.Number, line.Tokens[2]);
        var aliases = new List<string>();
        var name = OptionName(token);
        string? defaultValue = null;
        string? description = null;
        var required = false;
        var repeatable = false;

        for (var index = 3; index < line.Tokens.Count;)
        {
            var modifier = line.Tokens[index++];
            if (Is(modifier, "required"))
            {
                required = true;
            }
            else if (Is(modifier, "repeatable"))
            {
                repeatable = true;
            }
            else if (Is(modifier, "default"))
            {
                defaultValue = RequireValue(state, line, ref index, modifier);
            }
            else if (Is(modifier, "alias"))
            {
                aliases.Add(RequireValue(state, line, ref index, modifier));
            }
            else if (Is(modifier, "name"))
            {
                name = RequireValue(state, line, ref index, modifier);
            }
            else if (Is(modifier, "description"))
            {
                description = RequireValue(state, line, ref index, modifier);
            }
            else
            {
                throw state.Error(line.Number, $"Unknown option modifier '{modifier}'.");
            }
        }

        var optionIdPart = NormalizeIdPart(name);
        var parameter = new Parameter
        {
            Id = MakeId("param", idScope, "option", optionIdPart),
            ValueShape = shape,
            Name = Required(name, "option name"),
            IsRequired = required ? "true" : null,
            IsRepeatable = repeatable ? "true" : null,
            DefaultValue = EmptyToNull(defaultValue),
            Description = EmptyToNull(description),
        };
        var option = new Option
        {
            Id = MakeId("option", idScope, "parameter", optionIdPart),
            Parameter = parameter,
        };
        var primaryToken = new OptionToken
        {
            Id = MakeId("token", idScope, "option", optionIdPart),
            Option = option,
            Token = Required(token, "option token"),
        };
        state.Model.ParameterList.Add(parameter);
        state.Model.OptionList.Add(option);
        state.Model.OptionTokenList.Add(primaryToken);

        var previousToken = primaryToken;
        foreach (var alias in aliases)
        {
            var aliasToken = new OptionToken
            {
                Id = MakeId("token", idScope, "option", optionIdPart, "alias", NormalizeIdPart(alias)),
                Option = option,
                Token = alias,
                PreviousToken = previousToken,
            };
            state.Model.OptionTokenList.Add(aliasToken);
            previousToken = aliasToken;
        }

        return option;
    }

    private static void CompilePositional(CompilerState state, CommandContext context, SourceLine line)
    {
        RequireTokenCount(state, line, 3, "positional <name> <shape> [required] [repeatable]");
        var name = line.Tokens[1];
        var shape = state.ResolveShape(line.Number, line.Tokens[2]);
        string? defaultValue = null;
        string? description = null;
        var required = false;
        var repeatable = false;

        for (var index = 3; index < line.Tokens.Count;)
        {
            var modifier = line.Tokens[index++];
            if (Is(modifier, "required"))
            {
                required = true;
            }
            else if (Is(modifier, "repeatable"))
            {
                repeatable = true;
            }
            else if (Is(modifier, "default"))
            {
                defaultValue = RequireValue(state, line, ref index, modifier);
            }
            else if (Is(modifier, "description"))
            {
                description = RequireValue(state, line, ref index, modifier);
            }
            else
            {
                throw state.Error(line.Number, $"Unknown positional modifier '{modifier}'.");
            }
        }

        var positionalIdPart = NormalizeIdPart(name);
        var executableCommand = context.RequireExecutable(state, line.Number);
        var parameter = new Parameter
        {
            Id = MakeId("param", "command", context.RouteId, "positional", positionalIdPart),
            ValueShape = shape,
            Name = Required(name, "positional name"),
            IsRequired = required ? "true" : null,
            IsRepeatable = repeatable ? "true" : null,
            DefaultValue = EmptyToNull(defaultValue),
            Description = EmptyToNull(description),
        };
        var positional = new PositionalArgument
        {
            Id = MakeId("pos", "command", context.RouteId, "argument", positionalIdPart),
            Parameter = parameter,
            PreviousArgument = context.LastPositional,
        };
        state.Model.ParameterList.Add(parameter);
        state.Model.ExecutableCommandParameterList.Add(new ExecutableCommandParameter
        {
            Id = executableCommand.Id + ":parameter:" + parameter.Id,
            ExecutableCommand = executableCommand,
            Parameter = parameter,
        });
        state.Model.PositionalArgumentList.Add(positional);
        context.LastPositional = positional;
        state.IndexParameter(line.Number, context, parameter);
    }

    private static void CompileParameterGroup(CompilerState state, CommandContext context, SourceLine line)
    {
        RequireTokenCount(state, line, 4, "parameter-group <name> [required] [multiple] members <parameter-name>...");
        var name = line.Tokens[1];
        var required = false;
        var multiple = false;
        var members = new List<string>();

        for (var index = 2; index < line.Tokens.Count;)
        {
            var modifier = line.Tokens[index++];
            if (Is(modifier, "required"))
            {
                required = true;
            }
            else if (Is(modifier, "multiple"))
            {
                multiple = true;
            }
            else if (Is(modifier, "members"))
            {
                while (index < line.Tokens.Count)
                {
                    members.Add(line.Tokens[index++]);
                }
            }
            else
            {
                throw state.Error(line.Number, $"Unknown parameter-group modifier '{modifier}'.");
            }
        }

        if (members.Count == 0)
        {
            throw state.Error(line.Number, $"Parameter group '{name}' must list members.");
        }

        var groupIdPart = NormalizeIdPart(name);
        var executableCommand = context.RequireExecutable(state, line.Number);
        var group = new ParameterGroup
        {
            Id = MakeId("group", "command", context.RouteId, groupIdPart),
            ExecutableCommand = executableCommand,
            Name = Required(name, "parameter group name"),
            IsRequired = required ? "true" : null,
            AllowsMultiple = multiple ? "true" : null,
        };
        state.Model.ParameterGroupList.Add(group);

        ParameterGroupMember? previous = null;
        foreach (var memberName in members)
        {
            var parameter = state.ResolveParameter(line.Number, context, memberName);
            var member = new ParameterGroupMember
            {
                Id = MakeId("member", "command", context.RouteId, "group", groupIdPart, "parameter", NormalizeIdPart(parameter.Name)),
                ParameterGroup = group,
                Parameter = parameter,
                PreviousMember = previous,
            };
            state.Model.ParameterGroupMemberList.Add(member);
            previous = member;
        }
    }

    private static IEnumerable<(int Number, string Text)> ReadLines(string sourceText)
    {
        using var reader = new StringReader(sourceText);
        var number = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            number++;
            yield return (number, line);
        }
    }

    private static void RequireTokenCount(CompilerState state, SourceLine line, int minCount, string usage)
    {
        if (line.Tokens.Count < minCount)
        {
            throw state.Error(line.Number, $"Expected: {usage}.");
        }
    }

    private static string RequireValue(CompilerState state, SourceLine line, ref int index, string modifier)
    {
        if (index >= line.Tokens.Count)
        {
            throw state.Error(line.Number, $"Missing value after '{modifier}'.");
        }

        return line.Tokens[index++];
    }

    private static string RequireBoolValue(CompilerState state, SourceLine line, ref int index, string modifier)
    {
        var value = RequireValue(state, line, ref index, modifier);
        if (!Is(value, "true") && !Is(value, "false"))
        {
            throw state.Error(line.Number, $"'{modifier}' must be true or false.");
        }

        return value.ToLowerInvariant();
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{name} is required.")
            : value.Trim();

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Is(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static string OptionName(string token)
    {
        var name = token.TrimStart('-');
        return string.IsNullOrWhiteSpace(name) ? NormalizeIdPart(token) : name;
    }

    private static string DisplayName(string value)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        return char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }

    private static string MakeId(params string[] parts) =>
        string.Join("-", parts.Select(NormalizeIdPart).Where(static part => !string.IsNullOrWhiteSpace(part)));

    private static string NormalizeIdPart(string value)
    {
        var builder = new System.Text.StringBuilder();
        var previousDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                previousDash = false;
            }
            else if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private sealed class CompilerState
    {
        private readonly string sourceName;
        private readonly Dictionary<string, ValueArity> arities = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ValueShape> shapes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CommandContext> commandContexts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Parameter> parameters = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Parameter> applicationParameters = new(StringComparer.OrdinalIgnoreCase);

        public CompilerState(string sourceName)
        {
            this.sourceName = sourceName;
        }

        public MetaCliModel Model { get; } = MetaCliModel.CreateEmpty();

        public Application? Application { get; set; }

        public InvalidOperationException Error(int lineNumber, string message) =>
            new($"{sourceName}:{lineNumber}: {message}");

        public Application RequireApplication(int lineNumber) =>
            Application ?? throw Error(lineNumber, "Declare application before this line.");

        public void IndexArity(ValueArity arity)
        {
            arities[arity.Id] = arity;
            arities[arity.Name] = arity;
        }

        public ValueArity ResolveArity(int lineNumber, string value) =>
            arities.TryGetValue(value, out var arity)
                ? arity
                : AddBuiltInArity(lineNumber, value);

        public void IndexShape(ValueShape shape)
        {
            shapes[shape.Id] = shape;
            shapes[shape.Name] = shape;
        }

        public ValueShape ResolveShape(int lineNumber, string value) =>
            shapes.TryGetValue(value, out var shape)
                ? shape
                : AddBuiltInShape(lineNumber, value);

        public CommandContext EnsureCommandRoute(int lineNumber, IReadOnlyList<string> routeTokens, bool executable)
        {
            var application = RequireApplication(lineNumber);
            Command? parent = null;
            var route = new List<string>();
            CommandContext? context = null;
            for (var index = 0; index < routeTokens.Count; index++)
            {
                var segment = routeTokens[index];
                route.Add(segment);
                var routeId = NormalizeIdPart(string.Join("-", route));
                if (!commandContexts.TryGetValue(routeId, out context))
                {
                    var command = new Command
                    {
                        Id = "cmd-" + routeId,
                        Application = application,
                        Name = segment,
                        Token = segment,
                        ParentCommand = parent,
                    };
                    Model.CommandList.Add(command);
                    context = new CommandContext(routeId, command, executableCommand: null);
                    commandContexts[routeId] = context;
                }

                parent = context.Command;
            }

            if (context is null)
            {
                throw Error(lineNumber, "Command route is empty.");
            }

            if (executable && context.ExecutableCommand is null)
            {
                context.ExecutableCommand = new ExecutableCommand
                {
                    Id = "exec-" + context.RouteId,
                    Command = context.Command,
                };
                Model.ExecutableCommandList.Add(context.ExecutableCommand);
            }

            if (executable && context.ExecutableCommand is null)
            {
                throw Error(lineNumber, $"Command '{string.Join(" ", routeTokens)}' is not runnable.");
            }

            return context;
        }

        public void IndexApplicationParameter(int lineNumber, Parameter parameter)
        {
            if (applicationParameters.ContainsKey(parameter.Name))
            {
                throw Error(lineNumber, $"Application parameter '{parameter.Name}' is already defined.");
            }

            applicationParameters[parameter.Name] = parameter;
        }

        public void IndexParameter(int lineNumber, CommandContext context, Parameter parameter)
        {
            var key = ParameterKey(context, parameter.Name);
            if (parameters.ContainsKey(key))
            {
                throw Error(lineNumber, $"Parameter '{parameter.Name}' is already defined on command '{context.Command.Name}'.");
            }

            parameters[key] = parameter;
        }

        public Parameter ResolveParameter(int lineNumber, CommandContext context, string name)
        {
            var key = ParameterKey(context, name);
            return parameters.TryGetValue(key, out var parameter)
                ? parameter
                : throw Error(lineNumber, $"Parameter '{name}' does not exist on command '{context.Command.Name}'.");
        }

        private static string ParameterKey(CommandContext context, string name) =>
            context.RouteId + "\0" + name;

        private ValueArity AddBuiltInArity(int lineNumber, string value)
        {
            if (Is(value, "none") || Is(value, "arity-none"))
            {
                return AddBuiltInArity("arity-none", "None", "0", "0");
            }

            if (Is(value, "one") || Is(value, "arity-one"))
            {
                return AddBuiltInArity("arity-one", "One", "1", "1");
            }

            throw Error(lineNumber, $"Value arity '{value}' does not exist.");
        }

        private ValueArity AddBuiltInArity(string id, string name, string min, string max)
        {
            var arity = new ValueArity
            {
                Id = id,
                Name = name,
                MinValueCount = min,
                MaxValueCount = max,
            };
            Model.ValueArityList.Add(arity);
            IndexArity(arity);
            return arity;
        }

        private ValueShape AddBuiltInShape(int lineNumber, string value)
        {
            if (Is(value, "flag") || Is(value, "shape-flag"))
            {
                return AddBuiltInShape("shape-flag", "Flag", ResolveArity(lineNumber, "none"), null, false, Array.Empty<string>());
            }

            if (Is(value, "text") || Is(value, "shape-text"))
            {
                return AddBuiltInShape("shape-text", "Text", ResolveArity(lineNumber, "one"), "<value>", false, Array.Empty<string>());
            }

            if (Is(value, "path") || Is(value, "shape-path"))
            {
                return AddBuiltInShape("shape-path", "Path", ResolveArity(lineNumber, "one"), "<path>", true, Array.Empty<string>());
            }

            if (Is(value, "token") || Is(value, "shape-token"))
            {
                return AddBuiltInShape("shape-token", "Token", ResolveArity(lineNumber, "one"), "<token>", true, Array.Empty<string>());
            }

            if (Is(value, "bool") || Is(value, "boolean") || Is(value, "shape-bool"))
            {
                return AddBuiltInShape("shape-bool", "Boolean", ResolveArity(lineNumber, "one"), "true|false", false, new[] { "true", "false" });
            }

            throw Error(lineNumber, $"Value shape '{value}' does not exist.");
        }

        private ValueShape AddBuiltInShape(
            string id,
            string name,
            ValueArity arity,
            string? label,
            bool optionLike,
            IReadOnlyList<string> values)
        {
            var shape = new ValueShape
            {
                Id = id,
                Name = name,
                ValueArity = arity,
                ValueLabel = label,
                AllowsOptionLikeValue = optionLike ? "true" : null,
            };
            Model.ValueShapeList.Add(shape);
            IndexShape(shape);
            if (id.StartsWith("shape-", StringComparison.OrdinalIgnoreCase))
            {
                shapes[id["shape-".Length..]] = shape;
            }

            foreach (var value in values)
            {
                Model.AllowedValueList.Add(new AllowedValue
                {
                    Id = "allowed-" + NormalizeIdPart(name) + "-" + NormalizeIdPart(value),
                    ValueShape = shape,
                    Value = value,
                });
            }

            return shape;
        }

    }

    private sealed class CommandContext
    {
        public CommandContext(string routeId, Command command, ExecutableCommand? executableCommand)
        {
            RouteId = routeId;
            Command = command;
            ExecutableCommand = executableCommand;
        }

        public string RouteId { get; }

        public Command Command { get; }

        public ExecutableCommand? ExecutableCommand { get; set; }

        public PositionalArgument? LastPositional { get; set; }

        public ExecutableCommand RequireExecutable(CompilerState state, int lineNumber) =>
            ExecutableCommand ?? throw state.Error(lineNumber, $"Command '{Command.Name}' is not runnable.");
    }

    private sealed record SourceLine(
        int Number,
        bool IsChild,
        IReadOnlyList<string> Tokens)
    {
        public static SourceLine Parse(string text, int number)
        {
            var isChild = text.Length > 0 && char.IsWhiteSpace(text[0]);
            return new SourceLine(number, isChild, Tokenize(text.Trim(), number));
        }

        private static IReadOnlyList<string> Tokenize(string text, int number)
        {
            var tokens = new List<string>();
            var builder = new System.Text.StringBuilder();
            var inQuote = false;
            var escaping = false;

            void Flush()
            {
                if (builder.Length == 0)
                {
                    return;
                }

                tokens.Add(builder.ToString());
                builder.Clear();
            }

            foreach (var ch in text)
            {
                if (escaping)
                {
                    builder.Append(ch);
                    escaping = false;
                    continue;
                }

                if (inQuote && ch == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (ch == '"')
                {
                    inQuote = !inQuote;
                    continue;
                }

                if (!inQuote && ch == '#')
                {
                    break;
                }

                if (!inQuote && char.IsWhiteSpace(ch))
                {
                    Flush();
                    continue;
                }

                builder.Append(ch);
            }

            if (inQuote)
            {
                throw new InvalidOperationException($"Syntax line {number} has an unterminated quote.");
            }

            Flush();
            return tokens;
        }
    }
}
