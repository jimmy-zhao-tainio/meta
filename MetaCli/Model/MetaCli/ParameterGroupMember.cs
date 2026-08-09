#nullable enable

namespace MetaCli
{
    public sealed class ParameterGroupMember
    {
        public string Id { get; set; } = string.Empty;

        public ParameterGroup ParameterGroup { get; set; } = null!;

        public Parameter Parameter { get; set; } = null!;

        public ParameterGroupMember? PreviousMember { get; set; }

    }
}
