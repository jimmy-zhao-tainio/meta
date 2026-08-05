using System.Security.Cryptography;
using System.Text;
using Meta.Core.Domain;
using MetaWorkspaceGenerated = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace Meta.Core.Serialization;

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
            "workspace.xml");
        var configurationXml = XmlWorkspaceWriter.Serialize(
            MetaWorkspaceGenerated.BuildDocument(normalizedConfiguration),
            indented: false);
        var modelXml = XmlWorkspaceWriter.Serialize(
            ModelXmlCodec.BuildDocument(workspace.Model),
            indented: false);
        var instanceXml = XmlWorkspaceWriter.BuildCanonicalShardPayload(
            workspace,
            layout);
        var payload = configurationXml + "\n---\n" + modelXml + "\n---\n" + instanceXml;
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }
}
