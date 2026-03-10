using System.Collections.Generic;

namespace Meta.Core.Ddl;

public sealed class DdlForeignKeyConstraint
{
    public string Name { get; set; } = string.Empty;
    public List<string> ColumnNames { get; } = new();
    public string ReferencedSchema { get; set; } = "dbo";
    public string ReferencedTableName { get; set; } = string.Empty;
    public List<string> ReferencedColumnNames { get; } = new();
}