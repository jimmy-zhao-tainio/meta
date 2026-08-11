using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Core.Services;

public readonly record struct WorkspaceMergeOptions(
    string MergedModelName);

public readonly record struct WorkspaceMergeResult(
    int SourceWorkspaceCount,
    int EntitiesMerged,
    int RowsMerged,
    string MergedModelName);

public readonly record struct WorkspaceMergePlan(
    InMemoryWorkspace Workspace,
    WorkspaceMergeResult Result);

public interface IWorkspaceMergeService
{
    Task<WorkspaceMergePlan> MergeAsync(
        IReadOnlyList<IMetaWorkspaceSource> sourceWorkspaces,
        WorkspaceMergeOptions options,
        CancellationToken cancellationToken = default);
}

public readonly record struct InstanceDiffBuildResult(
    InMemoryWorkspace DiffWorkspace,
    bool HasDifferences,
    int LeftRowCount,
    int RightRowCount,
    int LeftPropertyCount,
    int RightPropertyCount,
    int LeftNotInRightCount,
    int RightNotInLeftCount);

public interface IInstanceDiffService
{
    InstanceDiffBuildResult BuildEqualDiffWorkspace(
        InMemoryWorkspace leftWorkspace,
        InMemoryWorkspace rightWorkspace);

    InstanceDiffBuildResult BuildAlignedDiffWorkspace(
        InMemoryWorkspace leftWorkspace,
        InMemoryWorkspace rightWorkspace,
        InMemoryWorkspace alignmentWorkspace);

    IReadOnlyList<Operation> PlanEqualDiffMerge(
        InMemoryWorkspace targetWorkspace,
        InMemoryWorkspace diffWorkspace);

    IReadOnlyList<Operation> PlanAlignedDiffMerge(
        InMemoryWorkspace targetWorkspace,
        InMemoryWorkspace diffWorkspace);
}
