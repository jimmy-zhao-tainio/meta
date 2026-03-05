using System.Xml.Linq;
using Meta.Core.Domain;
using Meta.Core.Serialization;

namespace MetaDataVault.Core;

public static class MetaDataVaultModels
{
    public const string MetaRawDataVaultModelName = "MetaRawDataVault";
    private const string MetaDataVaultModelResourceName = "MetaDataVault.Core.Models.MetaDataVault.model.xml";

    public static GenericModel CreateMetaRawDataVaultModel()
    {
        return LoadModel(MetaDataVaultModelResourceName, MetaRawDataVaultModelName);
    }

    private static GenericModel LoadModel(string resourceName, string expectedModelName)
    {
        var assembly = typeof(MetaDataVaultModels).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException(
                               $"Could not load embedded sanctioned model resource '{resourceName}'.");
        var document = XDocument.Load(stream, LoadOptions.None);
        var model = ModelXmlCodec.Load(document);
        if (!string.Equals(model.Name, expectedModelName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Sanctioned model name '{model.Name}' from resource '{resourceName}' does not match expected '{expectedModelName}'.");
        }

        return model;
    }
}
