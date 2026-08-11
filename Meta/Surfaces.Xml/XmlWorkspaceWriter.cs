using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Surfaces;
using MetaWorkspaceGenerated = Meta.Surfaces.Configuration.MetaWorkspace;

namespace Meta.Surfaces.Xml;

public static class XmlWorkspaceWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task WriteNewAsync(
        InMemoryWorkspace workspace,
        string workspaceRootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (string.IsNullOrWhiteSpace(workspaceRootPath))
        {
            throw new ArgumentException("Workspace root path is required.", nameof(workspaceRootPath));
        }

        EnsureValid(workspace);
        await WriteValidatedAsync(
                workspace,
                workspaceRootPath,
                MetaWorkspaceGenerated.CreateDefault(),
                new XmlWorkspaceLayout(),
                beforeWrite: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task WriteMergedAsync(
        InMemoryWorkspace workspace,
        string workspaceRootPath,
        IReadOnlyList<OpenedXmlWorkspace> sourceWorkspaces,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(sourceWorkspaces);
        if (string.IsNullOrWhiteSpace(workspaceRootPath))
        {
            throw new ArgumentException(
                "Workspace root path is required.",
                nameof(workspaceRootPath));
        }

        EnsureValid(workspace);
        var mergedLayout = XmlWorkspaceMergeLayout.Build(
            sourceWorkspaces
                .Select(source => new XmlWorkspaceMergeSource(
                    source.RootPath,
                    source.State,
                    source.Configuration,
                    source.Layout))
                .ToArray());
        await WriteValidatedAsync(
                workspace,
                workspaceRootPath,
                mergedLayout.Configuration,
                mergedLayout.Layout,
                beforeWrite: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task WriteAsync(
        OpenedXmlWorkspace workspace,
        InMemoryWorkspace candidate,
        IReadOnlyList<OperationResult> operationResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(operationResults);
        EnsureValid(candidate);

        var configuration = MetaWorkspaceGenerated.Normalize(
            workspace.Configuration,
            Path.Combine(workspace.RootPath, WorkspaceMetaFile.FileName));
        var layout = workspace.Layout.Clone();
        XmlWorkspaceOperationEffects.Apply(
            configuration,
            layout,
            operationResults);

        var result = await WriteValidatedAsync(
                candidate,
                workspace.RootPath,
                configuration,
                layout,
                async token =>
                {
                    var current = await TryOpenAsync(
                            workspace.RootPath,
                            token)
                        .ConfigureAwait(false);
                    var currentFingerprint = current?.Fingerprint ?? string.Empty;
                    if (!string.Equals(
                            currentFingerprint,
                            workspace.Fingerprint,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new WorkspaceConflictException(
                            $"Workspace fingerprint mismatch. Expected '{workspace.Fingerprint}', found '{currentFingerprint}'.",
                            workspace.Fingerprint,
                            currentFingerprint);
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
        var fingerprint = XmlWorkspaceFingerprint.Calculate(
            candidate,
            result.WorkspaceConfig,
            result.Layout);
        workspace.Accept(
            candidate,
            result.WorkspaceConfig,
            result.Layout,
            fingerprint);
    }

    internal static async Task<XmlWorkspaceWriteResult> WriteValidatedAsync(
        InMemoryWorkspace workspace,
        string workspaceRootPath,
        MetaWorkspaceGenerated workspaceConfig,
        XmlWorkspaceLayout sourceLayout,
        Func<CancellationToken, Task>? beforeWrite,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(workspaceConfig);
        ArgumentNullException.ThrowIfNull(sourceLayout);
        cancellationToken.ThrowIfCancellationRequested();

        var workspaceRoot = Path.GetFullPath(workspaceRootPath);
        var workspaceConfigPath = Path.Combine(workspaceRoot, WorkspaceMetaFile.FileName);
        var normalizedConfig = MetaWorkspaceGenerated.Normalize(workspaceConfig, workspaceConfigPath);
        using var writeLock = WorkspaceWriteLock.Acquire(workspaceRoot);

        if (beforeWrite != null)
        {
            await beforeWrite(cancellationToken).ConfigureAwait(false);
        }

        var modelPath = WorkspacePathResolver.ResolvePathFromWorkspaceRoot(
            workspaceRoot,
            MetaWorkspaceGenerated.GetModelFile(normalizedConfig));
        var instanceDirectoryPath = WorkspacePathResolver.ResolvePathFromWorkspaceRoot(
            workspaceRoot,
            MetaWorkspaceGenerated.GetInstanceDir(normalizedConfig));
        WorkspacePathResolver.EnsurePathUnderWorkspaceRoot(modelPath, workspaceRoot, "ModelFilePath");
        WorkspacePathResolver.EnsurePathUnderWorkspaceRoot(instanceDirectoryPath, workspaceRoot, "InstanceDirPath");

        var workspaceConfigBackupPath = workspaceConfigPath + ".__backup." + Guid.NewGuid().ToString("N");
        var hadExistingWorkspaceConfig = File.Exists(workspaceConfigPath);
        var candidateLayout = new XmlWorkspaceLayout();

        BackupIfPresent(workspaceConfigPath, workspaceConfigBackupPath);
        try
        {
            WriteText(
                WorkspaceMetaFile.Serialize(normalizedConfig, "xml", "."),
                workspaceConfigPath);
            WorkspaceStagingWriter.SaveByStagingConfiguredPaths(
                workspaceRoot,
                modelPath,
                instanceDirectoryPath,
                writeModel: path => WriteDocument(
                    ModelXmlCodec.BuildDocument(workspace.Model),
                    path,
                    indented: true),
                writeInstances: path => WriteInstanceShards(
                    workspace,
                    path,
                    sourceLayout,
                    candidateLayout));
        }
        catch
        {
            RestoreBackup(
                workspaceConfigPath,
                workspaceConfigBackupPath,
                hadExistingWorkspaceConfig);
            throw;
        }
        finally
        {
            DeleteFileIfPresent(workspaceConfigBackupPath);
        }

        return new XmlWorkspaceWriteResult(
            workspaceRoot,
            normalizedConfig,
            candidateLayout);
    }

    internal static string BuildCanonicalShardPayload(
        InMemoryWorkspace workspace,
        XmlWorkspaceLayout sourceLayout)
    {
        var rootName = ResolveRootName(workspace);
        var parts = new List<string>();
        foreach (var plan in BuildShardPlans(
                     workspace,
                     sourceLayout,
                     new XmlWorkspaceLayout()))
        {
            var document = InstanceXmlCodec.BuildEntityDocument(
                workspace.Model,
                plan.EntityName,
                plan.Records,
                rootName);
            parts.Add(plan.ShardFileName + "\n" + Serialize(document, indented: false));
        }

        return string.Join("\n---\n", parts);
    }

    internal static string Serialize(XDocument document, bool indented) =>
        CanonicalXmlSerializer.SerializeToString(document, indented);

    private static void EnsureValid(InMemoryWorkspace workspace)
    {
        var diagnostics = WorkspaceValidator.Validate(workspace.Model, workspace.Instance);
        if (!diagnostics.HasErrors)
        {
            return;
        }

        var preview = diagnostics.Issues
            .Where(issue => issue.Severity == IssueSeverity.Error)
            .Take(5)
            .Select(issue => $"{issue.Code} {issue.Location} - {issue.Message}");
        throw new InvalidOperationException(
            "Workspace validation failed before save: " + string.Join(" | ", preview));
    }

    private static async Task<OpenedXmlWorkspace?> TryOpenAsync(
        string workspaceRootPath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await XmlWorkspaceReader.OpenAsync(
                    workspaceRootPath,
                    cancellationToken)
                .ConfigureAwait(false);
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

    private static void WriteInstanceShards(
        InMemoryWorkspace workspace,
        string instanceDirectoryPath,
        XmlWorkspaceLayout sourceLayout,
        XmlWorkspaceLayout candidateLayout)
    {
        var plans = BuildShardPlans(workspace, sourceLayout, candidateLayout);
        if (plans.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(instanceDirectoryPath);
        var rootName = ResolveRootName(workspace);
        foreach (var plan in plans)
        {
            var document = InstanceXmlCodec.BuildEntityDocument(
                workspace.Model,
                plan.EntityName,
                plan.Records,
                rootName);
            WriteDocument(
                document,
                Path.Combine(instanceDirectoryPath, plan.ShardFileName),
                indented: true);
        }
    }

    private static IReadOnlyList<InstanceShardWritePlan> BuildShardPlans(
        InMemoryWorkspace workspace,
        XmlWorkspaceLayout sourceLayout,
        XmlWorkspaceLayout candidateLayout)
    {
        var plans = new List<InstanceShardWritePlan>();
        foreach (var entityName in GetOrderedEntityNames(workspace))
        {
            plans.AddRange(BuildEntityShardPlans(workspace, entityName, sourceLayout));
        }

        plans = plans
            .OrderBy(plan => plan.EntityName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(plan => plan.ShardFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            plan.ShardFileName = MakeUniqueShardFileName(plan.EntityName, plan.ShardFileName, usedFileNames);
            foreach (var record in plan.Records)
            {
                candidateLayout.AssignShard(plan.EntityName, record.Id, plan.ShardFileName);
            }
        }

        return plans;
    }

    private static IReadOnlyList<InstanceShardWritePlan> BuildEntityShardPlans(
        InMemoryWorkspace workspace,
        string entityName,
        XmlWorkspaceLayout sourceLayout)
    {
        var records = workspace.Instance.RecordsByEntity.TryGetValue(entityName, out var entityRecords)
            ? entityRecords.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<GenericRecord>();
        if (records.Count == 0)
        {
            return Array.Empty<InstanceShardWritePlan>();
        }

        var assignedNames = records
            .Select(record => NormalizeLoadedShardFileName(
                sourceLayout.FindShard(entityName, record.Id),
                entityName))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (assignedNames.Count == 0)
        {
            assignedNames.Add(NormalizeShardFileName(null, entityName));
        }

        var primaryShardFileName = assignedNames[0];
        var recordsByShard = new Dictionary<string, List<GenericRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            var shardFileName = NormalizeLoadedShardFileName(
                sourceLayout.FindShard(entityName, record.Id),
                entityName);
            if (string.IsNullOrWhiteSpace(shardFileName))
            {
                shardFileName = primaryShardFileName;
            }

            if (!recordsByShard.TryGetValue(shardFileName, out var shardRecords))
            {
                shardRecords = new List<GenericRecord>();
                recordsByShard[shardFileName] = shardRecords;
            }

            shardRecords.Add(record);
        }

        return recordsByShard
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => new InstanceShardWritePlan(entityName, item.Key, item.Value))
            .ToList();
    }

    private static IReadOnlyList<string> GetOrderedEntityNames(InMemoryWorkspace workspace)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in workspace.Model.Entities)
        {
            if (!string.IsNullOrWhiteSpace(entity.Name))
            {
                names.Add(entity.Name);
            }
        }

        foreach (var entityName in workspace.Instance.RecordsByEntity.Keys)
        {
            if (!string.IsNullOrWhiteSpace(entityName))
            {
                names.Add(entityName);
            }
        }

        return names.ToList();
    }

    private static string ResolveRootName(InMemoryWorkspace workspace)
    {
        var modelName = !string.IsNullOrWhiteSpace(workspace.Model.Name)
            ? workspace.Model.Name
            : workspace.Instance.ModelName;
        return string.IsNullOrWhiteSpace(modelName) ? "MetadataModel" : modelName;
    }

    private static string NormalizeLoadedShardFileName(string? shardFileName, string entityName)
    {
        var trimmed = (shardFileName ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? string.Empty
            : NormalizeShardFileName(trimmed, entityName);
    }

    private static string NormalizeShardFileName(string? shardFileName, string entityName)
    {
        var leafName = Path.GetFileName((shardFileName ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(leafName))
        {
            return entityName + ".xml";
        }

        return leafName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            ? leafName
            : leafName + ".xml";
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

    private static void WriteDocument(XDocument document, string path, bool indented)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
        File.WriteAllText(tempPath, Serialize(document, indented), Utf8NoBom);
        try
        {
            if (File.Exists(path))
            {
                var backupPath = path + ".bak";
                try
                {
                    File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
                    DeleteFileIfPresent(backupPath);
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
            DeleteFileIfPresent(tempPath);
        }
    }

    private static void WriteText(string contents, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
        File.WriteAllText(tempPath, contents, Utf8NoBom);
        try
        {
            if (File.Exists(path))
            {
                var backupPath = path + ".bak";
                try
                {
                    File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
                    DeleteFileIfPresent(backupPath);
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
            DeleteFileIfPresent(tempPath);
        }
    }

    private static void BackupIfPresent(string path, string backupPath)
    {
        if (File.Exists(path))
        {
            File.Copy(path, backupPath, overwrite: true);
        }
    }

    private static void RestoreBackup(string path, string backupPath, bool hadExistingFile)
    {
        if (hadExistingFile && File.Exists(backupPath))
        {
            DeleteFileIfPresent(path);
            File.Copy(backupPath, path, overwrite: true);
            return;
        }

        DeleteFileIfPresent(path);
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
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
}

internal readonly record struct XmlWorkspaceWriteResult(
    string WorkspaceRootPath,
    MetaWorkspaceGenerated WorkspaceConfig,
    XmlWorkspaceLayout Layout);
