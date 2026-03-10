using System.Collections.Generic;

namespace Meta.Core.Ddl;

public sealed class DdlUniqueConstraint
{
    public string Name { get; set; } = string.Empty;
    public List<string> ColumnNames { get; } = new();
}