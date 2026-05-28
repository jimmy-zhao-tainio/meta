using System.Xml.Linq;
using Meta.Core.Domain;
using Meta.Core.Serialization;

namespace MetaWeave.Core;

public static class MetaWeaveModels
{
    public const string MetaWeaveModelName = "MetaWeave";
    private const string MetaWeaveModelResourceName = "MetaWeave.Core.Models.MetaWeave.model.xml";

    public static GenericModel CreateMetaWeaveModel()
    {
        return LoadModel(MetaWeaveModelResourceName, MetaWeaveModelName);
    }

    private static GenericModel LoadModel(string resourceName, string expectedModelName)
    {
        var assembly = typeof(MetaWeaveModels).Assembly;
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
