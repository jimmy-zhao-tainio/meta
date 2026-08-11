using System.Collections.Generic;

namespace Meta.Surfaces.Sql.Ddl;

public sealed class DdlInsertStatement
{
    public string Schema { get; set; } = "dbo";
    public string TableName { get; set; } = string.Empty;
    public List<DdlInsertValue> Values { get; } = new();
}
