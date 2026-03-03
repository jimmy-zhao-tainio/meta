using System.Xml.Linq;
using Meta.Core.Domain;
using Meta.Core.Serialization;

namespace MetaSchema.Core;

public static class MetaSchemaModels
{
    public const string MetaDataTypeModelName = "MetaDataType";
    public const string MetaSchemaModelName = "MetaSchema";
    public const string TypeConversionCatalogModelName = "TypeConversionCatalog";
    private const string MetaDataTypeModelResourceName = "MetaSchema.Core.Models.MetaDataType.model.xml";
    private const string MetaSchemaModelResourceName = "MetaSchema.Core.Models.MetaSchema.model.xml";
    private const string TypeConversionCatalogModelResourceName = "MetaSchema.Core.Models.TypeConversionCatalog.model.xml";

    public static GenericModel CreateMetaDataTypeModel()
    {
        return LoadModel(MetaDataTypeModelResourceName, MetaDataTypeModelName);
    }

    public static GenericModel CreateMetaSchemaModel()
    {
        return LoadModel(MetaSchemaModelResourceName, MetaSchemaModelName);
    }

    public static GenericModel CreateTypeConversionCatalogModel()
    {
        return LoadModel(TypeConversionCatalogModelResourceName, TypeConversionCatalogModelName);
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

