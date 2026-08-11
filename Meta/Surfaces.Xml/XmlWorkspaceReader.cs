using System.Xml.Linq;
using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Surfaces;
using Meta.Surfaces.Configuration;
using MetaWorkspaceGenerated = Meta.Surfaces.Configuration.MetaWorkspace;

namespace Meta.Surfaces.Xml;

public static class XmlWorkspaceReader
{
    private const int SupportedContractMajorVersion = 1;
    private const int SupportedContractMinorVersion = 0;
    private const string ModelFileName = "model.xml";
    private const string DefaultInstanceDirectoryName = "instances";
    private const int LoadRetryCount = 3;
    private static readonly TimeSpan LoadRetryDelay = TimeSpan.FromMilliseconds(50);

    public static Task<OpenedXmlWorkspace> OpenAsync(
        string workspaceRootPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(workspaceRootPath))
        {
            throw new ArgumentException(
                "Workspace path must not be empty.",
                nameof(workspaceRootPath));
        }

        var rootPath = ResolveWorkspaceRoot(Path.GetFullPath(workspaceRootPath));
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var configuration = ReadConfiguration(rootPath);
                var modelPath = ResolveModelPath(rootPath, configuration);
                if (string.IsNullOrWhiteSpace(modelPath))
                {
                    throw new FileNotFoundException(
                        $"Could not find {ModelFileName} in '{rootPath}'.");
                }

                var model = ModelXmlCodec.LoadFromPath(modelPath);
                var loadedInstance = ReadInstance(
                    rootPath,
                    configuration,
                    model);
                var state = new InMemoryWorkspace(model, loadedInstance.Instance);
                EnsureValid(state, rootPath);
                var fingerprint = XmlWorkspaceFingerprint.Calculate(
                    state,
                    configuration,
                    loadedInstance.Layout);
                return Task.FromResult(new OpenedXmlWorkspace(
                    rootPath,
                    state,
                    configuration,
                    loadedInstance.Layout,
                    fingerprint));
            }
            catch (FileNotFoundException) when (
                attempt < LoadRetryCount - 1 &&
                ShouldRetry(rootPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(LoadRetryDelay);
            }
            catch (DirectoryNotFoundException) when (
                attempt < LoadRetryCount - 1 &&
                ShouldRetry(rootPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(LoadRetryDelay);
            }
        }
    }

    private static string ResolveWorkspaceRoot(string inputPath)
    {
        if (!string.Equals(
                Path.GetFileName(inputPath),
                DefaultInstanceDirectoryName,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            return inputPath;
        }

        return Directory.GetParent(inputPath)?.FullName ?? inputPath;
    }

    private static bool ShouldRetry(string rootPath) =>
        Directory.Exists(rootPath) ||
        File.Exists(Path.Combine(rootPath, WorkspaceMetaFile.FileName));

    private static void EnsureValid(
        InMemoryWorkspace state,
        string rootPath)
    {
        var diagnostics = WorkspaceValidator.Validate(
            state.Model,
            state.Instance);
        if (!diagnostics.HasErrors)
        {
            return;
        }

        var errors = diagnostics.Issues
            .Where(issue => issue.Severity == IssueSeverity.Error)
            .Take(5)
            .Select(issue =>
                $"{issue.Code} {issue.Location} - {issue.Message}");
        throw new InvalidDataException(
            $"Workspace '{rootPath}' is invalid. " +
            string.Join(" | ", errors));
    }

    private static MetaWorkspaceGenerated ReadConfiguration(string rootPath)
    {
        var metadata = WorkspaceMetaFile.Read(rootPath);
        if (!string.Equals(metadata.Representation, "xml", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Workspace metadata in '{rootPath}' selects '{metadata.Representation}', not the XML surface.");
        }

        var configuration = MetaWorkspaceGenerated.Normalize(
            metadata.Configuration,
            Path.Combine(rootPath, WorkspaceMetaFile.FileName));
        ValidateContractVersion(
            configuration,
            Path.Combine(rootPath, WorkspaceMetaFile.FileName));
        return configuration;
    }

    private static void ValidateContractVersion(
        MetaWorkspaceGenerated configuration,
        string workspacePath)
    {
        var contractVersion = MetaWorkspaceGenerated.GetContractVersion(configuration);
        if (!MetaWorkspaceGenerated.TryParseContractVersion(contractVersion, out var major, out _))
        {
            throw new InvalidDataException(
                $"Workspace config '{workspacePath}' has invalid contractVersion '{contractVersion}'.");
        }

        if (major != SupportedContractMajorVersion)
        {
            throw new InvalidDataException(
                $"Unsupported contract major version '{major}' in '{workspacePath}'. Tool supports '{SupportedContractMajorVersion}.{SupportedContractMinorVersion}'.");
        }
    }

    private static string ResolveModelPath(
        string rootPath,
        MetaWorkspaceGenerated configuration)
    {
        var configuredPath = WorkspacePathResolver.ResolvePathFromWorkspaceRoot(
            rootPath,
            MetaWorkspaceGenerated.GetModelFile(configuration));
        return new[]
        {
            configuredPath,
            Path.Combine(rootPath, ModelFileName),
        }.FirstOrDefault(File.Exists) ?? string.Empty;
    }

    private static LoadedInstance ReadInstance(
        string rootPath,
        MetaWorkspaceGenerated configuration,
        GenericModel model)
    {
        var directoryPath = WorkspacePathResolver.ResolvePathFromWorkspaceRoot(
            rootPath,
            MetaWorkspaceGenerated.GetInstanceDir(configuration));
        if (!Directory.Exists(directoryPath))
        {
            return EmptyInstance(model);
        }

        var shardFiles = Directory.GetFiles(directoryPath, "*.xml")
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return shardFiles.Count == 0
            ? EmptyInstance(model)
            : ReadShards(shardFiles, model);
    }

    private static LoadedInstance ReadShards(
        IReadOnlyCollection<string> shardFiles,
        GenericModel model)
    {
        var instance = new GenericInstance { ModelName = model.Name };
        var layout = new XmlWorkspaceLayout();
        foreach (var path in shardFiles)
        {
            var loadedRecords = InstanceXmlCodec.MergeDocumentAndGetRecordIdentities(
                instance,
                XDocument.Load(path, LoadOptions.None),
                model);
            var shardFileName = Path.GetFileName(path);
            foreach (var record in loadedRecords)
            {
                layout.AssignShard(record.EntityName, record.RecordId, shardFileName);
            }
        }

        return new LoadedInstance(instance, layout);
    }

    private static LoadedInstance EmptyInstance(GenericModel model) =>
        new(
            new GenericInstance { ModelName = model.Name },
            new XmlWorkspaceLayout());

    private sealed record LoadedInstance(
        GenericInstance Instance,
        XmlWorkspaceLayout Layout);
}
