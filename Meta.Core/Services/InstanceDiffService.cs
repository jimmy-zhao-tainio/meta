using System.Reflection;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Serialization;
using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace Meta.Core.Services;

public sealed partial class InstanceDiffService : IInstanceDiffService
{
    private const string InstanceDiffEqualModelName = "InstanceDiffModelEqual";
    private const string InstanceDiffEqualWorkspaceResourceName = "Meta.Core.Workspaces.InstanceDiff.Equal.workspace.xml";
    private const string InstanceDiffEqualModelResourceName = "Meta.Core.Workspaces.InstanceDiff.Equal.metadata.model.xml";
    private const string InstanceDiffAlignedModelName = "InstanceDiffModelAligned";
    private const string InstanceDiffAlignedWorkspaceResourceName = "Meta.Core.Workspaces.InstanceDiff.Aligned.workspace.xml";
    private const string InstanceDiffAlignedModelResourceName = "Meta.Core.Workspaces.InstanceDiff.Aligned.metadata.model.xml";
    private const string InstanceDiffAlignmentModelName = "InstanceDiffModelAlignment";
    private const string InstanceDiffAlignmentWorkspaceResourceName = "Meta.Core.Workspaces.InstanceDiff.Alignment.workspace.xml";
    private const string InstanceDiffAlignmentModelResourceName = "Meta.Core.Workspaces.InstanceDiff.Alignment.metadata.model.xml";

    private const string DiffEntityName = "Diff";
    private const string ModelEntityName = "Model";
    private const string EntityEntityName = "Entity";
    private const string PropertyEntityName = "Property";
    private const string ModelLeftEntityInstanceEntityName = "ModelLeftEntityInstance";
    private const string ModelRightEntityInstanceEntityName = "ModelRightEntityInstance";
    private const string ModelLeftPropertyInstanceEntityName = "ModelLeftPropertyInstance";
    private const string ModelRightPropertyInstanceEntityName = "ModelRightPropertyInstance";
    private const string ModelLeftEntityInstanceNotInRightEntityName = "ModelLeftEntityInstanceNotInRight";
    private const string ModelRightEntityInstanceNotInLeftEntityName = "ModelRightEntityInstanceNotInLeft";
    private const string ModelLeftPropertyInstanceNotInRightEntityName = "ModelLeftPropertyInstanceNotInRight";
    private const string ModelRightPropertyInstanceNotInLeftEntityName = "ModelRightPropertyInstanceNotInLeft";

    private const string AlignmentEntityName = "Alignment";
    private const string ModelLeftEntityName = "ModelLeft";
    private const string ModelRightEntityName = "ModelRight";
    private const string ModelLeftEntityEntityName = "ModelLeftEntity";
    private const string ModelRightEntityEntityName = "ModelRightEntity";
    private const string ModelLeftPropertyEntityName = "ModelLeftProperty";
    private const string ModelRightPropertyEntityName = "ModelRightProperty";
    private const string EntityMapEntityName = "EntityMap";
    private const string PropertyMapEntityName = "PropertyMap";

    private static readonly Lazy<InstanceDiffWorkspaceDefinition> InstanceDiffEqualWorkspaceDefinition =
        new(() => LoadWorkspaceDefinition(
            InstanceDiffEqualWorkspaceResourceName,
            InstanceDiffEqualModelResourceName,
            InstanceDiffEqualModelName));
    private static readonly Lazy<InstanceDiffWorkspaceDefinition> InstanceDiffAlignedWorkspaceDefinition =
        new(() => LoadWorkspaceDefinition(
            InstanceDiffAlignedWorkspaceResourceName,
            InstanceDiffAlignedModelResourceName,
            InstanceDiffAlignedModelName));
    private static readonly Lazy<InstanceDiffWorkspaceDefinition> InstanceDiffAlignmentWorkspaceDefinition =
        new(() => LoadWorkspaceDefinition(
            InstanceDiffAlignmentWorkspaceResourceName,
            InstanceDiffAlignmentModelResourceName,
            InstanceDiffAlignmentModelName));

    private static readonly Lazy<string> InstanceDiffEqualModelSignature =
        new(() => ComputeModelContractSignature(InstanceDiffEqualWorkspaceDefinition.Value.Model));
    private static readonly Lazy<string> InstanceDiffAlignedModelSignature =
        new(() => ComputeModelContractSignature(InstanceDiffAlignedWorkspaceDefinition.Value.Model));
    private static readonly Lazy<string> InstanceDiffAlignmentModelSignature =
        new(() => ComputeModelContractSignature(InstanceDiffAlignmentWorkspaceDefinition.Value.Model));

    private sealed record EqualEntityCatalog(
        string EntityId,
        string EntityName,
        GenericEntity ModelEntity,
        IReadOnlyDictionary<string, string> PropertyIdByName,
        IReadOnlyList<string> OrderedPropertyNames);

    private sealed record EqualSideData(
        IReadOnlyCollection<string> RowSet,
        IReadOnlyDictionary<string, string> EntityInstanceIdByRowKey,
        IReadOnlyCollection<string> PropertySet,
        IReadOnlyDictionary<string, string> PropertyInstanceIdByTupleKey,
        IReadOnlyDictionary<string, string> ValueByEntityRowPropertyKey,
        int RowCount,
        int PropertyCount);

    private sealed record EqualDiffData(
        IReadOnlyDictionary<string, EqualEntityCatalog> EntityCatalogByName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> OrderedPropertiesByEntity,
        IReadOnlyCollection<string> LeftRowSet,
        IReadOnlyCollection<string> RightRowSet,
        IReadOnlyCollection<string> LeftPropertySet,
        IReadOnlyCollection<string> RightPropertySet,
        IReadOnlyDictionary<string, string> RightValueByEntityRowPropertyKey,
        string DiffId);

    private sealed record AlignmentCatalog(
        string AlignmentId,
        string AlignmentName,
        string ModelLeftId,
        string ModelRightId,
        string ModelLeftName,
        string ModelRightName,
        IReadOnlyDictionary<string, string> LeftEntityNameById,
        IReadOnlyDictionary<string, string> RightEntityNameById,
        IReadOnlyDictionary<string, string> LeftPropertyNameById,
        IReadOnlyDictionary<string, string> RightPropertyNameById,
        IReadOnlyDictionary<string, string> LeftPropertyEntityIdByPropertyId,
        IReadOnlyDictionary<string, string> RightPropertyEntityIdByPropertyId,
        IReadOnlyDictionary<string, (string ModelLeftEntityId, string ModelRightEntityId)> EntityMapById,
        IReadOnlyDictionary<string, (string ModelLeftPropertyId, string ModelRightPropertyId)> PropertyMapById,
        IReadOnlyDictionary<string, string> EntityMapIdByLeftEntityId,
        IReadOnlyDictionary<string, string> EntityMapIdByRightEntityId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> PropertyMapIdsByEntityMapId);

    private sealed record AlignedSideData(
        IReadOnlyCollection<string> RowSet,
        IReadOnlyDictionary<string, string> EntityInstanceIdByRowKey,
        IReadOnlyCollection<string> PropertySet,
        IReadOnlyDictionary<string, string> PropertyInstanceIdByTupleKey,
        IReadOnlyDictionary<string, string> ValueByEntityMapRowPropertyMapKey,
        int RowCount,
        int PropertyCount);

    private sealed record AlignedDiffData(
        AlignmentCatalog Alignment,
        IReadOnlyCollection<string> LeftRowSet,
        IReadOnlyCollection<string> RightRowSet,
        IReadOnlyCollection<string> LeftPropertySet,
        IReadOnlyCollection<string> RightPropertySet,
        IReadOnlyDictionary<string, string> RightValueByEntityMapRowPropertyMapKey);

    private sealed record InstanceDiffWorkspaceDefinition(
        MetaWorkspaceConfig WorkspaceConfig,
        GenericModel Model);

