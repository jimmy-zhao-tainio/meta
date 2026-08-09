#nullable enable

using System.Collections.Generic;

namespace MetaCli
{
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
}
