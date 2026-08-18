using System.Collections.ObjectModel;
using System.Globalization;
using Meta.Operations.Domain;

namespace MetaWeaveScript.Execution;

public enum MetaWeaveScriptValueKind
{
    Null,
    String,
    Integer
}

public readonly record struct MetaWeaveScriptValue
{
    private MetaWeaveScriptValue(
        MetaWeaveScriptValueKind kind,
        string? stringValue,
        long integerValue)
    {
        Kind = kind;
        StringValue = stringValue;
        IntegerValue = integerValue;
    }

    public MetaWeaveScriptValueKind Kind { get; }

    public string? StringValue { get; }

    public long IntegerValue { get; }

    public bool IsNull => Kind == MetaWeaveScriptValueKind.Null;

    public static MetaWeaveScriptValue Null => default;

    public static MetaWeaveScriptValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new MetaWeaveScriptValue(MetaWeaveScriptValueKind.String, value, 0);
    }

    public static MetaWeaveScriptValue FromInteger(long value) =>
        new(MetaWeaveScriptValueKind.Integer, null, value);

    public string ToInvariantString() => Kind switch
    {
        MetaWeaveScriptValueKind.String => StringValue!,
        MetaWeaveScriptValueKind.Integer => IntegerValue.ToString(CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException("NULL cannot be serialized as a present workspace member.")
    };

    public override string ToString() => IsNull ? "NULL" : ToInvariantString();
}

public sealed record MetaWeaveScriptQueryColumn(string Name);

public sealed record MetaWeaveScriptQueryRow(
    IReadOnlyList<MetaWeaveScriptValue> Values);

public sealed record MetaWeaveScriptQueryOutput(
    IReadOnlyList<MetaWeaveScriptQueryColumn> Columns,
    IReadOnlyList<MetaWeaveScriptQueryRow> Rows);

public sealed record MetaWeaveScriptTransformation(
    string Name,
    string TargetEntityName,
    SelectStatement SelectStatement);

public sealed record MetaWeaveScriptRelation(
    string Name,
    SelectStatement SelectStatement);

public sealed record MetaWeaveScriptRequirement(
    string Name,
    string Code,
    string Message,
    SelectStatement SelectStatement);

public sealed record MetaWeaveScriptSourceWorkspace(
    string Name,
    string ModelName);

public sealed record MetaWeaveScriptStringParameter(string Name);

public sealed record MetaWeaveScriptDirection(
    string Name,
    IReadOnlyList<MetaWeaveScriptSourceWorkspace> SourceWorkspaces,
    string TargetModelName,
    IReadOnlyList<MetaWeaveScriptStringParameter> StringParameters,
    MetaWeaveModel Model,
    IReadOnlyList<MetaWeaveScriptTransformation> Transformations,
    IReadOnlyList<MetaWeaveScriptRequirement> Requirements,
    IReadOnlyList<MetaWeaveScriptRelation> Relations)
{
    public MetaWeaveScriptDirection(
        string name,
        string sourceModelName,
        string targetModelName,
        MetaWeaveModel model,
        IReadOnlyList<MetaWeaveScriptTransformation> transformations,
        IReadOnlyList<MetaWeaveScriptRequirement> requirements)
        : this(
            name,
            [new MetaWeaveScriptSourceWorkspace("source", sourceModelName)],
            targetModelName,
            [],
            model,
            transformations,
            requirements,
            [])
    {
    }
}

public sealed record MetaWeaveScriptExecutionIssue(
    string Code,
    string Message,
    string? TransformationName = null,
    string? SyntaxId = null,
    string? RequirementName = null,
    string? RelationName = null);

public sealed record MetaWeaveScriptQueryResult(
    MetaWeaveScriptQueryOutput? Output,
    IReadOnlyList<MetaWeaveScriptExecutionIssue> Issues)
{
    public bool IsSuccess => Output is not null && Issues.Count == 0;
}

public sealed record MetaWeaveScriptApplicationResult(
    InMemoryWorkspace? OutputWorkspace,
    IReadOnlyList<MetaWeaveScriptExecutionIssue> Issues)
{
    public bool IsSuccess => OutputWorkspace is not null && Issues.Count == 0;

    public IReadOnlyDictionary<string, MetaWeaveScriptQueryOutput> RelationOutputs { get; init; } =
        new ReadOnlyDictionary<string, MetaWeaveScriptQueryOutput>(
            new Dictionary<string, MetaWeaveScriptQueryOutput>(StringComparer.OrdinalIgnoreCase));
}

public enum MetaWeaveScriptExecutionTaskKind
{
    Requirement,
    Relation,
    TargetEntity
}

public sealed record MetaWeaveScriptExecutionProgress(
    int CompletedTaskCount,
    int TotalTaskCount,
    MetaWeaveScriptExecutionTaskKind? CompletedTaskKind,
    string? CompletedTaskName);
