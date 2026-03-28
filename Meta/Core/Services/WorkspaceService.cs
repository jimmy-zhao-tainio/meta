using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Meta.Core.Domain;
using Meta.Core.Serialization;
using Meta.Core.WorkspaceConfig;
using MetaWorkspaceGenerated = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace Meta.Core.Services;

public sealed class WorkspaceService : IWorkspaceService
{
    private const int SupportedContractMajorVersion = 1;
    private const int SupportedContractMinorVersion = 0;
    private const string WorkspaceXmlFileName = "workspace.xml";
    private const string ModelFileName = "model.xml";
    private const string DefaultInstanceDirectoryName = "instances";
    private const int LoadRetryCount = 3;
    private static readonly TimeSpan LoadRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public Task<Workspace> LoadAsync(
        string workspaceRootPath,
        bool searchUpward = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(workspaceRootPath))
        {
            throw new ArgumentException("Workspace path must not be empty.", nameof(workspaceRootPath));
        }

        var absoluteInputPath = Path.GetFullPath(workspaceRootPath);
        var paths = searchUpward
            ? DiscoverWorkspacePaths(absoluteInputPath)
            : ResolveWorkspacePathsFromRoot(absoluteInputPath);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var workspaceConfig = ReadWorkspaceConfig(paths.WorkspaceRootPath);

                var modelPath = ResolveModelPath(paths.WorkspaceRootPath, workspaceConfig);
                if (string.IsNullOrWhiteSpace(modelPath))
                {
                    throw new FileNotFoundException(
                        $"Could not find {ModelFileName} in '{paths.WorkspaceRootPath}'.");
                }

                var model = ReadModel(modelPath);
                var instance = ReadInstance(paths.WorkspaceRootPath, workspaceConfig, model);

                var workspace = new Workspace
                {
                    WorkspaceRootPath = paths.WorkspaceRootPath,
                    MetadataRootPath = paths.WorkspaceRootPath,
                    WorkspaceConfig = workspaceConfig,
                    Model = model,
                    Instance = instance,
                    IsDirty = false,
                };

