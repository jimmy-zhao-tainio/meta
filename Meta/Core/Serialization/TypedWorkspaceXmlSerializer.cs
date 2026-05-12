using System.Collections;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

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

        foreach (var shardPath in Directory.GetFiles(instanceDirectoryPath, "*.xml")
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(path => path, StringComparer.Ordinal))
        {
            var shard = Deserialize<TModel>(shardPath, modelMap);
            if (modelMap.ShardPropertiesByFileName.TryGetValue(Path.GetFileName(shardPath), out var shardProperty))
            {
                MergeShardProperty(model, shard, shardProperty);
            }
            else
            {
                MergeShard(model, shard, modelMap);
            }
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

        var modelPath = ResolveModelFilePath(workspaceRootPath);
        if (!string.IsNullOrWhiteSpace(modelXmlSourcePath) && File.Exists(modelXmlSourcePath))
        {
            var modelDirectory = Path.GetDirectoryName(modelPath);
            if (!string.IsNullOrWhiteSpace(modelDirectory))
            {
                Directory.CreateDirectory(modelDirectory);
            }

            CopyFileIfChanged(modelXmlSourcePath, modelPath);
        }

        var instanceDirectoryPath = ResolveInstanceDirectoryPath(workspaceRootPath);
        Directory.CreateDirectory(instanceDirectoryPath);

        var modelMap = GetModelMap(typeof(TModel));
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

            var shardModel = new TModel();
            var targetRows = shardProperty.GetList(shardModel);
            foreach (var row in sourceRows)
            {
                targetRows.Add(row);
            }

            SerializeIfChanged(shardPath, shardModel, modelMap);
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
            shardProperties.ToDictionary(item => item.FileName, StringComparer.OrdinalIgnoreCase),
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

    private static void SerializeIfChanged<TModel>(string path, TModel model, ModelMap modelMap)
        where TModel : class
    {
        WriteBytesIfChanged(path, SerializeToBytes(model, modelMap));
    }

    private static byte[] SerializeToBytes<TModel>(TModel model, ModelMap modelMap)
        where TModel : class
    {
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
            modelMap.Serializer.Serialize(writer, model);
        }

        return stream.ToArray();
    }

    private static void WriteBytesIfChanged(string path, byte[] contents)
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

    private static void CopyFileIfChanged(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath) && FilesEqual(sourcePath, destinationPath))
        {
            return;
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static bool FilesEqual(string leftPath, string rightPath)
    {
        var leftInfo = new FileInfo(leftPath);
        var rightInfo = new FileInfo(rightPath);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        using var left = File.OpenRead(leftPath);
        using var right = File.OpenRead(rightPath);
        return StreamsEqual(left, right);
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

    private static bool StreamsEqual(Stream left, Stream right)
    {
        Span<byte> leftBuffer = stackalloc byte[8192];
        Span<byte> rightBuffer = stackalloc byte[8192];
        while (true)
        {
            var leftRead = left.Read(leftBuffer);
            var rightRead = right.Read(rightBuffer);
            if (leftRead != rightRead)
            {
                return false;
            }

            if (leftRead == 0)
            {
                return true;
            }

            if (!leftBuffer[..leftRead].SequenceEqual(rightBuffer[..rightRead]))
            {
                return false;
            }
        }
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

    private static void MergeShardProperty<TModel>(TModel target, TModel source, ShardProperty shardProperty)
        where TModel : class
    {
        var sourceRows = shardProperty.GetList(source);
        if (sourceRows.Count == 0)
        {
            return;
        }

        var targetRows = shardProperty.GetList(target);
        foreach (var row in sourceRows)
        {
            targetRows.Add(row);
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

    private sealed record ModelMap(
        Type ModelType,
        string RootElementName,
        IReadOnlyList<ShardProperty> ShardProperties,
        IReadOnlyDictionary<string, ShardProperty> ShardPropertiesByFileName,
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
