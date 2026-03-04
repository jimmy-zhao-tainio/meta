using System.Xml.Linq;
using Meta.Core.Domain;
using Meta.Core.Serialization;

namespace MetaTypeConversion.Core;

public static class MetaTypeConversionModels
{
    public const string MetaTypeConversionModelName = "MetaTypeConversion";
    private const string MetaTypeConversionModelResourceName = "MetaTypeConversion.Core.Models.MetaTypeConversion.model.xml";

    public static GenericModel CreateMetaTypeConversionModel()
    {
        return LoadModel(MetaTypeConversionModelResourceName, MetaTypeConversionModelName);
    }

    private static GenericModel LoadModel(string resourceName, string expectedModelName)
    {
        var assembly = typeof(MetaTypeConversionModels).Assembly;
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
