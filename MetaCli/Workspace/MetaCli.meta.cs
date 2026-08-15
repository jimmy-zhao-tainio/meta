#nullable enable
using System;
using System.Collections.Generic;

namespace MetaCli;
public sealed partial class AllowedValue
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string Value { get; set; } = null !;
    public ValueShape ValueShape { get; set; } = null !;
}

public sealed partial class Application
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string? ExecutableName { get; set; }
    public string Name { get; set; } = null !;
    public string? Version { get; set; }
}

public sealed partial class ApplicationDefaultCommand
{
    public string Id { get; set; } = null !;
    public Application Application { get; set; } = null !;
    public ExecutableCommand ExecutableCommand { get; set; } = null !;
}

public sealed partial class ApplicationParameter
{
    public string Id { get; set; } = null !;
    public Application Application { get; set; } = null !;
    public Parameter Parameter { get; set; } = null !;
}

public sealed partial class Command
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string Name { get; set; } = null !;
    public string? Token { get; set; }
    public Application Application { get; set; } = null !;
    public Command? ParentCommand { get; set; }
}

public sealed partial class ExecutableCommand
{
    public string Id { get; set; } = null !;
    public Command Command { get; set; } = null !;
}

public sealed partial class ExecutableCommandParameter
{
    public string Id { get; set; } = null !;
    public ExecutableCommand ExecutableCommand { get; set; } = null !;
    public Parameter Parameter { get; set; } = null !;
}

public sealed partial class Option
{
    public string Id { get; set; } = null !;
    public Parameter Parameter { get; set; } = null !;
}

public sealed partial class OptionToken
{
    public string Id { get; set; } = null !;
    public string Token { get; set; } = null !;
    public Option Option { get; set; } = null !;
    public OptionToken? PreviousToken { get; set; }
}

public sealed partial class Parameter
{
    public string Id { get; set; } = null !;
    public string? DefaultValue { get; set; }
    public string? Description { get; set; }
    public string? IsRepeatable { get; set; }
    public string? IsRequired { get; set; }
    public string Name { get; set; } = null !;
    public ValueShape ValueShape { get; set; } = null !;
}

public sealed partial class ParameterGroup
{
    public string Id { get; set; } = null !;
    public string? AllowsMultiple { get; set; }
    public string? Description { get; set; }
    public string? IsRequired { get; set; }
    public string Name { get; set; } = null !;
    public ExecutableCommand ExecutableCommand { get; set; } = null !;
}

public sealed partial class ParameterGroupMember
{
    public string Id { get; set; } = null !;
    public ParameterGroup ParameterGroup { get; set; } = null !;
    public Parameter Parameter { get; set; } = null !;
    public ParameterGroupMember? PreviousMember { get; set; }
}

public sealed partial class PositionalArgument
{
    public string Id { get; set; } = null !;
    public Parameter Parameter { get; set; } = null !;
    public PositionalArgument? PreviousArgument { get; set; }
}

public sealed partial class ValueArity
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string? MaxValueCount { get; set; }
    public string MinValueCount { get; set; } = null !;
    public string Name { get; set; } = null !;
}

public sealed partial class ValueShape
{
    public string Id { get; set; } = null !;
    public string? AllowsOptionLikeValue { get; set; }
    public string? Description { get; set; }
    public string Name { get; set; } = null !;
    public string? ValueLabel { get; set; }
    public ValueArity ValueArity { get; set; } = null !;
}

public sealed partial class MetaCliModel
{
    public static MetaCliModel CreateEmpty() => new();
    public List<AllowedValue> AllowedValueList { get; set; } = new();
    public List<Application> ApplicationList { get; set; } = new();
    public List<ApplicationDefaultCommand> ApplicationDefaultCommandList { get; set; } = new();
    public List<ApplicationParameter> ApplicationParameterList { get; set; } = new();
    public List<Command> CommandList { get; set; } = new();
    public List<ExecutableCommand> ExecutableCommandList { get; set; } = new();
    public List<ExecutableCommandParameter> ExecutableCommandParameterList { get; set; } = new();
    public List<Option> OptionList { get; set; } = new();
    public List<OptionToken> OptionTokenList { get; set; } = new();
    public List<Parameter> ParameterList { get; set; } = new();
    public List<ParameterGroup> ParameterGroupList { get; set; } = new();
    public List<ParameterGroupMember> ParameterGroupMemberList { get; set; } = new();
    public List<PositionalArgument> PositionalArgumentList { get; set; } = new();
    public List<ValueArity> ValueArityList { get; set; } = new();
    public List<ValueShape> ValueShapeList { get; set; } = new();
}

public static partial class MetaCliInstance
{
    private static readonly MetaCliModel _builtIn = CreateBuiltIn();
    public static MetaCliModel BuiltIn => _builtIn;

    public static MetaCliModel CreateBuiltIn()
    {
        var model = MetaCliModel.CreateEmpty();
        return model;
    }
}