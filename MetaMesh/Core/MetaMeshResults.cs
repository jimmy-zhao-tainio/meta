namespace MetaMesh.Core;

public sealed record MetaMeshWorkspaceSummary(
    string Name,
    string Path,
    string ResolvedPath,
    string ModelName,
    string Description);

public sealed record MetaMeshWorkspaceIssue(
    string Name,
    string Path,
    string ResolvedPath,
    string ModelName,
    string Reason);

public sealed record MetaMeshOperationStepSummary(
    string Name,
    string Executable,
    string Arguments,
    string WorkingDirectory,
    string Description);

public sealed record MetaMeshOperationSummary(
    string Name,
    string Description,
    IReadOnlyList<MetaMeshOperationStepSummary> Steps);

public sealed record MetaMeshValidationStepSummary(
    string Name,
    string Command,
    string WorkingDirectory);

public sealed record MetaMeshValidationResult(
    string OperationName,
    IReadOnlyList<MetaMeshValidationStepSummary> Steps);

public sealed record MetaMeshShowResult(
    string MeshName,
    string RootPath,
    string ResolvedRootPath,
    IReadOnlyList<MetaMeshWorkspaceSummary> Workspaces,
    IReadOnlyList<MetaMeshWorkspaceIssue> WorkspaceIssues,
    IReadOnlyList<MetaMeshOperationSummary> Operations);

public sealed record MetaMeshRunStepResult(
    string Name,
    string Command,
    string WorkingDirectory,
    int ExitCode,
    string Output);

public sealed record MetaMeshRunResult(
    string OperationName,
    IReadOnlyList<MetaMeshRunStepResult> Steps)
{
    public bool Succeeded => Steps.All(static step => step.ExitCode == 0);
}

public sealed record MetaMeshRunStepStart(
    int Index,
    int Total,
    string Name,
    string Command,
    string WorkingDirectory);

public interface IMetaMeshRunObserver
{
    void StepStarted(MetaMeshRunStepStart step);

    void StepCompleted(MetaMeshRunStepResult step);
}

public sealed class MetaMeshWorkspaceIssueException : InvalidOperationException
{
    public MetaMeshWorkspaceIssueException(IReadOnlyList<MetaMeshWorkspaceIssue> issues)
        : base("Operation uses missing or invalid workspaces.")
    {
        Issues = issues;
    }

    public IReadOnlyList<MetaMeshWorkspaceIssue> Issues { get; }
}