    public InstanceDiffBuildResult BuildEqualDiffWorkspace(
        Workspace leftWorkspace,
        Workspace rightWorkspace,
        string rightWorkspacePath)
    {
        ArgumentNullException.ThrowIfNull(leftWorkspace);
        ArgumentNullException.ThrowIfNull(rightWorkspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(rightWorkspacePath);

        return BuildEqualInstanceDiffWorkspace(leftWorkspace, rightWorkspace, rightWorkspacePath);
    }

    public InstanceDiffBuildResult BuildAlignedDiffWorkspace(
        Workspace leftWorkspace,
        Workspace rightWorkspace,
        Workspace alignmentWorkspace,
        string rightWorkspacePath)
    {
        ArgumentNullException.ThrowIfNull(leftWorkspace);
        ArgumentNullException.ThrowIfNull(rightWorkspace);
        ArgumentNullException.ThrowIfNull(alignmentWorkspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(rightWorkspacePath);

        var alignment = ParseAlignmentCatalog(
            alignmentWorkspace,
            InstanceDiffAlignmentModelName,
            InstanceDiffAlignmentModelSignature.Value);
        ValidateWorkspaceMatchesAlignment(
            leftWorkspace,
            alignment.ModelLeftName,
            alignment.LeftEntityNameById,
            alignment.LeftPropertyNameById,
            alignment.LeftPropertyEntityIdByPropertyId);
        ValidateWorkspaceMatchesAlignment(
            rightWorkspace,
            alignment.ModelRightName,
            alignment.RightEntityNameById,
            alignment.RightPropertyNameById,
            alignment.RightPropertyEntityIdByPropertyId);

        return BuildAlignedInstanceDiffWorkspace(
            leftWorkspace,
            rightWorkspace,
            alignmentWorkspace,
            alignment,
            rightWorkspacePath);
    }

    public void ApplyEqualDiffWorkspace(
        Workspace targetWorkspace,
        Workspace diffWorkspace)
    {
        ArgumentNullException.ThrowIfNull(targetWorkspace);
        ArgumentNullException.ThrowIfNull(diffWorkspace);

        var diffData = ParseEqualDiffWorkspace(diffWorkspace);
        var preSnapshot = BuildWorkspaceSnapshotForEqualDiff(targetWorkspace, diffData);
        if (!preSnapshot.RowSet.SetEquals(diffData.LeftRowSet) ||
            !preSnapshot.PropertySet.SetEquals(diffData.LeftPropertySet))
        {
            throw new InvalidOperationException(
                "instance merge precondition failed: target does not match the diff left snapshot.");
        }

        ApplyEqualRightSnapshotToWorkspace(targetWorkspace, diffData);

        var postSnapshot = BuildWorkspaceSnapshotForEqualDiff(targetWorkspace, diffData);
        if (!postSnapshot.RowSet.SetEquals(diffData.RightRowSet) ||
            !postSnapshot.PropertySet.SetEquals(diffData.RightPropertySet))
        {
            throw new InvalidOperationException(
                "instance merge postcondition failed: target does not match the diff right snapshot.");
        }
    }

    public void ApplyAlignedDiffWorkspace(
        Workspace targetWorkspace,
        Workspace diffWorkspace)
    {
        ArgumentNullException.ThrowIfNull(targetWorkspace);
        ArgumentNullException.ThrowIfNull(diffWorkspace);

        var diffData = ParseAlignedDiffWorkspace(diffWorkspace);
        ValidateWorkspaceMatchesAlignment(
            targetWorkspace,
            diffData.Alignment.ModelLeftName,
            diffData.Alignment.LeftEntityNameById,
            diffData.Alignment.LeftPropertyNameById,
            diffData.Alignment.LeftPropertyEntityIdByPropertyId);

        var preSnapshot = BuildWorkspaceSnapshotForAlignedDiff(targetWorkspace, diffData.Alignment);
        if (!preSnapshot.RowSet.SetEquals(diffData.LeftRowSet) ||
            !preSnapshot.PropertySet.SetEquals(diffData.LeftPropertySet))
        {
            throw new InvalidOperationException(
                "instance merge-aligned precondition failed: target does not match the diff left snapshot.");
        }

        ApplyAlignedRightSnapshotToWorkspace(targetWorkspace, diffData);

        var postSnapshot = BuildWorkspaceSnapshotForAlignedDiff(targetWorkspace, diffData.Alignment);
        if (!postSnapshot.RowSet.SetEquals(diffData.RightRowSet) ||
            !postSnapshot.PropertySet.SetEquals(diffData.RightPropertySet))
        {
            throw new InvalidOperationException(
                "instance merge-aligned postcondition failed: target does not match the diff right snapshot.");
        }
    }

    private sealed class IdentityAllocator
    {
        private readonly Dictionary<string, int> nextIdByEntity = new(StringComparer.OrdinalIgnoreCase);

        public string NextId(string entityName)
        {
            if (string.IsNullOrWhiteSpace(entityName))
            {
                throw new InvalidOperationException("Identity allocator requires a non-empty entity name.");
            }

            var next = nextIdByEntity.TryGetValue(entityName, out var current) ? current + 1 : 1;
            nextIdByEntity[entityName] = next;
            return next.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AlignmentReferenceFieldsByEntity =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [ModelLeftEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelId"] = ModelEntityName,
            },
            [ModelRightEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelId"] = ModelEntityName,
            },
            [AlignmentEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelLeftId"] = ModelLeftEntityName,
                ["ModelRightId"] = ModelRightEntityName,
            },
            [ModelLeftEntityEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelLeftId"] = ModelLeftEntityName,
            },
            [ModelRightEntityEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelRightId"] = ModelRightEntityName,
            },
            [ModelLeftPropertyEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelLeftEntityId"] = ModelLeftEntityEntityName,
            },
            [ModelRightPropertyEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelRightEntityId"] = ModelRightEntityEntityName,
            },
            [EntityMapEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelLeftEntityId"] = ModelLeftEntityEntityName,
                ["ModelRightEntityId"] = ModelRightEntityEntityName,
            },
            [PropertyMapEntityName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelLeftPropertyId"] = ModelLeftPropertyEntityName,
                ["ModelRightPropertyId"] = ModelRightPropertyEntityName,
            },
        };

    private static string EscapeCanonicalPart(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string EncodeCanonicalPayload(string payload)
    {
        return Convert.ToHexString(Encoding.UTF8.GetBytes(payload ?? string.Empty)).ToLowerInvariant();
    }

    private static string CreateEntityInstanceKey(string entityId, string entityInstanceIdentifier)
    {
        return string.Join("\n", EscapeCanonicalPart(entityId), EscapeCanonicalPart(entityInstanceIdentifier));
    }

    private static string CreatePropertyTupleKey(string entityId, string entityInstanceIdentifier, string propertyId, string value)
    {
        return string.Join(
            "\n",
            EscapeCanonicalPart(entityId),
            EscapeCanonicalPart(entityInstanceIdentifier),
            EscapeCanonicalPart(propertyId),
            EscapeCanonicalPart(value));
    }

    private static string CreateEntityPropertyIdentityKey(string entityId, string entityInstanceIdentifier, string propertyId)
    {
        return string.Join(
            "\n",
            EscapeCanonicalPart(entityId),
            EscapeCanonicalPart(entityInstanceIdentifier),
            EscapeCanonicalPart(propertyId));
    }

    private static string CreateAlignedRowKey(string entityMapId, string rowId)
    {
        return string.Join("\n", EscapeCanonicalPart(entityMapId), EscapeCanonicalPart(rowId));
    }

    private static string CreateAlignedPropertyTupleKey(string entityMapId, string rowId, string propertyMapId, string value)
    {
        return string.Join(
            "\n",
            EscapeCanonicalPart(entityMapId),
            EscapeCanonicalPart(rowId),
            EscapeCanonicalPart(propertyMapId),
            EscapeCanonicalPart(value));
    }

    private static string CreateAlignedEntityRowPropertyMapKey(string entityMapId, string rowId, string propertyMapId)
    {
        return string.Join(
            "\n",
            EscapeCanonicalPart(entityMapId),
            EscapeCanonicalPart(rowId),
            EscapeCanonicalPart(propertyMapId));
    }

    private static GenericEntity RequireEntity(Workspace workspace, string entityName)
    {
        var entity = workspace.Model.FindEntity(entityName);
        if (entity == null)
        {
            throw new InvalidOperationException($"Entity '{entityName}' does not exist.");
        }

        return entity;
    }

    private Workspace CreateWorkspaceFromDefinition(InstanceDiffWorkspaceDefinition definition, string workspaceRootPath)
    {
        var model = CloneModelDefinition(definition.Model);
        return new Workspace
        {
            WorkspaceRootPath = workspaceRootPath,
            MetadataRootPath = Path.Combine(workspaceRootPath, "metadata"),
            WorkspaceConfig = MetaWorkspaceConfig.Normalize(definition.WorkspaceConfig, workspaceRootPath),
            Model = model,
            Instance = new GenericInstance
            {
                ModelName = model.Name,
            },
            IsDirty = true,
        };
    }

    private static InstanceDiffWorkspaceDefinition LoadWorkspaceDefinition(
        string workspaceResourceName,
        string modelResourceName,
        string expectedModelName)
    {
        var assembly = typeof(InstanceDiffService).Assembly;
        using var workspaceStream = assembly.GetManifestResourceStream(workspaceResourceName);
        if (workspaceStream == null)
        {
            throw new InvalidOperationException($"Embedded workspace '{workspaceResourceName}' was not found.");
        }

        using var modelStream = assembly.GetManifestResourceStream(modelResourceName);
        if (modelStream == null)
        {
            throw new InvalidOperationException($"Embedded model '{modelResourceName}' was not found.");
        }

        var workspaceDocument = XDocument.Load(workspaceStream, LoadOptions.None);
        var workspaceConfig = MetaWorkspaceConfig.Load(workspaceDocument, workspaceResourceName);
        var modelDocument = XDocument.Load(modelStream, LoadOptions.None);
        var model = ModelXmlCodec.Load(modelDocument);
        if (!string.Equals(model.Name, expectedModelName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Workspace '{modelResourceName}' model name must be '{expectedModelName}', found '{model.Name}'.");
        }

        return new InstanceDiffWorkspaceDefinition(workspaceConfig, model);
    }

    private static string ComputeModelContractSignature(GenericModel model)
    {
        var lines = new List<string>
        {
            "model|" + EscapeCanonicalPart(model.Name),
        };

        foreach (var entity in model.Entities.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add(
                "entity|" +
                EscapeCanonicalPart(entity.Name) + "|" +
                EscapeCanonicalPart(entity.GetListName()));
            foreach (var property in entity.Properties.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add(
                    "property|" +
                    EscapeCanonicalPart(entity.Name) + "|" +
                    EscapeCanonicalPart(property.Name) + "|" +
                    EscapeCanonicalPart(property.DataType) + "|" +
                    (property.IsNullable ? "nullable" : "required"));
            }

            foreach (var relationship in entity.Relationships
                         .OrderBy(item => item.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Entity, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add(
                    "relationship|" +
                    EscapeCanonicalPart(entity.Name) + "|" +
                    EscapeCanonicalPart(relationship.Entity) + "|" +
                    EscapeCanonicalPart(relationship.GetColumnName()));
            }
        }

        return EncodeCanonicalPayload(string.Join("\n", lines));
    }

    private static bool IsModelContract(GenericModel model, string expectedSignature)
    {
        return string.Equals(
            ComputeModelContractSignature(model),
            expectedSignature,
            StringComparison.Ordinal);
    }

    private static GenericModel CloneModelDefinition(GenericModel source)
    {
        var clone = new GenericModel
        {
            Name = source.Name ?? string.Empty,
        };
        foreach (var entity in source.Entities)
        {
            var entityClone = new GenericEntity
            {
                Name = entity.Name ?? string.Empty,
            };
            foreach (var property in entity.Properties)
            {
                entityClone.Properties.Add(new GenericProperty
                {
                    Name = property.Name ?? string.Empty,
                    DataType = property.DataType ?? string.Empty,
                    IsNullable = property.IsNullable,
                });
            }

            foreach (var relationship in entity.Relationships)
            {
                entityClone.Relationships.Add(new GenericRelationship
                {
                    Entity = relationship.Entity ?? string.Empty,
                    Role = relationship.Role ?? string.Empty,
                });
            }

            clone.Entities.Add(entityClone);
        }

        return clone;
    }

    private static GenericRecord AddDiffRecord(
        Workspace workspace,
        string entityName,
        string id,
        IReadOnlyDictionary<string, string?> values)
    {
        var modelEntity = workspace.Model.FindEntity(entityName)
                          ?? throw new InvalidOperationException($"Diff model is missing entity '{entityName}'.");
        var propertyNames = modelEntity.Properties
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationshipByAlias = new Dictionary<string, GenericRelationship>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in modelEntity.Relationships)
        {
            relationshipByAlias[relationship.GetColumnName()] = relationship;
            relationshipByAlias[relationship.GetRoleOrDefault()] = relationship;
        }

        var row = new GenericRecord
        {
            Id = id,
        };

        foreach (var pair in values
                     .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                     .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(pair.Key, "Id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (pair.Value == null)
            {
                continue;
            }

            if (relationshipByAlias.TryGetValue(pair.Key, out var relationship))
            {
                row.RelationshipIds[relationship.GetColumnName()] = pair.Value;
                continue;
            }

            if (propertyNames.Contains(pair.Key))
            {
                row.Values[pair.Key] = pair.Value;
                continue;
            }

            throw new InvalidOperationException(
                $"Diff row '{entityName} {id}' contains unknown field '{pair.Key}'.");
        }

        workspace.Instance.GetOrCreateEntityRecords(entityName).Add(row);
        return row;
    }

    private static bool TryGetRecordFieldValue(GenericRecord row, string key, out string value)
    {
        if (row.Values.TryGetValue(key, out var propertyValue))
        {
            if (propertyValue == null)
            {
                throw new InvalidOperationException(
                    $"Instance '{row.Id}' contains null value for '{key}'.");
            }

            value = propertyValue;
            return true;
        }

        if (row.RelationshipIds.TryGetValue(key, out var relationshipValue))
        {
            if (relationshipValue == null)
            {
                throw new InvalidOperationException(
                    $"Instance '{row.Id}' contains null relationship value for '{key}'.");
            }

            value = relationshipValue;
            return true;
        }

        if (key.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
        {
            var relationshipUsageName = key[..^2];
            if (row.RelationshipIds.TryGetValue(relationshipUsageName, out var usageRelationshipValue))
            {
                if (usageRelationshipValue == null)
                {
                    throw new InvalidOperationException(
                        $"Instance '{row.Id}' contains null relationship value for '{key}'.");
                }

                value = usageRelationshipValue;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static IReadOnlyDictionary<string, GenericRecord> BuildRecordMap(Workspace workspace, string entityName)
    {
        if (!workspace.Instance.RecordsByEntity.TryGetValue(entityName, out var rows))
        {
            return new Dictionary<string, GenericRecord>(StringComparer.OrdinalIgnoreCase);
        }

        var map = new Dictionary<string, GenericRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Id))
            {
                throw new InvalidOperationException($"Entity '{entityName}' has a record with blank Id.");
            }

            if (!map.TryAdd(row.Id, row))
            {
                throw new InvalidOperationException($"Entity '{entityName}' has duplicate Id '{row.Id}'.");
            }
        }

        return map;
    }

    private string ResolveModelXmlPath(string workspacePath, Workspace workspace)
    {
        var workspaceRoot = !string.IsNullOrWhiteSpace(workspace.WorkspaceRootPath)
            ? Path.GetFullPath(workspace.WorkspaceRootPath)
            : Path.GetFullPath(workspacePath);
        var metadataRoot = !string.IsNullOrWhiteSpace(workspace.MetadataRootPath)
            ? Path.GetFullPath(workspace.MetadataRootPath)
            : Path.Combine(workspaceRoot, "metadata");
        var modelRelativePath = MetaWorkspaceConfig.GetModelFile(workspace.WorkspaceConfig);
        var normalizedRelative = modelRelativePath.Replace('/', Path.DirectorySeparatorChar);
        var candidates = new[]
        {
            Path.IsPathRooted(normalizedRelative)
                ? normalizedRelative
                : Path.Combine(workspaceRoot, normalizedRelative),
            Path.Combine(metadataRoot, "model.xml"),
            Path.Combine(workspaceRoot, "model.xml"),
        };

        var match = candidates.FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(match))
        {
            throw new FileNotFoundException(
                $"Could not resolve model.xml for workspace '{workspacePath}'.");
        }

        return Path.GetFullPath(match);
    }

    private bool AreModelXmlFilesByteIdentical(
        string leftWorkspacePath,
        Workspace leftWorkspace,
        string rightWorkspacePath,
        Workspace rightWorkspace,
        out string leftModelPath,
        out string rightModelPath)
    {
        leftModelPath = ResolveModelXmlPath(leftWorkspacePath, leftWorkspace);
        rightModelPath = ResolveModelXmlPath(rightWorkspacePath, rightWorkspace);
        var leftBytes = File.ReadAllBytes(leftModelPath);
        var rightBytes = File.ReadAllBytes(rightModelPath);
        return leftBytes.AsSpan().SequenceEqual(rightBytes);
    }

    private string ResolveInstanceDiffOutputPath(string rightWorkspacePath, string suffix)
    {
        var rightFull = Path.GetFullPath(rightWorkspacePath);
        var parent = Directory.GetParent(rightFull)?.FullName ?? Environment.CurrentDirectory;
        var rightName = Path.GetFileName(rightFull);
        if (string.IsNullOrWhiteSpace(rightName))
        {
            rightName = "workspace";
        }

        return Path.Combine(parent, $"{rightName}.{suffix}");
    }

    private static bool TryGetPropertyLikeValue(GenericEntity entity, GenericRecord row, string propertyName, out string value)
    {
        if (row.Values.TryGetValue(propertyName, out var propertyValue))
        {
            if (propertyValue == null)
            {
                throw new InvalidOperationException(
                    $"Entity '{entity.Name}' row '{row.Id}' contains null value for property '{propertyName}'.");
            }

            value = propertyValue;
            return true;
        }

        var relationship = entity.FindRelationshipByRole(propertyName) ??
                           entity.FindRelationshipByColumnName(propertyName);
        if (relationship != null &&
            row.RelationshipIds.TryGetValue(relationship.GetColumnName(), out var relationshipValue))
        {
            if (relationshipValue == null)
            {
                throw new InvalidOperationException(
                    $"Entity '{entity.Name}' row '{row.Id}' contains null relationship target for '{relationship.GetColumnName()}'.");
            }

            if (string.IsNullOrWhiteSpace(relationshipValue))
            {
                throw new InvalidOperationException(
                    $"Entity '{entity.Name}' row '{row.Id}' contains blank relationship target for '{relationship.GetColumnName()}'.");
            }

            value = relationshipValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsRelationshipProperty(GenericEntity entity, string propertyName, out string relationshipUsageName)
    {
        relationshipUsageName = string.Empty;
        var relationship = entity.FindRelationshipByRole(propertyName) ??
                           entity.FindRelationshipByColumnName(propertyName);
        if (relationship == null)
        {
            return false;
        }

        relationshipUsageName = relationship.GetColumnName();
        return true;
    }

    private (string ModelId, IReadOnlyDictionary<string, EqualEntityCatalog> CatalogByName) BuildEqualEntityCatalog(
        GenericModel model,
        Workspace diffWorkspace,
        IdentityAllocator identityAllocator)
    {
        var modelId = identityAllocator.NextId(ModelEntityName);
        AddDiffRecord(
            diffWorkspace,
            ModelEntityName,
            modelId,
            new Dictionary<string, string?>
            {
                ["Name"] = model.Name,
            });

        var catalogByName = new Dictionary<string, EqualEntityCatalog>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in model.Entities.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var entityId = identityAllocator.NextId(EntityEntityName);
            AddDiffRecord(
                diffWorkspace,
                EntityEntityName,
                entityId,
                new Dictionary<string, string?>
                {
                    ["Name"] = entity.Name,
                    ["ModelId"] = modelId,
                });

            var propertyNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in entity.Properties)
            {
                if (string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                propertyNames.Add(property.Name);
            }

            foreach (var relationship in entity.Relationships)
            {
                propertyNames.Add(relationship.GetColumnName());
            }

            var propertyIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var propertyName in propertyNames.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                var propertyId = identityAllocator.NextId(PropertyEntityName);
                propertyIdByName[propertyName] = propertyId;
                AddDiffRecord(
                    diffWorkspace,
                    PropertyEntityName,
                    propertyId,
                    new Dictionary<string, string?>
                    {
                        ["Name"] = propertyName,
                        ["EntityId"] = entityId,
                    });
            }

            catalogByName[entity.Name] = new EqualEntityCatalog(
                EntityId: entityId,
                EntityName: entity.Name,
                ModelEntity: entity,
                PropertyIdByName: propertyIdByName,
                OrderedPropertyNames: propertyIdByName.Keys
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }

        return (modelId, catalogByName);
    }

    private EqualSideData BuildEqualSideData(
        Workspace sourceWorkspace,
        Workspace diffWorkspace,
        IReadOnlyDictionary<string, EqualEntityCatalog> catalogByEntityName,
        bool leftSide,
        string diffId,
        IdentityAllocator identityAllocator)
    {
        var rowEntityName = leftSide ? ModelLeftEntityInstanceEntityName : ModelRightEntityInstanceEntityName;
        var propertyEntityName = leftSide ? ModelLeftPropertyInstanceEntityName : ModelRightPropertyInstanceEntityName;

        var rowSet = new HashSet<string>(StringComparer.Ordinal);
        var entityInstanceIdByRowKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var propertySet = new HashSet<string>(StringComparer.Ordinal);
        var propertyInstanceIdByTupleKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var valueByEntityRowPropertyKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var rowCount = 0;
        var propertyCount = 0;

        foreach (var catalog in catalogByEntityName.Values.OrderBy(item => item.EntityName, StringComparer.OrdinalIgnoreCase))
        {
            var rows = BuildRecordMap(sourceWorkspace, catalog.EntityName);
            foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
            {
                var rowInstanceId = identityAllocator.NextId(rowEntityName);
                AddDiffRecord(
                    diffWorkspace,
                    rowEntityName,
                    rowInstanceId,
                    new Dictionary<string, string?>
                    {
                        ["DiffId"] = diffId,
                        ["EntityId"] = catalog.EntityId,
                        ["EntityInstanceIdentifier"] = row.Id,
                    });
                rowCount++;
                var rowKey = CreateEntityInstanceKey(catalog.EntityId, row.Id);
                rowSet.Add(rowKey);
                entityInstanceIdByRowKey[rowKey] = rowInstanceId;

                foreach (var propertyName in catalog.OrderedPropertyNames)
                {
                    if (!TryGetPropertyLikeValue(catalog.ModelEntity, row, propertyName, out var propertyValue))
                    {
                        continue;
                    }

                    if (!catalog.PropertyIdByName.TryGetValue(propertyName, out var propertyId))
                    {
                        throw new InvalidOperationException(
                            $"Property catalog for entity '{catalog.EntityName}' is missing '{propertyName}'.");
                    }

                    var propertyInstanceId = identityAllocator.NextId(propertyEntityName);
                    AddDiffRecord(
                        diffWorkspace,
                        propertyEntityName,
                        propertyInstanceId,
                        new Dictionary<string, string?>
                        {
                            [leftSide ? "ModelLeftEntityInstanceId" : "ModelRightEntityInstanceId"] = rowInstanceId,
                            ["PropertyId"] = propertyId,
                            ["Value"] = propertyValue,
                        });
                    propertyCount++;

                    var tupleKey = CreatePropertyTupleKey(catalog.EntityId, row.Id, propertyId, propertyValue);
                    propertySet.Add(tupleKey);
                    propertyInstanceIdByTupleKey[tupleKey] = propertyInstanceId;
                    valueByEntityRowPropertyKey[CreateEntityPropertyIdentityKey(catalog.EntityId, row.Id, propertyId)] = propertyValue;
                }
            }
        }

        return new EqualSideData(
            RowSet: rowSet,
            EntityInstanceIdByRowKey: entityInstanceIdByRowKey,
            PropertySet: propertySet,
            PropertyInstanceIdByTupleKey: propertyInstanceIdByTupleKey,
            ValueByEntityRowPropertyKey: valueByEntityRowPropertyKey,
            RowCount: rowCount,
            PropertyCount: propertyCount);
    }

    private InstanceDiffBuildResult BuildEqualInstanceDiffWorkspace(
        Workspace leftWorkspace,
        Workspace rightWorkspace,
        string rightWorkspacePath)
    {
        var diffWorkspacePath = ResolveInstanceDiffOutputPath(rightWorkspacePath, "instance-diff");
        var diffWorkspace = CreateWorkspaceFromDefinition(InstanceDiffEqualWorkspaceDefinition.Value, diffWorkspacePath);
        var identityAllocator = new IdentityAllocator();

        var (modelId, catalogByEntityName) = BuildEqualEntityCatalog(leftWorkspace.Model, diffWorkspace, identityAllocator);
        var diffId = identityAllocator.NextId(DiffEntityName);
        AddDiffRecord(
            diffWorkspace,
            DiffEntityName,
            diffId,
            new Dictionary<string, string?>
            {
                ["Name"] = "instance-diff",
                ["ModelId"] = modelId,
                ["DiffModelVersion"] = "1.0",
            });

        var leftSide = BuildEqualSideData(
            leftWorkspace,
            diffWorkspace,
            catalogByEntityName,
            leftSide: true,
            diffId,
            identityAllocator);
        var rightSide = BuildEqualSideData(
            rightWorkspace,
            diffWorkspace,
            catalogByEntityName,
            leftSide: false,
            diffId,
            identityAllocator);

        var leftEntityNotInRight = leftSide.RowSet
            .Except(rightSide.RowSet, StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        var rightEntityNotInLeft = rightSide.RowSet
            .Except(leftSide.RowSet, StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        var sharedEntityKeys = leftSide.RowSet
            .Intersect(rightSide.RowSet, StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        static string ExtractEntityInstanceKeyFromPropertyTuple(string tupleKey)
        {
            var parts = tupleKey.Split('\n');
            if (parts.Length < 2)
            {
                throw new InvalidOperationException($"Invalid property tuple key '{tupleKey}'.");
            }

            return string.Join("\n", parts[0], parts[1]);
        }

        var leftNotInRight = leftSide.PropertySet
            .Except(rightSide.PropertySet, StringComparer.Ordinal)
            .Where(tupleKey => sharedEntityKeys.Contains(ExtractEntityInstanceKeyFromPropertyTuple(tupleKey)))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        var rightNotInLeft = rightSide.PropertySet
            .Except(leftSide.PropertySet, StringComparer.Ordinal)
            .Where(tupleKey => sharedEntityKeys.Contains(ExtractEntityInstanceKeyFromPropertyTuple(tupleKey)))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();

        foreach (var rowKey in leftEntityNotInRight)
        {
            if (!leftSide.EntityInstanceIdByRowKey.TryGetValue(rowKey, out var entityInstanceId))
            {
                continue;
            }

            AddDiffRecord(
                diffWorkspace,
                ModelLeftEntityInstanceNotInRightEntityName,
                identityAllocator.NextId(ModelLeftEntityInstanceNotInRightEntityName),
                new Dictionary<string, string?>
                {
                    ["ModelLeftEntityInstanceId"] = entityInstanceId,
                });
        }

        foreach (var rowKey in rightEntityNotInLeft)
        {
            if (!rightSide.EntityInstanceIdByRowKey.TryGetValue(rowKey, out var entityInstanceId))
            {
                continue;
            }

            AddDiffRecord(
                diffWorkspace,
                ModelRightEntityInstanceNotInLeftEntityName,
                identityAllocator.NextId(ModelRightEntityInstanceNotInLeftEntityName),
                new Dictionary<string, string?>
                {
                    ["ModelRightEntityInstanceId"] = entityInstanceId,
                });
        }

        foreach (var tupleKey in leftNotInRight)
        {
            if (!leftSide.PropertyInstanceIdByTupleKey.TryGetValue(tupleKey, out var propertyInstanceId))
            {
                continue;
            }

            AddDiffRecord(
                diffWorkspace,
                ModelLeftPropertyInstanceNotInRightEntityName,
                identityAllocator.NextId(ModelLeftPropertyInstanceNotInRightEntityName),
                new Dictionary<string, string?>
                {
                    ["ModelLeftPropertyInstanceId"] = propertyInstanceId,
                });
        }

        foreach (var tupleKey in rightNotInLeft)
        {
            if (!rightSide.PropertyInstanceIdByTupleKey.TryGetValue(tupleKey, out var propertyInstanceId))
            {
                continue;
            }

            AddDiffRecord(
                diffWorkspace,
                ModelRightPropertyInstanceNotInLeftEntityName,
                identityAllocator.NextId(ModelRightPropertyInstanceNotInLeftEntityName),
                new Dictionary<string, string?>
                {
                    ["ModelRightPropertyInstanceId"] = propertyInstanceId,
                });
        }

        var hasDifferences = leftEntityNotInRight.Count > 0 ||
                             rightEntityNotInLeft.Count > 0 ||
                             leftNotInRight.Count > 0 ||
                             rightNotInLeft.Count > 0 ||
                             !new HashSet<string>(leftSide.RowSet, StringComparer.Ordinal).SetEquals(rightSide.RowSet);

        return new InstanceDiffBuildResult(
            DiffWorkspace: diffWorkspace,
            DiffWorkspacePath: diffWorkspacePath,
            HasDifferences: hasDifferences,
            LeftRowCount: leftSide.RowCount,
            RightRowCount: rightSide.RowCount,
            LeftPropertyCount: leftSide.PropertyCount,
            RightPropertyCount: rightSide.PropertyCount,
            LeftNotInRightCount: leftNotInRight.Count,
            RightNotInLeftCount: rightNotInLeft.Count);
    }

    private static EqualDiffData ParseEqualDiffWorkspace(Workspace diffWorkspace)
    {
        if (!string.Equals(diffWorkspace.Model.Name, InstanceDiffEqualModelName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"workspace '{diffWorkspace.WorkspaceRootPath}' is not an {InstanceDiffEqualModelName} workspace.");
        }

        if (!IsModelContract(diffWorkspace.Model, InstanceDiffEqualModelSignature.Value))
        {
            throw new InvalidOperationException(
                $"workspace '{diffWorkspace.WorkspaceRootPath}' does not match the fixed {InstanceDiffEqualModelName} model contract.");
        }

        var diffRows = BuildRecordMap(diffWorkspace, DiffEntityName);
        if (diffRows.Count != 1)
        {
            throw new InvalidOperationException(
                $"workspace '{diffWorkspace.WorkspaceRootPath}' must contain exactly one '{DiffEntityName}' row.");
        }

        var diffRow = diffRows.Values.Single();
        var diffId = diffRow.Id;
        var modelId = RequireValue(diffRow, "ModelId", DiffEntityName);
        var diffModelVersion = RequireValue(diffRow, "DiffModelVersion", DiffEntityName);
        if (!string.Equals(diffModelVersion, "1.0", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Diff row '{diffId}' uses unsupported DiffModelVersion '{diffModelVersion}'.");
        }

        var modelRows = BuildRecordMap(diffWorkspace, ModelEntityName);
        if (!modelRows.ContainsKey(modelId))
        {
            throw new InvalidOperationException($"Diff row '{diffId}' references missing Model '{modelId}'.");
        }

        var entityRows = BuildRecordMap(diffWorkspace, EntityEntityName);
        var entityCatalogByName = new Dictionary<string, EqualEntityCatalog>(StringComparer.OrdinalIgnoreCase);
        var entityIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entityNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entityRow in entityRows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var entityId = entityRow.Id;
            var entityName = RequireValue(entityRow, "Name", EntityEntityName);
            var rowModelId = RequireValue(entityRow, "ModelId", EntityEntityName);
            if (!string.Equals(rowModelId, modelId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Entity '{entityId}' references ModelId '{rowModelId}', expected '{modelId}'.");
            }

            if (!entityIdByName.TryAdd(entityName, entityId))
            {
                throw new InvalidOperationException($"Entity table contains duplicate Name '{entityName}'.");
            }

            entityNameById[entityId] = entityName;
        }

        var propertyRows = BuildRecordMap(diffWorkspace, PropertyEntityName);
        var propertyNamesByEntityId = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var propertyNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var propertyIdByEntityAndName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var propertyRow in propertyRows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var propertyId = propertyRow.Id;
            var propertyName = RequireValue(propertyRow, "Name", PropertyEntityName);
            if (string.Equals(propertyName, "Id", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Property table must not include special identity property 'Id' (row '{propertyId}').");
            }

            var entityId = RequireValue(propertyRow, "EntityId", PropertyEntityName);
            if (!entityRows.ContainsKey(entityId))
            {
                throw new InvalidOperationException(
                    $"Property '{propertyId}' references missing Entity '{entityId}'.");
            }

            if (!propertyNamesByEntityId.TryGetValue(entityId, out var names))
            {
                names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                propertyNamesByEntityId[entityId] = names;
            }

            if (!names.Add(propertyName))
            {
                throw new InvalidOperationException(
                    $"Entity '{entityId}' has duplicate Property Name '{propertyName}' in diff workspace.");
            }

            propertyNameById[propertyId] = propertyName;
            propertyIdByEntityAndName[entityId + "\n" + propertyName] = propertyId;
        }

        var orderedPropertiesByEntity = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entityName in entityIdByName.Keys.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            var entityId = entityIdByName[entityName];
            var ordered = propertyNamesByEntityId.TryGetValue(entityId, out var names)
                ? names.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();
            orderedPropertiesByEntity[entityName] = ordered;

            var propertyIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var propertyName in ordered)
            {
                propertyIdByName[propertyName] = propertyIdByEntityAndName[entityId + "\n" + propertyName];
            }

            entityCatalogByName[entityName] = new EqualEntityCatalog(
                EntityId: entityId,
                EntityName: entityName,
                ModelEntity: new GenericEntity { Name = entityName },
                PropertyIdByName: propertyIdByName,
                OrderedPropertyNames: ordered);
        }

        var leftRows = ParseSideRows(
            diffWorkspace,
            ModelLeftEntityInstanceEntityName,
            "EntityId",
            entityRows,
            expectedDiffId: diffId);
        var rightRows = ParseSideRows(
            diffWorkspace,
            ModelRightEntityInstanceEntityName,
            "EntityId",
            entityRows,
            expectedDiffId: diffId);
        var leftProperties = ParseEqualSideProperties(
            diffWorkspace,
            ModelLeftPropertyInstanceEntityName,
            "ModelLeftEntityInstanceId",
            "PropertyId",
            leftRows.RowIdentityByRowInstanceId,
            propertyRows);
        var rightProperties = ParseEqualSideProperties(
            diffWorkspace,
            ModelRightPropertyInstanceEntityName,
            "ModelRightEntityInstanceId",
            "PropertyId",
            rightRows.RowIdentityByRowInstanceId,
            propertyRows);

        ValidateEqualEntityNotInRows(
            diffWorkspace,
            ModelLeftEntityInstanceNotInRightEntityName,
            "ModelLeftEntityInstanceId",
            leftRows.EntityInstanceIdByRowKey,
            leftRows.RowSet.Except(rightRows.RowSet, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal));
        ValidateEqualEntityNotInRows(
            diffWorkspace,
            ModelRightEntityInstanceNotInLeftEntityName,
            "ModelRightEntityInstanceId",
            rightRows.EntityInstanceIdByRowKey,
            rightRows.RowSet.Except(leftRows.RowSet, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal));

        var sharedEntityKeys = leftRows.RowSet
            .Intersect(rightRows.RowSet, StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        static string ExtractEntityInstanceKeyFromPropertyTupleForValidation(string tupleKey)
        {
            var parts = tupleKey.Split('\n');
            if (parts.Length < 2)
            {
                throw new InvalidOperationException($"Invalid property tuple key '{tupleKey}'.");
            }

            return string.Join("\n", parts[0], parts[1]);
        }

        ValidateEqualNotInRows(
            diffWorkspace,
            ModelLeftPropertyInstanceNotInRightEntityName,
            "ModelLeftPropertyInstanceId",
            leftProperties.PropertyInstanceIdByTupleKey,
            leftProperties.PropertySet
                .Except(rightProperties.PropertySet, StringComparer.Ordinal)
                .Where(tupleKey => sharedEntityKeys.Contains(ExtractEntityInstanceKeyFromPropertyTupleForValidation(tupleKey)))
                .ToHashSet(StringComparer.Ordinal));
        ValidateEqualNotInRows(
            diffWorkspace,
            ModelRightPropertyInstanceNotInLeftEntityName,
            "ModelRightPropertyInstanceId",
            rightProperties.PropertyInstanceIdByTupleKey,
            rightProperties.PropertySet
                .Except(leftProperties.PropertySet, StringComparer.Ordinal)
                .Where(tupleKey => sharedEntityKeys.Contains(ExtractEntityInstanceKeyFromPropertyTupleForValidation(tupleKey)))
                .ToHashSet(StringComparer.Ordinal));

        return new EqualDiffData(
            EntityCatalogByName: entityCatalogByName,
            OrderedPropertiesByEntity: orderedPropertiesByEntity,
            LeftRowSet: leftRows.RowSet,
            RightRowSet: rightRows.RowSet,
            LeftPropertySet: leftProperties.PropertySet,
            RightPropertySet: rightProperties.PropertySet,
            RightValueByEntityRowPropertyKey: rightProperties.ValueByEntityRowPropertyKey,
            DiffId: diffId);
    }

    private sealed record ParsedSideRows(
        IReadOnlyCollection<string> RowSet,
        IReadOnlyDictionary<string, string> EntityInstanceIdByRowKey,
        IReadOnlyDictionary<string, (string EntityId, string EntityInstanceIdentifier)> RowIdentityByRowInstanceId);

    private static ParsedSideRows ParseSideRows(
        Workspace diffWorkspace,
        string rowEntityName,
        string entityIdPropertyName,
        IReadOnlyDictionary<string, GenericRecord> entityRowsById,
        string expectedDiffId)
    {
        var rows = BuildRecordMap(diffWorkspace, rowEntityName);
        var rowSet = new HashSet<string>(StringComparer.Ordinal);
        var entityInstanceIdByRowKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var rowIdentityByRowInstanceId = new Dictionary<string, (string EntityId, string EntityInstanceIdentifier)>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var diffId = RequireValue(row, "DiffId", rowEntityName);
            if (!string.Equals(diffId, expectedDiffId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{rowEntityName} row '{row.Id}' references DiffId '{diffId}', expected '{expectedDiffId}'.");
            }

            var entityId = RequireValue(row, entityIdPropertyName, rowEntityName);
            if (!entityRowsById.TryGetValue(entityId, out var entityRow))
            {
                throw new InvalidOperationException(
                    $"{rowEntityName} row '{row.Id}' references missing Entity '{entityId}'.");
            }

            _ = entityRow;
            var entityInstanceIdentifier = RequireValue(row, "EntityInstanceIdentifier", rowEntityName);
            var rowKey = CreateEntityInstanceKey(entityId, entityInstanceIdentifier);
            rowIdentityByRowInstanceId[row.Id] = (entityId, entityInstanceIdentifier);
            rowSet.Add(rowKey);
            entityInstanceIdByRowKey[rowKey] = row.Id;
        }

        return new ParsedSideRows(rowSet, entityInstanceIdByRowKey, rowIdentityByRowInstanceId);
    }

    private sealed record ParsedEqualSideProperties(
        IReadOnlyCollection<string> PropertySet,
        IReadOnlyDictionary<string, string> PropertyInstanceIdByTupleKey,
        IReadOnlyDictionary<string, string> ValueByEntityRowPropertyKey);

    private static ParsedEqualSideProperties ParseEqualSideProperties(
        Workspace diffWorkspace,
        string propertyEntityName,
        string rowInstanceIdPropertyName,
        string propertyIdPropertyName,
        IReadOnlyDictionary<string, (string EntityId, string EntityInstanceIdentifier)> rowIdentityByRowInstanceId,
        IReadOnlyDictionary<string, GenericRecord> propertyRowsById)
    {
        var rows = BuildRecordMap(diffWorkspace, propertyEntityName);
        var propertySet = new HashSet<string>(StringComparer.Ordinal);
        var propertyInstanceIdByTupleKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var valueByEntityRowPropertyKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var identityKeySet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var rowInstanceId = RequireValue(row, rowInstanceIdPropertyName, propertyEntityName);
            if (!rowIdentityByRowInstanceId.TryGetValue(rowInstanceId, out var rowIdentity))
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} row '{row.Id}' references missing row instance '{rowInstanceId}'.");
            }

            var propertyId = RequireValue(row, propertyIdPropertyName, propertyEntityName);
            if (!propertyRowsById.ContainsKey(propertyId))
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} row '{row.Id}' references missing Property '{propertyId}'.");
            }

            if (!row.Values.TryGetValue("Value", out var storedValue))
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} row '{row.Id}' is missing required value 'Value'.");
            }

            if (storedValue == null)
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} row '{row.Id}' contains null for required value 'Value'.");
            }

            var value = storedValue;
            var tupleKey = CreatePropertyTupleKey(
                rowIdentity.EntityId,
                rowIdentity.EntityInstanceIdentifier,
                propertyId,
                value);
            var identityKey = CreateEntityPropertyIdentityKey(
                rowIdentity.EntityId,
                rowIdentity.EntityInstanceIdentifier,
                propertyId);
            if (!identityKeySet.Add(identityKey))
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} contains duplicate property identity '{identityKey}'.");
            }

            propertySet.Add(tupleKey);
            propertyInstanceIdByTupleKey[tupleKey] = row.Id;
            valueByEntityRowPropertyKey[identityKey] = value;
        }

