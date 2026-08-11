using System.Collections.Generic;

namespace Meta.Surfaces.Sql.Ddl;

public sealed class DdlPrimaryKeyConstraint
{
    public string Name { get; set; } = string.Empty;
    public bool IsClustered { get; set; } = true;
    public List<string> ColumnNames { get; } = new();
}
