using System.Xml.Linq;
using Meta.Core.Domain;
using Meta.Core.Serialization;

namespace MetaFabric.Core;

public static class MetaFabricModels
{
    public const string MetaFabricModelName = "MetaFabric";
    private const string MetaFabricModelResourceName = "MetaFabric.Core.Models.MetaFabric.model.xml";

    public static GenericModel CreateMetaFabricModel()
    {
        return LoadModel(MetaFabricModelResourceName, MetaFabricModelName);
    }

    private static GenericModel LoadModel(string resourceName, string expectedModelName)
    {
        var assembly = typeof(MetaFabricModels).Assembly;
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
