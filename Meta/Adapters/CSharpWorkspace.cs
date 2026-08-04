using System.Security.Cryptography;
using System.Text;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Serialization;
using Meta.Core.Services;

namespace Meta.Adapters;

public sealed class CSharpWorkspace : IMetaWorkspace
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private InMemoryWorkspace _state;
    private string _fingerprint;

    private CSharpWorkspace(
        string rootPath,
        InMemoryWorkspace state,
        string fingerprint)
    {
        RootPath = rootPath;
        _state = state;
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

        var sources = await ReadSourcesAsync(rootPath, cancellationToken)
            .ConfigureAwait(false);
        var state = MetaCSharpReader.Read(new MetaCSharp(sources));
        return new CSharpWorkspace(
            rootPath,
            state,
            CalculateFingerprint(sources));
    }

    public static async Task CreateAsync(
        InMemoryWorkspace workspace,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var rootPath = Path.GetFullPath(workspacePath);
        if (Directory.Exists(rootPath) &&
            Directory.EnumerateFileSystemEntries(rootPath).Any())
        {
            throw new InvalidOperationException(
                $"C# workspace destination '{rootPath}' must be empty.");
        }

        var parent = Path.GetDirectoryName(rootPath) ??
            throw new InvalidOperationException(
                $"C# workspace '{rootPath}' has no parent directory.");
        Directory.CreateDirectory(parent);
        var name = Path.GetFileName(rootPath);
        var stagePath = Path.Combine(
            parent,
            $".{name}.meta-stage-{Guid.NewGuid():N}");
        var output = MetaCSharpWriter.Write(workspace);
        Directory.CreateDirectory(stagePath);
        try
        {
            await WriteSourcesAsync(
                    stagePath,
                    output.Sources,
                    cancellationToken)
                .ConfigureAwait(false);
            var staged = await ReadSourcesAsync(stagePath, cancellationToken)
                .ConfigureAwait(false);
            EnsureEquivalent(
                workspace,
                MetaCSharpReader.Read(new MetaCSharp(staged)));

            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath);
            }

            Directory.Move(stagePath, rootPath);
        }
        finally
        {
            DeleteDirectory(stagePath);
        }
    }

    public async ValueTask<IReadOnlyList<OperationResult>> ExecuteAsync(
        IReadOnlyList<Operation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        cancellationToken.ThrowIfCancellationRequested();
        var applied = InMemoryOperations.Execute(_state, operations);
        var output = MetaCSharpWriter.Write(applied.Workspace);
        await PublishAsync(
                output,
                applied.Workspace,
                cancellationToken)
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
        using var writeLock = WorkspaceWriteLock.Acquire(RootPath);
        var current = await ReadSourcesAsync(RootPath, cancellationToken)
            .ConfigureAwait(false);
        var actualFingerprint = CalculateFingerprint(current);
        if (!string.Equals(
                actualFingerprint,
                _fingerprint,
                StringComparison.Ordinal))
        {
            throw new WorkspaceConflictException(
                $"C# workspace fingerprint mismatch. Expected '{_fingerprint}', found '{actualFingerprint}'.",
                _fingerprint,
                actualFingerprint);
        }

        var parent = Path.GetDirectoryName(RootPath) ??
            throw new InvalidOperationException(
                $"C# workspace '{RootPath}' has no parent directory.");
        var name = Path.GetFileName(RootPath);
        var token = Guid.NewGuid().ToString("N");
        var stagePath = Path.Combine(parent, $".{name}.meta-stage-{token}");
        var backupPath = Path.Combine(parent, $".{name}.meta-backup-{token}");
        Directory.CreateDirectory(stagePath);
        Directory.CreateDirectory(backupPath);

        try
        {
            await WriteSourcesAsync(
                    stagePath,
                    output.Sources,
                    cancellationToken)
                .ConfigureAwait(false);
            var stagedSources = await ReadSourcesAsync(
                    stagePath,
                    cancellationToken)
                .ConfigureAwait(false);
            var stagedState = MetaCSharpReader.Read(
                new MetaCSharp(stagedSources));
            EnsureEquivalent(expected, stagedState);

            MoveSources(RootPath, backupPath);
            try
            {
                MoveSources(stagePath, RootPath);
                var publishedSources = await ReadSourcesAsync(
                        RootPath,
                        cancellationToken)
                    .ConfigureAwait(false);
                var publishedState = MetaCSharpReader.Read(
                    new MetaCSharp(publishedSources));
                EnsureEquivalent(expected, publishedState);
                _fingerprint = CalculateFingerprint(publishedSources);
            }
            catch
            {
                DeleteSources(RootPath);
                MoveSources(backupPath, RootPath);
                throw;
            }
        }
        finally
        {
            DeleteDirectory(stagePath);
            DeleteDirectory(backupPath);
        }
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadSourcesAsync(
        string rootPath,
        CancellationToken cancellationToken)
    {
        var sources = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateSources(rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(rootPath, path)
                .Replace(Path.DirectorySeparatorChar, '/');
            var contents = await File.ReadAllTextAsync(path, cancellationToken)
                .ConfigureAwait(false);
            if (!sources.TryAdd(relativePath, contents))
            {
                throw new InvalidDataException(
                    $"C# workspace contains source paths that differ only by case: '{relativePath}'.");
            }
        }

        if (sources.Count == 0)
        {
            throw new InvalidDataException(
                $"C# workspace '{rootPath}' contains no C# source files.");
        }

        return sources;
    }

    private static async Task WriteSourcesAsync(
        string rootPath,
        IReadOnlyDictionary<string, string> sources,
        CancellationToken cancellationToken)
    {
        foreach (var source in sources.OrderBy(
                     item => item.Key,
                     StringComparer.Ordinal))
        {
            var path = ResolveOwnedPath(rootPath, source.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(
                    path,
                    source.Value,
                    Utf8NoBom,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static void MoveSources(string sourceRoot, string targetRoot)
    {
        foreach (var sourcePath in EnumerateSources(sourceRoot))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            var targetPath = ResolveOwnedPath(targetRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Move(sourcePath, targetPath, overwrite: false);
        }
    }

    private static void DeleteSources(string rootPath)
    {
        foreach (var path in EnumerateSources(rootPath))
        {
            File.Delete(path);
        }
    }

    private static IEnumerable<string> EnumerateSources(string rootPath) =>
        Directory.Exists(rootPath)
            ? Directory.EnumerateFiles(
                    rootPath,
                    "*.cs",
                    SearchOption.AllDirectories)
                .OrderBy(
                    path => Path.GetRelativePath(rootPath, path),
                    StringComparer.Ordinal)
            : [];

    private static string ResolveOwnedPath(
        string rootPath,
        string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException(
                $"C# workspace source path '{relativePath}' must be relative.");
        }

        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!path.StartsWith(
                root,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"C# workspace source path '{relativePath}' escapes its workspace.");
        }

        return path;
    }

    private static string CalculateFingerprint(
        IReadOnlyDictionary<string, string> sources)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var source in sources.OrderBy(
                     item => item.Key,
                     StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(source.Key));
            hash.AppendData([0]);
            hash.AppendData(Encoding.UTF8.GetBytes(source.Value));
            hash.AppendData([0]);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void EnsureEquivalent(
        InMemoryWorkspace expected,
        InMemoryWorkspace actual)
    {
        var difference = InMemoryWorkspaceComparer.FindDifference(
            expected,
            actual);
        if (difference != null)
        {
            throw new InvalidDataException(
                $"Published C# workspace changed metadata semantics. {difference}");
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
