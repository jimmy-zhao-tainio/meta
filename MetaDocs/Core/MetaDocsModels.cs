using Meta.Integration;
using Meta.Operations.Domain;

namespace MetaDocs.Core;

public static class MetaDocsModels
{
    public const string MetaDocsModelName = "MetaDocs";

    public static GenericModel CreateMetaDocsModel()
    {
        var model = TypedWorkspaceModelMapper
            .ToInMemoryWorkspace(MetaDocsModel.CreateEmpty())
            .Model;
        if (!string.Equals(model.Name, MetaDocsModelName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Sanctioned model name '{model.Name}' does not match expected '{MetaDocsModelName}'.");
        }

        return model;
    }
}
