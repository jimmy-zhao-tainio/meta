using System.Text;
using System.Xml.Linq;
using Meta.Core.WorkspaceConfig.Generated;

namespace Meta.Surfaces;

public sealed record WorkspaceMetaDocument(
    string Representation,
    string Location,
    MetaWorkspace Configuration);

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

        var document = XDocument.Load(path, LoadOptions.None);
        var root = document.Root ?? throw new InvalidDataException(
            $"Workspace metadata '{path}' has no root element.");
        if (!string.Equals(root.Name.LocalName, "MetaWorkspace", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Workspace metadata '{path}' must have a 'MetaWorkspace' root element.");
        }

        var representation = root.Attribute("representation")?.Value?.Trim() ?? "xml";
        var location = root.Attribute("location")?.Value?.Trim() ?? ".";
        var configuration = root.Element("WorkspaceList") is null
            ? MetaWorkspace.CreateDefault()
            : MetaWorkspace.Load(document, path);
        return new WorkspaceMetaDocument(
            NormalizeRepresentation(representation, path),
            location,
            configuration);
    }

    public static void WriteXml(
        string workspaceRootPath,
        MetaWorkspace? configuration = null)
    {
        Write(
            workspaceRootPath,
            "xml",
            ".",
            configuration ?? MetaWorkspace.CreateDefault());
    }

    public static void WriteCSharp(string workspaceRootPath) =>
        Write(workspaceRootPath, "csharp", ".", MetaWorkspace.CreateDefault(), includeConfiguration: false);

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

    internal static XDocument BuildXmlDocument(
        MetaWorkspace configuration,
        string representation,
        string location)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(representation);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);

        var document = MetaWorkspace.BuildDocument(
            MetaWorkspace.Normalize(configuration, "workspace.meta"));
        document.Root!.SetAttributeValue("representation", NormalizeRepresentation(representation, "workspace.meta"));
        document.Root.SetAttributeValue("location", location.Trim());
        return document;
    }

    private static void Write(
        string workspaceRootPath,
        string representation,
        string location,
        MetaWorkspace configuration,
        bool includeConfiguration = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootPath);
        var rootPath = Path.GetFullPath(workspaceRootPath);
        Directory.CreateDirectory(rootPath);

        var document = includeConfiguration
            ? BuildXmlDocument(configuration, representation, location)
            : new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(
                    "MetaWorkspace",
                    new XAttribute("representation", NormalizeRepresentation(representation, FileName)),
                    new XAttribute("location", location.Trim())));
        File.WriteAllText(
            Path.Combine(rootPath, FileName),
            Meta.Core.Serialization.CanonicalXmlSerializer.SerializeToString(document, indented: true),
            Utf8NoBom);
    }

    private static string NormalizeRepresentation(string value, string path)
    {
        var representation = value.Trim().ToLowerInvariant();
        return representation is "xml" or "csharp" or "sql"
            ? representation
            : throw new InvalidDataException(
                $"Workspace metadata '{path}' has unsupported representation '{value}'.");
    }
}
