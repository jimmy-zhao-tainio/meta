using System.Xml.Linq;
using Meta.Core.Domain;
using Meta.Core.Serialization;

namespace Meta.Core.WorkspaceConfig;

public static class MetaWorkspaceModels
{
    public const string ModelName = "MetaWorkspace";
    public const string DefaultWorkspaceName = "Workspace";
    private const string ModelResourceName = "Meta.Core.WorkspaceConfig.Models.MetaWorkspace.model.xml";

    public static GenericModel CreateModel()
    {
        var assembly = typeof(MetaWorkspaceModels).Assembly;
        using var stream = assembly.GetManifestResourceStream(ModelResourceName)
                           ?? throw new InvalidOperationException(
                               $"Could not load embedded MetaWorkspace model resource '{ModelResourceName}'.");
        var document = XDocument.Load(stream, LoadOptions.None);
        var model = ModelXmlCodec.Load(document);
        if (!string.Equals(model.Name, ModelName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Embedded MetaWorkspace model name '{model.Name}' does not match expected '{ModelName}'.");
        }

        return model;
    }
}

