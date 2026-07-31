using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Services;

namespace Meta.Adapters;

public sealed class CSharpMetaOperationSession
{
    private readonly CSharpMetaWorkspaceReader _reader;
    private readonly MetaOperationInterpreter _interpreter;
    private GenericMetadataState _baseline;
    private GenericMetadataState _working;
    private string _baselineFingerprint;
    private bool _hasPendingOperations;

    private CSharpMetaOperationSession(
        string workspacePath,
        CSharpMetaWorkspaceReader reader,
        MetaOperationInterpreter interpreter,
        GenericMetadataState baseline,
        string baselineFingerprint)
    {
        WorkspacePath = workspacePath;
        _reader = reader;
        _interpreter = interpreter;
        _baseline = baseline;
        _working = baseline.Clone();
        _baselineFingerprint = baselineFingerprint;
    }

    public string WorkspacePath { get; }

    public static CSharpMetaOperationSession Create(
        string workspacePath,
        GenericMetadataState initialState,
        CSharpMetaWorkspaceReader? reader = null,
        MetaOperationInterpreter? interpreter = null)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new ArgumentException(
                "C# workspace path is required.",
                nameof(workspacePath));
        }

        CSharpMetaLanguage.RequireRepresentable(initialState);
        var root = Path.GetFullPath(workspacePath);
        if (Directory.Exists(root) &&
            Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new InvalidOperationException(
                $"C# workspace target must be absent or empty: {root}");
        }

        var targetExisted = Directory.Exists(root);
        var actualReader = reader ?? new CSharpMetaWorkspaceReader();
        var stagedPath = CSharpMetaWorkspaceFiles.CreateSiblingPath(
            root,
            "create");
        try
        {
            GenerationService.GenerateCSharpWorkspace(
                initialState.Model,
                initialState.Instance,
                stagedPath);
            CSharpMetaWorkspaceFiles.RequireOwnedWorkspace(stagedPath);
            var stagedDocument = actualReader.ReadDocument(stagedPath);
            if (!string.Equals(
                    CSharpMetaStateSignature.Build(initialState),
                    CSharpMetaStateSignature.Build(
                        stagedDocument.State),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Generated C# workspace did not preserve the initial metadata state.");
            }

            if (targetExisted)
            {
                Directory.Delete(root, recursive: false);
            }

            try
            {
                Directory.Move(stagedPath, root);
            }
            catch
            {
                if (targetExisted &&
                    !Directory.Exists(root))
                {
                    Directory.CreateDirectory(root);
                }

                throw;
            }
        }
        finally
        {
            if (Directory.Exists(stagedPath))
            {
                Directory.Delete(stagedPath, recursive: true);
            }
        }

        return OpenExisting(root, actualReader, interpreter);
    }

    public static CSharpMetaOperationSession OpenExisting(
        string workspacePath,
        CSharpMetaWorkspaceReader? reader = null,
        MetaOperationInterpreter? interpreter = null)
    {
        var root = CSharpMetaWorkspaceFiles.RequireOwnedWorkspace(
            workspacePath);
        var actualReader = reader ?? new CSharpMetaWorkspaceReader();
        var document = actualReader.ReadDocument(root);
        CSharpMetaLanguage.RequireRepresentable(document.State);
        return new CSharpMetaOperationSession(
            root,
            actualReader,
            interpreter ?? new MetaOperationInterpreter(),
            document.State.Clone(),
            document.Fingerprint);
    }

    public GenericMetadataState Snapshot()
    {
        return _working.Clone();
    }

    public GenericMetaOperationResult Apply(MetaOperationPlan plan)
    {
        var result = _interpreter.Apply(_working, plan);
        try
        {
            CSharpMetaLanguage.RequireRepresentable(result.State);
        }
        catch (Exception exception) when (exception is not MetaOperationException)
        {
            throw new MetaOperationException(
                $"Operation plan cannot be represented by the C# workspace. {exception.Message}",
                innerException: exception);
        }

        _working = result.State;
        _hasPendingOperations |= result.AppliedOperationCount > 0;
        return new GenericMetaOperationResult(
            _working.Clone(),
            result.AppliedOperationCount);
    }

    public void Commit()
    {
        if (!_hasPendingOperations)
        {
            return;
        }

        var actualFingerprint = Directory.Exists(WorkspacePath)
            ? CSharpMetaWorkspaceFiles.ComputeFingerprint(WorkspacePath)
            : "<missing>";
        if (!string.Equals(
                actualFingerprint,
                _baselineFingerprint,
                StringComparison.Ordinal))
        {
            throw new WorkspaceConflictException(
                $"C# workspace '{WorkspacePath}' changed after it was opened.",
                _baselineFingerprint,
                actualFingerprint);
        }

        var stagedPath = CSharpMetaWorkspaceFiles.CreateSiblingPath(
            WorkspacePath,
            "stage");
        try
        {
            GenerationService.GenerateCSharpWorkspace(
                _working.Model,
                _working.Instance,
                stagedPath);
            CSharpMetaWorkspaceFiles.RequireOwnedWorkspace(stagedPath);
            var stagedDocument = _reader.ReadDocument(stagedPath);
            if (!string.Equals(
                    CSharpMetaStateSignature.Build(_working),
                    CSharpMetaStateSignature.Build(stagedDocument.State),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Generated C# workspace did not preserve the pending metadata state.");
            }

            CSharpMetaWorkspaceFiles.PublishDirectory(
                stagedPath,
                WorkspacePath);
        }
        finally
        {
            if (Directory.Exists(stagedPath))
            {
                Directory.Delete(stagedPath, recursive: true);
            }
        }

        var committed = _reader.ReadDocument(WorkspacePath);
        _baseline = committed.State.Clone();
        _working = _baseline.Clone();
        _baselineFingerprint = committed.Fingerprint;
        _hasPendingOperations = false;
    }

    public void Discard()
    {
        _working = _baseline.Clone();
        _hasPendingOperations = false;
    }
}
