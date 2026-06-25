using System.Globalization;
using System.Runtime.CompilerServices;

namespace MetaCli.Core;

public sealed class MetaCliWorkspaceService
{
    public MetaCliModel CreateEmpty() => MetaCliModel.CreateEmpty();

    public MetaCliModel Load(string? workspacePath) =>
        MetaCliModel.LoadFromXmlWorkspace(ResolveWorkspacePath(workspacePath), searchUpward: false);

    public void Save(MetaCliModel model, string? workspacePath)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.SaveToXmlWorkspace(ResolveWorkspacePath(workspacePath));
    }

    public MetaCliShowResult Show(string? workspacePath)
    {
        var model = Load(workspacePath);
        var applications = model.ApplicationList
            .OrderBy(static application => application.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static application => application.Id, StringComparer.Ordinal)
            .Select(application => BuildApplicationSummary(model, application))
            .ToArray();

        return new MetaCliShowResult(applications);
    }

    public MetaCliIntegrityResult ValidateIntegrity(string? workspacePath) =>
        ValidateIntegrity(Load(workspacePath));

    public MetaCliIntegrityResult ValidateIntegrity(MetaCliModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var issues = Validate(model, includeCompleteness: true);
        return new MetaCliIntegrityResult(
            model.ApplicationList.Count,
            model.CommandList.Count,
            model.ExecutableCommandList.Count,
            model.ParameterList.Count,
            model.ApplicationParameterList.Count,
            model.ExecutableCommandParameterList.Count,
            model.OptionList.Count,
            model.OptionTokenList.Count,
            model.PositionalArgumentList.Count,
            model.ParameterGroupList.Count,
            model.ParameterGroupMemberList.Count,
            model.ValueArityList.Count,
            model.ValueShapeList.Count,
            model.AllowedValueList.Count,
            issues);
    }

    public MetaCliFromSyntaxResult CreateFromSyntaxFile(string syntaxPath, string? workspacePath)
    {
        if (string.IsNullOrWhiteSpace(syntaxPath))
        {
            throw new ArgumentException("Syntax path is required.", nameof(syntaxPath));
        }

        var sourcePath = Path.GetFullPath(syntaxPath);
        var model = new MetaCliSyntaxCompiler().CompileFile(sourcePath);
        var issues = Validate(model, includeCompleteness: true);
        if (issues.Any(static issue => issue.Severity == MetaCliIssueSeverity.Error))
        {
            throw new InvalidOperationException(RenderValidationFailure(issues));
        }

        var fullWorkspacePath = ResolveWorkspacePath(workspacePath);
        model.SaveToXmlWorkspace(fullWorkspacePath);
        return new MetaCliFromSyntaxResult(
            sourcePath,
            fullWorkspacePath,
            model.ApplicationList.Count,
            model.CommandList.Count,
            model.ExecutableCommandList.Count,
            model.ParameterList.Count,
            model.ApplicationParameterList.Count,
            model.ExecutableCommandParameterList.Count,
            model.OptionList.Count,
            model.PositionalArgumentList.Count,
            model.ParameterGroupList.Count);
    }

    public Application AddApplication(
        string? workspacePath,
        string id,
        string name,
        string? executableName,
        string? version,
        string? description) =>
        Mutate(workspacePath, model =>
        {
            RequireNewId(model.ApplicationList, static item => item.Id, id, "Application");
            var application = new Application
            {
                Id = RequiredText(id, "id"),
                Name = RequiredText(name, "name"),
                ExecutableName = EmptyToNull(executableName),
                Version = EmptyToNull(version),
                Description = EmptyToNull(description),
            };
            model.ApplicationList.Add(application);
            return application;
        });

    public Command AddCommand(
        string? workspacePath,
        string id,
        string applicationId,
        string name,
        string token,
        string? parentCommandId,
        string? description) =>
        Mutate(workspacePath, model =>
        {
            RequireNewId(model.CommandList, static item => item.Id, id, "Command");
            var application = RequireById(model.ApplicationList, static item => item.Id, applicationId, "Application");
            var parent = string.IsNullOrWhiteSpace(parentCommandId)
                ? null
                : RequireById(model.CommandList, static item => item.Id, parentCommandId, "Command");
            if (parent is not null && !ReferenceEquals(parent.Application, application))
            {
                throw new InvalidOperationException($"Parent command '{parent.Id}' does not belong to application '{application.Id}'.");
            }

            var command = new Command
            {
                Id = RequiredText(id, "id"),
                Application = application,
                Name = RequiredText(name, "name"),
                Token = RequiredText(token, "token"),
                ParentCommand = parent,
                Description = EmptyToNull(description),
            };
            model.CommandList.Add(command);
            return command;
        });

    public ExecutableCommand AddExecutableCommand(
        string? workspacePath,
        string id,
        string commandId) =>
        Mutate(workspacePath, model =>
        {
            RequireNewId(model.ExecutableCommandList, static item => item.Id, id, "ExecutableCommand");
            var command = RequireById(model.CommandList, static item => item.Id, commandId, "Command");
            var executableCommand = new ExecutableCommand
            {
                Id = RequiredText(id, "id"),
                Command = command,
            };
            model.ExecutableCommandList.Add(executableCommand);
            return executableCommand;
        });

    public ApplicationDefaultCommand SetDefaultCommand(
        string? workspacePath,
        string applicationId,
        string executableCommandId) =>
        Mutate(workspacePath, model =>
        {
            var application = RequireById(model.ApplicationList, static item => item.Id, applicationId, "Application");
            if (model.ApplicationDefaultCommandList.Any(item => ReferenceEquals(item.Application, application)))
            {
                throw new InvalidOperationException($"Application '{application.Id}' already has a default command.");
            }

            var executableCommand = RequireById(model.ExecutableCommandList, static item => item.Id, executableCommandId, "ExecutableCommand");
            if (!ReferenceEquals(executableCommand.Command.Application, application))
            {
                throw new InvalidOperationException($"Executable command '{executableCommand.Id}' does not belong to application '{application.Id}'.");
            }

            var defaultCommand = new ApplicationDefaultCommand
            {
                Id = application.Id + ":default-command",
                Application = application,
                ExecutableCommand = executableCommand,
            };
            model.ApplicationDefaultCommandList.Add(defaultCommand);
            return defaultCommand;
        });

    public ValueArity AddValueArity(
        string? workspacePath,
        string id,
        string name,
        string minValueCount,
        string? maxValueCount,
        string? description) =>
        Mutate(workspacePath, model =>
        {
            RequireNewId(model.ValueArityList, static item => item.Id, id, "ValueArity");
            var arity = new ValueArity
            {
                Id = RequiredText(id, "id"),
                Name = RequiredText(name, "name"),
                MinValueCount = RequiredText(minValueCount, "min-value-count"),
                MaxValueCount = EmptyToNull(maxValueCount),
                Description = EmptyToNull(description),
            };
            model.ValueArityList.Add(arity);
            return arity;
        });

    public ValueShape AddValueShape(
        string? workspacePath,
        string id,
        string name,
        string valueArityId,
        string? valueLabel,
        bool? allowsOptionLikeValue,
        string? description) =>
        Mutate(workspacePath, model =>
        {
            RequireNewId(model.ValueShapeList, static item => item.Id, id, "ValueShape");
            var arity = RequireById(model.ValueArityList, static item => item.Id, valueArityId, "ValueArity");
            var shape = new ValueShape
            {
                Id = RequiredText(id, "id"),
                Name = RequiredText(name, "name"),
                ValueArity = arity,
                ValueLabel = EmptyToNull(valueLabel),
                AllowsOptionLikeValue = BoolText(allowsOptionLikeValue),
                Description = EmptyToNull(description),
            };
            model.ValueShapeList.Add(shape);
            return shape;
        });

    public AllowedValue AddAllowedValue(
        string? workspacePath,
        string id,
        string valueShapeId,
        string value,
        string? description) =>
        Mutate(workspacePath, model =>
        {
            RequireNewId(model.AllowedValueList, static item => item.Id, id, "AllowedValue");
            var shape = RequireById(model.ValueShapeList, static item => item.Id, valueShapeId, "ValueShape");
            var allowedValue = new AllowedValue
            {
                Id = RequiredText(id, "id"),
                ValueShape = shape,
                Value = RequiredText(value, "value"),
                Description = EmptyToNull(description),
            };
            model.AllowedValueList.Add(allowedValue);
            return allowedValue;
        });

    public Option AddOption(
        string? workspacePath,
        string parameterId,
        string optionId,
        string executableCommandId,
        string name,
        string valueShapeId,
        string tokenId,
        string tokenValue,
        bool? required,
        bool? repeatable,
        string? defaultValue,
        string? description) =>
        Mutate(workspacePath, model =>
        {
            RequireNewId(model.ParameterList, static item => item.Id, parameterId, "Parameter");
            RequireNewId(model.OptionList, static item => item.Id, optionId, "Option");
            RequireNewId(model.OptionTokenList, static item => item.Id, tokenId, "OptionToken");
            var executable = RequireById(model.ExecutableCommandList, static item => item.Id, executableCommandId, "ExecutableCommand");
            var shape = RequireById(model.ValueShapeList, static item => item.Id, valueShapeId, "ValueShape");
            var parameter = new Parameter
            {
                Id = RequiredText(parameterId, "parameter-id"),
                ValueShape = shape,
                Name = RequiredText(name, "name"),
                IsRequired = BoolText(required),
                IsRepeatable = BoolText(repeatable),
                DefaultValue = EmptyToNull(defaultValue),
                Description = EmptyToNull(description),
            };
            var option = new Option
            {
                Id = RequiredText(optionId, "option-id"),
                Parameter = parameter,
            };
            var token = new OptionToken
            {
                Id = RequiredText(tokenId, "token-id"),
                Option = option,
                Token = RequiredText(tokenValue, "token"),
            };
            model.ParameterList.Add(parameter);
            model.ExecutableCommandParameterList.Add(new ExecutableCommandParameter
            {
                Id = executable.Id + ":parameter:" + parameter.Id,
                ExecutableCommand = executable,
                Parameter = parameter,
            });
            model.OptionList.Add(option);
            model.OptionTokenList.Add(token);
            return option;
        });

    public Option AddApplicationOption(
        string? workspacePath,
        string parameterId,
        string optionId,
        string applicationId,
        string name,
        string valueShapeId,
        string tokenId,
        string tokenValue,
        bool? required,
        bool? repeatable,
        string? defaultValue,
        string? description) =>
        Mutate(workspacePath, model =>
        {
            RequireNewId(model.ParameterList, static item => item.Id, parameterId, "Parameter");
            RequireNewId(model.OptionList, static item => item.Id, optionId, "Option");
            RequireNewId(model.OptionTokenList, static item => item.Id, tokenId, "OptionToken");
            var application = RequireById(model.ApplicationList, static item => item.Id, applicationId, "Application");
            var shape = RequireById(model.ValueShapeList, static item => item.Id, valueShapeId, "ValueShape");
            var parameter = new Parameter
            {
                Id = RequiredText(parameterId, "parameter-id"),
                ValueShape = shape,
                Name = RequiredText(name, "name"),
                IsRequired = BoolText(required),
                IsRepeatable = BoolText(repeatable),
                DefaultValue = EmptyToNull(defaultValue),
                Description = EmptyToNull(description),
            };
            var option = new Option
            {
                Id = RequiredText(optionId, "option-id"),
                Parameter = parameter,
            };
            var token = new OptionToken
            {
                Id = RequiredText(tokenId, "token-id"),
                Option = option,
                Token = RequiredText(tokenValue, "token"),
            };
            model.ParameterList.Add(parameter);
            model.ApplicationParameterList.Add(new ApplicationParameter
            {
                Id = application.Id + ":parameter:" + parameter.Id,
                Application = application,
                Parameter = parameter,
            });
            model.OptionList.Add(option);
            model.OptionTokenList.Add(token);
            return option;
        });

    public OptionToken AddOptionToken(
        string? workspacePath,
        string id,
        string optionId,
        string tokenValue,
        string previousTokenId) =>
        Mutate(workspacePath, model =>
        {
            RequireNewId(model.OptionTokenList, static item => item.Id, id, "OptionToken");
            var option = RequireById(model.OptionList, static item => item.Id, optionId, "Option");
            var previous = RequireById(model.OptionTokenList, static item => item.Id, previousTokenId, "OptionToken");
            if (previous is not null && !ReferenceEquals(previous.Option, option))
            {
                throw new InvalidOperationException($"Previous token '{previous.Id}' does not belong to option '{option.Id}'.");
            }

            var token = new OptionToken
            {
                Id = RequiredText(id, "id"),
                Option = option,
                Token = RequiredText(tokenValue, "token"),
                PreviousToken = previous,
            };
            model.OptionTokenList.Add(token);
            return token;
        });

    public PositionalArgument AddPositional(
        string? workspacePath,
        string parameterId,
        string positionalId,
        string executableCommandId,
        string name,
        string valueShapeId,
        string? previousArgumentId,
        bool? required,
        bool? repeatable,
        string? defaultValue,
        string? description) =>
        Mutate(workspacePath, model =>
        {
            RequireNewId(model.ParameterList, static item => item.Id, parameterId, "Parameter");
            RequireNewId(model.PositionalArgumentList, static item => item.Id, positionalId, "PositionalArgument");
            var executable = RequireById(model.ExecutableCommandList, static item => item.Id, executableCommandId, "ExecutableCommand");
            var shape = RequireById(model.ValueShapeList, static item => item.Id, valueShapeId, "ValueShape");
            var previous = string.IsNullOrWhiteSpace(previousArgumentId)
                ? null
                : RequireById(model.PositionalArgumentList, static item => item.Id, previousArgumentId, "PositionalArgument");
            if (previous is not null && !ParameterBelongsToExecutable(model, previous.Parameter, executable))
            {
                throw new InvalidOperationException($"Previous argument '{previous.Id}' does not belong to executable command '{executable.Id}'.");
            }

            var parameter = new Parameter
            {
                Id = RequiredText(parameterId, "parameter-id"),
                ValueShape = shape,
                Name = RequiredText(name, "name"),
                IsRequired = BoolText(required),
                IsRepeatable = BoolText(repeatable),
                DefaultValue = EmptyToNull(defaultValue),
                Description = EmptyToNull(description),
            };
            var positional = new PositionalArgument
            {
                Id = RequiredText(positionalId, "positional-id"),
                Parameter = parameter,
                PreviousArgument = previous,
            };
            model.ParameterList.Add(parameter);
            model.ExecutableCommandParameterList.Add(new ExecutableCommandParameter
            {
                Id = executable.Id + ":parameter:" + parameter.Id,
                ExecutableCommand = executable,
                Parameter = parameter,
            });
            model.PositionalArgumentList.Add(positional);
            return positional;
        });

    public ParameterGroup AddParameterGroup(
        string? workspacePath,
        string id,
        string executableCommandId,
        string name,
        string memberId,
        string parameterId,
        bool? required,
        bool? allowsMultiple,
        string? description) =>
        Mutate(workspacePath, model =>
        {
            RequireNewId(model.ParameterGroupList, static item => item.Id, id, "ParameterGroup");
            RequireNewId(model.ParameterGroupMemberList, static item => item.Id, memberId, "ParameterGroupMember");
            var executable = RequireById(model.ExecutableCommandList, static item => item.Id, executableCommandId, "ExecutableCommand");
            var parameter = RequireById(model.ParameterList, static item => item.Id, parameterId, "Parameter");
            if (!ParameterBelongsToExecutable(model, parameter, executable))
            {
                throw new InvalidOperationException($"Parameter '{parameter.Id}' does not belong to executable command '{executable.Id}'.");
            }

            var group = new ParameterGroup
            {
                Id = RequiredText(id, "id"),
                ExecutableCommand = executable,
                Name = RequiredText(name, "name"),
                IsRequired = BoolText(required),
                AllowsMultiple = BoolText(allowsMultiple),
                Description = EmptyToNull(description),
            };
            var member = new ParameterGroupMember
            {
                Id = RequiredText(memberId, "member-id"),
                ParameterGroup = group,
                Parameter = parameter,
            };
            model.ParameterGroupList.Add(group);
            model.ParameterGroupMemberList.Add(member);
            return group;
        });

    public ParameterGroupMember AddParameterGroupMember(
        string? workspacePath,
        string id,
        string parameterGroupId,
        string parameterId,
        string previousMemberId) =>
        Mutate(workspacePath, model =>
        {
            RequireNewId(model.ParameterGroupMemberList, static item => item.Id, id, "ParameterGroupMember");
            var group = RequireById(model.ParameterGroupList, static item => item.Id, parameterGroupId, "ParameterGroup");
            var parameter = RequireById(model.ParameterList, static item => item.Id, parameterId, "Parameter");
            if (!ParameterBelongsToExecutable(model, parameter, group.ExecutableCommand))
            {
                throw new InvalidOperationException($"Parameter '{parameter.Id}' does not belong to executable command '{group.ExecutableCommand.Id}'.");
            }

            var previous = RequireById(model.ParameterGroupMemberList, static item => item.Id, previousMemberId, "ParameterGroupMember");
            if (previous is not null && !ReferenceEquals(previous.ParameterGroup, group))
            {
                throw new InvalidOperationException($"Previous member '{previous.Id}' does not belong to parameter group '{group.Id}'.");
            }

            var member = new ParameterGroupMember
            {
                Id = RequiredText(id, "id"),
                ParameterGroup = group,
                Parameter = parameter,
                PreviousMember = previous,
            };
            model.ParameterGroupMemberList.Add(member);
            return member;
        });

    public static bool ParseBool(string? value, bool defaultValue = false) =>
        string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);

    public static string BuildRoute(Command command)
    {
        var tokens = new Stack<string>();
        var visited = new HashSet<Command>(ReferenceComparer<Command>.Instance);
        var current = command;
        while (current is not null && visited.Add(current))
        {
            if (!string.IsNullOrWhiteSpace(current.Token))
            {
                tokens.Push(current.Token);
            }

            current = current.ParentCommand;
        }

        return string.Join(" ", tokens);
    }

    private T Mutate<T>(string? workspacePath, Func<MetaCliModel, T> mutate)
    {
        var fullPath = ResolveWorkspacePath(workspacePath);
        var model = Load(fullPath);
        var result = mutate(model);
        var issues = Validate(model, includeCompleteness: true);
        if (issues.Any(static issue => issue.Severity == MetaCliIssueSeverity.Error))
        {
            throw new InvalidOperationException(RenderValidationFailure(issues));
        }

        model.SaveToXmlWorkspace(fullPath);
        return result;
    }

    private static MetaCliApplicationSummary BuildApplicationSummary(
        MetaCliModel model,
        Application application)
    {
        var commands = model.CommandList
            .Where(command => ReferenceEquals(command.Application, application))
            .OrderBy(BuildRoute, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static command => command.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static command => command.Id, StringComparer.Ordinal)
            .Select(command => BuildCommandSummary(model, application, command))
            .ToArray();

        return new MetaCliApplicationSummary(
            application.Id,
            application.Name,
            application.ExecutableName ?? string.Empty,
            application.Version ?? string.Empty,
            application.Description ?? string.Empty,
            commands.Length,
            commands.Count(static command => command.IsExecutable),
            model.ApplicationParameterList.Count(parameter => ReferenceEquals(parameter.Application, application)),
            EffectiveParametersForApplication(model, application).Count,
            model.OptionList.Count(option => ParameterBelongsToApplication(model, option.Parameter, application)),
            model.PositionalArgumentList.Count(argument => ParameterBelongsToApplication(model, argument.Parameter, application)),
            model.ParameterGroupList.Count(group => ReferenceEquals(group.ExecutableCommand.Command.Application, application)),
            commands);
    }

    private static MetaCliCommandSummary BuildCommandSummary(
        MetaCliModel model,
        Application application,
        Command command)
    {
        var executableCommand = model.ExecutableCommandList.FirstOrDefault(item => ReferenceEquals(item.Command, command));
        var parameters = executableCommand is null
            ? Array.Empty<Parameter>()
            : EffectiveParametersForExecutable(model, executableCommand).ToArray();
        var optionCount = model.OptionList.Count(option => parameters.Contains(option.Parameter, ReferenceComparer<Parameter>.Instance));
        var positionalCount = model.PositionalArgumentList.Count(argument => parameters.Contains(argument.Parameter, ReferenceComparer<Parameter>.Instance));
        var isDefault = executableCommand is not null &&
            model.ApplicationDefaultCommandList.Any(item =>
                ReferenceEquals(item.Application, application) &&
                ReferenceEquals(item.ExecutableCommand, executableCommand));
        return new MetaCliCommandSummary(
            command.Id,
            command.Name,
            BuildRoute(command),
            command.Description ?? string.Empty,
            executableCommand is not null,
            isDefault,
            parameters.Length,
            optionCount,
            positionalCount);
    }

    private static IReadOnlyList<MetaCliIssue> Validate(
        MetaCliModel model,
        bool includeCompleteness)
    {
        var issues = new List<MetaCliIssue>();
        ValidateIds(model, issues);
        ValidateApplications(model, issues);
        ValidateCommands(model, issues, includeCompleteness);
        ValidateExecutableCommands(model, issues);
        ValidateDefaultCommands(model, issues);
        ValidateValueArities(model, issues);
        ValidateValueShapes(model, issues);
        ValidateParameters(model, issues, includeCompleteness);
        ValidateOptions(model, issues, includeCompleteness);
        ValidatePositionals(model, issues, includeCompleteness);
        ValidateParameterGroups(model, issues, includeCompleteness);
        ValidateAllowedValues(model, issues, includeCompleteness);
        return issues;
    }

    private static void ValidateIds(MetaCliModel model, List<MetaCliIssue> issues)
    {
        ValidateEntityIds(model.ApplicationList, static item => item.Id, "Application", issues);
        ValidateEntityIds(model.CommandList, static item => item.Id, "Command", issues);
        ValidateEntityIds(model.ExecutableCommandList, static item => item.Id, "ExecutableCommand", issues);
        ValidateEntityIds(model.ApplicationDefaultCommandList, static item => item.Id, "ApplicationDefaultCommand", issues);
        ValidateEntityIds(model.ValueArityList, static item => item.Id, "ValueArity", issues);
        ValidateEntityIds(model.ValueShapeList, static item => item.Id, "ValueShape", issues);
        ValidateEntityIds(model.AllowedValueList, static item => item.Id, "AllowedValue", issues);
        ValidateEntityIds(model.ParameterList, static item => item.Id, "Parameter", issues);
        ValidateEntityIds(model.ApplicationParameterList, static item => item.Id, "ApplicationParameter", issues);
        ValidateEntityIds(model.ExecutableCommandParameterList, static item => item.Id, "ExecutableCommandParameter", issues);
        ValidateEntityIds(model.OptionList, static item => item.Id, "Option", issues);
        ValidateEntityIds(model.OptionTokenList, static item => item.Id, "OptionToken", issues);
        ValidateEntityIds(model.PositionalArgumentList, static item => item.Id, "PositionalArgument", issues);
        ValidateEntityIds(model.ParameterGroupList, static item => item.Id, "ParameterGroup", issues);
        ValidateEntityIds(model.ParameterGroupMemberList, static item => item.Id, "ParameterGroupMember", issues);
    }

    private static void ValidateApplications(
        MetaCliModel model,
        List<MetaCliIssue> issues)
    {
        foreach (var duplicate in model.ApplicationList
                     .Where(static application => !string.IsNullOrWhiteSpace(application.Name))
                     .GroupBy(static application => application.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1)
                     .Select(static group => group.Key)
                     .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
        {
            AddError(issues, "MCLI002", $"Application name '{duplicate}' is duplicated.", "Application.Name");
        }
    }

    private static void ValidateCommands(
        MetaCliModel model,
        List<MetaCliIssue> issues,
        bool includeCompleteness)
    {
        foreach (var application in model.ApplicationList)
        {
            foreach (var duplicate in model.CommandList
                         .Where(command => ReferenceEquals(command.Application, application))
                         .Where(static command => !string.IsNullOrWhiteSpace(command.Name))
                         .GroupBy(static command => command.Name, StringComparer.OrdinalIgnoreCase)
                         .Where(static group => group.Count() > 1)
                         .Select(static group => group.Key)
                         .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
            {
                AddError(issues, "MCLI010", $"Command name '{duplicate}' is duplicated in application '{application.Name}'.", "Command.Name");
            }

            foreach (var siblingGroup in model.CommandList
                         .Where(command => ReferenceEquals(command.Application, application))
                         .Where(static command => !string.IsNullOrWhiteSpace(command.Token))
                         .GroupBy(command => command.ParentCommand, ReferenceComparer<Command?>.Instance))
            {
                foreach (var duplicateToken in siblingGroup
                             .GroupBy(static command => command.Token!, StringComparer.OrdinalIgnoreCase)
                             .Where(static group => group.Count() > 1)
                             .Select(static group => group.Key)
                             .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
                {
                    AddError(issues, "MCLI011", $"Command token '{duplicateToken}' is duplicated among siblings in application '{application.Name}'.", "Command.Token");
                }
            }
        }

        foreach (var command in model.CommandList)
        {
            if (command.Application is null)
            {
                AddError(issues, "MCLI012", $"Command '{command.Id}' is not attached to an application.", $"Command:{command.Id}");
            }

            if (command.ParentCommand is not null && !ReferenceEquals(command.ParentCommand.Application, command.Application))
            {
                AddError(issues, "MCLI013", $"Command '{command.Name}' has a parent from another application.", $"Command:{command.Id}");
            }

            if (HasCommandParentCycle(command))
            {
                AddError(issues, "MCLI014", $"Command '{command.Name}' has a parent-command cycle.", $"Command:{command.Id}");
            }

            if (includeCompleteness &&
                string.IsNullOrWhiteSpace(command.Token))
            {
                AddError(issues, "MCLI015", $"Command '{command.Name}' must declare a token.", $"Command:{command.Id}");
            }
        }
    }

    private static void ValidateExecutableCommands(
        MetaCliModel model,
        List<MetaCliIssue> issues)
    {
        foreach (var duplicate in model.ExecutableCommandList
                     .Where(static item => item.Command is not null)
                     .GroupBy(static item => item.Command, ReferenceComparer<Command>.Instance)
                     .Where(static group => group.Count() > 1)
                     .Select(static group => group.Key))
        {
            AddError(issues, "MCLI020", $"Command '{duplicate.Name}' has multiple executable command rows.", $"Command:{duplicate.Id}");
        }
    }

    private static void ValidateDefaultCommands(
        MetaCliModel model,
        List<MetaCliIssue> issues)
    {
        foreach (var application in model.ApplicationList)
        {
            var defaults = model.ApplicationDefaultCommandList
                .Where(item => ReferenceEquals(item.Application, application))
                .ToArray();
            if (defaults.Length > 1)
            {
                AddError(issues, "MCLI030", $"Application '{application.Name}' has multiple default commands.", $"Application:{application.Id}");
            }

            foreach (var defaultCommand in defaults)
            {
                var command = defaultCommand.ExecutableCommand?.Command;
                if (command is null)
                {
                    AddError(issues, "MCLI031", $"Application '{application.Name}' has a default command without a command target.", $"ApplicationDefaultCommand:{defaultCommand.Id}");
                    continue;
                }

                if (!ReferenceEquals(command.Application, application))
                {
                    AddError(issues, "MCLI032", $"Application '{application.Name}' default command belongs to another application.", $"ApplicationDefaultCommand:{defaultCommand.Id}");
                }
            }
        }
    }

    private static void ValidateValueArities(
        MetaCliModel model,
        List<MetaCliIssue> issues)
    {
        foreach (var duplicate in model.ValueArityList
                     .Where(static arity => !string.IsNullOrWhiteSpace(arity.Name))
                     .GroupBy(static arity => arity.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1)
                     .Select(static group => group.Key)
                     .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
        {
            AddError(issues, "MCLI039", $"Value arity name '{duplicate}' is duplicated.", "ValueArity.Name");
        }

        foreach (var arity in model.ValueArityList)
        {
            if (!TryParseNonNegativeCardinality(arity.MinValueCount, out var min))
            {
                AddError(issues, "MCLI040", $"Value arity '{arity.Name}' has invalid MinValueCount '{arity.MinValueCount}'.", $"ValueArity:{arity.Id}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(arity.MaxValueCount))
            {
                continue;
            }

            if (!TryParseNonNegativeCardinality(arity.MaxValueCount, out var max))
            {
                AddError(issues, "MCLI041", $"Value arity '{arity.Name}' has invalid MaxValueCount '{arity.MaxValueCount}'.", $"ValueArity:{arity.Id}");
                continue;
            }

            if (max < min)
            {
                AddError(issues, "MCLI042", $"Value arity '{arity.Name}' has MaxValueCount below MinValueCount.", $"ValueArity:{arity.Id}");
            }
        }
    }

    private static void ValidateValueShapes(
        MetaCliModel model,
        List<MetaCliIssue> issues)
    {
        foreach (var duplicate in model.ValueShapeList
                     .Where(static shape => !string.IsNullOrWhiteSpace(shape.Name))
                     .GroupBy(static shape => shape.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1)
                     .Select(static group => group.Key)
                     .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
        {
            AddError(issues, "MCLI045", $"Value shape name '{duplicate}' is duplicated.", "ValueShape.Name");
        }

        foreach (var shape in model.ValueShapeList)
        {
            if (shape.ValueArity is null)
            {
                AddError(issues, "MCLI046", $"Value shape '{shape.Name}' has no value arity.", $"ValueShape:{shape.Id}");
            }
        }
    }

    private static void ValidateParameters(
        MetaCliModel model,
        List<MetaCliIssue> issues,
        bool includeCompleteness)
    {
        foreach (var application in model.ApplicationList)
        {
            foreach (var duplicate in model.ApplicationParameterList
                         .Where(item => ReferenceEquals(item.Application, application))
                         .Select(static item => item.Parameter)
                         .Where(static parameter => !string.IsNullOrWhiteSpace(parameter.Name))
                         .GroupBy(static parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
                         .Where(static group => group.Count() > 1)
                         .Select(static group => group.Key)
                         .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
            {
                AddError(issues, "MCLI049", $"Application parameter '{duplicate}' is duplicated in application '{application.Name}'.", $"Application:{application.Id}");
            }
        }

        foreach (var executableCommand in model.ExecutableCommandList)
        {
            foreach (var duplicate in model.ParameterList
                         .Where(parameter => EffectiveParametersForExecutable(model, executableCommand).Contains(parameter, ReferenceComparer<Parameter>.Instance))
                         .Where(static parameter => !string.IsNullOrWhiteSpace(parameter.Name))
                         .GroupBy(static parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
                         .Where(static group => group.Count() > 1)
                         .Select(static group => group.Key)
                         .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
            {
                AddError(issues, "MCLI050", $"Parameter '{duplicate}' is duplicated on command '{executableCommand.Command.Name}'.", $"ExecutableCommand:{executableCommand.Id}");
            }
        }

        foreach (var parameter in model.ParameterList)
        {
            var applicationScopeCount = model.ApplicationParameterList.Count(item => ReferenceEquals(item.Parameter, parameter));
            var commandScopeCount = model.ExecutableCommandParameterList.Count(item => ReferenceEquals(item.Parameter, parameter));
            if (applicationScopeCount + commandScopeCount == 0)
            {
                AddError(issues, "MCLI051", $"Parameter '{parameter.Name}' has no scope.", $"Parameter:{parameter.Id}");
            }

            if (applicationScopeCount + commandScopeCount > 1)
            {
                AddError(issues, "MCLI057", $"Parameter '{parameter.Name}' has more than one scope.", $"Parameter:{parameter.Id}");
            }

            if (parameter.ValueShape is null)
            {
                AddError(issues, "MCLI052", $"Parameter '{parameter.Name}' has no value shape.", $"Parameter:{parameter.Id}");
            }

            if (!includeCompleteness)
            {
                continue;
            }

            var optionCount = model.OptionList.Count(option => ReferenceEquals(option.Parameter, parameter));
            var positionalCount = model.PositionalArgumentList.Count(argument => ReferenceEquals(argument.Parameter, parameter));
            if (optionCount == 0 && positionalCount == 0)
            {
                AddError(issues, "MCLI053", $"Parameter '{parameter.Name}' is neither an option nor a positional argument.", $"Parameter:{parameter.Id}");
            }

            if (optionCount > 0 && positionalCount > 0)
            {
                AddError(issues, "MCLI054", $"Parameter '{parameter.Name}' is both an option and a positional argument.", $"Parameter:{parameter.Id}");
            }

            if (optionCount > 1)
            {
                AddError(issues, "MCLI055", $"Parameter '{parameter.Name}' has multiple option rows.", $"Parameter:{parameter.Id}");
            }

            if (positionalCount > 1)
            {
                AddError(issues, "MCLI056", $"Parameter '{parameter.Name}' has multiple positional argument rows.", $"Parameter:{parameter.Id}");
            }
        }

        foreach (var scoped in model.ApplicationParameterList)
        {
            if (!ContainsReference(model.ApplicationList, scoped.Application))
            {
                AddError(issues, "MCLI058", $"Application parameter '{scoped.Id}' is not attached to an application.", $"ApplicationParameter:{scoped.Id}");
            }

            if (!ContainsReference(model.ParameterList, scoped.Parameter))
            {
                AddError(issues, "MCLI058", $"Application parameter '{scoped.Id}' references a parameter outside the workspace.", $"ApplicationParameter:{scoped.Id}");
            }
        }

        foreach (var scoped in model.ExecutableCommandParameterList)
        {
            if (!ContainsReference(model.ExecutableCommandList, scoped.ExecutableCommand))
            {
                AddError(issues, "MCLI059", $"Executable command parameter '{scoped.Id}' is not attached to an executable command.", $"ExecutableCommandParameter:{scoped.Id}");
            }

            if (!ContainsReference(model.ParameterList, scoped.Parameter))
            {
                AddError(issues, "MCLI059", $"Executable command parameter '{scoped.Id}' references a parameter outside the workspace.", $"ExecutableCommandParameter:{scoped.Id}");
            }
        }
    }

    private static void ValidateOptions(
        MetaCliModel model,
        List<MetaCliIssue> issues,
        bool includeCompleteness)
    {
        foreach (var option in model.OptionList)
        {
            var tokens = model.OptionTokenList
                .Where(token => ReferenceEquals(token.Option, option))
                .ToArray();
            if (includeCompleteness && tokens.Length == 0)
            {
                AddError(issues, "MCLI060", $"Option parameter '{option.Parameter?.Name ?? option.Id}' has no option token.", $"Option:{option.Id}");
            }

            var headCount = tokens.Count(static token => token.PreviousToken is null);
            if (includeCompleteness && headCount != 1)
            {
                AddError(issues, "MCLI061", $"Option parameter '{option.Parameter?.Name ?? option.Id}' must have exactly one token-chain head.", $"Option:{option.Id}");
            }

            foreach (var token in tokens)
            {
                if (!token.Token.StartsWith("-", StringComparison.Ordinal))
                {
                    AddError(issues, "MCLI062", $"Option token '{token.Token}' must start with '-'.", $"OptionToken:{token.Id}");
                }

                if (token.PreviousToken is not null && !ReferenceEquals(token.PreviousToken.Option, option))
                {
                    AddError(issues, "MCLI063", $"Option token '{token.Token}' points to a previous token from another option.", $"OptionToken:{token.Id}");
                }
            }

            ValidatePreviousChain(
                tokens,
                static token => token.PreviousToken,
                token => $"OptionToken:{token.Id}",
                token => token.Token,
                "MCLI064",
                "option token",
                includeCompleteness,
                issues);
        }

        foreach (var executableCommand in model.ExecutableCommandList)
        {
            var commandOptions = model.OptionList
                .Where(option => EffectiveParametersForExecutable(model, executableCommand).Contains(option.Parameter, ReferenceComparer<Parameter>.Instance))
                .ToArray();
            foreach (var duplicate in model.OptionTokenList
                         .Where(token => commandOptions.Contains(token.Option, ReferenceComparer<Option>.Instance))
                         .GroupBy(static token => token.Token, StringComparer.OrdinalIgnoreCase)
                         .Where(static group => group.Count() > 1)
                         .Select(static group => group.Key)
                         .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
            {
                AddError(issues, "MCLI065", $"Option token '{duplicate}' is duplicated on command '{executableCommand.Command.Name}'.", $"Command:{executableCommand.Command.Id}");
            }
        }
    }

    private static void ValidatePositionals(
        MetaCliModel model,
        List<MetaCliIssue> issues,
        bool includeCompleteness)
    {
        foreach (var executableCommand in model.ExecutableCommandList)
        {
            var arguments = model.PositionalArgumentList
                .Where(argument => ParameterBelongsToExecutable(model, argument.Parameter, executableCommand))
                .ToArray();
            foreach (var argument in arguments)
            {
                if (argument.PreviousArgument is not null &&
                    !ParameterBelongsToExecutable(model, argument.PreviousArgument.Parameter, executableCommand))
                {
                    AddError(issues, "MCLI070", $"Positional argument '{argument.Parameter.Name}' points to a previous argument from another command.", $"PositionalArgument:{argument.Id}");
                }
            }

            ValidatePreviousChain(
                arguments,
                static argument => argument.PreviousArgument,
                argument => $"PositionalArgument:{argument.Id}",
                argument => argument.Parameter.Name,
                "MCLI071",
                "positional argument",
                includeCompleteness,
                issues);

            if (!includeCompleteness || !TryOrderByPrevious(arguments, static argument => argument.PreviousArgument, out var ordered))
            {
                continue;
            }

            var seenOptional = false;
            for (var index = 0; index < ordered.Count; index++)
            {
                var argument = ordered[index];
                var required = ParseBool(argument.Parameter.IsRequired);
                var repeatable = ParseBool(argument.Parameter.IsRepeatable);
                if (!required)
                {
                    seenOptional = true;
                }
                else if (seenOptional)
                {
                    AddError(issues, "MCLI072", $"Required positional '{argument.Parameter.Name}' follows an optional positional.", $"PositionalArgument:{argument.Id}");
                }

                if (repeatable && index != ordered.Count - 1)
                {
                    AddError(issues, "MCLI073", $"Repeatable positional '{argument.Parameter.Name}' must be last.", $"PositionalArgument:{argument.Id}");
                }
            }
        }
    }

    private static void ValidateParameterGroups(
        MetaCliModel model,
        List<MetaCliIssue> issues,
        bool includeCompleteness)
    {
        foreach (var group in model.ParameterGroupList)
        {
            var members = model.ParameterGroupMemberList
                .Where(member => ReferenceEquals(member.ParameterGroup, group))
                .ToArray();
            foreach (var member in members)
            {
                if (!ParameterBelongsToExecutable(model, member.Parameter, group.ExecutableCommand))
                {
                    AddError(issues, "MCLI080", $"Parameter group '{group.Name}' includes a parameter from another command.", $"ParameterGroupMember:{member.Id}");
                }

                if (member.PreviousMember is not null && !ReferenceEquals(member.PreviousMember.ParameterGroup, group))
                {
                    AddError(issues, "MCLI081", $"Parameter group member '{member.Id}' points to a previous member from another group.", $"ParameterGroupMember:{member.Id}");
                }
            }

            ValidatePreviousChain(
                members,
                static member => member.PreviousMember,
                member => $"ParameterGroupMember:{member.Id}",
                member => member.Parameter.Name,
                "MCLI082",
                "parameter group member",
                includeCompleteness,
                issues);

            if (includeCompleteness && ParseBool(group.IsRequired) && members.Length == 0)
            {
                AddError(issues, "MCLI085", $"Required parameter group '{group.Name}' must have at least one member.", $"ParameterGroup:{group.Id}");
            }
        }

        if (!includeCompleteness)
        {
            return;
        }

        foreach (var duplicate in model.ParameterGroupMemberList
                     .GroupBy(static member => member.Parameter, ReferenceComparer<Parameter>.Instance)
                     .Where(static group => group.Count() > 1)
                     .Select(static group => group.Key))
        {
            AddError(issues, "MCLI083", $"Parameter '{duplicate.Name}' belongs to more than one parameter group.", $"Parameter:{duplicate.Id}");
        }

        foreach (var member in model.ParameterGroupMemberList)
        {
            if (ParseBool(member.Parameter.IsRequired))
            {
                AddError(issues, "MCLI084", $"Parameter '{member.Parameter.Name}' is required inside parameter group '{member.ParameterGroup.Name}'.", $"ParameterGroupMember:{member.Id}");
            }
        }
    }

    private static void ValidateAllowedValues(
        MetaCliModel model,
        List<MetaCliIssue> issues,
        bool includeCompleteness)
    {
        foreach (var shape in model.ValueShapeList)
        {
            var values = model.AllowedValueList
                .Where(value => ReferenceEquals(value.ValueShape, shape))
                .ToArray();
            foreach (var duplicate in values
                         .GroupBy(static value => value.Value, StringComparer.OrdinalIgnoreCase)
                         .Where(static group => group.Count() > 1)
                         .Select(static group => group.Key)
                         .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
            {
                AddError(issues, "MCLI089", $"Allowed value '{duplicate}' is duplicated within value shape '{shape.Name}'.", $"ValueShape:{shape.Id}");
            }

        }
    }

    private static void ValidatePreviousChain<T>(
        IReadOnlyList<T> items,
        Func<T, T?> previous,
        Func<T, string> location,
        Func<T, string> label,
        string code,
        string noun,
        bool includeCompleteness,
        List<MetaCliIssue> issues)
        where T : class
    {
        var itemSet = items.ToHashSet(ReferenceComparer<T>.Instance);
        foreach (var item in items)
        {
            var previousItem = previous(item);
            if (previousItem is not null && !itemSet.Contains(previousItem))
            {
                AddError(issues, code, $"The {noun} '{label(item)}' points outside its ordered collection.", location(item));
            }

            var visited = new HashSet<T>(ReferenceComparer<T>.Instance);
            var current = item;
            while (current is not null)
            {
                if (!visited.Add(current))
                {
                    AddError(issues, code, $"The {noun} chain containing '{label(item)}' has a cycle.", location(item));
                    break;
                }

                current = previous(current);
            }
        }

        if (!includeCompleteness || items.Count == 0)
        {
            return;
        }

        var heads = items.Where(item => previous(item) is null).ToArray();
        if (heads.Length != 1)
        {
            AddError(issues, code, $"The {noun} chain must have exactly one head.", heads.Length == 0 ? noun : location(heads[0]));
        }

        foreach (var fork in items
                     .GroupBy(item => previous(item), ReferenceComparer<T?>.Instance)
                     .Where(static group => group.Key is not null && group.Count() > 1)
                     .Select(static group => group.Key!))
        {
            AddError(issues, code, $"The {noun} chain forks after '{label(fork)}'.", location(fork));
        }

        if (heads.Length == 1 && TryOrderByPrevious(items, previous, out var ordered) && ordered.Count != items.Count)
        {
            AddError(issues, code, $"The {noun} chain is not fully reachable from its head.", location(heads[0]));
        }
    }

    private static bool TryOrderByPrevious<T>(
        IReadOnlyList<T> items,
        Func<T, T?> previous,
        out IReadOnlyList<T> ordered)
        where T : class
    {
        ordered = Array.Empty<T>();
        if (items.Count == 0)
        {
            ordered = Array.Empty<T>();
            return true;
        }

        var heads = items.Where(item => previous(item) is null).ToArray();
        if (heads.Length != 1)
        {
            return false;
        }

        var orderedList = new List<T>();
        var current = heads[0];
        var visited = new HashSet<T>(ReferenceComparer<T>.Instance);
        while (current is not null && visited.Add(current))
        {
            orderedList.Add(current);
            var next = items.Where(item => ReferenceEquals(previous(item), current)).Take(2).ToArray();
            if (next.Length > 1)
            {
                return false;
            }

            current = next.Length == 0 ? null : next[0];
        }

        ordered = orderedList;
        return true;
    }

    private static IReadOnlyList<Parameter> EffectiveParametersForApplication(
        MetaCliModel model,
        Application application)
    {
        var parameters = new List<Parameter>();
        parameters.AddRange(model.ApplicationParameterList
            .Where(item => ReferenceEquals(item.Application, application))
            .Select(static item => item.Parameter));
        parameters.AddRange(model.ExecutableCommandList
            .Where(item => ReferenceEquals(item.Command.Application, application))
            .SelectMany(item => CommandParametersForExecutable(model, item)));
        return parameters
            .Distinct(ReferenceComparer<Parameter>.Instance)
            .ToArray();
    }

    private static IReadOnlyList<Parameter> EffectiveParametersForExecutable(
        MetaCliModel model,
        ExecutableCommand executableCommand)
    {
        var parameters = new List<Parameter>();
        parameters.AddRange(model.ApplicationParameterList
            .Where(item => ReferenceEquals(item.Application, executableCommand.Command.Application))
            .Select(static item => item.Parameter));
        parameters.AddRange(CommandParametersForExecutable(model, executableCommand));
        return parameters
            .Distinct(ReferenceComparer<Parameter>.Instance)
            .ToArray();
    }

    private static IEnumerable<Parameter> CommandParametersForExecutable(
        MetaCliModel model,
        ExecutableCommand executableCommand) =>
        model.ExecutableCommandParameterList
            .Where(item => ReferenceEquals(item.ExecutableCommand, executableCommand))
            .Select(static item => item.Parameter);

    private static bool ParameterBelongsToApplication(
        MetaCliModel model,
        Parameter parameter,
        Application application) =>
        model.ApplicationParameterList.Any(item =>
            ReferenceEquals(item.Application, application) &&
            ReferenceEquals(item.Parameter, parameter)) ||
        model.ExecutableCommandParameterList.Any(item =>
            ReferenceEquals(item.Parameter, parameter) &&
            ReferenceEquals(item.ExecutableCommand.Command.Application, application));

    private static bool ParameterBelongsToExecutable(
        MetaCliModel model,
        Parameter parameter,
        ExecutableCommand executableCommand) =>
        model.ExecutableCommandParameterList.Any(item =>
            ReferenceEquals(item.ExecutableCommand, executableCommand) &&
            ReferenceEquals(item.Parameter, parameter));

    private static bool HasCommandParentCycle(Command command)
    {
        var visited = new HashSet<Command>(ReferenceComparer<Command>.Instance);
        var current = command;
        while (current is not null)
        {
            if (!visited.Add(current))
            {
                return true;
            }

            current = current.ParentCommand;
        }

        return false;
    }

    private static bool TryParseNonNegativeCardinality(string? value, out int result)
    {
        result = 0;
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > 1 && normalized.StartsWith("0", StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= 0;
    }

    private static void ValidateUniqueName<T>(
        IEnumerable<T> rows,
        Func<T, string?> name,
        string entityName,
        string code,
        List<MetaCliIssue> issues)
    {
        foreach (var duplicate in rows
                     .Where(row => !string.IsNullOrWhiteSpace(name(row)))
                     .GroupBy(row => name(row)!, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1)
                     .Select(static group => group.Key)
                     .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
        {
            AddError(issues, code, $"{entityName} name '{duplicate}' is duplicated.", $"{entityName}.Name");
        }
    }

    private static void ValidateOptionalBooleanText(
        string? value,
        string code,
        string location,
        string propertyName,
        List<MetaCliIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!bool.TryParse(value.Trim(), out _))
        {
            AddError(issues, code, $"{propertyName} must be true or false.", location);
        }
    }

    private static bool ContainsReference<T>(IEnumerable<T> rows, T? row)
        where T : class =>
        row is not null && rows.Contains(row, ReferenceComparer<T>.Instance);

    private static void ValidateEntityIds<T>(
        IEnumerable<T> rows,
        Func<T, string> id,
        string entityName,
        List<MetaCliIssue> issues)
    {
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(id(row)))
            {
                AddError(issues, "MCLI-ID-001", $"Entity '{entityName}' contains a row with empty Id.", entityName);
            }
        }

        foreach (var duplicate in rows
                     .Where(row => !string.IsNullOrWhiteSpace(id(row)))
                     .GroupBy(id, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1)
                     .Select(static group => group.Key)
                     .OrderBy(static item => item, StringComparer.Ordinal))
        {
            AddError(issues, "MCLI-ID-002", $"Entity '{entityName}' contains duplicate Id '{duplicate}'.", $"{entityName}:{duplicate}");
        }
    }

    private static T RequireById<T>(
        IEnumerable<T> rows,
        Func<T, string> id,
        string idValue,
        string entityName)
        where T : class
    {
        var normalized = RequiredText(idValue, entityName + " id");
        return rows.FirstOrDefault(row => string.Equals(id(row), normalized, StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"{entityName} '{normalized}' does not exist.");
    }

    private static void RequireNewId<T>(
        IEnumerable<T> rows,
        Func<T, string> id,
        string idValue,
        string entityName)
    {
        var normalized = RequiredText(idValue, "id");
        if (rows.Any(row => string.Equals(id(row), normalized, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"{entityName} '{normalized}' already exists.");
        }
    }

    private static string RenderValidationFailure(IReadOnlyList<MetaCliIssue> issues)
    {
        var first = issues.First(issue => issue.Severity == MetaCliIssueSeverity.Error);
        return $"Resulting MetaCli workspace is invalid: {first.Code}: {first.Message}";
    }

    private static void AddError(
        List<MetaCliIssue> issues,
        string code,
        string message,
        string location) =>
        issues.Add(new MetaCliIssue(MetaCliIssueSeverity.Error, code, message, location));

    private static string ResolveWorkspacePath(string? workspacePath) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(workspacePath) ? Environment.CurrentDirectory : workspacePath);

    private static string RequiredText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"missing required value {name}.");
        }

        return value.Trim();
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? BoolText(bool? value) =>
        value.HasValue ? (value.Value ? "true" : "false") : null;

    private sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class?
    {
        public static readonly ReferenceComparer<T> Instance = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