                return Task.FromResult(workspace);
            }
            catch (FileNotFoundException) when (attempt < LoadRetryCount - 1 && ShouldRetryLoad(paths.WorkspaceRootPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(LoadRetryDelay);
            }
            catch (DirectoryNotFoundException) when (attempt < LoadRetryCount - 1 && ShouldRetryLoad(paths.WorkspaceRootPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(LoadRetryDelay);
            }
        }
    }

    public Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        return SaveAsync(workspace, expectedFingerprint: null, cancellationToken);
    }

    public async Task SaveAsync(
        Workspace workspace,
        string? expectedFingerprint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        if (string.IsNullOrWhiteSpace(workspace.WorkspaceRootPath))
        {
            throw new InvalidOperationException("WorkspaceRootPath is required before save.");
        }

        if (workspace.Model == null)
        {
            throw new InvalidOperationException("Workspace model must be initialized.");
        }

        if (workspace.Instance == null)
        {
            throw new InvalidOperationException("Workspace instance must be initialized.");
        }

        var diagnostics = new ValidationService().Validate(workspace);
        workspace.Diagnostics = diagnostics;
        if (diagnostics.HasErrors)
        {
            var preview = diagnostics.Issues
                .Where(issue => issue.Severity == IssueSeverity.Error)
                .Take(5)
                .Select(issue => $"{issue.Code} {issue.Location} - {issue.Message}");
            throw new InvalidOperationException(
                "Workspace validation failed before save: " + string.Join(" | ", preview));
        }

        var workspaceRoot = Path.GetFullPath(workspace.WorkspaceRootPath);
        var workspaceConfigPath = Path.Combine(workspaceRoot, WorkspaceXmlFileName);
        var workspaceConfig = NormalizeWorkspaceConfig(workspace.WorkspaceConfig, workspaceConfigPath);
        using var writeLock = WorkspaceWriteLock.Acquire(workspaceRoot);

        if (!string.IsNullOrWhiteSpace(expectedFingerprint))
        {
            var currentFingerprint = await TryCalculateCurrentFingerprintAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
            var normalizedCurrent = currentFingerprint ?? string.Empty;
            if (!string.Equals(normalizedCurrent, expectedFingerprint.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new WorkspaceConflictException(
                    $"Workspace fingerprint mismatch. Expected '{expectedFingerprint}', found '{normalizedCurrent}'.",
                    expectedFingerprint.Trim(),
                    normalizedCurrent);
            }
        }

        var modelPath = ResolvePathFromWorkspaceRoot(workspaceRoot, MetaWorkspaceGenerated.GetModelFile(workspaceConfig));
        var instanceDirectoryPath = ResolvePathFromWorkspaceRoot(workspaceRoot, MetaWorkspaceGenerated.GetInstanceDir(workspaceConfig));
        EnsurePathUnderWorkspaceRoot(modelPath, workspaceRoot, "ModelFilePath");
        EnsurePathUnderWorkspaceRoot(instanceDirectoryPath, workspaceRoot, "InstanceDirPath");

        var workspaceConfigBackupPath = workspaceConfigPath + ".__backup." + Guid.NewGuid().ToString("N");
        var hadExistingWorkspaceConfig = File.Exists(workspaceConfigPath);

        BackupWorkspaceConfigIfPresent(workspaceConfigPath, workspaceConfigBackupPath);
        try
        {
            WriteWorkspaceConfigToFile(workspaceConfig, workspaceConfigPath);
            SaveByStagingConfiguredPaths(workspaceRoot, modelPath, instanceDirectoryPath, workspace);

            DeleteIfExists(workspaceConfigBackupPath);
        }
        catch
        {
            RestoreWorkspaceConfigBackup(workspaceConfigPath, workspaceConfigBackupPath, hadExistingWorkspaceConfig);
            throw;
        }
        finally
        {
            DeleteIfExists(workspaceConfigBackupPath);
        }

        workspace.WorkspaceRootPath = workspaceRoot;
        workspace.MetadataRootPath = workspaceRoot;
        workspace.WorkspaceConfig = workspaceConfig;
        workspace.IsDirty = false;
    }

    private async Task<string?> TryCalculateCurrentFingerprintAsync(
        string workspaceRootPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentWorkspace = await LoadAsync(workspaceRootPath, searchUpward: false, cancellationToken).ConfigureAwait(false);
            return CalculateHash(currentWorkspace);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public string CalculateHash(Workspace workspace)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        using var sha256 = SHA256.Create();
        var workspaceConfigCanonical = SerializeWorkspaceConfig(
            NormalizeWorkspaceConfig(workspace.WorkspaceConfig, WorkspaceXmlFileName));
        var modelCanonical = SerializeXml(BuildModelDocument(workspace.Model), indented: false);
        var shardCanonicalPayload = BuildShardCanonicalPayload(workspace);
        var payload = workspaceConfigCanonical + "\n---\n" + modelCanonical + "\n---\n" + shardCanonicalPayload;
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static WorkspacePaths DiscoverWorkspacePaths(string inputPath)
    {
        var initialDirectory = Directory.Exists(inputPath)
            ? inputPath
            : Path.GetDirectoryName(inputPath) ?? inputPath;
        var current = Path.GetFullPath(initialDirectory);

        while (!string.IsNullOrWhiteSpace(current))
        {
            var workspaceXmlPath = Path.Combine(current, WorkspaceXmlFileName);

            if (File.Exists(workspaceXmlPath))
            {
                return new WorkspacePaths(current, current);
            }

            if (HasRootLevelWorkspaceData(current))
            {
                return new WorkspacePaths(current, current);
            }

            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        var fallbackRoot = Path.GetFullPath(initialDirectory);
        return new WorkspacePaths(fallbackRoot, fallbackRoot);
    }

    private static WorkspacePaths ResolveWorkspacePathsFromRoot(string inputPath)
    {
        var rootPath = Path.GetFullPath(inputPath);
        if (string.Equals(
                Path.GetFileName(rootPath),
                DefaultInstanceDirectoryName,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            var parent = Directory.GetParent(rootPath)?.FullName ?? rootPath;
            return new WorkspacePaths(parent, parent);
        }

        return new WorkspacePaths(rootPath, rootPath);
    }

    private static bool HasRootLevelWorkspaceData(string workspaceRootPath)
    {
        return File.Exists(Path.Combine(workspaceRootPath, ModelFileName)) ||
               Directory.Exists(Path.Combine(workspaceRootPath, DefaultInstanceDirectoryName));
    }

    private static bool ShouldRetryLoad(string workspaceRootPath)
    {
        var absoluteRoot = Path.GetFullPath(workspaceRootPath);
        return Directory.Exists(absoluteRoot) || File.Exists(Path.Combine(absoluteRoot, WorkspaceXmlFileName));
    }

    private static MetaWorkspaceGenerated ReadWorkspaceConfig(string workspaceRootPath)
    {
        var workspaceXmlPath = Path.Combine(workspaceRootPath, WorkspaceXmlFileName);
        if (File.Exists(workspaceXmlPath))
        {
            var generated = MetaWorkspaceGenerated.LoadFromXml(workspaceXmlPath);
            var normalizedFromXml = NormalizeWorkspaceConfig(generated, workspaceXmlPath);
            ValidateContractVersion(normalizedFromXml, workspaceXmlPath);
            return normalizedFromXml;
        }

        return MetaWorkspaceGenerated.CreateDefault();
    }

    private static MetaWorkspaceGenerated NormalizeWorkspaceConfig(MetaWorkspaceGenerated? workspaceConfig, string sourcePath)
    {
        return MetaWorkspaceGenerated.Normalize(workspaceConfig, sourcePath);
    }

    private static void ValidateContractVersion(MetaWorkspaceGenerated workspaceConfig, string workspaceFilePath)
    {
        var contractVersion = MetaWorkspaceGenerated.GetContractVersion(workspaceConfig);
        if (!MetaWorkspaceGenerated.TryParseContractVersion(contractVersion, out var major, out _))
        {
            throw new InvalidDataException(
                $"Workspace config '{workspaceFilePath}' has invalid contractVersion '{contractVersion}'.");
        }

        if (major != SupportedContractMajorVersion)
        {
            throw new InvalidDataException(
                $"Unsupported contract major version '{major}' in '{workspaceFilePath}'. Tool supports '{SupportedContractMajorVersion}.{SupportedContractMinorVersion}'.");
        }
    }

    private static void WriteWorkspaceConfigToFile(MetaWorkspaceGenerated workspaceConfig, string workspaceFilePath)
    {
        var normalizedWorkspaceConfig = NormalizeWorkspaceConfig(workspaceConfig, workspaceFilePath);
        var document = MetaWorkspaceGenerated.BuildDocument(normalizedWorkspaceConfig);
        WriteXmlToFile(document, workspaceFilePath, indented: true);
    }

    private static string ResolveModelPath(string workspaceRootPath, MetaWorkspaceGenerated workspaceConfig)
    {
        var workspaceModelPath = ResolvePathFromWorkspaceRoot(
            workspaceRootPath,
            MetaWorkspaceGenerated.GetModelFile(workspaceConfig));
        var candidatePaths = new[]
        {
            workspaceModelPath,
            Path.Combine(workspaceRootPath, ModelFileName),
        };

        return FirstExistingPath(candidatePaths);
    }

    private static GenericInstance ReadInstance(
        string workspaceRootPath,
        MetaWorkspaceGenerated workspaceConfig,
        GenericModel model)
    {
        var shardDirectoryPath = ResolvePathFromWorkspaceRoot(
            workspaceRootPath,
            MetaWorkspaceGenerated.GetInstanceDir(workspaceConfig));
        var hasShardDirectory = Directory.Exists(shardDirectoryPath);
        if (Directory.Exists(shardDirectoryPath))
        {
            var shardFiles = Directory.GetFiles(shardDirectoryPath, "*.xml")
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (shardFiles.Count > 0)
            {
                return ReadInstanceShards(shardFiles, model);
            }
        }

        if (hasShardDirectory)
        {
            return new GenericInstance
            {
                ModelName = model.Name ?? string.Empty,
            };
        }

        return new GenericInstance
        {
            ModelName = model.Name ?? string.Empty,
        };
    }

    private static GenericInstance ReadInstanceShards(
        IReadOnlyCollection<string> shardFiles,
        GenericModel model)
    {
        return InstanceXmlCodec.LoadFromPaths(shardFiles, model);
    }

    private static void WriteInstanceShards(Workspace workspace, string instanceDirectoryPath)
    {
        var modelName = !string.IsNullOrWhiteSpace(workspace.Model.Name)
            ? workspace.Model.Name
            : workspace.Instance.ModelName;
        var rootName = string.IsNullOrWhiteSpace(modelName) ? "MetadataModel" : modelName;
        var shardPlans = BuildInstanceShardWritePlans(workspace, persistAssignments: true);

        if (shardPlans.Count == 0)
        {
            DeleteDirectoryIfExists(instanceDirectoryPath);
            return;
        }

        Directory.CreateDirectory(instanceDirectoryPath);

        var expectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var shardPlan in shardPlans)
        {
            var shardPath = Path.Combine(instanceDirectoryPath, shardPlan.ShardFileName);
            var shardDocument = BuildInstanceShardDocument(workspace, rootName, shardPlan.EntityName, shardPlan.Records);
            WriteXmlToFile(shardDocument, shardPath, indented: true);
            expectedPaths.Add(Path.GetFullPath(shardPath));
        }

        foreach (var existingPath in Directory.GetFiles(instanceDirectoryPath, "*.xml"))
        {
            var absolutePath = Path.GetFullPath(existingPath);
            if (!expectedPaths.Contains(absolutePath))
            {
                File.Delete(existingPath);
            }
        }
    }

    private static XDocument BuildInstanceShardDocument(
        Workspace workspace,
        string rootName,
        string entityName,
        IReadOnlyCollection<GenericRecord>? recordsOverride = null)
    {
        var records = recordsOverride ??
                      (workspace.Instance.RecordsByEntity.TryGetValue(entityName, out var entityRecords)
                          ? entityRecords
                          : new List<GenericRecord>());
        return InstanceXmlCodec.BuildEntityDocument(workspace.Model, entityName, records, rootName);
    }

    private static string BuildShardCanonicalPayload(Workspace workspace)
    {
        var modelName = !string.IsNullOrWhiteSpace(workspace.Model.Name)
            ? workspace.Model.Name
            : workspace.Instance.ModelName;
        var rootName = string.IsNullOrWhiteSpace(modelName) ? "MetadataModel" : modelName;
        var parts = new List<string>();
        foreach (var shardPlan in BuildInstanceShardWritePlans(workspace, persistAssignments: false))
        {
            var shardDocument = BuildInstanceShardDocument(
                workspace,
                rootName,
                shardPlan.EntityName,
                shardPlan.Records);
            var shardCanonical = SerializeXml(shardDocument, indented: false);
            parts.Add(shardPlan.ShardFileName + "\n" + shardCanonical);
        }

        return string.Join("\n---\n", parts);
    }

    private static IReadOnlyList<InstanceShardWritePlan> BuildInstanceShardWritePlans(
        Workspace workspace,
        bool persistAssignments)
    {
        var plans = new List<InstanceShardWritePlan>();
        foreach (var entityName in GetOrderedEntityNames(workspace))
        {
            plans.AddRange(BuildEntityShardWritePlans(workspace, entityName));
        }

        plans = plans
            .OrderBy(plan => plan.EntityName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(plan => plan.ShardFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            plan.ShardFileName = MakeUniqueShardFileName(plan.EntityName, plan.ShardFileName, usedFileNames);
            if (persistAssignments)
            {
                foreach (var record in plan.Records)
                {
                    record.SourceShardFileName = plan.ShardFileName;
                }
            }
        }

        return plans;
    }

    private static IReadOnlyList<InstanceShardWritePlan> BuildEntityShardWritePlans(Workspace workspace, string entityName)
    {
        var records = workspace.Instance.RecordsByEntity.TryGetValue(entityName, out var entityRecords)
            ? entityRecords
            : new List<GenericRecord>();
        var orderedRecords = records
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (orderedRecords.Count == 0)
        {
            return Array.Empty<InstanceShardWritePlan>();
        }

        var defaultShardFileName = NormalizeShardFileName(null, entityName);
        var assignedNames = orderedRecords
            .Select(record => NormalizeLoadedShardFileName(record.SourceShardFileName, entityName))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (assignedNames.Count == 0)
        {
            assignedNames.Add(defaultShardFileName);
        }

        var primaryShardFileName = assignedNames[0];
        var recordsByShard = new Dictionary<string, List<GenericRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in orderedRecords)
        {
            var shardFileName = NormalizeLoadedShardFileName(record.SourceShardFileName, entityName);
            if (string.IsNullOrWhiteSpace(shardFileName))
            {
                shardFileName = primaryShardFileName;
            }

            if (!recordsByShard.TryGetValue(shardFileName, out var shardRecords))
            {
                shardRecords = new List<GenericRecord>();
                recordsByShard[shardFileName] = shardRecords;
            }

            record.SourceShardFileName = shardFileName;
            shardRecords.Add(record);
        }


        return recordsByShard
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => new InstanceShardWritePlan(entityName, item.Key, item.Value))
            .ToList();
    }

    private static string NormalizeLoadedShardFileName(string? shardFileName, string entityName)
    {
        var trimmed = (shardFileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return NormalizeShardFileName(trimmed, entityName);
    }

    private static string NormalizeShardFileName(string? shardFileName, string entityName)
    {
        var trimmed = (shardFileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return entityName + ".xml";
        }

        var leafName = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(leafName))
        {
            return entityName + ".xml";
        }

        if (!leafName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return leafName + ".xml";
        }

        return leafName;
    }

    private static string MakeUniqueShardFileName(
        string entityName,
        string candidate,
        ISet<string> usedFileNames)
    {
        var normalized = NormalizeShardFileName(candidate, entityName);
        if (usedFileNames.Add(normalized))
        {
            return normalized;
        }

        var baseName = Path.GetFileNameWithoutExtension(normalized);
        var extension = Path.GetExtension(normalized);
        var disambiguatedBase = entityName + "." + baseName;
        var disambiguated = disambiguatedBase + extension;
        var suffix = 2;
        while (!usedFileNames.Add(disambiguated))
        {
            disambiguated = disambiguatedBase + "." + suffix.ToString(CultureInfo.InvariantCulture) + extension;
            suffix++;
        }

        return disambiguated;
    }

    private sealed class InstanceShardWritePlan
    {
        public InstanceShardWritePlan(string entityName, string shardFileName, List<GenericRecord> records)
        {
            EntityName = entityName;
            ShardFileName = shardFileName;
            Records = records;
        }

        public string EntityName { get; }
        public string ShardFileName { get; set; }
        public List<GenericRecord> Records { get; }
    }

    private static IReadOnlyList<string> GetOrderedEntityNames(Workspace workspace)
    {
        var entityNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in workspace.Model.Entities)
        {
            if (!string.IsNullOrWhiteSpace(entity.Name))
            {
                entityNames.Add(entity.Name);
            }
        }

        foreach (var entityName in workspace.Instance.RecordsByEntity.Keys)
        {
            if (!string.IsNullOrWhiteSpace(entityName))
            {
                entityNames.Add(entityName);
            }
        }

        return entityNames.ToList();
    }

    private static bool IsValidIdentity(string? value)
    {
        return !string.IsNullOrWhiteSpace(value?.Trim());
    }

    private static void EnsurePathUnderWorkspaceRoot(string path, string workspaceRootPath, string workspaceConfigFieldName)
    {
        if (!IsPathWithinRoot(path, workspaceRootPath))
        {
            throw new InvalidDataException(
                $"Workspace config '{workspaceConfigFieldName}' must resolve under the workspace root. Resolved path '{path}' is outside '{workspaceRootPath}'.");
        }
    }

    private static void SaveByStagingConfiguredPaths(
        string workspaceRoot,
        string modelPath,
        string instanceDirectoryPath,
        Workspace workspace)
    {
        var stagingRootPath = Path.Combine(
            workspaceRoot,
            ".__workspace-staging." + Guid.NewGuid().ToString("N"));
        var backupRootPath = Path.Combine(
            workspaceRoot,
            ".__workspace-backup." + Guid.NewGuid().ToString("N"));
        var stagedModelPath = MapPathToStagingRoot(workspaceRoot, stagingRootPath, modelPath);
        var stagedInstanceDirectoryPath = MapPathToStagingRoot(workspaceRoot, stagingRootPath, instanceDirectoryPath);
        var backupModelPath = MapPathToStagingRoot(workspaceRoot, backupRootPath, modelPath);
        var backupInstanceDirectoryPath = MapPathToStagingRoot(workspaceRoot, backupRootPath, instanceDirectoryPath);

        Directory.CreateDirectory(stagingRootPath);
        try
        {
            WriteXmlToFile(BuildModelDocument(workspace.Model), stagedModelPath, indented: true);
            WriteInstanceShards(workspace, stagedInstanceDirectoryPath);
            ReplaceFileFromStaging(modelPath, stagedModelPath, backupModelPath);
            ReplaceDirectoryFromStaging(instanceDirectoryPath, stagedInstanceDirectoryPath, backupInstanceDirectoryPath);
        }
        catch
        {
            RestoreFileFromBackup(modelPath, backupModelPath);
            RestoreDirectoryFromBackup(instanceDirectoryPath, backupInstanceDirectoryPath);
            throw;
        }
        finally
        {
            DeleteDirectoryIfExists(stagingRootPath);
            DeleteDirectoryIfExists(backupRootPath);
        }
    }

    private static string MapPathToStagingRoot(
        string rootPath,
        string stagingRootPath,
        string resolvedFinalPath)
    {
        var relative = Path.GetRelativePath(rootPath, resolvedFinalPath);
        return Path.GetFullPath(Path.Combine(stagingRootPath, relative));
    }

    private static void ReplaceFileFromStaging(string finalPath, string stagedPath, string backupPath)
    {
        DeleteDirectoryIfExists(finalPath);
        DeleteIfExists(backupPath);

        if (File.Exists(finalPath))
        {
            var backupDirectory = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrWhiteSpace(backupDirectory))
            {
                Directory.CreateDirectory(backupDirectory);
            }

            File.Move(finalPath, backupPath);
        }

        if (!File.Exists(stagedPath))
        {
            DeleteIfExists(finalPath);
            return;
        }

        var finalDirectory = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrWhiteSpace(finalDirectory))
        {
            Directory.CreateDirectory(finalDirectory);
        }

        File.Move(stagedPath, finalPath);
    }

    private static void ReplaceDirectoryFromStaging(string finalPath, string stagedPath, string backupPath)
    {
        DeleteIfExists(finalPath);
        DeleteDirectoryIfExists(backupPath);

        if (Directory.Exists(finalPath))
        {
            var backupDirectory = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrWhiteSpace(backupDirectory))
            {
                Directory.CreateDirectory(backupDirectory);
            }

            Directory.Move(finalPath, backupPath);
        }
        else if (File.Exists(finalPath))
        {
            File.Delete(finalPath);
        }

        if (!Directory.Exists(stagedPath))
        {
            DeleteDirectoryIfExists(finalPath);
            return;
        }

        var finalDirectory = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrWhiteSpace(finalDirectory))
        {
            Directory.CreateDirectory(finalDirectory);
        }

        Directory.Move(stagedPath, finalPath);
    }

    private static void RestoreFileFromBackup(string finalPath, string backupPath)
    {
        if (File.Exists(finalPath))
        {
            File.Delete(finalPath);
        }

        if (!File.Exists(backupPath))
        {
            return;
        }

        var finalDirectory = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrWhiteSpace(finalDirectory))
        {
            Directory.CreateDirectory(finalDirectory);
        }

        File.Move(backupPath, finalPath);
    }

    private static void RestoreDirectoryFromBackup(string finalPath, string backupPath)
    {
        if (Directory.Exists(finalPath))
        {
            Directory.Delete(finalPath, recursive: true);
        }

        if (!Directory.Exists(backupPath))
        {
            return;
        }

        var finalDirectory = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrWhiteSpace(finalDirectory))
        {
            Directory.CreateDirectory(finalDirectory);
        }

        Directory.Move(backupPath, finalPath);
    }

    private static void BackupWorkspaceConfigIfPresent(string workspaceConfigPath, string backupPath)
    {
        if (File.Exists(workspaceConfigPath))
        {
            File.Copy(workspaceConfigPath, backupPath, overwrite: true);
        }
    }

    private static void RestoreWorkspaceConfigBackup(
        string workspaceConfigPath,
        string backupPath,
        bool hadExistingWorkspaceConfig)
    {
        if (hadExistingWorkspaceConfig && File.Exists(backupPath))
        {
            if (!File.Exists(workspaceConfigPath))
            {
                File.Copy(backupPath, workspaceConfigPath, overwrite: true);
            }
            return;
        }

        DeleteIfExists(workspaceConfigPath);
    }

    private static string SerializeWorkspaceConfig(MetaWorkspaceGenerated workspaceConfig)
    {
        return SerializeXml(
            MetaWorkspaceGenerated.BuildDocument(
                NormalizeWorkspaceConfig(workspaceConfig, WorkspaceXmlFileName)),
            indented: false);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string ResolvePathFromWorkspaceRoot(string workspaceRootPath, string path)
    {
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidDataException(
                $"Workspace config path '{path}' must be relative to the workspace root.");
        }

        var resolvedPath = Path.GetFullPath(Path.Combine(workspaceRootPath, normalized));
        var workspaceRoot = Path.GetFullPath(workspaceRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!IsPathWithinRoot(resolvedPath, workspaceRoot))
        {
            throw new InvalidDataException(
                $"Workspace config path '{path}' resolves outside workspace root '{workspaceRoot}'.");
        }

        return resolvedPath;
    }

    private static bool IsPathWithinRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(path, root, comparison))
        {
            return true;
        }

        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSeparator, comparison);
    }

    private static string FirstExistingPath(IEnumerable<string> candidatePaths)
    {
        return candidatePaths.FirstOrDefault(File.Exists) ?? string.Empty;
    }

    private static GenericModel ReadModel(string modelPath)
    {
        return ModelXmlCodec.LoadFromPath(modelPath);
    }

    private static XDocument BuildModelDocument(GenericModel model)
    {
        return ModelXmlCodec.BuildDocument(model);
    }

    private static void WriteXmlToFile(XDocument document, string path, bool indented)
    {
        var xml = SerializeXml(document, indented);
        WriteTextAtomic(path, xml);
    }

    private static string SerializeXml(XDocument document, bool indented)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false,
            Indent = indented,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
        };

        using var stringWriter = new Utf8StringWriter();
        using var xmlWriter = XmlWriter.Create(stringWriter, settings);
        document.Save(xmlWriter);
        xmlWriter.Flush();
        return stringWriter.ToString();
    }

    private static void WriteTextAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
        File.WriteAllText(tempPath, content, Utf8NoBom);

        try
        {
            if (File.Exists(path))
            {
                var backupPath = path + ".bak";
                try
                {
                    File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
                    DeleteIfExists(backupPath);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Delete(path);
                    File.Move(tempPath, path);
                }
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public Utf8StringWriter()
            : base(CultureInfo.InvariantCulture)
        {
        }

        public override Encoding Encoding => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    private sealed class WorkspacePaths
    {
        public WorkspacePaths(string workspaceRootPath, string metadataRootPath)
        {
            WorkspaceRootPath = workspaceRootPath;
            MetadataRootPath = metadataRootPath;
        }

        public string WorkspaceRootPath { get; }
        public string MetadataRootPath { get; }
    }
}







