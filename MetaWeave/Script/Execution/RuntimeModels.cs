namespace MetaWeaveScript.Execution;

internal sealed class MetaWeaveScriptExecutionFault : Exception
{
    public MetaWeaveScriptExecutionFault(
        string code,
        string message,
        string? syntaxId = null,
        string? relationName = null)
        : base(message)
    {
        Code = code;
        SyntaxId = syntaxId;
        RelationName = relationName;
    }

    public string Code { get; }

    public string? SyntaxId { get; }

    public string? RelationName { get; }
}

internal sealed record RuntimeColumn(string Name);

internal sealed record RuntimeRow(MetaWeaveScriptValue[] Values);

internal sealed record RuntimeRowset(
    IReadOnlyList<RuntimeColumn> Columns,
    IReadOnlyList<RuntimeRow> Rows);

internal sealed record RuntimeSourceShape(
    string Name,
    IReadOnlyList<RuntimeColumn> Columns);

internal sealed record RuntimeSourceRow(
    RuntimeSourceShape Shape,
    RuntimeRow Row);

internal sealed record RuntimeResolvedColumnReference(
    int ScopeDepth,
    string SourceName,
    int ColumnOrdinal);

internal sealed record RuntimeTableResult(
    IReadOnlyList<RuntimeSourceShape> Sources,
    IReadOnlyList<RuntimeLocalRow> Rows);

internal sealed class RuntimeLocalRow
{
    private readonly Dictionary<string, RuntimeSourceRow> sources;

    public RuntimeLocalRow()
        : this(new Dictionary<string, RuntimeSourceRow>(StringComparer.OrdinalIgnoreCase))
    {
    }

    private RuntimeLocalRow(Dictionary<string, RuntimeSourceRow> sources)
    {
        this.sources = sources;
    }

    public IReadOnlyDictionary<string, RuntimeSourceRow> Sources => sources;

    public RuntimeLocalRow Combine(RuntimeLocalRow other)
    {
        var combined = new Dictionary<string, RuntimeSourceRow>(sources, StringComparer.OrdinalIgnoreCase);
        foreach (var source in other.sources)
        {
            if (!combined.TryAdd(source.Key, source.Value))
            {
                throw new MetaWeaveScriptExecutionFault(
                    "TableAliasDuplicate",
                    $"Table source name or alias '{source.Key}' is exposed more than once in the same scope.");
            }
        }

        return new RuntimeLocalRow(combined);
    }

    public static RuntimeLocalRow From(RuntimeSourceShape shape, RuntimeRow row) =>
        new(new Dictionary<string, RuntimeSourceRow>(StringComparer.OrdinalIgnoreCase)
        {
            [shape.Name] = new RuntimeSourceRow(shape, row)
        });
}

internal sealed class RuntimeFrame
{
    public RuntimeFrame(RuntimeLocalRow local, RuntimeFrame? parent = null)
    {
        Local = local;
        Parent = parent;
    }

    public RuntimeLocalRow Local { get; }

    public RuntimeFrame? Parent { get; }
}

internal enum RuntimeTruth
{
    False,
    True,
    Unknown
}

internal sealed record RuntimeEvaluationContext(
    RuntimeFrame Frame,
    int VisibleCommonTableExpressionOrdinal,
    IReadOnlyList<RuntimeFrame>? GroupFrames = null,
    bool WithinAggregate = false,
    IReadOnlyList<RuntimeFrame>? WindowFrames = null,
    int WindowFrameOrdinal = -1);

internal sealed record RuntimeWindowEvaluationKey(
    string FunctionId,
    IReadOnlyList<RuntimeFrame> Frames);

internal enum RuntimeCommonTableExpressionState
{
    NotEvaluated,
    Evaluating,
    Evaluated,
    Failed
}

internal enum RuntimeNamedRelationState
{
    NotEvaluated,
    Evaluating,
    Evaluated,
    Failed
}

internal sealed record RuntimeCommonTableExpressionDefinition(
    string Id,
    string Name,
    QueryExpression QueryExpression,
    int Ordinal);

internal sealed class MetaWeaveScriptValueEqualityComparer : IEqualityComparer<MetaWeaveScriptValue>
{
    public static MetaWeaveScriptValueEqualityComparer Instance { get; } = new();

    public bool Equals(MetaWeaveScriptValue x, MetaWeaveScriptValue y)
    {
        if (x.Kind != y.Kind)
        {
            return false;
        }

        return x.Kind switch
        {
            MetaWeaveScriptValueKind.Null => true,
            MetaWeaveScriptValueKind.String => StringComparer.OrdinalIgnoreCase.Equals(x.StringValue, y.StringValue),
            MetaWeaveScriptValueKind.Integer => x.IntegerValue == y.IntegerValue,
            _ => false
        };
    }

    public int GetHashCode(MetaWeaveScriptValue value) => value.Kind switch
    {
        MetaWeaveScriptValueKind.Null => 0,
        MetaWeaveScriptValueKind.String => HashCode.Combine(value.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(value.StringValue!)),
        MetaWeaveScriptValueKind.Integer => HashCode.Combine(value.Kind, value.IntegerValue),
        _ => 0
    };
}

internal sealed class RuntimeRowEqualityComparer : IEqualityComparer<RuntimeRow>
{
    public static RuntimeRowEqualityComparer Instance { get; } = new();

    public bool Equals(RuntimeRow? x, RuntimeRow? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null || x.Values.Length != y.Values.Length)
        {
            return false;
        }

        for (var index = 0; index < x.Values.Length; index++)
        {
            if (!MetaWeaveScriptValueEqualityComparer.Instance.Equals(x.Values[index], y.Values[index]))
            {
                return false;
            }
        }

        return true;
    }

    public int GetHashCode(RuntimeRow row)
    {
        var hash = new HashCode();
        foreach (var value in row.Values)
        {
            hash.Add(value, MetaWeaveScriptValueEqualityComparer.Instance);
        }

        return hash.ToHashCode();
    }
}
