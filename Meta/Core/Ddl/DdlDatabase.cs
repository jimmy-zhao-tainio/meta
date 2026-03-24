using System.Collections.Generic;

namespace Meta.Core.Ddl;

public sealed class DdlDatabase
{
    public List<DdlTable> Tables { get; } = new();
    public List<DdlInsertStatement> Inserts { get; } = new();
}