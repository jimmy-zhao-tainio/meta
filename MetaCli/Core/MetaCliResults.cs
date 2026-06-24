namespace MetaCli.Core;

public sealed record MetaCliShowResult(
    IReadOnlyList<MetaCliApplicationSummary> Applications);

public sealed record MetaCliApplicationSummary(
    string Id,
    string Name,
    string ExecutableName,
    string Version,
    string Description,
    int CommandCount,
    int ExecutableCommandCount,
    int ParameterCount,
    int OptionCount,
    int PositionalArgumentCount,
    int ParameterGroupCount,
    IReadOnlyList<MetaCliCommandSummary> Commands);

public sealed record MetaCliCommandSummary(
    string Id,
    string Name,
    string Route,
    string Description,
    bool IsExecutable,
    bool IsDefault,
    int ParameterCount,
    int OptionCount,
    int PositionalArgumentCount);

public sealed record MetaCliIntegrityResult(
    int ApplicationCount,
    int CommandCount,
    int ExecutableCommandCount,
    int ParameterCount,
    int OptionCount,
    int OptionTokenCount,
    int PositionalArgumentCount,
    int ParameterGroupCount,
    int ParameterGroupMemberCount,
    int ValueArityCount,
    int ValueShapeCount,
    int AllowedValueCount,
    int DuplicateOptionBehaviorCount,
    int UnknownTokenBehaviorCount,
    int ParserPolicyCount,
    int OutputFormatCount,
    int OutputStreamCount,
    int OutputCount,
    int ExitCodeCount,
    IReadOnlyList<MetaCliIssue> Issues)
{
    public bool HasErrors => Issues.Any(static issue => issue.Severity == MetaCliIssueSeverity.Error);

    public int WarningCount => Issues.Count(static issue => issue.Severity == MetaCliIssueSeverity.Warning);
}

public sealed record MetaCliIssue(
    MetaCliIssueSeverity Severity,
    string Code,
    string Message,
    string Location);

public enum MetaCliIssueSeverity
{
    Error,
    Warning,
}