        return new ParsedEqualSideProperties(
            PropertySet: propertySet,
            PropertyInstanceIdByTupleKey: propertyInstanceIdByTupleKey,
            ValueByEntityRowPropertyKey: valueByEntityRowPropertyKey);
    }

    private static void ValidateEqualEntityNotInRows(
        Workspace diffWorkspace,
        string notInEntityName,
        string entityInstanceIdFieldName,
        IReadOnlyDictionary<string, string> entityInstanceIdByRowKey,
        IReadOnlySet<string> expectedRowKeys)
    {
        var rows = BuildRecordMap(diffWorkspace, notInEntityName);
        var actualRowKeys = new HashSet<string>(StringComparer.Ordinal);
        var rowKeyByEntityInstanceId = entityInstanceIdByRowKey
            .ToDictionary(item => item.Value, item => item.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var entityInstanceId = RequireValue(row, entityInstanceIdFieldName, notInEntityName);
            if (!rowKeyByEntityInstanceId.TryGetValue(entityInstanceId, out var rowKey))
            {
                throw new InvalidOperationException(
                    $"{notInEntityName} row '{row.Id}' references missing entity instance '{entityInstanceId}'.");
            }

            actualRowKeys.Add(rowKey);
        }

        if (!actualRowKeys.SetEquals(expectedRowKeys))
        {
            throw new InvalidOperationException($"{notInEntityName} does not match computed set difference.");
        }
    }

    private static void ValidateEqualNotInRows(
        Workspace diffWorkspace,
        string notInEntityName,
        string propertyInstanceIdFieldName,
        IReadOnlyDictionary<string, string> propertyInstanceIdByTupleKey,
        IReadOnlySet<string> expectedTupleKeys)
    {
        var rows = BuildRecordMap(diffWorkspace, notInEntityName);
        var actualTupleKeys = new HashSet<string>(StringComparer.Ordinal);
        var tupleKeyByPropertyInstanceId = propertyInstanceIdByTupleKey
            .ToDictionary(item => item.Value, item => item.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var propertyInstanceId = RequireValue(row, propertyInstanceIdFieldName, notInEntityName);
            if (!tupleKeyByPropertyInstanceId.TryGetValue(propertyInstanceId, out var tupleKey))
            {
                throw new InvalidOperationException(
                    $"{notInEntityName} row '{row.Id}' references missing property instance '{propertyInstanceId}'.");
            }

            actualTupleKeys.Add(tupleKey);
        }

        if (!actualTupleKeys.SetEquals(expectedTupleKeys))
        {
            throw new InvalidOperationException($"{notInEntityName} does not match computed set difference.");
        }
    }

    private static string RequireValue(GenericRecord row, string key, string entityName)
    {
        if (!TryGetRecordFieldValue(row, key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Entity '{entityName}' row '{row.Id}' is missing required value '{key}'.");
        }

        return value;
    }

    private (HashSet<string> RowSet, HashSet<string> PropertySet, Dictionary<string, string> ValueByEntityRowPropertyKey)
        BuildWorkspaceSnapshotForEqualDiff(
            Workspace workspace,
            EqualDiffData diffData)
    {
        var rowSet = new HashSet<string>(StringComparer.Ordinal);
        var propertySet = new HashSet<string>(StringComparer.Ordinal);
        var valueByEntityRowPropertyKey = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var catalog in diffData.EntityCatalogByName.Values.OrderBy(item => item.EntityName, StringComparer.OrdinalIgnoreCase))
        {
            var entityName = catalog.EntityName;
            var entity = RequireEntity(workspace, entityName);
            var rows = BuildRecordMap(workspace, entityName);
            foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
            {
                rowSet.Add(CreateEntityInstanceKey(catalog.EntityId, row.Id));
                foreach (var propertyName in diffData.OrderedPropertiesByEntity[entityName])
                {
                    if (!TryGetPropertyLikeValue(entity, row, propertyName, out var value))
                    {
                        continue;
                    }

                    if (!catalog.PropertyIdByName.TryGetValue(propertyName, out var propertyId))
                    {
                        throw new InvalidOperationException(
                            $"Diff catalog for entity '{entityName}' is missing PropertyId for '{propertyName}'.");
                    }

                    propertySet.Add(CreatePropertyTupleKey(catalog.EntityId, row.Id, propertyId, value));
                    valueByEntityRowPropertyKey[CreateEntityPropertyIdentityKey(catalog.EntityId, row.Id, propertyId)] = value;
                }
            }
        }

        return (rowSet, propertySet, valueByEntityRowPropertyKey);
    }

    private void ApplyEqualRightSnapshotToWorkspace(
        Workspace targetWorkspace,
        EqualDiffData diffData)
    {
        foreach (var catalog in diffData.EntityCatalogByName.Values.OrderBy(item => item.EntityName, StringComparer.OrdinalIgnoreCase))
        {
            var entityName = catalog.EntityName;
            var entity = RequireEntity(targetWorkspace, entityName);
            var rightRowsForEntity = diffData.RightRowSet
                .Select(key =>
                {
                    var parts = key.Split('\n');
                    return (EntityId: UnescapeCanonical(parts[0]), EntityInstanceIdentifier: UnescapeCanonical(parts[1]));
                })
                .Where(item => string.Equals(item.EntityId, catalog.EntityId, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.EntityInstanceIdentifier)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var rows = targetWorkspace.Instance.GetOrCreateEntityRecords(entityName);
            rows.Clear();

            foreach (var rowId in rightRowsForEntity)
            {
                var row = new GenericRecord
                {
                    Id = rowId,
                };

                foreach (var propertyName in diffData.OrderedPropertiesByEntity[entityName])
                {
                    if (!catalog.PropertyIdByName.TryGetValue(propertyName, out var propertyId))
                    {
                        throw new InvalidOperationException(
                            $"Diff catalog for entity '{entityName}' is missing PropertyId for '{propertyName}'.");
                    }

                    var key = CreateEntityPropertyIdentityKey(catalog.EntityId, rowId, propertyId);
                    if (!diffData.RightValueByEntityRowPropertyKey.TryGetValue(key, out var value))
                    {
                        continue;
                    }

                    if (IsRelationshipProperty(entity, propertyName, out var relationshipEntity))
                    {
                        row.RelationshipIds[relationshipEntity] = value;
                    }
                    else
                    {
                        row.Values[propertyName] = value;
                    }
                }

                rows.Add(row);
            }
        }
    }

    private static string UnescapeCanonical(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static void CopyRowsByEntityWithIdentity(
        Workspace source,
        Workspace destination,
        string entityName,
        IdentityAllocator identityAllocator,
        IDictionary<string, Dictionary<string, string>> idMapByEntity,
        IReadOnlyDictionary<string, string>? referenceFields = null,
        IReadOnlySet<string>? includeSourceRowIds = null)
    {
        var sourceRows = BuildRecordMap(source, entityName).Values
            .Where(row => includeSourceRowIds == null || includeSourceRowIds.Contains(row.Id))
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceRow in sourceRows)
        {
            var newId = identityAllocator.NextId(entityName);
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in sourceRow.Values)
            {
                fields[pair.Key] = pair.Value;
            }

            foreach (var pair in sourceRow.RelationshipIds)
            {
                fields[pair.Key] = pair.Value;
            }

            foreach (var pair in fields.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(pair.Key, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (pair.Value == null)
                {
                    continue;
                }

                if (referenceFields != null &&
                    referenceFields.TryGetValue(pair.Key, out var referencedEntityName))
                {
                    if (!idMapByEntity.TryGetValue(referencedEntityName, out var referencedIdMap) ||
                        !referencedIdMap.TryGetValue(pair.Value, out var remappedId))
                    {
                        throw new InvalidOperationException(
                            $"{entityName} row '{sourceRow.Id}' references missing '{referencedEntityName}' id '{pair.Value}' via '{pair.Key}'.");
                    }

                    values[pair.Key] = remappedId;
                    continue;
                }

                values[pair.Key] = pair.Value;
            }

            AddDiffRecord(
                destination,
                entityName,
                newId,
                values);
            idMap[sourceRow.Id] = newId;
        }

        idMapByEntity[entityName] = idMap;
    }

    private static AlignmentCatalog ParseAlignmentCatalog(
        Workspace alignmentWorkspace,
        string expectedModelName,
        string expectedModelSignature)
    {
        if (!string.Equals(alignmentWorkspace.Model.Name, expectedModelName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"workspace '{alignmentWorkspace.WorkspaceRootPath}' is not an {expectedModelName} workspace.");
        }

        if (!IsModelContract(alignmentWorkspace.Model, expectedModelSignature))
        {
            throw new InvalidOperationException(
                $"workspace '{alignmentWorkspace.WorkspaceRootPath}' does not match the fixed {expectedModelName} model contract.");
        }

        var alignmentRows = BuildRecordMap(alignmentWorkspace, AlignmentEntityName);
        if (alignmentRows.Count != 1)
        {
            throw new InvalidOperationException(
                $"workspace '{alignmentWorkspace.WorkspaceRootPath}' must contain exactly one '{AlignmentEntityName}' row.");
        }

        var alignmentRow = alignmentRows.Values.Single();
        var alignmentId = alignmentRow.Id;
        var alignmentName = RequireValue(alignmentRow, "Name", AlignmentEntityName);
        var modelLeftId = RequireValue(alignmentRow, "ModelLeftId", AlignmentEntityName);
        var modelRightId = RequireValue(alignmentRow, "ModelRightId", AlignmentEntityName);

        var modelRows = BuildRecordMap(alignmentWorkspace, ModelEntityName);
        var modelLeftRows = BuildRecordMap(alignmentWorkspace, ModelLeftEntityName);
        var modelRightRows = BuildRecordMap(alignmentWorkspace, ModelRightEntityName);
        var modelLeftRow = modelLeftRows.TryGetValue(modelLeftId, out var modelLeft)
            ? modelLeft
            : throw new InvalidOperationException($"Alignment references missing ModelLeft '{modelLeftId}'.");
        var modelRightRow = modelRightRows.TryGetValue(modelRightId, out var modelRight)
            ? modelRight
            : throw new InvalidOperationException($"Alignment references missing ModelRight '{modelRightId}'.");

        var modelLeftModelId = RequireValue(modelLeftRow, "ModelId", ModelLeftEntityName);
        var modelRightModelId = RequireValue(modelRightRow, "ModelId", ModelRightEntityName);
        var modelLeftName = modelRows.TryGetValue(modelLeftModelId, out var modelLeftModel)
            ? RequireValue(modelLeftModel, "Name", ModelEntityName)
            : throw new InvalidOperationException($"ModelLeft '{modelLeftId}' references missing Model '{modelLeftModelId}'.");
        var modelRightName = modelRows.TryGetValue(modelRightModelId, out var modelRightModel)
            ? RequireValue(modelRightModel, "Name", ModelEntityName)
            : throw new InvalidOperationException($"ModelRight '{modelRightId}' references missing Model '{modelRightModelId}'.");

        var leftEntityRows = BuildRecordMap(alignmentWorkspace, ModelLeftEntityEntityName);
        var rightEntityRows = BuildRecordMap(alignmentWorkspace, ModelRightEntityEntityName);
        var leftEntityNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rightEntityNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in leftEntityRows.Values)
        {
            var rowModelLeftId = RequireValue(row, "ModelLeftId", ModelLeftEntityEntityName);
            if (!string.Equals(rowModelLeftId, modelLeftId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{ModelLeftEntityEntityName} row '{row.Id}' references ModelLeftId '{rowModelLeftId}', expected '{modelLeftId}'.");
            }

            leftEntityNameById[row.Id] = RequireValue(row, "Name", ModelLeftEntityEntityName);
        }

        foreach (var row in rightEntityRows.Values)
        {
            var rowModelRightId = RequireValue(row, "ModelRightId", ModelRightEntityEntityName);
            if (!string.Equals(rowModelRightId, modelRightId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{ModelRightEntityEntityName} row '{row.Id}' references ModelRightId '{rowModelRightId}', expected '{modelRightId}'.");
            }

            rightEntityNameById[row.Id] = RequireValue(row, "Name", ModelRightEntityEntityName);
        }

        var leftPropertyRows = BuildRecordMap(alignmentWorkspace, ModelLeftPropertyEntityName);
        var rightPropertyRows = BuildRecordMap(alignmentWorkspace, ModelRightPropertyEntityName);
        var leftPropertyNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rightPropertyNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var leftPropertyEntityIdByPropertyId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rightPropertyEntityIdByPropertyId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in leftPropertyRows.Values)
        {
            var entityId = RequireValue(row, "ModelLeftEntityId", ModelLeftPropertyEntityName);
            if (!leftEntityRows.ContainsKey(entityId))
            {
                throw new InvalidOperationException(
                    $"{ModelLeftPropertyEntityName} row '{row.Id}' references missing ModelLeftEntity '{entityId}'.");
            }

            leftPropertyNameById[row.Id] = RequireValue(row, "Name", ModelLeftPropertyEntityName);
            leftPropertyEntityIdByPropertyId[row.Id] = entityId;
        }

        foreach (var row in rightPropertyRows.Values)
        {
            var entityId = RequireValue(row, "ModelRightEntityId", ModelRightPropertyEntityName);
            if (!rightEntityRows.ContainsKey(entityId))
            {
                throw new InvalidOperationException(
                    $"{ModelRightPropertyEntityName} row '{row.Id}' references missing ModelRightEntity '{entityId}'.");
            }

            rightPropertyNameById[row.Id] = RequireValue(row, "Name", ModelRightPropertyEntityName);
            rightPropertyEntityIdByPropertyId[row.Id] = entityId;
        }

        var entityMapRows = BuildRecordMap(alignmentWorkspace, EntityMapEntityName);
        var entityMapById = new Dictionary<string, (string ModelLeftEntityId, string ModelRightEntityId)>(StringComparer.OrdinalIgnoreCase);
        var entityMapIdByLeftEntityId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entityMapIdByRightEntityId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in entityMapRows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var leftEntityId = RequireValue(row, "ModelLeftEntityId", EntityMapEntityName);
            var rightEntityId = RequireValue(row, "ModelRightEntityId", EntityMapEntityName);
            if (!leftEntityRows.ContainsKey(leftEntityId))
            {
                throw new InvalidOperationException(
                    $"{EntityMapEntityName} row '{row.Id}' references missing ModelLeftEntity '{leftEntityId}'.");
            }

            if (!rightEntityRows.ContainsKey(rightEntityId))
            {
                throw new InvalidOperationException(
                    $"{EntityMapEntityName} row '{row.Id}' references missing ModelRightEntity '{rightEntityId}'.");
            }

            if (!entityMapIdByLeftEntityId.TryAdd(leftEntityId, row.Id))
            {
                throw new InvalidOperationException(
                    $"{EntityMapEntityName} has multiple mappings for ModelLeftEntityId '{leftEntityId}'.");
            }

            if (!entityMapIdByRightEntityId.TryAdd(rightEntityId, row.Id))
            {
                throw new InvalidOperationException(
                    $"{EntityMapEntityName} has multiple mappings for ModelRightEntityId '{rightEntityId}'.");
            }

            entityMapById[row.Id] = (leftEntityId, rightEntityId);
        }

        var propertyMapRows = BuildRecordMap(alignmentWorkspace, PropertyMapEntityName);
        var propertyMapById = new Dictionary<string, (string ModelLeftPropertyId, string ModelRightPropertyId)>(StringComparer.OrdinalIgnoreCase);
        var propertyMapIdsByEntityMapId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var leftPropertyMapSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rightPropertyMapSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in propertyMapRows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var leftPropertyId = RequireValue(row, "ModelLeftPropertyId", PropertyMapEntityName);
            var rightPropertyId = RequireValue(row, "ModelRightPropertyId", PropertyMapEntityName);
            if (!leftPropertyRows.ContainsKey(leftPropertyId))
            {
                throw new InvalidOperationException(
                    $"{PropertyMapEntityName} row '{row.Id}' references missing ModelLeftProperty '{leftPropertyId}'.");
            }

            if (!rightPropertyRows.ContainsKey(rightPropertyId))
            {
                throw new InvalidOperationException(
                    $"{PropertyMapEntityName} row '{row.Id}' references missing ModelRightProperty '{rightPropertyId}'.");
            }

            var leftPropertyName = leftPropertyNameById[leftPropertyId];
            var rightPropertyName = rightPropertyNameById[rightPropertyId];
            if (string.Equals(leftPropertyName, "Id", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rightPropertyName, "Id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!leftPropertyMapSeen.Add(leftPropertyId))
            {
                throw new InvalidOperationException(
                    $"{PropertyMapEntityName} has multiple mappings for ModelLeftPropertyId '{leftPropertyId}'.");
            }

            if (!rightPropertyMapSeen.Add(rightPropertyId))
            {
                throw new InvalidOperationException(
                    $"{PropertyMapEntityName} has multiple mappings for ModelRightPropertyId '{rightPropertyId}'.");
            }

            var leftEntityId = leftPropertyEntityIdByPropertyId[leftPropertyId];
            var rightEntityId = rightPropertyEntityIdByPropertyId[rightPropertyId];
            if (!entityMapIdByLeftEntityId.TryGetValue(leftEntityId, out var entityMapId) ||
                !entityMapById.TryGetValue(entityMapId, out var mappedEntityIds) ||
                !string.Equals(mappedEntityIds.ModelRightEntityId, rightEntityId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{PropertyMapEntityName} row '{row.Id}' maps properties across entities without a matching EntityMap.");
            }

            propertyMapById[row.Id] = (leftPropertyId, rightPropertyId);
            if (!propertyMapIdsByEntityMapId.TryGetValue(entityMapId, out var mapIds))
            {
                mapIds = new List<string>();
                propertyMapIdsByEntityMapId[entityMapId] = mapIds;
            }

            mapIds.Add(row.Id);
        }

        var normalizedPropertyMapIdsByEntityMapId = propertyMapIdsByEntityMapId.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        return new AlignmentCatalog(
            AlignmentId: alignmentId,
            AlignmentName: alignmentName,
            ModelLeftId: modelLeftId,
            ModelRightId: modelRightId,
            ModelLeftName: modelLeftName,
            ModelRightName: modelRightName,
            LeftEntityNameById: leftEntityNameById,
            RightEntityNameById: rightEntityNameById,
            LeftPropertyNameById: leftPropertyNameById,
            RightPropertyNameById: rightPropertyNameById,
            LeftPropertyEntityIdByPropertyId: leftPropertyEntityIdByPropertyId,
            RightPropertyEntityIdByPropertyId: rightPropertyEntityIdByPropertyId,
            EntityMapById: entityMapById,
            PropertyMapById: propertyMapById,
            EntityMapIdByLeftEntityId: entityMapIdByLeftEntityId,
            EntityMapIdByRightEntityId: entityMapIdByRightEntityId,
            PropertyMapIdsByEntityMapId: normalizedPropertyMapIdsByEntityMapId);
    }

    private void ValidateWorkspaceMatchesAlignment(
        Workspace workspace,
        string expectedModelName,
        IReadOnlyDictionary<string, string> entityNameById,
        IReadOnlyDictionary<string, string> propertyNameById,
        IReadOnlyDictionary<string, string> propertyEntityIdByPropertyId)
    {
        if (!string.Equals(workspace.Model.Name, expectedModelName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Workspace model '{workspace.Model.Name}' does not match expected model '{expectedModelName}'.");
        }

        foreach (var entityName in entityNameById.Values.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            if (workspace.Model.FindEntity(entityName) == null)
            {
                throw new InvalidOperationException(
                    $"Workspace model '{workspace.Model.Name}' is missing aligned entity '{entityName}'.");
            }
        }

        foreach (var propertyId in propertyNameById.Keys.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            var propertyName = propertyNameById[propertyId];
            var entityId = propertyEntityIdByPropertyId[propertyId];
            if (!entityNameById.TryGetValue(entityId, out var entityName))
            {
                throw new InvalidOperationException(
                    $"Alignment property '{propertyId}' references missing entity '{entityId}'.");
            }

            var entity = workspace.Model.FindEntity(entityName)
                         ?? throw new InvalidOperationException(
                             $"Workspace model '{workspace.Model.Name}' is missing aligned entity '{entityName}'.");
            var hasDirectProperty = entity.Properties.Any(item =>
                string.Equals(item.Name, propertyName, StringComparison.OrdinalIgnoreCase));
            var hasRelationshipProperty = IsRelationshipProperty(entity, propertyName, out _);
            if (!hasDirectProperty && !hasRelationshipProperty)
            {
                throw new InvalidOperationException(
                    $"Workspace model '{workspace.Model.Name}' entity '{entityName}' is missing aligned property '{propertyName}'.");
            }
        }
    }

    private AlignedSideData BuildAlignedSideData(
        Workspace sourceWorkspace,
        Workspace diffWorkspace,
        AlignmentCatalog alignment,
        bool leftSide,
        IdentityAllocator identityAllocator)
    {
        var rowEntityName = leftSide ? ModelLeftEntityInstanceEntityName : ModelRightEntityInstanceEntityName;
        var propertyEntityName = leftSide ? ModelLeftPropertyInstanceEntityName : ModelRightPropertyInstanceEntityName;
        var entityNameById = leftSide ? alignment.LeftEntityNameById : alignment.RightEntityNameById;
        var propertyNameById = leftSide ? alignment.LeftPropertyNameById : alignment.RightPropertyNameById;

        var rowSet = new HashSet<string>(StringComparer.Ordinal);
        var entityInstanceIdByRowKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var propertySet = new HashSet<string>(StringComparer.Ordinal);
        var propertyInstanceIdByTupleKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var valueByEntityMapRowPropertyMapKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var rowCount = 0;
        var propertyCount = 0;

        foreach (var entityMap in alignment.EntityMapById.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var entityMapId = entityMap.Key;
            var modelEntityId = leftSide ? entityMap.Value.ModelLeftEntityId : entityMap.Value.ModelRightEntityId;
            if (!entityNameById.TryGetValue(modelEntityId, out var entityName))
            {
                throw new InvalidOperationException(
                    $"Alignment EntityMap '{entityMapId}' references missing {(leftSide ? "ModelLeftEntity" : "ModelRightEntity")} '{modelEntityId}'.");
            }

            var modelEntity = RequireEntity(sourceWorkspace, entityName);
            var rows = BuildRecordMap(sourceWorkspace, entityName);
            foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
            {
                var rowInstanceId = identityAllocator.NextId(rowEntityName);
                AddDiffRecord(
                    diffWorkspace,
                    rowEntityName,
                    rowInstanceId,
                    new Dictionary<string, string?>
                    {
                        [leftSide ? "ModelLeftEntityId" : "ModelRightEntityId"] = modelEntityId,
                        ["EntityInstanceIdentifier"] = row.Id,
                    });
                rowCount++;
                var rowKey = CreateAlignedRowKey(entityMapId, row.Id);
                rowSet.Add(rowKey);
                entityInstanceIdByRowKey[rowKey] = rowInstanceId;

                if (!alignment.PropertyMapIdsByEntityMapId.TryGetValue(entityMapId, out var propertyMapIds))
                {
                    continue;
                }

                foreach (var propertyMapId in propertyMapIds)
                {
                    if (!alignment.PropertyMapById.TryGetValue(propertyMapId, out var propertyMap))
                    {
                        continue;
                    }

                    var modelPropertyId = leftSide ? propertyMap.ModelLeftPropertyId : propertyMap.ModelRightPropertyId;
                    if (!propertyNameById.TryGetValue(modelPropertyId, out var propertyName))
                    {
                        throw new InvalidOperationException(
                            $"Alignment PropertyMap '{propertyMapId}' references missing property '{modelPropertyId}'.");
                    }

                    if (!TryGetPropertyLikeValue(modelEntity, row, propertyName, out var value))
                    {
                        continue;
                    }

                    var propertyInstanceId = identityAllocator.NextId(propertyEntityName);
                    AddDiffRecord(
                        diffWorkspace,
                        propertyEntityName,
                        propertyInstanceId,
                        new Dictionary<string, string?>
                        {
                            [leftSide ? "ModelLeftEntityInstanceId" : "ModelRightEntityInstanceId"] = rowInstanceId,
                            [leftSide ? "ModelLeftPropertyId" : "ModelRightPropertyId"] = modelPropertyId,
                            ["Value"] = value,
                        });
                    propertyCount++;

                    var tupleKey = CreateAlignedPropertyTupleKey(entityMapId, row.Id, propertyMapId, value);
                    propertySet.Add(tupleKey);
                    propertyInstanceIdByTupleKey[tupleKey] = propertyInstanceId;
                    valueByEntityMapRowPropertyMapKey[CreateAlignedEntityRowPropertyMapKey(entityMapId, row.Id, propertyMapId)] = value;
                }
            }
        }

        return new AlignedSideData(
            RowSet: rowSet,
            EntityInstanceIdByRowKey: entityInstanceIdByRowKey,
            PropertySet: propertySet,
            PropertyInstanceIdByTupleKey: propertyInstanceIdByTupleKey,
            ValueByEntityMapRowPropertyMapKey: valueByEntityMapRowPropertyMapKey,
            RowCount: rowCount,
            PropertyCount: propertyCount);
    }

    private InstanceDiffBuildResult BuildAlignedInstanceDiffWorkspace(
        Workspace leftWorkspace,
        Workspace rightWorkspace,
        Workspace alignmentWorkspace,
        AlignmentCatalog alignment,
        string rightWorkspacePath)
    {
        var diffWorkspacePath = ResolveInstanceDiffOutputPath(rightWorkspacePath, "instance-diff-aligned");
        var diffWorkspace = CreateWorkspaceFromDefinition(InstanceDiffAlignedWorkspaceDefinition.Value, diffWorkspacePath);
        var identityAllocator = new IdentityAllocator();
        var idMapByEntity = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var includedPropertyMapIds = alignment.PropertyMapById.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includedLeftPropertyIds = alignment.PropertyMapById.Values
            .Select(item => item.ModelLeftPropertyId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includedRightPropertyIds = alignment.PropertyMapById.Values
            .Select(item => item.ModelRightPropertyId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entityName in new[]
                 {
                     ModelEntityName,
                     ModelLeftEntityName,
                     ModelRightEntityName,
                     AlignmentEntityName,
                     ModelLeftEntityEntityName,
                     ModelRightEntityEntityName,
                     ModelLeftPropertyEntityName,
                     ModelRightPropertyEntityName,
                     EntityMapEntityName,
                     PropertyMapEntityName,
                 })
        {
            var referenceFields = AlignmentReferenceFieldsByEntity.TryGetValue(entityName, out var fields)
                ? fields
                : null;
            IReadOnlySet<string>? includeSourceRowIds = null;
            if (string.Equals(entityName, PropertyMapEntityName, StringComparison.OrdinalIgnoreCase))
            {
                includeSourceRowIds = includedPropertyMapIds;
            }
            else if (string.Equals(entityName, ModelLeftPropertyEntityName, StringComparison.OrdinalIgnoreCase))
            {
                includeSourceRowIds = includedLeftPropertyIds;
            }
            else if (string.Equals(entityName, ModelRightPropertyEntityName, StringComparison.OrdinalIgnoreCase))
            {
                includeSourceRowIds = includedRightPropertyIds;
            }

            CopyRowsByEntityWithIdentity(
                alignmentWorkspace,
                diffWorkspace,
                entityName,
                identityAllocator,
                idMapByEntity,
                referenceFields,
                includeSourceRowIds);
        }

        var diffAlignment = ParseAlignmentCatalog(
            diffWorkspace,
            InstanceDiffAlignedModelName,
            InstanceDiffAlignedModelSignature.Value);
        var leftSide = BuildAlignedSideData(
            leftWorkspace,
            diffWorkspace,
            diffAlignment,
            leftSide: true,
            identityAllocator);
        var rightSide = BuildAlignedSideData(
            rightWorkspace,
            diffWorkspace,
            diffAlignment,
            leftSide: false,
            identityAllocator);

        var leftEntityNotInRight = leftSide.RowSet
            .Except(rightSide.RowSet, StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        var rightEntityNotInLeft = rightSide.RowSet
            .Except(leftSide.RowSet, StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        var sharedEntityKeys = leftSide.RowSet
            .Intersect(rightSide.RowSet, StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        static string ExtractAlignedEntityKeyFromPropertyTuple(string tupleKey)
        {
            var parts = tupleKey.Split('\n');
            if (parts.Length < 2)
            {
                throw new InvalidOperationException($"Invalid aligned property tuple key '{tupleKey}'.");
            }

            return string.Join("\n", parts[0], parts[1]);
        }

        var leftNotInRight = leftSide.PropertySet
            .Except(rightSide.PropertySet, StringComparer.Ordinal)
            .Where(tupleKey => sharedEntityKeys.Contains(ExtractAlignedEntityKeyFromPropertyTuple(tupleKey)))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        var rightNotInLeft = rightSide.PropertySet
            .Except(leftSide.PropertySet, StringComparer.Ordinal)
            .Where(tupleKey => sharedEntityKeys.Contains(ExtractAlignedEntityKeyFromPropertyTuple(tupleKey)))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();

        foreach (var rowKey in leftEntityNotInRight)
        {
            if (!leftSide.EntityInstanceIdByRowKey.TryGetValue(rowKey, out var entityInstanceId))
            {
                continue;
            }

            AddDiffRecord(
                diffWorkspace,
                ModelLeftEntityInstanceNotInRightEntityName,
                identityAllocator.NextId(ModelLeftEntityInstanceNotInRightEntityName),
                new Dictionary<string, string?>
                {
                    ["ModelLeftEntityInstanceId"] = entityInstanceId,
                });
        }

        foreach (var rowKey in rightEntityNotInLeft)
        {
            if (!rightSide.EntityInstanceIdByRowKey.TryGetValue(rowKey, out var entityInstanceId))
            {
                continue;
            }

            AddDiffRecord(
                diffWorkspace,
                ModelRightEntityInstanceNotInLeftEntityName,
                identityAllocator.NextId(ModelRightEntityInstanceNotInLeftEntityName),
                new Dictionary<string, string?>
                {
                    ["ModelRightEntityInstanceId"] = entityInstanceId,
                });
        }

        foreach (var tupleKey in leftNotInRight)
        {
            if (!leftSide.PropertyInstanceIdByTupleKey.TryGetValue(tupleKey, out var propertyInstanceId))
            {
                continue;
            }

            AddDiffRecord(
                diffWorkspace,
                ModelLeftPropertyInstanceNotInRightEntityName,
                identityAllocator.NextId(ModelLeftPropertyInstanceNotInRightEntityName),
                new Dictionary<string, string?>
                {
                    ["ModelLeftPropertyInstanceId"] = propertyInstanceId,
                });
        }

        foreach (var tupleKey in rightNotInLeft)
        {
            if (!rightSide.PropertyInstanceIdByTupleKey.TryGetValue(tupleKey, out var propertyInstanceId))
            {
                continue;
            }

            AddDiffRecord(
                diffWorkspace,
                ModelRightPropertyInstanceNotInLeftEntityName,
                identityAllocator.NextId(ModelRightPropertyInstanceNotInLeftEntityName),
                new Dictionary<string, string?>
                {
                    ["ModelRightPropertyInstanceId"] = propertyInstanceId,
                });
        }

        var hasDifferences = leftEntityNotInRight.Count > 0 ||
                             rightEntityNotInLeft.Count > 0 ||
                             leftNotInRight.Count > 0 ||
                             rightNotInLeft.Count > 0 ||
                             !new HashSet<string>(leftSide.RowSet, StringComparer.Ordinal).SetEquals(rightSide.RowSet);

        return new InstanceDiffBuildResult(
            DiffWorkspace: diffWorkspace,
            DiffWorkspacePath: diffWorkspacePath,
            HasDifferences: hasDifferences,
            LeftRowCount: leftSide.RowCount,
            RightRowCount: rightSide.RowCount,
            LeftPropertyCount: leftSide.PropertyCount,
            RightPropertyCount: rightSide.PropertyCount,
            LeftNotInRightCount: leftNotInRight.Count,
            RightNotInLeftCount: rightNotInLeft.Count);
    }

    private AlignedDiffData ParseAlignedDiffWorkspace(Workspace diffWorkspace)
    {
        var alignment = ParseAlignmentCatalog(
            diffWorkspace,
            InstanceDiffAlignedModelName,
            InstanceDiffAlignedModelSignature.Value);

        var leftRows = ParseAlignedSideRows(
            diffWorkspace,
            ModelLeftEntityInstanceEntityName,
            "ModelLeftEntityId",
            alignment,
            leftSide: true);
        var rightRows = ParseAlignedSideRows(
            diffWorkspace,
            ModelRightEntityInstanceEntityName,
            "ModelRightEntityId",
            alignment,
            leftSide: false);
        var leftProperties = ParseAlignedSideProperties(
            diffWorkspace,
            ModelLeftPropertyInstanceEntityName,
            "ModelLeftEntityInstanceId",
            "ModelLeftPropertyId",
            leftRows.RowIdentityByRowInstanceId,
            alignment,
            leftSide: true);
        var rightProperties = ParseAlignedSideProperties(
            diffWorkspace,
            ModelRightPropertyInstanceEntityName,
            "ModelRightEntityInstanceId",
            "ModelRightPropertyId",
            rightRows.RowIdentityByRowInstanceId,
            alignment,
            leftSide: false);

        ValidateAlignedEntityNotInRows(
            diffWorkspace,
            ModelLeftEntityInstanceNotInRightEntityName,
            "ModelLeftEntityInstanceId",
            leftRows.EntityInstanceIdByRowKey,
            leftRows.RowSet.Except(rightRows.RowSet, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal));
        ValidateAlignedEntityNotInRows(
            diffWorkspace,
            ModelRightEntityInstanceNotInLeftEntityName,
            "ModelRightEntityInstanceId",
            rightRows.EntityInstanceIdByRowKey,
            rightRows.RowSet.Except(leftRows.RowSet, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal));

        var sharedEntityKeys = leftRows.RowSet
            .Intersect(rightRows.RowSet, StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        static string ExtractAlignedEntityKeyFromPropertyTupleForValidation(string tupleKey)
        {
            var parts = tupleKey.Split('\n');
            if (parts.Length < 2)
            {
                throw new InvalidOperationException($"Invalid aligned property tuple key '{tupleKey}'.");
            }

            return string.Join("\n", parts[0], parts[1]);
        }

        ValidateAlignedNotInRows(
            diffWorkspace,
            ModelLeftPropertyInstanceNotInRightEntityName,
            "ModelLeftPropertyInstanceId",
            leftProperties.PropertyInstanceIdByTupleKey,
            leftProperties.PropertySet
                .Except(rightProperties.PropertySet, StringComparer.Ordinal)
                .Where(tupleKey => sharedEntityKeys.Contains(ExtractAlignedEntityKeyFromPropertyTupleForValidation(tupleKey)))
                .ToHashSet(StringComparer.Ordinal));
        ValidateAlignedNotInRows(
            diffWorkspace,
            ModelRightPropertyInstanceNotInLeftEntityName,
            "ModelRightPropertyInstanceId",
            rightProperties.PropertyInstanceIdByTupleKey,
            rightProperties.PropertySet
                .Except(leftProperties.PropertySet, StringComparer.Ordinal)
                .Where(tupleKey => sharedEntityKeys.Contains(ExtractAlignedEntityKeyFromPropertyTupleForValidation(tupleKey)))
                .ToHashSet(StringComparer.Ordinal));

        return new AlignedDiffData(
            Alignment: alignment,
            LeftRowSet: leftRows.RowSet,
            RightRowSet: rightRows.RowSet,
            LeftPropertySet: leftProperties.PropertySet,
            RightPropertySet: rightProperties.PropertySet,
            RightValueByEntityMapRowPropertyMapKey: rightProperties.ValueByEntityMapRowPropertyMapKey);
    }

    private sealed record ParsedAlignedSideRows(
        IReadOnlyCollection<string> RowSet,
        IReadOnlyDictionary<string, string> EntityInstanceIdByRowKey,
        IReadOnlyDictionary<string, (string EntityMapId, string EntityInstanceIdentifier)> RowIdentityByRowInstanceId);

    private static ParsedAlignedSideRows ParseAlignedSideRows(
        Workspace diffWorkspace,
        string rowEntityName,
        string entityIdPropertyName,
        AlignmentCatalog alignment,
        bool leftSide)
    {
        var rows = BuildRecordMap(diffWorkspace, rowEntityName);
        var rowSet = new HashSet<string>(StringComparer.Ordinal);
        var entityInstanceIdByRowKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var rowIdentityByRowInstanceId =
            new Dictionary<string, (string EntityMapId, string EntityInstanceIdentifier)>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var modelEntityId = RequireValue(row, entityIdPropertyName, rowEntityName);
            var entityMapId = leftSide
                ? alignment.EntityMapIdByLeftEntityId.TryGetValue(modelEntityId, out var mappedByLeft)
                    ? mappedByLeft
                    : string.Empty
                : alignment.EntityMapIdByRightEntityId.TryGetValue(modelEntityId, out var mappedByRight)
                    ? mappedByRight
                    : string.Empty;
            if (string.IsNullOrWhiteSpace(entityMapId))
            {
                throw new InvalidOperationException(
                    $"{rowEntityName} row '{row.Id}' references unmapped entity id '{modelEntityId}'.");
            }

            var entityInstanceIdentifier = RequireValue(row, "EntityInstanceIdentifier", rowEntityName);
            var rowKey = CreateAlignedRowKey(entityMapId, entityInstanceIdentifier);
            rowSet.Add(rowKey);
            entityInstanceIdByRowKey[rowKey] = row.Id;
            rowIdentityByRowInstanceId[row.Id] = (entityMapId, entityInstanceIdentifier);
        }

        return new ParsedAlignedSideRows(rowSet, entityInstanceIdByRowKey, rowIdentityByRowInstanceId);
    }

    private sealed record ParsedAlignedSideProperties(
        IReadOnlyCollection<string> PropertySet,
        IReadOnlyDictionary<string, string> PropertyInstanceIdByTupleKey,
        IReadOnlyDictionary<string, string> ValueByEntityMapRowPropertyMapKey);

    private static ParsedAlignedSideProperties ParseAlignedSideProperties(
        Workspace diffWorkspace,
        string propertyEntityName,
        string rowInstanceIdPropertyName,
        string propertyIdPropertyName,
        IReadOnlyDictionary<string, (string EntityMapId, string EntityInstanceIdentifier)> rowIdentityByRowInstanceId,
        AlignmentCatalog alignment,
        bool leftSide)
    {
        var rows = BuildRecordMap(diffWorkspace, propertyEntityName);
        var propertySet = new HashSet<string>(StringComparer.Ordinal);
        var propertyInstanceIdByTupleKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var valueByEntityMapRowPropertyMapKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var identityKeys = new HashSet<string>(StringComparer.Ordinal);

        var propertyMapIdByLeftPropertyId = alignment.PropertyMapById
            .ToDictionary(pair => pair.Value.ModelLeftPropertyId, pair => pair.Key, StringComparer.OrdinalIgnoreCase);
        var propertyMapIdByRightPropertyId = alignment.PropertyMapById
            .ToDictionary(pair => pair.Value.ModelRightPropertyId, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var rowInstanceId = RequireValue(row, rowInstanceIdPropertyName, propertyEntityName);
            if (!rowIdentityByRowInstanceId.TryGetValue(rowInstanceId, out var rowIdentity))
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} row '{row.Id}' references missing row instance '{rowInstanceId}'.");
            }

            var modelPropertyId = RequireValue(row, propertyIdPropertyName, propertyEntityName);
            var propertyMapId = leftSide
                ? propertyMapIdByLeftPropertyId.TryGetValue(modelPropertyId, out var mappedLeft)
                    ? mappedLeft
                    : string.Empty
                : propertyMapIdByRightPropertyId.TryGetValue(modelPropertyId, out var mappedRight)
                    ? mappedRight
                    : string.Empty;
            if (string.IsNullOrWhiteSpace(propertyMapId))
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} row '{row.Id}' references unmapped property id '{modelPropertyId}'.");
            }

            if (!row.Values.TryGetValue("Value", out var storedValue))
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} row '{row.Id}' is missing required value 'Value'.");
            }

            if (storedValue == null)
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} row '{row.Id}' contains null for required value 'Value'.");
            }

            var value = storedValue;
            var tupleKey = CreateAlignedPropertyTupleKey(
                rowIdentity.EntityMapId,
                rowIdentity.EntityInstanceIdentifier,
                propertyMapId,
                value);
            var identityKey = CreateAlignedEntityRowPropertyMapKey(
                rowIdentity.EntityMapId,
                rowIdentity.EntityInstanceIdentifier,
                propertyMapId);
            if (!identityKeys.Add(identityKey))
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} contains duplicate mapped property identity '{identityKey}'.");
            }

            propertySet.Add(tupleKey);
            propertyInstanceIdByTupleKey[tupleKey] = row.Id;
            valueByEntityMapRowPropertyMapKey[identityKey] = value;
        }

        return new ParsedAlignedSideProperties(
            PropertySet: propertySet,
            PropertyInstanceIdByTupleKey: propertyInstanceIdByTupleKey,
            ValueByEntityMapRowPropertyMapKey: valueByEntityMapRowPropertyMapKey);
    }

    private static void ValidateAlignedNotInRows(
        Workspace diffWorkspace,
        string notInEntityName,
        string propertyInstanceIdFieldName,
        IReadOnlyDictionary<string, string> propertyInstanceIdByTupleKey,
        IReadOnlySet<string> expectedTupleKeys)
    {
        var rows = BuildRecordMap(diffWorkspace, notInEntityName);
        var actualTupleKeys = new HashSet<string>(StringComparer.Ordinal);
        var tupleKeyByPropertyInstanceId = propertyInstanceIdByTupleKey
            .ToDictionary(item => item.Value, item => item.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var propertyInstanceId = RequireValue(row, propertyInstanceIdFieldName, notInEntityName);
            if (!tupleKeyByPropertyInstanceId.TryGetValue(propertyInstanceId, out var tupleKey))
            {
                throw new InvalidOperationException(
                    $"{notInEntityName} row '{row.Id}' references missing property instance '{propertyInstanceId}'.");
            }

            actualTupleKeys.Add(tupleKey);
        }

        if (!actualTupleKeys.SetEquals(expectedTupleKeys))
        {
            throw new InvalidOperationException($"{notInEntityName} does not match computed set difference.");
        }
    }

    private static void ValidateAlignedEntityNotInRows(
        Workspace diffWorkspace,
        string notInEntityName,
        string entityInstanceIdFieldName,
        IReadOnlyDictionary<string, string> entityInstanceIdByRowKey,
        IReadOnlySet<string> expectedRowKeys)
    {
        var rows = BuildRecordMap(diffWorkspace, notInEntityName);
        var actualRowKeys = new HashSet<string>(StringComparer.Ordinal);
        var rowKeyByEntityInstanceId = entityInstanceIdByRowKey
            .ToDictionary(item => item.Value, item => item.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var entityInstanceId = RequireValue(row, entityInstanceIdFieldName, notInEntityName);
            if (!rowKeyByEntityInstanceId.TryGetValue(entityInstanceId, out var rowKey))
            {
                throw new InvalidOperationException(
                    $"{notInEntityName} row '{row.Id}' references missing entity instance '{entityInstanceId}'.");
            }

            actualRowKeys.Add(rowKey);
        }

        if (!actualRowKeys.SetEquals(expectedRowKeys))
        {
            throw new InvalidOperationException($"{notInEntityName} does not match computed set difference.");
        }
    }

    private (
        HashSet<string> RowSet,
        HashSet<string> PropertySet,
        Dictionary<string, string> ValueByEntityMapRowPropertyMapKey)
        BuildWorkspaceSnapshotForAlignedDiff(
            Workspace workspace,
            AlignmentCatalog alignment)
    {
        var rowSet = new HashSet<string>(StringComparer.Ordinal);
        var propertySet = new HashSet<string>(StringComparer.Ordinal);
        var valueByEntityMapRowPropertyMapKey = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entityMap in alignment.EntityMapById.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var entityMapId = entityMap.Key;
            var leftEntityId = entityMap.Value.ModelLeftEntityId;
            if (!alignment.LeftEntityNameById.TryGetValue(leftEntityId, out var entityName))
            {
                throw new InvalidOperationException(
                    $"EntityMap '{entityMapId}' references missing ModelLeftEntity '{leftEntityId}'.");
            }

            var entity = RequireEntity(workspace, entityName);
            var rows = BuildRecordMap(workspace, entityName);
            foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
            {
                rowSet.Add(CreateAlignedRowKey(entityMapId, row.Id));
                if (!alignment.PropertyMapIdsByEntityMapId.TryGetValue(entityMapId, out var propertyMapIds))
                {
                    continue;
                }

                foreach (var propertyMapId in propertyMapIds)
                {
                    if (!alignment.PropertyMapById.TryGetValue(propertyMapId, out var propertyMap))
                    {
                        continue;
                    }

                    if (!alignment.LeftPropertyNameById.TryGetValue(propertyMap.ModelLeftPropertyId, out var propertyName))
                    {
                        continue;
                    }

                    if (!TryGetPropertyLikeValue(entity, row, propertyName, out var value))
                    {
                        continue;
                    }

                    propertySet.Add(CreateAlignedPropertyTupleKey(entityMapId, row.Id, propertyMapId, value));
                    valueByEntityMapRowPropertyMapKey[CreateAlignedEntityRowPropertyMapKey(entityMapId, row.Id, propertyMapId)] = value;
                }
            }
        }

        return (rowSet, propertySet, valueByEntityMapRowPropertyMapKey);
    }

    private void ApplyAlignedRightSnapshotToWorkspace(
        Workspace targetWorkspace,
        AlignedDiffData diffData)
    {
        var alignment = diffData.Alignment;
        foreach (var entityMap in alignment.EntityMapById.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var entityMapId = entityMap.Key;
            var leftEntityId = entityMap.Value.ModelLeftEntityId;
            if (!alignment.LeftEntityNameById.TryGetValue(leftEntityId, out var entityName))
            {
                continue;
            }

            var entity = RequireEntity(targetWorkspace, entityName);
            var rows = targetWorkspace.Instance.GetOrCreateEntityRecords(entityName);
            var rightRowIds = diffData.RightRowSet
                .Select(key =>
                {
                    var parts = key.Split('\n');
                    return (EntityMapId: UnescapeCanonical(parts[0]), RowId: UnescapeCanonical(parts[1]));
                })
                .Where(item => string.Equals(item.EntityMapId, entityMapId, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.RowId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            rows.RemoveAll(row => !rightRowIds.Contains(row.Id));
            foreach (var rowId in rightRowIds.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                var row = rows.FirstOrDefault(item => string.Equals(item.Id, rowId, StringComparison.OrdinalIgnoreCase));
                if (row == null)
                {
                    row = new GenericRecord
                    {
                        Id = rowId,
                    };
                    rows.Add(row);
                }
                else
                {
                    row.Id = rowId;
                }

                if (!alignment.PropertyMapIdsByEntityMapId.TryGetValue(entityMapId, out var propertyMapIds))
                {
                    continue;
                }

                foreach (var propertyMapId in propertyMapIds)
                {
                    if (!alignment.PropertyMapById.TryGetValue(propertyMapId, out var propertyMap))
                    {
                        continue;
                    }

                    if (!alignment.LeftPropertyNameById.TryGetValue(propertyMap.ModelLeftPropertyId, out var propertyName))
                    {
                        continue;
                    }

                    var valueKey = CreateAlignedEntityRowPropertyMapKey(entityMapId, rowId, propertyMapId);
                    if (!diffData.RightValueByEntityMapRowPropertyMapKey.TryGetValue(valueKey, out var value))
                    {
                        if (IsRelationshipProperty(entity, propertyName, out var missingRelationshipEntity))
                        {
                            row.RelationshipIds.Remove(missingRelationshipEntity);
                        }
                        else
                        {
                            row.Values.Remove(propertyName);
                        }

                        continue;
                    }

                    if (IsRelationshipProperty(entity, propertyName, out var relationshipEntity))
                    {
                        row.RelationshipIds[relationshipEntity] = value;
                    }
                    else
                    {
                        row.Values[propertyName] = value;
                    }
                }
            }
        }
    }
}







