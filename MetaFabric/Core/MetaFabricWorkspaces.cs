using Meta.Core.Domain;

namespace MetaFabric.Core;

public static class MetaFabricWorkspaces
{
    public static Workspace CreateEmptyMetaFabricWorkspace(string workspaceRootPath)
    {
        return MetaFabricWorkspaceFactory.CreateEmptyWorkspace(
            workspaceRootPath,
            MetaFabricModels.CreateMetaFabricModel());
    }
}
