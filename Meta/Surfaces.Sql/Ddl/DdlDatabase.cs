using System.Collections.Generic;

namespace Meta.Surfaces.Sql.Ddl;

public sealed class DdlDatabase
{
    public List<DdlTable> Tables { get; } = new();
    public List<DdlInsertStatement> Inserts { get; } = new();
    public List<DdlUpdateStatement> Updates { get; } = new();
}
