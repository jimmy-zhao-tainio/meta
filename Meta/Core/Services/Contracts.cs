using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meta.Core.Domain;
using Meta.Core.Operations;

namespace Meta.Core.Services;

public readonly record struct RelationshipColumnRecovery(
    string SourceEntityName,
    string TargetEntityName,
    string ExistingColumnName);

public sealed class WorkspaceLoadOptions
{
    public WorkspaceLoadOptions(IReadOnlyList<RelationshipColumnRecovery> relationshipColumnRecoveries)
    {
        RelationshipColumnRecoveries = relationshipColumnRecoveries ??
            throw new ArgumentNullException(nameof(relationshipColumnRecoveries));
    }

    public IReadOnlyList<RelationshipColumnRecovery> RelationshipColumnRecoveries { get; }
}

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

public interface IImportService
{
    Task<InMemoryWorkspace> ImportSqlAsync(string connectionString, string schema, CancellationToken cancellationToken = default);
    Task<InMemoryWorkspace> ImportCsvAsync(
        string csvPath,
        string entityName,
        CancellationToken cancellationToken = default);
    CsvImportPlan PlanCsvImport(
        InMemoryWorkspace targetWorkspace,
        InMemoryWorkspace importedWorkspace);
}

public readonly record struct CsvImportPlan(
    string EntityName,
    int RowCount,
    IReadOnlyList<Operation> Operations);

public interface IExportService
{
    Task ExportXmlAsync(InMemoryWorkspace workspace, string outputDirectory, CancellationToken cancellationToken = default);
    Task ExportCsvAsync(
        IMetaWorkspaceSource source,
        string entityName,
        string outputPath,
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
