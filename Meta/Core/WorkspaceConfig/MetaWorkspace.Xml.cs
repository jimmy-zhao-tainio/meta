using System.Xml.Linq;

namespace Meta.Core.WorkspaceConfig.Generated;

public sealed partial class MetaWorkspace
{
    public static MetaWorkspace Load(XDocument document, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Root ?? throw new InvalidDataException("Workspace XML has no root element.");
        if (!string.Equals(root.Name.LocalName, MetaWorkspaceModels.ModelName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Workspace XML root must be '{MetaWorkspaceModels.ModelName}', found '{root.Name.LocalName}'.");
        }

        var raw = new MetaWorkspace
        {
            Workspace = ParseWorkspaceRows(root),
            WorkspaceLayout = ParseWorkspaceLayoutRows(root),
            Encoding = ParseEncodingRows(root),
            Newlines = ParseNewlinesRows(root),
            CanonicalOrder = ParseCanonicalOrderRows(root),
            EntityStorage = ParseEntityStorageRows(root),
        };
        return Normalize(raw, sourcePath);
    }
}
