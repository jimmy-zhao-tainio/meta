using System.Xml.Linq;
using Meta.Core.Domain;
using Meta.Core.Serialization;

namespace MetaDocs.Core;

public static class MetaDocsModels
{
    public const string MetaDocsModelName = "MetaDocs";
    private const string MetaDocsModelResourceName = "MetaDocs.Core.Models.MetaDocs.model.xml";

    public static GenericModel CreateMetaDocsModel()
    {
        var assembly = typeof(MetaDocsModels).Assembly;
        using var stream = assembly.GetManifestResourceStream(MetaDocsModelResourceName)
                           ?? throw new InvalidOperationException(
                               $"Could not load embedded sanctioned model resource '{MetaDocsModelResourceName}'.");
        var document = XDocument.Load(stream, LoadOptions.None);
        var model = ModelXmlCodec.Load(document);
        if (!string.Equals(model.Name, MetaDocsModelName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Sanctioned model name '{model.Name}' from resource '{MetaDocsModelResourceName}' does not match expected '{MetaDocsModelName}'.");
        }

        return model;
    }
}
