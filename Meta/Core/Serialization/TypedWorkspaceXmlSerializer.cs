using System.Collections;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Meta.Core.Domain;

namespace Meta.Core.Serialization;

public static class TypedWorkspaceXmlSerializer
{
    private const string WorkspaceXmlFileName = "workspace.xml";
    private const string DefaultModelFileRelativePath = "model.xml";
    private const string DefaultInstanceDirectoryRelativePath = "instances";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly object CacheLock = new();
    private static readonly Dictionary<Type, ModelMap> ModelMaps = new();

    public static TModel Load<TModel>(string workspacePath, bool searchUpward = true)
        where TModel : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        var workspaceRootPath = searchUpward
            ? DiscoverWorkspaceRoot(workspacePath)
            : ResolveWorkspaceRootFromPath(workspacePath);
        if (!Directory.Exists(workspaceRootPath))
        {
            throw new DirectoryNotFoundException($"Workspace '{workspaceRootPath}' was not found.");
        }

        var modelMap = GetModelMap(typeof(TModel));
        var model = new TModel();
        var instanceDirectoryPath = ResolveInstanceDirectoryPath(workspaceRootPath);
        if (!Directory.Exists(instanceDirectoryPath))
        {
            return model;
        }

        var pendingRows = new List<PendingRow>();
        foreach (var shardPath in Directory.GetFiles(instanceDirectoryPath, "*.xml")
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(path => path, StringComparer.Ordinal))
        {
            LoadShard(model, shardPath, modelMap, pendingRows);
        }

