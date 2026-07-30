using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meta.Core.Domain;

namespace Meta.Core.Services;

public interface IWorkspaceService
{
    Task<Workspace> LoadAsync(
        string workspaceRootPath,
        bool searchUpward = false,
        CancellationToken cancellationToken = default,
        WorkspaceLoadOptions? loadOptions = null);
    Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default);
    Task SaveAsync(Workspace workspace, string? expectedFingerprint, CancellationToken cancellationToken = default);
    string CalculateHash(Workspace workspace);
}

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

public interface IWorkspaceMergeService
{
    WorkspaceMergeResult MergeInto(
        Workspace targetWorkspace,
        IReadOnlyList<Workspace> sourceWorkspaces,
        WorkspaceMergeOptions options);
}

public interface IValidationService
{
    WorkspaceDiagnostics Validate(Workspace workspace);
}

public interface IImportService
{
    Task<Workspace> ImportSqlAsync(string connectionString, string schema, CancellationToken cancellationToken = default);
    Task<Workspace> ImportCsvAsync(
        string csvPath,
        string entityName,
        CancellationToken cancellationToken = default);
    Task<CsvWorkspaceImportResult> ImportCsvIntoWorkspaceAsync(
        Workspace workspace,
        string csvPath,
        string entityName,
        CancellationToken cancellationToken = default);
}

public readonly record struct CsvWorkspaceImportResult(
    string EntityName,
    int RowsImported);

public interface IExportService
{
    Task ExportXmlAsync(Workspace workspace, string outputDirectory, CancellationToken cancellationToken = default);
    Task ExportCsvAsync(Workspace workspace, string entityName, string outputPath, CancellationToken cancellationToken = default);
}

public interface IModelRefactorService
{
    RenameModelRefactorResult RenameModel(
        Workspace workspace,
        RenameModelRefactorOptions options);

    RenameEntityRefactorResult RenameEntity(
        Workspace workspace,
        RenameEntityRefactorOptions options);

    RenameRelationshipRefactorResult RenameRelationship(
        Workspace workspace,
        RenameRelationshipRefactorOptions options);

    PropertyToRelationshipRefactorResult RefactorPropertyToRelationship(
        Workspace workspace,
        PropertyToRelationshipRefactorOptions options);

    RelationshipToPropertyRefactorResult RefactorRelationshipToProperty(
        Workspace workspace,
        RelationshipToPropertyRefactorOptions options);
}

public interface IInstanceRefactorService
{
    RenameInstanceIdRefactorResult RenameInstanceId(
        Workspace workspace,
        RenameInstanceIdRefactorOptions options);
}

public readonly record struct InstanceDiffBuildResult(
    Workspace DiffWorkspace,
    string DiffWorkspacePath,
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
        Workspace leftWorkspace,
        Workspace rightWorkspace,
        string rightWorkspacePath);

    InstanceDiffBuildResult BuildAlignedDiffWorkspace(
        Workspace leftWorkspace,
        Workspace rightWorkspace,
        Workspace alignmentWorkspace,
        string rightWorkspacePath);

    void ApplyEqualDiffWorkspace(
        Workspace targetWorkspace,
        Workspace diffWorkspace);

    void ApplyAlignedDiffWorkspace(
        Workspace targetWorkspace,
        Workspace diffWorkspace);
}
