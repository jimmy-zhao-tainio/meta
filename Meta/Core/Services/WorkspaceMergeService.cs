using System.Linq;
using Meta.Core.Domain;
using MetaWorkspaceGenerated = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;
using MetaWorkspaceCanonicalOrder = Meta.Core.WorkspaceConfig.Generated.CanonicalOrder;
using MetaWorkspaceEntityStorage = Meta.Core.WorkspaceConfig.Generated.EntityStorage;

namespace Meta.Core.Services;

public sealed class WorkspaceMergeService : IWorkspaceMergeService
{
    public WorkspaceMergeResult MergeInto(
        Workspace targetWorkspace,
        IReadOnlyList<Workspace> sourceWorkspaces,
        WorkspaceMergeOptions options)
    {
        ArgumentNullException.ThrowIfNull(targetWorkspace);
        ArgumentNullException.ThrowIfNull(sourceWorkspaces);

        if (string.IsNullOrWhiteSpace(options.MergedModelName))
        {
            throw new InvalidOperationException("Merged model name is required.");
        }

        if (sourceWorkspaces.Count < 2)
        {
            throw new InvalidOperationException("Workspace merge requires at least two source workspaces.");
        }

        if (!IsValidIdentifier(options.MergedModelName))
        {
            throw new InvalidOperationException(
                $"Model '{options.MergedModelName}' is invalid. Use identifier pattern [A-Za-z_][A-Za-z0-9_]*.");
        }

        ValidateWorkspaceConfigCompatibility(sourceWorkspaces);
        ValidateModelAndInstanceCollisions(sourceWorkspaces);

        var baselineConfig = MetaWorkspaceGenerated.Normalize(sourceWorkspaces[0].WorkspaceConfig, "workspace-config");
        targetWorkspace.WorkspaceConfig = MetaWorkspaceGenerated.CreateDefault();
        ApplyWorkspaceConfigSettings(targetWorkspace.WorkspaceConfig, baselineConfig);
        targetWorkspace.Model = new GenericModel { Name = options.MergedModelName };
        targetWorkspace.Instance = new GenericInstance { ModelName = options.MergedModelName };
        targetWorkspace.IsDirty = true;

        var totalRows = 0;
        foreach (var sourceWorkspace in sourceWorkspaces)
        {
            totalRows += MergeModelAndInstance(targetWorkspace, sourceWorkspace);
            MergeEntityStorage(targetWorkspace.WorkspaceConfig, sourceWorkspace.WorkspaceConfig);
        }

        return new WorkspaceMergeResult(
            SourceWorkspaceCount: sourceWorkspaces.Count,
            EntitiesMerged: targetWorkspace.Model.Entities.Count,
            RowsMerged: totalRows,
            MergedModelName: options.MergedModelName);
    }

    private static void ValidateWorkspaceConfigCompatibility(IReadOnlyList<Workspace> sourceWorkspaces)
    {
        var baseline = BuildWorkspaceConfigCompatibilitySignature(sourceWorkspaces[0].WorkspaceConfig);
        foreach (var workspace in sourceWorkspaces.Skip(1))
        {
            var candidate = BuildWorkspaceConfigCompatibilitySignature(workspace.WorkspaceConfig);
            if (!string.Equals(candidate, baseline, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Cannot merge workspace '{workspace.WorkspaceRootPath}' because workspace config settings differ.");
            }
        }
    }

    private static string BuildWorkspaceConfigCompatibilitySignature(MetaWorkspaceGenerated config)
    {
        var normalized = MetaWorkspaceGenerated.Normalize(config, "workspace-config");
        var workspace = normalized.Workspace.Single();
        var encoding = normalized.Encoding.Single().Name;
        var newlines = normalized.Newlines.Single().Name;
        var canonicalOrderById = normalized.CanonicalOrder.ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);

        return string.Join(
            "\n",
            encoding,
            newlines,
            canonicalOrderById[workspace.EntitiesOrderId],
            canonicalOrderById[workspace.PropertiesOrderId],
            canonicalOrderById[workspace.RelationshipsOrderId],
            canonicalOrderById[workspace.RowsOrderId],
            canonicalOrderById[workspace.AttributesOrderId]);
    }

