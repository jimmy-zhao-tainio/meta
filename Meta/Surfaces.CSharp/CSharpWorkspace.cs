using System.Security.Cryptography;
using System.Text;
using Meta.Operations.Domain;
using Meta.Operations;

using Meta.Surfaces;

namespace Meta.Surfaces.CSharp;

public sealed class CSharpWorkspace : IMetaWorkspace
{
    private const int MaxStableReadAttempts = 3;

    private InMemoryWorkspace _state;
    private IReadOnlyList<string> _ownedSources;
    private bool _explicitOwnership;
    private string _fingerprint;

    private CSharpWorkspace(
        string rootPath,
        InMemoryWorkspace state,
        IReadOnlyList<string> ownedSources,
        bool explicitOwnership,
        string fingerprint)
    {
        RootPath = rootPath;
        _state = state;
        _ownedSources = ownedSources;
        _explicitOwnership = explicitOwnership;
        _fingerprint = fingerprint;
    }

    public string RootPath { get; }

    private IMetaWorkspaceSource Source => new InMemoryWorkspaceSource(_state);

    public static async Task<CSharpWorkspace> OpenAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var rootPath = Path.GetFullPath(workspacePath);
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException(
                $"C# workspace '{rootPath}' was not found.");
        }

        var snapshot = await ReadStableSnapshotAsync(rootPath, cancellationToken)
            .ConfigureAwait(false);
        var metadata = snapshot.Metadata;
        var ownedSources = snapshot.OwnedSources;
        var sources = snapshot.Sources;
        var state = MetaCSharpReader.Read(new MetaCSharp(sources));
        return new CSharpWorkspace(
            rootPath,
            state,
            ownedSources,
            metadata.Sources.Count > 0,
            CalculateFingerprint(metadata, ownedSources, sources));
    }

    public static async Task CreateAsync(
        InMemoryWorkspace workspace,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var rootPath = Path.GetFullPath(workspacePath);
        Directory.CreateDirectory(rootPath);
        var descriptorPath = Path.Combine(rootPath, WorkspaceMetaFile.FileName);
        if (File.Exists(descriptorPath))
        {
            throw new InvalidOperationException(
                $"C# workspace destination '{rootPath}' already contains {WorkspaceMetaFile.FileName}.");
        }

        var output = MetaCSharpWriter.Write(workspace);
        var sourcePaths = WorkspaceMetaFile.NormalizeCSharpSources(
            rootPath,
            output.Sources.Keys,
            descriptorPath);
        EnsureTargetPathsAvailable(rootPath, sourcePaths, []);

        var parent = Path.GetDirectoryName(rootPath) ??
            throw new InvalidOperationException(
                $"C# workspace '{rootPath}' has no parent directory.");
        var stagePath = CreateTemporaryDirectory(parent, Path.GetFileName(rootPath), "stage");
        var movedSources = new List<string>();
        var descriptorPublished = false;
        byte[]? publishedDescriptorBytes = null;
        var preserveStage = false;
        WorkspaceWriteLockHandle? writeLock = null;
        try
        {
            await WriteSourcesAsync(stagePath, output.Sources, cancellationToken)
                .ConfigureAwait(false);
            WorkspaceMetaFile.WriteCSharp(stagePath, sourcePaths);
            await ValidateStagedAsync(stagePath, workspace, cancellationToken)
                .ConfigureAwait(false);

            writeLock = WorkspaceWriteLock.Acquire(rootPath);
            try
            {
                if (File.Exists(descriptorPath))
                {
                    throw new WorkspaceConflictException(
                        $"C# workspace '{rootPath}' was created while publication was staged.",
                        string.Empty,
                        "workspace.meta");
                }

                EnsureTargetPathsAvailable(rootPath, sourcePaths, []);
                MoveExactSources(stagePath, rootPath, sourcePaths, movedSources);
                CSharpWorkspacePublicationTestHooks.Invoke(
                    rootPath,
                    CSharpWorkspacePublicationCheckpoint.AfterCreationSourcesMoved);

                var stagedDescriptorPath = Path.Combine(stagePath, WorkspaceMetaFile.FileName);
                publishedDescriptorBytes = File.ReadAllBytes(stagedDescriptorPath);
                File.Move(stagedDescriptorPath, descriptorPath);
                descriptorPublished = true;
            }
            catch (Exception creationFailure)
            {
                try
                {
                    if (descriptorPublished)
                    {
                        DeletePublishedFile(descriptorPath, publishedDescriptorBytes!);
                    }

                    DeletePublishedSources(rootPath, movedSources, output.Sources);
                }
                catch (Exception cleanupFailure)
                {
                    preserveStage = true;
                    throw new WorkspaceCreationException(
                        rootPath,
                        stagePath,
                        creationFailure,
                        cleanupFailure);
                }

                throw;
            }
        }
        finally
        {
            try
            {
                if (!preserveStage)
                {
                    DeleteKnownTemporaryDirectory(stagePath);
                }
            }
            finally
            {
                writeLock?.Dispose();
            }
        }
    }

    public async ValueTask<IReadOnlyList<OperationResult>> ExecuteAsync(
        IReadOnlyList<Operation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        cancellationToken.ThrowIfCancellationRequested();
        var applied = InMemoryOperations.ExecuteBatch(_state, operations);
        var output = MetaCSharpWriter.Write(applied.Workspace);
        await PublishAsync(output, applied.Workspace, cancellationToken)
            .ConfigureAwait(false);
        _state = applied.Workspace;
        return applied.Results;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ValueTask<string> ReadModelNameAsync(
        CancellationToken cancellationToken = default) =>
        Source.ReadModelNameAsync(cancellationToken);

    public IAsyncEnumerable<string> ReadEntityNamesAsync(
        CancellationToken cancellationToken = default) =>
        Source.ReadEntityNamesAsync(cancellationToken);

    public IAsyncEnumerable<PropertyDefinition> ReadPropertiesAsync(
        string entityName,
        CancellationToken cancellationToken = default) =>
        Source.ReadPropertiesAsync(entityName, cancellationToken);

    public IAsyncEnumerable<RelationshipDefinition> ReadRelationshipsAsync(
        string entityName,
        CancellationToken cancellationToken = default) =>
        Source.ReadRelationshipsAsync(entityName, cancellationToken);

    public IAsyncEnumerable<RecordData> ReadRecordsAsync(
        string entityName,
        CancellationToken cancellationToken = default) =>
        Source.ReadRecordsAsync(entityName, cancellationToken);

    public ValueTask<long> CountRecordsAsync(
        string entityName,
        CancellationToken cancellationToken = default) =>
        Source.CountRecordsAsync(entityName, cancellationToken);

    public ValueTask<RecordQueryResult> QueryRecordsAsync(
        string entityName,
        RecordQuery query,
        CancellationToken cancellationToken = default) =>
        Source.QueryRecordsAsync(entityName, query, cancellationToken);

    public ValueTask<RecordData?> ReadRecordAsync(
        string entityName,
        string id,
        CancellationToken cancellationToken = default) =>
        Source.ReadRecordAsync(entityName, id, cancellationToken);

    private async Task PublishAsync(
        MetaCSharp output,
        InMemoryWorkspace expected,
        CancellationToken cancellationToken)
    {
        var newSources = WorkspaceMetaFile.NormalizeCSharpSources(
            RootPath,
            output.Sources.Keys,
            Path.Combine(RootPath, WorkspaceMetaFile.FileName));
        var parent = Path.GetDirectoryName(RootPath) ??
            throw new InvalidOperationException(
                $"C# workspace '{RootPath}' has no parent directory.");
        var stagePath = CreateTemporaryDirectory(parent, Path.GetFileName(RootPath), "stage");
        string? backupPath = null;
        var movedOldSources = new List<string>();
        var movedNewSources = new List<string>();
        var oldDescriptorBackedUp = false;
        var newDescriptorPublished = false;
        byte[]? publishedDescriptorBytes = null;
        var preserveBackup = false;
        WorkspaceWriteLockHandle? writeLock = null;

        try
        {
            await WriteSourcesAsync(stagePath, output.Sources, cancellationToken)
                .ConfigureAwait(false);
            WorkspaceMetaFile.WriteCSharp(stagePath, newSources);
            await ValidateStagedAsync(stagePath, expected, cancellationToken)
                .ConfigureAwait(false);

            writeLock = WorkspaceWriteLock.Acquire(RootPath);
            backupPath = CreateTemporaryDirectory(parent, Path.GetFileName(RootPath), "backup");

            try
            {
                var currentMetadata = WorkspaceMetaFile.Read(RootPath);
                EnsureCSharp(currentMetadata, RootPath);
                var currentSources = currentMetadata.Sources.Count > 0
                    ? currentMetadata.Sources
                    : ReadLegacySources(RootPath, cancellationToken);
                var currentExplicitOwnership = currentMetadata.Sources.Count > 0;
                if (currentExplicitOwnership != _explicitOwnership ||
                    !currentSources.SequenceEqual(_ownedSources, StringComparer.OrdinalIgnoreCase))
                {
                    throw new WorkspaceConflictException(
                        "C# workspace ownership declaration changed after it was opened.",
                        _fingerprint,
                        "ownership declaration");
                }

                var currentContents = await ReadOwnedSourcesAsync(
                        RootPath,
                        currentSources,
                        cancellationToken)
                    .ConfigureAwait(false);
                var actualFingerprint = CalculateFingerprint(
                    currentMetadata,
                    currentSources,
                    currentContents);
                if (!string.Equals(actualFingerprint, _fingerprint, StringComparison.Ordinal))
                {
                    throw new WorkspaceConflictException(
                        $"C# workspace fingerprint mismatch. Expected '{_fingerprint}', found '{actualFingerprint}'.",
                        _fingerprint,
                        actualFingerprint);
                }

                EnsureTargetPathsAvailable(RootPath, newSources, currentSources);
                var currentDescriptorPath = Path.Combine(RootPath, WorkspaceMetaFile.FileName);
                var backupDescriptorPath = Path.Combine(backupPath, WorkspaceMetaFile.FileName);
                File.Copy(currentDescriptorPath, backupDescriptorPath, overwrite: false);
                oldDescriptorBackedUp = true;
                MoveExactSources(RootPath, backupPath, currentSources, movedOldSources);

                MoveExactSources(stagePath, RootPath, newSources, movedNewSources);
                var stagedDescriptorPath = Path.Combine(stagePath, WorkspaceMetaFile.FileName);
                publishedDescriptorBytes = File.ReadAllBytes(stagedDescriptorPath);
                File.Replace(
                    stagedDescriptorPath,
                    currentDescriptorPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
                newDescriptorPublished = true;
                CSharpWorkspacePublicationTestHooks.Invoke(
                    RootPath,
                    CSharpWorkspacePublicationCheckpoint.AfterNewStatePublished);

                var publishedMetadata = WorkspaceMetaFile.Read(RootPath);
                if (!publishedMetadata.Sources.SequenceEqual(newSources, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Published C# workspace '{RootPath}' does not declare the complete owned source set.");
                }

                var publishedSources = await ReadOwnedSourcesAsync(
                        RootPath,
                        publishedMetadata.Sources,
                        cancellationToken)
                    .ConfigureAwait(false);
                var publishedState = MetaCSharpReader.Read(new MetaCSharp(publishedSources));
                EnsureEquivalent(expected, publishedState);
                _ownedSources = publishedMetadata.Sources;
                _explicitOwnership = true;
                _fingerprint = CalculateFingerprint(
                    publishedMetadata,
                    _ownedSources,
                    publishedSources);
            }
            catch (Exception publicationFailure)
            {
                try
                {
                    CSharpWorkspacePublicationTestHooks.Invoke(
                        RootPath,
                        CSharpWorkspacePublicationCheckpoint.BeforeRollback);
                    CSharpWorkspacePublicationTestHooks.Invoke(
                        RootPath,
                        CSharpWorkspacePublicationCheckpoint.BeforeRestore);

                    DeletePublishedSources(RootPath, movedNewSources, output.Sources);
                    if (newDescriptorPublished && oldDescriptorBackedUp)
                    {
                        RestorePublishedDescriptor(
                            backupPath,
                            RootPath,
                            publishedDescriptorBytes!);
                    }

                    RestoreExactSources(backupPath, RootPath, movedOldSources);
                }
                catch (Exception rollbackFailure)
                {
                    preserveBackup = true;
                    throw new WorkspacePublicationException(
                        RootPath,
                        backupPath,
                        publicationFailure,
                        rollbackFailure);
                }

                throw;
            }
        }
        finally
        {
            try
            {
                DeleteKnownTemporaryDirectory(stagePath);
            }
            finally
            {
                try
                {
                    if (!preserveBackup && backupPath != null)
                    {
                        DeleteKnownTemporaryDirectory(backupPath);
                    }
                }
                finally
                {
                    writeLock?.Dispose();
                }
            }
        }
    }

    private static async Task<CSharpWorkspaceSnapshot> ReadStableSnapshotAsync(
        string rootPath,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        var descriptorPath = Path.Combine(rootPath, WorkspaceMetaFile.FileName);

        for (var attempt = 0; attempt < MaxStableReadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkspaceWriteLock.ThrowIfActive(rootPath);

            try
            {
                var descriptorBefore = await File.ReadAllBytesAsync(
                        descriptorPath,
                        cancellationToken)
                    .ConfigureAwait(false);
                var metadata = WorkspaceMetaFile.Read(rootPath);
                EnsureCSharp(metadata, rootPath);
                var ownedSources = metadata.Sources.Count > 0
                    ? metadata.Sources
                    : ReadLegacySources(rootPath, cancellationToken);
                var sources = await ReadOwnedSourcesAsync(
                        rootPath,
                        ownedSources,
                        cancellationToken)
                    .ConfigureAwait(false);
                var verificationSources = await ReadOwnedSourcesAsync(
                        rootPath,
                        ownedSources,
                        cancellationToken)
                    .ConfigureAwait(false);
                var descriptorAfter = await File.ReadAllBytesAsync(
                        descriptorPath,
                        cancellationToken)
                    .ConfigureAwait(false);
                var verificationOwnedSources = metadata.Sources.Count > 0
                    ? metadata.Sources
                    : ReadLegacySources(rootPath, cancellationToken);

                WorkspaceWriteLock.ThrowIfActive(rootPath);
                if (descriptorBefore.SequenceEqual(descriptorAfter) &&
                    ownedSources.SequenceEqual(
                        verificationOwnedSources,
                        StringComparer.OrdinalIgnoreCase) &&
                    SourcesEqual(sources, verificationSources))
                {
                    return new CSharpWorkspaceSnapshot(
                        metadata,
                        ownedSources,
                        verificationSources);
                }

                lastFailure = new IOException(
                    $"C# workspace '{rootPath}' changed while it was being opened.");
            }
            catch (Exception exception)
            {
                try
                {
                    WorkspaceWriteLock.ThrowIfActive(rootPath);
                }
                catch (InvalidOperationException)
                {
                    throw;
                }

                if (attempt + 1 >= MaxStableReadAttempts ||
                    exception is not IOException and not InvalidDataException)
                {
                    throw;
                }

                lastFailure = exception;
            }
        }

        throw new IOException(
            $"C# workspace '{rootPath}' changed while it was being opened after {MaxStableReadAttempts} attempts.",
            lastFailure);
    }

    private static async Task ValidateStagedAsync(
        string stagePath,
        InMemoryWorkspace expected,
        CancellationToken cancellationToken)
    {
        var metadata = WorkspaceMetaFile.Read(stagePath);
        EnsureCSharp(metadata, stagePath);
        var sources = await ReadOwnedSourcesAsync(
                stagePath,
                metadata.Sources,
                cancellationToken)
            .ConfigureAwait(false);
        var actual = MetaCSharpReader.Read(new MetaCSharp(sources));
        EnsureEquivalent(expected, actual);
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadOwnedSourcesAsync(
        string rootPath,
        IReadOnlyList<string> ownedSources,
        CancellationToken cancellationToken)
    {
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in ownedSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = WorkspaceMetaFile.ResolveCSharpSourcePath(rootPath, source);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"C# workspace source '{source}' is listed in {WorkspaceMetaFile.FileName} but does not exist.",
                    path);
            }

            var contents = await File.ReadAllTextAsync(path, cancellationToken)
                .ConfigureAwait(false);
            sources.Add(source, contents);
        }

        if (sources.Count == 0)
        {
            throw new InvalidDataException(
                $"C# workspace '{rootPath}' owns no source files.");
        }

        return sources;
    }

    private static bool SourcesEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var contents) ||
                !string.Equals(pair.Value, contents, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<string> ReadLegacySources(
        string rootPath,
        CancellationToken cancellationToken)
    {
        var paths = Directory.EnumerateFiles(rootPath, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(rootPath, path).Replace('\\', '/'))
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        if (paths.Length != 1)
        {
            throw new InvalidDataException(
                $"Legacy C# workspace '{rootPath}' must contain exactly one unambiguous source file before it can migrate; found {paths.Length}. Add explicit source directives to {WorkspaceMetaFile.FileName}.");
        }

        return WorkspaceMetaFile.NormalizeCSharpSources(
            rootPath,
            paths,
            Path.Combine(rootPath, WorkspaceMetaFile.FileName));
    }

    private static async Task WriteSourcesAsync(
        string rootPath,
        IReadOnlyDictionary<string, string> sources,
        CancellationToken cancellationToken)
    {
        var sourcePaths = WorkspaceMetaFile.NormalizeCSharpSources(
            rootPath,
            sources.Keys,
            Path.Combine(rootPath, WorkspaceMetaFile.FileName));
        foreach (var sourcePath in sourcePaths)
        {
            var path = WorkspaceMetaFile.ResolveCSharpSourcePath(rootPath, sourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(
                    path,
                    sources[sourcePath],
                    new UTF8Encoding(false),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static void MoveExactSources(
        string sourceRoot,
        string targetRoot,
        IReadOnlyList<string> sources,
        ICollection<string> moved)
    {
        foreach (var source in sources)
        {
            var sourcePath = WorkspaceMetaFile.ResolveCSharpSourcePath(sourceRoot, source);
            var targetPath = WorkspaceMetaFile.ResolveCSharpSourcePath(targetRoot, source);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    $"C# workspace source '{source}' was not found during publication.",
                    sourcePath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Move(sourcePath, targetPath, overwrite: false);
            moved.Add(source);
        }
    }

    private static void RestoreExactSources(
        string backupRoot,
        string targetRoot,
        IReadOnlyList<string> sources)
    {
        foreach (var source in sources)
        {
            var sourcePath = WorkspaceMetaFile.ResolveCSharpSourcePath(backupRoot, source);
            var targetPath = WorkspaceMetaFile.ResolveCSharpSourcePath(targetRoot, source);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Move(sourcePath, targetPath, overwrite: false);
        }
    }

    private static void DeletePublishedSources(
        string rootPath,
        IEnumerable<string> sources,
        IReadOnlyDictionary<string, string> expectedContents)
    {
        foreach (var source in sources)
        {
            var path = WorkspaceMetaFile.ResolveCSharpSourcePath(rootPath, source);
            if (!File.Exists(path))
            {
                continue;
            }

            var actual = File.ReadAllBytes(path);
            var expected = new UTF8Encoding(false).GetBytes(expectedContents[source]);
            if (!actual.SequenceEqual(expected))
            {
                throw new IOException(
                    $"Cannot roll back C# workspace source '{source}' because it changed after publication.");
            }

            File.Delete(path);
        }
    }

    private static void DeletePublishedFile(string path, byte[] expectedContents)
    {
        if (!File.Exists(path))
        {
            return;
        }

        if (!File.ReadAllBytes(path).SequenceEqual(expectedContents))
        {
            throw new IOException(
                $"Cannot roll back C# workspace descriptor '{path}' because it changed after publication.");
        }

        File.Delete(path);
    }

    private static void RestorePublishedDescriptor(
        string backupRoot,
        string targetRoot,
        byte[] publishedContents)
    {
        var backupPath = Path.Combine(backupRoot, WorkspaceMetaFile.FileName);
        var targetPath = Path.Combine(targetRoot, WorkspaceMetaFile.FileName);
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException(
                $"C# workspace descriptor backup '{backupPath}' was not found.",
                backupPath);
        }

        if (File.Exists(targetPath) &&
            !File.ReadAllBytes(targetPath).SequenceEqual(publishedContents))
        {
            throw new IOException(
                $"Cannot roll back C# workspace descriptor '{targetPath}' because it changed after publication.");
        }

        var restorePath = Path.Combine(
            backupRoot,
            $".{WorkspaceMetaFile.FileName}.restore-{Guid.NewGuid():N}");
        try
        {
            File.Copy(backupPath, restorePath, overwrite: false);
            if (File.Exists(targetPath))
            {
                File.Replace(
                    restorePath,
                    targetPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(restorePath, targetPath);
            }
        }
        finally
        {
            TryDeleteFile(restorePath);
        }
    }

    private static void EnsureTargetPathsAvailable(
        string rootPath,
        IReadOnlyList<string> newSources,
        IReadOnlyList<string> oldSources)
    {
        var oldSet = oldSources.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var source in newSources)
        {
            var path = WorkspaceMetaFile.ResolveCSharpSourcePath(rootPath, source);
            if ((File.Exists(path) || Directory.Exists(path)) && !oldSet.Contains(source))
            {
                throw new IOException(
                    $"C# workspace cannot publish owned source '{source}' because an unowned path already exists.");
            }
        }
    }

    private static string CalculateFingerprint(
        WorkspaceMetaDocument metadata,
        IReadOnlyList<string> ownedSources,
        IReadOnlyDictionary<string, string> sources)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprintPart(hash, metadata.Representation);
        AppendFingerprintPart(hash, metadata.Location);
        AppendFingerprintPart(hash, metadata.Sources.Count == 0 ? "legacy" : "explicit");
        foreach (var source in ownedSources.OrderBy(item => item, StringComparer.Ordinal))
        {
            AppendFingerprintPart(hash, source);
            AppendFingerprintPart(hash, sources[source]);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendFingerprintPart(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static void EnsureEquivalent(
        InMemoryWorkspace expected,
        InMemoryWorkspace actual)
    {
        var difference = InMemoryWorkspaceComparer.FindDifference(expected, actual);
        if (difference != null)
        {
            throw new InvalidDataException(
                $"Published C# workspace changed metadata semantics. {difference}");
        }
    }

    private static void EnsureCSharp(
        WorkspaceMetaDocument metadata,
        string rootPath)
    {
        if (!string.Equals(metadata.Representation, "csharp", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Workspace '{rootPath}' selects '{metadata.Representation}', not the C# surface.");
        }
    }

    private static string CreateTemporaryDirectory(
        string parent,
        string workspaceName,
        string purpose)
    {
        var path = Path.Combine(
            parent,
            $".{workspaceName}.meta-{purpose}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteKnownTemporaryDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record CSharpWorkspaceSnapshot(
        WorkspaceMetaDocument Metadata,
        IReadOnlyList<string> OwnedSources,
        IReadOnlyDictionary<string, string> Sources);
}
