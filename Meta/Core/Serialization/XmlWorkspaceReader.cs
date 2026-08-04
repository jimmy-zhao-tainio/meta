using System.Xml.Linq;
using Meta.Core.Domain;
using Meta.Core.Services;
using Meta.Core.WorkspaceConfig;
using MetaWorkspaceGenerated = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace Meta.Core.Serialization;

public static class XmlWorkspaceReader
{
    private const int SupportedContractMajorVersion = 1;
    private const int SupportedContractMinorVersion = 0;
    private const string WorkspaceFileName = "workspace.xml";
    private const string ModelFileName = "model.xml";
    private const string DefaultInstanceDirectoryName = "instances";
    private const int LoadRetryCount = 3;
    private static readonly TimeSpan LoadRetryDelay = TimeSpan.FromMilliseconds(50);

    public static Task<OpenedXmlWorkspace> OpenAsync(
        string workspaceRootPath,
        CancellationToken cancellationToken = default,
        WorkspaceLoadOptions? loadOptions = null)
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
                var relationshipColumnAliases = ResolveRelationshipColumnAliases(
                    model,
                    loadOptions);
                var loadedInstance = ReadInstance(
                    rootPath,
                    configuration,
                    model,
                    relationshipColumnAliases);
                var state = new InMemoryWorkspace(model, loadedInstance.Instance);
                var fingerprint = XmlWorkspaceFingerprint.Calculate(
                    state,
                    configuration,
                    loadedInstance.Layout);
                return Task.FromResult(new OpenedXmlWorkspace(
                    rootPath,
                    state,
                    configuration,
                    loadedInstance.Layout,
                    loadOptions,
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
        File.Exists(Path.Combine(rootPath, WorkspaceFileName));

    private static MetaWorkspaceGenerated ReadConfiguration(string rootPath)
    {
        var workspacePath = Path.Combine(rootPath, WorkspaceFileName);
        if (!File.Exists(workspacePath))
        {
            return MetaWorkspaceGenerated.CreateDefault();
        }

        var configuration = MetaWorkspaceGenerated.Normalize(
            MetaWorkspaceGenerated.LoadFromXml(workspacePath),
            workspacePath);
        ValidateContractVersion(configuration, workspacePath);
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
        GenericModel model,
        IReadOnlyList<InstanceRelationshipColumnAlias> relationshipColumnAliases)
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
            : ReadShards(shardFiles, model, relationshipColumnAliases);
    }

    private static LoadedInstance ReadShards(
        IReadOnlyCollection<string> shardFiles,
        GenericModel model,
        IReadOnlyList<InstanceRelationshipColumnAlias> relationshipColumnAliases)
    {
        var loadOptions = relationshipColumnAliases.Count == 0
            ? null
            : new InstanceXmlLoadOptions(relationshipColumnAliases);
        var instance = new GenericInstance { ModelName = model.Name };
        var layout = new XmlWorkspaceLayout();
        foreach (var path in shardFiles)
        {
            var loadedRecords = InstanceXmlCodec.MergeDocumentAndGetRecordIdentities(
                instance,
                XDocument.Load(path, LoadOptions.None),
                model,
                loadOptions);
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

    private static IReadOnlyList<InstanceRelationshipColumnAlias> ResolveRelationshipColumnAliases(
        GenericModel model,
        WorkspaceLoadOptions? loadOptions)
    {
        if (loadOptions == null || loadOptions.RelationshipColumnRecoveries.Count == 0)
        {
            return Array.Empty<InstanceRelationshipColumnAlias>();
        }

        var aliases = new List<InstanceRelationshipColumnAlias>();
        var recoveredColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var recovery in loadOptions.RelationshipColumnRecoveries)
        {
            var sourceEntityName = recovery.SourceEntityName?.Trim() ?? string.Empty;
            var targetEntityName = recovery.TargetEntityName?.Trim() ?? string.Empty;
            var existingColumnName = recovery.ExistingColumnName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sourceEntityName) ||
                string.IsNullOrWhiteSpace(targetEntityName) ||
                string.IsNullOrWhiteSpace(existingColumnName))
            {
                throw new InvalidDataException(
                    "Relationship column recovery requires source entity, target entity, and existing column.");
            }

            var sourceEntity = model.FindEntity(sourceEntityName) ??
                throw new InvalidDataException(
                    $"Relationship column recovery source entity '{sourceEntityName}' does not exist.");
            var relationships = sourceEntity.Relationships
                .Where(item => string.Equals(
                    item.Entity,
                    targetEntityName,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (relationships.Count != 1)
            {
                throw new InvalidDataException(
                    relationships.Count == 0
                        ? $"Relationship column recovery target '{sourceEntityName}->{targetEntityName}' does not exist."
                        : $"Relationship column recovery target '{sourceEntityName}->{targetEntityName}' is ambiguous.");
            }

            var recoveryKey = sourceEntity.Name + "\u001f" + existingColumnName;
            if (!recoveredColumns.Add(recoveryKey))
            {
                throw new InvalidDataException(
                    $"Relationship column recovery for '{sourceEntity.Name}.{existingColumnName}' was specified more than once.");
            }

            aliases.Add(new InstanceRelationshipColumnAlias(
                sourceEntity.Name,
                existingColumnName,
                relationships[0].GetColumnName()));
        }

        return aliases;
    }

    private sealed record LoadedInstance(
        GenericInstance Instance,
        XmlWorkspaceLayout Layout);
}
