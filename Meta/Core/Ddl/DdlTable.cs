using System.Collections.Generic;

namespace Meta.Core.Ddl;

public sealed class DdlTable
{
    public string Schema { get; set; } = "dbo";
    public string Name { get; set; } = string.Empty;
    public List<DdlColumn> Columns { get; } = new();
    public DdlPrimaryKeyConstraint? PrimaryKey { get; set; }
    public List<DdlUniqueConstraint> UniqueConstraints { get; } = new();
    public List<DdlForeignKeyConstraint> ForeignKeys { get; } = new();
    public List<DdlIndex> Indexes { get; } = new();
}