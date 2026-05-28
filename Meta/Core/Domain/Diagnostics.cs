using System.Collections.Generic;
using System.Linq;

namespace Meta.Core.Domain;

public enum IssueSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

public sealed class DiagnosticIssue
{
    public string Code { get; set; } = string.Empty;
    public IssueSeverity Severity { get; set; } = IssueSeverity.Error;
    public string Message { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
}

public sealed class WorkspaceDiagnostics
{
    public List<DiagnosticIssue> Issues { get; } = new();

    public bool HasErrors => Issues.Any(issue => issue.Severity == IssueSeverity.Error);
    public int ErrorCount => Issues.Count(issue => issue.Severity == IssueSeverity.Error);
    public int WarningCount => Issues.Count(issue => issue.Severity == IssueSeverity.Warning);
}
