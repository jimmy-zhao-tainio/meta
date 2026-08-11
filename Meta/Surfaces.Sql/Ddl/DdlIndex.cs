using System.Collections.Generic;

namespace Meta.Surfaces.Sql.Ddl;

public sealed class DdlIndex
{
    public string Name { get; set; } = string.Empty;
    public bool IsUnique { get; set; }
    public bool IsClustered { get; set; }
    public List<DdlIndexColumn> KeyColumns { get; } = new();
    public List<string> IncludedColumnNames { get; } = new();
}
