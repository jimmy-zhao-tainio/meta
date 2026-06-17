namespace MetaMesh.Core;

public sealed record MetaMeshWorkspaceSummary(
    string Handle,
    string PhysicalPath,
    string ModelName,
    string WorkspaceKind,
    string Lifecycle);

public sealed record MetaMeshLinkSummary(
    string FromHandle,
    string ToHandle,
    string Kind,
    string Description);

public sealed record MetaMeshSuggestionSummary(
    string SuggestionKind,
    string Severity,
    string WorkspaceHandle,
    string Message,
    string SuggestedHandle = "",
    string SuggestedWorkspaceKind = "",
    string SuggestedLifecycle = "",
    string SuggestedPath = "");

public sealed record MetaMeshIssue(
    string Severity,
    string Code,
    string Message,
    string WorkspaceHandle = "");

public sealed record MetaMeshScanResult(
    string RootPath,
    IReadOnlyList<MetaMeshWorkspaceSummary> Workspaces,
    IReadOnlyList<MetaMeshSuggestionSummary> Suggestions);

public sealed record MetaMeshShowResult(
    string MeshName,
    string RootPath,
    IReadOnlyList<MetaMeshWorkspaceSummary> Workspaces,
    IReadOnlyList<MetaMeshLinkSummary> Links,
    IReadOnlyList<MetaMeshSuggestionSummary> Suggestions);

public sealed record MetaMeshCheckResult(IReadOnlyList<MetaMeshIssue> Issues)
{
    public bool HasErrors => Issues.Any(static issue => string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase));
}

public sealed record MetaMeshImpactResult(
    string WorkspaceHandle,
    IReadOnlyList<MetaMeshLinkSummary> AffectedLinks,
    IReadOnlyList<string> AffectedHandles);
