namespace Meta.Core.Ddl;

public sealed class DdlCheckConstraint
{
    public string Name { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
}
