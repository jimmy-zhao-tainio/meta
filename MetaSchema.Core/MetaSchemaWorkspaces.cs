using Meta.Core.Domain;

namespace MetaSchema.Core;

public static class MetaSchemaWorkspaces
{
    public static Workspace CreateEmptyMetaSchemaWorkspace(string workspaceRootPath)
    {
        return MetaSchemaWorkspaceFactory.CreateEmptyWorkspace(
            workspaceRootPath,
            MetaSchemaModels.CreateMetaSchemaModel());
    }
}