    private static void ValidateModelAndInstanceCollisions(IReadOnlyList<Workspace> sourceWorkspaces)
    {
        var entityNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var rowKeys = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var workspace in sourceWorkspaces)
        {
            foreach (var entity in workspace.Model.Entities.OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                if (entityNames.TryGetValue(entity.Name, out var existingWorkspacePath))
                {
                    throw new InvalidOperationException(
                        $"Cannot merge workspace '{workspace.WorkspaceRootPath}' because entity '{entity.Name}' already exists in '{existingWorkspacePath}'.");
                }

                entityNames[entity.Name] = workspace.WorkspaceRootPath;
            }

            foreach (var entityPair in workspace.Instance.RecordsByEntity.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                foreach (var row in entityPair.Value.OrderBy(item => item.Id, StringComparer.Ordinal))
                {
                    var key = entityPair.Key + "\n" + row.Id;
                    if (rowKeys.TryGetValue(key, out var existingWorkspacePath))
                    {
                        throw new InvalidOperationException(
                            $"Cannot merge workspace '{workspace.WorkspaceRootPath}' because row '{entityPair.Key}:{row.Id}' already exists in '{existingWorkspacePath}'.");
                    }

                    rowKeys[key] = workspace.WorkspaceRootPath;
                }
            }
        }
    }

    private static void ApplyWorkspaceConfigSettings(MetaWorkspaceGenerated target, MetaWorkspaceGenerated source)
    {
        var normalized = MetaWorkspaceGenerated.Normalize(source, "workspace-config");
        var sourceWorkspace = normalized.Workspace.Single();
        var sourceCanonicalOrderById = normalized.CanonicalOrder.ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);

        var targetNormalized = MetaWorkspaceGenerated.Normalize(target, "workspace-config");
        targetNormalized.Encoding.Single().Name = normalized.Encoding.Single().Name;
        targetNormalized.Newlines.Single().Name = normalized.Newlines.Single().Name;

        var targetWorkspace = targetNormalized.Workspace.Single();
        targetWorkspace.EntitiesOrderId = ResolveCanonicalOrderId(targetNormalized.CanonicalOrder, sourceCanonicalOrderById[sourceWorkspace.EntitiesOrderId]);
        targetWorkspace.PropertiesOrderId = ResolveCanonicalOrderId(targetNormalized.CanonicalOrder, sourceCanonicalOrderById[sourceWorkspace.PropertiesOrderId]);
        targetWorkspace.RelationshipsOrderId = ResolveCanonicalOrderId(targetNormalized.CanonicalOrder, sourceCanonicalOrderById[sourceWorkspace.RelationshipsOrderId]);
        targetWorkspace.RowsOrderId = ResolveCanonicalOrderId(targetNormalized.CanonicalOrder, sourceCanonicalOrderById[sourceWorkspace.RowsOrderId]);
        targetWorkspace.AttributesOrderId = ResolveCanonicalOrderId(targetNormalized.CanonicalOrder, sourceCanonicalOrderById[sourceWorkspace.AttributesOrderId]);

        target.Workspace = targetNormalized.Workspace;
        target.WorkspaceLayout = targetNormalized.WorkspaceLayout;
        target.Encoding = targetNormalized.Encoding;
        target.Newlines = targetNormalized.Newlines;
        target.CanonicalOrder = targetNormalized.CanonicalOrder;
        target.EntityStorage = targetNormalized.EntityStorage;
    }

    private static string ResolveCanonicalOrderId(IReadOnlyCollection<MetaWorkspaceCanonicalOrder> orders, string name)
    {
        var match = orders.SingleOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Workspace config is missing canonical order '{name}'.");
        return match.Id;
    }

    private static int MergeModelAndInstance(Workspace targetWorkspace, Workspace sourceWorkspace)
    {
        var rowsMerged = 0;
        foreach (var entity in sourceWorkspace.Model.Entities.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            var clone = new GenericEntity
            {
                Name = entity.Name,
            };

            foreach (var property in entity.Properties.OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                clone.Properties.Add(new GenericProperty
                {
                    Name = property.Name,
                    DataType = property.DataType,
                    IsNullable = property.IsNullable,
                });
            }

            foreach (var relationship in entity.Relationships.OrderBy(item => item.GetColumnName(), StringComparer.Ordinal))
            {
                clone.Relationships.Add(new GenericRelationship
                {
                    Entity = relationship.Entity,
                    Role = relationship.Role,
                });
            }

            targetWorkspace.Model.Entities.Add(clone);
        }

        foreach (var entityPair in sourceWorkspace.Instance.RecordsByEntity.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var targetRecords = targetWorkspace.Instance.GetOrCreateEntityRecords(entityPair.Key);
            foreach (var record in entityPair.Value.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                var clone = new GenericRecord
                {
                    Id = record.Id,
                    SourceShardFileName = record.SourceShardFileName,
                };

                foreach (var value in record.Values.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    clone.Values[value.Key] = value.Value;
                }

                foreach (var relationship in record.RelationshipIds.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    clone.RelationshipIds[relationship.Key] = relationship.Value;
                }

                targetRecords.Add(clone);
                rowsMerged++;
            }
        }

        return rowsMerged;
    }

    private static void MergeEntityStorage(MetaWorkspaceGenerated targetConfig, MetaWorkspaceGenerated sourceConfig)
    {
        var normalizedSource = MetaWorkspaceGenerated.Normalize(sourceConfig, "workspace-config");
        var targetWorkspace = targetConfig.Workspace.Single();
        var nextId = targetConfig.EntityStorage.Count == 0
            ? 1
            : targetConfig.EntityStorage.Select(item => int.TryParse(item.Id, out var value) ? value : 0).Max() + 1;

        foreach (var item in normalizedSource.EntityStorage
                     .OrderBy(storage => storage.EntityName, StringComparer.Ordinal)
                     .ThenBy(storage => storage.Id, StringComparer.Ordinal))
        {
            targetConfig.EntityStorage.Add(new MetaWorkspaceEntityStorage
            {
                Id = nextId.ToString(),
                WorkspaceId = targetWorkspace.Id,
                Workspace = targetWorkspace,
                EntityName = item.EntityName,
                StorageKind = item.StorageKind,
                DirectoryPath = item.DirectoryPath,
                FilePath = item.FilePath,
                Pattern = item.Pattern,
            });
            nextId++;
        }
    }

    private static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        return value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
    }
}
