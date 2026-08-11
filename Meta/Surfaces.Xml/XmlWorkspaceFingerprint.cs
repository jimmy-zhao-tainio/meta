using System.Security.Cryptography;
using System.Text;
using Meta.Operations.Domain;
using Meta.Surfaces;
using MetaWorkspaceGenerated = Meta.Surfaces.Configuration.MetaWorkspace;

namespace Meta.Surfaces.Xml;

internal static class XmlWorkspaceFingerprint
{
    public static string Calculate(
        InMemoryWorkspace workspace,
        MetaWorkspaceGenerated configuration,
        XmlWorkspaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(layout);

        var normalizedConfiguration = MetaWorkspaceGenerated.Normalize(
            configuration,
            WorkspaceMetaFile.FileName);
        var configurationText = WorkspaceMetaFile.Serialize(
            normalizedConfiguration,
            "xml",
            ".");
        var modelXml = XmlWorkspaceWriter.Serialize(
            ModelXmlCodec.BuildDocument(workspace.Model),
            indented: false);
        var instanceXml = XmlWorkspaceWriter.BuildCanonicalShardPayload(
            workspace,
            layout);
        var payload = configurationText + "\n---\n" + modelXml + "\n---\n" + instanceXml;
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }
}
