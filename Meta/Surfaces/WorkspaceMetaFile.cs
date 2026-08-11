using System.Globalization;
using System.Text;
using Meta.Surfaces.Configuration;

namespace Meta.Surfaces;

public sealed record WorkspaceMetaDocument(
    string Representation,
    string Location,
    MetaWorkspace Configuration,
    IReadOnlyList<string> Sources);

public static class WorkspaceMetaFile
{
    public const string FileName = "workspace.meta";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static WorkspaceMetaDocument Read(string workspaceRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootPath);
        var rootPath = Path.GetFullPath(workspaceRootPath);
        var path = Path.Combine(rootPath, FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Workspace '{rootPath}' does not contain {FileName}.",
                path);
        }

        var descriptor = Parse(File.ReadAllLines(path, Utf8NoBom), path);
        var representation = NormalizeRepresentation(descriptor.Required("representation"), path);
        if (descriptor.Sources.Count > 0 && representation != "csharp")
        {
            throw new InvalidDataException(
                $"Workspace metadata '{path}' permits source directives only for the C# representation.");
        }

        var sources = representation == "csharp"
            ? NormalizeCSharpSources(rootPath, descriptor.Sources, path)
            : Array.Empty<string>();
        if (string.Equals(representation, "sql", StringComparison.Ordinal))
        {
            descriptor.Require("location", path);
        }

        var configuration = descriptor.HasConfiguration
            ? BuildConfiguration(descriptor, path)
            : MetaWorkspace.CreateDefault();
        return new WorkspaceMetaDocument(
            representation,
            descriptor.Get("location", "."),
            configuration,
            sources);
    }

    public static void WriteXml(
        string workspaceRootPath,
        MetaWorkspace? configuration = null)
    {
        Write(
            workspaceRootPath,
            "xml",
            ".",
            configuration ?? MetaWorkspace.CreateDefault(),
            includeConfiguration: true);
    }

    public static void WriteCSharp(
        string workspaceRootPath,
        IReadOnlyCollection<string> sources) =>
        WriteCSharpDescriptor(workspaceRootPath, sources);

    private static void WriteCSharpDescriptor(
        string workspaceRootPath,
        IReadOnlyCollection<string> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new ArgumentException(
                "A C# workspace descriptor requires at least one owned source.",
                nameof(sources));
        }

        Write(
            workspaceRootPath,
            "csharp",
            ".",
            MetaWorkspace.CreateDefault(),
            includeConfiguration: false,
            sources: sources);
    }

    public static void WriteSql(
        string workspaceRootPath,
        string connectionEnvironmentVariable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionEnvironmentVariable);
        Write(
            workspaceRootPath,
            "sql",
            connectionEnvironmentVariable.Trim(),
            MetaWorkspace.CreateDefault(),
            includeConfiguration: false);
    }

    public static string Serialize(
        MetaWorkspace configuration,
        string representation,
        string location,
        bool includeConfiguration = true,
        IReadOnlyCollection<string>? sources = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(representation);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);

        var normalizedRepresentation = NormalizeRepresentation(representation, FileName);
        if (sources != null && normalizedRepresentation != "csharp" && sources.Count > 0)
        {
            throw new InvalidDataException(
                $"Workspace metadata '{FileName}' permits source directives only for the C# representation.");
        }
        var normalizedSources = normalizedRepresentation == "csharp"
            ? NormalizeCSharpSourceText(sources ?? Array.Empty<string>(), FileName)
            : Array.Empty<string>();
        var normalizedConfiguration = MetaWorkspace.Normalize(configuration, FileName);
        var builder = new StringBuilder();
        AppendDirective(builder, "representation", normalizedRepresentation);
        if (!string.Equals(location.Trim(), ".", StringComparison.Ordinal))
        {
            AppendDirective(builder, "location", location.Trim());
        }

        foreach (var source in normalizedSources)
        {
            AppendDirective(builder, "source", source);
        }

        if (!includeConfiguration)
        {
            return NormalizeLineEndings(builder.ToString());
        }

        var workspace = normalizedConfiguration.Workspace.Single();
        var layout = normalizedConfiguration.WorkspaceLayout
            .Single(item => string.Equals(item.Id, workspace.WorkspaceLayoutId, StringComparison.OrdinalIgnoreCase));
        var encoding = normalizedConfiguration.Encoding
            .Single(item => string.Equals(item.Id, workspace.EncodingId, StringComparison.OrdinalIgnoreCase));
        var newlines = normalizedConfiguration.Newlines
            .Single(item => string.Equals(item.Id, workspace.NewlinesId, StringComparison.OrdinalIgnoreCase));
        var defaults = MetaWorkspace.Normalize(MetaWorkspace.CreateDefault(), FileName);
        var defaultWorkspace = defaults.Workspace.Single();
        var defaultLayout = defaults.WorkspaceLayout.Single();
        var defaultEncoding = defaults.Encoding.Single();
        var defaultNewlines = defaults.Newlines.Single();

        AppendIfDifferent(builder, "name", workspace.Name, defaultWorkspace.Name);
        AppendIfDifferent(builder, "format-version", workspace.FormatVersion, defaultWorkspace.FormatVersion);
        AppendIfDifferent(builder, "model-file", layout.ModelFilePath, defaultLayout.ModelFilePath);
        AppendIfDifferent(builder, "instance-directory", layout.InstanceDirPath, defaultLayout.InstanceDirPath);
        AppendIfDifferent(builder, "encoding", encoding.Name, defaultEncoding.Name);
        AppendIfDifferent(builder, "newlines", newlines.Name, defaultNewlines.Name);
        AppendIfDifferent(
            builder,
            "order.entities",
            FindOrderName(normalizedConfiguration, workspace.EntitiesOrderId),
            FindOrderName(defaults, defaultWorkspace.EntitiesOrderId));
        AppendIfDifferent(
            builder,
            "order.properties",
            FindOrderName(normalizedConfiguration, workspace.PropertiesOrderId),
            FindOrderName(defaults, defaultWorkspace.PropertiesOrderId));
        AppendIfDifferent(
            builder,
            "order.relationships",
            FindOrderName(normalizedConfiguration, workspace.RelationshipsOrderId),
            FindOrderName(defaults, defaultWorkspace.RelationshipsOrderId));
        AppendIfDifferent(
            builder,
            "order.rows",
            FindOrderName(normalizedConfiguration, workspace.RowsOrderId),
            FindOrderName(defaults, defaultWorkspace.RowsOrderId));
        AppendIfDifferent(
            builder,
            "order.attributes",
            FindOrderName(normalizedConfiguration, workspace.AttributesOrderId),
            FindOrderName(defaults, defaultWorkspace.AttributesOrderId));

        foreach (var storage in normalizedConfiguration.EntityStorage
                     .OrderBy(item => item.EntityName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.EntityName, StringComparer.Ordinal))
        {
            builder.AppendLine();
            AppendDirective(builder, "storage", storage.EntityName);
            AppendDirective(builder, "  kind", storage.StorageKind);
            AppendOptionalDirective(builder, "  directory", storage.DirectoryPath);
            AppendOptionalDirective(builder, "  file", storage.FilePath);
            AppendOptionalDirective(builder, "  pattern", storage.Pattern);
            AppendDirective(builder, "end-storage", string.Empty, omitValue: true);
        }

        return NormalizeLineEndings(builder.ToString());
    }

    private static void Write(
        string workspaceRootPath,
        string representation,
        string location,
        MetaWorkspace configuration,
        bool includeConfiguration,
        IReadOnlyCollection<string>? sources = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootPath);
        var rootPath = Path.GetFullPath(workspaceRootPath);
        Directory.CreateDirectory(rootPath);
        if (sources != null && representation == "csharp")
        {
            _ = NormalizeCSharpSources(rootPath, sources, Path.Combine(rootPath, FileName));
        }
        File.WriteAllText(
            Path.Combine(rootPath, FileName),
            Serialize(configuration, representation, location, includeConfiguration, sources),
            Utf8NoBom);
    }

    private static Descriptor Parse(IReadOnlyList<string> lines, string path)
    {
        var descriptor = new Descriptor();
        StorageValues? storage = null;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (index == 0)
            {
                line = line.TrimStart('\uFEFF');
            }

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var directive = ParseDirective(line, path, index);
            if (storage != null)
            {
                if (string.Equals(directive.Key, "end-storage", StringComparison.Ordinal))
                {
                    if (directive.Value.Length != 0)
                    {
                        throw Invalid(path, index, "end-storage does not accept a value");
                    }

                    descriptor.Storages.Add(storage);
                    storage = null;
                    continue;
                }

                switch (directive.Key)
                {
                    case "kind":
                        storage.Kind = RequireValue(directive, path, index);
                        break;
                    case "directory":
                        storage.Directory = directive.Value;
                        break;
                    case "file":
                        storage.File = directive.Value;
                        break;
                    case "pattern":
                        storage.Pattern = directive.Value;
                        break;
                    default:
                        throw Invalid(path, index, $"unknown storage field '{directive.Key}'");
                }

                continue;
            }

            if (string.Equals(directive.Key, "storage", StringComparison.Ordinal))
            {
                storage = new StorageValues
                {
                    EntityName = RequireValue(directive, path, index),
                };
                descriptor.HasConfiguration = true;
                continue;
            }

            if (string.Equals(directive.Key, "source", StringComparison.Ordinal))
            {
                descriptor.Sources.Add(RequireValue(directive, path, index));
                continue;
            }

            if (string.Equals(directive.Key, "end-storage", StringComparison.Ordinal))
            {
                throw Invalid(path, index, "end-storage has no matching storage directive");
            }

            if (!descriptor.Values.TryAdd(directive.Key, directive.Value))
            {
                throw Invalid(path, index, $"duplicate directive '{directive.Key}'");
            }

            if (!string.Equals(directive.Key, "representation", StringComparison.Ordinal) &&
                !string.Equals(directive.Key, "location", StringComparison.Ordinal))
            {
                descriptor.HasConfiguration = true;
            }
        }

        if (storage != null)
        {
            throw new InvalidDataException($"Workspace metadata '{path}' has an unterminated storage block.");
        }

        descriptor.Require("representation", path);
        return descriptor;
    }

    private static MetaWorkspace BuildConfiguration(Descriptor descriptor, string path)
    {
        var configuration = MetaWorkspace.CreateDefault();
        var workspace = configuration.Workspace.Single();
        var layout = configuration.WorkspaceLayout.Single();
        var encoding = configuration.Encoding.Single();
        var newlines = configuration.Newlines.Single();

        workspace.Name = descriptor.Get("name", workspace.Name);
        workspace.FormatVersion = descriptor.Get("format-version", workspace.FormatVersion);
        layout.ModelFilePath = descriptor.Get("model-file", layout.ModelFilePath);
        layout.InstanceDirPath = descriptor.Get("instance-directory", layout.InstanceDirPath);
        encoding.Name = descriptor.Get("encoding", encoding.Name);
        newlines.Name = descriptor.Get("newlines", newlines.Name);

        var orders = new[]
        {
            descriptor.Get("order.entities", "name-ordinal"),
            descriptor.Get("order.properties", "name-ordinal"),
            descriptor.Get("order.relationships", "name-ordinal"),
            descriptor.Get("order.rows", "id-ordinal"),
            descriptor.Get("order.attributes", "id-first-then-name-ordinal"),
        };
        configuration.CanonicalOrder = orders
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((name, index) => new CanonicalOrder
            {
                Id = (index + 1).ToString(CultureInfo.InvariantCulture),
                Name = name,
            })
            .ToList();
        workspace.EntitiesOrderId = FindOrderId(configuration, orders[0]);
        workspace.PropertiesOrderId = FindOrderId(configuration, orders[1]);
        workspace.RelationshipsOrderId = FindOrderId(configuration, orders[2]);
        workspace.RowsOrderId = FindOrderId(configuration, orders[3]);
        workspace.AttributesOrderId = FindOrderId(configuration, orders[4]);
        configuration.EntityStorage = descriptor.Storages
            .Select((item, index) => new EntityStorage
            {
                Id = (index + 1).ToString(CultureInfo.InvariantCulture),
                WorkspaceId = workspace.Id,
                EntityName = item.EntityName,
                StorageKind = item.Kind,
                DirectoryPath = item.Directory,
                FilePath = item.File,
                Pattern = item.Pattern,
            })
            .ToList();

        return MetaWorkspace.Normalize(configuration, path);
    }

    private static string FindOrderName(MetaWorkspace configuration, string orderId) =>
        configuration.CanonicalOrder.Single(item =>
            string.Equals(item.Id, orderId, StringComparison.OrdinalIgnoreCase)).Name;

    private static string FindOrderId(MetaWorkspace configuration, string orderName) =>
        configuration.CanonicalOrder.Single(item =>
            string.Equals(item.Name, orderName, StringComparison.OrdinalIgnoreCase)).Id;

    private static (string Key, string Value) ParseDirective(string line, string path, int lineIndex)
    {
        var separator = line.IndexOfAny([' ', '\t']);
        if (separator < 0)
        {
            return (line.ToLowerInvariant(), string.Empty);
        }

        var key = line[..separator].Trim().ToLowerInvariant();
        var value = ParseValue(line[(separator + 1)..].Trim(), path, lineIndex);
        return (key, value);
    }

    private static string ParseValue(string text, string path, int lineIndex)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        if (text[0] != '"')
        {
            if (text.Any(char.IsWhiteSpace))
            {
                throw Invalid(path, lineIndex, "values containing whitespace must be quoted");
            }

            return text;
        }

        var builder = new StringBuilder();
        for (var index = 1; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                if (!string.IsNullOrWhiteSpace(text[(index + 1)..]))
                {
                    throw Invalid(path, lineIndex, "quoted values cannot have trailing text");
                }

                return builder.ToString();
            }

            if (character == '\\')
            {
                if (++index >= text.Length)
                {
                    throw Invalid(path, lineIndex, "quoted value has an unfinished escape");
                }

                builder.Append(text[index]);
                continue;
            }

            builder.Append(character);
        }

        throw Invalid(path, lineIndex, "quoted value is not terminated");
    }

    private static void AppendDirective(StringBuilder builder, string key, string value, bool omitValue = false)
    {
        builder.Append(key);
        if (!omitValue)
        {
            builder.Append(' ').Append(FormatValue(value));
        }

        builder.AppendLine();
    }

    private static void AppendIfDifferent(StringBuilder builder, string key, string value, string defaultValue)
    {
        if (!string.Equals(value, defaultValue, StringComparison.Ordinal))
        {
            AppendDirective(builder, key, value);
        }
    }

    private static void AppendOptionalDirective(StringBuilder builder, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            AppendDirective(builder, key, value);
        }
    }

    private static string FormatValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 0 && value.All(character =>
                !char.IsWhiteSpace(character) && character != '#' && character != '"' && character != '\\'))
        {
            return value;
        }

        return '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
    }

    private static string NormalizeLineEndings(string value) =>
        value.ReplaceLineEndings("\n");

    private static string RequireValue((string Key, string Value) directive, string path, int lineIndex)
    {
        if (string.IsNullOrWhiteSpace(directive.Value))
        {
            throw Invalid(path, lineIndex, $"directive '{directive.Key}' requires a value");
        }

        return directive.Value;
    }

    private static InvalidDataException Invalid(string path, int lineIndex, string detail) =>
        new($"Workspace metadata '{path}' line {lineIndex + 1}: {detail}.");

    private static string NormalizeRepresentation(string value, string path)
    {
        var representation = value.Trim().ToLowerInvariant();
        return representation is "xml" or "csharp" or "sql"
            ? representation
            : throw new InvalidDataException(
                $"Workspace metadata '{path}' has unsupported representation '{value}'.");
    }

    private sealed class Descriptor
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<StorageValues> Storages { get; } = new();
        public List<string> Sources { get; } = new();
        public bool HasConfiguration { get; set; }

        public string Get(string key, string fallback) =>
            Values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;

        public string Required(string key) => Values[key];

        public void Require(string key, string path)
        {
            if (!Values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"Workspace metadata '{path}' is missing '{key}'.");
            }
        }
    }

    internal static IReadOnlyList<string> NormalizeCSharpSources(
        string workspaceRootPath,
        IEnumerable<string> sources,
        string descriptorPath)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var normalized = NormalizeCSharpSourceText(sources, descriptorPath);
        foreach (var source in normalized)
        {
            var current = Path.GetFullPath(workspaceRootPath);
            foreach (var segment in source.Split('/'))
            {
                current = Path.Combine(current, segment);
                try
                {
                    var attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidDataException(
                            $"C# workspace source path '{source}' traverses a symbolic link or reparse point.");
                    }
                }
                catch (FileNotFoundException)
                {
                    // A not-yet-created path is valid; publication creates it later.
                }
                catch (DirectoryNotFoundException)
                {
                    // A not-yet-created parent is valid; publication creates it later.
                }
            }
        }

        return normalized;
    }

    internal static string ResolveCSharpSourcePath(
        string workspaceRootPath,
        string source)
    {
        var normalized = NormalizeCSharpSourceText([source], FileName).Single();
        var root = Path.GetFullPath(workspaceRootPath);
        var path = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.StartsWith(rootWithSeparator, comparison))
        {
            throw new InvalidDataException(
                $"C# workspace source path '{source}' escapes its workspace.");
        }

        return path;
    }

    private static IReadOnlyList<string> NormalizeCSharpSourceText(
        IEnumerable<string> sources,
        string descriptorPath)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new InvalidDataException(
                    $"Workspace metadata '{descriptorPath}' contains an empty C# source path.");
            }

            var value = source.Trim();
            if (value.Contains('\\') || Path.IsPathRooted(value))
            {
                throw new InvalidDataException(
                    $"Workspace metadata '{descriptorPath}' requires relative C# source paths using '/': '{source}'.");
            }

            var segments = value.Split('/');
            if (segments.Any(segment =>
                    string.IsNullOrEmpty(segment) ||
                    segment is "." or ".." ||
                    segment.Contains(':')))
            {
                throw new InvalidDataException(
                    $"Workspace metadata '{descriptorPath}' contains an unsafe C# source path '{source}'.");
            }

            if (!value.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(Path.GetFileNameWithoutExtension(value)))
            {
                throw new InvalidDataException(
                    $"Workspace metadata '{descriptorPath}' requires nonempty '.cs' source paths: '{source}'.");
            }

            if (!seen.Add(value))
            {
                throw new InvalidDataException(
                    $"Workspace metadata '{descriptorPath}' contains case-equivalent duplicate C# source paths: '{source}'.");
            }

            normalized.Add(value);
        }

        normalized.Sort(StringComparer.Ordinal);
        return normalized;
    }

    private sealed class StorageValues
    {
        public string EntityName { get; set; } = string.Empty;
        public string Kind { get; set; } = "Sharded";
        public string Directory { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public string Pattern { get; set; } = string.Empty;
    }
}
