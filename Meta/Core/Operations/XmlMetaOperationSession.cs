using Meta.Core.Domain;
using Meta.Core.Services;

namespace Meta.Core.Operations;

public sealed class XmlMetaOperationSession
{
    private readonly IWorkspaceService _workspaceService;
    private readonly MetaOperationInterpreter _interpreter;
    private WorkspaceSnapshot _baseline;
    private string _baselineFingerprint;
    private readonly Workspace _workspace;

    private XmlMetaOperationSession(
        IWorkspaceService workspaceService,
        MetaOperationInterpreter interpreter,
        Workspace workspace,
        WorkspaceSnapshot baseline,
        string baselineFingerprint)
    {
        _workspaceService = workspaceService;
        _interpreter = interpreter;
        _workspace = workspace;
        _baseline = baseline;
        _baselineFingerprint = baselineFingerprint;
    }

    public string WorkspacePath => _workspace.WorkspaceRootPath;

    public static async Task<XmlMetaOperationSession> OpenExistingAsync(
        string workspacePath,
        IWorkspaceService? workspaceService = null,
        MetaOperationInterpreter? interpreter = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new ArgumentException("Workspace path is required.", nameof(workspacePath));
        }

        var service = workspaceService ?? new WorkspaceService();
        var workspace = await service.LoadAsync(
                workspacePath,
                searchUpward: false,
                cancellationToken)
            .ConfigureAwait(false);
        var baseline = WorkspaceSnapshotCloner.Capture(workspace);
        var fingerprint = service.CalculateHash(workspace);

        return new XmlMetaOperationSession(
            service,
            interpreter ?? new MetaOperationInterpreter(),
            workspace,
            baseline,
            fingerprint);
    }

    public static XmlMetaOperationSession OpenLoaded(
        Workspace workspace,
        IWorkspaceService? workspaceService = null,
        MetaOperationInterpreter? interpreter = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var service = workspaceService ?? new WorkspaceService();
        return new XmlMetaOperationSession(
            service,
            interpreter ?? new MetaOperationInterpreter(),
            workspace,
            WorkspaceSnapshotCloner.Capture(workspace),
            service.CalculateHash(workspace));
    }

    public GenericMetadataState Snapshot()
    {
        return GenericMetadataState.Capture(_workspace);
    }

    public GenericMetaOperationResult Apply(MetaOperationPlan plan)
    {
        var current = new GenericMetadataState(
            _workspace.Model,
            _workspace.Instance);
        var result = _interpreter.Apply(current, plan);

        _workspace.Model = result.State.Model;
        _workspace.Instance = result.State.Instance;
        _workspace.IsDirty = true;

        return new GenericMetaOperationResult(
            result.State.Clone(),
            result.AppliedOperationCount);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _workspaceService.SaveAsync(
                _workspace,
                _baselineFingerprint,
                cancellationToken)
            .ConfigureAwait(false);

        _baseline = WorkspaceSnapshotCloner.Capture(_workspace);
        _baselineFingerprint = _workspaceService.CalculateHash(_workspace);
    }

    public void Discard()
    {
        WorkspaceSnapshotCloner.Restore(_workspace, _baseline);
        _workspace.IsDirty = false;
    }
}
