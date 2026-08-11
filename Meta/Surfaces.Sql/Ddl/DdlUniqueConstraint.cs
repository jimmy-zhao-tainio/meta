using System.Collections.Generic;

namespace Meta.Surfaces.Sql.Ddl;

public sealed class DdlUniqueConstraint
{
    public string Name { get; set; } = string.Empty;
    public List<string> ColumnNames { get; } = new();
}
