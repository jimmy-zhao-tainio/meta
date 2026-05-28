using System.Reflection;
using System.Globalization;
using System.Xml.Linq;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Serialization;
using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace Meta.Core.Services;

public sealed partial class InstanceDiffService : IInstanceDiffService
{
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
        var model = definition.Model.Clone();
        return new Workspace
        {
            WorkspaceRootPath = workspaceRootPath,
            MetadataRootPath = workspaceRootPath,
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

    private static bool IsModelContract(GenericModel model, string expectedSignature)
    {
        return string.Equals(
            model.ComputeContractSignature(),
            expectedSignature,
            StringComparison.Ordinal);
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

}