        ResolveReferences(model, modelMap, pendingRows);
        return model;
    }

    public static void Save<TModel>(TModel model, string workspacePath)
        where TModel : class, new()
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        var modelMap = GetModelMap(typeof(TModel));
        var indexes = ValidateForSave(model, modelMap);
        var workspaceRootPath = SaveModelDocument(workspacePath, modelMap);

        var instanceDirectoryPath = ResolveInstanceDirectoryPath(workspaceRootPath);
        Directory.CreateDirectory(instanceDirectoryPath);

        var expectedShardPaths = new HashSet<string>(
            modelMap.ShardProperties
                .Select(shardProperty => Path.GetFullPath(Path.Combine(instanceDirectoryPath, shardProperty.FileName))),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var shardProperty in modelMap.ShardProperties)
        {
            var shardPath = Path.Combine(instanceDirectoryPath, shardProperty.FileName);
            var sourceRows = shardProperty.GetList(model);
            if (sourceRows.Count == 0)
            {
                DeleteIfExists(shardPath);
                continue;
            }

            WriteBytesIfChanged(shardPath, SerializeShardToBytes(model, modelMap, shardProperty, indexes));
        }

        foreach (var shardPath in Directory.GetFiles(instanceDirectoryPath, "*.xml")
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(path => path, StringComparer.Ordinal))
        {
            if (!expectedShardPaths.Contains(Path.GetFullPath(shardPath)))
            {
                File.Delete(shardPath);
            }
        }
    }

    public static void SaveModel<TModel>(TModel model, string workspacePath)
        where TModel : class, new()
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        SaveModelDocument(workspacePath, GetModelMap(typeof(TModel)));
    }

    public static string DiscoverWorkspaceRoot(string inputPath)
    {
        var initialDirectory = Directory.Exists(inputPath)
            ? inputPath
            : Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Path.GetFullPath(inputPath);
        var current = Path.GetFullPath(initialDirectory);

        while (!string.IsNullOrWhiteSpace(current))
        {
            if (LooksLikeWorkspaceRoot(current))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        return Path.GetFullPath(initialDirectory);
    }

    public static string ResolveWorkspaceRootFromPath(string inputPath)
    {
        var fullPath = Path.GetFullPath(inputPath);
        if (File.Exists(fullPath))
        {
            return Path.GetDirectoryName(fullPath) ?? fullPath;
        }

        if (string.Equals(
                Path.GetFileName(fullPath),
                DefaultInstanceDirectoryRelativePath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return Directory.GetParent(fullPath)?.FullName ?? fullPath;
        }

        return fullPath;
    }

    public static string ResolveInstanceDirectoryPath(string workspaceRootPath)
    {
        var relativePath = ReadInstanceDirectoryRelativePath(workspaceRootPath);
        var normalizedRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var instanceDirectoryPath = Path.GetFullPath(Path.Combine(workspaceRootPath, normalizedRelativePath));
        EnsurePathUnderWorkspaceRoot(workspaceRootPath, instanceDirectoryPath, "InstanceDirPath");
        return instanceDirectoryPath;
    }

    public static void WriteBytesIfChanged(string path, byte[] contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(path) && FileEquals(path, contents))
        {
            return;
        }

        File.WriteAllBytes(path, contents);
    }

    private static ModelMap GetModelMap(Type modelType)
    {
        lock (CacheLock)
        {
            if (!ModelMaps.TryGetValue(modelType, out var modelMap))
            {
                modelMap = BuildModelMap(modelType);
                ModelMaps[modelType] = modelMap;
            }

            return modelMap;
        }
    }

    private static ModelMap BuildModelMap(Type modelType)
    {
        var xmlRootAttribute = modelType.GetCustomAttribute<XmlRootAttribute>();
        var rootElementName = string.IsNullOrWhiteSpace(xmlRootAttribute?.ElementName)
            ? InferModelName(modelType)
            : xmlRootAttribute!.ElementName;

        var shardProperties = modelType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.CanWrite)
            .Select(property => ShardProperty.TryCreate(modelType, property))
            .Where(item => item != null)
            .Cast<ShardProperty>()
            .OrderBy(item => item.EntityName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.EntityName, StringComparer.Ordinal)
            .ToList();

        var entityNameByType = new Dictionary<Type, string>();
        foreach (var shardProperty in shardProperties)
        {
            if (!entityNameByType.TryAdd(shardProperty.ItemType, shardProperty.EntityName))
            {
                throw new InvalidOperationException(
                    $"Model type '{modelType.FullName}' contains more than one collection for entity type '{shardProperty.ItemType.FullName}'.");
            }
        }

        var entityTypes = entityNameByType.Keys.ToHashSet();
        foreach (var shardProperty in shardProperties)
        {
            shardProperty.EntityMap = BuildEntityMap(modelType, shardProperty, entityTypes, entityNameByType);
        }

        var entityMaps = shardProperties.Select(item => item.EntityMap).ToList();
        var entityMapsByName = new Dictionary<string, EntityMap>(StringComparer.OrdinalIgnoreCase);
        foreach (var entityMap in entityMaps)
        {
            if (!entityMapsByName.TryAdd(entityMap.EntityName, entityMap))
            {
                throw new InvalidOperationException(
                    $"Model type '{modelType.FullName}' contains duplicate entity name '{entityMap.EntityName}'.");
            }
        }

        var modelMap = new ModelMap(
            rootElementName,
            shardProperties,
            shardProperties.ToDictionary(item => item.XmlArrayName, StringComparer.OrdinalIgnoreCase),
            entityMapsByName,
            entityMaps);

        ValidateRequiredRelationshipGraphIsAcyclic(modelMap);
        return modelMap;
    }

    private static EntityMap BuildEntityMap(
        Type modelType,
        ShardProperty shardProperty,
        ISet<Type> entityTypes,
        IReadOnlyDictionary<Type, string> entityNameByType)
    {
        var itemType = shardProperty.ItemType;
        var idProperty = itemType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(property =>
                property.CanRead &&
                property.CanWrite &&
                property.GetIndexParameters().Length == 0 &&
                property.PropertyType == typeof(string) &&
                string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase));
        if (idProperty == null)
        {
            throw new InvalidOperationException(
                $"Entity type '{itemType.FullName}' in model '{modelType.FullName}' must declare public string Id {{ get; set; }}.");
        }

        var scalarProperties = new List<ScalarProperty>();
        var relationshipProperties = new List<RelationshipProperty>();
        foreach (var property in itemType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                     .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(property => property.Name, StringComparer.Ordinal))
        {
            if (property == idProperty)
            {
                continue;
            }

            var isEntityReference = entityTypes.Contains(property.PropertyType);
            if (isEntityReference)
            {
                var relationshipRole = property.Name;
                var relationshipName = relationshipRole + "Id";

                relationshipProperties.Add(new RelationshipProperty(
                    property,
                    relationshipRole,
                    relationshipName,
                    entityNameByType[property.PropertyType],
                    !IsNullableProperty(property)));
                continue;
            }

            if (property.GetCustomAttribute<XmlIgnoreAttribute>() != null)
            {
                continue;
            }

            if (property.PropertyType == typeof(string))
            {
                scalarProperties.Add(new ScalarProperty(
                    property,
                    GetXmlElementName(property),
                    !IsNullableProperty(property)));
                continue;
            }

            throw new InvalidOperationException(
                $"Property '{itemType.FullName}.{property.Name}' is neither a string scalar nor an entity relationship.");
        }

        return new EntityMap(
            shardProperty,
            itemType,
            shardProperty.EntityName,
            idProperty,
            scalarProperties
                .OrderBy(item => item.XmlElementName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.XmlElementName, StringComparer.Ordinal)
                .ToList(),
            relationshipProperties
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TargetEntityName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.Ordinal)
                .ToList());
    }

    private static void ValidateRequiredRelationshipGraphIsAcyclic(ModelMap modelMap)
    {
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<EntityMap>();

        foreach (var entityMap in modelMap.EntityMaps
                     .OrderBy(item => item.EntityName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.EntityName, StringComparer.Ordinal))
        {
            Visit(entityMap, new Stack<string>());
        }

        modelMap.LoadOrder = ordered;

        void Visit(EntityMap entityMap, Stack<string> path)
        {
            if (visited.Contains(entityMap.EntityName))
            {
                return;
            }

            if (visiting.Contains(entityMap.EntityName))
            {
                var cycle = string.Join(" -> ", path.Reverse().Append(entityMap.EntityName));
                throw new InvalidOperationException(
                    $"Required relationship graph for model '{modelMap.RootElementName}' contains a cycle: {cycle}.");
            }

            visiting.Add(entityMap.EntityName);
            path.Push(entityMap.EntityName);
            foreach (var relationship in entityMap.RelationshipProperties
                         .Where(item => item.IsRequired)
                         .OrderBy(item => item.TargetEntityName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                Visit(modelMap.EntityMapsByName[relationship.TargetEntityName], path);
            }

            path.Pop();
            visiting.Remove(entityMap.EntityName);
            visited.Add(entityMap.EntityName);
            ordered.Add(entityMap);
        }
    }

    private static void LoadShard<TModel>(
        TModel model,
        string shardPath,
        ModelMap modelMap,
        List<PendingRow> pendingRows)
        where TModel : class
    {
        var document = XDocument.Load(shardPath, LoadOptions.None);
        var root = document.Root ?? throw new InvalidDataException($"Instance XML '{shardPath}' has no root element.");
        if (!string.Equals(root.Name.LocalName, modelMap.RootElementName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Instance XML '{shardPath}' root must be <{modelMap.RootElementName}>.");
        }

        foreach (var listElement in root.Elements())
        {
            if (!modelMap.ShardPropertiesByListElementName.TryGetValue(listElement.Name.LocalName, out var shardProperty))
            {
                throw new InvalidDataException(
                    $"Unknown XML element '{listElement.Name.LocalName}' in '{shardPath}'.");
            }

            LoadListElement(model, shardPath, shardProperty, listElement, pendingRows);
        }
    }

    private static void LoadListElement<TModel>(
        TModel model,
        string shardPath,
        ShardProperty shardProperty,
        XElement listElement,
        List<PendingRow> pendingRows)
        where TModel : class
    {
        var entityMap = shardProperty.EntityMap;
        var rows = shardProperty.GetList(model);
        foreach (var rowElement in listElement.Elements())
        {
            if (!string.Equals(rowElement.Name.LocalName, shardProperty.XmlArrayItemName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Unknown XML element '{rowElement.Name.LocalName}' in '{shardPath}'.");
            }

            var row = Activator.CreateInstance(entityMap.ItemType)
                ?? throw new InvalidOperationException($"Could not create entity '{entityMap.ItemType.FullName}'.");
            var relationshipValues = new Dictionary<RelationshipProperty, string>();

            foreach (var attribute in rowElement.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                var attributeName = attribute.Name.LocalName;
                if (string.Equals(attributeName, GetXmlAttributeName(entityMap.IdProperty), StringComparison.Ordinal))
                {
                    entityMap.IdProperty.SetValue(row, attribute.Value);
                    continue;
                }

                var relationship = entityMap.RelationshipProperties
                    .FirstOrDefault(item => string.Equals(item.Name, attributeName, StringComparison.Ordinal));
                if (relationship != null)
                {
                    relationshipValues[relationship] = attribute.Value;
                    continue;
                }

                throw new InvalidDataException(
                    $"Unknown XML attribute '{attributeName}' on '{entityMap.EntityName}' in '{shardPath}'.");
            }

            var seenScalars = new HashSet<string>(StringComparer.Ordinal);
            foreach (var valueElement in rowElement.Elements())
            {
                var scalar = entityMap.ScalarProperties
                    .FirstOrDefault(item => string.Equals(item.XmlElementName, valueElement.Name.LocalName, StringComparison.Ordinal));
                if (scalar == null)
                {
                    throw new InvalidDataException(
                        $"Unknown XML element '{valueElement.Name.LocalName}' on '{entityMap.EntityName}' in '{shardPath}'.");
                }

                if (!seenScalars.Add(scalar.XmlElementName))
                {
                    throw new InvalidDataException(
                        $"Duplicate XML element '{scalar.XmlElementName}' on '{entityMap.EntityName}' in '{shardPath}'.");
                }

                scalar.Property.SetValue(row, valueElement.Value);
            }

            rows.Add(row);
            pendingRows.Add(new PendingRow(entityMap, row, relationshipValues));
        }
    }

    private static void ResolveReferences<TModel>(TModel model, ModelMap modelMap, IReadOnlyList<PendingRow> pendingRows)
        where TModel : class
    {
        var indexes = BuildIndexes(model, modelMap);
        foreach (var pendingRow in pendingRows)
        {
            ValidateRequiredScalars(pendingRow.EntityMap, pendingRow.Row);
        }

        var pendingByEntity = pendingRows
            .GroupBy(item => item.EntityMap.EntityName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Key, item => item.ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var entityMap in modelMap.LoadOrder)
        {
            if (!pendingByEntity.TryGetValue(entityMap.EntityName, out var entityRows))
            {
                continue;
            }

            foreach (var pendingRow in entityRows)
            {
                foreach (var relationship in entityMap.RelationshipProperties)
                {
                    pendingRow.RelationshipValues.TryGetValue(relationship, out var targetId);
                    var normalizedTargetId = NormalizeIdentity(targetId);
                    if (string.IsNullOrEmpty(normalizedTargetId))
                    {
                        if (relationship.IsRequired)
                        {
                            throw new InvalidDataException(
                                $"Relationship '{entityMap.EntityName}.{relationship.Name}' on row '{entityMap.EntityName}:{GetId(entityMap, pendingRow.Row)}' is empty.");
                        }

                        relationship.Property.SetValue(pendingRow.Row, null);
                        continue;
                    }

                    var targetIndex = indexes[relationship.TargetEntityName];
                    if (!targetIndex.TryGetValue(normalizedTargetId, out var target))
                    {
                        throw new InvalidDataException(
                            $"Relationship '{entityMap.EntityName}.{relationship.Name}' on row '{entityMap.EntityName}:{GetId(entityMap, pendingRow.Row)}' points to missing Id '{normalizedTargetId}'.");
                    }

                    relationship.Property.SetValue(pendingRow.Row, target);
                }
            }
        }
    }

    private static Dictionary<string, Dictionary<string, object>> ValidateForSave<TModel>(
        TModel model,
        ModelMap modelMap)
        where TModel : class
    {
        var indexes = BuildIndexes(model, modelMap);
        foreach (var entityMap in modelMap.EntityMaps)
        {
            foreach (var row in entityMap.ShardProperty.GetList(model))
            {
                if (row == null)
                {
                    throw new InvalidOperationException($"Entity '{entityMap.EntityName}' contains a null row.");
                }

                ValidateRequiredScalars(entityMap, row);
                foreach (var relationship in entityMap.RelationshipProperties)
                {
                    var target = relationship.Property.GetValue(row);
                    if (target == null)
                    {
                        if (relationship.IsRequired)
                        {
                            throw new InvalidOperationException(
                                $"Relationship '{entityMap.EntityName}.{relationship.Name}' on row '{entityMap.EntityName}:{GetId(entityMap, row)}' is empty.");
                        }

                        continue;
                    }

                    var targetEntity = modelMap.EntityMapsByName[relationship.TargetEntityName];
                    var targetId = GetRequiredId(
                        targetEntity,
                        target,
                        $"Relationship '{entityMap.EntityName}.{relationship.Name}' on row '{entityMap.EntityName}:{GetId(entityMap, row)}' references a target with empty Id.");
                    if (!indexes[relationship.TargetEntityName].TryGetValue(targetId, out var canonical))
                    {
                        throw new InvalidOperationException(
                            $"Relationship '{entityMap.EntityName}.{relationship.Name}' on row '{entityMap.EntityName}:{GetId(entityMap, row)}' points to missing Id '{targetId}'.");
                    }

                    if (!ReferenceEquals(canonical, target))
                    {
                        throw new InvalidOperationException(
                            $"Relationship '{entityMap.EntityName}.{relationship.Name}' on row '{entityMap.EntityName}:{GetId(entityMap, row)}' references an object that is not the canonical row for Id '{targetId}'.");
                    }
                }
            }
        }

        return indexes;
    }

    private static Dictionary<string, Dictionary<string, object>> BuildIndexes<TModel>(TModel model, ModelMap modelMap)
        where TModel : class
    {
        var indexes = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entityMap in modelMap.EntityMaps)
        {
            var rowsById = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var row in entityMap.ShardProperty.GetList(model))
            {
                if (row == null)
                {
                    throw new InvalidOperationException($"Entity '{entityMap.EntityName}' contains a null row.");
                }

                var id = GetRequiredId(
                    entityMap,
                    row,
                    $"Entity '{entityMap.EntityName}' contains a row with empty Id.");
                if (!rowsById.TryAdd(id, row))
                {
                    throw new InvalidOperationException($"Entity '{entityMap.EntityName}' contains duplicate Id '{id}'.");
                }
            }

            indexes[entityMap.EntityName] = rowsById;
        }

        return indexes;
    }

    private static void ValidateRequiredScalars(EntityMap entityMap, object row)
    {
        foreach (var scalar in entityMap.ScalarProperties.Where(item => item.IsRequired))
        {
            var value = scalar.Property.GetValue(row) as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Entity '{entityMap.EntityName}' row '{GetId(entityMap, row)}' is missing required property '{scalar.Property.Name}'.");
            }
        }
    }

    private static byte[] SerializeModelToBytes(ModelMap modelMap)
    {
        var document = ModelXmlCodec.BuildDocument(BuildGenericModel(modelMap));
        return Utf8NoBom.GetBytes(document.ToString(SaveOptions.None));
    }

    private static GenericModel BuildGenericModel(ModelMap modelMap)
    {
        var model = new GenericModel
        {
            Name = modelMap.RootElementName,
        };

        foreach (var entityMap in modelMap.EntityMaps
                     .OrderBy(item => item.EntityName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.EntityName, StringComparer.Ordinal))
        {
            var entity = new GenericEntity
            {
                Name = entityMap.EntityName,
            };
            foreach (var scalar in entityMap.ScalarProperties)
            {
                entity.Properties.Add(new GenericProperty
                {
                    Name = scalar.XmlElementName,
                    DataType = "string",
                    IsNullable = !scalar.IsRequired,
                });
            }

            foreach (var relationship in entityMap.RelationshipProperties)
            {
                entity.Relationships.Add(new GenericRelationship
                {
                    Entity = relationship.TargetEntityName,
                    Role = string.Equals(relationship.Role, relationship.TargetEntityName, StringComparison.Ordinal)
                        ? string.Empty
                        : relationship.Role,
                    IsNullable = !relationship.IsRequired,
                });
            }

            model.Entities.Add(entity);
        }

        return model;
    }

    private static byte[] SerializeShardToBytes<TModel>(
        TModel model,
        ModelMap modelMap,
        ShardProperty shardProperty,
        IReadOnlyDictionary<string, Dictionary<string, object>> indexes)
        where TModel : class
    {
        var root = new XElement(modelMap.RootElementName);
        var listElement = new XElement(shardProperty.XmlArrayName);
        var entityMap = shardProperty.EntityMap;
        foreach (var row in shardProperty.GetList(model))
        {
            if (row == null)
            {
                throw new InvalidOperationException($"Entity '{entityMap.EntityName}' contains a null row.");
            }

            var rowElement = new XElement(shardProperty.XmlArrayItemName);
            rowElement.Add(new XAttribute(GetXmlAttributeName(entityMap.IdProperty), GetRequiredId(
                entityMap,
                row,
                $"Entity '{entityMap.EntityName}' contains a row with empty Id.")));

            foreach (var relationship in entityMap.RelationshipProperties)
            {
                var target = relationship.Property.GetValue(row);
                if (target == null)
                {
                    if (relationship.IsRequired)
                    {
                        throw new InvalidOperationException(
                            $"Relationship '{entityMap.EntityName}.{relationship.Name}' on row '{entityMap.EntityName}:{GetId(entityMap, row)}' is empty.");
                    }

                    continue;
                }

                var targetEntity = modelMap.EntityMapsByName[relationship.TargetEntityName];
                var targetId = GetRequiredId(
                    targetEntity,
                    target,
                    $"Relationship '{entityMap.EntityName}.{relationship.Name}' on row '{entityMap.EntityName}:{GetId(entityMap, row)}' references a target with empty Id.");
                if (!indexes[relationship.TargetEntityName].TryGetValue(targetId, out var canonical) ||
                    !ReferenceEquals(canonical, target))
                {
                    throw new InvalidOperationException(
                        $"Relationship '{entityMap.EntityName}.{relationship.Name}' on row '{entityMap.EntityName}:{GetId(entityMap, row)}' references an object that is not the canonical row for Id '{targetId}'.");
                }

                rowElement.Add(new XAttribute(relationship.Name, targetId));
            }

            foreach (var scalar in entityMap.ScalarProperties)
            {
                var value = scalar.Property.GetValue(row) as string ?? string.Empty;
                if (!scalar.IsRequired && string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                rowElement.Add(new XElement(scalar.XmlElementName, value));
            }

            listElement.Add(rowElement);
        }

        root.Add(listElement);
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = Utf8NoBom,
            Indent = true,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = false,
        }))
        {
            document.Save(writer);
        }

        return stream.ToArray();
    }

    private static string SaveModelDocument(string workspacePath, ModelMap modelMap)
    {
        var workspaceRootPath = Path.GetFullPath(workspacePath);
        Directory.CreateDirectory(workspaceRootPath);

        var workspaceXmlPath = Path.Combine(workspaceRootPath, WorkspaceXmlFileName);
        if (!File.Exists(workspaceXmlPath))
        {
            WriteWorkspaceDocument(workspaceXmlPath);
        }

        var modelPath = ResolveModelFilePath(workspaceRootPath);
        WriteBytesIfChanged(modelPath, SerializeModelToBytes(modelMap));

        return workspaceRootPath;
    }

    private static bool FileEquals(string path, ReadOnlySpan<byte> contents)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Length != contents.Length)
        {
            return false;
        }

        using var stream = File.OpenRead(path);
        Span<byte> buffer = stackalloc byte[8192];
        var offset = 0;
        while (true)
        {
            var read = stream.Read(buffer);
            if (read == 0)
            {
                return true;
            }

            if (!buffer[..read].SequenceEqual(contents.Slice(offset, read)))
            {
                return false;
            }

            offset += read;
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool LooksLikeWorkspaceRoot(string workspaceRootPath)
    {
        if (File.Exists(Path.Combine(workspaceRootPath, WorkspaceXmlFileName)))
        {
            return true;
        }

        return File.Exists(Path.Combine(workspaceRootPath, Path.GetFileName(DefaultModelFileRelativePath))) ||
               Directory.Exists(Path.Combine(workspaceRootPath, DefaultInstanceDirectoryRelativePath));
    }

    private static string ReadInstanceDirectoryRelativePath(string workspaceRootPath)
    {
        var workspaceXmlPath = Path.Combine(workspaceRootPath, WorkspaceXmlFileName);
        if (!File.Exists(workspaceXmlPath))
        {
            return DefaultInstanceDirectoryRelativePath;
        }

        var document = XDocument.Load(workspaceXmlPath, LoadOptions.None);
        var instanceDirPath = document.Root?
            .Element("WorkspaceLayoutList")?
            .Elements("WorkspaceLayout")
            .Elements("InstanceDirPath")
            .Select(element => element.Value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return string.IsNullOrWhiteSpace(instanceDirPath)
            ? DefaultInstanceDirectoryRelativePath
            : instanceDirPath!;
    }

    private static string ResolveModelFilePath(string workspaceRootPath)
    {
        var relativePath = ReadModelFileRelativePath(workspaceRootPath);
        var normalizedRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var modelFilePath = Path.GetFullPath(Path.Combine(workspaceRootPath, normalizedRelativePath));
        EnsurePathUnderWorkspaceRoot(workspaceRootPath, modelFilePath, "ModelFilePath");
        return modelFilePath;
    }

    private static string ReadModelFileRelativePath(string workspaceRootPath)
    {
        var workspaceXmlPath = Path.Combine(workspaceRootPath, WorkspaceXmlFileName);
        if (!File.Exists(workspaceXmlPath))
        {
            return DefaultModelFileRelativePath;
        }

        var document = XDocument.Load(workspaceXmlPath, LoadOptions.None);
        var modelFilePath = document.Root?
            .Element("WorkspaceLayoutList")?
            .Elements("WorkspaceLayout")
            .Elements("ModelFilePath")
            .Select(element => element.Value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return string.IsNullOrWhiteSpace(modelFilePath)
            ? DefaultModelFileRelativePath
            : modelFilePath!;
    }

    private static void EnsurePathUnderWorkspaceRoot(string workspaceRootPath, string path, string memberName)
    {
        var absoluteRootPath = Path.GetFullPath(workspaceRootPath);
        var absolutePath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootWithSeparator = absoluteRootPath.EndsWith(Path.DirectorySeparatorChar)
            ? absoluteRootPath
            : absoluteRootPath + Path.DirectorySeparatorChar;
        if (!absolutePath.StartsWith(rootWithSeparator, comparison) &&
            !string.Equals(absolutePath, absoluteRootPath, comparison))
        {
            throw new InvalidOperationException(
                $"Workspace {memberName} '{absolutePath}' must stay under workspace root '{absoluteRootPath}'.");
        }
    }

    private static void WriteWorkspaceDocument(string workspaceXmlPath)
    {
        var root = new XElement("MetaWorkspace",
            new XElement("WorkspaceList",
                new XElement("Workspace",
                    new XAttribute("Id", "1"),
                    new XAttribute("WorkspaceLayoutId", "1"),
                    new XAttribute("EncodingId", "1"),
                    new XAttribute("NewlinesId", "1"),
                    new XAttribute("EntitiesOrderId", "1"),
                    new XAttribute("PropertiesOrderId", "1"),
                    new XAttribute("RelationshipsOrderId", "1"),
                    new XAttribute("RowsOrderId", "2"),
                    new XAttribute("AttributesOrderId", "3"),
                    new XElement("Name", "Workspace"),
                    new XElement("FormatVersion", "1.0"))),
            new XElement("WorkspaceLayoutList",
                new XElement("WorkspaceLayout",
                    new XAttribute("Id", "1"),
                    new XElement("ModelFilePath", DefaultModelFileRelativePath.Replace(Path.DirectorySeparatorChar, '/')),
                    new XElement("InstanceDirPath", DefaultInstanceDirectoryRelativePath.Replace(Path.DirectorySeparatorChar, '/')))),
            new XElement("EncodingList",
                new XElement("Encoding",
                    new XAttribute("Id", "1"),
                    new XElement("Name", "utf-8-no-bom"))),
            new XElement("NewlinesList",
                new XElement("Newlines",
                    new XAttribute("Id", "1"),
                    new XElement("Name", "lf"))),
            new XElement("CanonicalOrderList",
                new XElement("CanonicalOrder", new XAttribute("Id", "1"), new XElement("Name", "name-ordinal")),
                new XElement("CanonicalOrder", new XAttribute("Id", "2"), new XElement("Name", "id-ordinal")),
                new XElement("CanonicalOrder", new XAttribute("Id", "3"), new XElement("Name", "id-first-then-name-ordinal"))),
            new XElement("EntityStorageList"));

        var directory = Path.GetDirectoryName(workspaceXmlPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            workspaceXmlPath,
            new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString(SaveOptions.None),
            Utf8NoBom);
    }

    private static string GetXmlAttributeName(PropertyInfo property)
    {
        var attribute = property.GetCustomAttribute<XmlAttributeAttribute>();
        return string.IsNullOrWhiteSpace(attribute?.AttributeName)
            ? property.Name
            : attribute!.AttributeName;
    }

    private static string GetXmlElementName(PropertyInfo property)
    {
        var attribute = property.GetCustomAttribute<XmlElementAttribute>();
        return string.IsNullOrWhiteSpace(attribute?.ElementName)
            ? property.Name
            : attribute!.ElementName;
    }

    private static string InferModelName(Type modelType)
    {
        return modelType.Name.EndsWith("Model", StringComparison.Ordinal) && modelType.Name.Length > "Model".Length
            ? modelType.Name[..^"Model".Length]
            : modelType.Name;
    }

    private static bool IsNullableProperty(PropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) != null)
        {
            return true;
        }

        if (property.PropertyType.IsValueType)
        {
            return false;
        }

        var nullability = new NullabilityInfoContext().Create(property);
        return nullability.WriteState == NullabilityState.Nullable ||
               nullability.ReadState == NullabilityState.Nullable;
    }

    private static string GetId(EntityMap entityMap, object row)
    {
        return NormalizeIdentity(entityMap.IdProperty.GetValue(row) as string);
    }

    private static string GetRequiredId(EntityMap entityMap, object row, string errorMessage)
    {
        var id = GetId(entityMap, row);
        if (string.IsNullOrEmpty(id))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return id;
    }

    private static string NormalizeIdentity(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private sealed class ModelMap
    {
        public ModelMap(
            string rootElementName,
            IReadOnlyList<ShardProperty> shardProperties,
            IReadOnlyDictionary<string, ShardProperty> shardPropertiesByListElementName,
            IReadOnlyDictionary<string, EntityMap> entityMapsByName,
            IReadOnlyList<EntityMap> entityMaps)
        {
            RootElementName = rootElementName;
            ShardProperties = shardProperties;
            ShardPropertiesByListElementName = shardPropertiesByListElementName;
            EntityMapsByName = entityMapsByName;
            EntityMaps = entityMaps;
            LoadOrder = entityMaps;
        }

        public string RootElementName { get; }
        public IReadOnlyList<ShardProperty> ShardProperties { get; }
        public IReadOnlyDictionary<string, ShardProperty> ShardPropertiesByListElementName { get; }
        public IReadOnlyDictionary<string, EntityMap> EntityMapsByName { get; }
        public IReadOnlyList<EntityMap> EntityMaps { get; }
        public IReadOnlyList<EntityMap> LoadOrder { get; set; }
    }

    private sealed class ShardProperty
    {
        private ShardProperty(
            PropertyInfo property,
            Type itemType,
            string entityName,
            string xmlArrayName,
            string xmlArrayItemName,
            string fileName)
        {
            Property = property;
            ItemType = itemType;
            EntityName = entityName;
            XmlArrayName = xmlArrayName;
            XmlArrayItemName = xmlArrayItemName;
            FileName = fileName;
        }

        public PropertyInfo Property { get; }
        public Type ItemType { get; }
        public string EntityName { get; }
        public string XmlArrayName { get; }
        public string XmlArrayItemName { get; }
        public string FileName { get; }
        public EntityMap EntityMap { get; set; } = null!;

        public static ShardProperty? TryCreate(Type modelType, PropertyInfo property)
        {
            var xmlArray = property.GetCustomAttribute<XmlArrayAttribute>();
            var xmlArrayItem = property.GetCustomAttribute<XmlArrayItemAttribute>();
            if (!property.PropertyType.IsGenericType ||
                property.PropertyType.GetGenericTypeDefinition() != typeof(List<>))
            {
                if (xmlArray != null || xmlArrayItem != null)
                {
                    throw new InvalidOperationException(
                        $"Model type '{modelType.FullName}' property '{property.Name}' must be List<T> when marked with XML collection attributes.");
                }

                return null;
            }

            var itemType = property.PropertyType.GetGenericArguments()[0];
            var entityName = string.IsNullOrWhiteSpace(xmlArrayItem?.ElementName)
                ? itemType.Name
                : xmlArrayItem!.ElementName;
            var xmlArrayName = string.IsNullOrWhiteSpace(xmlArray?.ElementName)
                ? property.Name
                : xmlArray!.ElementName;
            return new ShardProperty(
                property,
                itemType,
                entityName,
                xmlArrayName,
                entityName,
                entityName + ".xml");
        }

        public IList GetList(object owner)
        {
            if (Property.GetValue(owner) is IList list)
            {
                return list;
            }

            var created = (IList?)Activator.CreateInstance(Property.PropertyType);
            if (created == null)
            {
                throw new InvalidOperationException(
                    $"Could not create list instance for '{Property.DeclaringType?.FullName}.{Property.Name}'.");
            }

            Property.SetValue(owner, created);
            return created;
        }
    }

    private sealed record EntityMap(
        ShardProperty ShardProperty,
        Type ItemType,
        string EntityName,
        PropertyInfo IdProperty,
        IReadOnlyList<ScalarProperty> ScalarProperties,
        IReadOnlyList<RelationshipProperty> RelationshipProperties);

    private sealed record ScalarProperty(
        PropertyInfo Property,
        string XmlElementName,
        bool IsRequired);

    private sealed record RelationshipProperty(
        PropertyInfo Property,
        string Role,
        string Name,
        string TargetEntityName,
        bool IsRequired);

    private sealed record PendingRow(
        EntityMap EntityMap,
        object Row,
        IReadOnlyDictionary<RelationshipProperty, string> RelationshipValues);
}
