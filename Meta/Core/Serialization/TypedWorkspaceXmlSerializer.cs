using System.Collections;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace Meta.Core.Serialization;

public static class TypedWorkspaceXmlSerializer
{
    private const string MetadataDirectoryName = "metadata";
    private const string WorkspaceXmlFileName = "workspace.xml";
    private const string DefaultInstanceDirectoryRelativePath = "metadata/instance";
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

        foreach (var shardPath in Directory.GetFiles(instanceDirectoryPath, "*.xml")
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(path => path, StringComparer.Ordinal))
        {
            var shard = Deserialize<TModel>(shardPath, modelMap);
            MergeShard(model, shard, modelMap);
        }

        return model;
    }

    public static void Save<TModel>(TModel model, string workspacePath, string? modelXmlSourcePath = null)
        where TModel : class, new()
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        var workspaceRootPath = Path.GetFullPath(workspacePath);
        Directory.CreateDirectory(workspaceRootPath);

        var workspaceXmlPath = Path.Combine(workspaceRootPath, WorkspaceXmlFileName);
        if (!File.Exists(workspaceXmlPath))
        {
            WriteWorkspaceDocument(workspaceXmlPath);
        }

        var metadataRootPath = Path.Combine(workspaceRootPath, MetadataDirectoryName);
        Directory.CreateDirectory(metadataRootPath);

        if (!string.IsNullOrWhiteSpace(modelXmlSourcePath) && File.Exists(modelXmlSourcePath))
        {
            File.Copy(modelXmlSourcePath, Path.Combine(metadataRootPath, "model.xml"), overwrite: true);
        }

        var instanceDirectoryPath = ResolveInstanceDirectoryPath(workspaceRootPath);
        Directory.CreateDirectory(instanceDirectoryPath);
        foreach (var shardPath in Directory.GetFiles(instanceDirectoryPath, "*.xml")
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(path => path, StringComparer.Ordinal))
        {
            File.Delete(shardPath);
        }

        var modelMap = GetModelMap(typeof(TModel));
        foreach (var shardProperty in modelMap.ShardProperties)
        {
            var sourceRows = shardProperty.GetList(model);
            if (sourceRows.Count == 0)
            {
                continue;
            }

            var shardModel = new TModel();
            var targetRows = shardProperty.GetList(shardModel);
            foreach (var row in sourceRows)
            {
                targetRows.Add(row);
            }

            Serialize(Path.Combine(instanceDirectoryPath, shardProperty.FileName), shardModel, modelMap);
        }
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
                MetadataDirectoryName,
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
        if (xmlRootAttribute == null || string.IsNullOrWhiteSpace(xmlRootAttribute.ElementName))
        {
            throw new InvalidOperationException(
                $"Model type '{modelType.FullName}' must declare [XmlRoot].");
        }

        var shardProperties = modelType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.CanWrite)
            .Select(property => ShardProperty.TryCreate(modelType, property))
            .Where(item => item != null)
            .Cast<ShardProperty>()
            .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FileName, StringComparer.Ordinal)
            .ToList();

        return new ModelMap(
            modelType,
            xmlRootAttribute.ElementName,
            shardProperties,
            new XmlSerializer(modelType));
    }

    private static TModel Deserialize<TModel>(string shardPath, ModelMap modelMap)
        where TModel : class, new()
    {
        using var stream = File.OpenRead(shardPath);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
        });

        var events = new XmlDeserializationEvents
        {
            OnUnknownAttribute = (_, args) =>
                throw new InvalidDataException($"Unknown XML attribute '{args.Attr.Name}' in '{shardPath}'."),
            OnUnknownElement = (_, args) =>
                throw new InvalidDataException($"Unknown XML element '{args.Element.Name}' in '{shardPath}'."),
            OnUnknownNode = (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Name))
                {
                    return;
                }

                throw new InvalidDataException($"Unknown XML node '{args.Name}' in '{shardPath}'.");
            },
        };

        var deserialized = modelMap.Serializer.Deserialize(reader, events) as TModel;
        if (deserialized == null)
        {
            throw new InvalidDataException($"Could not deserialize '{shardPath}' into '{typeof(TModel).FullName}'.");
        }

        return deserialized;
    }

    private static void Serialize<TModel>(string path, TModel model, ModelMap modelMap)
        where TModel : class
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(path);
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = Utf8NoBom,
            Indent = true,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = false,
        });
        modelMap.Serializer.Serialize(writer, model);
    }

    private static void MergeShard<TModel>(TModel target, TModel source, ModelMap modelMap)
        where TModel : class
    {
        foreach (var shardProperty in modelMap.ShardProperties)
        {
            var sourceRows = shardProperty.GetList(source);
            if (sourceRows.Count == 0)
            {
                continue;
            }

            var targetRows = shardProperty.GetList(target);
            foreach (var row in sourceRows)
            {
                targetRows.Add(row);
            }
        }
    }

    private static bool LooksLikeWorkspaceRoot(string workspaceRootPath)
    {
        if (File.Exists(Path.Combine(workspaceRootPath, WorkspaceXmlFileName)))
        {
            return true;
        }

        var metadataRootPath = Path.Combine(workspaceRootPath, MetadataDirectoryName);
        return File.Exists(Path.Combine(metadataRootPath, "model.xml")) ||
               Directory.Exists(Path.Combine(metadataRootPath, "instance"));
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
                    new XElement("ModelFilePath", "metadata/model.xml"),
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

    private sealed record ModelMap(
        Type ModelType,
        string RootElementName,
        IReadOnlyList<ShardProperty> ShardProperties,
        XmlSerializer Serializer);

    private sealed class ShardProperty
    {
        private ShardProperty(PropertyInfo property, Type itemType, string fileName)
        {
            Property = property;
            ItemType = itemType;
            FileName = fileName;
        }

        public PropertyInfo Property { get; }
        public Type ItemType { get; }
        public string FileName { get; }

        public static ShardProperty? TryCreate(Type modelType, PropertyInfo property)
        {
            var xmlArray = property.GetCustomAttribute<XmlArrayAttribute>();
            var xmlArrayItem = property.GetCustomAttribute<XmlArrayItemAttribute>();
            if (xmlArray == null || xmlArrayItem == null)
            {
                return null;
            }

            if (!property.PropertyType.IsGenericType ||
                property.PropertyType.GetGenericTypeDefinition() != typeof(List<>))
            {
                throw new InvalidOperationException(
                    $"Model type '{modelType.FullName}' property '{property.Name}' must be List<T> when marked with [XmlArray].");
            }

            var itemType = property.PropertyType.GetGenericArguments()[0];
            return new ShardProperty(property, itemType, itemType.Name + ".xml");
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
}
