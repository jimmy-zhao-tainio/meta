namespace Meta.Surfaces.Sql.Ddl;

public sealed class DdlIndexColumn
{
    public string Name { get; set; } = string.Empty;
    public bool IsDescending { get; set; }
}
