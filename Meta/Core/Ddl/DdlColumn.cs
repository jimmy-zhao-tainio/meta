namespace Meta.Core.Ddl;

public sealed class DdlColumn
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string? Collation { get; set; }
    public bool IsNullable { get; set; }
}
