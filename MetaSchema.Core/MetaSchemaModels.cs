using System.Xml.Linq;
using Meta.Core.Domain;
using Meta.Core.Serialization;

namespace MetaSchema.Core;

public static class MetaSchemaModels
{
    public const string MetaSchemaModelName = "MetaSchema";
    private const string MetaSchemaModelResourceName = "MetaSchema.Core.Models.MetaSchema.model.xml";

    public static GenericModel CreateMetaSchemaModel()
    {
        return LoadModel(MetaSchemaModelResourceName, MetaSchemaModelName);
    }

    private static GenericModel LoadModel(string resourceName, string expectedModelName)
    {
        var assembly = typeof(MetaSchemaModels).Assembly;
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

